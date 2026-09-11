namespace SuperFix.Core;

internal static class LpConstants
{
    public const uint GeometryMagic = 0x616C4467;
    public const uint HeaderMagic = 0x414C5030;
    public const int ReservedBytes = 4096;
    public const int GeometryBlockSize = 4096;
    public const int GeometryStructSize = 52;
    public const int HeaderV10Size = 128;
    public const int HeaderV12Size = 256;
    public const int PartitionEntrySize = 52;
    public const int ExtentEntrySize = 24;
    public const int GroupEntrySize = 48;
    public const int BlockDeviceEntrySize = 64;
    public const int NameLength = 36;
    public const int SectorSize = 512;

    public static long GetTotalMetadataSize(uint metadataMaxSize, uint slotCount) => checked(
        ReservedBytes + 2L * (GeometryBlockSize + (long)metadataMaxSize * slotCount));

    public static long GetPrimaryMetadataOffset(uint metadataMaxSize, uint slot) => checked(
        ReservedBytes + 2L * GeometryBlockSize + (long)metadataMaxSize * slot);

    public static long GetBackupMetadataOffset(uint metadataMaxSize, uint slotCount, uint slot) => checked(
        ReservedBytes + 2L * GeometryBlockSize + (long)metadataMaxSize * slotCount +
        (long)metadataMaxSize * slot);
}

public sealed class SuperDefinition
{
    public required string DefinitionPath { get; init; }
    public required string PackageRoot { get; init; }
    public required string SuperMetaRelativePath { get; init; }
    public ulong SuperMetaDeclaredSize { get; init; }
    public ulong SuperDeviceTotalSize { get; init; }
    public ulong SuperDeviceUsedSize { get; init; }
    public string NvId { get; init; } = string.Empty;
    public string NvText { get; init; } = string.Empty;
    public List<SuperDefinitionBlockDevice> BlockDevices { get; } = [];
    public List<SuperDefinitionGroup> Groups { get; } = [];
    public List<SuperDefinitionPartition> Partitions { get; } = [];
}

public sealed record SuperDefinitionBlockDevice(
    string Name,
    ulong Size,
    uint BlockSize,
    uint Alignment,
    uint AlignmentOffset);

public sealed record SuperDefinitionGroup(string Name, ulong MaximumSize);

public sealed record SuperDefinitionPartition(
    string Name,
    string GroupName,
    bool IsDynamic,
    ulong Size,
    string Path);

public sealed record LpGeometry(
    uint StructSize,
    uint MetadataMaxSize,
    uint MetadataSlotCount,
    uint LogicalBlockSize,
    byte[] RawStruct);

public sealed record LpHeaderInfo(
    ushort MajorVersion,
    ushort MinorVersion,
    uint HeaderSize,
    uint Flags,
    uint TablesSize,
    byte[] RawHeader);

public sealed record LpPartitionInfo(
    string Name,
    uint Attributes,
    uint FirstExtentIndex,
    uint NumExtents,
    uint GroupIndex,
    ulong Size);

public sealed record LpExtentInfo(
    ulong NumSectors,
    uint TargetType,
    ulong TargetData,
    uint TargetSource);

public sealed record LpGroupInfo(string Name, uint Flags, ulong MaximumSize);

public sealed record LpBlockDeviceInfo(
    ulong FirstLogicalSector,
    uint Alignment,
    uint AlignmentOffset,
    ulong Size,
    string Name,
    uint Flags);

public sealed class LpMetadataTemplate
{
    public required string SourcePath { get; init; }
    public required LpGeometry Geometry { get; init; }
    public required LpHeaderInfo Header { get; init; }
    public List<LpPartitionInfo> Partitions { get; } = [];
    public List<LpExtentInfo> Extents { get; } = [];
    public List<LpGroupInfo> Groups { get; } = [];
    public List<LpBlockDeviceInfo> BlockDevices { get; } = [];
}

public sealed record ScatterSuperEntry(
    string SourcePath,
    string Storage,
    ulong StartAddress,
    ulong PartitionSize);

public sealed class SuperPackageAnalysis
{
    public required SuperDefinition Definition { get; init; }
    public required LpMetadataTemplate Template { get; init; }
    public required string ResolvedSuperMetaPath { get; init; }
    public required ulong DeclaredPartitionBytes { get; init; }
    public required long EmptyImageSize { get; init; }
    public List<ScatterSuperEntry> ScatterEntries { get; } = [];

    public LpBlockDeviceInfo SuperBlockDevice => Template.BlockDevices.Single();
}

public sealed record EmptySuperVerification(
    ulong SuperSize,
    uint MetadataMaxSize,
    uint MetadataSlotCount,
    uint LogicalBlockSize,
    ushort MajorVersion,
    ushort MinorVersion,
    uint HeaderFlags,
    long ImageSize,
    string Sha256);
