namespace AFlashTool.Modular.Super.LpMake;

/// <summary>
/// 逻辑分区 (运行期 builder 视角, 与 LpMetadataPartition 二进制条目区分)
/// </summary>
public sealed class Partition
{
    public string Name { get; }
    public string GroupName { get; internal set; }
    public uint Attributes { get; set; }
    public ulong Size { get; private set; }

    private readonly List<Extent> _extents = new();
    public IReadOnlyList<Extent> Extents => _extents;

    public Partition(string name, string groupName, uint attributes)
    {
        Name = name;
        GroupName = groupName;
        Attributes = attributes;
        Size = 0;
    }

    /// <summary>追加 extent (相邻同设备 LinearExtent 自动合并)</summary>
    public void AddExtent(Extent extent)
    {
        Size += extent.NumSectors * LpMakeConstants.LP_SECTOR_SIZE;

        if (extent is LinearExtent newLinear &&
            _extents.Count > 0 &&
            _extents[^1] is LinearExtent prev &&
            prev.EndSector == newLinear.PhysicalSector &&
            prev.DeviceIndex == newLinear.DeviceIndex)
        {
            var merged = new LinearExtent(prev.NumSectors + newLinear.NumSectors,
                prev.DeviceIndex, prev.PhysicalSector);
            _extents.RemoveAt(_extents.Count - 1);
            _extents.Add(merged);
            return;
        }

        _extents.Add(extent);
    }

    public void RemoveExtents()
    {
        Size = 0;
        _extents.Clear();
    }

    /// <summary>裁剪到指定大小 (字节, 必须是扇区对齐)</summary>
    internal void ShrinkTo(ulong alignedSize)
    {
        if (alignedSize == 0)
        {
            RemoveExtents();
            return;
        }

        ulong sectorsToRemove = (Size - alignedSize) / LpMakeConstants.LP_SECTOR_SIZE;
        while (sectorsToRemove > 0)
        {
            var extent = _extents[^1];
            if (extent.NumSectors > sectorsToRemove)
            {
                Size -= sectorsToRemove * LpMakeConstants.LP_SECTOR_SIZE;
                extent.NumSectors -= sectorsToRemove;
                break;
            }
            Size -= extent.NumSectors * LpMakeConstants.LP_SECTOR_SIZE;
            sectorsToRemove -= extent.NumSectors;
            _extents.RemoveAt(_extents.Count - 1);
        }
    }

    public ulong BytesOnDisk()
    {
        ulong sectors = 0;
        foreach (var e in _extents)
            if (e is LinearExtent) sectors += e.NumSectors;
        return sectors * LpMakeConstants.LP_SECTOR_SIZE;
    }
}

/// <summary>
/// 分区组 (用于限制一组分区的总大小, 例如 qti_dynamic_partitions_a)
/// </summary>
public sealed class PartitionGroup
{
    public string Name { get; }
    public ulong MaximumSize { get; internal set; }

    public PartitionGroup(string name, ulong maximumSize)
    {
        Name = name;
        MaximumSize = maximumSize;
    }
}
