namespace Yu.Modular.Qualcomm.Firehose.Parsers;

public interface IByteReader
{
    byte[]? ReadBytes(long offset, int length);
}

public sealed class CachedByteReader : IByteReader
{
    private readonly Func<long, int, byte[]?> _read;
    private readonly int _sectorSize;
    private readonly Dictionary<long, byte[]> _cache = new();

    public CachedByteReader(Func<long, int, byte[]?> read, int sectorSize)
    {
        _read = read;
        _sectorSize = sectorSize;
    }

    public byte[]? ReadBytes(long offset, int length)
    {
        if (offset < 0 || length < 0)
            return null;
        if (length == 0)
            return Array.Empty<byte>();

        long firstSector = offset / _sectorSize;
        long lastSector = checked((offset + length - 1) / _sectorSize);
        if (!EnsureCached(firstSector, lastSector))
            return null;

        byte[] result = new byte[length];
        int destinationOffset = 0;
        long currentOffset = offset;
        while (destinationOffset < length)
        {
            long sector = currentOffset / _sectorSize;
            int offsetInSector = (int)(currentOffset % _sectorSize);
            int copyLength = Math.Min(length - destinationOffset, _sectorSize - offsetInSector);
            Buffer.BlockCopy(_cache[sector], offsetInSector, result, destinationOffset, copyLength);
            currentOffset += copyLength;
            destinationOffset += copyLength;
        }
        return result;
    }

    private bool EnsureCached(long firstSector, long lastSector)
    {
        long sector = firstSector;
        while (sector <= lastSector)
        {
            if (_cache.ContainsKey(sector))
            {
                sector++;
                continue;
            }

            long batchStart = sector;
            while (sector <= lastSector && !_cache.ContainsKey(sector))
                sector++;

            int sectorCount = checked((int)(sector - batchStart));
            byte[]? data = _read(checked(batchStart * _sectorSize), checked(sectorCount * _sectorSize));
            if (data == null || data.Length < sectorCount * _sectorSize)
                return false;

            for (int i = 0; i < sectorCount; i++)
            {
                byte[] sectorData = new byte[_sectorSize];
                Buffer.BlockCopy(data, i * _sectorSize, sectorData, 0, _sectorSize);
                _cache[batchStart + i] = sectorData;
            }
        }
        return true;
    }
}

public sealed class LogicalPartitionReader : IByteReader
{
    private readonly IByteReader _superReader;
    private readonly IReadOnlyList<LpExtentMapping> _extents;
    private readonly int _sectorSize;

    public LogicalPartitionReader(
        IByteReader superReader,
        IReadOnlyList<LpExtentMapping> extents,
        int sectorSize)
    {
        _superReader = superReader;
        _extents = extents;
        _sectorSize = sectorSize;
    }

    public byte[]? ReadBytes(long offset, int length)
    {
        if (offset < 0 || length < 0)
            return null;
        if (length == 0)
            return Array.Empty<byte>();

        byte[] result = new byte[length];
        int destinationOffset = 0;
        long logicalOffset = offset;

        while (destinationOffset < length)
        {
            if (!FindExtent(logicalOffset, out LpExtentMapping? extent, out long offsetInExtent))
                return null;

            long extentBytes = checked(extent.NumSectors * _sectorSize);
            int copyLength = (int)Math.Min(length - destinationOffset, extentBytes - offsetInExtent);
            if (!extent.IsZero)
            {
                long physicalOffset = checked(extent.PhysicalSector * _sectorSize + offsetInExtent);
                byte[]? data = _superReader.ReadBytes(physicalOffset, copyLength);
                if (data == null || data.Length != copyLength)
                    return null;
                Buffer.BlockCopy(data, 0, result, destinationOffset, copyLength);
            }

            logicalOffset += copyLength;
            destinationOffset += copyLength;
        }

        return result;
    }

    private bool FindExtent(long logicalOffset, out LpExtentMapping? result, out long offsetInExtent)
    {
        long extentStart = 0;
        foreach (LpExtentMapping extent in _extents)
        {
            long extentBytes = checked(extent.NumSectors * _sectorSize);
            if (logicalOffset < extentStart + extentBytes)
            {
                result = extent;
                offsetInExtent = logicalOffset - extentStart;
                return true;
            }
            extentStart += extentBytes;
        }

        result = null;
        offsetInExtent = 0;
        return false;
    }
}

public sealed record LpExtentMapping(long PhysicalSector, long NumSectors, bool IsZero = false);
