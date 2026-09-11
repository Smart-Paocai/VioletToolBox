using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using SuperFix.Core;
using SuperFix.Services;

namespace WpfApp1.SuperRepair;

public enum RepairSuperLogTone
{
    Normal,
    Secondary,
    Info,
    Success,
    Warning,
    Error
}

public partial class RepairSuperWindow : Window
{
    private static readonly BrandOption[] SupportedBrands =
    [
        new BrandOption("一加", "OnePlus"),
        new BrandOption("真我", "Realme")
    ];

    private readonly SuperRepairCloudCatalog _catalog;
    private readonly CloudSuperPackageService _cloudPackageService = new();
    private readonly FastbootService _fastbootService = new();
    private readonly Action<string, string, RepairSuperLogTone>? _externalLog;
    private readonly Func<string, string, Action<bool, string?>?>? _externalStepLog;
    private CancellationTokenSource? _catalogLoadSource;
    private int _catalogLoadVersion;
    private bool _loaded;
    private bool _selectionUpdating;
    private bool _catalogLoading;
    private bool _busy;

    public RepairSuperWindow(
        SuperRepairCloudCatalog catalog,
        Action<string, string, RepairSuperLogTone>? externalLog = null,
        Func<string, string, Action<bool, string?>?>? externalStepLog = null)
    {
        InitializeComponent();
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _externalLog = externalLog;
        _externalStepLog = externalStepLog;
        UpdateActionState();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
            return;
        _loaded = true;
        _selectionUpdating = true;
        try
        {
            BrandComboBox.ItemsSource = SupportedBrands;
            BrandComboBox.DisplayMemberPath = nameof(BrandOption.DisplayName);
            BrandComboBox.SelectedIndex = 0;
        }
        finally
        {
            _selectionUpdating = false;
        }
        _ = LoadSeriesAndDevicesAsync();
    }

    private async void BrandComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectionUpdating || !_loaded || _busy)
            return;
        await LoadSeriesAndDevicesAsync();
    }

    private async void SeriesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectionUpdating || !_loaded || _busy)
            return;
        await LoadDevicesAsync();
    }

    private void DeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectionUpdating || _busy)
            return;
        StatusText.Text = DeviceComboBox.SelectedItem is string device
            ? $"已选择 {device}"
            : "请选择机型";
        UpdateActionState();
    }

    private async Task LoadSeriesAndDevicesAsync()
    {
        if (BrandComboBox.SelectedItem is not BrandOption brand)
            return;

        (CancellationToken token, int version) = BeginCatalogLoad("正在加载售后包系列...");
        try
        {
            SetSelections(Array.Empty<string>(), Array.Empty<string>());
            IReadOnlyList<string> series = await _catalog.LoadSeriesAsync(brand.ApiName, token);
            token.ThrowIfCancellationRequested();
            if (series.Count == 0)
                throw new InvalidOperationException($"云端没有{brand.DisplayName}售后包系列");

            SetSelections(series, Array.Empty<string>());
            string selectedSeries = SeriesComboBox.SelectedItem as string
                ?? throw new InvalidOperationException("未能选中售后包系列");
            StatusText.Text = $"正在加载 {brand.DisplayName} / {selectedSeries} 机型...";
            IReadOnlyList<string> devices = await _catalog.LoadDevicesAsync(
                brand.ApiName,
                selectedSeries,
                token);
            token.ThrowIfCancellationRequested();
            if (devices.Count == 0)
                throw new InvalidOperationException("云端没有该系列的售后包机型");

            SetDevices(devices);
            StatusText.Text = $"已加载 {devices.Count} 个机型";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AppendException(ex);
            StatusText.Text = "云端售后包列表加载失败";
        }
        finally
        {
            EndCatalogLoad(version);
        }
    }

    private async Task LoadDevicesAsync()
    {
        if (BrandComboBox.SelectedItem is not BrandOption brand ||
            SeriesComboBox.SelectedItem is not string series)
        {
            return;
        }

        (CancellationToken token, int version) = BeginCatalogLoad("正在加载机型...");
        try
        {
            SetDevices(Array.Empty<string>());
            IReadOnlyList<string> devices = await _catalog.LoadDevicesAsync(
                brand.ApiName,
                series,
                token);
            token.ThrowIfCancellationRequested();
            if (devices.Count == 0)
                throw new InvalidOperationException("云端没有该系列的售后包机型");

            SetDevices(devices);
            StatusText.Text = $"已加载 {devices.Count} 个机型";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AppendException(ex);
            StatusText.Text = "云端机型列表加载失败";
        }
        finally
        {
            EndCatalogLoad(version);
        }
    }

    private async void RepairButton_Click(object sender, RoutedEventArgs e)
    {
        if (BrandComboBox.SelectedItem is not BrandOption brand ||
            SeriesComboBox.SelectedItem is not string series ||
            DeviceComboBox.SelectedItem is not string device)
        {
            StatusText.Text = "请先选择品牌、系列和机型";
            return;
        }

        _busy = true;
        SetBusyVisual(true, "正在查询最新售后包...");
        UpdateActionState();
        Action<bool, string?>? pendingStep = null;
        try
        {
            SuperRepairCloudPackage package = await _catalog.ResolveLatestPackageAsync(
                brand.ApiName,
                series,
                device,
                CancellationToken.None);
            AppendLog(
                "[Cloud]",
                $"{brand.DisplayName} / {series} / {device} · {package.Version}",
                RepairSuperLogTone.Normal);

            SetBusyVisual(true, "正在读取售后包中的 Super 定义...");
            CloudSuperPackageContent content = await _cloudPackageService.ExtractAsync(
                package,
                message => Dispatcher.BeginInvoke(() => SetBusyVisual(true, message)),
                CancellationToken.None);
            ValidateVersionInfo(content.VersionInfo);

            SetBusyVisual(true, $"正在按 NV ID {content.VersionInfo.NvId} 解析原厂 LP metadata...");
            SuperPackageAnalysis analysis = await Task.Run(() =>
                AnalyzeDefinitionForNv(content.DefinitionPaths, content.VersionInfo.NvId));
            AppendAnalysisDetails(analysis);

            SetBusyVisual(true, "正在检测 Bootloader Fastboot 设备...");
            FastbootPreflight inspection = await _fastbootService.EnsureBootloaderAsync(
                ReportBootloaderTransition,
                requireSuperSize: false);
            bool superSizeMismatch = inspection.SuperSize == 0 ||
                                     inspection.SuperSize != analysis.SuperBlockDevice.Size;
            if (superSizeMismatch)
            {
                string reportedSize = inspection.SuperSize == 0
                    ? "未提供"
                    : $"0x{inspection.SuperSize:X}";
                AppendLog(
                    "[Warning]",
                    $"设备 Super={reportedSize}，云端定义=0x{analysis.SuperBlockDevice.Size:X}，" +
                    "需要确认所选机型。",
                    RepairSuperLogTone.Warning);

                SetBusyVisual(false);
                var mismatchWindow = new SuperSizeMismatchWindow(
                    device,
                    inspection.SuperSize,
                    analysis.SuperBlockDevice.Size)
                {
                    Owner = this
                };
                if (mismatchWindow.ShowDialog() != true)
                {
                    AppendLog(
                        "[Cancelled]",
                        "Super 容量不一致，用户取消继续刷入，设备未发生写入。",
                        RepairSuperLogTone.Warning);
                    StatusText.Text = "已取消 · Super 容量不一致";
                    return;
                }

                AppendLog(
                    "[Confirmed]",
                    $"用户已确认机型 {device.Replace('_', ' ')} 无误，继续修复。",
                    RepairSuperLogTone.Warning);
                SetBusyVisual(true, "机型已确认，正在继续生成空 Super...");
            }
            AppendPreflightDetails(inspection);

            SetBusyVisual(true, "正在生成并回读校验空 Super...");
            string outputPath = GetDefaultOutputPath(analysis);
            pendingStep = BeginLogStep("[Generating]", Path.GetFileName(outputPath));
            EmptySuperVerification verification = await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                return EmptySuperImageBuilder.Generate(analysis, outputPath);
            });
            pendingStep?.Invoke(true, null);
            pendingStep = null;
            AppendLog(
                "[Verified]",
                $"{verification.ImageSize:N0} bytes · SHA-256 {verification.Sha256}",
                RepairSuperLogTone.Secondary);
            AppendLog("[Saved]", outputPath, RepairSuperLogTone.Normal);

            SetBusyVisual(true, "正在进行刷写前最终核对...");
            FastbootPreflight preflight = await _fastbootService.PreflightAsync(
                verification.SuperSize,
                enforceSuperSize: false);

            pendingStep = BeginLogStep(
                "[Flashing]",
                $"{Path.GetFileName(outputPath)} → super");
            SetBusyVisual(true, "正在刷入 Super，请勿断开数据线...");
            FastbootCommandResult result = await _fastbootService.FlashSuperAsync(
                preflight,
                outputPath,
                enforceSuperSize: false);
            pendingStep?.Invoke(true, $"({result.Elapsed.TotalSeconds:0.00}s)");
            pendingStep = null;
            AppendLog(
                "[Success]",
                "重置 Super 完成，您现在可以正常线刷了！！！请务必搭配强力线刷功能使用！！！",
                RepairSuperLogTone.Success);
            StatusText.Text = "重置 Super 完成 · 请务必搭配强力线刷功能使用";
        }
        catch (Exception ex)
        {
            pendingStep?.Invoke(false, null);
            AppendException(ex);
            StatusText.Text = "Super 修复失败 · 未通过安全校验时不会刷写";
        }
        finally
        {
            _busy = false;
            SetBusyVisual(false);
            UpdateActionState();
        }
    }

    private static SuperPackageAnalysis AnalyzeDefinitionForNv(
        IReadOnlyList<string> definitionPaths,
        string nvId)
    {
        var matches = new List<string>();
        var discoveredNvIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in definitionPaths)
        {
            SuperDefinition definition = SuperDefinitionParser.ParseFile(path);
            if (!string.IsNullOrWhiteSpace(definition.NvId))
                discoveredNvIds.Add(definition.NvId);
            if (definition.NvId.Equals(nvId, StringComparison.OrdinalIgnoreCase))
                matches.Add(path);
        }

        if (matches.Count == 0)
        {
            string available = discoveredNvIds.Count == 0
                ? "未声明"
                : string.Join(", ", discoveredNvIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
            throw new InvalidDataException(
                $"version_info.txt 指定 nv_id={nvId}，但 META 中没有对应 super_def（可用：{available}）");
        }
        if (matches.Count > 1)
            throw new InvalidDataException($"META 中存在多份 nv_id={nvId} 的 super_def，已阻止模糊选择");

        SuperPackageAnalysis analysis = SuperPackageAnalyzer.Analyze(matches[0]);
        if (!analysis.Definition.NvId.Equals(nvId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("super_def 内部 nv_id 与 version_info.txt 不一致");
        return analysis;
    }

    private static void ValidateVersionInfo(CloudSuperVersionInfo info)
    {
        if (string.IsNullOrWhiteSpace(info.NvId))
            throw new InvalidDataException("version_info.txt 缺少 nv_id");

        Match encodedNvId = Regex.Match(
            info.DownloadType ?? string.Empty,
            @"^nv[_-]?(?<id>[0-9a-f]{8})$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (encodedNvId.Success &&
            !encodedNvId.Groups["id"].Value.Equals(info.NvId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"version_info.txt 的 download_type={info.DownloadType} 与 nv_id={info.NvId} 不一致");
        }
    }

    private void SetSelections(
        IReadOnlyList<string> series,
        IReadOnlyList<string> devices)
    {
        _selectionUpdating = true;
        try
        {
            SeriesComboBox.ItemsSource = series;
            SeriesComboBox.SelectedIndex = series.Count > 0 ? 0 : -1;
            DeviceComboBox.ItemsSource = devices;
            DeviceComboBox.SelectedIndex = devices.Count > 0 ? 0 : -1;
        }
        finally
        {
            _selectionUpdating = false;
        }
        UpdateActionState();
    }

    private void SetDevices(IReadOnlyList<string> devices)
    {
        _selectionUpdating = true;
        try
        {
            DeviceComboBox.ItemsSource = devices;
            DeviceComboBox.SelectedIndex = devices.Count > 0 ? 0 : -1;
        }
        finally
        {
            _selectionUpdating = false;
        }
        UpdateActionState();
    }

    private (CancellationToken Token, int Version) BeginCatalogLoad(string status)
    {
        _catalogLoadSource?.Cancel();
        _catalogLoadSource?.Dispose();
        _catalogLoadSource = new CancellationTokenSource();
        int version = ++_catalogLoadVersion;
        _catalogLoading = true;
        StatusText.Text = status;
        SetBusyVisual(true, status);
        UpdateActionState();
        return (_catalogLoadSource.Token, version);
    }

    private void EndCatalogLoad(int version)
    {
        if (version != _catalogLoadVersion)
            return;
        _catalogLoading = false;
        SetBusyVisual(false);
        UpdateActionState();
    }

    private void SetBusyVisual(bool visible, string text = "正在处理...")
    {
        if (visible)
            BusyText.Text = text;
        BusyOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateActionState()
    {
        bool enabled = !_busy && !_catalogLoading;
        BrandComboBox.IsEnabled = enabled;
        SeriesComboBox.IsEnabled = enabled;
        DeviceComboBox.IsEnabled = enabled;
        RepairButton.IsEnabled = enabled &&
                                 BrandComboBox.SelectedItem is BrandOption &&
                                 SeriesComboBox.SelectedItem is string &&
                                 DeviceComboBox.SelectedItem is string;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;
            StatusText.Text = "当前修复流程尚未完成，暂时不能关闭";
            AppendLog("[Warning]", "当前修复操作尚未完成，暂时不能关闭窗口。", RepairSuperLogTone.Warning);
            return;
        }
        _catalogLoadSource?.Cancel();
        _catalogLoadSource?.Dispose();
        _catalogLoadSource = null;
    }

    private void AppendAnalysisDetails(SuperPackageAnalysis analysis)
    {
        LpMetadataTemplate metadata = analysis.Template;
        LpBlockDeviceInfo device = analysis.SuperBlockDevice;
        AppendLog(
            "[Definition]",
            $"Super {BinaryHelpers.FormatBytes(device.Size)} · " +
            $"LP {metadata.Header.MajorVersion}.{metadata.Header.MinorVersion} · " +
            $"NV {analysis.Definition.NvId} · {metadata.Partitions.Count} partitions",
            RepairSuperLogTone.Normal);
    }

    private void AppendPreflightDetails(FastbootPreflight preflight)
    {
        AppendLog(
            "[Device]",
            $"{preflight.Serial} · Bootloader Fastboot · slot {preflight.CurrentSlot} · " +
            $"unlocked {preflight.Unlocked}",
            RepairSuperLogTone.Normal);
    }

    private void ReportBootloaderTransition(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ReportBootloaderTransition(message));
            return;
        }

        SetBusyVisual(true, message);
        AppendLog(
            "[Fastboot]",
            message,
            message.StartsWith("已确认", StringComparison.Ordinal)
                ? RepairSuperLogTone.Success
                : RepairSuperLogTone.Info);
    }

    private static string GetDefaultOutputPath(SuperPackageAnalysis analysis)
    {
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopPath))
            throw new DirectoryNotFoundException("无法定位当前用户的桌面目录");
        string safeNvId = new string(analysis.Definition.NvId
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_')
            .ToArray());
        if (string.IsNullOrWhiteSpace(safeNvId))
            throw new InvalidDataException("super_def 的 nv_id 无法用于镜像命名");
        return Path.Combine(
            desktopPath,
            "Violet_SuperFix",
            $"VioletFix_super.{safeNvId}.img");
    }

    private void AppendLog(string label, string message, RepairSuperLogTone tone)
    {
        try
        {
            _externalLog?.Invoke(label, message, tone);
        }
        catch
        {
            // 主窗口日志不可用时，不影响 Super 安全流程。
        }
    }

    private Action<bool, string?>? BeginLogStep(string label, string message)
    {
        try
        {
            return _externalStepLog?.Invoke(label, message);
        }
        catch
        {
            // 主窗口日志不可用时，不影响 Super 安全流程。
            return null;
        }
    }

    private void AppendException(Exception exception)
    {
        string message = exception.Message.Trim();
        if (string.IsNullOrWhiteSpace(message))
            message = exception.GetType().Name;
        AppendLog("[Error]", message, RepairSuperLogTone.Error);
    }

    private sealed record BrandOption(string DisplayName, string ApiName);
}
