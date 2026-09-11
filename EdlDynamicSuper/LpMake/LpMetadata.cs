namespace AFlashTool.Modular.Super.LpMake;

/// <summary>
/// LP 元数据平坦容器 - 由 Builder.Export() 产生, 可直接序列化到二进制
/// </summary>
public sealed class LpMetadata
{
    public LpMetadataGeometry Geometry { get; set; } = new();
    public LpMetadataHeader Header { get; set; } = new();
    public List<LpMetadataPartition> Partitions { get; } = new();
    public List<LpMetadataExtent> Extents { get; } = new();
    public List<LpMetadataPartitionGroup> Groups { get; } = new();
    public List<LpMetadataBlockDevice> BlockDevices { get; } = new();
}

/// <summary>
/// 块设备输入信息 (用于 Builder 初始化, 区别于序列化用的 LpMetadataBlockDevice)
/// </summary>
public sealed class BlockDeviceInfo
{
    public string PartitionName { get; init; } = LpMakeConstants.LP_METADATA_DEFAULT_PARTITION_NAME;
    public ulong Size { get; init; }
    public uint Alignment { get; init; }
    public uint AlignmentOffset { get; init; }
    public uint LogicalBlockSize { get; init; } = LpMakeConstants.KDefaultBlockSize;
}
