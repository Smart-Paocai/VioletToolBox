using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Payload_Dumper_C_.Core;
using Payload_Dumper_C_.Models;

namespace WpfApp1
{
    public partial class MainWindow
    {
        private const string InputPlaceholderText = "支持本地/云端全量包_欧加散包_无需解压直接提取";
        private const string OutputPlaceholderText = "请选择解包后文件的保存路径文件夹...";

        private readonly ObservableCollection<PartitionItem> _payloadPartitions = new();
        private DispatcherTimer? _payloadUiTimer;
        private readonly Stopwatch _payloadStopwatch = new();
        private long _payloadLastBytes;
        private TimeSpan _payloadLastTime;

        private IRandomAccessReader? _payloadSourceReader;
        private IRandomAccessReader? _payloadReader;
        private PayloadContext? _payloadCtx;
        private bool _payloadIsGenericZip;
        private Dictionary<string, ZipStoredEntryLocator.ZipEntryInfo>? _genericZipEntries;

        private bool _payloadSuppressSelectAllApply;
        private bool _payloadBulkSelectionInProgress;
        private Paragraph? _payloadPendingExtractLine;
        private bool _payloadUiInitialized;
        private CancellationTokenSource? _payloadReadProgressCts;
        private bool _payloadReadUsingActualProgress;
        private string _payloadCurrentSpeed = "0 B/s";
        private CancellationTokenSource? _payloadOperationCts;
        private static readonly object PayloadErrorLogLock = new();

        public static readonly DependencyProperty SelectAllStateProperty =
            DependencyProperty.Register(
                nameof(SelectAllState),
                typeof(bool?),
                typeof(MainWindow),
                new PropertyMetadata(false, OnSelectAllStateChanged));

        public bool? SelectAllState
        {
            get => (bool?)GetValue(SelectAllStateProperty);
            set => SetValue(SelectAllStateProperty, value);
        }

        private const string DefaultMeizuPayloadReferer = "https://www.flyme.com/firmware.html";

        private static bool IsHttpOrHttpsUrl(string input)
        {
            return Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
                   (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsMeizuFirmwareResourceLink(string url)
        {
            url = CleanLink(url);
            return !string.IsNullOrWhiteSpace(url) &&
                   url.Contains("firmware-res.flyme.com/", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string?> ResolveMeizuPayloadInputAsync(
            string url,
            IReadOnlyDictionary<string, string> headers,
            CancellationToken cancellationToken)
        {
            url = CleanLink(url);
            if (string.IsNullOrWhiteSpace(url)) return null;
            if (!IsMeizuDownloadEntryLink(url)) return url;

            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler);

            foreach (var header in headers)
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(true);

            int statusCode = (int)response.StatusCode;
            if (statusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location;
                if (location == null) return null;
                if (!location.IsAbsoluteUri)
                {
                    location = new Uri(new Uri(url), location);
                }

                return location.ToString();
            }

            return response.RequestMessage?.RequestUri?.ToString() ?? url;
        }

        private async Task<(string Source, IReadOnlyDictionary<string, string> Headers)> PreparePayloadRemoteSourceAsync(
            string input,
            CancellationToken cancellationToken)
        {
            input = CleanLink(input);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!IsHttpOrHttpsUrl(input))
            {
                return (input, headers);
            }

            if (IsMeizuDownloadEntryLink(input))
            {
                headers = BuildMeizuRequestHeaders(DefaultMeizuPayloadReferer);
                string? resolved = await ResolveMeizuPayloadInputAsync(input, headers, cancellationToken).ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    input = resolved;
                }
            }
            else if (IsMeizuFirmwareResourceLink(input))
            {
                headers = BuildMeizuRequestHeaders(DefaultMeizuPayloadReferer);
            }

            input = NormalizeUrlForRequest(input);
            return (input, headers);
        }

        private static readonly IReadOnlyDictionary<string, string> DeviceCodeToModel =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PLZ110"] = "一加15T",
                ["PLQ110"] = "一加 ACE 6",
                ["PLR110"] = "一加 ACE 6T",
                ["PLK110"] = "一加15",
                ["PLU110"] = "一加Turbo 6",
                ["PLY110"] = "一加Turbo 6V",
                ["PYS110"] = "一加Turbo 6X",
                ["PYR110"] = "一加Turbo 6X Pro",
                ["PLC110"] = "一加ACE5至尊版",
                ["PMB110"] = "一加ACE6至尊版",
                ["OPD2513"] = "一加Pad3 Pro",
                ["OPD2413"] = "一加Pad2 Pro",
                ["PKX110"] = "一加13T",
                ["OPD2508"] = "一加平板 2",
                ["OPD2407"] = "一加平板",
                ["PKR110"] = "一加ACE5 Pro",
                ["PKG110"] = "一加ACE5",
                ["PJZ110"] = "一加13",
                ["OPD2404"] = "一加Pad Pro",
                ["OPD2417"] = "OPPO Pad SE",
                ["OPD2515"] = "OPPO Pad Mini",
                ["OPD2102"] = "OPPO Pad Air",
                ["OPD2301"] = "OPPO Pad Air2",
                ["OPD2405"] = "OPPO Pad 3",
                ["OPD2401"] = "OPPO Pad 3 Pro",
                ["OPD2409"] = "OPPO Pad 4 Pro",
                ["OPD2501"] = "OPPO Pad Air5",
                ["OPD2506"] = "OPPO Pad 5",
                ["OPD2511"] = "OPPO Pad 5 Pro",
                ["OPD2601"] = "OPPO Pad 6",
                ["PJX110"] = "一加ACE3 Pro",
                ["PJF110"] = "一加ACE3V",
                ["PJE110"] = "一加ACE3",
                ["PJD110"] = "一加12",
                ["PJA110"] = "一加ACE2 Pro",
                ["PHP110"] = "一加ACE2v",
                ["PHK110"] = "一加ACE2",
                ["PHB110"] = "一加11",
                ["PGP110"] = "一加ACE Pro",
                ["PGZ110"] = "一加ACE竞速版",
                ["PKGM10"] = "一加ACE",
                ["NE2210"] = "一加10Pro",
                ["martini"] = "一加 9RT",
                ["lemonades"] = "一加 9R",
                ["lemonadep"] = "一加 9 Pro",
                ["lemonade"] = "一加 9",
                ["kebab"] = "一加 8T",
                ["instantnoodlep"] = "一加 8 Pro",
                ["instantnoodle"] = "一加 8",
                ["hotdogg"] = "一加 7T Pro",
                ["RMX3370"] = "真我GT Neo2",
                ["RMX3357"] = "真我GT neo2T",
                ["RMX3562"] = "真我GT neo3 150w",
                ["RMX3560"] = "真我GT neo3 80w",
                ["RMX3706"] = "真我GT neo5 150w",
                ["RMX3708"] = "真我GT neo5 240w",
                ["RMX3700"] = "真我GT neo5 SE",
                ["RMX3850"] = "真我GT neo6 SE",
                ["RMX3852"] = "真我GT neo6",
                ["RMX3366"] = "真我GT大师探索版",
                ["RMX3300"] = "真我GT2 Pro",
                ["RMX3551"] = "真我GT2大师探索版",
                ["RMX3310"] = "真我GT2",
                ["RMX3820"] = "真我GT5 150w",
                ["RMX3823"] = "真我GT5 240w",
                ["RMX3888"] = "真我GT5 Pro",
                ["RMX3800"] = "真我GT6",
                ["RMX5090"] = "真我GT7 Pro竞速版",
                ["RMX5010"] = "真我GT7 Pro",
                ["RMX6688"] = "真我GT7",
                ["RMX5200"] = "真我GT8 Pro",
                ["RMX6699"] = "真我GT8",
                ["RMX8899"] = "真我Neo8",
                ["RMX5080"] = "真我GT neo7 SE",
                ["RMX5062"] = "真我GT neo7 Turbo",
                ["RMX5060"] = "真我GT neo7",
                ["RMX5071"] = "真我GT neo7X",
            };

        private void PayloadView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_payloadUiInitialized) return;
            _payloadUiInitialized = true;

            PartitionsDataGrid.ItemsSource = _payloadPartitions;
            var view = CollectionViewSource.GetDefaultView(_payloadPartitions);
            view.Filter = FilterPayloadPartition;

            _payloadUiTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => UpdatePayloadStatusUi(), Dispatcher);
            _payloadUiTimer.Start();

            UpdatePayloadSelectAllState();
            ExportButton.IsEnabled = false;
            StopOperationButton.IsEnabled = false;
        }

        private void PayloadButton_Click(object sender, RoutedEventArgs e)
        {
            var homeView = this.FindName("HomeView") as Grid;
            var screenMirrorView = this.FindName("ScreenMirrorView") as Grid;
            var basicFlashView = this.FindName("BasicFlashView") as Grid;
            var fastbootVisualizationView = this.FindName("FastbootVisualizationView") as Grid;
            var hiddenEnvironmentView = this.FindName("HiddenEnvironmentView") as Grid;
            var downloadView = this.FindName("DownloadView") as Grid;
            var aboutToolView = this.FindName("AboutToolView") as Grid;
            var systemZoneView = this.FindName("SystemZoneView") as Grid;
            var oujiaFlashView = this.FindName("OujiaFlashView") as Grid;
            var autorootView = this.FindName("AutorootView") as Grid;
            var appManagementView = this.FindName("AppManagementView") as Grid;
            var androidGeneralView = this.FindName("AndroidGeneralView") as Grid;
            var payloadView = this.FindName("PayloadView") as Grid;
            var romDownloadView = this.FindName("RomDownloadview") as Grid;
            var edlFlashView = this.FindName("EdlFlashView") as Grid;
            var colorOSAssistantView = this.FindName("ColorOSAssistantView") as Grid;
            var backupAssistantView = this.FindName("BackupAssistantView") as Grid;
            var violetDownloadView = this.FindName("VioletDownloadView") as Grid;

            if (homeView != null) homeView.Visibility = Visibility.Collapsed;
            if (screenMirrorView != null) screenMirrorView.Visibility = Visibility.Collapsed;
            if (basicFlashView != null) basicFlashView.Visibility = Visibility.Collapsed;
            if (fastbootVisualizationView != null) fastbootVisualizationView.Visibility = Visibility.Collapsed;
            if (hiddenEnvironmentView != null) hiddenEnvironmentView.Visibility = Visibility.Collapsed;
            if (downloadView != null) downloadView.Visibility = Visibility.Collapsed;
            if (aboutToolView != null) aboutToolView.Visibility = Visibility.Collapsed;
            if (systemZoneView != null) systemZoneView.Visibility = Visibility.Collapsed;
            if (oujiaFlashView != null) oujiaFlashView.Visibility = Visibility.Collapsed;
            if (autorootView != null) autorootView.Visibility = Visibility.Collapsed;
            if (appManagementView != null) appManagementView.Visibility = Visibility.Collapsed;
            if (androidGeneralView != null) androidGeneralView.Visibility = Visibility.Collapsed;
            if (romDownloadView != null) romDownloadView.Visibility = Visibility.Collapsed;
            if (edlFlashView != null) edlFlashView.Visibility = Visibility.Collapsed;
            if (colorOSAssistantView != null) colorOSAssistantView.Visibility = Visibility.Collapsed;
            if (backupAssistantView != null) backupAssistantView.Visibility = Visibility.Collapsed;
            if (violetDownloadView != null) violetDownloadView.Visibility = Visibility.Collapsed;
            
            if (payloadView != null) payloadView.Visibility = Visibility.Visible;

            UpdateButtonStates("Payload");

            currentView = "Payload";
        }

        private bool FilterPayloadPartition(object obj)
        {
            if (obj is not PartitionItem p) return false;
            var key = SearchTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(key)) return true;
            return p.Name.Contains(key, StringComparison.OrdinalIgnoreCase);
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            CollectionViewSource.GetDefaultView(_payloadPartitions).Refresh();
            UpdatePayloadSelectAllState();
        }

        private void BrowseInputButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "payload.bin/zip (*.bin;*.zip)|*.bin;*.zip|All files (*.*)|*.*",
                Title = "选择 payload.bin 或 OTA ZIP"
            };
            if (dlg.ShowDialog(this) == true)
            {
                InputPathTextBox.Text = dlg.FileName;
            }
        }

        private void BrowseOutputButton_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择输出文件夹",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };
            var r = dlg.ShowDialog();
            if (r == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
            {
                OutputDirTextBox.Text = dlg.SelectedPath;
            }
        }

        private async void ReadInfoButton_Click(object sender, RoutedEventArgs e)
        {
            var operationCts = BeginPayloadOperation();
            CancellationToken cancellationToken = operationCts.Token;
            bool readSucceeded = false;
            try
            {
                ReadInfoButton.IsEnabled = false;
                ExportButton.IsEnabled = false;
                SetPayloadStatus("读取中...");
                ClearPayloadPartitions();
                OverallProgressBar.Value = 0;
                OverallPercentTextBlock.Text = "0.00%";
                _payloadIsGenericZip = false;
                StartPayloadReadProgressSimulation();

                await CloseOpenedPayloadReadersAsync().ConfigureAwait(true);

                string input = InputPathTextBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(input) || input.Contains("请选择", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("请输入路径或 URL");

                var unzipProgress = new Progress<double>(p =>
                {
                    _payloadReadUsingActualProgress = true;
                    OverallProgressBar.Value = p;
                    OverallPercentTextBlock.Text = $"{p:0.00}%";
                });

                var (preparedInput, requestHeaders) = await PreparePayloadRemoteSourceAsync(input, cancellationToken).ConfigureAwait(true);
                _payloadSourceReader = await PayloadProcessing.OpenSourceAsync(preparedInput, cancellationToken, requestHeaders).ConfigureAwait(true);
                if (await TryPrepareRemoteGenericZipAsync(preparedInput, cancellationToken).ConfigureAwait(true))
                {
                    SetPayloadStatus("ZIP 目录已读取");
                    readSucceeded = true;
                    return;
                }

                var (payloadReader, _) = await PayloadProcessing.OpenPayloadReaderAsync(
                        _payloadSourceReader,
                        cancellationToken,
                        log: AppendPayloadLog,
                        extractProgress: unzipProgress,
                        eagerExtract: false)
                    .ConfigureAwait(true);
                _payloadReader = payloadReader;

                _payloadCtx = await PayloadProcessing.ReadManifestAsync(_payloadReader, cancellationToken).ConfigureAwait(true);

                var infos = PayloadProcessing.GetPartitions(_payloadCtx);
                foreach (var p in infos)
                {
                    var item = new PartitionItem
                    {
                        Name = p.Name,
                        SizeBytes = p.SizeBytes,
                        SizeReadable = p.SizeReadable,
                        IsSelected = false
                    };
                    item.PropertyChanged += PayloadPartitionItem_PropertyChanged;
                    _payloadPartitions.Add(item);
                }

                AppendPayloadLog($"读取完成: 共 {infos.Count} 个分区");
                ExportButton.IsEnabled = true;
                SelectAllState = true;
                UpdatePayloadSelectAllState();

                if (OverallProgressBar.Value <= 0.1)
                {
                    await SimulatePayloadOverallProgressOnceAsync(cancellationToken).ConfigureAwait(true);
                }

                try
                {
                    SetPayloadStatus("读取metadata中...");
                    if (_payloadSourceReader is null) throw new InvalidOperationException("未打开源文件");
                    var text = await PayloadProcessing.ReadAndroidMetadataTextAsync(_payloadSourceReader, cancellationToken).ConfigureAwait(true);

                    var versionDetails = ParsePayloadVersionDetails(text);

                    if (!versionDetails.HasAnyValue)
                    {
                        AppendPayloadLog("未解析到版本信息[跳过解析]");
                    }
                    else
                    {
                        AppendPayloadLog("");
                        AppendPayloadLog($"手机机型：{versionDetails.Model}");
                        AppendPayloadLog($"设备代号：{versionDetails.ProductName}");
                        AppendPayloadLog($"安卓版本：{versionDetails.AndroidVersion}");
                        AppendPayloadLog($"版本信息：{versionDetails.VersionName}");
                        AppendPayloadLog($"安全补丁：{versionDetails.SecurityPatch}");
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception metadataException)
                {
                    WritePayloadExceptionLog(metadataException, "metadata解析", CreatePayloadErrorId());
                    AppendPayloadLog("未解析到版本信息[跳过解析]");
                }

                SetPayloadStatus("准备就绪");
                readSucceeded = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                AppendPayloadLog("已停止操作");
                SetPayloadStatus("已停止");
            }
            catch (Exception ex)
            {
                try
                {
                    bool handled = await TryHandleGenericZipOnOpenErrorAsync(ex, cancellationToken).ConfigureAwait(true);
                    if (handled)
                    {
                        readSucceeded = true;
                    }
                    else
                    {
                        ReportPayloadError(ex, "读取信息");
                        SetPayloadStatus("读取失败");
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    AppendPayloadLog("已停止操作");
                    SetPayloadStatus("已停止");
                }
                catch (Exception fallbackException)
                {
                    ReportPayloadError(fallbackException, "读取 ZIP 目录");
                    SetPayloadStatus("读取失败");
                }
            }
            finally
            {
                StopPayloadReadProgressSimulation(readSucceeded);
                ReadInfoButton.IsEnabled = true;
                ExportButton.IsEnabled = _payloadCtx is not null || _payloadIsGenericZip;
                EndPayloadOperation(operationCts);
            }
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var operationCts = BeginPayloadOperation();
            CancellationToken cancellationToken = operationCts.Token;
            try
            {
                if (_payloadCtx is null && !_payloadIsGenericZip) throw new InvalidOperationException("请先点击“读取信息”");

                string outDir = OutputDirTextBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(outDir) || outDir.Contains("请选择", StringComparison.OrdinalIgnoreCase))
                {
                    outDir = Path.Combine(Environment.CurrentDirectory, "output");
                    OutputDirTextBox.Text = outDir;
                }

                PartitionsDataGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                PartitionsDataGrid.CommitEdit(DataGridEditingUnit.Row, true);

                var selected = _payloadPartitions.Where(p => p.IsSelected).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (selected.Count == 0)
                {
                    foreach (var item in PartitionsDataGrid.SelectedItems)
                    {
                        if (item is PartitionItem p) selected.Add(p.Name);
                    }
                }
                if (selected.Count == 0) throw new InvalidOperationException("请选择要导出的分区");

                _payloadStopwatch.Restart();
                _payloadLastBytes = _payloadCtx?.Reader.BytesRead ?? _payloadSourceReader?.BytesRead ?? 0;
                _payloadLastTime = TimeSpan.Zero;

                if (_payloadIsGenericZip)
                {
                    ReadInfoButton.IsEnabled = false;
                    ExportButton.IsEnabled = false;
                    OverallProgressBar.Value = 0;
                    OverallPercentTextBlock.Text = "0.00%";
                    SetPayloadStatus("ZIP 提取中...");
                    long totalCompressedBytes = selected
                        .Select(name => _genericZipEntries != null && _genericZipEntries.TryGetValue(name, out var info)
                            ? Math.Max(1L, info.CompressedSize)
                            : 0L)
                        .Sum();

                    var zipProgress = new Progress<long>(doneBytes =>
                    {
                        if (totalCompressedBytes <= 0) return;
                        double percent = Math.Min(100d, doneBytes * 100d / totalCompressedBytes);
                        OverallProgressBar.Value = percent;
                        OverallPercentTextBlock.Text = $"{percent:0.00}%";
                    });

                    await ExportGenericZipEntriesAsync(selected, outDir, zipProgress, cancellationToken).ConfigureAwait(true);
                    OverallProgressBar.Value = 100;
                    OverallPercentTextBlock.Text = "100.00%";
                    AppendPayloadLog("[ZIP] 所选条目提取完成..");
                    SetPayloadStatus("完成");
                    return;
                }

                ReadInfoButton.IsEnabled = false;
                ExportButton.IsEnabled = false;

                OverallProgressBar.Value = 0;
                OverallPercentTextBlock.Text = "0.00%";

                if (_payloadCtx.Reader is ZipPayloadLazyReader lazy && !lazy.IsExtracted)
                {
                    SetPayloadStatus("解压payload.bin中...");
                    var unzipProgress = new Progress<double>(p =>
                    {
                        OverallProgressBar.Value = p;
                        OverallPercentTextBlock.Text = $"{p:0.00}%";
                    });

                    await lazy.EnsureExtractedAsync(unzipProgress, cancellationToken).ConfigureAwait(true);
                    OverallProgressBar.Value = 0;
                    OverallPercentTextBlock.Text = "0.00%";
                }

                SetPayloadStatus("导出中...");

                var progress = new Progress<(long DoneBytes, long TotalBytes)>(p =>
                {
                    if (p.TotalBytes <= 0) return;
                    double percent = (double)p.DoneBytes * 100d / p.TotalBytes;
                    OverallProgressBar.Value = percent;
                    OverallPercentTextBlock.Text = $"{percent:0.00}%";
                });

                await PayloadProcessing.ExtractPartitionsAsync(
                        _payloadCtx,
                        selected,
                        outDir,
                        Environment.ProcessorCount,
                        log: AppendPayloadExportLog,
                        progress: progress,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(true);

                AppendPayloadLog("[Done]所有选中分区提取完成..");
                SetPayloadStatus("完成");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                AppendPayloadExtractFail("已停止");
                AppendPayloadLog("已停止操作");
                SetPayloadStatus("已停止");
            }
            catch (Exception ex)
            {
                ReportPayloadError(ex, "提取镜像");
                SetPayloadStatus("导出失败");
            }
            finally
            {
                _payloadStopwatch.Stop();
                ReadInfoButton.IsEnabled = true;
                ExportButton.IsEnabled = _payloadCtx is not null || _payloadIsGenericZip;
                EndPayloadOperation(operationCts);
            }
        }

        private async Task<bool> TryPrepareRemoteGenericZipAsync(string input, CancellationToken cancellationToken)
        {
            if (_payloadSourceReader is not HttpRangeReader)
            {
                return false;
            }

            if (!input.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                SetPayloadStatus("读取 ZIP 目录中...");
                var entries = await ZipStoredEntryLocator.ListEntriesAsync(_payloadSourceReader, cancellationToken).ConfigureAwait(true);
                if (entries.Count == 0)
                {
                    return false;
                }

                bool hasPayload = entries.Any(e =>
                    !string.IsNullOrEmpty(e.Name) &&
                    e.Name.EndsWith("payload.bin", StringComparison.OrdinalIgnoreCase));

                if (hasPayload)
                {
                    return false;
                }

                InitializeGenericZipEntries(entries);
                AppendPayloadLog("检测到远程 ZIP 不包含 payload.bin，直接按通用 ZIP 模式读取。");
                AppendPayloadLog($"ZIP 中共 {_payloadPartitions.Count} 个文件条目。");
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }

        private async Task SimulatePayloadOverallProgressOnceAsync(CancellationToken cancellationToken)
        {
            const int totalMs = 360;
            const int steps = 18;

            for (int i = 0; i <= steps; i++)
            {
                double p = i * 100d / steps;
                OverallProgressBar.Value = p;
                OverallPercentTextBlock.Text = $"{p:0.00}%";
                await Task.Delay(totalMs / steps, cancellationToken).ConfigureAwait(true);
            }

            await Task.Delay(60, cancellationToken).ConfigureAwait(true);
            OverallProgressBar.Value = 0;
            OverallPercentTextBlock.Text = "0.00%";
        }

        private void PayloadStopOperationButton_Click(object sender, RoutedEventArgs e)
        {
            if (_payloadOperationCts is null || _payloadOperationCts.IsCancellationRequested) return;

            StopOperationButton.IsEnabled = false;
            _payloadOperationCts.Cancel();
        }

        private CancellationTokenSource BeginPayloadOperation()
        {
            _payloadOperationCts?.Dispose();
            _payloadOperationCts = new CancellationTokenSource();
            StopOperationButton.IsEnabled = true;
            return _payloadOperationCts;
        }

        private void EndPayloadOperation(CancellationTokenSource operationCts)
        {
            if (ReferenceEquals(_payloadOperationCts, operationCts))
            {
                _payloadOperationCts = null;
                StopOperationButton.IsEnabled = false;
            }

            operationCts.Dispose();
        }

        private void ClearPayloadPartitions()
        {
            foreach (var p in _payloadPartitions)
            {
                p.PropertyChanged -= PayloadPartitionItem_PropertyChanged;
            }
            _payloadPartitions.Clear();
            _genericZipEntries = null;
            UpdatePayloadSelectAllState();
        }

        private static void OnSelectAllStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var w = (MainWindow)d;
            if (w._payloadSuppressSelectAllApply) return;
            if (e.NewValue is not bool b) return;
            w.ApplyPayloadSelectAllToView(b);
        }

        private void PayloadSelectAllCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.CheckBox cb) return;
            ApplyPayloadSelectAllToView(cb.IsChecked == true);
        }

        private void ApplyPayloadSelectAllToView(bool selected)
        {
            _payloadSuppressSelectAllApply = true;
            _payloadBulkSelectionInProgress = true;
            try
            {
                var view = CollectionViewSource.GetDefaultView(_payloadPartitions);
                foreach (var obj in view)
                {
                    if (obj is PartitionItem p) p.IsSelected = selected;
                }
            }
            finally
            {
                _payloadBulkSelectionInProgress = false;
                _payloadSuppressSelectAllApply = false;
                UpdatePayloadSelectAllState();
            }
        }

        private void PayloadPartitionItem_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_payloadBulkSelectionInProgress) return;
            if (!string.Equals(e.PropertyName, nameof(PartitionItem.IsSelected), StringComparison.Ordinal)) return;
            UpdatePayloadSelectAllState();
        }

        private void UpdatePayloadSelectAllState()
        {
            var view = CollectionViewSource.GetDefaultView(_payloadPartitions);
            int total = 0;
            int selected = 0;
            foreach (var obj in view)
            {
                if (obj is not PartitionItem p) continue;
                total++;
                if (p.IsSelected) selected++;
            }

            bool? state =
                total == 0 ? false :
                selected == 0 ? false :
                selected == total ? true :
                null;

            _payloadSuppressSelectAllApply = true;
            try
            {
                if (SelectAllState != state) SelectAllState = state;
            }
            finally
            {
                _payloadSuppressSelectAllApply = false;
            }
        }

        private void ReportPayloadError(Exception exception, string operation)
        {
            string errorId = CreatePayloadErrorId();
            string? logPath = WritePayloadExceptionLog(exception, operation, errorId);
            Exception root = UnwrapPayloadException(exception);
            var (message, isKnown) = GetPayloadUserError(root);

            AppendPayloadLog($"{operation}失败：{message}");
            if (!isKnown)
            {
                AppendPayloadLog($"错误编号：{errorId}");
                if (string.IsNullOrWhiteSpace(logPath))
                {
                    AppendPayloadLog("详细错误日志写入失败");
                }
            }
        }

        private static (string Message, bool IsKnown) GetPayloadUserError(Exception exception)
        {
            string message = exception.Message?.Trim() ?? string.Empty;

            if (message.Contains("请输入路径或 URL", StringComparison.OrdinalIgnoreCase))
                return ("请输入有效的本地文件路径或 OTA 下载链接", true);
            if (message.Contains("请先点击", StringComparison.OrdinalIgnoreCase))
                return ("请先读取包信息，再执行提取", true);
            if (message.Contains("请选择要导出的分区", StringComparison.OrdinalIgnoreCase))
                return ("尚未选择需要提取的分区", true);
            if (message.Contains("找不到文件或不是有效 URL", StringComparison.OrdinalIgnoreCase))
                return ("输入文件不存在，或链接格式无效", true);

            if (message.Contains("不支持 Range", StringComparison.OrdinalIgnoreCase))
                return ("下载服务器不支持分段读取，请先将 OTA 下载到本地再导入", true);
            if (message.Contains("Content-Length", StringComparison.OrdinalIgnoreCase))
                return ("下载服务器未提供文件大小，请先将 OTA 下载到本地再导入", true);
            if (message.Contains("Partial Content", StringComparison.OrdinalIgnoreCase))
                return ("下载服务器拒绝了分段读取，请检查链接是否过期，或下载到本地后导入", true);
            if (message.Contains("远程块读取不完整", StringComparison.OrdinalIgnoreCase))
                return ("网络传输不完整，请检查网络后重试", true);

            if (message.Contains("没有 payload.bin", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("未找到条目: payload.bin", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("无法定位 payload.bin", StringComparison.OrdinalIgnoreCase))
                return ("该 OTA 包中没有可提取的 payload.bin", true);
            if (message.Contains("payload.bin magic", StringComparison.OrdinalIgnoreCase))
                return ("所选文件不是有效的 payload.bin", true);
            if (message.Contains("不是 ZIP", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("EOCD", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("ZIP64", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("中央目录", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("LocalFileHeader", StringComparison.OrdinalIgnoreCase))
                return ("ZIP 文件结构损坏或文件下载不完整", true);
            if (message.Contains("暂不支持的压缩方法", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("目标条目不是 Stored", StringComparison.OrdinalIgnoreCase))
                return ("该 ZIP 使用了当前不支持的压缩方式，请先解压后再导入 payload.bin", true);

            if (message.Contains("差分 OTA", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("SOURCE_", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("src_sha256_hash", StringComparison.OrdinalIgnoreCase))
                return ("该文件是差分 OTA，无法直接提取完整镜像", true);
            if (message.Contains("暂不支持的 operation", StringComparison.OrdinalIgnoreCase))
                return ($"OTA 包含暂不支持的数据操作：{GetFirstPayloadErrorLine(message)}", true);
            if (message.Contains("sha256", StringComparison.OrdinalIgnoreCase))
                return ("数据校验失败，OTA 文件可能已损坏或下载不完整", true);
            if (message.Contains("manifest", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("payload 版本", StringComparison.OrdinalIgnoreCase))
                return ("Payload 清单格式不受支持或数据已损坏", true);

            if (exception is UnauthorizedAccessException || exception is System.Security.SecurityException)
                return ("没有文件访问权限，请更换输出目录或以管理员身份运行", true);
            if (exception is PathTooLongException)
                return ("文件路径过长，请选择层级更浅的输入或输出目录", true);
            if (exception is DriveNotFoundException)
                return ("目标磁盘不存在或已断开连接", true);
            if (exception is DirectoryNotFoundException)
                return ("指定目录不存在，请重新选择路径", true);
            if (exception is FileNotFoundException)
                return ("未找到指定文件，请确认文件未被移动或删除", true);

            if (exception is HttpRequestException httpException)
            {
                int? statusCode = httpException.StatusCode.HasValue ? (int)httpException.StatusCode.Value : null;
                return statusCode switch
                {
                    401 or 403 => ("服务器拒绝访问，下载链接可能需要登录或已经过期", true),
                    404 => ("服务器上未找到该 OTA 文件，下载链接可能已经失效", true),
                    416 => ("服务器不接受当前分段下载请求，请下载到本地后导入", true),
                    429 => ("服务器请求过于频繁，请稍后重试", true),
                    >= 500 => ("下载服务器暂时不可用，请稍后重试", true),
                    _ => ("网络连接失败，请检查网络、代理或下载链接", true)
                };
            }
            if (exception is TimeoutException || exception is TaskCanceledException)
                return ("网络或文件读取超时，请稍后重试", true);
            if (exception is System.Net.Sockets.SocketException)
                return ("无法连接下载服务器，请检查网络、DNS 或代理设置", true);

            if (exception is EndOfStreamException)
                return ("文件数据不完整，OTA 可能没有下载完成", true);
            if (exception is IOException ioException)
            {
                int nativeCode = ioException.HResult & 0xFFFF;
                if (nativeCode is 39 or 112)
                    return ("磁盘剩余空间不足，请清理空间或更换输出目录", true);
                if (nativeCode is 32 or 33)
                    return ("文件正被其他程序占用，请关闭占用程序后重试", true);
                return ($"文件读写失败：{GetFirstPayloadErrorLine(message)}", true);
            }
            if (exception is InvalidDataException)
                return ($"包数据异常：{GetFirstPayloadErrorLine(message)}", true);
            if (exception is NotSupportedException)
                return ($"当前暂不支持该包：{GetFirstPayloadErrorLine(message)}", true);
            if (exception is InvalidOperationException && !string.IsNullOrWhiteSpace(message))
                return (GetFirstPayloadErrorLine(message), true);
            if (exception is OutOfMemoryException)
                return ("可用内存不足，请关闭其他程序后重试", true);

            return ("发生未识别错误，请根据错误编号查看详细日志", false);
        }

        private static Exception UnwrapPayloadException(Exception exception)
        {
            if (exception is AggregateException aggregate)
            {
                var flattened = aggregate.Flatten();
                if (flattened.InnerExceptions.Count > 0)
                {
                    return UnwrapPayloadException(flattened.InnerExceptions[0]);
                }
            }
            return exception;
        }

        private static string GetFirstPayloadErrorLine(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return "数据格式无效";
            int lineEnd = message.IndexOfAny(['\r', '\n']);
            return (lineEnd >= 0 ? message[..lineEnd] : message).Trim();
        }

        private static string CreatePayloadErrorId()
        {
            return $"PV-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..33].ToUpperInvariant();
        }

        private static string? WritePayloadExceptionLog(Exception exception, string operation, string errorId)
        {
            string entry =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{errorId}] [{operation}]{Environment.NewLine}" +
                exception + Environment.NewLine + new string('-', 80) + Environment.NewLine;

            string[] roots =
            [
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp", "log"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VioletTool", "log")
            ];

            lock (PayloadErrorLogLock)
            {
                foreach (string directory in roots.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        Directory.CreateDirectory(directory);
                        string path = Path.Combine(directory, $"VioletToolPayloadLog_{DateTime.Now:yyyyMMdd}.txt");
                        File.AppendAllText(path, entry, new UTF8Encoding(false));
                        return path;
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        private void AppendPayloadLog(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendPayloadLog(message));
                return;
            }

            var lines = (message ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            foreach (var line in lines)
            {
                var p = new Paragraph { Margin = new Thickness(0) };
                p.Inlines.Add(new Run(line));
                LogTextBox.Document.Blocks.Add(p);
            }
            LogTextBox.ScrollToEnd();
        }

        private void AppendPayloadExportLog(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendPayloadExportLog(message));
                return;
            }

            const string StartPrefix = "开始导出:";
            const string EndPrefix = "完成导出:";

            var msg = message?.Trim() ?? string.Empty;
            if (msg.StartsWith(StartPrefix, StringComparison.Ordinal))
            {
                var s = msg[StartPrefix.Length..].Trim();
                var name = s;
                int i = s.IndexOf(' ');
                if (i > 0) name = s[..i];
                AppendPayloadExtractStart($"{name}.img...");
                return;
            }

            if (msg.StartsWith(EndPrefix, StringComparison.Ordinal))
            {
                AppendPayloadExtractOk();
                return;
            }

            AppendPayloadLog(message ?? string.Empty);
        }

        private void AppendPayloadExtractStart(string text)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendPayloadExtractStart(text));
                return;
            }

            _payloadPendingExtractLine = new Paragraph { Margin = new Thickness(0) };
            _payloadPendingExtractLine.Inlines.Add(new Run($"[提取]{text}") { Foreground = System.Windows.Media.Brushes.Black });
            LogTextBox.Document.Blocks.Add(_payloadPendingExtractLine);
            LogTextBox.ScrollToEnd();
        }

        private void AppendPayloadExtractOk()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(AppendPayloadExtractOk);
                return;
            }

            var p = _payloadPendingExtractLine;
            if (p is null)
            {
                AppendPayloadLog("OK");
                return;
            }

            p.Inlines.Add(new Run(" ") { Foreground = System.Windows.Media.Brushes.Black });
            p.Inlines.Add(new Run("OK") { Foreground = System.Windows.Media.Brushes.Green });
            _payloadPendingExtractLine = null;
            LogTextBox.ScrollToEnd();
        }

        private void AppendPayloadExtractFail(string text = "失败")
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendPayloadExtractFail(text));
                return;
            }

            var p = _payloadPendingExtractLine;
            if (p is null)
            {
                AppendPayloadLog(text);
                return;
            }

            p.Inlines.Add(new Run(" ") { Foreground = System.Windows.Media.Brushes.Black });
            p.Inlines.Add(new Run(text) { Foreground = System.Windows.Media.Brushes.Red });
            _payloadPendingExtractLine = null;
            LogTextBox.ScrollToEnd();
        }

        private void SetPayloadStatus(string status)
        {
            UpdatePayloadProgressTag();
        }

        private void UpdatePayloadStatusUi()
        {
            if (!_payloadStopwatch.IsRunning)
            {
                _payloadCurrentSpeed = "0 B/s";
                UpdatePayloadProgressTag();
                return;
            }

            long nowBytes = _payloadCtx?.Reader.BytesRead ?? _payloadSourceReader?.BytesRead ?? 0;
            var nowTime = _payloadStopwatch.Elapsed;

            long deltaBytes = nowBytes - _payloadLastBytes;
            double deltaSeconds = (nowTime - _payloadLastTime).TotalSeconds;
            if (deltaSeconds <= 0) deltaSeconds = 0.5;

            double bps = deltaBytes / deltaSeconds;
            _payloadCurrentSpeed = $"{ToPayloadReadableSpeed(bps)}/s";
            UpdatePayloadProgressTag();

            _payloadLastBytes = nowBytes;
            _payloadLastTime = nowTime;
        }

        private void UpdatePayloadProgressTag()
        {
            TimeSpan elapsedTime = _payloadStopwatch.Elapsed;
            string elapsed = $"{(long)elapsedTime.TotalMinutes:00}:{elapsedTime.Seconds:00}";
            OverallProgressBar.Tag = $"速度: {_payloadCurrentSpeed}  |  耗时: {elapsed}";
        }

        private static string ToPayloadReadableSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond >= 1024d * 1024d * 1024d) return $"{bytesPerSecond / (1024d * 1024d * 1024d):0.0} GB";
            if (bytesPerSecond >= 1024d * 1024d) return $"{bytesPerSecond / (1024d * 1024d):0.0} MB";
            if (bytesPerSecond >= 1024d) return $"{bytesPerSecond / 1024d:0.0} KB";
            return $"{bytesPerSecond:0} B";
        }

        private static string ToPayloadReadableSize(long bytes)
        {
            double b = bytes;
            if (b >= 1024d * 1024d * 1024d) return $"{b / (1024d * 1024d * 1024d):0.0}GB";
            if (b >= 1024d * 1024d) return $"{b / (1024d * 1024d):0.0}MB";
            return $"{b / 1024d:0.0}KB";
        }

        private async Task<bool> TryHandleGenericZipOnOpenErrorAsync(Exception ex, CancellationToken cancellationToken)
        {
            string msg = ex.Message ?? "";
            if (!msg.Contains("payload.bin", StringComparison.OrdinalIgnoreCase) &&
                !msg.Contains("ZIP 中未找到条目", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (_payloadSourceReader is null)
            {
                return false;
            }

            try
            {
                SetPayloadStatus("读取 ZIP 目录中...");
                ClearPayloadPartitions();
                _genericZipEntries = null;
                OverallProgressBar.Value = 0;
                OverallPercentTextBlock.Text = "0.00%";

                var entries = await ZipStoredEntryLocator.ListEntriesAsync(_payloadSourceReader, cancellationToken).ConfigureAwait(true);
                InitializeGenericZipEntries(entries);
                AppendPayloadLog("检测到 ZIP 包不包含 payload.bin，将作为通用 ZIP 处理。");
                AppendPayloadLog($"ZIP 中共 {_payloadPartitions.Count} 个文件条目。");

                ExportButton.IsEnabled = _payloadPartitions.Count > 0;
                SelectAllState = _payloadPartitions.Count > 0;
                UpdatePayloadSelectAllState();

                SetPayloadStatus("ZIP 目录已读取");
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                throw;
            }
        }

        private void InitializeGenericZipEntries(
            IReadOnlyList<ZipStoredEntryLocator.ZipEntryInfo> entries)
        {
            ClearPayloadPartitions();

            var map = new Dictionary<string, ZipStoredEntryLocator.ZipEntryInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;

                long size = entry.UncompressedSize;
                var item = new PartitionItem
                {
                    Name = entry.Name,
                    SizeBytes = size,
                    SizeReadable = ToPayloadReadableSize(size),
                    IsSelected = false
                };
                item.PropertyChanged += PayloadPartitionItem_PropertyChanged;
                _payloadPartitions.Add(item);
                map[entry.Name] = entry;
            }

            _genericZipEntries = map;
            _payloadIsGenericZip = true;
            ExportButton.IsEnabled = _payloadPartitions.Count > 0;
            SelectAllState = _payloadPartitions.Count > 0;
            UpdatePayloadSelectAllState();
        }

        private async Task ExportGenericZipEntriesAsync(
            HashSet<string> selected,
            string outDir,
            IProgress<long>? progress,
            CancellationToken cancellationToken)
        {
            if (!_payloadIsGenericZip)
            {
                throw new InvalidOperationException("ZIP 目录尚未读取");
            }

            if (_payloadSourceReader is null)
            {
                throw new InvalidOperationException("ZIP 源未打开");
            }

            if (_genericZipEntries is null || _genericZipEntries.Count == 0)
            {
                throw new InvalidOperationException("ZIP 目录信息缺失");
            }

            Directory.CreateDirectory(outDir);
            long completedBytes = 0;
            progress?.Report(0);

            foreach (var name in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_genericZipEntries.TryGetValue(name, out var info))
                {
                    AppendPayloadLog($"[ZIP] 跳过未知条目 {name}");
                    continue;
                }

                string targetPath = Path.Combine(outDir, name.Replace('/', Path.DirectorySeparatorChar));
                string? dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string fileName = Path.GetFileName(targetPath);
                if (string.IsNullOrEmpty(fileName))
                {
                    continue;
                }

                string partialPath = $"{targetPath}.partial";
                TryDeletePayloadPartialFile(partialPath);
                var entryProgress = new Progress<long>(entryDone =>
                {
                    progress?.Report(completedBytes + entryDone);
                });
                AppendPayloadExtractStart($"{name}...");
                try
                {
                    await using (var outFs = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
                    {
                        await ZipStoredEntryLocator.ExtractEntryAsync(_payloadSourceReader, info, outFs, cancellationToken, entryProgress).ConfigureAwait(false);
                    }
                    File.Move(partialPath, targetPath, true);
                    completedBytes += Math.Max(1L, info.CompressedSize);
                    progress?.Report(completedBytes);
                    AppendPayloadExtractOk();
                }
                catch
                {
                    TryDeletePayloadPartialFile(partialPath);
                    AppendPayloadExtractFail();
                    throw;
                }
            }
        }

        private static void TryDeletePayloadPartialFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        private void StartPayloadReadProgressSimulation()
        {
            _payloadReadProgressCts?.Cancel();
            _payloadReadProgressCts?.Dispose();
            _payloadReadProgressCts = new CancellationTokenSource();
            _payloadReadUsingActualProgress = false;
            _ = RunPayloadReadProgressSimulationAsync(_payloadReadProgressCts.Token);
        }

        private async Task RunPayloadReadProgressSimulationAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!_payloadReadUsingActualProgress)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (cancellationToken.IsCancellationRequested) return;

                            double current = OverallProgressBar.Value;
                            if (current >= 92) return;

                            double step =
                                current < 45 ? 5 :
                                current < 75 ? 2.5 :
                                1;

                            double next = Math.Min(92, current + step);
                            OverallProgressBar.Value = next;
                            OverallPercentTextBlock.Text = $"{next:0.00}%";
                        }, DispatcherPriority.Background);
                    }

                    await Task.Delay(120, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void StopPayloadReadProgressSimulation(bool success)
        {
            _payloadReadProgressCts?.Cancel();
            _payloadReadProgressCts?.Dispose();
            _payloadReadProgressCts = null;

            if (_payloadReadUsingActualProgress && !success)
            {
                return;
            }

            if (success)
            {
                OverallProgressBar.Value = 100;
                OverallPercentTextBlock.Text = "100.00%";
                return;
            }

            OverallProgressBar.Value = 0;
            OverallPercentTextBlock.Text = "0.00%";
        }

        private async Task CloseOpenedPayloadReadersAsync()
        {
            if (_payloadReader is not null && !ReferenceEquals(_payloadReader, _payloadSourceReader))
            {
                await _payloadReader.DisposeAsync().ConfigureAwait(false);
            }
            _payloadReader = null;

            if (_payloadSourceReader is not null)
            {
                await _payloadSourceReader.DisposeAsync().ConfigureAwait(false);
            }
            _payloadSourceReader = null;
            _payloadCtx = null;
            _payloadIsGenericZip = false;
        }

        protected override async void OnClosed(EventArgs e)
        {
            _payloadOperationCts?.Cancel();
            await CloseOpenedPayloadReadersAsync().ConfigureAwait(true);
            base.OnClosed(e);
        }

        private void PartitionsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void InputPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
        }

        private void OutputDirTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
        }

        private void InputPathTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (string.Equals(InputPathTextBox.Text, InputPlaceholderText, StringComparison.Ordinal))
            {
                InputPathTextBox.Clear();
            }
        }

        private void InputPathTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(InputPathTextBox.Text))
            {
                InputPathTextBox.Text = InputPlaceholderText;
            }
        }

        private void OutputDirTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (string.Equals(OutputDirTextBox.Text, OutputPlaceholderText, StringComparison.Ordinal))
            {
                OutputDirTextBox.Clear();
            }
        }

        private static Dictionary<string, string> ParsePayloadMetadataText(string text)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var sr = new StringReader(text ?? string.Empty);
            while (true)
            {
                var line = sr.ReadLine();
                if (line is null) break;
                line = line.Trim();
                if (line.Length == 0) continue;
                int idx = line.IndexOf('=');
                if (idx <= 0) continue;
                var key = line[..idx].Trim();
                var value = line[(idx + 1)..].Trim();
                if (key.Length == 0) continue;
                dict[key] = value;
            }
            return dict;
        }

        private sealed class PayloadVersionDetails
        {
            public string Model { get; init; } = "";
            public string ProductName { get; init; } = "";
            public string AndroidVersion { get; init; } = "";
            public string VersionName { get; init; } = "";
            public string SecurityPatch { get; init; } = "";

            public bool HasAnyValue =>
                !string.IsNullOrWhiteSpace(ProductName)
                || !string.IsNullOrWhiteSpace(AndroidVersion)
                || !string.IsNullOrWhiteSpace(VersionName)
                || !string.IsNullOrWhiteSpace(SecurityPatch);
        }

        private static PayloadVersionDetails ParsePayloadVersionDetails(string metadataText)
        {
            var metadata = ParsePayloadMetadataText(metadataText);
            string productName = GetPayloadMetadataValue(metadata, "product_name", "");

            return new PayloadVersionDetails
            {
                Model = ResolvePayloadModel(productName),
                ProductName = productName,
                AndroidVersion = GetPayloadMetadataValue(metadata, "android_version", ""),
                VersionName = GetPayloadMetadataValue(metadata, "version_name", ""),
                SecurityPatch = GetPayloadMetadataValue(metadata, "security_patch", "")
            };
        }

        private async Task<PayloadVersionDetails> ReadPayloadVersionDetailsFromInputAsync(
            string input,
            CancellationToken cancellationToken)
        {
            var (preparedInput, requestHeaders) =
                await PreparePayloadRemoteSourceAsync(input, cancellationToken).ConfigureAwait(true);

            await using var source =
                await PayloadProcessing.OpenSourceAsync(preparedInput, cancellationToken, requestHeaders)
                    .ConfigureAwait(true);

            string metadataText =
                await PayloadProcessing.ReadAndroidMetadataTextAsync(source, cancellationToken)
                    .ConfigureAwait(true);

            return ParsePayloadVersionDetails(metadataText);
        }

        private static string GetPayloadMetadataValue(Dictionary<string, string> metadata, string key, string fallback)
        {
            if (metadata.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) return v.Trim();
            return fallback;
        }

        private static string ResolvePayloadModel(string productNameOrCode)
        {
            if (string.IsNullOrWhiteSpace(productNameOrCode)) return "";
            var key = productNameOrCode.Trim();
            return DeviceCodeToModel.TryGetValue(key, out var model) ? model : "";
        }
    }
}
