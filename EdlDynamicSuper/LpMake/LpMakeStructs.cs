namespace AFlashTool.Modular.Super.LpMake;

/// <summary>
/// 表描述符 - 描述某张 metadata 表的位置/数量/单条尺寸
/// </summary>
public sealed class LpMetadataTableDescriptor
{
    public uint Offset;
    public uint NumEntries;
    public uint EntrySize;
}

/// <summary>
/// LP 几何信息 (4096 bytes 区块)
/// </summary>
public sealed class LpMetadataGeometry
{
    public uint Magic = LpMakeConstants.LP_METADATA_GEOMETRY_MAGIC;
    public uint StructSize = (uint)LpMakeConstants.SizeOfGeometry;
    public byte[] Checksum = new byte[32];
    public uint MetadataMaxSize;
    public uint MetadataSlotCount;
    public uint LogicalBlockSize;
}

/// <summary>
/// LP 元数据头部
/// </summary>
public sealed class LpMetadataHeader
{
    public uint Magic = LpMakeConstants.LP_METADATA_HEADER_MAGIC;
    public ushort MajorVersion = LpMakeConstants.LP_METADATA_MAJOR_VERSION;
    public ushort MinorVersion = LpMakeConstants.LP_METADATA_MINOR_VERSION_MIN;
    public uint HeaderSize = LpMakeConstants.SizeOfHeaderV10;
    public byte[] HeaderChecksum = new byte[32];
    public uint TablesSize;
    public byte[] TablesChecksum = new byte[32];

    public LpMetadataTableDescriptor Partitions = new() { EntrySize = LpMakeConstants.SizeOfPartition };
    public LpMetadataTableDescriptor Extents = new() { EntrySize = LpMakeConstants.SizeOfExtent };
    public LpMetadataTableDescriptor Groups = new() { EntrySize = LpMakeConstants.SizeOfPartitionGroup };
    public LpMetadataTableDescriptor BlockDevices = new() { EntrySize = LpMakeConstants.SizeOfBlockDevice };

    // V1.2+
    public uint Flags;
    public byte[] Reserved = new byte[124];
}

/// <summary>
/// 分区条目 (52 bytes)
/// </summary>
public sealed class LpMetadataPartition
{
    public string Name = string.Empty;
    public uint Attributes;
    public uint FirstExtentIndex;
    public uint NumExtents;
    public uint GroupIndex;
}

/// <summary>
/// 分区 extent (24 bytes)
/// </summary>
public sealed class LpMetadataExtent
{
    public ulong NumSectors;
    public uint TargetType;
    public ulong TargetData;
    public uint TargetSource;
}

/// <summary>
/// 分区组 (48 bytes)
/// </summary>
public sealed class LpMetadataPartitionGroup
{
    public string Name = string.Empty;
    public uint Flags;
    public ulong MaximumSize;
}

/// <summary>
/// 块设备条目 (64 bytes)
/// </summary>
public sealed class LpMetadataBlockDevice
{
    public ulong FirstLogicalSector;
    public uint Alignment;
    public uint AlignmentOffset;
    public ulong Size;
    public string PartitionName = string.Empty;
    public uint Flags;
}
