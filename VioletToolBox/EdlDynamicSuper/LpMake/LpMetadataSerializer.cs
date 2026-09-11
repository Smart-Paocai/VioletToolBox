namespace AFlashTool.Modular.Super.LpMake;

using static LpMakeConstants;
using static LpMakeUtility;

/// <summary>
/// LP 元数据二进制序列化 - 移植自 AOSP liblp writer.cpp
/// </summary>
public static class LpMetadataSerializer
{
    /// <summary>序列化几何信息为 4096 字节块 (含 SHA256 校验)</summary>
    public static byte[] SerializeGeometry(LpMetadataGeometry geometry)
    {
        var buf = new byte[LP_METADATA_GEOMETRY_SIZE];
        int off = 0;

        WriteU32(buf, ref off, geometry.Magic);
        WriteU32(buf, ref off, geometry.StructSize);

        // checksum 占位先写 0
        int checksumOffset = off;
        off += 32;

        WriteU32(buf, ref off, geometry.MetadataMaxSize);
        WriteU32(buf, ref off, geometry.MetadataSlotCount);
        WriteU32(buf, ref off, geometry.LogicalBlockSize);

        // 对前 SizeOfGeometry 字节计算 SHA256 (checksum 区域为 0)
        byte[] hash = Sha256(buf.AsSpan(0, SizeOfGeometry));
        hash.CopyTo(buf.AsSpan(checksumOffset));

        return buf;
    }

    /// <summary>序列化 metadata header + tables (不含 geometry)</summary>
    public static byte[] SerializeMetadata(LpMetadata metadata)
    {
        var header = metadata.Header;

        // 序列化各表
        byte[] partitionsBlob = SerializePartitions(metadata.Partitions);
        byte[] extentsBlob = SerializeExtents(metadata.Extents);
        byte[] groupsBlob = SerializeGroups(metadata.Groups);
        byte[] blockDevicesBlob = SerializeBlockDevices(metadata.BlockDevices);

        // 计算偏移
        header.Partitions.Offset = 0;
        header.Extents.Offset = (uint)(header.Partitions.Offset + partitionsBlob.Length);
        header.Groups.Offset = (uint)(header.Extents.Offset + extentsBlob.Length);
        header.BlockDevices.Offset = (uint)(header.Groups.Offset + groupsBlob.Length);
        header.TablesSize = (uint)(header.BlockDevices.Offset + blockDevicesBlob.Length);

        // 表 payload
        byte[] tables = new byte[header.TablesSize];
        partitionsBlob.CopyTo(tables, header.Partitions.Offset);
        extentsBlob.CopyTo(tables, header.Extents.Offset);
        groupsBlob.CopyTo(tables, header.Groups.Offset);
        blockDevicesBlob.CopyTo(tables, header.BlockDevices.Offset);

        // tables checksum
        header.TablesChecksum = Sha256(tables);

        // The checksum field itself must be zero while calculating the header hash.
        // SerializeMetadata can be called more than once for the same metadata object.
        header.HeaderChecksum = new byte[32];
        byte[] headerBlob = SerializeHeader(header);

        // header checksum (checksum 区域为 0)
        header.HeaderChecksum = Sha256(headerBlob.AsSpan(0, (int)header.HeaderSize));
        headerBlob = SerializeHeader(header);

        // 拼接
        byte[] result = new byte[headerBlob.Length + tables.Length];
        headerBlob.CopyTo(result, 0);
        tables.CopyTo(result, headerBlob.Length);
        return result;
    }

    // ────── Header ──────

    private static byte[] SerializeHeader(LpMetadataHeader h)
    {
        var buf = new byte[h.HeaderSize];
        int off = 0;

        WriteU32(buf, ref off, h.Magic);
        WriteU16(buf, ref off, h.MajorVersion);
        WriteU16(buf, ref off, h.MinorVersion);
        WriteU32(buf, ref off, h.HeaderSize);
        WriteBytes(buf, ref off, h.HeaderChecksum);
        WriteU32(buf, ref off, h.TablesSize);
        WriteBytes(buf, ref off, h.TablesChecksum);
        WriteTableDescriptor(buf, ref off, h.Partitions);
        WriteTableDescriptor(buf, ref off, h.Extents);
        WriteTableDescriptor(buf, ref off, h.Groups);
        WriteTableDescriptor(buf, ref off, h.BlockDevices);

        // V1.2 扩展字段
        if (h.HeaderSize >= SizeOfHeaderV12)
        {
            WriteU32(buf, ref off, h.Flags);
            // reserved 已为 0
        }

        return buf;
    }

    private static void WriteTableDescriptor(Span<byte> buf, ref int off, LpMetadataTableDescriptor td)
    {
        WriteU32(buf, ref off, td.Offset);
        WriteU32(buf, ref off, td.NumEntries);
        WriteU32(buf, ref off, td.EntrySize);
    }

    // ────── Partitions ──────

    private static byte[] SerializePartitions(IReadOnlyList<LpMetadataPartition> partitions)
    {
        var buf = new byte[partitions.Count * SizeOfPartition];
        for (int i = 0; i < partitions.Count; i++)
        {
            int off = i * SizeOfPartition;
            var p = partitions[i];
            WriteFixedAsciiName(buf.AsSpan(off, PartitionNameLen), p.Name);
            off += PartitionNameLen;
            WriteU32(buf, ref off, p.Attributes);
            WriteU32(buf, ref off, p.FirstExtentIndex);
            WriteU32(buf, ref off, p.NumExtents);
            WriteU32(buf, ref off, p.GroupIndex);
        }
        return buf;
    }

    // ────── Extents ──────

    private static byte[] SerializeExtents(IReadOnlyList<LpMetadataExtent> extents)
    {
        var buf = new byte[extents.Count * SizeOfExtent];
        for (int i = 0; i < extents.Count; i++)
        {
            int off = i * SizeOfExtent;
            var e = extents[i];
            WriteU64(buf, ref off, e.NumSectors);
            WriteU32(buf, ref off, e.TargetType);
            WriteU64(buf, ref off, e.TargetData);
            WriteU32(buf, ref off, e.TargetSource);
        }
        return buf;
    }

    // ────── Groups ──────

    private static byte[] SerializeGroups(IReadOnlyList<LpMetadataPartitionGroup> groups)
    {
        var buf = new byte[groups.Count * SizeOfPartitionGroup];
        for (int i = 0; i < groups.Count; i++)
        {
            int off = i * SizeOfPartitionGroup;
            var g = groups[i];
            WriteFixedAsciiName(buf.AsSpan(off, PartitionNameLen), g.Name);
            off += PartitionNameLen;
            WriteU32(buf, ref off, g.Flags);
            WriteU64(buf, ref off, g.MaximumSize);
        }
        return buf;
    }

    // ────── Block Devices ──────

    private static byte[] SerializeBlockDevices(IReadOnlyList<LpMetadataBlockDevice> devices)
    {
        var buf = new byte[devices.Count * SizeOfBlockDevice];
        for (int i = 0; i < devices.Count; i++)
        {
            int off = i * SizeOfBlockDevice;
            var d = devices[i];
            WriteU64(buf, ref off, d.FirstLogicalSector);
            WriteU32(buf, ref off, d.Alignment);
            WriteU32(buf, ref off, d.AlignmentOffset);
            WriteU64(buf, ref off, d.Size);
            WriteFixedAsciiName(buf.AsSpan(off, PartitionNameLen), d.PartitionName);
            off += PartitionNameLen;
            WriteU32(buf, ref off, d.Flags);
        }
        return buf;
    }
}
