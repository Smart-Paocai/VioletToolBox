using System.Text;

namespace Yu.Modular.Qualcomm.Firehose.Parsers;

/// <summary>
/// Android Dynamic Partitions (super 分区) LP 元数据解析器
/// 解析 LpMetadataGeometry + LpMetadataHeader + 分区表/Extent表
/// </summary>
public static class LpMetadataParser
{
    private const uint GEOMETRY_MAGIC = 0x616c4467; // "gDla"
    private const uint HEADER_MAGIC = 0x414c5030;   // "0PLA"
    private const int GEOMETRY_OFFSET = 4096;        // primary geometry at 4096
    private const int METADATA_OFFSET = 4096 * 3;   // primary metadata at 12288

    /// <summary>
    /// 从 super 分区读取并解析 LP 元数据, 返回逻辑分区列表
    /// </summary>
    public static List<LpPartitionInfo>? Parse(IByteReader superReader)
    {
        // 1. 读取 Geometry
        var geomData = superReader.ReadBytes(GEOMETRY_OFFSET, 4096);
        if (geomData == null) return null;

        var geomMagic = BitConverter.ToUInt32(geomData, 0);
        if (geomMagic != GEOMETRY_MAGIC)
            return null;

        var metadataMaxSize = BitConverter.ToUInt32(geomData, 40);
        var logicalBlockSize = BitConverter.ToUInt32(geomData, 48);
        // 2. 读取 Metadata Header
        var metaData = superReader.ReadBytes(METADATA_OFFSET, (int)metadataMaxSize);
        if (metaData == null) return null;

        var headerMagic = BitConverter.ToUInt32(metaData, 0);
        if (headerMagic != HEADER_MAGIC)
            return null;

        var majorVersion = BitConverter.ToUInt16(metaData, 4);
        var minorVersion = BitConverter.ToUInt16(metaData, 6);
        var headerSize = BitConverter.ToUInt32(metaData, 8);
        // checksum at 12..43 (32 bytes)
        var tablesSize = BitConverter.ToUInt32(metaData, 44);
        // tables_checksum at 48..79 (32 bytes)

        // Table descriptors (offset, num_entries, entry_size) - each 12 bytes
        var descBase = 80;
        var (partOff, partNum, partEntrySize) = ReadTableDesc(metaData, descBase);
        var (extOff, extNum, extEntrySize) = ReadTableDesc(metaData, descBase + 12);
        var (grpOff, grpNum, grpEntrySize) = ReadTableDesc(metaData, descBase + 24);
        var (blkOff, blkNum, blkEntrySize) = ReadTableDesc(metaData, descBase + 36);

        var tablesBase = (int)headerSize;

        // 3. 解析 Extent 表
        var extents = new List<LpExtentEntry>();
        for (var i = 0; i < extNum; i++)
        {
            var off = tablesBase + (int)extOff + i * (int)extEntrySize;
            if (off + 28 > metaData.Length) break;

            var numSectors = BitConverter.ToUInt64(metaData, off);
            var targetType = BitConverter.ToUInt32(metaData, off + 8);
            var targetData = BitConverter.ToUInt64(metaData, off + 12);
            var targetSource = BitConverter.ToUInt32(metaData, off + 20);

            extents.Add(new LpExtentEntry
            {
                NumSectors = (long)numSectors,
                TargetType = targetType,
                TargetData = (long)targetData,
                TargetSource = (int)targetSource
            });
        }

        // 4. 解析分区表
        var partitions = new List<LpPartitionInfo>();
        for (var i = 0; i < partNum; i++)
        {
            var off = tablesBase + (int)partOff + i * (int)partEntrySize;
            if (off + 52 > metaData.Length) break;

            // name: 36 bytes null-terminated
            var nameBytes = new byte[36];
            Buffer.BlockCopy(metaData, off, nameBytes, 0, 36);
            var nameEnd = Array.IndexOf(nameBytes, (byte)0);
            var name = Encoding.ASCII.GetString(nameBytes, 0, nameEnd >= 0 ? nameEnd : 36).Trim();

            var attributes = BitConverter.ToUInt32(metaData, off + 36);
            var firstExtentIndex = BitConverter.ToUInt32(metaData, off + 40);
            var numExtents = BitConverter.ToUInt32(metaData, off + 44);
            var groupIndex = BitConverter.ToUInt32(metaData, off + 48);

            // 收集此分区的所有 linear extent
            var partExtents = new List<LpExtentMapping>();
            long totalSectors = 0;
            for (var e = firstExtentIndex; e < firstExtentIndex + numExtents && e < extents.Count; e++)
            {
                var ext = extents[(int)e];
                if (ext.TargetType == 0) // LINEAR
                    partExtents.Add(new LpExtentMapping(ext.TargetData, ext.NumSectors));
                else if (ext.TargetType == 1) // ZERO
                    partExtents.Add(new LpExtentMapping(0, ext.NumSectors, true));
                totalSectors += ext.NumSectors;
            }

            if (totalSectors > 0)
            {
                partitions.Add(new LpPartitionInfo
                {
                    Name = name,
                    Attributes = attributes,
                    GroupIndex = (int)groupIndex,
                    TotalSectors = totalSectors,
                    Extents = partExtents
                });
            }
        }

        return partitions;
    }

    private static (uint offset, uint numEntries, uint entrySize) ReadTableDesc(byte[] data, int pos)
    {
        return (
            BitConverter.ToUInt32(data, pos),
            BitConverter.ToUInt32(data, pos + 4),
            BitConverter.ToUInt32(data, pos + 8)
        );
    }
}

/// <summary>
/// LP extent 原始条目
/// </summary>
internal class LpExtentEntry
{
    public long NumSectors { get; set; }
    public uint TargetType { get; set; }  // 0=LINEAR, 1=ZERO
    public long TargetData { get; set; }  // LINEAR 时为物理扇区偏移
    public int TargetSource { get; set; } // block device index
}

/// <summary>
/// 解析后的 LP 逻辑分区信息
/// </summary>
public class LpPartitionInfo
{
    public string Name { get; set; } = "";
    public uint Attributes { get; set; }
    public int GroupIndex { get; set; }
    public long TotalSectors { get; set; }
    public List<LpExtentMapping> Extents { get; set; } = new();
}
