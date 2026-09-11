using System.Globalization;
using System.Text.Json;

namespace SuperFix.Core;

public static class SuperDefinitionParser
{
    public static SuperDefinition ParseFile(string definitionPath)
    {
        if (string.IsNullOrWhiteSpace(definitionPath))
            throw new ArgumentException("请选择 super_def*.json", nameof(definitionPath));

        string fullPath = Path.GetFullPath(definitionPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("未找到 Super 定义文件", fullPath);

        string definitionDirectory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("无法确定定义文件目录");
        string packageRoot = new DirectoryInfo(definitionDirectory).Name.Equals(
            "META", StringComparison.OrdinalIgnoreCase)
            ? Directory.GetParent(definitionDirectory)?.FullName ?? definitionDirectory
            : definitionDirectory;

        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(fullPath),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("super_def 根节点必须是 JSON 对象");

        JsonElement meta = RequireProperty(root, "super_meta");
        string metaPath = ReadString(meta, "path");
        if (string.IsNullOrWhiteSpace(metaPath))
            throw new InvalidDataException("super_def 缺少 super_meta.path");

        ulong totalSize = 0;
        ulong usedSize = 0;
        if (root.TryGetProperty("super_device", out JsonElement superDevice))
        {
            totalSize = ReadUInt64(superDevice, "total_size");
            usedSize = ReadUInt64(superDevice, "used_size");
        }

        var definition = new SuperDefinition
        {
            DefinitionPath = fullPath,
            PackageRoot = packageRoot,
            SuperMetaRelativePath = metaPath,
            SuperMetaDeclaredSize = ReadUInt64(meta, "size"),
            SuperDeviceTotalSize = totalSize,
            SuperDeviceUsedSize = usedSize,
            NvId = ReadString(root, "nv_id"),
            NvText = ReadString(root, "nv_text")
        };

        JsonElement blockDevices = RequireProperty(root, "block_devices");
        if (blockDevices.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("block_devices 必须是数组");
        foreach (JsonElement item in blockDevices.EnumerateArray())
        {
            definition.BlockDevices.Add(new SuperDefinitionBlockDevice(
                ReadString(item, "name", "super"),
                ReadUInt64(item, "size"),
                ReadUInt32(item, "block_size", 4096),
                ReadUInt32(item, "alignment", 1024 * 1024),
                ReadUInt32(item, "alignment_offset")));
        }

        JsonElement groups = RequireProperty(root, "groups");
        if (groups.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("groups 必须是数组");
        foreach (JsonElement item in groups.EnumerateArray())
        {
            definition.Groups.Add(new SuperDefinitionGroup(
                ReadString(item, "name"),
                ReadUInt64(item, "maximum_size")));
        }

        JsonElement partitions = RequireProperty(root, "partitions");
        if (partitions.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("partitions 必须是数组");
        foreach (JsonElement item in partitions.EnumerateArray())
        {
            string groupName = ReadString(item, "group_name");
            if (string.IsNullOrWhiteSpace(groupName))
                groupName = ReadString(item, "group");

            definition.Partitions.Add(new SuperDefinitionPartition(
                ReadString(item, "name"),
                groupName,
                ReadBoolean(item, "is_dynamic", true),
                ReadUInt64(item, "size"),
                ReadString(item, "path")));
        }

        return definition;
    }

    private static JsonElement RequireProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
            throw new InvalidDataException($"super_def 缺少 {name}");
        return value;
    }

    private static string ReadString(JsonElement element, string name, string fallback = "")
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            return fallback;
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : value.ToString();
    }

    private static bool ReadBoolean(JsonElement element, string name, bool fallback)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
            return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt32(out int number) => number != 0,
            JsonValueKind.String when bool.TryParse(value.GetString(), out bool parsed) => parsed,
            _ => fallback
        };
    }

    private static uint ReadUInt32(JsonElement element, string name, uint fallback = 0)
    {
        ulong value = ReadUInt64(element, name, fallback);
        if (value > uint.MaxValue)
            throw new InvalidDataException($"{name} 超出 UInt32 范围: {value}");
        return (uint)value;
    }

    private static ulong ReadUInt64(JsonElement element, string name, ulong fallback = 0)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out ulong number))
            return number;
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"{name} 不是有效无符号整数");

        string text = (value.GetString() ?? string.Empty)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (ulong.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hex))
                return hex;
        }
        else if (ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsed))
        {
            return parsed;
        }

        throw new InvalidDataException($"{name} 不是有效无符号整数: {text}");
    }
}
