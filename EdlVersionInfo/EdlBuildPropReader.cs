using System.Text;

namespace Yu.Modular.Qualcomm.Firehose.Parsers;

public sealed class EdlBuildPropReader
{
    private static readonly string[] BuildPropPaths =
    {
        "build.prop",
        "etc/build.prop",
        "system/build.prop",
        "system/etc/build.prop"
    };

    private static readonly string[] PartitionPriority =
    {
        "my_manifest", "odm", "my_product", "vendor",
        "product", "system_ext", "system"
    };

    public BuildPropResult ReadFromSuper(
        IByteReader superReader,
        Action<string>? onStatus = null,
        Action<int, int>? onProgress = null)
    {
        var result = new BuildPropResult();
        List<LpPartitionInfo>? partitions = LpMetadataParser.Parse(superReader);
        if (partitions == null || partitions.Count == 0)
            throw new InvalidDataException("无法解析 super 动态分区元数据");

        List<LpPartitionInfo> versionPartitions = partitions
            .Where(item => item.Extents.Count > 0 &&
                           IsVersionPartition(item.Name))
            .OrderBy(item => GetPartitionPriority(item.Name))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        int completedPartitions = 0;
        onProgress?.Invoke(completedPartitions, versionPartitions.Count);

        foreach (LpPartitionInfo partition in versionPartitions)
        {
            try
            {
                string baseName = NormalizePartitionName(partition.Name);
                if (result.PartitionProps.ContainsKey(baseName))
                    continue;

                onStatus?.Invoke($"解析 {partition.Name} 版本信息...");
                var reader = new LogicalPartitionReader(superReader, partition.Extents, 512);
                Dictionary<string, string>? properties = ReadBuildProp(reader, result);
                if (properties == null || properties.Count == 0)
                    continue;

                result.PartitionProps[baseName] = properties;
                MergeProperties(result.Properties, properties);
            }
            finally
            {
                completedPartitions++;
                onProgress?.Invoke(completedPartitions, versionPartitions.Count);
            }
        }

        return result;
    }

    public void ReadFromPhysical(
        BuildPropResult result,
        string partitionName,
        IByteReader reader,
        Action<string>? onStatus = null)
    {
        string baseName = NormalizePartitionName(partitionName);
        if (result.PartitionProps.ContainsKey(baseName))
            return;

        onStatus?.Invoke($"解析 {partitionName} 版本信息...");
        Dictionary<string, string>? properties = ReadBuildProp(reader, result);
        if (properties == null || properties.Count == 0)
            return;

        result.PartitionProps[baseName] = properties;
        MergeProperties(result.Properties, properties);
    }

    private static Dictionary<string, string>? ReadBuildProp(IByteReader reader, BuildPropResult result)
    {
        byte[]? data = null;
        if (Ext4Reader.Detect(reader))
        {
            result.FileSystems.Add("EXT4");
            var ext4 = new Ext4Reader(reader);
            if (!ext4.IsValid)
                return null;
            foreach (string path in BuildPropPaths)
            {
                data = ext4.ReadFile(path);
                if (data is { Length: > 0 })
                    break;
            }
        }
        else if (ErofsReader.Detect(reader))
        {
            result.FileSystems.Add("EROFS");
            var erofs = new ErofsReader(reader);
            if (!erofs.IsValid)
                return null;
            foreach (string path in BuildPropPaths)
            {
                data = erofs.ReadFile(path);
                if (data is { Length: > 0 })
                    break;
            }
        }

        return data is { Length: > 0 } ? ParseProperties(data) : null;
    }

    private static Dictionary<string, string> ParseProperties(byte[] data)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in Encoding.UTF8.GetString(data).Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            int separator = line.IndexOf('=');
            if (separator <= 0)
                continue;
            result[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        return result;
    }

    private static void MergeProperties(
        Dictionary<string, string> target,
        Dictionary<string, string> source)
    {
        foreach ((string key, string value) in source)
            target.TryAdd(key, value);
    }

    private static int GetPartitionPriority(string name)
    {
        string baseName = NormalizePartitionName(name);
        int index = Array.FindIndex(
            PartitionPriority,
            item => item.Equals(baseName, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : PartitionPriority.Length;
    }

    private static bool IsVersionPartition(string name)
    {
        string baseName = NormalizePartitionName(name);
        return PartitionPriority.Any(
            item => item.Equals(baseName, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePartitionName(string name)
    {
        if (name.EndsWith("_a", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_b", StringComparison.OrdinalIgnoreCase))
            return name[..^2];
        return name;
    }
}

public sealed class BuildPropResult
{
    public Dictionary<string, Dictionary<string, string>> PartitionProps { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Properties { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> FileSystems { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string? Get(params string[] keys)
    {
        foreach (string key in keys)
        {
            if (Properties.TryGetValue(key, out string? value) &&
                !string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }
}
