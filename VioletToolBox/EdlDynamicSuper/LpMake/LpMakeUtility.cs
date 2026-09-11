using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AFlashTool.Modular.Super.LpMake;

/// <summary>
/// LpMake 通用工具方法
/// </summary>
public static class LpMakeUtility
{
    /// <summary>向上对齐到 alignment 的倍数 (alignment_offset 用于 stacked device)</summary>
    public static ulong AlignTo(ulong value, ulong alignment, ulong alignmentOffset = 0)
    {
        if (alignment == 0)
            return value;
        ulong rem = (value - alignmentOffset) % alignment;
        if (rem == 0)
            return value;
        return value + (alignment - rem);
    }

    public static uint AlignTo(uint value, uint alignment) =>
        alignment == 0 ? value : (uint)AlignTo((ulong)value, alignment);

    /// <summary>计算 super 上 metadata 所需保留空间 (含 reserved + 双份 geometry + 双份每槽 metadata)</summary>
    public static ulong GetTotalMetadataSize(uint metadataMaxSize, uint maxSlots) =>
        (ulong)LpMakeConstants.LP_PARTITION_RESERVED_BYTES +
        ((ulong)LpMakeConstants.LP_METADATA_GEOMETRY_SIZE + (ulong)metadataMaxSize * maxSlots) * 2UL;

    public static long GetPrimaryGeometryOffset() => LpMakeConstants.LP_PARTITION_RESERVED_BYTES;

    public static long GetBackupGeometryOffset() =>
        GetPrimaryGeometryOffset() + LpMakeConstants.LP_METADATA_GEOMETRY_SIZE;

    public static long GetPrimaryMetadataOffset(LpMetadataGeometry geometry, uint slotNumber) =>
        LpMakeConstants.LP_PARTITION_RESERVED_BYTES +
        LpMakeConstants.LP_METADATA_GEOMETRY_SIZE * 2L +
        (long)geometry.MetadataMaxSize * slotNumber;

    public static long GetBackupMetadataOffset(LpMetadataGeometry geometry, uint slotNumber)
    {
        long start = LpMakeConstants.LP_PARTITION_RESERVED_BYTES +
                     LpMakeConstants.LP_METADATA_GEOMETRY_SIZE * 2L +
                     (long)geometry.MetadataMaxSize * geometry.MetadataSlotCount;
        return start + (long)geometry.MetadataMaxSize * slotNumber;
    }

    public static byte[] Sha256(ReadOnlySpan<byte> data)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(data, hash);
        return hash.ToArray();
    }

    /// <summary>将字符串以 ASCII 写入定长字段, 不足补零</summary>
    public static void WriteFixedAsciiName(Span<byte> dest, string name)
    {
        dest.Clear();
        if (string.IsNullOrEmpty(name))
            return;
        int len = Math.Min(name.Length, dest.Length);
        Encoding.ASCII.GetBytes(name.AsSpan(0, len), dest);
    }

    /// <summary>从定长 ASCII 字段读出字符串 (按首个 0 截断)</summary>
    public static string ReadFixedAsciiName(ReadOnlySpan<byte> src)
    {
        int end = src.IndexOf((byte)0);
        if (end < 0) end = src.Length;
        return Encoding.ASCII.GetString(src[..end]);
    }

    // === 序列化 helpers (little-endian) ===

    public static void WriteU16(Span<byte> dest, ref int offset, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(dest[offset..], value);
        offset += 2;
    }

    public static void WriteU32(Span<byte> dest, ref int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(dest[offset..], value);
        offset += 4;
    }

    public static void WriteU64(Span<byte> dest, ref int offset, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(dest[offset..], value);
        offset += 8;
    }

    public static void WriteBytes(Span<byte> dest, ref int offset, ReadOnlySpan<byte> data)
    {
        data.CopyTo(dest[offset..]);
        offset += data.Length;
    }
}
