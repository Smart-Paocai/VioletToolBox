namespace AFlashTool.Modular.Super.LpMake;

using static LpMakeConstants;
using static LpMakeUtility;

/// <summary>
/// LP 镜像写入器 - 将 metadata 写入 super_meta / super_empty 镜像文件
/// </summary>
public static class LpImageWriter
{
    /// <summary>
    /// 写入 metadata-only 镜像 (super_empty.img 格式)
    /// 布局: geometry + metadata (无 reserved 前缀, 仅 geometry + 序列化 metadata)
    /// </summary>
    public static void WriteEmptyImage(Stream output, LpMetadata metadata)
    {
        byte[] geometry = LpMetadataSerializer.SerializeGeometry(metadata.Geometry);
        byte[] metaBlob = LpMetadataSerializer.SerializeMetadata(metadata);
        output.Write(geometry);
        output.Write(metaBlob);
    }

    /// <summary>
    /// 写入完整 metadata 区域 (OPLUS super_meta 格式)
    /// 布局:
    ///   [4096 reserved zeros]
    ///   [4096 geometry primary]
    ///   [4096 geometry backup]
    ///   [metadata_max_size × slot_count]  (primary copies, 有效数据 + 零填充)
    ///   [metadata_max_size × slot_count]  (backup copies)
    /// 总大小 = GetTotalMetadataSize(metadata_max_size, slot_count)
    /// </summary>
    public static void WriteMetaImage(Stream output, LpMetadata metadata)
    {
        var geo = metadata.Geometry;
        byte[] geometryBlob = LpMetadataSerializer.SerializeGeometry(geo);
        byte[] metaBlob = LpMetadataSerializer.SerializeMetadata(metadata);

        // 将 metadata 填充到 metadata_max_size
        byte[] paddedMeta = new byte[geo.MetadataMaxSize];
        metaBlob.AsSpan(0, Math.Min(metaBlob.Length, paddedMeta.Length)).CopyTo(paddedMeta);

        // 1. Reserved zeros
        output.Write(new byte[LP_PARTITION_RESERVED_BYTES]);

        // 2. Geometry primary + backup
        output.Write(geometryBlob);
        output.Write(geometryBlob);

        // 3. Primary metadata slots
        for (uint i = 0; i < geo.MetadataSlotCount; i++)
            output.Write(paddedMeta);

        // 4. Backup metadata slots
        for (uint i = 0; i < geo.MetadataSlotCount; i++)
            output.Write(paddedMeta);
    }

    /// <summary>
    /// 写入完整 metadata 区域并截断/填充到指定字节大小
    /// (用于 OPLUS super_meta 固定尺寸, 如 65536 bytes)
    /// </summary>
    public static void WriteMetaImage(Stream output, LpMetadata metadata, long targetSize)
    {
        using var ms = new MemoryStream();
        WriteMetaImage(ms, metadata);
        byte[] raw = ms.ToArray();

        if (raw.Length <= targetSize)
        {
            output.Write(raw);
            // 零填充到 targetSize
            long pad = targetSize - raw.Length;
            if (pad > 0)
                output.Write(new byte[pad]);
        }
        else
        {
            // 截断 (不应发生, 说明 metadata_max_size 过大)
            output.Write(raw.AsSpan(0, (int)targetSize));
        }
    }
}
