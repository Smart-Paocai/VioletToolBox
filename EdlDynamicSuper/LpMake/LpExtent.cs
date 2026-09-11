namespace AFlashTool.Modular.Super.LpMake;

/// <summary>
/// dm-target 抽象, 可被编码进 logical partition 表
/// </summary>
public abstract class Extent
{
    public ulong NumSectors { get; set; }

    protected Extent(ulong numSectors) { NumSectors = numSectors; }

    /// <summary>追加到 metadata.extents 表</summary>
    public abstract bool AddTo(LpMetadata output);

    public virtual LinearExtent? AsLinearExtent() => null;
}

/// <summary>dm-linear extent (实际占用 super 上的连续扇区)</summary>
public sealed class LinearExtent : Extent
{
    public uint DeviceIndex { get; }
    public ulong PhysicalSector { get; }

    public LinearExtent(ulong numSectors, uint deviceIndex, ulong physicalSector)
        : base(numSectors)
    {
        DeviceIndex = deviceIndex;
        PhysicalSector = physicalSector;
    }

    public ulong EndSector => PhysicalSector + NumSectors;

    public override bool AddTo(LpMetadata output)
    {
        if (DeviceIndex >= output.BlockDevices.Count)
            return false;
        output.Extents.Add(new LpMetadataExtent
        {
            NumSectors = NumSectors,
            TargetType = LpMakeConstants.LP_TARGET_TYPE_LINEAR,
            TargetData = PhysicalSector,
            TargetSource = DeviceIndex
        });
        return true;
    }

    public override LinearExtent AsLinearExtent() => this;

    public bool OverlapsWith(LinearExtent other) =>
        DeviceIndex == other.DeviceIndex &&
        PhysicalSector < other.EndSector && other.PhysicalSector < EndSector;

    public bool OverlapsWith(Interval interval) =>
        DeviceIndex == interval.DeviceIndex &&
        PhysicalSector < interval.End && interval.Start < EndSector;

    public Interval AsInterval() => new(DeviceIndex, PhysicalSector, EndSector);
}

/// <summary>dm-zero extent (虚拟全零, 不占 super 实际空间)</summary>
public sealed class ZeroExtent : Extent
{
    public ZeroExtent(ulong numSectors) : base(numSectors) { }

    public override bool AddTo(LpMetadata output)
    {
        output.Extents.Add(new LpMetadataExtent
        {
            NumSectors = NumSectors,
            TargetType = LpMakeConstants.LP_TARGET_TYPE_ZERO,
            TargetData = 0,
            TargetSource = 0
        });
        return true;
    }
}

/// <summary>
/// 自由/已用区间 (按 device 内的扇区计数)
/// </summary>
public readonly record struct Interval(uint DeviceIndex, ulong Start, ulong End)
    : IComparable<Interval>
{
    public ulong Length => End - Start;

    public int CompareTo(Interval other)
    {
        if (Start != other.Start)
            return Start.CompareTo(other.Start);
        return End.CompareTo(other.End);
    }

    public Extent AsExtent() => new LinearExtent(Length, DeviceIndex, Start);

    public static Interval Intersect(Interval a, Interval b)
    {
        if (a.DeviceIndex != b.DeviceIndex)
            return new Interval(a.DeviceIndex, a.Start, a.Start);
        ulong start = Math.Max(a.Start, b.Start);
        ulong end = Math.Max(start, Math.Min(a.End, b.End));
        return new Interval(a.DeviceIndex, start, end);
    }

    public static List<Interval> Intersect(List<Interval> a, List<Interval> b)
    {
        var ret = new List<Interval>();
        foreach (var ai in a)
        foreach (var bi in b)
        {
            var ix = Intersect(ai, bi);
            if (ix.Length > 0) ret.Add(ix);
        }
        return ret;
    }
}
