using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace WpfApp1;

internal sealed record OfpExtractProgress(double Percent, string Message);

internal static class OppoOfpExtractor
{
    private const int CopyBufferSize = 2 * 1024 * 1024;
    private static readonly byte[] HeaderShuffleKey = Encoding.ASCII.GetBytes("geyixue");

    private static readonly string[][] QcKeyTables =
    [
        ["27827963787265EF89D126B69A495A21", "82C50203285A2CE7D8C3E198383CE94C", "422DD5399181E223813CD8ECDF2E4D72"],
        ["E11AA7BB558A436A8375FD15DDD4651F", "77DDF6A0696841F6B74782C097835169", "A739742384A44E8BA45207AD5C3700EA"],
        ["67657963787565E837D226B69A495D21", "F6C50203515A2CE7D8C3E1F938B7E94C", "42F2D5399137E2B2813CD8ECDF2F4D72"],
        ["3C2D518D9BF2E4279DC758CD535147C3", "87C74A29709AC1BF2382276C4E8DF232", "598D92E967265E9BCABE2469FE4A915E"],
        ["8FB8FB261930260BE945B841AEFA9FD4", "E529E82B28F5A2F8831D860AE39E425D", "8A09DA60ED36F125D64709973372C1CF"],
        ["E8AE288C0192C54BF10C5707E9C4705B", "D64FC385DCD52A3C9B5FBA8650F92EDA", "79051FD8D8B6297E2E4559E997F63B7F"]
    ];

    private static readonly string[][] MtkKeyTables =
    [
        ["67657963787565E837D226B69A495D21", "F6C50203515A2CE7D8C3E1F938B7E94C", "42F2D5399137E2B2813CD8ECDF2F4D72"],
        ["9E4F32639D21357D37D226B69A495D21", "A3D8D358E42F5A9E931DD3917D9A3218", "386935399137416B67416BECF22F519A"],
        ["892D57E92A4D8A975E3C216B7C9DE189", "D26DF2D9913785B145D18C7219B89F26", "516989E4A1BFC78B365C6BC57D944391"],
        ["27827963787265EF89D126B69A495A21", "82C50203285A2CE7D8C3E198383CE94C", "422DD5399181E223813CD8ECDF2E4D72"],
        ["3C4A618D9BF2E4279DC758CD535147C3", "87B13D29709AC1BF2382276C4E8DF232", "59B7A8E967265E9BCABE2469FE4A915E"],
        ["1C3288822BF824259DC852C1733127D3", "E7918D22799181CF2312176C9E2DF298", "3247F889A7B6DECBCA3E28693E4AAAFE"],
        ["1E4F32239D65A57D37D2266D9A775D43", "A332D3C3E42F5A3E931DD991729A321D", "3F2A35399A373377674155ECF28FD19A"],
        ["122D57E92A518AFF5E3C786B7C34E189", "DD6DF2D9543785674522717219989FB0", "12698965A132C76136CC88C5DD94EE91"],
        ["ab3f76d7989207f2", "2bf515b3a9737835"]
    ];

    public static Task ExtractAsync(
        string sourcePath,
        string outputDirectory,
        IProgress<OfpExtractProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => Extract(sourcePath, outputDirectory, progress, cancellationToken),
            cancellationToken);
    }

    private static void Extract(
        string sourcePath,
        string outputDirectory,
        IProgress<OfpExtractProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        Span<byte> signature = stackalloc byte[3];
        ReadExactly(input, signature);
        input.Position = 0;

        if (signature[0] == (byte)'P' && signature[1] == (byte)'K')
        {
            ExtractEncryptedZip(sourcePath, outputDirectory, progress, cancellationToken);
            return;
        }

        if (TryGetMtkKey(input, out byte[]? mtkKey, out byte[]? mtkIv))
        {
            progress?.Report(new OfpExtractProgress(2, "识别为联发科 OFP"));
            ExtractMtk(input, outputDirectory, mtkKey!, mtkIv!, progress, cancellationToken);
            return;
        }

        progress?.Report(new OfpExtractProgress(2, "正在识别高通 OFP 密钥"));
        ExtractQualcomm(input, outputDirectory, progress, cancellationToken);
    }

    private static void ExtractEncryptedZip(
        string sourcePath,
        string outputDirectory,
        IProgress<OfpExtractProgress>? progress,
        CancellationToken cancellationToken)
    {
        const string password = "flash@realme$50E7F7D847732396F1582CD62DD385ED7ABB0897";
        using IArchive archive = ArchiveFactory.Open(
            sourcePath,
            new SharpCompress.Readers.ReaderOptions { Password = password });
        var entries = archive.Entries.Where(entry => !entry.IsDirectory).ToList();

        for (int index = 0; index < entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[index];
            string destination = GetSafeOutputPath(outputDirectory, entry.Key ?? $"file_{index}");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using Stream source = entry.OpenEntryStream();
            using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            source.CopyTo(target, CopyBufferSize);
            progress?.Report(new OfpExtractProgress(
                Percent(index + 1, entries.Count),
                $"已解包 {Path.GetFileName(destination)}"));
        }
    }

    private static void ExtractMtk(
        FileStream input,
        string outputDirectory,
        byte[] key,
        byte[] iv,
        IProgress<OfpExtractProgress>? progress,
        CancellationToken cancellationToken)
    {
        const int headerLength = 0x6C;
        if (input.Length < headerLength)
        {
            throw new InvalidDataException("OFP 文件过小，无法读取联发科文件表。");
        }

        byte[] header = new byte[headerLength];
        input.Position = input.Length - headerLength;
        ReadExactly(input, header);
        MtkShuffle(HeaderShuffleKey, header);

        ushort entryCount = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(72, 2));
        long tableLength = checked(entryCount * 0x60L);
        if (entryCount == 0 || tableLength > input.Length - headerLength)
        {
            throw new InvalidDataException("联发科 OFP 文件表无效。");
        }

        byte[] table = new byte[checked((int)tableLength)];
        input.Position = input.Length - headerLength - tableLength;
        ReadExactly(input, table);
        MtkShuffle(HeaderShuffleKey, table);

        for (int index = 0; index < entryCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> entry = table.AsSpan(index * 0x60, 0x60);
            long start = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(entry.Slice(32, 8)));
            long length = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(entry.Slice(40, 8)));
            long encryptedLength = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(entry.Slice(48, 8)));
            string fileName = ReadCString(entry.Slice(56, 32));

            ValidateRange(input.Length, start, length);
            string destination = GetSafeOutputPath(outputDirectory, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);

            input.Position = start;
            long safeEncryptedLength = Math.Min(encryptedLength, length);
            if (safeEncryptedLength > 0)
            {
                byte[] encrypted = new byte[checked((int)safeEncryptedLength)];
                ReadExactly(input, encrypted);
                byte[] decrypted = AesCfbDecrypt(encrypted, key, iv);
                output.Write(decrypted, 0, encrypted.Length);
            }

            CopyRange(input, output, length - safeEncryptedLength, cancellationToken);
            progress?.Report(new OfpExtractProgress(
                Percent(index + 1, entryCount),
                $"已解包 {fileName}"));
        }
    }

    private static void ExtractQualcomm(
        FileStream input,
        string outputDirectory,
        IProgress<OfpExtractProgress>? progress,
        CancellationToken cancellationToken)
    {
        int pageSize = FindQcPageSize(input);
        if (pageSize == 0)
        {
            throw new InvalidDataException("无法识别 OFP 页大小或文件格式。");
        }

        byte[]? selectedKey = null;
        byte[]? selectedIv = null;
        string? xml = null;

        foreach (string[] table in QcKeyTables)
        {
            (byte[] key, byte[] iv) = DeriveKey(table);
            if (TryReadQcXml(input, pageSize, key, iv, out xml))
            {
                selectedKey = key;
                selectedIv = iv;
                break;
            }
        }

        if (selectedKey == null || selectedIv == null || string.IsNullOrWhiteSpace(xml))
        {
            throw new InvalidDataException("未找到匹配的高通 OFP 密钥，该固件格式可能过新。");
        }

        File.WriteAllText(GetSafeOutputPath(outputDirectory, "ProFile.xml"), xml, new UTF8Encoding(false));
        XDocument document = XDocument.Parse(xml, LoadOptions.None);
        List<QcEntry> entries = ParseQcEntries(document, pageSize);
        int superEntryCount = entries.Count(entry =>
            Path.GetFileName(entry.FileName).StartsWith("super", StringComparison.OrdinalIgnoreCase));

        progress?.Report(new OfpExtractProgress(
            3,
            superEntryCount > 0
                ? $"识别到 {entries.Count} 个文件，其中 super 分段 {superEntryCount} 个"
                : $"识别到 {entries.Count} 个文件，未发现 super 分段"));

        for (int index = 0; index < entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            QcEntry entry = entries[index];
            ValidateRange(input.Length, entry.Start, entry.ReadLength);
            string destination = GetSafeOutputPath(outputDirectory, entry.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);

            input.Position = entry.Start;
            if (entry.CopyWithoutDecrypt)
            {
                CopyRange(input, output, entry.ReadLength, cancellationToken);
            }
            else
            {
                long decryptLength = Math.Min(entry.DecryptLength, entry.ReadLength);
                byte[] encrypted = new byte[checked((int)decryptLength)];
                ReadExactly(input, encrypted);
                byte[] decrypted = AesCfbDecrypt(encrypted, selectedKey, selectedIv);
                output.Write(decrypted, 0, encrypted.Length);
                CopyRange(input, output, entry.ReadLength - decryptLength, cancellationToken);
            }

            progress?.Report(new OfpExtractProgress(
                Percent(index + 1, entries.Count),
                $"已解包 {entry.FileName}"));
        }
    }

    private static List<QcEntry> ParseQcEntries(XDocument document, int pageSize)
    {
        var result = new List<QcEntry>();
        XElement root = document.Root ?? throw new InvalidDataException("ProFile.xml 缺少根节点。");

        foreach (XElement item in root.Descendants().Where(HasFileName))
        {
            string category = item.Parent?.Name.LocalName ?? string.Empty;

            AddQcEntry(result, item, category, pageSize);
        }

        return result
            .GroupBy(entry => new { entry.FileName, entry.Start, entry.ReadLength })
            .Select(group => group.First())
            .ToList();
    }

    private static void AddQcEntry(List<QcEntry> entries, XElement item, string category, int pageSize)
    {
        string fileName = Attribute(item, "Path") ?? Attribute(item, "filename") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        long start = -1;
        if (TryLongAttribute(item, "FileOffsetInSrc", out long offset))
        {
            start = checked(offset * pageSize);
        }
        else if (TryLongAttribute(item, "SizeInSectorInSrc", out long sectorOffset))
        {
            start = checked(sectorOffset * pageSize);
        }

        if (start < 0)
        {
            return;
        }

        TryLongAttribute(item, "SizeInByteInSrc", out long rawLength);
        long readLength = rawLength;
        if (TryLongAttribute(item, "SizeInSectorInSrc", out long sectors) && sectors > 0)
        {
            readLength = checked(sectors * pageSize);
        }

        bool rawLengthCategory = category is "Config" or "Provision" or "ChainedTableOfDigests" or "DigestsToSign" or "Firmware";
        if (rawLengthCategory && rawLength > 0)
        {
            readLength = rawLength;
        }

        bool copyWithoutDecrypt = category is "DigestsToSign" or "ChainedTableOfDigests" or "Firmware";
        long decryptLength = category == "Sahara" ? rawLength : Math.Min(0x40000, rawLength);
        if (readLength > 0)
        {
            entries.Add(new QcEntry(fileName, start, readLength, Math.Max(0, decryptLength), copyWithoutDecrypt));
        }
    }

    private static bool TryGetMtkKey(FileStream input, out byte[]? key, out byte[]? iv)
    {
        byte[] encryptedHeader = new byte[16];
        input.Position = 0;
        ReadExactly(input, encryptedHeader);

        foreach (string[] table in MtkKeyTables)
        {
            (byte[] candidateKey, byte[] candidateIv) = DeriveKey(table);
            byte[] decrypted = AesCfbDecrypt(encryptedHeader, candidateKey, candidateIv);
            if (decrypted.AsSpan(0, 3).SequenceEqual("MMM"u8))
            {
                key = candidateKey;
                iv = candidateIv;
                input.Position = 0;
                return true;
            }
        }

        key = null;
        iv = null;
        input.Position = 0;
        return false;
    }

    private static int FindQcPageSize(FileStream input)
    {
        foreach (int pageSize in new[] { 0x200, 0x1000 })
        {
            long position = input.Length - pageSize + 0x10;
            if (position < 0)
            {
                continue;
            }

            Span<byte> value = stackalloc byte[4];
            input.Position = position;
            ReadExactly(input, value);
            if (BinaryPrimitives.ReadUInt32LittleEndian(value) == 0x7CEF)
            {
                return pageSize;
            }
        }

        return 0;
    }

    private static bool TryReadQcXml(
        FileStream input,
        int pageSize,
        byte[] key,
        byte[] iv,
        out string? xml)
    {
        long xmlHeader = input.Length - pageSize;
        Span<byte> metadata = stackalloc byte[8];
        input.Position = xmlHeader + 0x14;
        ReadExactly(input, metadata);
        long offset = checked(BinaryPrimitives.ReadUInt32LittleEndian(metadata[..4]) * (long)pageSize);
        long length = BinaryPrimitives.ReadUInt32LittleEndian(metadata[4..]);
        if (length < 200)
        {
            length = xmlHeader - offset - 0x57;
        }

        if (length <= 0 || length > int.MaxValue || offset < 0 || offset + length > input.Length)
        {
            xml = null;
            return false;
        }

        byte[] encrypted = new byte[(int)length];
        input.Position = offset;
        ReadExactly(input, encrypted);
        byte[] decrypted = AesCfbDecrypt(encrypted, key, iv);
        int xmlStart = FindSequence(decrypted, "<?xml"u8);
        int xmlEnd = Array.LastIndexOf(decrypted, (byte)'>');
        if (xmlStart < 0 || xmlEnd < xmlStart)
        {
            xml = null;
            return false;
        }

        xml = Encoding.UTF8.GetString(decrypted, xmlStart, xmlEnd - xmlStart + 1);
        return true;
    }

    private static (byte[] Key, byte[] Iv) DeriveKey(string[] table)
    {
        if (table.Length == 2)
        {
            return (Encoding.ASCII.GetBytes(table[0]), Encoding.ASCII.GetBytes(table[1]));
        }

        byte[] mask = Convert.FromHexString(table[0]);
        byte[] encodedKey = Convert.FromHexString(table[1]);
        byte[] encodedIv = Convert.FromHexString(table[2]);
        byte[] keySource = MtkShuffle2(mask, encodedKey);
        byte[] ivSource = MtkShuffle2(mask, encodedIv);
        return (Md5HexPrefix(keySource), Md5HexPrefix(ivSource));
    }

    private static byte[] AesCfbDecrypt(byte[] input, byte[] key, byte[] iv)
    {
        IBufferedCipher cipher = new BufferedBlockCipher(new CfbBlockCipher(new AesEngine(), 128));
        cipher.Init(false, new ParametersWithIV(new KeyParameter(key), iv));
        byte[] output = new byte[cipher.GetOutputSize(input.Length)];
        int length = cipher.ProcessBytes(input, 0, input.Length, output, 0);
        length += cipher.DoFinal(output, length);
        return length == output.Length ? output : output[..length];
    }

    private static byte[] Md5HexPrefix(byte[] value)
    {
        string hex = Convert.ToHexString(MD5.HashData(value)).ToLowerInvariant();
        return Encoding.ASCII.GetBytes(hex[..16]);
    }

    private static byte[] MtkShuffle2(byte[] key, byte[] input)
    {
        byte[] output = input.ToArray();
        for (int index = 0; index < output.Length; index++)
        {
            byte mixed = (byte)(key[index % key.Length] ^ output[index]);
            output[index] = SwapNibbles(mixed);
        }

        return output;
    }

    private static void MtkShuffle(byte[] key, byte[] data)
    {
        for (int index = 0; index < data.Length; index++)
        {
            data[index] = (byte)(key[index % key.Length] ^ SwapNibbles(data[index]));
        }
    }

    private static byte SwapNibbles(byte value)
    {
        return (byte)(((value & 0x0F) << 4) | ((value & 0xF0) >> 4));
    }

    private static void CopyRange(
        Stream input,
        Stream output,
        long length,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[CopyBufferSize];
        while (length > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int requested = (int)Math.Min(buffer.Length, length);
            int read = input.Read(buffer, 0, requested);
            if (read == 0)
            {
                throw new EndOfStreamException("OFP 文件在读取镜像数据时意外结束。");
            }

            output.Write(buffer, 0, read);
            length -= read;
        }
    }

    private static string GetSafeOutputPath(string outputDirectory, string relativePath)
    {
        string root = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        string destination = Path.GetFullPath(Path.Combine(root, normalized));
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"OFP 包含不安全的输出路径: {relativePath}");
        }

        return destination;
    }

    private static void ValidateRange(long fileLength, long start, long length)
    {
        if (start < 0 || length < 0 || start > fileLength || length > fileLength - start)
        {
            throw new InvalidDataException("OFP 文件表包含越界的镜像范围。");
        }
    }

    private static string ReadCString(ReadOnlySpan<byte> value)
    {
        int end = value.IndexOf((byte)0);
        if (end < 0)
        {
            end = value.Length;
        }

        return Encoding.UTF8.GetString(value[..end]);
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = stream.Read(buffer[offset..]);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }
    }

    private static void ReadExactly(Stream stream, byte[] buffer)
    {
        ReadExactly(stream, buffer.AsSpan());
    }

    private static int FindSequence(byte[] source, ReadOnlySpan<byte> sequence)
    {
        for (int index = 0; index <= source.Length - sequence.Length; index++)
        {
            if (source.AsSpan(index, sequence.Length).SequenceEqual(sequence))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool HasFileName(XElement element)
    {
        return Attribute(element, "Path") != null || Attribute(element, "filename") != null;
    }

    private static string? Attribute(XElement element, string name)
    {
        return element.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static bool TryLongAttribute(XElement element, string name, out long value)
    {
        return long.TryParse(Attribute(element, name), out value);
    }

    private static double Percent(int current, int total)
    {
        return total <= 0 ? 100 : current * 100d / total;
    }

    private sealed record QcEntry(
        string FileName,
        long Start,
        long ReadLength,
        long DecryptLength,
        bool CopyWithoutDecrypt);
}
