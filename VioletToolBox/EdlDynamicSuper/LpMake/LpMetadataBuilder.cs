namespace AFlashTool.Modular.Super.LpMake;

using static LpMakeConstants;
using static LpMakeUtility;

/// <summary>
/// LP 元数据构建器 - 移植自 AOSP liblp MetadataBuilder (精简版)
/// </summary>
public sealed class LpMetadataBuilder
{
    private LpMetadataGeometry _geometry;
    private LpMetadataHeader _header;
    private readonly List<Partition> _partitions = new();
    private readonly List<PartitionGroup> _groups = new();
    private readonly List<LpMetadataBlockDevice> _blockDevices = new();

    private LpMetadataBuilder()
    {
        _geometry = new LpMetadataGeometry();
        _header = new LpMetadataHeader();
    }

    // ────────── 工厂 ──────────

    /// <summary>
    /// 从 block device 列表创建空的逻辑分区表
    /// </summary>
    public static LpMetadataBuilder? New(
        IReadOnlyList<BlockDeviceInfo> blockDevices,
        string superPartition,
        uint metadataMaxSize,
        uint metadataSlotCount)
    {
        var builder = new LpMetadataBuilder();
        return builder.Init(blockDevices, superPartition, metadataMaxSize, metadataSlotCount)
            ? builder
            : null;
    }

    // ────────── 公开 API ──────────

    public bool AddGroup(string groupName, ulong maximumSize)
    {
        if (FindGroup(groupName) != null)
            return false;
        _groups.Add(new PartitionGroup(groupName, maximumSize));
        return true;
    }

    public Partition? AddPartition(string name, string groupName, uint attributes)
    {
        if (string.IsNullOrEmpty(name) || FindPartition(name) != null || FindGroup(groupName) == null)
            return null;
        var p = new Partition(name, groupName, attributes);
        _partitions.Add(p);
        return p;
    }

    /// <summary>调整分区大小 (向上对齐到 logical_block_size)</summary>
    public bool ResizePartition(Partition partition, ulong requestedSize)
    {
        ulong alignedSize = AlignTo(requestedSize, _geometry.LogicalBlockSize);
        ulong oldSize = partition.Size;

        if (!ValidatePartitionSizeChange(partition, oldSize, alignedSize))
            return false;

        if (alignedSize > oldSize)
            return GrowPartition(partition, alignedSize);
        if (alignedSize < oldSize)
            partition.ShrinkTo(alignedSize);
        return true;
    }

    /// <summary>导出为可序列化的 LpMetadata</summary>
    public LpMetadata? Export()
    {
        if (!ValidatePartitionGroups())
            return null;

        var metadata = new LpMetadata
        {
            Geometry = _geometry,
            Header = CloneHeader()
        };

        foreach (var bd in _blockDevices)
            metadata.BlockDevices.Add(bd);

        // 映射 group 名 → index
        var groupIndices = new Dictionary<string, uint>();
        foreach (var group in _groups)
        {
            if (group.Name.Length > PartitionNameLen)
                return null;

            metadata.Groups.Add(new LpMetadataPartitionGroup
            {
                Name = group.Name,
                MaximumSize = group.MaximumSize
            });
            groupIndices[group.Name] = (uint)(metadata.Groups.Count - 1);
        }

        foreach (var partition in _partitions)
        {
            if (partition.Name.Length > PartitionNameLen)
                return null;
            if (!groupIndices.TryGetValue(partition.GroupName, out uint gi))
                return null;

            var part = new LpMetadataPartition
            {
                Name = partition.Name,
                Attributes = partition.Attributes,
                FirstExtentIndex = (uint)metadata.Extents.Count,
                NumExtents = (uint)partition.Extents.Count,
                GroupIndex = gi
            };

            foreach (var extent in partition.Extents)
            {
                if (!extent.AddTo(metadata))
                    return null;
            }

            metadata.Partitions.Add(part);
        }

        metadata.Header.Partitions.NumEntries = (uint)metadata.Partitions.Count;
        metadata.Header.Extents.NumEntries = (uint)metadata.Extents.Count;
        metadata.Header.Groups.NumEntries = (uint)metadata.Groups.Count;
        metadata.Header.BlockDevices.NumEntries = (uint)metadata.BlockDevices.Count;

        return metadata;
    }

    // ────────── 查询 ──────────

    public Partition? FindPartition(string name) =>
        _partitions.FirstOrDefault(p => p.Name == name);

    public PartitionGroup? FindGroup(string name) =>
        _groups.FirstOrDefault(g => g.Name == name);

    public ulong AllocatableSpace()
    {
        ulong total = 0;
        foreach (var bd in _blockDevices)
            total += bd.Size - bd.FirstLogicalSector * LP_SECTOR_SIZE;
        return total;
    }

    // ────────── 初始化 ──────────

    private bool Init(IReadOnlyList<BlockDeviceInfo> blockDevices, string superPartition,
        uint metadataMaxSize, uint metadataSlotCount)
    {
        if (metadataMaxSize < SizeOfHeaderV10 || metadataSlotCount == 0 || blockDevices.Count == 0)
            return false;

        metadataMaxSize = AlignTo(metadataMaxSize, (uint)LP_SECTOR_SIZE);

        uint logicalBlockSize = 0;
        foreach (var di in blockDevices)
        {
            if (di.LogicalBlockSize == 0 || di.LogicalBlockSize % LP_SECTOR_SIZE != 0)
                return false;
            if (di.Size % di.LogicalBlockSize != 0)
                return false;

            if (logicalBlockSize == 0) logicalBlockSize = di.LogicalBlockSize;
            if (logicalBlockSize != di.LogicalBlockSize) return false;

            var bd = new LpMetadataBlockDevice
            {
                Alignment = di.Alignment,
                AlignmentOffset = di.AlignmentOffset,
                Size = di.Size,
                PartitionName = di.PartitionName
            };

            // 初始 first_logical_sector (非 super 设备)
            ulong freeStart = LP_SECTOR_SIZE;
            if (bd.Alignment != 0 || bd.AlignmentOffset != 0)
                freeStart = AlignTo(freeStart, bd.Alignment, bd.AlignmentOffset);
            else
                freeStart = AlignTo(freeStart, logicalBlockSize);
            bd.FirstLogicalSector = freeStart / LP_SECTOR_SIZE;

            if (di.PartitionName == superPartition)
                _blockDevices.Insert(0, bd);
            else
                _blockDevices.Add(bd);
        }

        if (_blockDevices.Count == 0 || _blockDevices[0].PartitionName != superPartition)
            return false;

        ref var super = ref CollectionsMarshal_GetRef(_blockDevices, 0);

        ulong totalReserved = GetTotalMetadataSize(metadataMaxSize, metadataSlotCount);
        if (super.Size < totalReserved)
            return false;

        ulong freeAreaStart = totalReserved;
        if (super.Alignment != 0 || super.AlignmentOffset != 0)
            freeAreaStart = AlignTo(freeAreaStart, super.Alignment, super.AlignmentOffset);
        else
            freeAreaStart = AlignTo(freeAreaStart, logicalBlockSize);
        super.FirstLogicalSector = freeAreaStart / LP_SECTOR_SIZE;

        ulong minimumDisk = super.FirstLogicalSector * LP_SECTOR_SIZE + logicalBlockSize;
        if (super.Size < minimumDisk)
            return false;

        _geometry.MetadataMaxSize = metadataMaxSize;
        _geometry.MetadataSlotCount = metadataSlotCount;
        _geometry.LogicalBlockSize = logicalBlockSize;

        return AddGroup(KDefaultGroup, 0);
    }

    // ────────── 扩容 ──────────

    private bool GrowPartition(Partition partition, ulong alignedSize)
    {
        ulong spaceNeeded = alignedSize - partition.Size;
        ulong sectorsNeeded = spaceNeeded / LP_SECTOR_SIZE;

        var freeRegions = GetFreeRegions();
        ulong sectorsPerBlock = _geometry.LogicalBlockSize / LP_SECTOR_SIZE;
        if (sectorsPerBlock == 0) return false;

        var newExtents = new List<LinearExtent>();

        // 尝试扩展最后一个 extent 的尾巴
        if (partition.Extents.Count > 0 && partition.Extents[^1] is LinearExtent lastExt)
        {
            var bd = _blockDevices[(int)lastExt.DeviceIndex];
            ulong nextAligned = AlignSector(bd, lastExt.EndSector);
            if (lastExt.EndSector != nextAligned)
            {
                ulong extra = Math.Min(nextAligned - lastExt.EndSector, sectorsNeeded);
                var ext = new LinearExtent(extra, lastExt.DeviceIndex, lastExt.EndSector);
                if (!IsAnyRegionAllocated(ext))
                {
                    sectorsNeeded -= extra;
                    newExtents.Add(ext);
                }
            }
        }

        foreach (var region in freeRegions)
        {
            if (sectorsNeeded == 0) break;

            ulong len = region.Length;
            if (len % sectorsPerBlock != 0)
            {
                len = len / sectorsPerBlock * sectorsPerBlock;
                if (len == 0) continue;
            }

            ulong sectors = Math.Min(sectorsNeeded, len);
            newExtents.Add(new LinearExtent(sectors, region.DeviceIndex, region.Start));
            sectorsNeeded -= sectors;
        }

        if (sectorsNeeded > 0)
            return false;

        foreach (var ext in newExtents)
            partition.AddExtent(ext);
        return true;
    }

    // ────────── 自由区间 ──────────

    private List<Interval> GetFreeRegions()
    {
        var freeRegions = new List<Interval>();
        var deviceExtents = new List<List<Interval>>(_blockDevices.Count);
        for (int i = 0; i < _blockDevices.Count; i++)
            deviceExtents.Add(new List<Interval>());

        foreach (var partition in _partitions)
        foreach (var extent in partition.Extents)
        {
            if (extent is LinearExtent le)
                deviceExtents[(int)le.DeviceIndex].Add(le.AsInterval());
        }

        for (int i = 0; i < _blockDevices.Count; i++)
        {
            var bd = _blockDevices[i];
            ulong firstSector = bd.FirstLogicalSector;
            ulong lastSector = bd.Size / LP_SECTOR_SIZE;

            var extents = deviceExtents[i];
            extents.Add(new Interval((uint)i, firstSector, firstSector));
            extents.Add(new Interval((uint)i, lastSector, lastSector));
            extents.Sort();

            for (int j = 1; j < extents.Count; j++)
            {
                var prev = extents[j - 1];
                var cur = extents[j];
                ulong aligned = AlignSector(bd, prev.End);
                if (aligned >= cur.Start) continue;
                freeRegions.Add(new Interval(cur.DeviceIndex, aligned, cur.Start));
            }
        }

        return freeRegions;
    }

    private ulong AlignSector(LpMetadataBlockDevice bd, ulong sector)
    {
        ulong lba = sector * LP_SECTOR_SIZE;
        ulong aligned = AlignTo(lba, bd.Alignment, bd.AlignmentOffset);
        return AlignTo(aligned, LP_SECTOR_SIZE) / LP_SECTOR_SIZE;
    }

    // ────────── 校验 ──────────

    private bool ValidatePartitionSizeChange(Partition partition, ulong oldSize, ulong newSize)
    {
        if (newSize <= oldSize) return true;
        var group = FindGroup(partition.GroupName);
        if (group == null) return false;
        if (group.MaximumSize == 0) return true;

        ulong spaceNeeded = newSize - oldSize;
        ulong groupSize = TotalSizeOfGroup(group);
        return groupSize + spaceNeeded <= group.MaximumSize;
    }

    private ulong TotalSizeOfGroup(PartitionGroup group) =>
        _partitions.Where(p => p.GroupName == group.Name).Aggregate(0UL, (s, p) => s + p.BytesOnDisk());

    private bool ValidatePartitionGroups()
    {
        foreach (var group in _groups)
        {
            if (group.MaximumSize == 0) continue;
            if (TotalSizeOfGroup(group) > group.MaximumSize)
                return false;
        }
        return true;
    }

    private bool IsAnyRegionAllocated(LinearExtent candidate)
    {
        foreach (var partition in _partitions)
        foreach (var extent in partition.Extents)
        {
            if (extent is LinearExtent le && le.OverlapsWith(candidate))
                return true;
        }
        return false;
    }

    // ────────── 辅助 ──────────

    private LpMetadataHeader CloneHeader() => new()
    {
        Magic = _header.Magic,
        MajorVersion = _header.MajorVersion,
        MinorVersion = _header.MinorVersion,
        HeaderSize = _header.HeaderSize,
        Flags = _header.Flags
    };

    // 避免引入 CollectionsMarshal 的 AOT 问题, 用简单索引操作替代
    private static ref LpMetadataBlockDevice CollectionsMarshal_GetRef(List<LpMetadataBlockDevice> list, int index)
    {
        // 通过 Span 方式直接引用 list 内部数组
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
        return ref span[index];
    }
}
