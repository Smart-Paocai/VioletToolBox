using System.Buffers;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32.SafeHandles;

namespace WpfApp1;

internal sealed record OfpSuperMergeProgress(double Percent, string? Message = null);

internal sealed record OfpSuperMergeResult(
    string OutputPath,
    int SegmentCount,
    long OutputLength,
    bool SourceIsSparse,
    bool OutputUsesNtfsSparse);

internal sealed record OfpSuperVariant(
    string DisplayName,
    IReadOnlyList<string> SegmentPaths);

internal static class OfpSegmentedSuperMerger
{
    private const uint SparseMagic = 0xED26FF3A;
    private const ushort RawChunk = 0xCAC1;
    private const ushort FillChunk = 0xCAC2;
    private const ushort DontCareChunk = 0xCAC3;
    private const ushort Crc32Chunk = 0xCAC4;
    private const uint FsctlSetSparse = 0x000900C4;
    private const int CopyBufferSize = 4 * 1024 * 1024;

    private static readonly Regex[] SegmentPatterns =
    [
        new(@"^super\.(?<index>\d+)\.[^.]+\.(?:img|bin|raw|sparse)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"^super[_\-.]?(?<index>\d+)(?:\.(?:img|bin|raw|sparse))?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"^super\.(?:img|bin|raw|sparse)[_\-.](?<index>\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
    ];

    public static IReadOnlyList<string> FindSegments(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new DirectoryNotFoundException("分段目录不存在。");

        List<SegmentFile> segments = FindSegments(directory, SearchOption.TopDirectoryOnly);
        if (segments.Count < 2)
            segments = FindSegments(directory, SearchOption.AllDirectories);

        return ValidateAndSortSegments(segments);
    }

    public static IReadOnlyList<string> ValidateAndSortSegments(IEnumerable<string> segmentPaths)
    {
        ArgumentNullException.ThrowIfNull(segmentPaths);
        var segments = new List<SegmentFile>();
        foreach (string path in segmentPaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("所选 Super 分段文件不存在。", path);
            if (!TryGetSegmentIndex(Path.GetFileName(path), out int index))
            {
                throw new InvalidDataException(
                    $"无法识别分段编号：{Path.GetFileName(path)}");
            }
            segments.Add(new SegmentFile(index, Path.GetFullPath(path)));
        }

        return ValidateAndSortSegments(segments);
    }

    public static IReadOnlyList<OfpSuperVariant> FindMappedVariants(
        IEnumerable<string> selectedSegmentPaths)
    {
        ArgumentNullException.ThrowIfNull(selectedSegmentPaths);
        string[] selectedPaths = selectedSegmentPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selectedPaths.Length < 2)
            return Array.Empty<OfpSuperVariant>();

        var selectedByName = selectedPaths
            .Select(path => new { Path = path, FileName = Path.GetFileName(path) })
            .Where(item => !string.IsNullOrWhiteSpace(item.FileName))
            .GroupBy(item => item.FileName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Path,
                StringComparer.OrdinalIgnoreCase);
        string? profilePath = FindProfilePath(selectedPaths[0]);
        if (profilePath == null)
            return Array.Empty<OfpSuperVariant>();

        XDocument document;
        try
        {
            document = XDocument.Load(profilePath, LoadOptions.None);
        }
        catch
        {
            return Array.Empty<OfpSuperVariant>();
        }

        var variants = new List<OfpSuperVariant>();
        var signatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement element in document.Descendants())
        {
            XAttribute[] segmentAttributes = element.Attributes()
                .Select(attribute => new
                {
                    Attribute = attribute,
                    Match = Regex.Match(
                        attribute.Name.LocalName,
                        @"^super(?<index>\d+)$",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                })
                .Where(item => item.Match.Success)
                .OrderBy(item => int.Parse(item.Match.Groups["index"].Value))
                .Select(item => item.Attribute)
                .ToArray();
            if (segmentAttributes.Length < 2)
                continue;

            var mappedPaths = new List<string>(segmentAttributes.Length);
            bool allSelected = true;
            foreach (XAttribute attribute in segmentAttributes)
            {
                string fileName = Path.GetFileName(attribute.Value.Trim());
                if (!selectedByName.TryGetValue(fileName, out string? selectedPath))
                {
                    allSelected = false;
                    break;
                }
                mappedPaths.Add(selectedPath);
            }
            if (!allSelected)
                continue;

            IReadOnlyList<string> validatedPaths;
            try
            {
                validatedPaths = ValidateAndSortSegments(mappedPaths);
            }
            catch
            {
                continue;
            }

            string signature = string.Join(
                "|",
                validatedPaths.Select(path => Path.GetFileName(path).ToUpperInvariant()));
            if (!signatures.Add(signature))
                continue;

            string variantName =
                element.Attribute("text")?.Value?.Trim()
                ?? element.Attribute("id")?.Value?.Trim()
                ?? $"版本 {variants.Count + 1}";
            variants.Add(new OfpSuperVariant(variantName, validatedPaths));
        }

        return variants;
    }

    private static IReadOnlyList<string> ValidateAndSortSegments(List<SegmentFile> segments)
    {
        if (segments.Count < 2)
            throw new InvalidDataException("请至少选择两个 Super 分段文件。");

        IGrouping<int, SegmentFile>? duplicate = segments
            .GroupBy(segment => segment.Index)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
        {
            string names = string.Join("、", duplicate.Select(item => Path.GetFileName(item.Path)));
            throw new InvalidDataException($"发现重复的 super 分段编号 {duplicate.Key}: {names}");
        }

        segments.Sort((left, right) => left.Index.CompareTo(right.Index));
        int firstIndex = segments[0].Index;
        if (firstIndex is not 0 and not 1)
            throw new InvalidDataException($"super 分段编号应从 0 或 1 开始，当前从 {firstIndex} 开始。");

        for (int i = 0; i < segments.Count; i++)
        {
            int expected = firstIndex + i;
            if (segments[i].Index != expected)
                throw new InvalidDataException($"super 分段不连续，缺少编号 {expected}。");
        }

        return segments.Select(segment => segment.Path).ToArray();
    }

    public static Task<OfpSuperMergeResult> MergeAsync(
        string directory,
        string outputPath,
        IProgress<OfpSuperMergeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => Merge(directory, outputPath, progress, cancellationToken),
            cancellationToken);
    }

    public static Task<OfpSuperMergeResult> MergeAsync(
        IReadOnlyList<string> segmentPaths,
        string outputPath,
        IProgress<OfpSuperMergeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => Merge(segmentPaths, outputPath, progress, cancellationToken),
            cancellationToken);
    }

    private static OfpSuperMergeResult Merge(
        string directory,
        string outputPath,
        IProgress<OfpSuperMergeProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Merge(FindSegments(directory), outputPath, progress, cancellationToken);
    }

    private static OfpSuperMergeResult Merge(
        IReadOnlyList<string> segmentPaths,
        string outputPath,
        IProgress<OfpSuperMergeProgress>? progress,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> segments = ValidateAndSortSegments(segmentPaths);
        outputPath = NormalizeOutputPath(outputPath);
        string fullOutputPath = Path.GetFullPath(outputPath);

        if (segments.Any(path =>
                string.Equals(Path.GetFullPath(path), fullOutputPath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("输出文件不能与任意 super 分段文件相同。");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
        bool sourceIsSparse = IsSparseImage(segments[0]);
        for (int i = 1; i < segments.Count; i++)
        {
            if (IsSparseImage(segments[i]) != sourceIsSparse)
                throw new InvalidDataException("检测到 Sparse 与 Raw 混合分段，无法安全合并。");
        }

        progress?.Report(new OfpSuperMergeProgress(
            0,
            $"检测到 {segments.Count} 个分段，格式：{(sourceIsSparse ? "Android Sparse" : "Raw")}"));

        string temporaryPath = fullOutputPath + ".partial";
        TryDelete(temporaryPath);

        try
        {
            return sourceIsSparse
                ? MergeSparseSegments(segments, fullOutputPath, temporaryPath, progress, cancellationToken)
                : MergeRawSegments(segments, fullOutputPath, temporaryPath, progress, cancellationToken);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private static OfpSuperMergeResult MergeSparseSegments(
        IReadOnlyList<string> segments,
        string outputPath,
        string temporaryPath,
        IProgress<OfpSuperMergeProgress>? progress,
        CancellationToken cancellationToken)
    {
        var infos = new List<SparseSegmentInfo>(segments.Count);
        foreach (string segment in segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            infos.Add(InspectSparseSegment(segment));
        }

        SparseSegmentInfo first = infos[0];
        foreach (SparseSegmentInfo info in infos.Skip(1))
        {
            if (info.BlockSize != first.BlockSize || info.ExpandedLength != first.ExpandedLength)
            {
                throw new InvalidDataException(
                    $"{Path.GetFileName(info.Path)} 的 Sparse 布局与首个分段不一致。");
            }
        }

        long totalInputBytes = infos.Sum(info => new FileInfo(info.Path).Length);
        long estimatedWrittenBytes = Math.Min(
            first.ExpandedLength,
            infos.Sum(info => info.MaterializedBytes));

        bool ntfsSparse;
        using (var output = new FileStream(
                   temporaryPath,
                   FileMode.Create,
                   FileAccess.ReadWrite,
                   FileShare.None,
                   CopyBufferSize,
                   FileOptions.RandomAccess))
        {
            ntfsSparse = TryEnableSparseFile(output.SafeFileHandle);
            EnsureOutputCapacity(
                outputPath,
                ntfsSparse ? estimatedWrittenBytes : first.ExpandedLength);
            output.SetLength(first.ExpandedLength);

            long completedInputBytes = 0;
            for (int i = 0; i < infos.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SparseSegmentInfo info = infos[i];
                progress?.Report(new OfpSuperMergeProgress(
                    Percent(completedInputBytes, totalInputBytes),
                    $"正在合并 [{i + 1}/{infos.Count}] {Path.GetFileName(info.Path)}"));

                ApplySparseSegment(
                    info,
                    output,
                    completedInputBytes,
                    totalInputBytes,
                    progress,
                    cancellationToken);
                completedInputBytes += new FileInfo(info.Path).Length;
            }

            output.Flush(true);
        }

        ReplaceOutput(temporaryPath, outputPath);
        progress?.Report(new OfpSuperMergeProgress(100, "分段 Super 合并完成"));
        return new OfpSuperMergeResult(
            outputPath,
            segments.Count,
            first.ExpandedLength,
            true,
            ntfsSparse);
    }

    private static OfpSuperMergeResult MergeRawSegments(
        IReadOnlyList<string> segments,
        string outputPath,
        string temporaryPath,
        IProgress<OfpSuperMergeProgress>? progress,
        CancellationToken cancellationToken)
    {
        long outputLength = segments.Sum(path => new FileInfo(path).Length);
        EnsureOutputCapacity(outputPath, outputLength);

        long completedBytes = 0;
        using (var output = new FileStream(
                   temporaryPath,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None,
                   CopyBufferSize,
                   FileOptions.SequentialScan))
        {
            for (int i = 0; i < segments.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string segment = segments[i];
                progress?.Report(new OfpSuperMergeProgress(
                    Percent(completedBytes, outputLength),
                    $"正在拼接 [{i + 1}/{segments.Count}] {Path.GetFileName(segment)}"));

                using var input = new FileStream(
                    segment,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    CopyBufferSize,
                    FileOptions.SequentialScan);
                CopyExact(
                    input,
                    output,
                    input.Length,
                    bytes =>
                    {
                        completedBytes += bytes;
                        progress?.Report(new OfpSuperMergeProgress(
                            Percent(completedBytes, outputLength)));
                    },
                    cancellationToken);
            }

            output.Flush(true);
        }

        ReplaceOutput(temporaryPath, outputPath);
        progress?.Report(new OfpSuperMergeProgress(100, "分段 Super 合并完成"));
        return new OfpSuperMergeResult(outputPath, segments.Count, outputLength, false, false);
    }

    private static SparseSegmentInfo InspectSparseSegment(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream);
        SparseHeader header = ReadSparseHeader(reader);
        long parsedOutputBytes = 0;
        long materializedBytes = 0;

        for (uint chunkIndex = 0; chunkIndex < header.TotalChunks; chunkIndex++)
        {
            SparseChunk chunk = ReadSparseChunk(reader, header);
            long outputBytes = checked((long)chunk.BlockCount * header.BlockSize);
            ValidateChunk(chunk, outputBytes);

            if (chunk.Type != Crc32Chunk)
                parsedOutputBytes = checked(parsedOutputBytes + outputBytes);
            if (chunk.Type is RawChunk or FillChunk)
                materializedBytes = checked(materializedBytes + outputBytes);

            SeekForward(stream, chunk.DataSize);
        }

        long expandedLength = checked((long)header.TotalBlocks * header.BlockSize);
        if (parsedOutputBytes != expandedLength)
        {
            throw new InvalidDataException(
                $"{Path.GetFileName(path)} 的 Sparse 展开大小不匹配。");
        }

        return new SparseSegmentInfo(
            path,
            header.BlockSize,
            expandedLength,
            materializedBytes);
    }

    private static void ApplySparseSegment(
        SparseSegmentInfo info,
        FileStream output,
        long completedInputBytes,
        long totalInputBytes,
        IProgress<OfpSuperMergeProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var input = new FileStream(
            info.Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.SequentialScan);
        using var reader = new BinaryReader(input);
        SparseHeader header = ReadSparseHeader(reader);
        output.Position = 0;

        for (uint chunkIndex = 0; chunkIndex < header.TotalChunks; chunkIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SparseChunk chunk = ReadSparseChunk(reader, header);
            long outputBytes = checked((long)chunk.BlockCount * header.BlockSize);
            ValidateChunk(chunk, outputBytes);

            switch (chunk.Type)
            {
                case RawChunk:
                    CopyExact(
                        input,
                        output,
                        outputBytes,
                        _ => ReportSparseProgress(
                            progress,
                            completedInputBytes,
                            input.Position,
                            totalInputBytes),
                        cancellationToken);
                    break;
                case FillChunk:
                    uint fillValue = reader.ReadUInt32();
                    if (fillValue == 0)
                    {
                        output.Seek(outputBytes, SeekOrigin.Current);
                    }
                    else
                    {
                        WriteFill(output, fillValue, outputBytes, cancellationToken);
                    }
                    break;
                case DontCareChunk:
                    output.Seek(outputBytes, SeekOrigin.Current);
                    break;
                case Crc32Chunk:
                    SeekForward(input, chunk.DataSize);
                    break;
            }

            ReportSparseProgress(
                progress,
                completedInputBytes,
                input.Position,
                totalInputBytes);
        }

        if (output.Position != info.ExpandedLength)
        {
            throw new InvalidDataException(
                $"{Path.GetFileName(info.Path)} 合并后的块位置不完整。");
        }
    }

    private static SparseHeader ReadSparseHeader(BinaryReader reader)
    {
        Stream stream = reader.BaseStream;
        if (stream.Length < 28)
            throw new InvalidDataException("Sparse 分段文件过小。");

        uint magic = reader.ReadUInt32();
        ushort major = reader.ReadUInt16();
        reader.ReadUInt16();
        ushort fileHeaderSize = reader.ReadUInt16();
        ushort chunkHeaderSize = reader.ReadUInt16();
        uint blockSize = reader.ReadUInt32();
        uint totalBlocks = reader.ReadUInt32();
        uint totalChunks = reader.ReadUInt32();
        reader.ReadUInt32();

        if (magic != SparseMagic)
            throw new InvalidDataException("不是 Android Sparse 镜像。");
        if (major != 1)
            throw new InvalidDataException($"不支持的 Sparse 主版本：{major}。");
        if (fileHeaderSize < 28 || chunkHeaderSize < 12 || blockSize == 0)
            throw new InvalidDataException("Sparse 文件头参数无效。");
        if (fileHeaderSize > stream.Length)
            throw new InvalidDataException("Sparse 文件头超出文件范围。");

        stream.Position = fileHeaderSize;
        return new SparseHeader(chunkHeaderSize, blockSize, totalBlocks, totalChunks);
    }

    private static SparseChunk ReadSparseChunk(BinaryReader reader, SparseHeader header)
    {
        Stream stream = reader.BaseStream;
        if (stream.Position + header.ChunkHeaderSize > stream.Length)
            throw new EndOfStreamException("Sparse chunk 头数据不完整。");

        ushort type = reader.ReadUInt16();
        reader.ReadUInt16();
        uint blockCount = reader.ReadUInt32();
        uint totalSize = reader.ReadUInt32();
        if (header.ChunkHeaderSize > 12)
            SeekForward(stream, header.ChunkHeaderSize - 12);
        if (totalSize < header.ChunkHeaderSize)
            throw new InvalidDataException("Sparse chunk 大小无效。");

        long dataSize = totalSize - header.ChunkHeaderSize;
        if (stream.Position + dataSize > stream.Length)
            throw new EndOfStreamException("Sparse chunk 数据超出文件范围。");
        return new SparseChunk(type, blockCount, dataSize);
    }

    private static void ValidateChunk(SparseChunk chunk, long outputBytes)
    {
        bool valid = chunk.Type switch
        {
            RawChunk => chunk.DataSize == outputBytes,
            FillChunk => chunk.DataSize == 4,
            DontCareChunk => chunk.DataSize == 0,
            Crc32Chunk => chunk.DataSize == 4 && chunk.BlockCount == 0,
            _ => false
        };
        if (!valid)
            throw new InvalidDataException($"无效的 Sparse chunk：0x{chunk.Type:X4}。");
    }

    private static void CopyExact(
        Stream input,
        Stream output,
        long length,
        Action<int>? onCopied,
        CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            long remaining = length;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int requested = (int)Math.Min(remaining, buffer.Length);
                int read = input.Read(buffer, 0, requested);
                if (read <= 0)
                    throw new EndOfStreamException("分段文件数据提前结束。");
                output.Write(buffer, 0, read);
                remaining -= read;
                onCopied?.Invoke(read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void WriteFill(
        Stream output,
        uint fillValue,
        long length,
        CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            Span<byte> pattern = stackalloc byte[4];
            BitConverter.TryWriteBytes(pattern, fillValue);
            int usableLength = buffer.Length - buffer.Length % 4;
            for (int offset = 0; offset < usableLength; offset += 4)
                pattern.CopyTo(buffer.AsSpan(offset, 4));

            long remaining = length;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = (int)Math.Min(remaining, usableLength);
                output.Write(buffer, 0, count);
                remaining -= count;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void ReportSparseProgress(
        IProgress<OfpSuperMergeProgress>? progress,
        long completedInputBytes,
        long currentInputPosition,
        long totalInputBytes)
    {
        progress?.Report(new OfpSuperMergeProgress(
            Percent(completedInputBytes + currentInputPosition, totalInputBytes)));
    }

    private static bool IsSparseImage(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);
            return stream.Length >= sizeof(uint) && reader.ReadUInt32() == SparseMagic;
        }
        catch
        {
            return false;
        }
    }

    private static List<SegmentFile> FindSegments(string directory, SearchOption searchOption)
    {
        var result = new List<SegmentFile>();
        foreach (string path in Directory.EnumerateFiles(directory, "super*", searchOption))
        {
            if (TryGetSegmentIndex(Path.GetFileName(path), out int index))
                result.Add(new SegmentFile(index, path));
        }
        return result;
    }

    private static bool TryGetSegmentIndex(string fileName, out int index)
    {
        foreach (Regex pattern in SegmentPatterns)
        {
            Match match = pattern.Match(fileName);
            if (match.Success &&
                int.TryParse(match.Groups["index"].Value, out index))
            {
                return true;
            }
        }

        index = -1;
        return false;
    }

    private static string? FindProfilePath(string segmentPath)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(segmentPath));
        for (int level = 0; level < 3 && !string.IsNullOrWhiteSpace(directory); level++)
        {
            string candidate = Path.Combine(directory, "ProFile.xml");
            if (File.Exists(candidate))
                return candidate;
            directory = Directory.GetParent(directory)?.FullName;
        }
        return null;
    }

    private static string NormalizeOutputPath(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("请设置合并后的输出文件。", nameof(outputPath));
        string trimmed = outputPath.Trim();
        return string.IsNullOrEmpty(Path.GetExtension(trimmed)) ? trimmed + ".img" : trimmed;
    }

    private static void EnsureOutputCapacity(string outputPath, long requiredBytes)
    {
        string? root = Path.GetPathRoot(Path.GetFullPath(outputPath));
        if (string.IsNullOrWhiteSpace(root))
            return;

        try
        {
            var drive = new DriveInfo(root);
            if (!drive.IsReady)
                return;
            if (drive.DriveFormat.Equals("FAT32", StringComparison.OrdinalIgnoreCase) &&
                requiredBytes > uint.MaxValue)
            {
                throw new IOException("FAT32 不支持超过 4GB 的完整 Super 文件，请选择 NTFS 或 exFAT 分区。");
            }

            const long reserveBytes = 128L * 1024 * 1024;
            if (drive.AvailableFreeSpace < requiredBytes + reserveBytes)
            {
                throw new IOException(
                    $"输出磁盘空间不足，至少需要约 {FormatBytes(requiredBytes + reserveBytes)} 可用空间。");
            }
        }
        catch (ArgumentException)
        {
            // UNC 和部分虚拟路径无法通过 DriveInfo 判断，交由实际写入结果处理。
        }
    }

    private static bool TryEnableSparseFile(SafeFileHandle handle)
    {
        if (!OperatingSystem.IsWindows())
            return false;
        return DeviceIoControl(
            handle,
            FsctlSetSparse,
            IntPtr.Zero,
            0,
            IntPtr.Zero,
            0,
            out _,
            IntPtr.Zero);
    }

    private static void SeekForward(Stream stream, long bytes)
    {
        if (bytes < 0 || stream.Position + bytes > stream.Length)
            throw new EndOfStreamException("分段文件数据超出范围。");
        stream.Seek(bytes, SeekOrigin.Current);
    }

    private static void ReplaceOutput(string temporaryPath, string outputPath)
    {
        File.Move(temporaryPath, outputPath, true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static double Percent(long completed, long total)
    {
        return total <= 0 ? 0 : Math.Clamp(completed * 100d / total, 0, 100);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        IntPtr inputBuffer,
        uint inputBufferSize,
        IntPtr outputBuffer,
        uint outputBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

    private sealed record SegmentFile(int Index, string Path);

    private sealed record SparseSegmentInfo(
        string Path,
        uint BlockSize,
        long ExpandedLength,
        long MaterializedBytes);

    private readonly record struct SparseHeader(
        ushort ChunkHeaderSize,
        uint BlockSize,
        uint TotalBlocks,
        uint TotalChunks);

    private readonly record struct SparseChunk(
        ushort Type,
        uint BlockCount,
        long DataSize);
}
