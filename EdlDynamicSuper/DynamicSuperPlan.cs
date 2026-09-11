using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using AFlashTool.Modular.Super.LpMake;

namespace AFlashTool.Modular.Super;

public sealed class DynamicSuperImage
{
    public required string PartitionName { get; init; }
    public required string SourcePath { get; init; }
    public required long ExpandedSize { get; init; }
    public required ulong AllocatedSize { get; init; }
    public required bool IsSparse { get; init; }
}

public sealed class DynamicSuperPlan
{
    public required string DefinitionPath { get; init; }
    public required ulong BlockDeviceSize { get; init; }
    public required LpMetadata Metadata { get; init; }
    public required byte[] MetadataImage { get; init; }
    public required IReadOnlyList<DynamicSuperImage> Images { get; init; }

    public long TotalWriteBytes => checked(
        MetadataImage.LongLength + Images.Sum(image => checked((long)image.AllocatedSize)));
}

public static class DynamicSuperPlanner
{
    private const uint DefaultMetadataMaxSize = 65536;

    public static string? FindDefinition(string programDirectory)
    {
        if (string.IsNullOrWhiteSpace(programDirectory))
            return null;

        string startDirectory;
        try
        {
            startDirectory = Path.GetFullPath(programDirectory);
        }
        catch
        {
            return null;
        }

        var searchDirectories = new List<string>();
        AddSearchDirectory(searchDirectories, startDirectory);
        AddSearchDirectory(searchDirectories, Path.Combine(startDirectory, "META"));

        DirectoryInfo? cursor = Directory.Exists(startDirectory)
            ? new DirectoryInfo(startDirectory)
            : new FileInfo(startDirectory).Directory;
        for (int i = 0; i < 2 && cursor?.Parent != null; i++)
        {
            cursor = cursor.Parent;
            AddSearchDirectory(searchDirectories, cursor.FullName);
            AddSearchDirectory(searchDirectories, Path.Combine(cursor.FullName, "META"));
        }

        var definitions = searchDirectories
            .Where(Directory.Exists)
            .SelectMany(directory =>
            {
                try
                {
                    return Directory.GetFiles(directory, "super_def*.json", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    return Array.Empty<string>();
                }
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (definitions.Count == 0)
            return null;

        string? nvId = TryFindNvId(searchDirectories);
        if (!string.IsNullOrWhiteSpace(nvId))
        {
            string expectedName = $"super_def.{nvId}.json";
            string? exact = definitions.FirstOrDefault(path =>
                Path.GetFileName(path).Equals(expectedName, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;
        }

        return definitions
            .OrderBy(path => Path.GetFileName(path).Equals("super_def.json", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    public static DynamicSuperPlan Create(
        string programDirectory,
        string definitionPath,
        uint metadataSlotCount = 2)
    {
        if (!File.Exists(definitionPath))
            throw new FileNotFoundException("未找到 super_def 定义文件", definitionPath);
        if (metadataSlotCount == 0)
            throw new InvalidOperationException("metadata 槽位数量不能为 0");

        string definitionJson = File.ReadAllText(definitionPath);
        SuperDef definition = SuperDefParser.Parse(definitionJson);
        if (definition.BlockDevices.Count != 1)
            throw new NotSupportedException($"免合并写入当前仅支持单一 super 块设备，定义中包含 {definition.BlockDevices.Count} 个");

        SuperDefBlockDevice blockDevice = definition.BlockDevices[0];
        if (blockDevice.Size == 0)
            throw new InvalidOperationException("super_def 中的块设备大小为 0");
        if (blockDevice.BlockSize == 0 || blockDevice.BlockSize % LpMakeConstants.LP_SECTOR_SIZE != 0)
            throw new InvalidOperationException($"不支持的逻辑块大小: {blockDevice.BlockSize}");

        uint metadataMaxSize = DefaultMetadataMaxSize;
        ushort metadataMinorVersion = LpMakeConstants.LP_METADATA_MINOR_VERSION_MIN;
        uint metadataHeaderSize = LpMakeConstants.SizeOfHeaderV10;
        uint metadataHeaderFlags = 0;
        if (TryReadVendorGeometry(
                programDirectory,
                definitionPath,
                definition,
                out uint vendorMetadataMaxSize,
                out uint vendorMetadataSlotCount,
                out ushort vendorMetadataMinorVersion,
                out uint vendorMetadataHeaderSize,
                out uint vendorMetadataHeaderFlags))
        {
            metadataMaxSize = vendorMetadataMaxSize;
            metadataSlotCount = vendorMetadataSlotCount;
            metadataMinorVersion = vendorMetadataMinorVersion;
            metadataHeaderSize = vendorMetadataHeaderSize;
            metadataHeaderFlags = vendorMetadataHeaderFlags;
        }

        var resolvedImages = new Dictionary<string, DynamicSuperImage>(StringComparer.OrdinalIgnoreCase);
        foreach (SuperDefPartition partition in definition.Partitions.Where(partition => partition.IsDynamic))
        {
            if (string.IsNullOrWhiteSpace(partition.Name))
                throw new InvalidOperationException("super_def 中存在名称为空的动态分区");

            string? imagePath = ResolveImagePath(programDirectory, definitionPath, partition);
            if (!string.IsNullOrWhiteSpace(partition.Path) && imagePath == null)
                throw new FileNotFoundException($"动态分区 {partition.Name} 的镜像文件不存在: {partition.Path}");
            if (imagePath == null)
                continue;

            bool isSparse = AndroidSparseReadStream.IsSparseImage(imagePath);
            long expandedSize = isSparse
                ? AndroidSparseReadStream.ValidateAndGetExpandedLength(imagePath)
                : new FileInfo(imagePath).Length;
            if (expandedSize < 0)
                throw new InvalidDataException($"动态分区 {partition.Name} 的镜像大小无效");

            ulong requiredSize = checked((ulong)expandedSize);
            if (requiredSize > partition.Size)
                partition.Size = requiredSize;

            resolvedImages[partition.Name] = new DynamicSuperImage
            {
                PartitionName = partition.Name,
                SourcePath = imagePath,
                ExpandedSize = expandedSize,
                AllocatedSize = partition.Size,
                IsSparse = isSparse
            };
        }

        List<SuperDefPartition> dynamicPartitions = OrderDynamicPartitions(
            definition.Partitions.Where(partition => partition.IsDynamic));
        if (dynamicPartitions.Count == 0)
            throw new InvalidOperationException("super_def 中没有动态分区");

        var blockDevices = new[]
        {
            new BlockDeviceInfo
            {
                PartitionName = string.IsNullOrWhiteSpace(blockDevice.Name) ? "super" : blockDevice.Name,
                Size = blockDevice.Size,
                Alignment = blockDevice.Alignment,
                AlignmentOffset = blockDevice.AlignmentOffset,
                LogicalBlockSize = blockDevice.BlockSize
            }
        };

        LpMetadataBuilder builder = LpMetadataBuilder.New(
            blockDevices,
            blockDevices[0].PartitionName,
            metadataMaxSize,
            metadataSlotCount)
            ?? throw new InvalidOperationException("无法初始化 LP metadata");

        foreach (SuperDefGroup group in definition.Groups)
        {
            if (string.IsNullOrWhiteSpace(group.Name)
                || group.Name.Equals(LpMakeConstants.KDefaultGroup, StringComparison.Ordinal))
            {
                continue;
            }
            if (!builder.AddGroup(group.Name, group.MaximumSize))
                throw new InvalidOperationException($"添加动态分区组失败: {group.Name}");
        }

        foreach (SuperDefPartition partitionDefinition in dynamicPartitions)
        {
            string groupName = string.IsNullOrWhiteSpace(partitionDefinition.GroupName)
                ? LpMakeConstants.KDefaultGroup
                : partitionDefinition.GroupName;
            Partition partition = builder.AddPartition(
                partitionDefinition.Name,
                groupName,
                LpMakeConstants.LP_PARTITION_ATTR_READONLY)
                ?? throw new InvalidOperationException($"添加动态分区失败: {partitionDefinition.Name}");

            if (partitionDefinition.Size > 0 && !builder.ResizePartition(partition, partitionDefinition.Size))
                throw new InvalidOperationException($"动态分区空间不足: {partitionDefinition.Name} ({partitionDefinition.Size} bytes)");
        }

        LpMetadata metadata = builder.Export()
            ?? throw new InvalidOperationException("LP metadata 导出失败");
        metadata.Header.MinorVersion = metadataMinorVersion;
        metadata.Header.HeaderSize = metadataHeaderSize;
        metadata.Header.Flags = metadataHeaderFlags;
        byte[] serializedMetadata = LpMetadataSerializer.SerializeMetadata(metadata);
        if (serializedMetadata.Length > metadata.Geometry.MetadataMaxSize)
            throw new InvalidOperationException(
                $"LP metadata 超出上限: {serializedMetadata.Length} > {metadata.Geometry.MetadataMaxSize}");

        ValidateGeneratedMetadataMatchesVendor(programDirectory, definitionPath, definition, metadata);

        byte[] metadataImage;
        using (var stream = new MemoryStream())
        {
            LpImageWriter.WriteMetaImage(stream, metadata);
            metadataImage = stream.ToArray();
        }

        if ((ulong)metadataImage.LongLength > blockDevice.Size)
            throw new InvalidOperationException("LP metadata 大于 super 块设备");

        var finalImages = new List<DynamicSuperImage>();
        foreach (SuperDefPartition partitionDefinition in dynamicPartitions)
        {
            LpMetadataPartition metadataPartition = metadata.Partitions.First(partition =>
                partition.Name.Equals(partitionDefinition.Name, StringComparison.Ordinal));
            ulong allocatedBytes = 0;
            for (uint i = 0; i < metadataPartition.NumExtents; i++)
            {
                LpMetadataExtent extent = metadata.Extents[(int)(metadataPartition.FirstExtentIndex + i)];
                if (extent.TargetType != LpMakeConstants.LP_TARGET_TYPE_LINEAR)
                    continue;
                if (extent.TargetSource != 0)
                    throw new NotSupportedException($"动态分区 {partitionDefinition.Name} 使用了多个块设备");

                ulong extentBytes = checked(extent.NumSectors * (ulong)LpMakeConstants.LP_SECTOR_SIZE);
                ulong extentEnd = checked(
                    extent.TargetData * (ulong)LpMakeConstants.LP_SECTOR_SIZE + extentBytes);
                if (extentEnd > blockDevice.Size)
                    throw new InvalidOperationException($"动态分区 {partitionDefinition.Name} 的 extent 超出 super 边界");
                allocatedBytes = checked(allocatedBytes + extentBytes);
            }

            if (allocatedBytes == 0)
                continue;

            resolvedImages.TryGetValue(partitionDefinition.Name, out DynamicSuperImage? image);
            long expandedSize = image?.ExpandedSize ?? 0;
            if ((ulong)expandedSize > allocatedBytes)
                throw new InvalidOperationException(
                    $"动态分区 {partitionDefinition.Name} 镜像超出已分配空间: {expandedSize} > {allocatedBytes}");

            finalImages.Add(new DynamicSuperImage
            {
                PartitionName = partitionDefinition.Name,
                SourcePath = image?.SourcePath ?? "",
                ExpandedSize = expandedSize,
                AllocatedSize = allocatedBytes,
                IsSparse = image?.IsSparse ?? false
            });
        }

        return new DynamicSuperPlan
        {
            DefinitionPath = Path.GetFullPath(definitionPath),
            BlockDeviceSize = blockDevice.Size,
            Metadata = metadata,
            MetadataImage = metadataImage,
            Images = finalImages
        };
    }

    public static Stream OpenExpandedImage(DynamicSuperImage image)
    {
        if (string.IsNullOrWhiteSpace(image.SourcePath))
            return Stream.Null;
        return image.IsSparse
            ? new AndroidSparseReadStream(image.SourcePath)
            : new FileStream(image.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private static string? ResolveImagePath(
        string programDirectory,
        string definitionPath,
        SuperDefPartition partition)
    {
        string programDir = Path.GetFullPath(programDirectory);
        string definitionDir = Path.GetDirectoryName(Path.GetFullPath(definitionPath)) ?? programDir;
        string packageRoot = new DirectoryInfo(definitionDir).Name.Equals("META", StringComparison.OrdinalIgnoreCase)
            ? Directory.GetParent(definitionDir)?.FullName ?? definitionDir
            : definitionDir;

        string relativePath = (partition.Path ?? "").Replace('/', Path.DirectorySeparatorChar);
        string fileName = !string.IsNullOrWhiteSpace(relativePath)
            ? Path.GetFileName(relativePath)
            : partition.Name + ".img";

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            if (Path.IsPathRooted(relativePath))
                candidates.Add(relativePath);
            else
            {
                candidates.Add(Path.Combine(definitionDir, relativePath));
                candidates.Add(Path.Combine(packageRoot, relativePath));
                candidates.Add(Path.Combine(programDir, relativePath));
            }
        }

        candidates.Add(Path.Combine(programDir, fileName));
        candidates.Add(Path.Combine(packageRoot, "IMAGES", fileName));
        candidates.Add(Path.Combine(packageRoot, fileName));
        candidates.Add(Path.Combine(definitionDir, fileName));

        return candidates
            .Select(path =>
            {
                try { return Path.GetFullPath(path); }
                catch { return ""; }
            })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }

    private static bool TryReadVendorGeometry(
        string programDirectory,
        string definitionPath,
        SuperDef definition,
        out uint metadataMaxSize,
        out uint metadataSlotCount,
        out ushort metadataMinorVersion,
        out uint metadataHeaderSize,
        out uint metadataHeaderFlags)
    {
        metadataMaxSize = 0;
        metadataSlotCount = 0;
        metadataMinorVersion = LpMakeConstants.LP_METADATA_MINOR_VERSION_MIN;
        metadataHeaderSize = LpMakeConstants.SizeOfHeaderV10;
        metadataHeaderFlags = 0;
        string relativePath = definition.SuperMeta?.Path ?? "";
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        string? metadataPath = ResolvePackageFilePath(programDirectory, definitionPath, relativePath);
        if (metadataPath == null)
            return false;

        byte[] geometry = new byte[LpMakeConstants.LP_METADATA_GEOMETRY_SIZE];
        using (var stream = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            int total = 0;
            while (total < geometry.Length)
            {
                int read = stream.Read(geometry, total, geometry.Length - total);
                if (read == 0)
                    break;
                total += read;
            }
            if (total < LpMakeConstants.SizeOfGeometry)
                throw new InvalidDataException($"super_meta geometry is too small: {metadataPath}");
        }

        ReadOnlySpan<byte> span = geometry;
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(span);
        uint structSize = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        if (magic != LpMakeConstants.LP_METADATA_GEOMETRY_MAGIC
            || structSize < LpMakeConstants.SizeOfGeometry
            || structSize > geometry.Length)
        {
            throw new InvalidDataException($"Invalid super_meta geometry: {metadataPath}");
        }

        byte[] expectedChecksum = span.Slice(8, 32).ToArray();
        byte[] checksumInput = span.Slice(0, checked((int)structSize)).ToArray();
        checksumInput.AsSpan(8, 32).Clear();
        byte[] actualChecksum = SHA256.HashData(checksumInput);
        if (!CryptographicOperations.FixedTimeEquals(expectedChecksum, actualChecksum))
            throw new InvalidDataException($"Invalid super_meta geometry checksum: {metadataPath}");

        metadataMaxSize = BinaryPrimitives.ReadUInt32LittleEndian(span[40..]);
        metadataSlotCount = BinaryPrimitives.ReadUInt32LittleEndian(span[44..]);
        uint logicalBlockSize = BinaryPrimitives.ReadUInt32LittleEndian(span[48..]);
        if (metadataMaxSize < LpMakeConstants.SizeOfHeaderV10
            || metadataMaxSize % LpMakeConstants.LP_SECTOR_SIZE != 0
            || metadataSlotCount == 0
            || metadataSlotCount > 16
            || logicalBlockSize == 0)
        {
            throw new InvalidDataException($"Invalid super_meta geometry parameters: {metadataPath}");
        }

        int headerOffset = LpMakeConstants.LP_METADATA_GEOMETRY_SIZE;
        using (var stream = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Length < headerOffset + LpMakeConstants.SizeOfHeaderV10)
                throw new InvalidDataException($"super_meta metadata header is missing: {metadataPath}");

            stream.Position = headerOffset;
            byte[] header = new byte[LpMakeConstants.SizeOfHeaderV12];
            int total = 0;
            while (total < header.Length)
            {
                int read = stream.Read(header, total, header.Length - total);
                if (read == 0)
                    break;
                total += read;
            }

            ReadOnlySpan<byte> headerSpan = header.AsSpan(0, total);
            uint headerMagic = BinaryPrimitives.ReadUInt32LittleEndian(headerSpan);
            ushort majorVersion = BinaryPrimitives.ReadUInt16LittleEndian(headerSpan[4..]);
            metadataMinorVersion = BinaryPrimitives.ReadUInt16LittleEndian(headerSpan[6..]);
            metadataHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(headerSpan[8..]);
            if (headerMagic != LpMakeConstants.LP_METADATA_HEADER_MAGIC
                || majorVersion != LpMakeConstants.LP_METADATA_MAJOR_VERSION
                || metadataMinorVersion > LpMakeConstants.LP_METADATA_MINOR_VERSION_MAX
                || (metadataHeaderSize != LpMakeConstants.SizeOfHeaderV10
                    && metadataHeaderSize != LpMakeConstants.SizeOfHeaderV12)
                || total < metadataHeaderSize)
            {
                throw new InvalidDataException($"Invalid super_meta metadata header: {metadataPath}");
            }

            metadataHeaderFlags = metadataHeaderSize >= LpMakeConstants.SizeOfHeaderV12
                ? BinaryPrimitives.ReadUInt32LittleEndian(headerSpan[128..])
                : 0;
        }

        return true;
    }

    private static List<SuperDefPartition> OrderDynamicPartitions(
        IEnumerable<SuperDefPartition> partitions)
    {
        var indexed = partitions
            .Select((partition, index) =>
            {
                string name = partition.Name ?? "";
                int slotRank = 0;
                string baseName = name;
                if (name.EndsWith("_a", StringComparison.OrdinalIgnoreCase))
                {
                    baseName = name[..^2];
                    slotRank = 0;
                }
                else if (name.EndsWith("_b", StringComparison.OrdinalIgnoreCase))
                {
                    baseName = name[..^2];
                    slotRank = 1;
                }
                return new { Partition = partition, Index = index, BaseName = baseName, SlotRank = slotRank };
            })
            .ToList();

        var firstIndexByBase = indexed
            .GroupBy(item => item.BaseName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Min(item => item.Index),
                StringComparer.OrdinalIgnoreCase);

        return indexed
            .OrderBy(item => firstIndexByBase[item.BaseName])
            .ThenBy(item => item.SlotRank)
            .ThenBy(item => item.Index)
            .Select(item => item.Partition)
            .ToList();
    }

    private static void ValidateGeneratedMetadataMatchesVendor(
        string programDirectory,
        string definitionPath,
        SuperDef definition,
        LpMetadata metadata)
    {
        string relativePath = definition.SuperMeta?.Path ?? "";
        if (string.IsNullOrWhiteSpace(relativePath))
            return;

        string? metadataPath = ResolvePackageFilePath(programDirectory, definitionPath, relativePath);
        if (metadataPath == null)
            return;

        using var generatedStream = new MemoryStream();
        LpImageWriter.WriteEmptyImage(generatedStream, metadata);
        byte[] generated = generatedStream.ToArray();
        byte[] vendor = File.ReadAllBytes(metadataPath);
        bool matches = vendor.Length >= generated.Length
            && generated.AsSpan().SequenceEqual(vendor.AsSpan(0, generated.Length))
            && vendor.AsSpan(generated.Length).IndexOfAnyExcept((byte)0) < 0;
        if (!matches)
        {
            throw new InvalidDataException(
                "生成的 super metadata 与刷机包原厂 super_meta 不一致，已停止写入。");
        }
    }

    private static string? ResolvePackageFilePath(
        string programDirectory,
        string definitionPath,
        string relativePath)
    {
        string programDir = Path.GetFullPath(programDirectory);
        string definitionDir = Path.GetDirectoryName(Path.GetFullPath(definitionPath)) ?? programDir;
        string packageRoot = new DirectoryInfo(definitionDir).Name.Equals("META", StringComparison.OrdinalIgnoreCase)
            ? Directory.GetParent(definitionDir)?.FullName ?? definitionDir
            : definitionDir;
        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);

        var candidates = new List<string>();
        if (Path.IsPathRooted(normalized))
        {
            candidates.Add(normalized);
        }
        else
        {
            candidates.Add(Path.Combine(packageRoot, normalized));
            candidates.Add(Path.Combine(definitionDir, normalized));
            candidates.Add(Path.Combine(programDir, normalized));
            candidates.Add(Path.Combine(programDir, Path.GetFileName(normalized)));
        }

        return candidates
            .Select(path =>
            {
                try { return Path.GetFullPath(path); }
                catch { return ""; }
            })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }

    private static void AddSearchDirectory(ICollection<string> directories, string directory)
    {
        try
        {
            string fullPath = Path.GetFullPath(directory);
            if (!directories.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                directories.Add(fullPath);
        }
        catch
        {
        }
    }

    private static string? TryFindNvId(IEnumerable<string> searchDirectories)
    {
        foreach (string directory in searchDirectories.Where(Directory.Exists))
        {
            string versionInfo = Path.Combine(directory, "version_info.txt");
            if (!File.Exists(versionInfo))
                continue;
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(versionInfo));
                JsonElement root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0
                    && root[0].TryGetProperty("nv_id", out JsonElement nvId))
                {
                    return nvId.GetString();
                }
            }
            catch
            {
            }
        }
        return null;
    }
}

internal sealed class AndroidSparseReadStream : Stream
{
    private const uint SparseMagic = 0xED26FF3A;
    private const ushort RawChunk = 0xCAC1;
    private const ushort FillChunk = 0xCAC2;
    private const ushort DontCareChunk = 0xCAC3;
    private const ushort Crc32Chunk = 0xCAC4;

    private readonly FileStream _stream;
    private readonly BinaryReader _reader;
    private readonly ushort _chunkHeaderSize;
    private readonly uint _blockSize;
    private readonly uint _totalChunks;
    private uint _chunksRead;
    private ushort _chunkType;
    private long _chunkOutputRemaining;
    private readonly byte[] _fillPattern = new byte[4];
    private int _fillOffset;
    private long _position;

    public AndroidSparseReadStream(string path)
    {
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        _reader = new BinaryReader(_stream);
        SparseHeader header = ReadHeader(_reader);
        _chunkHeaderSize = header.ChunkHeaderSize;
        _blockSize = header.BlockSize;
        _totalChunks = header.TotalChunks;
        Length = checked((long)header.TotalBlocks * header.BlockSize);
    }

    public static bool IsSparseImage(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> magic = stackalloc byte[4];
            return stream.Read(magic) == magic.Length
                && BinaryPrimitives.ReadUInt32LittleEndian(magic) == SparseMagic;
        }
        catch
        {
            return false;
        }
    }

    public static long ValidateAndGetExpandedLength(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);
        SparseHeader header = ReadHeader(reader);
        long expandedLength = checked((long)header.TotalBlocks * header.BlockSize);
        long parsedOutput = 0;

        for (uint i = 0; i < header.TotalChunks; i++)
        {
            if (stream.Position + header.ChunkHeaderSize > stream.Length)
                throw new InvalidDataException("sparse chunk 头超出文件范围");
            ushort chunkType = reader.ReadUInt16();
            reader.ReadUInt16();
            uint chunkBlocks = reader.ReadUInt32();
            uint totalSize = reader.ReadUInt32();
            if (header.ChunkHeaderSize > 12)
                stream.Seek(header.ChunkHeaderSize - 12, SeekOrigin.Current);
            if (totalSize < header.ChunkHeaderSize)
                throw new InvalidDataException("sparse chunk 大小无效");

            long dataSize = totalSize - header.ChunkHeaderSize;
            long outputSize = checked((long)chunkBlocks * header.BlockSize);
            switch (chunkType)
            {
                case RawChunk when dataSize == outputSize:
                    break;
                case FillChunk when dataSize == 4:
                    break;
                case DontCareChunk when dataSize == 0:
                    break;
                case Crc32Chunk when dataSize == 4:
                    outputSize = 0;
                    break;
                default:
                    throw new InvalidDataException($"无效的 sparse chunk: 0x{chunkType:X4}");
            }

            if (stream.Position + dataSize > stream.Length)
                throw new InvalidDataException("sparse chunk 数据超出文件范围");
            stream.Seek(dataSize, SeekOrigin.Current);
            parsedOutput = checked(parsedOutput + outputSize);
        }

        if (parsedOutput != expandedLength)
            throw new InvalidDataException($"sparse 展开大小不匹配: {parsedOutput} != {expandedLength}");
        return expandedLength;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (offset < 0 || count < 0 || offset + count > buffer.Length)
            throw new ArgumentOutOfRangeException();
        if (count == 0 || _position >= Length)
            return 0;

        int written = 0;
        while (written < count && _position < Length)
        {
            if (_chunkOutputRemaining == 0)
            {
                if (!MoveToNextOutputChunk())
                    throw new EndOfStreamException("sparse 数据提前结束");
            }

            int take = (int)Math.Min(count - written, _chunkOutputRemaining);
            Span<byte> destination = buffer.AsSpan(offset + written, take);
            switch (_chunkType)
            {
                case RawChunk:
                    ReadExact(_stream, destination);
                    break;
                case FillChunk:
                    for (int i = 0; i < destination.Length; i++)
                    {
                        destination[i] = _fillPattern[_fillOffset];
                        _fillOffset = (_fillOffset + 1) & 3;
                    }
                    break;
                case DontCareChunk:
                    destination.Clear();
                    break;
                default:
                    throw new InvalidDataException($"无法展开 sparse chunk: 0x{_chunkType:X4}");
            }

            _chunkOutputRemaining -= take;
            _position += take;
            written += take;
        }
        return written;
    }

    private bool MoveToNextOutputChunk()
    {
        while (_chunksRead < _totalChunks)
        {
            if (_stream.Position + _chunkHeaderSize > _stream.Length)
                throw new EndOfStreamException("sparse chunk 头数据不完整");

            ushort chunkType = _reader.ReadUInt16();
            _reader.ReadUInt16();
            uint chunkBlocks = _reader.ReadUInt32();
            uint totalSize = _reader.ReadUInt32();
            if (_chunkHeaderSize > 12)
                _stream.Seek(_chunkHeaderSize - 12, SeekOrigin.Current);
            _chunksRead++;

            if (totalSize < _chunkHeaderSize)
                throw new InvalidDataException("sparse chunk 大小无效");
            long dataSize = totalSize - _chunkHeaderSize;
            long outputSize = checked((long)chunkBlocks * _blockSize);

            switch (chunkType)
            {
                case RawChunk:
                    if (dataSize != outputSize)
                        throw new InvalidDataException("sparse RAW chunk 大小不匹配");
                    _chunkType = chunkType;
                    _chunkOutputRemaining = outputSize;
                    if (outputSize > 0)
                        return true;
                    break;
                case FillChunk:
                    if (dataSize != 4)
                        throw new InvalidDataException("sparse FILL chunk 大小不匹配");
                    ReadExact(_stream, _fillPattern);
                    _fillOffset = 0;
                    _chunkType = chunkType;
                    _chunkOutputRemaining = outputSize;
                    if (outputSize > 0)
                        return true;
                    break;
                case DontCareChunk:
                    if (dataSize != 0)
                        throw new InvalidDataException("sparse DONT_CARE chunk 大小不匹配");
                    _chunkType = chunkType;
                    _chunkOutputRemaining = outputSize;
                    if (outputSize > 0)
                        return true;
                    break;
                case Crc32Chunk:
                    if (dataSize != 4)
                        throw new InvalidDataException("sparse CRC32 chunk 大小不匹配");
                    _stream.Seek(dataSize, SeekOrigin.Current);
                    break;
                default:
                    throw new InvalidDataException($"无效的 sparse chunk: 0x{chunkType:X4}");
            }
        }
        return false;
    }

    private static SparseHeader ReadHeader(BinaryReader reader)
    {
        if (reader.BaseStream.Length < 28)
            throw new InvalidDataException("sparse 文件过小");
        uint magic = reader.ReadUInt32();
        if (magic != SparseMagic)
            throw new InvalidDataException("不是 Android sparse 镜像");
        ushort major = reader.ReadUInt16();
        reader.ReadUInt16();
        ushort fileHeaderSize = reader.ReadUInt16();
        ushort chunkHeaderSize = reader.ReadUInt16();
        uint blockSize = reader.ReadUInt32();
        uint totalBlocks = reader.ReadUInt32();
        uint totalChunks = reader.ReadUInt32();
        reader.ReadUInt32();

        if (major != 1)
            throw new InvalidDataException($"不支持的 sparse 版本: {major}");
        if (fileHeaderSize < 28 || chunkHeaderSize < 12 || blockSize == 0)
            throw new InvalidDataException("sparse 头参数无效");
        if (fileHeaderSize > 28)
            reader.BaseStream.Seek(fileHeaderSize - 28, SeekOrigin.Current);

        return new SparseHeader(chunkHeaderSize, blockSize, totalBlocks, totalChunks);
    }

    private static void ReadExact(Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer[total..]);
            if (read == 0)
                throw new EndOfStreamException("sparse 数据不完整");
            total += read;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _reader.Dispose();
            _stream.Dispose();
        }
        base.Dispose(disposing);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length { get; }
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private readonly record struct SparseHeader(
        ushort ChunkHeaderSize,
        uint BlockSize,
        uint TotalBlocks,
        uint TotalChunks);
}
