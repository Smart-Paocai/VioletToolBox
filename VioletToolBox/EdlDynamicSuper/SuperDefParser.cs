using System.Text.Json;
using System.Globalization;

namespace AFlashTool.Modular.Super;

/// <summary>
/// super_def JSON 解析器 - 解析动态分区定义文件
/// </summary>
public static class SuperDefParser
{
    public static SuperDef Parse(string jsonContent)
    {
        using var doc = JsonDocument.Parse(jsonContent);
        var root = doc.RootElement;
        var def = new SuperDef();

        // super_meta
        if (root.TryGetProperty("super_meta", out var metaEl))
        {
            def.SuperMeta = new SuperMetaInfo
            {
                Path = GetString(metaEl, "path"),
                Size = ParseUlong(metaEl, "size")
            };
        }

        def.NvId = GetString(root, "nv_id");
        def.NvText = GetString(root, "nv_text");

        // block_devices
        if (root.TryGetProperty("block_devices", out var bdArr))
        {
            foreach (var bd in bdArr.EnumerateArray())
            {
                def.BlockDevices.Add(new SuperDefBlockDevice
                {
                    Name = GetString(bd, "name", "super"),
                    Size = ParseUlong(bd, "size"),
                    BlockSize = ParseUint(bd, "block_size", 4096),
                    Alignment = ParseUint(bd, "alignment", 1048576),
                    AlignmentOffset = ParseUint(bd, "alignment_offset")
                });
            }
        }

        // groups
        if (root.TryGetProperty("groups", out var grpArr))
        {
            foreach (var g in grpArr.EnumerateArray())
            {
                def.Groups.Add(new SuperDefGroup
                {
                    Name = GetString(g, "name", "default"),
                    MaximumSize = ParseUlong(g, "maximum_size")
                });
            }
        }

        // partitions
        if (root.TryGetProperty("partitions", out var partArr))
        {
            foreach (var p in partArr.EnumerateArray())
            {
                string groupName = GetString(p, "group_name");
                if (string.IsNullOrWhiteSpace(groupName))
                    groupName = GetString(p, "group");
                def.Partitions.Add(new SuperDefPartition
                {
                    Name = GetString(p, "name"),
                    GroupName = groupName,
                    IsDynamic = ParseBool(p, "is_dynamic", true),
                    Size = ParseUlong(p, "size"),
                    Path = GetString(p, "path")
                });
            }
        }

        return def;
    }

    private static ulong ParseUlong(JsonElement el, string prop, ulong fallback = 0)
    {
        if (!el.TryGetProperty(prop, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetUInt64(out ulong numeric))
            return numeric;
        if (v.ValueKind == JsonValueKind.String)
        {
            string text = (v.GetString() ?? "").Replace(",", "").Replace("_", "").Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && ulong.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hex))
            {
                return hex;
            }
            if (ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsed))
                return parsed;
        }
        return fallback;
    }

    private static uint ParseUint(JsonElement el, string prop, uint fallback = 0)
    {
        ulong value = ParseUlong(el, prop, fallback);
        return value <= uint.MaxValue ? (uint)value : fallback;
    }

    private static string GetString(JsonElement el, string prop, string fallback = "")
    {
        if (!el.TryGetProperty(prop, out var value))
            return fallback;
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : value.ToString();
    }

    private static bool ParseBool(JsonElement el, string prop, bool fallback)
    {
        if (!el.TryGetProperty(prop, out var value))
            return fallback;
        if (value.ValueKind == JsonValueKind.True)
            return true;
        if (value.ValueKind == JsonValueKind.False)
            return false;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int numeric))
            return numeric != 0;
        if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out bool parsed))
            return parsed;
        return fallback;
    }
}

// ────────── DTO ──────────

public sealed class SuperDef
{
    public SuperMetaInfo? SuperMeta { get; set; }
    public string NvId { get; set; } = "";
    public string NvText { get; set; } = "";
    public List<SuperDefBlockDevice> BlockDevices { get; } = new();
    public List<SuperDefGroup> Groups { get; } = new();
    public List<SuperDefPartition> Partitions { get; } = new();
}

public sealed class SuperMetaInfo
{
    public string Path { get; set; } = "";
    public ulong Size { get; set; }
}

public sealed class SuperDefBlockDevice
{
    public string Name { get; set; } = "super";
    public ulong Size { get; set; }
    public uint BlockSize { get; set; } = 4096;
    public uint Alignment { get; set; } = 1048576;
    public uint AlignmentOffset { get; set; }
}

public sealed class SuperDefGroup
{
    public string Name { get; set; } = "";
    public ulong MaximumSize { get; set; }
}

public sealed class SuperDefPartition
{
    public string Name { get; set; } = "";
    public string GroupName { get; set; } = "";
    public bool IsDynamic { get; set; }
    public ulong Size { get; set; }
    public string Path { get; set; } = "";
}
