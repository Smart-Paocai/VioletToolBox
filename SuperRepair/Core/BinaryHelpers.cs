using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SuperFix.Core;

internal static class BinaryHelpers
{
    public static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);

    public static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    public static ulong ReadUInt64(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(data[offset..]);

    public static void WriteUInt16(Span<byte> data, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(data[offset..], value);

    public static void WriteUInt32(Span<byte> data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data[offset..], value);

    public static void WriteUInt64(Span<byte> data, int offset, ulong value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(data[offset..], value);

    public static string ReadFixedAscii(ReadOnlySpan<byte> data)
    {
        int end = data.IndexOf((byte)0);
        if (end < 0)
            end = data.Length;
        return Encoding.ASCII.GetString(data[..end]);
    }

    public static void WriteFixedAscii(Span<byte> data, string value)
    {
        data.Clear();
        if (string.IsNullOrEmpty(value))
            return;
        int length = Math.Min(data.Length, value.Length);
        Encoding.ASCII.GetBytes(value.AsSpan(0, length), data[..length]);
    }

    public static byte[] Sha256(ReadOnlySpan<byte> data) => SHA256.HashData(data);

    public static bool FixedTimeEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) =>
        left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);

    public static bool IsPowerOfTwo(uint value) => value != 0 && (value & (value - 1)) == 0;

    public static string FormatBytes(ulong value)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double size = value;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }
}
