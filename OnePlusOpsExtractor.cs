using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace WpfApp1;

internal sealed record OpsExtractProgress(double Percent, string Message);

internal static class OnePlusOpsExtractor
{
    private const int SectorSize = 0x200;
    private const int CopyBufferSize = 2 * 1024 * 1024;

    private static readonly uint[] InitialKey =
    [
        0x9EE3B5D1,
        0x9D04EA5E,
        0xABD51D67,
        0xAFCBAFD2
    ];

    private static readonly byte[] Mbox5 =
    [
        0x60, 0x8A, 0x3F, 0x2D, 0x68, 0x6B, 0xD4, 0x23,
        0x51, 0x0C, 0xD0, 0x95, 0xBB, 0x40, 0xE9, 0x76,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x0A, 0
    ];

    private static readonly byte[] Mbox6 =
    [
        0xAA, 0x69, 0x82, 0x9E, 0x5D, 0xDE, 0xB1, 0x3D,
        0x30, 0xBB, 0x81, 0xA3, 0x46, 0x65, 0xA3, 0xE1,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x0A, 0
    ];

    private static readonly byte[] Mbox4 =
    [
        0xC4, 0x5D, 0x05, 0x71, 0x99, 0xDD, 0xBB, 0xEE,
        0x29, 0xA1, 0x6D, 0xC7, 0xAD, 0xBF, 0xA4, 0x3F,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x0A, 0
    ];

    private static readonly byte[] AesSBox =
    [
        0x63,0x7C,0x77,0x7B,0xF2,0x6B,0x6F,0xC5,0x30,0x01,0x67,0x2B,0xFE,0xD7,0xAB,0x76,
        0xCA,0x82,0xC9,0x7D,0xFA,0x59,0x47,0xF0,0xAD,0xD4,0xA2,0xAF,0x9C,0xA4,0x72,0xC0,
        0xB7,0xFD,0x93,0x26,0x36,0x3F,0xF7,0xCC,0x34,0xA5,0xE5,0xF1,0x71,0xD8,0x31,0x15,
        0x04,0xC7,0x23,0xC3,0x18,0x96,0x05,0x9A,0x07,0x12,0x80,0xE2,0xEB,0x27,0xB2,0x75,
        0x09,0x83,0x2C,0x1A,0x1B,0x6E,0x5A,0xA0,0x52,0x3B,0xD6,0xB3,0x29,0xE3,0x2F,0x84,
        0x53,0xD1,0x00,0xED,0x20,0xFC,0xB1,0x5B,0x6A,0xCB,0xBE,0x39,0x4A,0x4C,0x58,0xCF,
        0xD0,0xEF,0xAA,0xFB,0x43,0x4D,0x33,0x85,0x45,0xF9,0x02,0x7F,0x50,0x3C,0x9F,0xA8,
        0x51,0xA3,0x40,0x8F,0x92,0x9D,0x38,0xF5,0xBC,0xB6,0xDA,0x21,0x10,0xFF,0xF3,0xD2,
        0xCD,0x0C,0x13,0xEC,0x5F,0x97,0x44,0x17,0xC4,0xA7,0x7E,0x3D,0x64,0x5D,0x19,0x73,
        0x60,0x81,0x4F,0xDC,0x22,0x2A,0x90,0x88,0x46,0xEE,0xB8,0x14,0xDE,0x5E,0x0B,0xDB,
        0xE0,0x32,0x3A,0x0A,0x49,0x06,0x24,0x5C,0xC2,0xD3,0xAC,0x62,0x91,0x95,0xE4,0x79,
        0xE7,0xC8,0x37,0x6D,0x8D,0xD5,0x4E,0xA9,0x6C,0x56,0xF4,0xEA,0x65,0x7A,0xAE,0x08,
        0xBA,0x78,0x25,0x2E,0x1C,0xA6,0xB4,0xC6,0xE8,0xDD,0x74,0x1F,0x4B,0xBD,0x8B,0x8A,
        0x70,0x3E,0xB5,0x66,0x48,0x03,0xF6,0x0E,0x61,0x35,0x57,0xB9,0x86,0xC1,0x1D,0x9E,
        0xE1,0xF8,0x98,0x11,0x69,0xD9,0x8E,0x94,0x9B,0x1E,0x87,0xE9,0xCE,0x55,0x28,0xDF,
        0x8C,0xA1,0x89,0x0D,0xBF,0xE6,0x42,0x68,0x41,0x99,0x2D,0x0F,0xB0,0x54,0xBB,0x16
    ];

    private static readonly byte[] SBoxTable = BuildSBoxTable();

    public static Task ExtractAsync(
        string sourcePath,
        string outputDirectory,
        IProgress<OpsExtractProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () => Extract(sourcePath, outputDirectory, progress, cancellationToken),
            cancellationToken);
    }

    private static void Extract(
        string sourcePath,
        string outputDirectory,
        IProgress<OpsExtractProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        (byte[] mbox, int version, string settingsXml) = DetectMboxAndReadSettings(input);

        string settingsPath = GetSafeOutputPath(outputDirectory, "settings.xml");
        File.WriteAllText(settingsPath, settingsXml, new UTF8Encoding(false));
        progress?.Report(new OpsExtractProgress(2, $"识别为 MBox {version}，已解密 settings.xml"));

        XDocument document = XDocument.Parse(settingsXml, LoadOptions.None);
        List<OpsEntry> entries = ParseEntries(document);
        if (entries.Count == 0)
        {
            throw new InvalidDataException("settings.xml 中没有可提取的文件。");
        }

        for (int index = 0; index < entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpsEntry entry = entries[index];
            ValidateRange(input.Length, entry.Start, entry.Length);

            string destination = GetSafeOutputPath(outputDirectory, entry.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            input.Position = entry.Start;

            if (entry.IsEncrypted)
            {
                DecryptCustomStream(input, output, entry.Length, mbox, cancellationToken);
            }
            else
            {
                CopyRange(input, output, entry.Length, cancellationToken);
            }

            progress?.Report(new OpsExtractProgress(
                (index + 1) * 100d / entries.Count,
                $"已解包 {entry.FileName}"));
        }
    }

    private static (byte[] Mbox, int Version, string Xml) DetectMboxAndReadSettings(FileStream input)
    {
        foreach ((byte[] mbox, int version) in new[] { (Mbox5, 5), (Mbox6, 6), (Mbox4, 4) })
        {
            if (TryReadSettings(input, mbox, out string? xml))
            {
                return (mbox, version, xml!);
            }
        }

        throw new InvalidDataException("无法识别 OPS 密钥，仅支持旧版 MBox 4、5、6 格式。");
    }

    private static bool TryReadSettings(FileStream input, byte[] mbox, out string? xml)
    {
        xml = null;
        if (input.Length < SectorSize)
        {
            return false;
        }

        Span<byte> footer = stackalloc byte[SectorSize];
        input.Position = input.Length - SectorSize;
        ReadExactly(input, footer);
        uint xmlLength = BinaryPrimitives.ReadUInt32LittleEndian(footer.Slice(0x18, 4));
        if (xmlLength == 0 || xmlLength > int.MaxValue)
        {
            return false;
        }

        long padding = SectorSize - (xmlLength % SectorSize);
        long encryptedLength = xmlLength + padding;
        long offset = input.Length - SectorSize - encryptedLength;
        if (offset < 0 || encryptedLength > int.MaxValue)
        {
            return false;
        }

        byte[] encrypted = new byte[(int)encryptedLength];
        input.Position = offset;
        ReadExactly(input, encrypted);
        byte[] decrypted = DecryptCustom(encrypted, mbox);
        ReadOnlySpan<byte> xmlBytes = decrypted.AsSpan(0, (int)xmlLength);
        if (xmlBytes.IndexOf("xml "u8) < 0)
        {
            return false;
        }

        try
        {
            string candidate = Encoding.UTF8.GetString(xmlBytes)
                .TrimStart('\uFEFF')
                .TrimEnd('\0');
            _ = XDocument.Parse(candidate, LoadOptions.None);
            xml = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static List<OpsEntry> ParseEntries(XDocument document)
    {
        var entries = new List<OpsEntry>();
        XElement root = document.Root ?? throw new InvalidDataException("settings.xml 缺少根节点。");

        foreach (XElement section in root.Elements())
        {
            string sectionName = section.Name.LocalName;
            if (sectionName.Equals("SAHARA", StringComparison.OrdinalIgnoreCase))
            {
                foreach (XElement item in section.Descendants().Where(item =>
                             item.Name.LocalName.Equals("File", StringComparison.OrdinalIgnoreCase)))
                {
                    AddEntry(entries, item, isEncrypted: true);
                }
            }
            else if (sectionName.Equals("UFS_PROVISION", StringComparison.OrdinalIgnoreCase))
            {
                foreach (XElement item in section.Descendants().Where(item =>
                             item.Name.LocalName.Equals("File", StringComparison.OrdinalIgnoreCase)))
                {
                    AddEntry(entries, item, isEncrypted: false);
                }
            }
            else if (sectionName.Contains("Program", StringComparison.OrdinalIgnoreCase))
            {
                foreach (XElement item in section.DescendantsAndSelf().Where(HasFileName))
                {
                    AddEntry(entries, item, isEncrypted: false);
                }
            }
        }

        return entries
            .GroupBy(entry => new { entry.FileName, entry.Start, entry.Length })
            .Select(group => group.First())
            .ToList();
    }

    private static void AddEntry(List<OpsEntry> entries, XElement item, bool isEncrypted)
    {
        string fileName = Attribute(item, "Path") ?? Attribute(item, "filename") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fileName) ||
            !TryLongAttribute(item, "FileOffsetInSrc", out long offsetSectors) ||
            !TryLongAttribute(item, "SizeInByteInSrc", out long length) ||
            length <= 0)
        {
            return;
        }

        entries.Add(new OpsEntry(
            fileName,
            checked(offsetSectors * SectorSize),
            length,
            isEncrypted));
    }

    private static byte[] DecryptCustom(byte[] input, byte[] mbox)
    {
        using var source = new MemoryStream(input, writable: false);
        using var destination = new MemoryStream(input.Length);
        DecryptCustomStream(source, destination, input.Length, mbox, CancellationToken.None);
        return destination.ToArray();
    }

    private static void DecryptCustomStream(
        Stream input,
        Stream output,
        long length,
        byte[] mbox,
        CancellationToken cancellationToken)
    {
        uint[] rollingKey = InitialKey.ToArray();
        byte[] encryptedBlock = new byte[16];
        byte[] decryptedBlock = new byte[16];
        bool useMbox = length > 15;

        while (length > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int blockLength = (int)Math.Min(16, length);
            Array.Clear(encryptedBlock);
            ReadExactly(input, encryptedBlock.AsSpan(0, blockLength));
            rollingKey = KeyUpdate(rollingKey, useMbox ? mbox : SBoxTable);

            for (int index = 0; index < 4; index++)
            {
                uint encryptedWord = BinaryPrimitives.ReadUInt32LittleEndian(
                    encryptedBlock.AsSpan(index * 4, 4));
                uint decryptedWord = rollingKey[index] ^ encryptedWord;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    decryptedBlock.AsSpan(index * 4, 4),
                    decryptedWord);
                rollingKey[index] = encryptedWord;
            }

            output.Write(decryptedBlock, 0, blockLength);
            length -= blockLength;
        }
    }

    private static uint[] KeyUpdate(uint[] iv, byte[] constants)
    {
        unchecked
        {
            uint d = iv[0] ^ constants[0];
            uint a = iv[1] ^ constants[1];
            uint b = iv[2] ^ constants[2];
            uint c = iv[3] ^ constants[3];

            uint e = GetSBox((int)(((b >> 16) & 0xFF) * 8 + 2))
                     ^ GetSBox((int)(((a >> 8) & 0xFF) * 8 + 3))
                     ^ GetSBox((int)((c >> 24) * 8 + 1))
                     ^ GetSBox((int)((d & 0xFF) * 8))
                     ^ constants[4];
            uint h = GetSBox((int)(((c >> 16) & 0xFF) * 8 + 2))
                     ^ GetSBox((int)(((b >> 8) & 0xFF) * 8 + 3))
                     ^ GetSBox((int)((d >> 24) * 8 + 1))
                     ^ GetSBox((int)((a & 0xFF) * 8))
                     ^ constants[5];
            uint i = GetSBox((int)(((d >> 16) & 0xFF) * 8 + 2))
                     ^ GetSBox((int)(((c >> 8) & 0xFF) * 8 + 3))
                     ^ GetSBox((int)((a >> 24) * 8 + 1))
                     ^ GetSBox((int)((b & 0xFF) * 8))
                     ^ constants[6];
            a = GetSBox((int)(((d >> 8) & 0xFF) * 8 + 3))
                ^ GetSBox((int)(((a >> 16) & 0xFF) * 8 + 2))
                ^ GetSBox((int)((b >> 24) * 8 + 1))
                ^ GetSBox((int)((c & 0xFF) * 8))
                ^ constants[7];

            int g = 8;
            int rounds = constants[0x3C] - 2;
            for (int round = 0; round < rounds; round++)
            {
                uint oldE = e;
                uint oldH = h;
                uint oldI = i;
                uint oldA = a;
                uint highE = oldE >> 24;
                uint middleH = oldH >> 16;
                uint highH = oldH >> 24;
                uint middleE = oldE >> 16;
                uint highI = oldI >> 24;
                uint byteE = oldE >> 8;

                e = GetSBox((int)(((oldI >> 16) & 0xFF) * 8 + 2))
                    ^ GetSBox((int)(((oldH >> 8) & 0xFF) * 8 + 3))
                    ^ GetSBox((int)((oldA >> 24) * 8 + 1))
                    ^ GetSBox((int)((oldE & 0xFF) * 8))
                    ^ constants[g];
                h = GetSBox((int)(((oldA >> 16) & 0xFF) * 8 + 2))
                    ^ GetSBox((int)(((oldI >> 8) & 0xFF) * 8 + 3))
                    ^ GetSBox((int)(highE * 8 + 1))
                    ^ GetSBox((int)((oldH & 0xFF) * 8))
                    ^ constants[g + 1];
                i = GetSBox((int)((middleE & 0xFF) * 8 + 2))
                    ^ GetSBox((int)(((oldA >> 8) & 0xFF) * 8 + 3))
                    ^ GetSBox((int)(highH * 8 + 1))
                    ^ GetSBox((int)((oldI & 0xFF) * 8))
                    ^ constants[g + 2];
                a = GetSBox((int)((byteE & 0xFF) * 8 + 3))
                    ^ GetSBox((int)((middleH & 0xFF) * 8 + 2))
                    ^ GetSBox((int)(highI * 8 + 1))
                    ^ GetSBox((int)((oldA & 0xFF) * 8))
                    ^ constants[g + 3];
                g += 4;
            }

            return
            [
                (GetSBox((int)(((i >> 16) & 0xFF) * 8)) & 0x00FF0000)
                ^ (GetSBox((int)(((h >> 8) & 0xFF) * 8 + 1)) & 0x0000FF00)
                ^ (GetSBox((int)((a >> 24) * 8 + 3)) & 0xFF000000)
                ^ (GetSBox((int)((e & 0xFF) * 8 + 2)) & 0x000000FF)
                ^ constants[g],
                (GetSBox((int)(((a >> 16) & 0xFF) * 8)) & 0x00FF0000)
                ^ (GetSBox((int)(((i >> 8) & 0xFF) * 8 + 1)) & 0x0000FF00)
                ^ (GetSBox((int)((e >> 24) * 8 + 3)) & 0xFF000000)
                ^ (GetSBox((int)((h & 0xFF) * 8 + 2)) & 0x000000FF)
                ^ constants[g + 3],
                (GetSBox((int)(((e >> 16) & 0xFF) * 8)) & 0x00FF0000)
                ^ (GetSBox((int)(((a >> 8) & 0xFF) * 8 + 1)) & 0x0000FF00)
                ^ (GetSBox((int)((h >> 24) * 8 + 3)) & 0xFF000000)
                ^ (GetSBox((int)((i & 0xFF) * 8 + 2)) & 0x000000FF)
                ^ constants[g + 2],
                (GetSBox((int)(((h >> 16) & 0xFF) * 8)) & 0x00FF0000)
                ^ (GetSBox((int)(((e >> 8) & 0xFF) * 8 + 1)) & 0x0000FF00)
                ^ (GetSBox((int)((i >> 24) * 8 + 3)) & 0xFF000000)
                ^ (GetSBox((int)((a & 0xFF) * 8 + 2)) & 0x000000FF)
                ^ constants[g + 1]
            ];
        }
    }

    private static byte[] BuildSBoxTable()
    {
        byte[] table = new byte[AesSBox.Length * 8];
        for (int index = 0; index < AesSBox.Length; index++)
        {
            byte value = AesSBox[index];
            byte doubled = XTime(value);
            byte tripled = (byte)(doubled ^ value);
            Span<byte> entry = table.AsSpan(index * 8, 8);
            entry[0] = doubled;
            entry[1] = value;
            entry[2] = value;
            entry[3] = tripled;
            entry[4] = doubled;
            entry[5] = value;
            entry[6] = value;
            entry[7] = tripled;
        }

        return table;
    }

    private static byte XTime(byte value)
    {
        return (byte)((value << 1) ^ ((value & 0x80) != 0 ? 0x1B : 0));
    }

    private static uint GetSBox(int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(SBoxTable.AsSpan(offset, 4));
    }

    private static bool HasFileName(XElement element)
    {
        return !string.IsNullOrWhiteSpace(Attribute(element, "Path") ?? Attribute(element, "filename"));
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

    private static string GetSafeOutputPath(string outputDirectory, string relativePath)
    {
        string root = Path.GetFullPath(outputDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        string destination = Path.GetFullPath(Path.Combine(root, normalized));
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"OPS 包含不安全的输出路径: {relativePath}");
        }

        return destination;
    }

    private static void ValidateRange(long fileLength, long start, long length)
    {
        if (start < 0 || length < 0 || start > fileLength || length > fileLength - start)
        {
            throw new InvalidDataException("OPS 文件表包含越界的镜像范围。");
        }
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
                throw new EndOfStreamException("OPS 文件在读取镜像数据时意外结束。");
            }

            output.Write(buffer, 0, read);
            length -= read;
        }
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

    private sealed record OpsEntry(string FileName, long Start, long Length, bool IsEncrypted);
}
