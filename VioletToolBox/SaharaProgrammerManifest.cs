using System.Globalization;
using System.Xml.Linq;

namespace WpfApp1;

internal sealed record SaharaProgrammerImage(int ImageId, string FilePath);

internal static class SaharaProgrammerManifest
{
    public static bool IsManifest(string path)
    {
        if (!Path.GetExtension(path).Equals(".xml", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            XDocument document = XDocument.Load(path, LoadOptions.None);
            return document.Root?.Name.LocalName.Equals(
                "sahara_config",
                StringComparison.OrdinalIgnoreCase) == true
                && document.Descendants()
                    .Any(element => element.Name.LocalName.Equals(
                        "image",
                        StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    public static IReadOnlyList<SaharaProgrammerImage> Load(string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            throw new FileNotFoundException("Sahara 多镜像配置文件不存在。", manifestPath);

        XDocument document = XDocument.Load(manifestPath, LoadOptions.None);
        if (document.Root?.Name.LocalName.Equals(
                "sahara_config",
                StringComparison.OrdinalIgnoreCase) != true)
        {
            throw new InvalidDataException("Sahara 多镜像配置缺少 sahara_config 根节点。");
        }

        string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))
            ?? throw new InvalidDataException("无法确定 Sahara 多镜像配置目录。");
        var images = new List<SaharaProgrammerImage>();
        foreach (XElement element in document.Descendants()
                     .Where(element => element.Name.LocalName.Equals(
                         "image",
                         StringComparison.OrdinalIgnoreCase)))
        {
            string? idText = element.Attribute("image_id")?.Value;
            string? relativePath = element.Attribute("image_path")?.Value;
            if (!int.TryParse(
                    idText,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int imageId)
                || imageId < 0)
            {
                throw new InvalidDataException($"Sahara image_id 无效：{idText}");
            }
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new InvalidDataException($"Sahara image {imageId} 缺少 image_path。");

            string fullPath = Path.GetFullPath(Path.Combine(baseDirectory, relativePath.Trim()));
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    $"Sahara image {imageId} 文件不存在。",
                    fullPath);
            }
            images.Add(new SaharaProgrammerImage(imageId, fullPath));
        }

        if (images.Count == 0)
            throw new InvalidDataException("Sahara 多镜像配置中没有 image 映射。");
        if (images.GroupBy(image => image.ImageId).Any(group => group.Count() > 1))
            throw new InvalidDataException("Sahara 多镜像配置中存在重复的 image_id。");

        return images;
    }

    public static string BuildArguments(IReadOnlyList<SaharaProgrammerImage> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0)
            throw new ArgumentException("Sahara image 映射不能为空。", nameof(images));

        return string.Join(
            " ",
            images.Select(image => $"-s {image.ImageId}:\"{image.FilePath}\""));
    }
}
