using System.IO;
using System.IO.Compression;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Compressors.Xz;
using ZstdSharp;

namespace Payload_Dumper_C_.Core;

public static class Decompression
{
    public static byte[] BZip2(ReadOnlySpan<byte> input)
    {
        using var src = new MemoryStream(input.ToArray(), writable: false);
        using var bz = new BZip2Stream(src, SharpCompress.Compressors.CompressionMode.Decompress, false);
        using var dst = new MemoryStream();
        bz.CopyTo(dst);
        return dst.ToArray();
    }

    public static byte[] Xz(ReadOnlySpan<byte> input)
    {
        using var src = new MemoryStream(input.ToArray(), writable: false);
        using var xz = new XZStream(src);
        using var dst = new MemoryStream();
        xz.CopyTo(dst);
        return dst.ToArray();
    }

    public static byte[] Brotli(ReadOnlySpan<byte> input)
    {
        using var src = new MemoryStream(input.ToArray(), writable: false);
        using var br = new System.IO.Compression.BrotliStream(src, System.IO.Compression.CompressionMode.Decompress);
        using var dst = new MemoryStream();
        br.CopyTo(dst);
        return dst.ToArray();
    }

    public static byte[] Zstd(ReadOnlySpan<byte> input)
    {
        using var d = new Decompressor();
        return d.Unwrap(input.ToArray()).ToArray();
    }
}
