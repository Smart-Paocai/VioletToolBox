namespace SuperFix.Core;

public static class EmptySuperImageBuilder
{
    public static EmptySuperVerification Generate(SuperPackageAnalysis analysis, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("请选择输出文件", nameof(outputPath));

        string fullOutputPath = Path.GetFullPath(outputPath);
        string outputDirectory = Path.GetDirectoryName(fullOutputPath)
            ?? throw new InvalidOperationException("无法确定输出目录");
        if (!Directory.Exists(outputDirectory))
            throw new DirectoryNotFoundException($"输出目录不存在: {outputDirectory}");
        EnsureOutputDoesNotOverwriteSource(analysis, fullOutputPath);

        LpMetadataTemplate template = analysis.Template;
        if (template.BlockDevices.Count != 1)
            throw new NotSupportedException("空 Super 生成仅支持一个 block device");
        LpGroupInfo defaultGroup = template.Groups.SingleOrDefault(
            group => group.Name.Equals("default", StringComparison.Ordinal))
            ?? throw new InvalidDataException("原厂 metadata 缺少 default group");
        LpBlockDeviceInfo blockDevice = template.BlockDevices[0];

        byte[] geometry = BuildGeometry(template.Geometry);
        byte[] metadata = BuildEmptyMetadata(template.Header, defaultGroup, blockDevice);
        if (metadata.Length > template.Geometry.MetadataMaxSize)
            throw new InvalidDataException("生成的空 metadata 超过 metadata_max_size");

        byte[] paddedMetadata = new byte[template.Geometry.MetadataMaxSize];
        metadata.CopyTo(paddedMetadata, 0);
        long expectedLength = LpConstants.GetTotalMetadataSize(
            template.Geometry.MetadataMaxSize,
            template.Geometry.MetadataSlotCount);
        string temporaryPath = Path.Combine(
            outputDirectory,
            $".{Path.GetFileName(fullOutputPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var output = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       64 * 1024,
                       FileOptions.WriteThrough))
            {
                output.Write(new byte[LpConstants.ReservedBytes]);
                output.Write(geometry);
                output.Write(geometry);
                for (uint slot = 0; slot < template.Geometry.MetadataSlotCount; slot++)
                    output.Write(paddedMetadata);
                for (uint slot = 0; slot < template.Geometry.MetadataSlotCount; slot++)
                    output.Write(paddedMetadata);
                output.Flush(true);
                if (output.Length != expectedLength)
                {
                    throw new IOException(
                        $"空 Super 写入长度异常: {output.Length} != {expectedLength}");
                }
            }

            EmptySuperVerification verification = EmptySuperImageVerifier.VerifyFile(temporaryPath);
            if (verification.SuperSize != blockDevice.Size ||
                verification.MetadataMaxSize != template.Geometry.MetadataMaxSize ||
                verification.MetadataSlotCount != template.Geometry.MetadataSlotCount ||
                verification.MajorVersion != template.Header.MajorVersion ||
                verification.MinorVersion != template.Header.MinorVersion ||
                verification.HeaderFlags != template.Header.Flags)
            {
                throw new InvalidDataException("生成后的空 Super 参数与原厂模板不一致");
            }

            File.Move(temporaryPath, fullOutputPath, true);
            return verification with { Sha256 = EmptySuperImageVerifier.ComputeSha256(fullOutputPath) };
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void EnsureOutputDoesNotOverwriteSource(
        SuperPackageAnalysis analysis,
        string outputPath)
    {
        var protectedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(analysis.Definition.DefinitionPath),
            Path.GetFullPath(analysis.ResolvedSuperMetaPath)
        };
        foreach (ScatterSuperEntry entry in analysis.ScatterEntries)
            protectedPaths.Add(Path.GetFullPath(entry.SourcePath));
        foreach (SuperDefinitionPartition partition in analysis.Definition.Partitions)
        {
            if (string.IsNullOrWhiteSpace(partition.Path))
                continue;
            string relative = partition.Path.Replace('/', Path.DirectorySeparatorChar);
            if (!Path.IsPathRooted(relative))
                relative = Path.Combine(analysis.Definition.PackageRoot, relative);
            protectedPaths.Add(Path.GetFullPath(relative));
        }

        if (protectedPaths.Contains(outputPath))
            throw new InvalidOperationException("输出路径指向固件源文件，已阻止覆盖；请另存为新的 empty_super.img");
    }

    internal static byte[] BuildGeometry(LpGeometry geometry)
    {
        byte[] block = new byte[LpConstants.GeometryBlockSize];
        geometry.RawStruct.CopyTo(block, 0);
        BinaryHelpers.WriteUInt32(block, 0, LpConstants.GeometryMagic);
        BinaryHelpers.WriteUInt32(block, 4, geometry.StructSize);
        block.AsSpan(8, 32).Clear();
        BinaryHelpers.WriteUInt32(block, 40, geometry.MetadataMaxSize);
        BinaryHelpers.WriteUInt32(block, 44, geometry.MetadataSlotCount);
        BinaryHelpers.WriteUInt32(block, 48, geometry.LogicalBlockSize);
        byte[] checksum = BinaryHelpers.Sha256(block.AsSpan(0, (int)geometry.StructSize));
        checksum.CopyTo(block, 8);
        return block;
    }

    private static byte[] BuildEmptyMetadata(
        LpHeaderInfo sourceHeader,
        LpGroupInfo defaultGroup,
        LpBlockDeviceInfo blockDevice)
    {
        byte[] groupTable = SerializeGroup(defaultGroup);
        byte[] blockDeviceTable = SerializeBlockDevice(blockDevice);
        byte[] tables = new byte[groupTable.Length + blockDeviceTable.Length];
        groupTable.CopyTo(tables, 0);
        blockDeviceTable.CopyTo(tables, groupTable.Length);

        byte[] header = sourceHeader.RawHeader.ToArray();
        BinaryHelpers.WriteUInt32(header, 0, LpConstants.HeaderMagic);
        BinaryHelpers.WriteUInt16(header, 4, sourceHeader.MajorVersion);
        BinaryHelpers.WriteUInt16(header, 6, sourceHeader.MinorVersion);
        BinaryHelpers.WriteUInt32(header, 8, sourceHeader.HeaderSize);
        header.AsSpan(12, 32).Clear();
        BinaryHelpers.WriteUInt32(header, 44, checked((uint)tables.Length));
        BinaryHelpers.Sha256(tables).CopyTo(header, 48);

        WriteDescriptor(header, 80, 0, 0, LpConstants.PartitionEntrySize);
        WriteDescriptor(header, 92, 0, 0, LpConstants.ExtentEntrySize);
        WriteDescriptor(header, 104, 0, 1, LpConstants.GroupEntrySize);
        WriteDescriptor(
            header,
            116,
            LpConstants.GroupEntrySize,
            1,
            LpConstants.BlockDeviceEntrySize);
        if (sourceHeader.HeaderSize >= LpConstants.HeaderV12Size)
            BinaryHelpers.WriteUInt32(header, 128, sourceHeader.Flags);

        byte[] headerChecksum = BinaryHelpers.Sha256(header);
        headerChecksum.CopyTo(header, 12);
        byte[] result = new byte[header.Length + tables.Length];
        header.CopyTo(result, 0);
        tables.CopyTo(result, header.Length);
        return result;
    }

    private static byte[] SerializeGroup(LpGroupInfo group)
    {
        byte[] data = new byte[LpConstants.GroupEntrySize];
        BinaryHelpers.WriteFixedAscii(data.AsSpan(0, LpConstants.NameLength), group.Name);
        BinaryHelpers.WriteUInt32(data, 36, group.Flags);
        BinaryHelpers.WriteUInt64(data, 40, group.MaximumSize);
        return data;
    }

    private static byte[] SerializeBlockDevice(LpBlockDeviceInfo device)
    {
        byte[] data = new byte[LpConstants.BlockDeviceEntrySize];
        BinaryHelpers.WriteUInt64(data, 0, device.FirstLogicalSector);
        BinaryHelpers.WriteUInt32(data, 8, device.Alignment);
        BinaryHelpers.WriteUInt32(data, 12, device.AlignmentOffset);
        BinaryHelpers.WriteUInt64(data, 16, device.Size);
        BinaryHelpers.WriteFixedAscii(data.AsSpan(24, LpConstants.NameLength), device.Name);
        BinaryHelpers.WriteUInt32(data, 60, device.Flags);
        return data;
    }

    private static void WriteDescriptor(
        Span<byte> header,
        int offset,
        uint tableOffset,
        uint count,
        uint entrySize)
    {
        BinaryHelpers.WriteUInt32(header, offset, tableOffset);
        BinaryHelpers.WriteUInt32(header, offset + 4, count);
        BinaryHelpers.WriteUInt32(header, offset + 8, entrySize);
    }
}
