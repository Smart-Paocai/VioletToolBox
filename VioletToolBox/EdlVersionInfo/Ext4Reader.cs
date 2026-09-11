using System.Text;

namespace Yu.Modular.Qualcomm.Firehose.Parsers;

/// <summary>
/// ext4 文件系统只读解析器 - 通过 IByteReader 逐扇区读取, 定位并读取指定路径的文件
/// 仅支持 extent-based 布局 (Android 默认), 支持 64-bit feature
/// </summary>
public class Ext4Reader
{
    private const ushort EXT4_MAGIC = 0xEF53;
    private const int SUPERBLOCK_OFFSET = 1024;
    private const int ROOT_INODE = 2;
    private const uint INCOMPAT_EXTENTS = 0x0040;
    private const uint INCOMPAT_64BIT = 0x0080;
    private const ushort EXTENT_MAGIC = 0xF30A;

    private readonly IByteReader _reader;

    // Superblock 字段
    private int _blockSize;
    private int _inodeSize;
    private int _inodesPerGroup;
    private int _descSize;
    private bool _has64Bit;
    private bool _hasExtents;
    private bool _valid;

    public bool IsValid => _valid;

    public Ext4Reader(IByteReader reader)
    {
        _reader = reader;
        ParseSuperblock();
    }

    /// <summary>
    /// 检测数据是否为 ext4 文件系统
    /// </summary>
    public static bool Detect(IByteReader reader)
    {
        var magic = reader.ReadBytes(SUPERBLOCK_OFFSET + 0x38, 2);
        if (magic == null) return false;
        return BitConverter.ToUInt16(magic, 0) == EXT4_MAGIC;
    }

    /// <summary>
    /// 读取指定路径的文件内容 (如 "build.prop" 或 "etc/build.prop")
    /// </summary>
    public byte[]? ReadFile(string path)
    {
        if (!_valid) return null;

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var currentInode = ROOT_INODE;

        for (var i = 0; i < parts.Length; i++)
        {
            var dirData = ReadInodeData(currentInode);
            if (dirData == null) return null;

            var found = FindDirEntry(dirData, parts[i]);
            if (found == null) return null;

            currentInode = found.Value;
        }

        return ReadInodeData(currentInode);
    }

    private void ParseSuperblock()
    {
        var sb = _reader.ReadBytes(SUPERBLOCK_OFFSET, 256);
        if (sb == null) return;

        var magic = BitConverter.ToUInt16(sb, 0x38);
        if (magic != EXT4_MAGIC) return;

        var logBlockSize = BitConverter.ToUInt32(sb, 0x18);
        _blockSize = (int)(1024U << (int)logBlockSize);
        _inodesPerGroup = (int)BitConverter.ToUInt32(sb, 0x28);
        _inodeSize = BitConverter.ToUInt16(sb, 0x58);
        if (_inodeSize == 0) _inodeSize = 128;

        var featureIncompat = BitConverter.ToUInt32(sb, 0x60);
        _hasExtents = (featureIncompat & INCOMPAT_EXTENTS) != 0;
        _has64Bit = (featureIncompat & INCOMPAT_64BIT) != 0;
        _descSize = _has64Bit ? BitConverter.ToUInt16(sb, 0xFE) : 32;
        if (_descSize < 32) _descSize = 32;

        _valid = true;
    }

    private byte[]? ReadInodeData(int inodeNum)
    {
        var inode = ReadInode(inodeNum);
        if (inode == null) return null;

        var sizeLo = BitConverter.ToUInt32(inode, 0x04);
        var sizeHi = BitConverter.ToUInt32(inode, 0x6C);
        var fileSize = (long)sizeLo | ((long)sizeHi << 32);
        if (fileSize <= 0 || fileSize > 64 * 1024 * 1024)
            return fileSize == 0 ? Array.Empty<byte>() : null;

        var flags = BitConverter.ToUInt32(inode, 0x20);
        var iBlock = new byte[60];
        Buffer.BlockCopy(inode, 0x28, iBlock, 0, 60);

        if (_hasExtents && (flags & 0x00080000) != 0)
            return ReadExtentData(iBlock, fileSize);
        else
            return ReadDirectBlockData(iBlock, fileSize);
    }

    private byte[]? ReadExtentData(byte[] extentBlock, long fileSize)
    {
        var result = new byte[fileSize];
        var written = 0L;

        var ehMagic = BitConverter.ToUInt16(extentBlock, 0);
        if (ehMagic != EXTENT_MAGIC) return null;

        var ehEntries = BitConverter.ToUInt16(extentBlock, 2);
        var ehDepth = BitConverter.ToUInt16(extentBlock, 6);

        if (ehDepth == 0)
        {
            for (var i = 0; i < ehEntries && written < fileSize; i++)
            {
                var off = 12 + i * 12;
                var eeLen = BitConverter.ToUInt16(extentBlock, off + 4);
                var eeStartHi = BitConverter.ToUInt16(extentBlock, off + 6);
                var eeStartLo = BitConverter.ToUInt32(extentBlock, off + 8);
                var physBlock = (long)eeStartLo | ((long)eeStartHi << 32);
                var blockCount = eeLen > 32768 ? eeLen - 32768 : eeLen;

                for (var b = 0; b < blockCount && written < fileSize; b++)
                {
                    var toRead = (int)Math.Min(_blockSize, fileSize - written);
                    var data = _reader.ReadBytes((physBlock + b) * _blockSize, toRead);
                    if (data == null) return null;
                    Buffer.BlockCopy(data, 0, result, (int)written, toRead);
                    written += toRead;
                }
            }
        }
        else
        {
            for (var i = 0; i < ehEntries && written < fileSize; i++)
            {
                var off = 12 + i * 12;
                var leafLo = BitConverter.ToUInt32(extentBlock, off + 4);
                var leafHi = BitConverter.ToUInt16(extentBlock, off + 8);
                var leafBlock = (long)leafLo | ((long)leafHi << 32);

                var childData = _reader.ReadBytes(leafBlock * _blockSize, _blockSize);
                if (childData == null) return null;

                var childResult = ReadExtentDataFromBlock(childData, fileSize - written);
                if (childResult == null) return null;
                Buffer.BlockCopy(childResult, 0, result, (int)written, childResult.Length);
                written += childResult.Length;
            }
        }

        return result;
    }

    private byte[]? ReadExtentDataFromBlock(byte[] block, long maxSize)
    {
        var ehMagic = BitConverter.ToUInt16(block, 0);
        if (ehMagic != EXTENT_MAGIC) return null;

        var ehEntries = BitConverter.ToUInt16(block, 2);
        var ehDepth = BitConverter.ToUInt16(block, 6);

        if (ehDepth == 0)
        {
            var result = new MemoryStream();
            for (var i = 0; i < ehEntries && result.Length < maxSize; i++)
            {
                var off = 12 + i * 12;
                var eeLen = BitConverter.ToUInt16(block, off + 4);
                var eeStartHi = BitConverter.ToUInt16(block, off + 6);
                var eeStartLo = BitConverter.ToUInt32(block, off + 8);
                var physBlock = (long)eeStartLo | ((long)eeStartHi << 32);
                var blockCount = eeLen > 32768 ? eeLen - 32768 : eeLen;

                for (var b = 0; b < blockCount && result.Length < maxSize; b++)
                {
                    var toRead = (int)Math.Min(_blockSize, maxSize - result.Length);
                    var data = _reader.ReadBytes((physBlock + b) * _blockSize, toRead);
                    if (data == null) return null;
                    result.Write(data, 0, toRead);
                }
            }
            return result.ToArray();
        }
        else
        {
            var result = new MemoryStream();
            for (var i = 0; i < ehEntries && result.Length < maxSize; i++)
            {
                var off = 12 + i * 12;
                var leafLo = BitConverter.ToUInt32(block, off + 4);
                var leafHi = BitConverter.ToUInt16(block, off + 8);
                var leafBlock = (long)leafLo | ((long)leafHi << 32);

                var childData = _reader.ReadBytes(leafBlock * _blockSize, _blockSize);
                if (childData == null) return null;

                var childResult = ReadExtentDataFromBlock(childData, maxSize - result.Length);
                if (childResult == null) return null;
                result.Write(childResult, 0, childResult.Length);
            }
            return result.ToArray();
        }
    }

    private byte[]? ReadDirectBlockData(byte[] iBlock, long fileSize)
    {
        var result = new byte[fileSize];
        var written = 0L;

        for (var i = 0; i < 12 && written < fileSize; i++)
        {
            var blockNum = BitConverter.ToUInt32(iBlock, i * 4);
            if (blockNum == 0) break;
            var toRead = (int)Math.Min(_blockSize, fileSize - written);
            var data = _reader.ReadBytes((long)blockNum * _blockSize, toRead);
            if (data == null) return null;
            Buffer.BlockCopy(data, 0, result, (int)written, toRead);
            written += toRead;
        }

        return result;
    }

    private byte[]? ReadInode(int inodeNum)
    {
        if (inodeNum < 1) return null;

        var group = (inodeNum - 1) / _inodesPerGroup;
        var indexInGroup = (inodeNum - 1) % _inodesPerGroup;

        var descBlockStart = _blockSize == 1024 ? 2 * _blockSize : _blockSize;
        var descOffset = descBlockStart + group * _descSize;
        var desc = _reader.ReadBytes(descOffset, _descSize);
        if (desc == null) return null;

        var inodeTableLo = BitConverter.ToUInt32(desc, 8);
        var inodeTableBlock = (long)inodeTableLo;
        if (_has64Bit && _descSize >= 64)
        {
            var inodeTableHi = BitConverter.ToUInt32(desc, 40);
            inodeTableBlock |= (long)inodeTableHi << 32;
        }

        var inodeOffset = inodeTableBlock * _blockSize + (long)indexInGroup * _inodeSize;
        return _reader.ReadBytes(inodeOffset, _inodeSize);
    }

    private int? FindDirEntry(byte[] dirData, string name)
    {
        var pos = 0;
        while (pos + 8 <= dirData.Length)
        {
            var inode = BitConverter.ToUInt32(dirData, pos);
            var recLen = BitConverter.ToUInt16(dirData, pos + 4);
            var nameLen = dirData[pos + 6];

            if (recLen < 8 || pos + recLen > dirData.Length) break;
            if (recLen == 0) break;

            if (inode != 0 && nameLen > 0 && pos + 8 + nameLen <= dirData.Length)
            {
                var entryName = Encoding.UTF8.GetString(dirData, pos + 8, nameLen);
                if (entryName == name)
                    return (int)inode;
            }

            pos += recLen;
        }
        return null;
    }
}
