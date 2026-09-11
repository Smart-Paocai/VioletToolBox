using System.Buffers;
using System.Buffers.Binary;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using ChromeosUpdateEngine;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Microsoft.Win32.SafeHandles;

namespace Payload_Dumper_C_.Core;

public sealed record PayloadContext(
    IRandomAccessReader Reader,
    DeltaArchiveManifest Manifest,
    long BlockSize,
    long DataOffset,
    long PayloadSize);

public sealed record PartitionInfo(
    string Name,
    long SizeBytes,
    string SizeReadable);

public static class PayloadProcessing
{
    private static async ValueTask<bool> IsZipAsync(IRandomAccessReader reader, CancellationToken cancellationToken)
    {
        var sig = await reader.ReadExactlyAsync(0, 4, cancellationToken).ConfigureAwait(false);
        return sig.Length == 4 &&
               sig[0] == (byte)'P' &&
               sig[1] == (byte)'K' &&
               (sig[2] == 3 || sig[2] == 5 || sig[2] == 7);
    }

    internal static async Task<(string TempPath, long PayloadSize)> ExtractPayloadFromLocalZipAsync(
        string zipPath,
        Action<string>? log,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var zipFs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var zip = new ZipArchive(zipFs, ZipArchiveMode.Read, leaveOpen: false);

            ZipArchiveEntry? entry = zip.GetEntry("payload.bin");
            if (entry is null)
            {
                foreach (var e in zip.Entries)
                {
                    if (e.FullName.EndsWith("payload.bin", StringComparison.OrdinalIgnoreCase))
                    {
                        entry = e;
                        break;
                    }
                }
            }

            if (entry is null) throw new InvalidDataException("OTA ZIP 内没有 payload.bin");

            long totalSize = entry.Length;
            log?.Invoke($"正在从 ZIP 解压 payload.bin ({ToReadableSize(totalSize)})，可能需要几分钟...");
            progress?.Report(0);

            string tempPath = Path.Combine(System.IO.Path.GetTempPath(), $"payload_{Guid.NewGuid():N}.bin");
            var buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
            try
            {
                using var inStream = entry.Open();
                using var outFs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.SequentialScan);

                long readTotal = 0;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int read = inStream.Read(buffer, 0, buffer.Length);
                    if (read == 0) break;
                    outFs.Write(buffer, 0, read);
                    readTotal += read;
                    if (totalSize > 0)
                    {
                        double percent = Math.Min(100d, readTotal * 100d / totalSize);
                        progress?.Report(percent);
                    }
                }
                outFs.Flush();
            }
            catch
            {
                try { File.Delete(tempPath); } catch { }
                throw;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            progress?.Report(100);
            log?.Invoke("payload.bin 解压完成");
            return (tempPath, totalSize);
        }, cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<IRandomAccessReader> OpenSourceAsync(
        string pathOrUrl,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        if (File.Exists(pathOrUrl))
        {
            return new LocalFileReader(pathOrUrl);
        }

        if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return await HttpRangeReader.CreateAsync(pathOrUrl, headers, cancellationToken).ConfigureAwait(false);
        }

        throw new FileNotFoundException($"找不到文件或不是有效 URL: {pathOrUrl}");
    }

    public static async ValueTask<(IRandomAccessReader PayloadReader, long PayloadSize)> OpenPayloadReaderAsync(
        IRandomAccessReader source,
        CancellationToken cancellationToken,
        Action<string>? log = null,
        IProgress<double>? extractProgress = null,
        bool eagerExtract = true)
    {
        bool isZip = await IsZipAsync(source, cancellationToken).ConfigureAwait(false);

        try
        {
            var (off, size) = await ZipStoredEntryLocator.TryGetStoredEntryAsync(source, "payload.bin", cancellationToken).ConfigureAwait(false);
            return (new SubrangeReader(source, off, size), size);
        }
        catch
        {
            if (isZip)
            {
                try
                {
                    var (off2, size2) = await ZipStoredEntryLocator.TryGetStoredEntryEndsWithAsync(source, "payload.bin", cancellationToken).ConfigureAwait(false);
                    return (new SubrangeReader(source, off2, size2), size2);
                }
                catch
                {
                }

                if (source is LocalFileReader lf)
                {
                    if (!eagerExtract)
                    {
                        var lazy = new ZipPayloadLazyReader(lf.Path, log);
                        long lazyPayloadSize = await lazy.GetSizeAsync(cancellationToken).ConfigureAwait(false);
                        return (lazy, lazyPayloadSize);
                    }

                    var (tempPath, payloadSize) = await ExtractPayloadFromLocalZipAsync(lf.Path, log, extractProgress, cancellationToken).ConfigureAwait(false);
                    return (new TempFileReader(tempPath), payloadSize);
                }

                throw new InvalidDataException("检测到输入是 ZIP，但无法定位 payload.bin。请先下载并解压得到 payload.bin 再导入。");
            }

            long sz = await source.GetSizeAsync(cancellationToken).ConfigureAwait(false);
            return (source, sz);
        }
    }

    public static async ValueTask<PayloadContext> ReadManifestAsync(IRandomAccessReader payloadReader, CancellationToken cancellationToken)
    {
        long payloadSize = await payloadReader.GetSizeAsync(cancellationToken).ConfigureAwait(false);
        var header = await payloadReader.ReadExactlyAsync(0, 24, cancellationToken).ConfigureAwait(false);

        if (!header.AsSpan(0, 4).SequenceEqual("CrAU"u8))
        {
            if (header[0] == (byte)'P' && header[1] == (byte)'K')
            {
                throw new InvalidDataException("检测到 ZIP 文件头(PK)。请确认选择的是 payload.bin，或选择包含 payload.bin 的 OTA ZIP。");
            }
            throw new InvalidDataException("payload.bin magic 不正确");
        }

        ulong fileFormatVersion = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(4, 8));
        if (fileFormatVersion != 2)
        {
            throw new InvalidDataException($"不支持的 payload 版本: {fileFormatVersion}");
        }

        ulong manifestSize = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(12, 8));
        uint metadataSignatureSize = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(20, 4));

        if (manifestSize > int.MaxValue)
        {
            throw new InvalidDataException($"manifest 过大: {manifestSize}");
        }

        long fp = 24;
        var manifestBytes = await payloadReader.ReadExactlyAsync(fp, (int)manifestSize, cancellationToken).ConfigureAwait(false);
        fp += (long)manifestSize;

        if (metadataSignatureSize > 0)
        {
            var _ = await payloadReader.ReadExactlyAsync(fp, checked((int)metadataSignatureSize), cancellationToken).ConfigureAwait(false);
            fp += metadataSignatureSize;
        }

        long dataOffset = fp;

        var manifest = new DeltaArchiveManifest();
        manifest.MergeFrom(manifestBytes);

        return new PayloadContext(payloadReader, manifest, (long)manifest.BlockSize, dataOffset, payloadSize);
    }

    public static IReadOnlyList<PartitionInfo> GetPartitions(PayloadContext ctx)
    {
        var results = new List<PartitionInfo>(ctx.Manifest.Partitions.Count);
        foreach (var p in ctx.Manifest.Partitions)
        {
            long endBlock = 0;
            foreach (var op in p.Operations)
            {
                foreach (var ext in op.DstExtents)
                {
                    long start = (long)ext.StartBlock;
                    long num = (long)ext.NumBlocks;
                    long e = start + num;
                    if (e > endBlock) endBlock = e;
                }
            }

            long bytes = checked(endBlock * ctx.BlockSize);
            results.Add(new PartitionInfo(p.PartitionName, bytes, ToReadableSize(bytes)));
        }

        results.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return results;
    }

    public static async Task<(string OutputPath, string Text)> ExtractAndroidMetadataAsync(
        IRandomAccessReader source,
        string outDir,
        CancellationToken cancellationToken)
    {
        byte[] data = await ReadAndroidMetadataBytesAsync(source, cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(outDir);
        string outputPath = Path.Combine(outDir, "metadata");
        await File.WriteAllBytesAsync(outputPath, data, cancellationToken).ConfigureAwait(false);

        return (outputPath, DecodeAndroidMetadata(data));
    }

    public static async Task<string> ReadAndroidMetadataTextAsync(
        IRandomAccessReader source,
        CancellationToken cancellationToken)
    {
        byte[] data = await ReadAndroidMetadataBytesAsync(source, cancellationToken).ConfigureAwait(false);
        return DecodeAndroidMetadata(data);
    }

    private static async Task<byte[]> ReadAndroidMetadataBytesAsync(
        IRandomAccessReader source,
        CancellationToken cancellationToken)
    {
        try
        {
            var (off, size) = await ZipStoredEntryLocator.TryGetStoredEntryAsync(source, "META-INF/com/android/metadata", cancellationToken)
                .ConfigureAwait(false);

            if (size < 0 || size > int.MaxValue) throw new InvalidDataException($"metadata 大小异常: {size}");
            return await source.ReadExactlyAsync(off, (int)size, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            bool isZip = await IsZipAsync(source, cancellationToken).ConfigureAwait(false);
            if (!isZip) throw;

            if (source is not LocalFileReader lf)
            {
                throw new InvalidDataException("远程 ZIP 的 metadata 可能被压缩，暂不支持直接提取。请先下载到本地再操作。");
            }

            return await Task.Run(() =>
            {
                using var zipFs = new FileStream(lf.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var zip = new ZipArchive(zipFs, ZipArchiveMode.Read, leaveOpen: false);
                var entry = zip.GetEntry("META-INF/com/android/metadata");
                if (entry is null) throw new InvalidDataException("OTA ZIP 内没有 META-INF/com/android/metadata");

                using var s = entry.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string DecodeAndroidMetadata(byte[] data)
    {
        try
        {
            return System.Text.Encoding.UTF8.GetString(data);
        }
        catch
        {
            return System.Text.Encoding.Latin1.GetString(data);
        }
    }

    public static async Task ExtractPartitionsAsync(
        PayloadContext ctx,
        IReadOnlySet<string> partitions,
        string outDir,
        int workers,
        Action<string> log,
        IProgress<(long DoneBytes, long TotalBytes)> progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outDir);

        var partitionUpdates = ctx.Manifest.Partitions.Where(p => partitions.Contains(p.PartitionName)).ToList();
        if (partitionUpdates.Count == 0) return;

        long totalBytes = partitionUpdates.Sum(p => p.Operations.Sum(op => ComputeOperationOutputSizeBytes(ctx, op)));
        long doneBytes = 0;
        progress.Report((doneBytes, totalBytes));

        void ReportWrittenBytes(long bytes)
        {
            if (bytes <= 0) return;
            long value = Interlocked.Add(ref doneBytes, bytes);
            progress.Report((Math.Min(value, totalBytes), totalBytes));
        }

        foreach (var partition in partitionUpdates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string name = partition.PartitionName;
            string outPath = Path.Combine(outDir, $"{name}.img");
            string partialPath = $"{outPath}.partial";

            long outputSize = ComputePartitionOutputSizeBytes(ctx, partition);
            log($"开始导出: {name} ({ToReadableSize(outputSize)})");

            TryDeleteFile(partialPath);
            try
            {
                await using (var outStream = new FileStream(partialPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 1 << 20, FileOptions.Asynchronous | FileOptions.RandomAccess))
                {
                    outStream.SetLength(outputSize);
                    var handle = outStream.SafeFileHandle;

                    using var sem = new SemaphoreSlim(Math.Max(1, workers), Math.Max(1, workers));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    var opTasks = new List<Task>(partition.Operations.Count);

                    try
                    {
                        foreach (var op in partition.Operations)
                        {
                            await sem.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                            opTasks.Add(Task.Run(async () =>
                            {
                                try
                                {
                                    await ApplyOperationAsync(ctx, op, handle, ReportWrittenBytes, linkedCts.Token).ConfigureAwait(false);
                                }
                                catch
                                {
                                    linkedCts.Cancel();
                                    throw;
                                }
                                finally
                                {
                                    sem.Release();
                                }
                            }));
                        }

                        await Task.WhenAll(opTasks).ConfigureAwait(false);
                    }
                    catch
                    {
                        linkedCts.Cancel();
                        try
                        {
                            await Task.WhenAll(opTasks).ConfigureAwait(false);
                        }
                        catch
                        {
                        }
                        throw;
                    }
                }

                File.Move(partialPath, outPath, true);
                log($"完成导出: {name}");
            }
            catch
            {
                TryDeleteFile(partialPath);
                throw;
            }
        }

        progress.Report((totalBytes, totalBytes));
    }

    private static long ComputeOperationOutputSizeBytes(PayloadContext ctx, InstallOperation op)
    {
        return op.DstExtents.Sum(ext => checked((long)ext.NumBlocks * ctx.BlockSize));
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    private static long ComputePartitionOutputSizeBytes(PayloadContext ctx, PartitionUpdate p)
    {
        long endBlock = 0;
        foreach (var op in p.Operations)
        {
            foreach (var ext in op.DstExtents)
            {
                long start = (long)ext.StartBlock;
                long num = (long)ext.NumBlocks;
                long e = start + num;
                if (e > endBlock) endBlock = e;
            }
        }
        return checked(endBlock * ctx.BlockSize);
    }

    private static async Task ApplyOperationAsync(
        PayloadContext ctx,
        InstallOperation op,
        SafeFileHandle outHandle,
        Action<long> reportWrittenBytes,
        CancellationToken cancellationToken)
    {
        long dataOffset = checked(ctx.DataOffset + (long)op.DataOffset);
        long dataLengthLong = (long)op.DataLength;
        if (dataLengthLong < 0 || dataLengthLong > int.MaxValue) throw new InvalidDataException($"data_length 过大: {dataLengthLong}");
        int dataLength = (int)dataLengthLong;

        byte[] data = dataLength == 0
            ? Array.Empty<byte>()
            : await ctx.Reader.ReadExactlyAsync(dataOffset, dataLength, cancellationToken).ConfigureAwait(false);

        if (op.DataSha256Hash is { Length: > 0 })
        {
            var h = SHA256.HashData(data);
            if (!h.AsSpan().SequenceEqual(op.DataSha256Hash.Span))
            {
                throw new InvalidDataException("operation data sha256 校验失败");
            }
        }

        if (op.SrcSha256Hash is { Length: > 0 }) throw new NotSupportedException("暂不支持差分 OTA（包含 src_sha256_hash）");

        switch (op.Type)
        {
            case InstallOperation.Types.Type.Replace:
                await WriteAcrossExtentsAsync(outHandle, ctx.BlockSize, op.DstExtents, data, reportWrittenBytes, cancellationToken).ConfigureAwait(false);
                break;
            case InstallOperation.Types.Type.ReplaceBz:
                {
                    var dec = Decompression.BZip2(data);
                    await WriteAcrossExtentsAsync(outHandle, ctx.BlockSize, op.DstExtents, dec, reportWrittenBytes, cancellationToken).ConfigureAwait(false);
                    break;
                }
            case InstallOperation.Types.Type.ReplaceXz:
                {
                    var dec = Decompression.Xz(data);
                    await WriteAcrossExtentsAsync(outHandle, ctx.BlockSize, op.DstExtents, dec, reportWrittenBytes, cancellationToken).ConfigureAwait(false);
                    break;
                }
            case InstallOperation.Types.Type.Zero:
                await WriteZeroAsync(outHandle, ctx.BlockSize, op.DstExtents, reportWrittenBytes, cancellationToken).ConfigureAwait(false);
                break;
            case InstallOperation.Types.Type.Zstd:
                {
                    var dec = Decompression.Zstd(data);
                    await WriteAcrossExtentsAsync(outHandle, ctx.BlockSize, op.DstExtents, dec, reportWrittenBytes, cancellationToken).ConfigureAwait(false);
                    break;
                }
            case InstallOperation.Types.Type.SourceCopy:
            case InstallOperation.Types.Type.SourceBsdiff:
            case InstallOperation.Types.Type.BrotliBsdiff:
                throw new NotSupportedException("暂不支持差分 OTA（SOURCE_* 操作）");
            default:
                throw new NotSupportedException($"暂不支持的 operation: {op.Type}");
        }
    }

    private static async Task<byte[]> ReadAcrossExtentsAsync(
        IRandomAccessReader reader,
        long blockSize,
        RepeatedField<Extent> extents,
        CancellationToken cancellationToken)
    {
        long total = extents.Sum(e => checked((long)e.NumBlocks * blockSize));
        if (total > int.MaxValue) throw new InvalidDataException($"extent 数据过大: {total}");

        var result = new byte[(int)total];
        int p = 0;
        foreach (var ext in extents)
        {
            int bytes = checked((int)((long)ext.NumBlocks * blockSize));
            long off = checked((long)ext.StartBlock * blockSize);
            int readTotal = 0;
            while (readTotal < bytes)
            {
                int r = await reader.ReadAsync(off + readTotal, result.AsMemory(p + readTotal, bytes - readTotal), cancellationToken).ConfigureAwait(false);
                if (r == 0) throw new EndOfStreamException();
                readTotal += r;
            }
            p += bytes;
        }
        return result;
    }

    private static async Task WriteAcrossExtentsAsync(
        SafeFileHandle outHandle,
        long blockSize,
        RepeatedField<Extent> dstExtents,
        byte[] data,
        Action<long> reportWrittenBytes,
        CancellationToken cancellationToken)
    {
        long expected = dstExtents.Sum(e => checked((long)e.NumBlocks * blockSize));
        if (data.LongLength > expected)
        {
            data = data.AsSpan(0, (int)expected).ToArray();
        }

        const int Chunk = 1 << 20;
        int p = 0;
        foreach (var ext in dstExtents)
        {
            long bytes = checked((long)ext.NumBlocks * blockSize);
            long off = checked((long)ext.StartBlock * blockSize);
            int canWrite = (int)Math.Min(bytes, data.Length - p);
            if (canWrite <= 0) break;
            int extentWritten = 0;
            while (extentWritten < canWrite)
            {
                int n = Math.Min(Chunk, canWrite - extentWritten);
                await RandomAccess.WriteAsync(outHandle, data.AsMemory(p + extentWritten, n), off + extentWritten, cancellationToken).ConfigureAwait(false);
                extentWritten += n;
                reportWrittenBytes(n);
            }
            p += extentWritten;
        }
    }

    private static async Task WriteZeroAsync(
        SafeFileHandle outHandle,
        long blockSize,
        RepeatedField<Extent> dstExtents,
        Action<long> reportWrittenBytes,
        CancellationToken cancellationToken)
    {
        const int Chunk = 1 << 20;
        byte[] zeros = ArrayPool<byte>.Shared.Rent(Chunk);
        Array.Clear(zeros, 0, zeros.Length);
        try
        {
            foreach (var ext in dstExtents)
            {
                long bytes = checked((long)ext.NumBlocks * blockSize);
                long off = checked((long)ext.StartBlock * blockSize);
                long wrote = 0;
                while (wrote < bytes)
                {
                    int n = (int)Math.Min(Chunk, bytes - wrote);
                    await RandomAccess.WriteAsync(outHandle, zeros.AsMemory(0, n), off + wrote, cancellationToken).ConfigureAwait(false);
                    wrote += n;
                    reportWrittenBytes(n);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(zeros);
        }
    }

    private static string ToReadableSize(long bytes)
    {
        double b = bytes;
        if (b >= 1024d * 1024d * 1024d) return $"{b / (1024d * 1024d * 1024d):0.0}GB";
        if (b >= 1024d * 1024d) return $"{b / (1024d * 1024d):0.0}MB";
        return $"{b / 1024d:0.0}KB";
    }
}
