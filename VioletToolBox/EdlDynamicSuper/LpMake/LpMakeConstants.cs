namespace AFlashTool.Modular.Super.LpMake;

/// <summary>
/// LP (Logical Partition) 元数据格式常量, 与 AOSP liblp metadata_format.h 对齐
/// </summary>
public static class LpMakeConstants
{
    public const uint LP_METADATA_GEOMETRY_MAGIC = 0x616c4467;
    public const int LP_METADATA_GEOMETRY_SIZE = 4096;

    public const uint LP_METADATA_HEADER_MAGIC = 0x414C5030;

    public const ushort LP_METADATA_MAJOR_VERSION = 10;
    public const ushort LP_METADATA_MINOR_VERSION_MIN = 0;
    public const ushort LP_METADATA_MINOR_VERSION_MAX = 2;
    public const ushort LP_METADATA_VERSION_FOR_UPDATED_ATTR = 1;
    public const ushort LP_METADATA_VERSION_FOR_EXPANDED_HEADER = 2;

    public const uint LP_PARTITION_ATTR_NONE = 0x0;
    public const uint LP_PARTITION_ATTR_READONLY = 1u << 0;
    public const uint LP_PARTITION_ATTR_SLOT_SUFFIXED = 1u << 1;
    public const uint LP_PARTITION_ATTR_UPDATED = 1u << 2;
    public const uint LP_PARTITION_ATTR_DISABLED = 1u << 3;

    public const uint LP_PARTITION_ATTRIBUTE_MASK_V0 =
        LP_PARTITION_ATTR_READONLY | LP_PARTITION_ATTR_SLOT_SUFFIXED;
    public const uint LP_PARTITION_ATTRIBUTE_MASK_V1 =
        LP_PARTITION_ATTR_UPDATED | LP_PARTITION_ATTR_DISABLED;
    public const uint LP_PARTITION_ATTRIBUTE_MASK =
        LP_PARTITION_ATTRIBUTE_MASK_V0 | LP_PARTITION_ATTRIBUTE_MASK_V1;

    public const string LP_METADATA_DEFAULT_PARTITION_NAME = "super";
    public const int LP_SECTOR_SIZE = 512;
    public const int LP_PARTITION_RESERVED_BYTES = 4096;

    public const uint LP_HEADER_FLAG_VIRTUAL_AB_DEVICE = 0x1;

    public const uint LP_TARGET_TYPE_LINEAR = 0;
    public const uint LP_TARGET_TYPE_ZERO = 1;

    public const uint LP_GROUP_SLOT_SUFFIXED = 1u << 0;
    public const uint LP_BLOCK_DEVICE_SLOT_SUFFIXED = 1u << 0;

    public const string KDefaultGroup = "default";
    public const uint KDefaultPartitionAlignment = 1024 * 1024; // 1 MiB
    public const uint KDefaultBlockSize = 4096;

    // 序列化后的 struct 尺寸 (与 AOSP 二进制兼容)
    public const int SizeOfGeometry = 52;            // 实际写入 4096
    public const int SizeOfHeaderV10 = 128;
    public const int SizeOfHeaderV12 = 256;
    public const int SizeOfPartition = 52;
    public const int SizeOfExtent = 24;
    public const int SizeOfPartitionGroup = 48;
    public const int SizeOfBlockDevice = 64;
    public const int SizeOfTableDescriptor = 12;
    public const int PartitionNameLen = 36;
}
