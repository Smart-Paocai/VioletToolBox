using System.Text.Json;
using Payload_Dumper_C_.Core;

namespace SuperFix.Services;

public sealed record CloudSuperPackageContent(
    string WorkingDirectory,
    IReadOnlyList<string> DefinitionPaths,
    CloudSuperVersionInfo VersionInfo,
    string SourceNode);

public sealed record CloudSuperVersionInfo(
    string NvId,
    string DownloadType,
    string ProductName,
    string ProductModel,
    string MarketName,
    string VersionName,
    string Platform);

public sealed class CloudSuperPackageService
{
    public async Task<CloudSuperPackageContent> ExtractAsync(
        WpfApp1.SuperRepair.SuperRepairCloudPackage package,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.DownloadUrls.Count == 0)
            throw new InvalidOperationException("云端未返回售后包下载节点");

        var failures = new List<Exception>();
        for (int index = 0; index < package.DownloadUrls.Count; index++)
        {
            string url = package.DownloadUrls[index];
            if (string.IsNullOrWhiteSpace(url))
                continue;

            progress?.Invoke($"正在读取云端售后包目录（节点 {index + 1}/{package.DownloadUrls.Count}）...");
            try
            {
                return await ExtractFromNodeAsync(
                    url,
                    package.RequestHeaders,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        string detail = failures.Count == 0
            ? "下载节点无效"
            : string.Join(" | ", failures.Select(ex => ex.Message).Distinct(StringComparer.OrdinalIgnoreCase).Take(3));
        throw new InvalidOperationException($"无法从云端售后包读取 Super 定义：{detail}");
    }

    private static async Task<CloudSuperPackageContent> ExtractFromNodeAsync(
        string url,
        IReadOnlyDictionary<string, string> requestHeaders,
        Action<string>? progress,
        CancellationToken cancellationToken)
    {
        await using IRandomAccessReader source = await PayloadProcessing.OpenSourceAsync(
            url,
            cancellationToken,
            requestHeaders).ConfigureAwait(false);
        IReadOnlyList<ZipStoredEntryLocator.ZipEntryInfo> entries =
            await ZipStoredEntryLocator.ListEntriesAsync(source, cancellationToken).ConfigureAwait(false);

        List<ZipStoredEntryLocator.ZipEntryInfo> definitions = SelectUniqueEntries(
            entries.Where(entry => IsDefinitionEntry(entry.Name)),
            "super_def*.json");
        if (definitions.Count == 0)
            throw new InvalidDataException("售后包 META 目录中未找到 super_def*.json");

        ZipStoredEntryLocator.ZipEntryInfo[] versionInfoEntries = entries
            .Where(entry => Path.GetFileName(NormalizeEntryName(entry.Name)).Equals(
                "version_info.txt",
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => NormalizeEntryName(entry.Name).Count(ch => ch == '/'))
            .ToArray();
        if (versionInfoEntries.Length == 0)
            throw new InvalidDataException("售后包根目录中未找到 version_info.txt，无法精准确定 NV ID");
        ZipStoredEntryLocator.ZipEntryInfo versionInfoEntry = versionInfoEntries[0];

        string workingDirectory = Path.Combine(
            Path.GetTempPath(),
            "Violet_SuperFix",
            "cloud",
            Guid.NewGuid().ToString("N"));
        string metaDirectory = Path.Combine(workingDirectory, "META");
        string imagesDirectory = Path.Combine(workingDirectory, "IMAGES");
        Directory.CreateDirectory(metaDirectory);
        Directory.CreateDirectory(imagesDirectory);

        string versionInfoPath = Path.Combine(workingDirectory, "version_info.txt");
        progress?.Invoke("正在提取 version_info.txt...");
        await ExtractEntryAsync(source, versionInfoEntry, versionInfoPath, cancellationToken).ConfigureAwait(false);
        CloudSuperVersionInfo versionInfo = ReadVersionInfo(versionInfoPath);
        if (string.IsNullOrWhiteSpace(versionInfo.NvId))
            throw new InvalidDataException("version_info.txt 未提供 nv_id，已阻止自动选择 Super 定义");

        var definitionPaths = new List<string>();
        foreach (ZipStoredEntryLocator.ZipEntryInfo entry in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string outputPath = Path.Combine(metaDirectory, Path.GetFileName(NormalizeEntryName(entry.Name)));
            progress?.Invoke($"正在提取 {Path.GetFileName(outputPath)}...");
            await ExtractEntryAsync(source, entry, outputPath, cancellationToken).ConfigureAwait(false);
            definitionPaths.Add(outputPath);
        }

        HashSet<string> referencedMetaNames = ReadReferencedMetaNames(definitionPaths);
        List<ZipStoredEntryLocator.ZipEntryInfo> metadataEntries = SelectUniqueEntries(
            entries.Where(entry => IsMetadataEntry(entry.Name, referencedMetaNames)),
            "super_meta*.raw");
        if (metadataEntries.Count == 0)
            throw new InvalidDataException("售后包 IMAGES 目录中未找到 super_meta metadata");

        foreach (ZipStoredEntryLocator.ZipEntryInfo entry in metadataEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string outputPath = Path.Combine(imagesDirectory, Path.GetFileName(NormalizeEntryName(entry.Name)));
            progress?.Invoke($"正在提取 {Path.GetFileName(outputPath)}...");
            await ExtractEntryAsync(source, entry, outputPath, cancellationToken).ConfigureAwait(false);
        }

        return new CloudSuperPackageContent(
            workingDirectory,
            definitionPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            versionInfo,
            GetSafeNodeLabel(url));
    }

    private static CloudSuperVersionInfo ReadVersionInfo(string path)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(path),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
            JsonElement item = document.RootElement.ValueKind switch
            {
                JsonValueKind.Array when document.RootElement.GetArrayLength() > 0 =>
                    document.RootElement.EnumerateArray().First(),
                JsonValueKind.Object => document.RootElement,
                _ => throw new InvalidDataException("version_info.txt 必须是 JSON 对象或非空数组")
            };

            return new CloudSuperVersionInfo(
                ReadString(item, "nv_id"),
                ReadString(item, "download_type"),
                ReadString(item, "product_name"),
                ReadString(item, "product_model"),
                ReadString(item, "market_name"),
                ReadString(item, "version_name"),
                ReadString(item, "platform"));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"version_info.txt JSON 格式无效：{ex.Message}", ex);
        }
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            return string.Empty;
        return value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : value.ToString().Trim();
    }

    private static async Task ExtractEntryAsync(
        IRandomAccessReader source,
        ZipStoredEntryLocator.ZipEntryInfo entry,
        string outputPath,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await ZipStoredEntryLocator.ExtractEntryAsync(
            source,
            entry,
            output,
            cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static HashSet<string> ReadReferencedMetaNames(IEnumerable<string> definitionPaths)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in definitionPaths)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.TryGetProperty("super_meta", out JsonElement superMeta) &&
                    superMeta.TryGetProperty("path", out JsonElement pathElement))
                {
                    string? value = pathElement.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        result.Add(Path.GetFileName(value.Replace('/', Path.DirectorySeparatorChar)));
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"{Path.GetFileName(path)} JSON 格式无效：{ex.Message}", ex);
            }
        }
        return result;
    }

    private static List<ZipStoredEntryLocator.ZipEntryInfo> SelectUniqueEntries(
        IEnumerable<ZipStoredEntryLocator.ZipEntryInfo> entries,
        string kind)
    {
        var result = new List<ZipStoredEntryLocator.ZipEntryInfo>();
        foreach (IGrouping<string, ZipStoredEntryLocator.ZipEntryInfo> group in entries.GroupBy(
                     entry => Path.GetFileName(NormalizeEntryName(entry.Name)),
                     StringComparer.OrdinalIgnoreCase))
        {
            ZipStoredEntryLocator.ZipEntryInfo[] candidates = group
                .OrderBy(entry => NormalizeEntryName(entry.Name).Count(ch => ch == '/'))
                .ToArray();
            if (candidates.Select(entry => entry.UncompressedSize).Distinct().Count() > 1)
            {
                throw new InvalidDataException($"售后包中存在多份内容不同的 {group.Key}，无法安全选择 {kind}");
            }
            result.Add(candidates[0]);
        }
        return result;
    }

    private static bool IsDefinitionEntry(string name)
    {
        string normalized = NormalizeEntryName(name);
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[^2].Equals("META", StringComparison.OrdinalIgnoreCase))
            return false;
        string fileName = parts[^1];
        return fileName.StartsWith("super_def", StringComparison.OrdinalIgnoreCase) &&
               fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMetadataEntry(string name, IReadOnlySet<string> referencedMetaNames)
    {
        string normalized = NormalizeEntryName(name);
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[^2].Equals("IMAGES", StringComparison.OrdinalIgnoreCase))
            return false;
        string fileName = parts[^1];
        return referencedMetaNames.Contains(fileName) ||
               (fileName.StartsWith("super_meta", StringComparison.OrdinalIgnoreCase) &&
                fileName.EndsWith(".raw", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeEntryName(string name) =>
        (name ?? string.Empty).Replace('\\', '/').Trim('/');

    private static string GetSafeNodeLabel(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            return "cloud";
        return string.IsNullOrWhiteSpace(uri.Host) ? "cloud" : uri.Host;
    }
}
