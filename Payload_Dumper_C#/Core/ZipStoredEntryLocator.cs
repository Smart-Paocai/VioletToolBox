using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Payload_Dumper_C_.Core;

public static class ZipStoredEntryLocator
{
    private const uint EocdSignature = 0x06054b50;
    private const uint Zip64EocdLocatorSignature = 0x07064b50;
    private const uint Zip64EocdSignature = 0x06064b50;
    private const uint CentralDirFileHeaderSignature = 0x02014b50;
    private const uint LocalFileHeaderSignature = 0x04034b50;

    private const int EocdFixedSize = 22;
    private const int Zip64EocdLocatorSize = 20;
    private const int Zip64EocdSize = 56;
    private const int CentralDirFileHeaderFixedSize = 46;
    private const int LocalFileHeaderFixedSize = 30;
    private const int ZipMaxComment = (1 << 16) - 1;
    private const ushort CompressionStored = 0;

    public readonly record struct ZipEntryInfo(
        string Name,
        long CompressedSize,
        long UncompressedSize,
        ushort CompressionMethod,
        long LocalHeaderOffset);

    public static async ValueTask<(long DataOffset, long UncompressedSize)> TryGetStoredEntryAsync(
        IRandomAccessReader file,
        string entryName,
        CancellationToken cancellationToken)
    {
        long size = await file.GetSizeAsync(cancellationToken).ConfigureAwait(false);
        if (size < EocdFixedSize) throw new InvalidDataException("文件过小，无法包含 ZIP EOCD");

        long eocdOffset = -1;
        byte[] eocdData;

        {
            var tail = await file.ReadExactlyAsync(size - EocdFixedSize, EocdFixedSize, cancellationToken).ConfigureAwait(false);
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(0, 4)) == EocdSignature &&
                tail.AsSpan(EocdFixedSize - 2, 2).SequenceEqual(new byte[] { 0x00, 0x00 }))
            {
                eocdOffset = size - EocdFixedSize;
                eocdData = tail;
            }
            else
            {
                int trySize = ZipMaxComment + EocdFixedSize;
                long start = size - trySize;
                if (start < 0)
                {
                    start = 0;
                    trySize = (int)size;
                }

                var buf = await file.ReadExactlyAsync(start, trySize, cancellationToken).ConfigureAwait(false);

                for (int length = 1; length <= trySize - EocdFixedSize; length++)
                {
                    ushort commentLen = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(trySize - length - 2, 2));
                    if (commentLen != length) continue;

                    int candidate = trySize - length - EocdFixedSize;
                    if (BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(candidate, 4)) == EocdSignature)
                    {
                        eocdOffset = size - length - EocdFixedSize;
                        eocdData = buf.AsSpan(candidate, EocdFixedSize).ToArray();
                        goto FoundEocd;
                    }
                }

                throw new InvalidDataException("不是 ZIP 文件(未找到 EOCD)");
            }
        }

    FoundEocd:

        ushort totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(eocdData.AsSpan(10, 2));
        uint centralDirSize = BinaryPrimitives.ReadUInt32LittleEndian(eocdData.AsSpan(12, 4));
        uint centralDirOffset = BinaryPrimitives.ReadUInt32LittleEndian(eocdData.AsSpan(16, 4));

        bool isZip64 = totalEntries == 0xFFFF || centralDirSize == 0xFFFFFFFF || centralDirOffset == 0xFFFFFFFF;
        ulong cdEntries = totalEntries;
        ulong cdSize = centralDirSize;
        ulong cdOffset = centralDirOffset;

        if (isZip64)
        {
            long locatorOffset = eocdOffset - Zip64EocdLocatorSize;
            if (locatorOffset < 0) throw new InvalidDataException("ZIP64 Locator 偏移无效");

            var locator = await file.ReadExactlyAsync(locatorOffset, Zip64EocdLocatorSize, cancellationToken).ConfigureAwait(false);
            if (BinaryPrimitives.ReadUInt32LittleEndian(locator.AsSpan(0, 4)) != Zip64EocdLocatorSignature)
            {
                throw new InvalidDataException("ZIP64 EOCD Locator 签名不匹配");
            }

            ulong zip64EocdOffset = BinaryPrimitives.ReadUInt64LittleEndian(locator.AsSpan(8, 8));

            var zip64Eocd = await file.ReadExactlyAsync((long)zip64EocdOffset, Zip64EocdSize, cancellationToken).ConfigureAwait(false);
            if (BinaryPrimitives.ReadUInt32LittleEndian(zip64Eocd.AsSpan(0, 4)) != Zip64EocdSignature)
            {
                throw new InvalidDataException("ZIP64 EOCD 签名不匹配");
            }

            cdEntries = BinaryPrimitives.ReadUInt64LittleEndian(zip64Eocd.AsSpan(32, 8));
            cdSize = BinaryPrimitives.ReadUInt64LittleEndian(zip64Eocd.AsSpan(40, 8));
            cdOffset = BinaryPrimitives.ReadUInt64LittleEndian(zip64Eocd.AsSpan(48, 8));
        }

        if (cdSize > int.MaxValue) throw new InvalidDataException($"中央目录过大: {cdSize}");

        var centralDir = await file.ReadExactlyAsync((long)cdOffset, (int)cdSize, cancellationToken).ConfigureAwait(false);
        var targetNameBytes = Encoding.UTF8.GetBytes(entryName);

        int p = 0;
        for (ulong i = 0; i < cdEntries && p + CentralDirFileHeaderFixedSize <= centralDir.Length; i++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(centralDir.AsSpan(p, 4)) != CentralDirFileHeaderSignature)
            {
                throw new InvalidDataException($"中央目录项签名错误: i={i} p={p}");
            }

            ushort compression = BinaryPrimitives.ReadUInt16LittleEndian(centralDir.AsSpan(p + 10, 2));
            uint compressedSize32 = BinaryPrimitives.ReadUInt32LittleEndian(centralDir.AsSpan(p + 20, 4));
            uint uncompressedSize32 = BinaryPrimitives.ReadUInt32LittleEndian(centralDir.AsSpan(p + 24, 4));
            ushort fileNameLen = BinaryPrimitives.ReadUInt16LittleEndian(centralDir.AsSpan(p + 28, 2));
            ushort extraLen = BinaryPrimitives.ReadUInt16LittleEndian(centralDir.AsSpan(p + 30, 2));
            ushort commentLen = BinaryPrimitives.ReadUInt16LittleEndian(centralDir.AsSpan(p + 32, 2));
            uint lfhOffset32 = BinaryPrimitives.ReadUInt32LittleEndian(centralDir.AsSpan(p + 42, 4));

            int entryHeaderSize = CentralDirFileHeaderFixedSize + fileNameLen + extraLen + commentLen;
            int nameOffset = p + CentralDirFileHeaderFixedSize;
            int extraOffset = nameOffset + fileNameLen;

            if (nameOffset + fileNameLen > centralDir.Length) break;

            bool isTarget = centralDir.AsSpan(nameOffset, fileNameLen).SequenceEqual(targetNameBytes);

            ulong compressedSize = compressedSize32;
            ulong uncompressedSize = uncompressedSize32;
            ulong lfhOffset = lfhOffset32;

            if (isZip64 && extraLen > 0)
            {
                int ep = extraOffset;
                int ee = extraOffset + extraLen;
                while (ee - ep >= 4)
                {
                    ushort headerId = BinaryPrimitives.ReadUInt16LittleEndian(centralDir.AsSpan(ep, 2));
                    ushort fieldSize = BinaryPrimitives.ReadUInt16LittleEndian(centralDir.AsSpan(ep + 2, 2));
                    int fieldDataOff = ep + 4;
                    if (fieldDataOff + fieldSize > ee) break;

                    if (headerId == 0x0001)
                    {
                        int k = fieldDataOff;
                        if (uncompressedSize32 == 0xFFFFFFFF)
                        {
                            uncompressedSize = BinaryPrimitives.ReadUInt64LittleEndian(centralDir.AsSpan(k, 8));
                            k += 8;
                        }
                        if (compressedSize32 == 0xFFFFFFFF)
                        {
                            compressedSize = BinaryPrimitives.ReadUInt64LittleEndian(centralDir.AsSpan(k, 8));
                            k += 8;
                        }
                        if (lfhOffset32 == 0xFFFFFFFF)
                        {
                            lfhOffset = BinaryPrimitives.ReadUInt64LittleEndian(centralDir.AsSpan(k, 8));
                            k += 8;
                        }
                    }

                    ep += 4 + fieldSize;
                }
            }

            if (isTarget)
            {
                if (compression != CompressionStored)
                {
                    throw new InvalidDataException($"目标条目不是 Stored(未压缩): {entryName} compression={compression}");
                }

                var lfh = await file.ReadExactlyAsync((long)lfhOffset, LocalFileHeaderFixedSize, cancellationToken).ConfigureAwait(false);
                if (BinaryPrimitives.ReadUInt32LittleEndian(lfh.AsSpan(0, 4)) != LocalFileHeaderSignature)
                {
                    throw new InvalidDataException($"LocalFileHeader 签名不匹配: {lfhOffset}");
                }

                ushort lfhNameLen = BinaryPrimitives.ReadUInt16LittleEndian(lfh.AsSpan(26, 2));
                ushort lfhExtraLen = BinaryPrimitives.ReadUInt16LittleEndian(lfh.AsSpan(28, 2));
                long dataOffset = (long)lfhOffset + LocalFileHeaderFixedSize + lfhNameLen + lfhExtraLen;
                return (dataOffset, (long)uncompressedSize);
            }

            p += entryHeaderSize;
        }

        throw new FileNotFoundException($"ZIP 中未找到条目: {entryName}");
    }

    public static async ValueTask<(long DataOffset, long UncompressedSize)> TryGetStoredEntryEndsWithAsync(
        IRandomAccessReader file,
        string entrySuffix,
        CancellationToken cancellationToken)
    {
        long size = await file.GetSizeAsync(cancellationToken).ConfigureAwait(false);
        if (size < EocdFixedSize) throw new InvalidDataException("文件过小，无法包含 ZIP EOCD");

        long eocdOffset = -1;
        byte[] eocdData;

        {
            var tail = await file.ReadExactlyAsync(size - EocdFixedSize, EocdFixedSize, cancellationToken).ConfigureAwait(false);
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(0, 4)) == EocdSignature &&
                tail.AsSpan(EocdFixedSize - 2, 2).SequenceEqual(new byte[] { 0x00, 0x00 }))
            {
                eocdOffset = size - EocdFixedSize;
                eocdData = tail;
            }
            else
            {
                int trySize = ZipMaxComment + EocdFixedSize;
                long start = size - trySize;
                if (start < 0)
                {
                    start = 0;
                    trySize = (int)size;
                }

                var buf = await file.ReadExactlyAsync(start, trySize, cancellationToken).ConfigureAwait(false);

                for (int length = 1; length <= trySize - EocdFixedSize; length++)
                {
                    ushort commentLen = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(trySize - length - 2, 2));
                    if (commentLen != length) continue;

                    int candidate = trySize - length - EocdFixedSize;
                    if (BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(candidate, 4)) == EocdSignature)
                    {
                        eocdOffset = size - length - EocdFixedSize;
                        eocdData = buf.AsSpan(candidate, EocdFixedSize).ToArray();
                        goto FoundEocdSuffix;
                    }
                }

                throw new InvalidDataException("不是 ZIP 文件(未找到 EOCD)");
            }
        }

    FoundEocdSuffix:

        ushort totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(eocdData.AsSpan(10, 2));
        uint centralDirSize = BinaryPrimitives.ReadUInt32LittleEndian(eocdData.AsSpan(12, 4));
        uint centralDirOffset = BinaryPrimitives.ReadUInt32LittleEndian(eocdData.AsSpan(16, 4));

        bool isZip64 = totalEntries == 0xFFFF || centralDirSize == 0xFFFFFFFF || centralDirOffset == 0xFFFFFFFF;
        ulong cdEntries = totalEntries;
        ulong cdSize = centralDirSize;
        ulong cdOffset = centralDirOffset;

        if (isZip64)
        {
            long locatorOffset = eocdOffset - Zip64EocdLocatorSize;
            if (locatorOffset < 0) throw new InvalidDataException("ZIP64 Locator 偏移无效");

            var locator = await file.ReadExactlyAsync(locatorOffset, Zip64EocdLocatorSize, cancellationToken).ConfigureAwait(false);
            if (BinaryPrimitives.ReadUInt32LittleEndian(locator.AsSpan(0, 4)) != Zip64EocdLocatorSignature)
            {
                throw new InvalidDataException("ZIP64 EOCD Locator 签名不匹配");
            }

            ulong zip64EocdOffset = BinaryPrimitives.ReadUInt64LittleEndian(locator.AsSpan(8, 8));

            var zip64Eocd = await file.ReadExactlyAsync((long)zip64EocdOffset, Zip64EocdSize, cancellationToken).ConfigureAwait(false);
            if (BinaryPrimitives.ReadUInt32LittleEndian(zip64Eocd.AsSpan(0, 4)) != Zip64EocdSignature)
            {
                throw new InvalidDataException("ZIP64 EOCD 签名不匹配");
            }

            cdEntries = BinaryPrimitives.ReadUInt64LittleEndian(zip64Eocd.AsSpan(32, 8));
            cdSize = BinaryPrimitives.ReadUInt64LittleEndian(zip64Eocd.AsSpan(40, 8));
            cdOffset = BinaryPrimitives.ReadUInt64LittleEndian(zip64Eocd.AsSpan(48, 8));
        }

        if (cdSize > int.MaxValue) throw new InvalidDataException($"中央目录过大: {cdSize}");

        var centralDir = await file.ReadExactlyAsync((long)cdOffset, (int)cdSize, cancellationToken).ConfigureAwait(false);
        var suffixBytes = Encoding.UTF8.GetBytes(entrySuffix);

        int p = 0;
        for (ulong i = 0; i < cdEntries && p + CentralDirFileHeaderFixedSize <= centralDir.Length; i++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(centralDir.AsSpan(p, 4)) != CentralDirFileHeaderSignature)
            {
                throw new InvalidDataException($"中央目录项签名错误: i={i} p={p}");
            }

            ushort compression = BinaryPrimitives.ReadUInt16LittleEndian(centralDir.AsSpan(p + 10, 2));
            uint compressedSize32 = BinaryPrimitives.ReadUInt32LittleEndian(centralDir.AsSpan(p + 20, 4));
            uint uncompressedSize32 = BinaryPrimitives.ReadUInt32LittleEndian(centralDir.AsSpan(p + 24, 4));
            ushort fileNameLen = BinaryPrimitives.ReadUInt16LittleEndian(centralDir.AsSpan(p + 28, 2));
            ushort extraLen = BinaryPrimitives.ReadUInt16LittleEndian(centralDir.AsSpan(p + 30, 2));
            ushort commentLen = BinaryPrimitives.ReadUInt16LittleEndian(centralDir.AsSpan(p + 32, 2));
            uint lfhOffset32 = BinaryPrimitives.ReadUInt32LittleEndian(centralDir.AsSpan(p + 42, 4));

            int entryHeaderSize = CentralDirFileHeaderFixedSize + fileNameLen + extraLen + commentLen;
            int nameOffset = p + CentralDirFileHeaderFixedSize;
            int extraOffset = nameOffset + fileNameLen;

            if (nameOffset + fileNameLen > centralDir.Length) break;

            bool isTarget = false;
            if (fileNameLen >= suffixBytes.Length)
            {
                int suffixOffset = nameOffset + fileNameLen - suffixBytes.Length;
                isTarget = centralDir.AsSpan(suffixOffset, suffixBytes.Length).SequenceEqual(suffixBytes);
            }

            ulong compressedSize = compressedSize32;
            ulong uncompressedSize = uncompressedSize32;
            ulong lfhOffset = lfhOffset32;

            if (isZip64 && extraLen > 0)
            {
                int ep = extraOffset;
                int ee = extraOffset + extraLen;
                while (ee - ep >= 4)
                {
                    ushort headerId = BinaryPrimitives.ReadUInt16LittleEndian(centralDir.AsSpan(ep, 2));
                    ushort fieldSize = BinaryPrimitives.ReadUInt16LittleEndian(centralDir.AsSpan(ep + 2, 2));
                    int fieldDataOff = ep + 4;
                    if (fieldDataOff + fieldSize > ee) break;

                    if (headerId == 0x0001)
                    {
                        int k = fieldDataOff;
                        if (uncompressedSize32 == 0xFFFFFFFF)
                        {
                            uncompressedSize = BinaryPrimitives.ReadUInt64LittleEndian(centralDir.AsSpan(k, 8));
                            k += 8;
                        }
                        if (compressedSize32 == 0xFFFFFFFF)
                        {
                            compressedSize = BinaryPrimitives.ReadUInt64LittleEndian(centralDir.AsSpan(k, 8));
                            k += 8;
                        }
                        if (lfhOffset32 == 0xFFFFFFFF)
                        {
                            lfhOffset = BinaryPrimitives.ReadUInt64LittleEndian(centralDir.AsSpan(k, 8));
                            k += 8;
                        }
                    }

                    ep += 4 + fieldSize;
                }
            }

            if (isTarget)
            {
                if (compression != CompressionStored)
                {
                    throw new InvalidDataException($"目标条目不是 Stored(未压缩): *{entrySuffix}");
                }

                var lfh = await file.ReadExactlyAsync((long)lfhOffset, LocalFileHeaderFixedSize, cancellationToken).ConfigureAwait(false);
                if (BinaryPrimitives.ReadUInt32LittleEndian(lfh.AsSpan(0, 4)) != LocalFileHeaderSignature)
                {
                    throw new InvalidDataException($"LocalFileHeader 签名不匹配: {lfhOffset}");
                }

                ushort lfhNameLen = BinaryPrimitives.ReadUInt16LittleEndian(lfh.AsSpan(26, 2));
                ushort lfhExtraLen = BinaryPrimitives.ReadUInt16LittleEndian(lfh.AsSpan(28, 2));
                long dataOffset = (long)lfhOffset + LocalFileHeaderFixedSize + lfhNameLen + lfhExtraLen;
                return (dataOffset, (long)uncompressedSize);
            }

            p += entryHeaderSize;
        }

        throw new FileNotFoundException($"ZIP 中未找到以 {entrySuffix} 结尾的条目");
    }

    public static async ValueTask<IReadOnlyList<ZipEntryInfo>> ListEntriesAsync(
        IRandomAccessReader file,
        CancellationToken cancellationToken)
    {
        long size = await file.GetSizeAsync(cancellationToken).ConfigureAwait(false);
        if (size < EocdFixedSize) throw new InvalidDataException("文件过小，无法包含 ZIP EOCD");

        long eocdOffset = -1;
        byte[] eocdData;

        {
            var tail = await file.ReadExactlyAsync(size - EocdFixedSize, EocdFixedSize, cancellationToken).ConfigureAwait(false);
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(0, 4)) == EocdSignature &&
                tail.AsSpan(EocdFixedSize - 2, 2).SequenceEqual(new byte[] { 0x00, 0x00 }))
            {
                eocdOffset = size - EocdFixedSize;
                eocdData = tail;
            }
            else
            {
                int trySize = ZipMaxComment + EocdFixedSize;
                long start = size - trySize;
                if (start < 0)
                {
                    start = 0;
                    trySize = (int)size;
                }

                var buf = await file.ReadExactlyAsync(start, trySize, cancellationToken).ConfigureAwait(false);

                for (int length = 1; length <= trySize - EocdFixedSize; length++)
                {
                    ushort commentLen = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(trySize - length - 2, 2));
                    if (commentLen != length) continue;

                    int candidate = trySize - length - EocdFixedSize;
                    if (BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(candidate, 4)) == EocdSignature)
                    {
                        eocdOffset = size - length - EocdFixedSize;
                        eocdData = buf.AsSpan(candidate, EocdFixedSize).ToArray();
                        goto FoundEocdList;
                    }
                }

                throw new InvalidDataException("不是 ZIP 文件(未找到 EOCD)");
            }
        }

    FoundEocdList:

        ushort totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(eocdData.AsSpan(10, 2));
        uint centralDirSize = BinaryPrimitives.ReadUInt32LittleEndian(eocdData.AsSpan(12, 4));
        uint centralDirOffset = BinaryPrimitives.ReadUInt32LittleEndian(eocdData.AsSpan(16, 4));

        bool isZip64 = totalEntries == 0xFFFF || centralDirSize == 0xFFFFFFFF || centralDirOffset == 0xFFFFFFFF;
        ulong cdEntries = totalEntries;
        ulong cdSize = centralDirSize;
        ulong cdOffset = centralDirOffset;

        if (isZip64)
        {
            long locatorOffset = eocdOffset - Zip64EocdLocatorSize;
            if (locatorOffset < 0) throw new InvalidDataException("ZIP64 Locator 偏移无效");

            var locator = await file.ReadExactlyAsync(locatorOffset, Zip64EocdLocatorSize, cancellationToken).ConfigureAwait(false);
            if (BinaryPrimitives.ReadUInt32LittleEndian(locator.AsSpan(0, 4)) != Zip64EocdLocatorSignature)
            {
                throw new InvalidDataException("ZIP64 EOCD Locator 签名不匹配");
            }

            ulong zip64EocdOffset = BinaryPrimitives.ReadUInt64LittleEndian(locator.AsSpan(8, 8));

            var zip64Eocd = await file.ReadExactlyAsync((long)zip64EocdOffset, Zip64EocdSize, cancellationToken).ConfigureAwait(false);
            if (BinaryPrimitives.ReadUInt32LittleEndian(zip64Eocd.AsSpan(0, 4)) != Zip64EocdSignature)
            {
                throw new InvalidDataException("ZIP64 EOCD 签名不匹配");
            }

            cdEntries = BinaryPrimitives.ReadUInt64LittleEndian(zip64Eocd.AsSpan(32, 8));
            cdSize = BinaryPrimitives.ReadUInt64LittleEndian(zip64Eocd.AsSpan(40, 8));
            cdOffset = BinaryPrimitives.ReadUInt64LittleEndian(zip64Eocd.AsSpan(48, 8));
        }

        if (cdSize > int.MaxValue) throw new InvalidDataException($"中央目录过大: {cdSize}");

        var centralDir = await file.ReadExactlyAsync((long)cdOffset, (int)cdSize, cancellationToken).ConfigureAwait(false);
        var results = new List<ZipEntryInfo>();

        int p = 0;
        for (ulong i = 0; i < cdEntries && p + CentralDirFileHeaderFixedSize <= centralDir.Length; i++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(centralDir.AsSpan(p, 4)) != CentralDirFileHeaderSignature)
            {
                throw new InvalidDataException($"中央目录项签名错误: i={i} p={p}");
            }

            ushort compression = BinaryPrimitives.ReadUInt16LittleEndian(centralDir.AsSpan(p + 10, 2));
            uint compressedSize32 = BinaryPrimitives.ReadUInt32LittleEndian(centralDir.AsSpan(p + 20, 4));
            uint uncompressedSize32 = BinaryPrimitives.ReadUInt32LittleEndian(centralDir.AsSpan(p + 24, 4));
            ushort fileNameLen = BinaryPrimitives.ReadUInt16LittleEndian(centralDir.AsSpan(p + 28, 2));
            ushort extraLen = BinaryPrimitives.ReadUInt16LittleEndian(centralDir.AsSpan(p + 30, 2));
            ushort commentLen = BinaryPrimitives.ReadUInt16LittleEndian(centralDir.AsSpan(p + 32, 2));
            uint lfhOffset32 = BinaryPrimitives.ReadUInt32LittleEndian(centralDir.AsSpan(p + 42, 4));

            int entryHeaderSize = CentralDirFileHeaderFixedSize + fileNameLen + extraLen + commentLen;
            int nameOffset = p + CentralDirFileHeaderFixedSize;
            int extraOffset = nameOffset + fileNameLen;

            if (nameOffset + fileNameLen > centralDir.Length) break;

            ulong compressedSize = compressedSize32;
            ulong uncompressedSize = uncompressedSize32;
            ulong lfhOffset = lfhOffset32;

            if (isZip64 && extraLen > 0)
            {
                int ep = extraOffset;
                int ee = extraOffset + extraLen;
                while (ee - ep >= 4)
                {
                    ushort headerId = BinaryPrimitives.ReadUInt16LittleEndian(centralDir.AsSpan(ep, 2));
                    ushort fieldSize = BinaryPrimitives.ReadUInt16LittleEndian(centralDir.AsSpan(ep + 2, 2));
                    int fieldDataOff = ep + 4;
                    if (fieldDataOff + fieldSize > ee) break;

                    if (headerId == 0x0001)
                    {
                        int k = fieldDataOff;
                        if (uncompressedSize32 == 0xFFFFFFFF)
                        {
                            uncompressedSize = BinaryPrimitives.ReadUInt64LittleEndian(centralDir.AsSpan(k, 8));
                            k += 8;
                        }
                        if (compressedSize32 == 0xFFFFFFFF)
                        {
                            compressedSize = BinaryPrimitives.ReadUInt64LittleEndian(centralDir.AsSpan(k, 8));
                            k += 8;
                        }
                        if (lfhOffset32 == 0xFFFFFFFF)
                        {
                            lfhOffset = BinaryPrimitives.ReadUInt64LittleEndian(centralDir.AsSpan(k, 8));
                            k += 8;
                        }
                    }

                    ep += 4 + fieldSize;
                }
            }

            string name = Encoding.UTF8.GetString(centralDir, nameOffset, fileNameLen);

            results.Add(new ZipEntryInfo(name, (long)compressedSize, (long)uncompressedSize, compression, (long)lfhOffset));

            p += entryHeaderSize;
        }

        return results;
    }

    public static async Task ExtractEntryAsync(
        IRandomAccessReader file,
        ZipEntryInfo entry,
        Stream output,
        CancellationToken cancellationToken,
        IProgress<long>? progress = null)
    {
        if (entry.CompressedSize < 0 || entry.UncompressedSize < 0)
        {
            throw new InvalidDataException($"条目大小异常: {entry.Name}");
        }

        long dataOffset = await GetEntryDataOffsetAsync(file, entry.LocalHeaderOffset, cancellationToken).ConfigureAwait(false);

        if (entry.CompressionMethod == CompressionStored)
        {
            await CopyRangeAsync(file, dataOffset, entry.UncompressedSize, output, cancellationToken, progress).ConfigureAwait(false);
            return;
        }

        if (entry.CompressionMethod == 8)
        {
            using var slice = new RandomAccessSliceStream(file, dataOffset, entry.CompressedSize, progress: progress);
            using var deflate = new DeflateStream(slice, CompressionMode.Decompress, leaveOpen: false);
            await deflate.CopyToAsync(output, 1 << 20, cancellationToken).ConfigureAwait(false);
            return;
        }

        throw new InvalidDataException($"暂不支持的压缩方法: {entry.CompressionMethod} ({entry.Name})");
    }

    private static async Task<long> GetEntryDataOffsetAsync(
        IRandomAccessReader file,
        long localHeaderOffset,
        CancellationToken cancellationToken)
    {
        var lfh = await file.ReadExactlyAsync(localHeaderOffset, LocalFileHeaderFixedSize, cancellationToken).ConfigureAwait(false);
        if (BinaryPrimitives.ReadUInt32LittleEndian(lfh.AsSpan(0, 4)) != LocalFileHeaderSignature)
        {
            throw new InvalidDataException($"LocalFileHeader 签名不匹配: {localHeaderOffset}");
        }

        ushort lfhNameLen = BinaryPrimitives.ReadUInt16LittleEndian(lfh.AsSpan(26, 2));
        ushort lfhExtraLen = BinaryPrimitives.ReadUInt16LittleEndian(lfh.AsSpan(28, 2));
        return localHeaderOffset + LocalFileHeaderFixedSize + lfhNameLen + lfhExtraLen;
    }

    private static async Task CopyRangeAsync(
        IRandomAccessReader file,
        long offset,
        long length,
        Stream output,
        CancellationToken cancellationToken,
        IProgress<long>? progress = null)
    {
        if (length <= 0) return;

        byte[] buffer = new byte[1 << 20];
        long remaining = length;
        long pos = offset;
        long copied = 0;
        progress?.Report(0);

        while (remaining > 0)
        {
            int toRead = (int)System.Math.Min(buffer.Length, remaining);
            int r = await file.ReadAsync(pos, buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
            if (r <= 0) break;

            await output.WriteAsync(buffer.AsMemory(0, r), cancellationToken).ConfigureAwait(false);

            remaining -= r;
            pos += r;
            copied += r;
            progress?.Report(copied);
        }
    }

    private sealed class RandomAccessSliceStream : Stream
    {
        private readonly IRandomAccessReader _reader;
        private readonly long _baseOffset;
        private readonly long _length;
        private readonly IProgress<long>? _progress;
        private long _position;

        private readonly byte[] _buffer;
        private int _bufferOffset;
        private int _bufferCount;
        private bool _disposed;

        public RandomAccessSliceStream(IRandomAccessReader reader, long baseOffset, long length, int bufferSize = 1 << 20, IProgress<long>? progress = null)
        {
            _reader = reader;
            _baseOffset = baseOffset;
            _length = length;
            _buffer = new byte[bufferSize];
            _progress = progress;
            _progress?.Report(0);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsyncInternal(buffer, offset, count, cancellationToken);
        }

        private async Task<int> ReadAsyncInternal(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RandomAccessSliceStream));
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException();
            if (count == 0) return 0;
            if (_position >= _length) return 0;

            int totalRead = 0;

            while (count > 0)
            {
                if (_bufferCount > 0)
                {
                    int available = _bufferCount;
                    if (available > count) available = count;

                    Buffer.BlockCopy(_buffer, _bufferOffset, buffer, offset, available);
                    _bufferOffset += available;
                    _bufferCount -= available;
                    offset += available;
                    count -= available;
                    totalRead += available;
                    _position += available;
                    _progress?.Report(_position);

                    if (count == 0 || _position >= _length) break;
                }
                else
                {
                    long remaining = _length - _position;
                    if (remaining <= 0) break;

                    int toFill = (int)System.Math.Min(_buffer.Length, remaining);
                    var mem = new Memory<byte>(_buffer, 0, toFill);
                    int r = await _reader.ReadAsync(_baseOffset + _position, mem, cancellationToken).ConfigureAwait(false);
                    if (r <= 0) break;

                    _bufferOffset = 0;
                    _bufferCount = r;
                }
            }

            return totalRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            _disposed = true;
            base.Dispose(disposing);
        }
    }
}
