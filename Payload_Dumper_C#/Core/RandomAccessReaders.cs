using System.Buffers;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Payload_Dumper_C_.Core;

public interface IRandomAccessReader : IAsyncDisposable
{
    ValueTask<long> GetSizeAsync(CancellationToken cancellationToken);
    ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken);
    long BytesRead { get; }
}

public sealed class LocalFileReader : IRandomAccessReader
{
    public string Path { get; }

    private readonly FileStream _stream;
    private long _bytesRead;

    public LocalFileReader(string path)
    {
        Path = path;
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.Asynchronous | FileOptions.RandomAccess);
    }

    public long BytesRead => Interlocked.Read(ref _bytesRead);

    public ValueTask<long> GetSizeAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult(_stream.Length);

    public async ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Length == 0) return 0;

        int read = await RandomAccess.ReadAsync(_stream.SafeFileHandle, buffer, offset, cancellationToken).ConfigureAwait(false);
        if (read > 0) Interlocked.Add(ref _bytesRead, read);
        return read;
    }

    public ValueTask DisposeAsync()
    {
        _stream.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class TempFileReader : IRandomAccessReader
{
    public string Path { get; }

    private readonly FileStream _stream;
    private long _bytesRead;

    public TempFileReader(string path)
    {
        Path = path;
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.Asynchronous | FileOptions.RandomAccess);
    }

    public long BytesRead => Interlocked.Read(ref _bytesRead);

    public ValueTask<long> GetSizeAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult(_stream.Length);

    public async ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Length == 0) return 0;

        int read = await RandomAccess.ReadAsync(_stream.SafeFileHandle, buffer, offset, cancellationToken).ConfigureAwait(false);
        if (read > 0) Interlocked.Add(ref _bytesRead, read);
        return read;
    }

    public ValueTask DisposeAsync()
    {
        _stream.Dispose();
        try
        {
            File.Delete(Path);
        }
        catch
        {
        }
        return ValueTask.CompletedTask;
    }
}

public sealed class ZipPayloadLazyReader : IRandomAccessReader
{
    private const long CacheSoftLimitBytes = 32L << 20;

    private readonly string _zipPath;
    private readonly Action<string>? _log;
    private long _bytesRead;

    private FileStream? _zipStream;
    private ZipArchive? _zip;
    private Stream? _entryStream;
    private string? _entryName;

    private MemoryStream? _cache;
    private long _size;
    private TempFileReader? _extracted;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ZipPayloadLazyReader(string zipPath, Action<string>? log)
    {
        _zipPath = zipPath;
        _log = log;
        _size = ResolvePayloadSize(zipPath);
    }

    public bool IsExtracted => _extracted is not null;

    public long BytesRead => Interlocked.Read(ref _bytesRead);

    public ValueTask<long> GetSizeAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult(_size);

    public async ValueTask EnsureExtractedAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (_extracted is not null) return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_extracted is not null) return;

            var (tempPath, _) = await PayloadProcessing.ExtractPayloadFromLocalZipAsync(_zipPath, _log, progress, cancellationToken).ConfigureAwait(false);
            _extracted = new TempFileReader(tempPath);

            await DisposeZipHandlesAsync().ConfigureAwait(false);
            _cache?.Dispose();
            _cache = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Length == 0) return 0;

        var extracted = _extracted;
        if (extracted is not null)
        {
            int r = await extracted.ReadAsync(offset, buffer, cancellationToken).ConfigureAwait(false);
            if (r > 0) Interlocked.Add(ref _bytesRead, r);
            return r;
        }

        long needEnd = offset + buffer.Length;
        if (needEnd > int.MaxValue || needEnd > CacheSoftLimitBytes)
        {
            await EnsureExtractedAsync(progress: null, cancellationToken: cancellationToken).ConfigureAwait(false);
            extracted = _extracted;
            if (extracted is null) return 0;

            int r = await extracted.ReadAsync(offset, buffer, cancellationToken).ConfigureAwait(false);
            if (r > 0) Interlocked.Add(ref _bytesRead, r);
            return r;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            extracted = _extracted;
            if (extracted is not null)
            {
                int r = await extracted.ReadAsync(offset, buffer, cancellationToken).ConfigureAwait(false);
                if (r > 0) Interlocked.Add(ref _bytesRead, r);
                return r;
            }

            EnsureZipEntryOpen();
            _cache ??= new MemoryStream(capacity: (int)Math.Min(_size, CacheSoftLimitBytes));

            await EnsureCacheAsync(needEnd, cancellationToken).ConfigureAwait(false);

            if (_cache.Length <= offset) return 0;
            int canRead = (int)Math.Min(buffer.Length, _cache.Length - offset);
            _cache.Position = offset;
            int read = _cache.Read(buffer.Span.Slice(0, canRead));
            if (read > 0) Interlocked.Add(ref _bytesRead, read);
            return read;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_extracted is not null)
            {
                await _extracted.DisposeAsync().ConfigureAwait(false);
                _extracted = null;
            }

            await DisposeZipHandlesAsync().ConfigureAwait(false);
            _cache?.Dispose();
            _cache = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private static long ResolvePayloadSize(string zipPath)
    {
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
        return entry.Length;
    }

    private void EnsureZipEntryOpen()
    {
        if (_entryStream is not null) return;

        _zipStream = new FileStream(_zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        _zip = new ZipArchive(_zipStream, ZipArchiveMode.Read, leaveOpen: false);
        ZipArchiveEntry? entry = _zip.GetEntry("payload.bin");
        if (entry is null)
        {
            foreach (var e in _zip.Entries)
            {
                if (e.FullName.EndsWith("payload.bin", StringComparison.OrdinalIgnoreCase))
                {
                    entry = e;
                    break;
                }
            }
        }
        if (entry is null) throw new InvalidDataException("OTA ZIP 内没有 payload.bin");
        _entryName = entry.FullName;
        _entryStream = entry.Open();
    }

    private async Task EnsureCacheAsync(long targetLen, CancellationToken cancellationToken)
    {
        if (_entryStream is null || _cache is null) throw new InvalidOperationException();
        if (_cache.Length >= targetLen) return;

        var tmp = ArrayPool<byte>.Shared.Rent(1 << 20);
        try
        {
            while (_cache.Length < targetLen)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int r = await _entryStream.ReadAsync(tmp.AsMemory(0, tmp.Length), cancellationToken).ConfigureAwait(false);
                if (r == 0) break;
                _cache.Write(tmp, 0, r);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(tmp);
        }
    }

    private async Task DisposeZipHandlesAsync()
    {
        if (_entryStream is not null)
        {
            await _entryStream.DisposeAsync().ConfigureAwait(false);
            _entryStream = null;
        }

        _zip?.Dispose();
        _zip = null;

        if (_zipStream is not null)
        {
            await _zipStream.DisposeAsync().ConfigureAwait(false);
            _zipStream = null;
        }
    }
}

public sealed class HttpRangeReader : IRandomAccessReader
{
    private const int CacheBlockSize = 1 << 20;
    private const int MaxCachedBlocks = 8;
    private const int MaxCachedReadBytes = 4 << 20;
    private const int MaxRetryAttempts = 3;
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromMilliseconds(450);

    private readonly HttpClient _client;
    private readonly Uri _uri;
    private readonly long _size;
    private long _bytesRead;
    private readonly object _cacheLock = new();
    private readonly Dictionary<long, byte[]> _blockCache = new();
    private readonly Dictionary<long, LinkedListNode<long>> _lruNodes = new();
    private readonly LinkedList<long> _lruOrder = new();
    private readonly SemaphoreSlim _requestGate = new(1, 1);

    private HttpRangeReader(HttpClient client, Uri uri, long size)
    {
        _client = client;
        _uri = uri;
        _size = size;
    }

    public static async ValueTask<HttpRangeReader> CreateAsync(string url, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(100);
        var uri = new Uri(url, UriKind.Absolute);

        if (headers is not null)
        {
            foreach (var kv in headers)
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(kv.Key, kv.Value);
            }
        }

        using var resp = await SendWithRetryAsync(client, () =>
        {
            var probe = new HttpRequestMessage(HttpMethod.Get, uri);
            probe.Headers.Range = new RangeHeaderValue(0, 0);
            return probe;
        }, cancellationToken).ConfigureAwait(false);
        if ((int)resp.StatusCode != 206)
        {
            client.Dispose();
            throw new InvalidOperationException($"远程不支持 Range 请求: {url}");
        }

        long? totalLength = resp.Content.Headers.ContentRange?.Length;
        if (!totalLength.HasValue || totalLength.Value <= 0)
        {
            client.Dispose();
            throw new InvalidOperationException($"远程没有 Content-Length: {url}");
        }

        return new HttpRangeReader(client, uri, totalLength.Value);
    }

    public long BytesRead => Interlocked.Read(ref _bytesRead);

    public ValueTask<long> GetSizeAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult(_size);

    public async ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Length == 0) return 0;
        if (offset < 0 || offset >= _size) return 0;

        int requested = (int)Math.Min(buffer.Length, _size - offset);
        if (requested <= 0) return 0;

        if (requested <= MaxCachedReadBytes)
        {
            int cachedRead = await TryReadFromBlockCacheAsync(offset, buffer.Slice(0, requested), cancellationToken).ConfigureAwait(false);
            if (cachedRead > 0)
            {
                Interlocked.Add(ref _bytesRead, cachedRead);
            }
            return cachedRead;
        }

        int directRead = await ReadRangeAsync(offset, buffer.Slice(0, requested), cancellationToken).ConfigureAwait(false);
        if (directRead > 0)
        {
            Interlocked.Add(ref _bytesRead, directRead);
        }
        return directRead;
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        _requestGate.Dispose();
        lock (_cacheLock)
        {
            _blockCache.Clear();
            _lruNodes.Clear();
            _lruOrder.Clear();
        }
        return ValueTask.CompletedTask;
    }

    private async Task<int> TryReadFromBlockCacheAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int totalRead = 0;
        long endExclusive = offset + buffer.Length;
        long startBlock = offset / CacheBlockSize;
        long endBlock = (endExclusive - 1) / CacheBlockSize;

        for (long blockIndex = startBlock; blockIndex <= endBlock; blockIndex++)
        {
            byte[] block = await GetOrCreateBlockAsync(blockIndex, cancellationToken).ConfigureAwait(false);
            long blockStart = blockIndex * CacheBlockSize;
            long copyStart = Math.Max(offset, blockStart);
            long copyEnd = Math.Min(endExclusive, blockStart + block.Length);
            int copyLength = (int)(copyEnd - copyStart);
            if (copyLength <= 0)
            {
                continue;
            }

            int sourceOffset = (int)(copyStart - blockStart);
            block.AsSpan(sourceOffset, copyLength).CopyTo(buffer.Span.Slice(totalRead, copyLength));
            totalRead += copyLength;
        }

        return totalRead;
    }

    private async Task<byte[]> GetOrCreateBlockAsync(long blockIndex, CancellationToken cancellationToken)
    {
        if (TryGetCachedBlock(blockIndex, out byte[]? cached) && cached is not null)
        {
            return cached;
        }

        long blockOffset = blockIndex * CacheBlockSize;
        int blockLength = (int)Math.Min(CacheBlockSize, _size - blockOffset);
        var block = new byte[blockLength];
        int read = await ReadRangeAsync(blockOffset, block, cancellationToken).ConfigureAwait(false);
        if (read != blockLength)
        {
            throw new IOException($"远程块读取不完整: block={blockIndex} expected={blockLength} actual={read}");
        }

        lock (_cacheLock)
        {
            if (TryGetCachedBlock_NoLock(blockIndex, out cached) && cached is not null)
            {
                return cached;
            }

            if (_blockCache.Count >= MaxCachedBlocks && _lruOrder.First is not null)
            {
                long oldest = _lruOrder.First.Value;
                _lruOrder.RemoveFirst();
                _lruNodes.Remove(oldest);
                _blockCache.Remove(oldest);
            }

            var node = _lruOrder.AddLast(blockIndex);
            _blockCache[blockIndex] = block;
            _lruNodes[blockIndex] = node;
            return block;
        }
    }

    private bool TryGetCachedBlock(long blockIndex, out byte[]? block)
    {
        lock (_cacheLock)
        {
            return TryGetCachedBlock_NoLock(blockIndex, out block);
        }
    }

    private bool TryGetCachedBlock_NoLock(long blockIndex, out byte[]? block)
    {
        if (_blockCache.TryGetValue(blockIndex, out block))
        {
            if (_lruNodes.TryGetValue(blockIndex, out var node))
            {
                _lruOrder.Remove(node);
                _lruOrder.AddLast(node);
            }
            return true;
        }

        block = null;
        return false;
    }

    private async Task<int> ReadRangeAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        long end = Math.Min(offset + buffer.Length - 1L, _size - 1L);
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var resp = await SendWithRetryAsync(_client, () =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, _uri);
                req.Headers.Range = new RangeHeaderValue(offset, end);
                return req;
            }, cancellationToken).ConfigureAwait(false);
            if ((int)resp.StatusCode != 206)
            {
                throw new IOException($"远程未返回 Partial Content(206): {(int)resp.StatusCode} {resp.ReasonPhrase}");
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int r = await stream.ReadAsync(buffer.Slice(totalRead), cancellationToken).ConfigureAwait(false);
                if (r == 0) break;
                totalRead += r;
            }

            return totalRead;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private static async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient client,
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var request = requestFactory();
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode == 429 && attempt < MaxRetryAttempts - 1)
                {
                    var delay = GetRetryDelay(response, attempt);
                    response.Dispose();
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return response;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < MaxRetryAttempts - 1)
            {
                await Task.Delay(GetRetryDelay(null, attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < MaxRetryAttempts - 1)
            {
                await Task.Delay(GetRetryDelay(null, attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage? response, int attempt)
    {
        if (response?.Headers.RetryAfter?.Delta is TimeSpan retryAfter && retryAfter > TimeSpan.Zero)
        {
            return retryAfter;
        }

        double factor = Math.Pow(2, Math.Max(0, attempt));
        var delay = TimeSpan.FromMilliseconds(BaseRetryDelay.TotalMilliseconds * factor);
        return delay > TimeSpan.FromSeconds(4) ? TimeSpan.FromSeconds(4) : delay;
    }
}

public sealed class SubrangeReader : IRandomAccessReader
{
    private readonly IRandomAccessReader _baseReader;
    private readonly long _baseOffset;
    private readonly long _length;

    public SubrangeReader(IRandomAccessReader baseReader, long baseOffset, long length)
    {
        _baseReader = baseReader;
        _baseOffset = baseOffset;
        _length = length;
    }

    public long BytesRead => _baseReader.BytesRead;

    public ValueTask<long> GetSizeAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult(_length);

    public ValueTask<int> ReadAsync(long offset, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (offset < 0 || offset >= _length) return ValueTask.FromResult(0);

        long maxReadable = _length - offset;
        int toRead = (int)Math.Min(maxReadable, buffer.Length);
        return _baseReader.ReadAsync(_baseOffset + offset, buffer.Slice(0, toRead), cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

public static class RandomAccessReaderExtensions
{
    public static async ValueTask<byte[]> ReadExactlyAsync(this IRandomAccessReader reader, long offset, int size, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            int readTotal = 0;
            while (readTotal < size)
            {
                int r = await reader.ReadAsync(offset + readTotal, buffer.AsMemory(readTotal, size - readTotal), cancellationToken).ConfigureAwait(false);
                if (r == 0) throw new EndOfStreamException($"无法读取足够数据: 期望 {size} 实际 {readTotal}");
                readTotal += r;
            }

            var result = new byte[size];
            Buffer.BlockCopy(buffer, 0, result, 0, size);
            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
