using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using SharpCompress.Compressors.BZip2;

namespace Payload_Dumper_C_.Core;

public static class BsDiff
{
    private static readonly byte[] BsDiffMagic = "BSDIFF40"u8.ToArray();
    private static readonly byte[] Bsdf2Magic5 = "BSDF2"u8.ToArray();

    public static byte[] ApplyBsdf2Patch(ReadOnlySpan<byte> oldData, ReadOnlySpan<byte> patchData)
    {
        using var patchStream = new MemoryStream(patchData.ToArray(), writable: false);

        Span<byte> magic = stackalloc byte[8];
        if (patchStream.Read(magic) != 8) throw new InvalidDataException("patch 头不足");

        byte algControl;
        byte algDiff;
        byte algExtra;

        if (magic.SequenceEqual(BsDiffMagic))
        {
            algControl = 1;
            algDiff = 1;
            algExtra = 1;
        }
        else if (magic.Slice(0, 5).SequenceEqual(Bsdf2Magic5))
        {
            algControl = magic[5];
            algDiff = magic[6];
            algExtra = magic[7];
        }
        else
        {
            throw new InvalidDataException("patch magic 不正确");
        }

        long lenControl = DecodeInt64(ReadExactly(patchStream, 8));
        long lenDiff = DecodeInt64(ReadExactly(patchStream, 8));
        long lenNew = DecodeInt64(ReadExactly(patchStream, 8));

        if (lenControl < 0 || lenDiff < 0 || lenNew < 0) throw new InvalidDataException("patch 长度字段无效");

        byte[] controlCompressed = ReadExactly(patchStream, (int)lenControl);
        byte[] diffCompressed = ReadExactly(patchStream, (int)lenDiff);
        byte[] extraCompressed = ReadToEnd(patchStream);

        byte[] control = Decompress(algControl, controlCompressed);
        byte[] diff = Decompress(algDiff, diffCompressed);
        byte[] extra = Decompress(algExtra, extraCompressed);

        var newData = new byte[lenNew];
        int controlPos = 0;
        int diffPos = 0;
        int extraPos = 0;
        int newPos = 0;
        int oldPos = 0;

        while (controlPos < control.Length)
        {
            if (controlPos + 24 > control.Length) throw new InvalidDataException("control block 不完整");

            long x = DecodeInt64(control.AsSpan(controlPos, 8));
            long y = DecodeInt64(control.AsSpan(controlPos + 8, 8));
            long z = DecodeInt64(control.AsSpan(controlPos + 16, 8));
            controlPos += 24;

            if (x < 0 || y < 0) throw new InvalidDataException("control tuple 无效");
            if (newPos + x > newData.Length) throw new InvalidDataException("new 数据越界(x)");
            if (diffPos + x > diff.Length) throw new InvalidDataException("diff 数据越界");

            for (int i = 0; i < x; i++)
            {
                byte oldByte = 0;
                int oldIndex = oldPos + i;
                if ((uint)oldIndex < (uint)oldData.Length)
                {
                    oldByte = oldData[oldIndex];
                }

                sbyte diffByte = unchecked((sbyte)diff[diffPos + i]);
                newData[newPos + i] = unchecked((byte)(oldByte + diffByte));
            }

            newPos += (int)x;
            oldPos += (int)x;
            diffPos += (int)x;

            if (newPos + y > newData.Length) throw new InvalidDataException("new 数据越界(y)");
            if (extraPos + y > extra.Length) throw new InvalidDataException("extra 数据越界");

            extra.AsSpan(extraPos, (int)y).CopyTo(newData.AsSpan(newPos, (int)y));
            newPos += (int)y;
            extraPos += (int)y;

            oldPos += (int)z;
        }

        return newData;
    }

    private static byte[] Decompress(byte alg, byte[] data)
    {
        return alg switch
        {
            0 => data,
            1 => DecompressBZip2(data),
            2 => DecompressBrotli(data),
            _ => throw new InvalidDataException($"未知 BSDF2 算法: {alg}"),
        };
    }

    private static byte[] DecompressBZip2(byte[] data)
    {
        using var input = new MemoryStream(data, writable: false);
        using var bz = new BZip2Stream(input, SharpCompress.Compressors.CompressionMode.Decompress, false);
        using var output = new MemoryStream();
        bz.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] DecompressBrotli(byte[] data)
    {
        using var input = new MemoryStream(data, writable: false);
        using var br = new BrotliStream(input, CompressionMode.Decompress, leaveOpen: false);
        using var output = new MemoryStream();
        br.CopyTo(output);
        return output.ToArray();
    }

    private static long DecodeInt64(ReadOnlySpan<byte> data)
    {
        if (data.Length != 8) throw new ArgumentException("需要 8 字节", nameof(data));

        ulong y = BinaryPrimitives.ReadUInt64LittleEndian(data);
        bool neg = (y & (1UL << 63)) != 0;
        y &= ~(1UL << 63);
        long x = unchecked((long)y);
        return neg ? -x : x;
    }

    private static byte[] ReadExactly(Stream s, int count)
    {
        var buf = new byte[count];
        int readTotal = 0;
        while (readTotal < count)
        {
            int r = s.Read(buf, readTotal, count - readTotal);
            if (r == 0) throw new EndOfStreamException();
            readTotal += r;
        }
        return buf;
    }

    private static byte[] ReadToEnd(Stream s)
    {
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
