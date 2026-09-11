using System.Windows;
using System.Windows.Documents;
using WpfApp1.SuperRepair;

namespace WpfApp1;

public partial class MainWindow
{
    private RepairSuperWindow? _repairSuperWindow;

    private void RepairSuperHardBrickButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isOugaFlashTaskRunning)
        {
            LogToOugaFlash("当前已有欧加线刷任务正在运行，暂时无法启动 Super 真死修复。", "Orange");
            return;
        }

        if (_repairSuperWindow is { IsVisible: true })
        {
            if (_repairSuperWindow.WindowState == WindowState.Minimized)
                _repairSuperWindow.WindowState = WindowState.Normal;
            _repairSuperWindow.Activate();
            return;
        }

        var window = new RepairSuperWindow(
            CreateSuperRepairCloudCatalog(),
            WriteRepairSuperLog,
            BeginRepairSuperLogStep)
        {
            Owner = this
        };
        _repairSuperWindow = window;
        RepairSuperHardBrickButton.IsEnabled = false;
        try
        {
            window.ShowDialog();
        }
        finally
        {
            _repairSuperWindow = null;
            RepairSuperHardBrickButton.IsEnabled = true;
        }
    }

    private SuperRepairCloudCatalog CreateSuperRepairCloudCatalog()
    {
        return new SuperRepairCloudCatalog(
            async (brand, cancellationToken) =>
            {
                List<string> series = await FetchRomApiSeriesAsync(
                    RomApiPackageTypeAfterSales,
                    brand,
                    cancellationToken);
                return series;
            },
            async (brand, series, cancellationToken) =>
            {
                List<string> devices = await FetchRomApiDevicesAsync(
                    RomApiPackageTypeAfterSales,
                    brand,
                    series,
                    cancellationToken);
                return devices;
            },
            ResolveLatestSuperRepairPackageAsync);
    }

    private static async Task<SuperRepairCloudPackage> ResolveLatestSuperRepairPackageAsync(
        string brand,
        string series,
        string device,
        CancellationToken cancellationToken)
    {
        List<string> versions = await FetchRomApiVersionsAsync(
            RomApiPackageTypeAfterSales,
            brand,
            series,
            device,
            cancellationToken);
        if (versions.Count == 0)
            throw new InvalidOperationException("云端没有该机型的售后包版本");

        string latestVersion = versions
            .Select((version, index) => new
            {
                Version = version,
                Index = index,
                HasSemanticVersion = ExtractRomSemanticVersion(version) != null
            })
            .Where(item => item.HasSemanticVersion)
            .OrderBy(item => item.Version, RomVersionDisplayComparer)
            .ThenBy(item => GetSuperRepairVersionTrackRank(item.Version))
            .ThenBy(item => item.Index)
            .Select(item => item.Version)
            .LastOrDefault()
            ?? versions[^1];

        RomApiDownloadLinkResponse response = await FetchRomApiDownloadLinksAsync(
            RomApiPackageTypeAfterSales,
            brand,
            series,
            device,
            latestVersion,
            cancellationToken);
        List<string> links = PreferRomArchiveLinks(
            RomSelectPackageTypeAfterSales,
            brand,
            response.Links);
        if (links.Count == 0)
            throw new InvalidOperationException("云端未返回该售后包的下载链接");

        return new SuperRepairCloudPackage(
            brand,
            series,
            device,
            latestVersion,
            links,
            response.RequestHeaders ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static int GetSuperRepairVersionTrackRank(string version)
    {
        if (version.Contains("稳定版", StringComparison.OrdinalIgnoreCase) ||
            version.Contains("stable", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }
        if (version.Contains("企业版", StringComparison.OrdinalIgnoreCase) ||
            version.Contains("company", StringComparison.OrdinalIgnoreCase) ||
            version.Contains("enterprise", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        return 1;
    }

    private void WriteRepairSuperLog(
        string label,
        string message,
        RepairSuperLogTone tone)
    {
        string color = tone switch
        {
            RepairSuperLogTone.Success => "Green",
            RepairSuperLogTone.Error => "Red",
            RepairSuperLogTone.Warning => "Orange",
            RepairSuperLogTone.Info => "Blue",
            RepairSuperLogTone.Secondary => "Gray",
            _ => "Black"
        };
        LogToOugaFlashDual(label + " ", "Purple", message, color);
    }

    private Action<bool, string?> BeginRepairSuperLogStep(string label, string message)
    {
        Run? resultRun = null;
        Paragraph? paragraph = null;

        void Begin()
        {
            paragraph = CreateOugaFlashLogParagraph();
            AppendOugaFlashTimestamp(paragraph);
            AppendOugaFlashStyledText(paragraph, label + " ", "Purple", emphasized: true);
            AppendOugaFlashStyledText(paragraph, message, "Black");
            resultRun = new Run(" ...")
            {
                Foreground = GetOugaFlashLogBrush("Gray")
            };
            paragraph.Inlines.Add(resultRun);
            OugaFlashLogTextBox.Document.Blocks.Add(paragraph);
            OugaFlashLogTextBox.ScrollToEnd();
        }

        if (Dispatcher.CheckAccess())
            Begin();
        else
            Dispatcher.Invoke(Begin);

        return (success, detail) =>
        {
            void Complete()
            {
                if (resultRun == null || paragraph == null)
                    return;

                resultRun.Text = success ? " ...OK" : " ...Error";
                resultRun.Foreground = GetOugaFlashLogBrush(success ? "Green" : "Red");
                resultRun.FontWeight = FontWeights.Bold;
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    paragraph.Inlines.Add(new Run(" " + detail.Trim())
                    {
                        Foreground = GetOugaFlashLogBrush("Gray")
                    });
                }
                OugaFlashLogTextBox.ScrollToEnd();
            }

            if (Dispatcher.CheckAccess())
                Complete();
            else
                Dispatcher.Invoke(Complete);
        };
    }
}
