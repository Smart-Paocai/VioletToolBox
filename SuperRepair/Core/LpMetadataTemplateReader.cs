namespace SuperFix.Core;

public static class LpMetadataTemplateReader
{
    private sealed record TableDescriptor(uint Offset, uint Count, uint EntrySize);

    public static LpMetadataTemplate Read(string sourcePath)
    {
        string fullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("未找到原厂 super_meta", fullPath);

        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < LpConstants.GeometryStructSize)
            throw new InvalidDataException("super_meta 文件过短");

        long geometryOffset;
        long headerOffset;
        uint firstMagic = ReadUInt32At(stream, 0);
        if (firstMagic == LpConstants.GeometryMagic)
        {
            geometryOffset = 0;
            headerOffset = LpConstants.GeometryBlockSize;
        }
        else if (stream.Length >= LpConstants.ReservedBytes + LpConstants.GeometryStructSize &&
                 ReadUInt32At(stream, LpConstants.ReservedBytes) == LpConstants.GeometryMagic)
        {
            geometryOffset = LpConstants.ReservedBytes;
            headerOffset = LpConstants.ReservedBytes + 2L * LpConstants.GeometryBlockSize;
        }
        else
        {
            throw new InvalidDataException("未找到 liblp geometry magic，文件不是有效 super_meta");
        }

        byte[] geometryBlock = ReadAt(stream, geometryOffset, LpConstants.GeometryBlockSize);
        LpGeometry geometry = ParseGeometry(geometryBlock);
        if (headerOffset + LpConstants.HeaderV10Size > stream.Length)
            throw new InvalidDataException("super_meta 缺少 metadata header");

        byte[] initialHeader = ReadAt(stream, headerOffset, LpConstants.HeaderV10Size);
        if (BinaryHelpers.ReadUInt32(initialHeader, 0) != LpConstants.HeaderMagic)
            throw new InvalidDataException("metadata header magic 无效");

        uint headerSize = BinaryHelpers.ReadUInt32(initialHeader, 8);
        if (headerSize is not (LpConstants.HeaderV10Size or LpConstants.HeaderV12Size))
            throw new NotSupportedException($"暂不支持 metadata header_size={headerSize}");
        uint tablesSize = BinaryHelpers.ReadUInt32(initialHeader, 44);
        ulong metadataLength = checked((ulong)headerSize + tablesSize);
        if (metadataLength > geometry.MetadataMaxSize)
            throw new InvalidDataException(
                $"metadata 超过 geometry 上限: {metadataLength} > {geometry.MetadataMaxSize}");
        if ((ulong)headerOffset + metadataLength > (ulong)stream.Length)
            throw new InvalidDataException("super_meta metadata 内容被截断");

        byte[] metadata = ReadAt(stream, headerOffset, checked((int)metadataLength));
        LpHeaderInfo header = ParseAndValidateHeader(metadata, tablesSize);
        var descriptors = new[]
        {
            ReadDescriptor(metadata, 80, LpConstants.PartitionEntrySize, "partition"),
            ReadDescriptor(metadata, 92, LpConstants.ExtentEntrySize, "extent"),
            ReadDescriptor(metadata, 104, LpConstants.GroupEntrySize, "group"),
            ReadDescriptor(metadata, 116, LpConstants.BlockDeviceEntrySize, "block device")
        };

        foreach (TableDescriptor descriptor in descriptors)
            ValidateDescriptorRange(descriptor, tablesSize);

        ReadOnlySpan<byte> tables = metadata.AsSpan((int)headerSize, (int)tablesSize);
        var template = new LpMetadataTemplate
        {
            SourcePath = fullPath,
            Geometry = geometry,
            Header = header
        };

        ParseExtents(tables, descriptors[1], template.Extents);
        ParseGroups(tables, descriptors[2], template.Groups);
        ParseBlockDevices(tables, descriptors[3], template.BlockDevices);
        ParsePartitions(tables, descriptors[0], template);
        ValidateRelationships(template);
        return template;
    }

    private static LpGeometry ParseGeometry(ReadOnlySpan<byte> geometryBlock)
    {
        if (BinaryHelpers.ReadUInt32(geometryBlock, 0) != LpConstants.GeometryMagic)
            throw new InvalidDataException("geometry magic 无效");
        uint structSize = BinaryHelpers.ReadUInt32(geometryBlock, 4);
        if (structSize < LpConstants.GeometryStructSize || structSize > LpConstants.GeometryBlockSize)
            throw new InvalidDataException($"geometry struct_size 无效: {structSize}");

        byte[] rawStruct = geometryBlock[..(int)structSize].ToArray();
        byte[] expectedChecksum = rawStruct.AsSpan(8, 32).ToArray();
        rawStruct.AsSpan(8, 32).Clear();
        byte[] actualChecksum = BinaryHelpers.Sha256(rawStruct);
        if (!BinaryHelpers.FixedTimeEquals(expectedChecksum, actualChecksum))
            throw new InvalidDataException("geometry SHA-256 校验失败");

        uint metadataMaxSize = BinaryHelpers.ReadUInt32(geometryBlock, 40);
        uint slotCount = BinaryHelpers.ReadUInt32(geometryBlock, 44);
        uint logicalBlockSize = BinaryHelpers.ReadUInt32(geometryBlock, 48);
        if (metadataMaxSize < LpConstants.HeaderV10Size || metadataMaxSize % LpConstants.SectorSize != 0)
            throw new InvalidDataException($"metadata_max_size 无效: {metadataMaxSize}");
        if (slotCount is 0 or > 16)
            throw new InvalidDataException($"metadata slot 数量无效: {slotCount}");
        if (!BinaryHelpers.IsPowerOfTwo(logicalBlockSize) ||
            logicalBlockSize % LpConstants.SectorSize != 0)
        {
            throw new InvalidDataException($"logical_block_size 无效: {logicalBlockSize}");
        }

        byte[] preservedStruct = geometryBlock[..(int)structSize].ToArray();
        return new LpGeometry(
            structSize,
            metadataMaxSize,
            slotCount,
            logicalBlockSize,
            preservedStruct);
    }

    private static LpHeaderInfo ParseAndValidateHeader(ReadOnlySpan<byte> metadata, uint tablesSize)
    {
        ushort major = BinaryHelpers.ReadUInt16(metadata, 4);
        ushort minor = BinaryHelpers.ReadUInt16(metadata, 6);
        uint headerSize = BinaryHelpers.ReadUInt32(metadata, 8);
        if (major != 10 || minor > 2)
            throw new NotSupportedException($"暂不支持 LP metadata {major}.{minor}");

        byte[] header = metadata[..(int)headerSize].ToArray();
        byte[] expectedHeaderChecksum = header.AsSpan(12, 32).ToArray();
        header.AsSpan(12, 32).Clear();
        byte[] actualHeaderChecksum = BinaryHelpers.Sha256(header);
        if (!BinaryHelpers.FixedTimeEquals(expectedHeaderChecksum, actualHeaderChecksum))
            throw new InvalidDataException("metadata header SHA-256 校验失败");

        byte[] expectedTablesChecksum = metadata.Slice(48, 32).ToArray();
        ReadOnlySpan<byte> tables = metadata.Slice((int)headerSize, (int)tablesSize);
        byte[] actualTablesChecksum = BinaryHelpers.Sha256(tables);
        if (!BinaryHelpers.FixedTimeEquals(expectedTablesChecksum, actualTablesChecksum))
            throw new InvalidDataException("metadata tables SHA-256 校验失败");

        uint flags = headerSize >= LpConstants.HeaderV12Size
            ? BinaryHelpers.ReadUInt32(metadata, 128)
            : 0;
        return new LpHeaderInfo(
            major,
            minor,
            headerSize,
            flags,
            tablesSize,
            metadata[..(int)headerSize].ToArray());
    }

    private static TableDescriptor ReadDescriptor(
        ReadOnlySpan<byte> header,
        int offset,
        int expectedEntrySize,
        string tableName)
    {
        var descriptor = new TableDescriptor(
            BinaryHelpers.ReadUInt32(header, offset),
            BinaryHelpers.ReadUInt32(header, offset + 4),
            BinaryHelpers.ReadUInt32(header, offset + 8));
        if (descriptor.EntrySize != expectedEntrySize)
        {
            throw new InvalidDataException(
                $"{tableName} entry_size 无效: {descriptor.EntrySize} != {expectedEntrySize}");
        }
        return descriptor;
    }

    private static void ValidateDescriptorRange(TableDescriptor descriptor, uint tablesSize)
    {
        ulong end = checked((ulong)descriptor.Offset + (ulong)descriptor.Count * descriptor.EntrySize);
        if (end > tablesSize)
            throw new InvalidDataException("metadata 表描述符越过 tables_size");
    }

    private static void ParseExtents(
        ReadOnlySpan<byte> tables,
        TableDescriptor descriptor,
        ICollection<LpExtentInfo> destination)
    {
        for (uint i = 0; i < descriptor.Count; i++)
        {
            int offset = checked((int)(descriptor.Offset + i * descriptor.EntrySize));
            destination.Add(new LpExtentInfo(
                BinaryHelpers.ReadUInt64(tables, offset),
                BinaryHelpers.ReadUInt32(tables, offset + 8),
                BinaryHelpers.ReadUInt64(tables, offset + 12),
                BinaryHelpers.ReadUInt32(tables, offset + 20)));
        }
    }

    private static void ParseGroups(
        ReadOnlySpan<byte> tables,
        TableDescriptor descriptor,
        ICollection<LpGroupInfo> destination)
    {
        for (uint i = 0; i < descriptor.Count; i++)
        {
            int offset = checked((int)(descriptor.Offset + i * descriptor.EntrySize));
            destination.Add(new LpGroupInfo(
                BinaryHelpers.ReadFixedAscii(tables.Slice(offset, LpConstants.NameLength)),
                BinaryHelpers.ReadUInt32(tables, offset + 36),
                BinaryHelpers.ReadUInt64(tables, offset + 40)));
        }
    }

    private static void ParseBlockDevices(
        ReadOnlySpan<byte> tables,
        TableDescriptor descriptor,
        ICollection<LpBlockDeviceInfo> destination)
    {
        for (uint i = 0; i < descriptor.Count; i++)
        {
            int offset = checked((int)(descriptor.Offset + i * descriptor.EntrySize));
            destination.Add(new LpBlockDeviceInfo(
                BinaryHelpers.ReadUInt64(tables, offset),
                BinaryHelpers.ReadUInt32(tables, offset + 8),
                BinaryHelpers.ReadUInt32(tables, offset + 12),
                BinaryHelpers.ReadUInt64(tables, offset + 16),
                BinaryHelpers.ReadFixedAscii(tables.Slice(offset + 24, LpConstants.NameLength)),
                BinaryHelpers.ReadUInt32(tables, offset + 60)));
        }
    }

    private static void ParsePartitions(
        ReadOnlySpan<byte> tables,
        TableDescriptor descriptor,
        LpMetadataTemplate template)
    {
        for (uint i = 0; i < descriptor.Count; i++)
        {
            int offset = checked((int)(descriptor.Offset + i * descriptor.EntrySize));
            string name = BinaryHelpers.ReadFixedAscii(tables.Slice(offset, LpConstants.NameLength));
            uint firstExtent = BinaryHelpers.ReadUInt32(tables, offset + 40);
            uint extentCount = BinaryHelpers.ReadUInt32(tables, offset + 44);
            ulong extentEnd = checked((ulong)firstExtent + extentCount);
            if (extentEnd > (ulong)template.Extents.Count)
                throw new InvalidDataException($"逻辑分区 {name} 的 extent 索引越界");

            ulong size = 0;
            for (uint extentIndex = 0; extentIndex < extentCount; extentIndex++)
            {
                LpExtentInfo extent = template.Extents[checked((int)(firstExtent + extentIndex))];
                size = checked(size + extent.NumSectors * LpConstants.SectorSize);
            }

            template.Partitions.Add(new LpPartitionInfo(
                name,
                BinaryHelpers.ReadUInt32(tables, offset + 36),
                firstExtent,
                extentCount,
                BinaryHelpers.ReadUInt32(tables, offset + 48),
                size));
        }
    }

    private static void ValidateRelationships(LpMetadataTemplate template)
    {
        if (template.BlockDevices.Count == 0)
            throw new InvalidDataException("metadata 没有 block device");
        if (template.Groups.Count == 0)
            throw new InvalidDataException("metadata 没有 partition group");

        EnsureUniqueNames(template.BlockDevices.Select(item => item.Name), "block device");
        EnsureUniqueNames(template.Groups.Select(item => item.Name), "partition group");
        EnsureUniqueNames(template.Partitions.Select(item => item.Name), "logical partition");

        foreach (LpBlockDeviceInfo device in template.BlockDevices)
        {
            if (string.IsNullOrWhiteSpace(device.Name) || device.Size == 0)
                throw new InvalidDataException("metadata block device 名称或容量无效");
            if (device.Size % template.Geometry.LogicalBlockSize != 0)
                throw new InvalidDataException($"block device {device.Name} 容量未按逻辑块对齐");
            ulong firstByte = checked(device.FirstLogicalSector * LpConstants.SectorSize);
            if (firstByte >= device.Size)
                throw new InvalidDataException($"block device {device.Name} 的首逻辑扇区越界");
        }

        foreach (LpExtentInfo extent in template.Extents)
        {
            if (extent.TargetType is not (0 or 1))
                throw new NotSupportedException($"不支持的 extent target type: {extent.TargetType}");
            if (extent.TargetType != 0)
                continue;
            if (extent.TargetSource >= template.BlockDevices.Count)
                throw new InvalidDataException("extent target_source 越界");
            LpBlockDeviceInfo device = template.BlockDevices[(int)extent.TargetSource];
            ulong start = checked(extent.TargetData * LpConstants.SectorSize);
            ulong length = checked(extent.NumSectors * LpConstants.SectorSize);
            if (start < checked(device.FirstLogicalSector * LpConstants.SectorSize) ||
                checked(start + length) > device.Size)
            {
                throw new InvalidDataException("extent 超出 block device 物理边界");
            }
        }

        foreach (LpPartitionInfo partition in template.Partitions)
        {
            if (string.IsNullOrWhiteSpace(partition.Name))
                throw new InvalidDataException("metadata 存在空逻辑分区名");
            if (partition.GroupIndex >= template.Groups.Count)
                throw new InvalidDataException($"逻辑分区 {partition.Name} 的 group_index 越界");
        }
    }

    private static void EnsureUniqueNames(IEnumerable<string> names, string kind)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (!seen.Add(name))
                throw new InvalidDataException($"metadata 存在重复 {kind} 名称: {name}");
        }
    }

    private static uint ReadUInt32At(FileStream stream, long offset)
    {
        byte[] data = ReadAt(stream, offset, sizeof(uint));
        return BinaryHelpers.ReadUInt32(data, 0);
    }

    private static byte[] ReadAt(FileStream stream, long offset, int count)
    {
        if (offset < 0 || count < 0 || offset > stream.Length - count)
            throw new EndOfStreamException("读取 super_meta 时超出文件范围");
        byte[] buffer = new byte[count];
        stream.Position = offset;
        stream.ReadExactly(buffer);
        return buffer;
    }
}
