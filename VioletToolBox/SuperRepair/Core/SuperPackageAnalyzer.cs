using System.Globalization;
using System.Text;

namespace SuperFix.Core;

public static class SuperPackageAnalyzer
{
    public static SuperPackageAnalysis Analyze(string definitionPath)
    {
        SuperDefinition definition = SuperDefinitionParser.ParseFile(definitionPath);
        string superMetaPath = ResolveSuperMetaPath(definition);
        LpMetadataTemplate template = LpMetadataTemplateReader.Read(superMetaPath);

        ValidateDefinition(definition);
        ValidateTemplateAgainstDefinition(template, definition);
        List<ScatterSuperEntry> scatterEntries = ReadScatterEntries(definition.PackageRoot);
        foreach (ScatterSuperEntry entry in scatterEntries)
        {
            if (entry.PartitionSize != definition.BlockDevices[0].Size)
            {
                throw new InvalidDataException(
                    $"Scatter 中 Super 容量不一致 ({Path.GetFileName(entry.SourcePath)}, {entry.Storage}): " +
                    $"0x{entry.PartitionSize:X} != 0x{definition.BlockDevices[0].Size:X}");
            }
        }

        ulong declaredBytes = SumChecked(
            definition.Partitions.Where(partition => partition.IsDynamic).Select(partition => partition.Size));
        long emptySize = LpConstants.GetTotalMetadataSize(
            template.Geometry.MetadataMaxSize,
            template.Geometry.MetadataSlotCount);

        var analysis = new SuperPackageAnalysis
        {
            Definition = definition,
            Template = template,
            ResolvedSuperMetaPath = superMetaPath,
            DeclaredPartitionBytes = declaredBytes,
            EmptyImageSize = emptySize
        };
        analysis.ScatterEntries.AddRange(scatterEntries);
        return analysis;
    }

    private static void ValidateDefinition(SuperDefinition definition)
    {
        if (definition.BlockDevices.Count != 1)
            throw new NotSupportedException(
                $"当前只支持单一物理 Super，定义中有 {definition.BlockDevices.Count} 个 block device");
        SuperDefinitionBlockDevice device = definition.BlockDevices[0];
        if (string.IsNullOrWhiteSpace(device.Name) || device.Size == 0)
            throw new InvalidDataException("super_def 的 block device 名称或容量无效");
        if (!device.Name.Equals("super", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"Fastboot 刷写目标固定为 super，不支持 block device {device.Name}");
        if (!BinaryHelpers.IsPowerOfTwo(device.BlockSize) ||
            device.BlockSize % LpConstants.SectorSize != 0)
        {
            throw new InvalidDataException($"super_def block_size 无效: {device.BlockSize}");
        }
        if (device.Alignment != 0 && !BinaryHelpers.IsPowerOfTwo(device.Alignment))
            throw new InvalidDataException($"super_def alignment 无效: {device.Alignment}");
        if (definition.SuperDeviceTotalSize != 0 && definition.SuperDeviceTotalSize != device.Size)
            throw new InvalidDataException("super_device.total_size 与 block_devices.size 不一致");

        EnsureUniqueNames(definition.BlockDevices.Select(item => item.Name), "block device");
        EnsureUniqueNames(definition.Groups.Select(item => item.Name), "group");
        EnsureUniqueNames(definition.Partitions.Select(item => item.Name), "partition");
        if (definition.Groups.All(group => !group.Name.Equals("default", StringComparison.Ordinal)))
            throw new InvalidDataException("super_def 缺少 default group");

        foreach (SuperDefinitionPartition partition in definition.Partitions.Where(item => item.IsDynamic))
        {
            if (string.IsNullOrWhiteSpace(partition.Name))
                throw new InvalidDataException("super_def 存在空动态分区名");
            if (string.IsNullOrWhiteSpace(partition.GroupName))
                throw new InvalidDataException($"动态分区 {partition.Name} 缺少 group_name");
            if (partition.Size % device.BlockSize != 0)
            {
                throw new InvalidDataException(
                    $"动态分区 {partition.Name} 大小未按 {device.BlockSize} 字节对齐");
            }
        }

        ulong sum = SumChecked(
            definition.Partitions.Where(partition => partition.IsDynamic).Select(partition => partition.Size));
        if (definition.SuperDeviceUsedSize != 0 && definition.SuperDeviceUsedSize != sum)
        {
            throw new InvalidDataException(
                $"super_device.used_size 与动态分区声明合计不一致: {definition.SuperDeviceUsedSize} != {sum}");
        }

        var groupTotals = definition.Partitions
            .Where(partition => partition.IsDynamic)
            .GroupBy(partition => partition.GroupName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => SumChecked(group.Select(item => item.Size)), StringComparer.Ordinal);
        foreach (SuperDefinitionGroup group in definition.Groups)
        {
            groupTotals.TryGetValue(group.Name, out ulong groupTotal);
            if (group.MaximumSize != 0 && groupTotal > group.MaximumSize)
            {
                throw new InvalidDataException(
                    $"分区组 {group.Name} 已声明 {groupTotal} 字节，超过 maximum_size {group.MaximumSize}");
            }
        }
    }

    private static void ValidateTemplateAgainstDefinition(
        LpMetadataTemplate template,
        SuperDefinition definition)
    {
        if (template.BlockDevices.Count != 1)
            throw new NotSupportedException(
                $"当前只支持单一物理 Super，原厂 metadata 中有 {template.BlockDevices.Count} 个 block device");

        SuperDefinitionBlockDevice declaredDevice = definition.BlockDevices[0];
        LpBlockDeviceInfo metadataDevice = template.BlockDevices[0];
        if (!metadataDevice.Name.Equals(declaredDevice.Name, StringComparison.Ordinal) ||
            metadataDevice.Size != declaredDevice.Size ||
            metadataDevice.Alignment != declaredDevice.Alignment ||
            metadataDevice.AlignmentOffset != declaredDevice.AlignmentOffset)
        {
            throw new InvalidDataException("super_def 与原厂 metadata 的 block device 参数不一致");
        }
        if (template.Geometry.LogicalBlockSize != declaredDevice.BlockSize)
            throw new InvalidDataException("super_def block_size 与原厂 geometry 不一致");
        if (definition.SuperMetaDeclaredSize != 0 &&
            definition.SuperMetaDeclaredSize != template.Geometry.MetadataMaxSize)
        {
            throw new InvalidDataException(
                $"super_meta.size 与 metadata_max_size 不一致: " +
                $"{definition.SuperMetaDeclaredSize} != {template.Geometry.MetadataMaxSize}");
        }

        long totalMetadataSize = LpConstants.GetTotalMetadataSize(
            template.Geometry.MetadataMaxSize,
            template.Geometry.MetadataSlotCount);
        ulong expectedFirstByte = AlignWithOffset(
            checked((ulong)totalMetadataSize),
            metadataDevice.Alignment == 0 ? template.Geometry.LogicalBlockSize : metadataDevice.Alignment,
            metadataDevice.AlignmentOffset);
        ulong actualFirstByte = checked(metadataDevice.FirstLogicalSector * LpConstants.SectorSize);
        if (actualFirstByte != expectedFirstByte)
        {
            throw new InvalidDataException(
                $"原厂 metadata 首逻辑扇区异常: 0x{actualFirstByte:X} != 0x{expectedFirstByte:X}");
        }
        if ((ulong)totalMetadataSize > metadataDevice.Size)
            throw new InvalidDataException("metadata 区域大于物理 Super");

        var declaredGroups = definition.Groups.ToDictionary(group => group.Name, StringComparer.Ordinal);
        var metadataGroups = template.Groups.ToDictionary(group => group.Name, StringComparer.Ordinal);
        if (declaredGroups.Count != metadataGroups.Count)
            throw new InvalidDataException("super_def 与原厂 metadata 的 group 数量不一致");
        foreach ((string name, SuperDefinitionGroup group) in declaredGroups)
        {
            if (!metadataGroups.TryGetValue(name, out LpGroupInfo? metadataGroup) ||
                metadataGroup.MaximumSize != group.MaximumSize)
            {
                throw new InvalidDataException($"分区组 {name} 与原厂 metadata 不一致");
            }
        }

        List<SuperDefinitionPartition> declaredPartitions = definition.Partitions
            .Where(partition => partition.IsDynamic)
            .ToList();
        if (declaredPartitions.Count != template.Partitions.Count)
        {
            throw new InvalidDataException(
                $"super_def 与原厂 metadata 的逻辑分区数量不一致: " +
                $"{declaredPartitions.Count} != {template.Partitions.Count}");
        }

        var metadataPartitions = template.Partitions.ToDictionary(partition => partition.Name, StringComparer.Ordinal);
        foreach (SuperDefinitionPartition partition in declaredPartitions)
        {
            if (!metadataPartitions.TryGetValue(partition.Name, out LpPartitionInfo? metadataPartition))
                throw new InvalidDataException($"原厂 metadata 缺少逻辑分区 {partition.Name}");
            string metadataGroupName = template.Groups[(int)metadataPartition.GroupIndex].Name;
            if (metadataPartition.Size != partition.Size ||
                !metadataGroupName.Equals(partition.GroupName, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"逻辑分区 {partition.Name} 的大小或分组与原厂 metadata 不一致");
            }
        }

        ValidateExtentOverlap(template);
    }

    private static void ValidateExtentOverlap(LpMetadataTemplate template)
    {
        var perDevice = new Dictionary<uint, List<(ulong Start, ulong End)>>();
        foreach (LpExtentInfo extent in template.Extents.Where(extent => extent.TargetType == 0))
        {
            ulong end = checked(extent.TargetData + extent.NumSectors);
            if (!perDevice.TryGetValue(extent.TargetSource, out List<(ulong Start, ulong End)>? ranges))
            {
                ranges = [];
                perDevice.Add(extent.TargetSource, ranges);
            }
            ranges.Add((extent.TargetData, end));
        }

        foreach (List<(ulong Start, ulong End)> ranges in perDevice.Values)
        {
            ranges.Sort((left, right) => left.Start.CompareTo(right.Start));
            for (int i = 1; i < ranges.Count; i++)
            {
                if (ranges[i].Start < ranges[i - 1].End)
                    throw new InvalidDataException("原厂 metadata 存在重叠 extent");
            }
        }
    }

    private static string ResolveSuperMetaPath(SuperDefinition definition)
    {
        string relative = definition.SuperMetaRelativePath.Replace('/', Path.DirectorySeparatorChar);
        string definitionDirectory = Path.GetDirectoryName(definition.DefinitionPath) ?? definition.PackageRoot;
        var candidates = new List<string>();
        if (Path.IsPathRooted(relative))
            candidates.Add(relative);
        else
        {
            candidates.Add(Path.Combine(definition.PackageRoot, relative));
            candidates.Add(Path.Combine(definitionDirectory, relative));
            candidates.Add(Path.Combine(definition.PackageRoot, "IMAGES", Path.GetFileName(relative)));
        }

        string? existing = candidates
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
        if (existing == null)
            throw new FileNotFoundException($"找不到 super_meta: {definition.SuperMetaRelativePath}");

        if (LooksLikeLpMetadata(existing))
            return existing;

        var info = new FileInfo(existing);
        if (info.Length is <= 0 or > 512)
            throw new InvalidDataException($"super_meta 不是 liblp metadata: {existing}");

        string alias = File.ReadAllText(existing, Encoding.UTF8).Trim().Trim('\0');
        if (string.IsNullOrWhiteSpace(alias) || alias.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            throw new InvalidDataException($"super_meta 别名内容无效: {existing}");
        string aliasPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(existing)!, alias));
        if (!File.Exists(aliasPath) || !LooksLikeLpMetadata(aliasPath))
            throw new FileNotFoundException($"super_meta 别名目标无效: {alias}", aliasPath);
        return aliasPath;
    }

    private static bool LooksLikeLpMetadata(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> magic = stackalloc byte[4];
        if (stream.Read(magic) == magic.Length && BinaryHelpers.ReadUInt32(magic, 0) == LpConstants.GeometryMagic)
            return true;
        if (stream.Length < LpConstants.ReservedBytes + magic.Length)
            return false;
        stream.Position = LpConstants.ReservedBytes;
        return stream.Read(magic) == magic.Length &&
               BinaryHelpers.ReadUInt32(magic, 0) == LpConstants.GeometryMagic;
    }

    private static List<ScatterSuperEntry> ReadScatterEntries(string packageRoot)
    {
        var result = new List<ScatterSuperEntry>();
        string[] files;
        try
        {
            files = Directory.GetFiles(packageRoot, "*scatter*.txt", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return result;
        }

        foreach (string path in files)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Trim().Equals("partition_name: super", StringComparison.OrdinalIgnoreCase))
                    continue;

                ulong start = 0;
                ulong size = 0;
                string storage = string.Empty;
                for (int j = i + 1; j < lines.Length && j <= i + 24; j++)
                {
                    string line = lines[j].Trim();
                    if (line.StartsWith("- partition_index:", StringComparison.OrdinalIgnoreCase))
                        break;
                    if (TryReadKey(line, "physical_start_addr", out string startText))
                        start = ParseUnsigned(startText);
                    else if (TryReadKey(line, "partition_size", out string sizeText))
                        size = ParseUnsigned(sizeText);
                    else if (TryReadKey(line, "storage", out string storageText))
                        storage = storageText;
                    else if (string.IsNullOrWhiteSpace(storage) &&
                             TryReadKey(line, "region", out string regionText))
                        storage = regionText;
                }

                if (size != 0)
                    result.Add(new ScatterSuperEntry(Path.GetFullPath(path), storage, start, size));
            }
        }

        return result
            .DistinctBy(entry => (entry.SourcePath, entry.Storage, entry.StartAddress, entry.PartitionSize))
            .ToList();
    }

    private static bool TryReadKey(string line, string key, out string value)
    {
        string prefix = key + ":";
        if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = line[prefix.Length..].Trim();
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static ulong ParseUnsigned(string text)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            ulong.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hex))
        {
            return hex;
        }
        if (ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong value))
            return value;
        throw new InvalidDataException($"Scatter 数值无效: {text}");
    }

    private static ulong AlignWithOffset(ulong value, ulong alignment, ulong offset)
    {
        if (alignment == 0)
            return value;
        if (value <= offset)
            return offset;
        ulong remainder = (value - offset) % alignment;
        return remainder == 0 ? value : checked(value + alignment - remainder);
    }

    private static ulong SumChecked(IEnumerable<ulong> values)
    {
        ulong sum = 0;
        foreach (ulong value in values)
            sum = checked(sum + value);
        return sum;
    }

    private static void EnsureUniqueNames(IEnumerable<string> names, string kind)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException($"super_def 存在空 {kind} 名称");
            if (!seen.Add(name))
                throw new InvalidDataException($"super_def 存在重复 {kind} 名称: {name}");
        }
    }
}
