using System.Text;

namespace Yu.Modular.Qualcomm.Firehose.Parsers;

/// <summary>
/// erofs (Enhanced Read-Only File System) 只读解析器
/// 通过 IByteReader 逐扇区读取, 定位并读取指定路径的文件
/// 支持 FLAT_PLAIN 和 FLAT_INLINE 数据布局
/// </summary>
public class ErofsReader
{
    private const uint EROFS_MAGIC = 0xE0F5E1E2;
    private const int SUPERBLOCK_OFFSET = 1024;

    // Data layout types (i_format bits 1-3)
    private const int LAYOUT_FLAT_PLAIN = 0;
    private const int LAYOUT_COMPRESSED_FULL = 1;
    private const int LAYOUT_FLAT_INLINE = 2;
    private const int LAYOUT_COMPRESSED_COMPACT = 3;
    private const int LAYOUT_CHUNK_BASED = 4;

    private readonly IByteReader _reader;

    // Superblock 字段
    private int _blockSize;
    private int _blkSzBits;
    private uint _metaBlkAddr;
    private ushort _rootNid;
    private bool _zeroPadding;
    private bool _valid;

    public bool IsValid => _valid;

    public ErofsReader(IByteReader reader)
    {
        _reader = reader;
        ParseSuperblock();
    }

    /// <summary>
    /// 检测数据是否为 erofs 文件系统
    /// </summary>
    public static bool Detect(IByteReader reader)
    {
        var magic = reader.ReadBytes(SUPERBLOCK_OFFSET, 4);
        if (magic == null) return false;
        return BitConverter.ToUInt32(magic, 0) == EROFS_MAGIC;
    }

    /// <summary>
    /// 读取指定路径的文件 (如 "build.prop" 或 "etc/build.prop")
    /// </summary>
    public byte[]? ReadFile(string path)
    {
        if (!_valid) return null;

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var currentNid = (long)_rootNid;

        for (var i = 0; i < parts.Length; i++)
        {
            var dirData = ReadNidData(currentNid);
            if (dirData == null) return null;

            var found = FindDirEntry(dirData, parts[i]);
            if (found == null) return null;
            currentNid = found.Value;
        }

        return ReadNidData(currentNid);
    }

    private void ParseSuperblock()
    {
        var sb = _reader.ReadBytes(SUPERBLOCK_OFFSET, 128);
        if (sb == null) return;

        var magic = BitConverter.ToUInt32(sb, 0);
        if (magic != EROFS_MAGIC) return;

        _blkSzBits = sb[12];
        _blockSize = 1 << _blkSzBits;
        _rootNid = BitConverter.ToUInt16(sb, 14);
        var blocks = BitConverter.ToUInt32(sb, 36);
        _metaBlkAddr = BitConverter.ToUInt32(sb, 40);
        var featureIncompat = BitConverter.ToUInt32(sb, 80);

        _zeroPadding = (featureIncompat & 0x1) != 0;
        _valid = true;
    }

    private long NidToOffset(long nid) => (long)_metaBlkAddr * _blockSize + nid * 32;

    private byte[]? ReadNidData(long nid)
    {
        var inodeOff = NidToOffset(nid);
        var hdr = _reader.ReadBytes(inodeOff, 64);
        if (hdr == null) return null;

        var iFormat = BitConverter.ToUInt16(hdr, 0);
        var isExtended = (iFormat & 0x01) != 0;
        var dataLayout = (iFormat >> 1) & 0x07;

        var xattrCount = BitConverter.ToUInt16(hdr, 2);
        int inodeSize = isExtended ? 64 : 32;

        long fileSize;
        uint rawBlkAddr;
        if (isExtended)
        {
            fileSize = (long)BitConverter.ToUInt64(hdr, 8);
            rawBlkAddr = BitConverter.ToUInt32(hdr, 16);
        }
        else
        {
            fileSize = BitConverter.ToUInt32(hdr, 8);
            rawBlkAddr = BitConverter.ToUInt32(hdr, 16);
        }

        if (fileSize <= 0 || fileSize > 64 * 1024 * 1024)
            return fileSize == 0 ? Array.Empty<byte>() : null;

        var xattrSize = xattrCount > 0 ? 12 + (xattrCount - 1) * 4 : 0;
        var inlineDataOff = inodeOff + inodeSize + xattrSize;

        switch (dataLayout)
        {
            case LAYOUT_FLAT_PLAIN:
            {
                var dataOff = (long)rawBlkAddr * _blockSize;
                return _reader.ReadBytes(dataOff, (int)fileSize);
            }

            case LAYOUT_FLAT_INLINE:
            {
                var tailSize = (int)(fileSize % _blockSize);
                var blockDataSize = fileSize - tailSize;

                var result = new byte[fileSize];

                if (blockDataSize > 0)
                {
                    var blockData = _reader.ReadBytes((long)rawBlkAddr * _blockSize, (int)blockDataSize);
                    if (blockData == null) return null;
                    Buffer.BlockCopy(blockData, 0, result, 0, (int)blockDataSize);
                }

                if (tailSize > 0)
                {
                    var tailData = _reader.ReadBytes(inlineDataOff, tailSize);
                    if (tailData == null) return null;
                    Buffer.BlockCopy(tailData, 0, result, (int)blockDataSize, tailSize);
                }

                return result;
            }

            case LAYOUT_CHUNK_BASED:
            {
                var dataOff = (long)rawBlkAddr * _blockSize;
                return _reader.ReadBytes(dataOff, (int)fileSize);
            }

            case LAYOUT_COMPRESSED_FULL:
            case LAYOUT_COMPRESSED_COMPACT:
            {
                return ReadCompressedData(hdr, inodeOff, inodeSize, xattrSize,
                    rawBlkAddr, fileSize, dataLayout, isExtended, nid);
            }

            default:
                return null;
        }
    }

    private long? FindDirEntry(byte[] dirData, string name)
    {
        var pos = 0;
        while (pos < dirData.Length)
        {
            var blockStart = (pos / _blockSize) * _blockSize;
            var blockEnd = Math.Min(blockStart + _blockSize, dirData.Length);

            if (pos + 12 > blockEnd) { pos = blockEnd; continue; }

            var firstNameOff = BitConverter.ToUInt16(dirData, blockStart + 8);
            if (firstNameOff < 12 || blockStart + firstNameOff > blockEnd)
            {
                pos = blockEnd;
                continue;
            }

            var direntCount = firstNameOff / 12;
            for (var i = 0; i < direntCount; i++)
            {
                var dOff = blockStart + i * 12;
                if (dOff + 12 > blockEnd) break;

                var nid = BitConverter.ToUInt64(dirData, dOff);
                var nameOff = BitConverter.ToUInt16(dirData, dOff + 8);

                int nameEnd;
                if (i + 1 < direntCount)
                {
                    var nextDOff = blockStart + (i + 1) * 12;
                    nameEnd = blockStart + BitConverter.ToUInt16(dirData, nextDOff + 8);
                }
                else
                {
                    nameEnd = blockEnd;
                }

                var nameStart = blockStart + nameOff;
                if (nameStart >= blockEnd || nameEnd > dirData.Length) continue;

                var nameLen = nameEnd - nameStart;
                while (nameLen > 0 && dirData[nameStart + nameLen - 1] == 0)
                    nameLen--;

                if (nameLen > 0)
                {
                    var entryName = Encoding.UTF8.GetString(dirData, nameStart, nameLen);
                    if (entryName == name)
                        return (long)nid;
                }
            }

            pos = blockEnd;
        }

        return null;
    }

    // Lcluster types
    private const int LCLUSTER_TYPE_PLAIN = 0;
    private const int LCLUSTER_TYPE_HEAD1 = 1;
    private const int LCLUSTER_TYPE_NONHEAD = 2;
    private const int LCLUSTER_TYPE_HEAD2 = 3;

    private const int ADVISE_COMPACTED_2B = 0x0001;
    private const int ADVISE_BIG_PCLUSTER_1 = 0x0002;
    private const int ADVISE_BIG_PCLUSTER_2 = 0x0008;

    private byte[]? ReadCompressedData(byte[] hdr, long inodeOff, int inodeSize, int xattrSize,
        uint compressedBlocks, long fileSize, int dataLayout, bool isExtended, long nid)
    {
        var zIdataOff = inodeOff + inodeSize + xattrSize;
        var mapHdr = _reader.ReadBytes(zIdataOff, 8);
        if (mapHdr == null) return null;

        var hIdataSize = BitConverter.ToUInt16(mapHdr, 2);
        var hAdvise = BitConverter.ToUInt16(mapHdr, 4);
        var hAlgoType = mapHdr[6];
        var hClusterBits = mapHdr[7] & 0x0F;

        var algoPrimary = hAlgoType & 0x0F;
        if (algoPrimary != 0) return null; // 仅支持 LZ4=0

        var lclusterBits = _blkSzBits + hClusterBits;
        var lclusterSize = 1 << lclusterBits;
        var ebase = Align8(zIdataOff + 8);
        var totalidx = (int)((fileSize + lclusterSize - 1) >> lclusterBits);
        var compacted2b = (hAdvise & ADVISE_COMPACTED_2B) != 0;
        var bigPcl = (hAdvise & (ADVISE_BIG_PCLUSTER_1 | ADVISE_BIG_PCLUSTER_2)) != 0;

        if (hIdataSize > 0 && totalidx > 0)
        {
            var tailResult = TryReadZtailpacking(ebase, totalidx, lclusterBits,
                compacted2b, bigPcl, dataLayout, hIdataSize, fileSize, nid);
            if (tailResult != null)
                return tailResult;
        }

        int type0;
        long pblk0;
        if (dataLayout == LAYOUT_COMPRESSED_COMPACT)
        {
            (type0, _, pblk0, _) = LoadCompactLcluster(ebase, 0, totalidx, lclusterBits, compacted2b, bigPcl);
        }
        else
        {
            var fullIdx = _reader.ReadBytes(ebase, 8);
            if (fullIdx == null) return null;
            type0 = BitConverter.ToUInt16(fullIdx, 0) & 3;
            pblk0 = BitConverter.ToUInt32(fullIdx, 4);
        }

        if (pblk0 < 0) return null;

        var compBlks = Math.Max(1, (int)compressedBlocks);
        var compSize = compBlks * _blockSize;
        var compressedData = _reader.ReadBytes(pblk0 * _blockSize, compSize);
        if (compressedData == null) return null;

        if (type0 == LCLUSTER_TYPE_PLAIN)
        {
            return compressedData.Length >= (int)fileSize
                ? compressedData[..(int)fileSize] : compressedData;
        }

        var inputMargin = 0;
        if (_zeroPadding)
        {
            while (inputMargin < compressedData.Length && compressedData[inputMargin] == 0)
                inputMargin++;
        }

        var output = new byte[fileSize];
        var dec = Lz4Decompressor.Decompress(compressedData, inputMargin,
            compSize - inputMargin, output, 0, (int)fileSize);
        if (dec >= (int)fileSize) return output;

        var written = 0;
        for (var blk = 0; blk < compBlks && written < (int)fileSize; blk++)
        {
            var srcOff = blk * _blockSize;
            var srcLen = Math.Min(_blockSize, compressedData.Length - srcOff);
            if (srcLen <= 0) break;
            var decompSize = Math.Min(lclusterSize, (int)fileSize - written);
            dec = Lz4Decompressor.Decompress(compressedData, srcOff, srcLen, output, written, decompSize);
            if (dec > 0)
            {
                written += dec;
            }
            else
            {
                var toCopy = Math.Min(srcLen, decompSize);
                Buffer.BlockCopy(compressedData, srcOff, output, written, toCopy);
                written += toCopy;
            }
        }

        if (written >= (int)fileSize) return output;

        var altData = _reader.ReadBytes((pblk0 + 1) * _blockSize, compSize);
        if (altData != null)
        {
            var altMargin = 0;
            if (_zeroPadding)
            {
                while (altMargin < altData.Length && altData[altMargin] == 0)
                    altMargin++;
            }
            dec = Lz4Decompressor.Decompress(altData, altMargin, altData.Length - altMargin, output, 0, (int)fileSize);
            if (dec >= (int)fileSize) return output;
        }

        return written > 0 ? output[..written] : null;
    }

    private byte[]? TryReadZtailpacking(long ebase, int totalidx, int lclusterBits,
        bool compacted2b, bool bigPcl, int dataLayout, int idataSize, long fileSize, long nid)
    {
        long idataOff;
        int tailType;

        if (dataLayout == LAYOUT_COMPRESSED_COMPACT)
        {
            (tailType, _, _, idataOff) = LoadCompactLcluster(
                ebase, totalidx - 1, totalidx, lclusterBits, compacted2b, bigPcl);
        }
        else
        {
            idataOff = ebase + totalidx * 8;
            tailType = LCLUSTER_TYPE_HEAD1;
        }

        if (idataOff <= 0) return null;

        var compData = _reader.ReadBytes(idataOff, idataSize);
        if (compData == null) return null;

        if (tailType == LCLUSTER_TYPE_PLAIN)
        {
            if (idataSize >= (int)fileSize)
                return compData[..(int)fileSize];
        }

        var result = new byte[fileSize];
        var decoded = Lz4Decompressor.Decompress(compData, 0, idataSize, result, 0, (int)fileSize);
        if (decoded >= (int)fileSize) return result;

        if (idataSize >= (int)fileSize)
            return compData[..(int)fileSize];

        return null;
    }

    private (int type, int clusterofs, long pblk, long nextPackOff) LoadCompactLcluster(
        long ebase, int lcn, int totalidx, int lclusterBits, bool compacted2b, bool bigPcl)
    {
        var compacted4bInitial = (int)(((32 - ebase % 32) / 4) & 7);
        var compacted2bCount = 0;
        if (compacted2b && compacted4bInitial < totalidx)
            compacted2bCount = ((totalidx - compacted4bInitial) / 16) * 16;

        var pos = ebase;
        var amortizedShift = 2;
        var adjustedLcn = lcn;

        if (adjustedLcn >= compacted4bInitial)
        {
            pos += compacted4bInitial * 4;
            adjustedLcn -= compacted4bInitial;
            if (adjustedLcn < compacted2bCount)
            {
                amortizedShift = 1;
            }
            else
            {
                pos += compacted2bCount * 2;
                adjustedLcn -= compacted2bCount;
            }
        }
        pos += adjustedLcn * (1 << amortizedShift);

        int vcnt;
        if ((1 << amortizedShift) == 4 && lclusterBits <= 14)
            vcnt = 2;
        else if ((1 << amortizedShift) == 2 && lclusterBits <= 12)
            vcnt = 16;
        else
            return (-1, 0, -1, -1);

        var packSize = vcnt << amortizedShift;
        var bytesInPack = (int)(pos & (packSize - 1));
        var packStart = pos - bytesInPack;
        var i = bytesInPack >> amortizedShift;

        var packData = _reader.ReadBytes(packStart, packSize);
        if (packData == null) return (-1, 0, -1, -1);

        var nextPackOff = packStart + packSize;

        var lobits = Math.Max(lclusterBits, 12);
        var encodeBits = (packSize - 4) * 8 / vcnt;
        var (lo, type) = DecodeCompactedBits(lobits, packData, encodeBits * i);

        if (type == LCLUSTER_TYPE_NONHEAD)
            return (type, 1 << lclusterBits, -1, nextPackOff);

        int nblk;
        if (!bigPcl)
        {
            nblk = 1;
            var j = i;
            while (j > 0)
            {
                j--;
                var (lo2, type2) = DecodeCompactedBits(lobits, packData, encodeBits * j);
                if (type2 == LCLUSTER_TYPE_NONHEAD)
                    j -= lo2;
                if (j >= 0)
                    nblk++;
            }
        }
        else
        {
            nblk = 0;
            var j = i;
            while (j > 0)
            {
                j--;
                var (lo2, type2) = DecodeCompactedBits(lobits, packData, encodeBits * j);
                if (type2 == LCLUSTER_TYPE_NONHEAD)
                {
                    if ((lo2 & 0x800) != 0)
                    {
                        j--;
                        nblk += lo2 & ~0x800;
                        continue;
                    }
                    if (lo2 <= 1) break;
                    j -= lo2 - 2;
                    continue;
                }
                nblk++;
            }
        }

        var baseBlkAddr = BitConverter.ToUInt32(packData, packSize - 4);
        var pblk = (long)baseBlkAddr + nblk;

        return (type, lo, pblk, nextPackOff);
    }

    private static (int lo, int type) DecodeCompactedBits(int lobits, byte[] data, int bitPos)
    {
        var byteOff = bitPos / 8;
        var bitOff = bitPos & 7;

        uint v = 0;
        for (var k = 0; k < 4 && byteOff + k < data.Length; k++)
            v |= (uint)data[byteOff + k] << (k * 8);
        v >>= bitOff;

        var lo = (int)(v & ((1u << lobits) - 1));
        var type = (int)((v >> lobits) & 3);
        return (lo, type);
    }

    private static long Align8(long v) => (v + 7) & ~7L;
}
