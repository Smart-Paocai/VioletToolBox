using System.Security.Cryptography;

namespace SuperFix.Core;

public static class EmptySuperImageVerifier
{
    public static EmptySuperVerification VerifyFile(string imagePath)
    {
        string fullPath = Path.GetFullPath(imagePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("未找到空 Super 镜像", fullPath);

        LpMetadataTemplate template = LpMetadataTemplateReader.Read(fullPath);
        if (template.Partitions.Count != 0 || template.Extents.Count != 0)
            throw new InvalidDataException("镜像不是空 Super：仍包含逻辑分区或 extent");
        if (template.Groups.Count != 1 ||
            !template.Groups[0].Name.Equals("default", StringComparison.Ordinal))
        {
            throw new InvalidDataException("空 Super 必须只包含 default group");
        }
        if (template.BlockDevices.Count != 1 ||
            !template.BlockDevices[0].Name.Equals("super", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("空 Super 必须只包含名为 super 的 block device");
        }

        long expectedLength = LpConstants.GetTotalMetadataSize(
            template.Geometry.MetadataMaxSize,
            template.Geometry.MetadataSlotCount);
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"空 Super 文件长度错误: {stream.Length} != {expectedLength}");
        }

        byte[] primaryGeometry = ReadAt(stream, LpConstants.ReservedBytes, LpConstants.GeometryBlockSize);
        byte[] backupGeometry = ReadAt(
            stream,
            LpConstants.ReservedBytes + LpConstants.GeometryBlockSize,
            LpConstants.GeometryBlockSize);
        if (!primaryGeometry.AsSpan().SequenceEqual(backupGeometry))
            throw new InvalidDataException("Primary/Backup geometry 不一致");

        byte[]? referenceMetadata = null;
        for (uint slot = 0; slot < template.Geometry.MetadataSlotCount; slot++)
        {
            VerifyMetadataCopy(
                stream,
                LpConstants.GetPrimaryMetadataOffset(template.Geometry.MetadataMaxSize, slot),
                template.Geometry.MetadataMaxSize,
                ref referenceMetadata,
                $"Primary slot {slot}");
            VerifyMetadataCopy(
                stream,
                LpConstants.GetBackupMetadataOffset(
                    template.Geometry.MetadataMaxSize,
                    template.Geometry.MetadataSlotCount,
                    slot),
                template.Geometry.MetadataMaxSize,
                ref referenceMetadata,
                $"Backup slot {slot}");
        }

        return new EmptySuperVerification(
            template.BlockDevices[0].Size,
            template.Geometry.MetadataMaxSize,
            template.Geometry.MetadataSlotCount,
            template.Geometry.LogicalBlockSize,
            template.Header.MajorVersion,
            template.Header.MinorVersion,
            template.Header.Flags,
            stream.Length,
            ComputeSha256(fullPath));
    }

    public static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void VerifyMetadataCopy(
        FileStream stream,
        long offset,
        uint metadataMaxSize,
        ref byte[]? reference,
        string label)
    {
        byte[] copy = ReadAt(stream, offset, checked((int)metadataMaxSize));
        if (BinaryHelpers.ReadUInt32(copy, 0) != LpConstants.HeaderMagic)
            throw new InvalidDataException($"{label} header magic 无效");
        uint headerSize = BinaryHelpers.ReadUInt32(copy, 8);
        if (headerSize is not (LpConstants.HeaderV10Size or LpConstants.HeaderV12Size))
            throw new InvalidDataException($"{label} header_size 无效");
        uint tablesSize = BinaryHelpers.ReadUInt32(copy, 44);
        if ((ulong)headerSize + tablesSize > metadataMaxSize)
            throw new InvalidDataException($"{label} metadata 越界");

        byte[] header = copy.AsSpan(0, (int)headerSize).ToArray();
        byte[] expectedHeaderChecksum = header.AsSpan(12, 32).ToArray();
        header.AsSpan(12, 32).Clear();
        if (!BinaryHelpers.FixedTimeEquals(expectedHeaderChecksum, BinaryHelpers.Sha256(header)))
            throw new InvalidDataException($"{label} header SHA-256 校验失败");
        ReadOnlySpan<byte> tables = copy.AsSpan((int)headerSize, (int)tablesSize);
        if (!BinaryHelpers.FixedTimeEquals(copy.AsSpan(48, 32), BinaryHelpers.Sha256(tables)))
            throw new InvalidDataException($"{label} tables SHA-256 校验失败");

        if (BinaryHelpers.ReadUInt32(copy, 84) != 0 ||
            BinaryHelpers.ReadUInt32(copy, 96) != 0 ||
            BinaryHelpers.ReadUInt32(copy, 108) != 1 ||
            BinaryHelpers.ReadUInt32(copy, 120) != 1)
        {
            throw new InvalidDataException($"{label} 不是 0 partition/0 extent/1 group/1 device");
        }

        if (reference == null)
            reference = copy;
        else if (!reference.AsSpan().SequenceEqual(copy))
            throw new InvalidDataException($"{label} 与其他 metadata 副本不一致");
    }

    private static byte[] ReadAt(FileStream stream, long offset, int count)
    {
        if (offset < 0 || count < 0 || offset > stream.Length - count)
            throw new EndOfStreamException("读取空 Super 时超出文件范围");
        byte[] buffer = new byte[count];
        stream.Position = offset;
        stream.ReadExactly(buffer);
        return buffer;
    }
}
