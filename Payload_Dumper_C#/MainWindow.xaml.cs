using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Payload_Dumper_C_.Core;
using Payload_Dumper_C_.Models;

namespace Payload_Dumper_C_
{
    public partial class MainWindow : Window
    {
        private const string InputPlaceholderText = "支持本地/云端全量包_欧加散包_无需解压直接提取";
        private const string OutputPlaceholderText = "请选择解包后文件的保存路径文件夹...";

        private readonly ObservableCollection<PartitionItem> _partitions = new();
        private readonly DispatcherTimer _uiTimer;
        private readonly Stopwatch _stopwatch = new();
        private long _lastBytes;
        private TimeSpan _lastTime;

        private IRandomAccessReader? _sourceReader;
        private IRandomAccessReader? _payloadReader;
        private PayloadContext? _ctx;

        private bool _suppressSelectAllApply;
        private bool _bulkSelectionInProgress;
        private Paragraph? _pendingExtractLine;

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

        private static readonly IReadOnlyDictionary<string, string> DeviceCodeToModel =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PLZ110"] = "一加15T",
                ["PLQ110"] = "一加 ACE 6 / 一加 ACE 6T",
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

        public MainWindow()
        {
            InitializeComponent();
            PartitionsDataGrid.ItemsSource = _partitions;

            var view = CollectionViewSource.GetDefaultView(_partitions);
            view.Filter = FilterPartition;

            _uiTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => UpdateStatusUi(), Dispatcher);
            _uiTimer.Start();

            UpdateSelectAllState();
        }

        private static Dictionary<string, string> ParseMetadataText(string text)
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

        private static string GetMetadataValue(Dictionary<string, string> metadata, string key, string fallback)
        {
            if (metadata.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) return v.Trim();
            return fallback;
        }

        private static string ResolveModel(string productNameOrCode)
        {
            if (string.IsNullOrWhiteSpace(productNameOrCode)) return "";
            var key = productNameOrCode.Trim();
            return DeviceCodeToModel.TryGetValue(key, out var model) ? model : "";
        }

        private bool FilterPartition(object obj)
        {
            if (obj is not PartitionItem p) return false;
            var key = SearchTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(key)) return true;
            return p.Name.Contains(key, StringComparison.OrdinalIgnoreCase);
        }

        private void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            CollectionViewSource.GetDefaultView(_partitions).Refresh();
            UpdateSelectAllState();
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
            try
            {
                ReadInfoButton.IsEnabled = false;
                ExportButton.IsEnabled = false;
                SetStatus("读取中...");
                ClearPartitions();
                OverallProgressBar.Value = 0;
                OverallPercentTextBlock.Text = "0%";

                await CloseOpenedReadersAsync().ConfigureAwait(true);

                string input = InputPathTextBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(input) || input.Contains("请选择", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("请输入路径或 URL");

                var unzipProgress = new Progress<double>(p =>
                {
                    OverallProgressBar.Value = p;
                    OverallPercentTextBlock.Text = $"{p:0}%";
                });

                _sourceReader = await PayloadProcessing.OpenSourceAsync(input, CancellationToken.None).ConfigureAwait(true);
                var (payloadReader, payloadSize) = await PayloadProcessing.OpenPayloadReaderAsync(
                        _sourceReader,
                        CancellationToken.None,
                        log: AppendLog,
                        extractProgress: unzipProgress,
                        eagerExtract: false)
                    .ConfigureAwait(true);
                _payloadReader = payloadReader;

                _ctx = await PayloadProcessing.ReadManifestAsync(_payloadReader, CancellationToken.None).ConfigureAwait(true);

                var infos = PayloadProcessing.GetPartitions(_ctx);
                foreach (var p in infos)
                {
                    var item = new PartitionItem
                    {
                        Name = p.Name,
                        SizeBytes = p.SizeBytes,
                        SizeReadable = p.SizeReadable,
                        IsSelected = false
                    };
                    item.PropertyChanged += PartitionItem_PropertyChanged;
                    _partitions.Add(item);
                }

                AppendLog($"读取完成: 共 {infos.Count} 个分区");
                ExportButton.IsEnabled = true;
                SelectAllState = true;
                UpdateSelectAllState();

                if (OverallProgressBar.Value <= 0.1)
                {
                    await SimulateOverallProgressOnceAsync().ConfigureAwait(true);
                }

                string outDir = OutputDirTextBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(outDir) || outDir.Contains("请选择", StringComparison.OrdinalIgnoreCase))
                {
                    outDir = Path.Combine(Environment.CurrentDirectory, "output");
                    OutputDirTextBox.Text = outDir;
                }

                try
                {
                    SetStatus("提取metadata中...");
                    if (_sourceReader is null) throw new InvalidOperationException("未打开源文件");
                    var (_, text) = await PayloadProcessing.ExtractAndroidMetadataAsync(_sourceReader, outDir, CancellationToken.None).ConfigureAwait(true);

                    var m = ParseMetadataText(text);

                    var productName = GetMetadataValue(m, "product_name", "");
                    var androidVersion = GetMetadataValue(m, "android_version", "");
                    var versionName = GetMetadataValue(m, "version_name", "");
                    var securityPatch = GetMetadataValue(m, "security_patch", "");

                    var model = ResolveModel(productName);

                    AppendLog("");
                    AppendLog($"手机机型：{model}");
                    AppendLog($"设备代号：{productName}");
                    AppendLog($"安卓版本：{androidVersion}");
                    AppendLog($"版本信息：{versionName}");
                    AppendLog($"安全补丁：{securityPatch}");
                }
                catch
                {
                }

                SetStatus("准备就绪");
            }
            catch (Exception ex)
            {
                AppendLog(ex.ToString());
                SetStatus("读取失败");
            }
            finally
            {
                ReadInfoButton.IsEnabled = true;
                ExportButton.IsEnabled = _ctx is not null;
            }
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_ctx is null) throw new InvalidOperationException("请先点击“读取信息”");

                string outDir = OutputDirTextBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(outDir) || outDir.Contains("请选择", StringComparison.OrdinalIgnoreCase))
                {
                    outDir = Path.Combine(Environment.CurrentDirectory, "output");
                    OutputDirTextBox.Text = outDir;
                }

                PartitionsDataGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
                PartitionsDataGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);

                var selected = _partitions.Where(p => p.IsSelected).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (selected.Count == 0)
                {
                    foreach (var item in PartitionsDataGrid.SelectedItems)
                    {
                        if (item is PartitionItem p) selected.Add(p.Name);
                    }
                }
                if (selected.Count == 0) throw new InvalidOperationException("请选择要导出的分区");

                ReadInfoButton.IsEnabled = false;
                ExportButton.IsEnabled = false;

                OverallProgressBar.Value = 0;
                OverallPercentTextBlock.Text = "0%";

                if (_ctx.Reader is ZipPayloadLazyReader lazy && !lazy.IsExtracted)
                {
                    SetStatus("解压payload.bin中...");
                    var unzipProgress = new Progress<double>(p =>
                    {
                        OverallProgressBar.Value = p;
                        OverallPercentTextBlock.Text = $"{p:0}%";
                    });

                    await lazy.EnsureExtractedAsync(unzipProgress, CancellationToken.None).ConfigureAwait(true);
                    OverallProgressBar.Value = 0;
                    OverallPercentTextBlock.Text = "0%";
                }

                _stopwatch.Restart();
                _lastBytes = _ctx.Reader.BytesRead;
                _lastTime = TimeSpan.Zero;

                SetStatus("导出中...");

                var progress = new Progress<(long DoneOps, long TotalOps)>(p =>
                {
                    if (p.TotalOps <= 0) return;
                    double percent = (double)p.DoneOps * 100d / p.TotalOps;
                    OverallProgressBar.Value = percent;
                    OverallPercentTextBlock.Text = $"{percent:0}%";
                });

                await PayloadProcessing.ExtractPartitionsAsync(
                        _ctx,
                        selected,
                        outDir,
                        Environment.ProcessorCount,
                        log: AppendExportLog,
                        progress: progress,
                        cancellationToken: CancellationToken.None)
                    .ConfigureAwait(true);

                AppendLog("[Done]所有选中分区提取完成..");
                SetStatus("完成");
            }
            catch (Exception ex)
            {
                AppendLog(ex.ToString());
                SetStatus("导出失败");
            }
            finally
            {
                _stopwatch.Stop();
                ReadInfoButton.IsEnabled = true;
                ExportButton.IsEnabled = _ctx is not null;
            }
        }

        private async Task SimulateOverallProgressOnceAsync()
        {
            const int totalMs = 360;
            const int steps = 18;

            for (int i = 0; i <= steps; i++)
            {
                double p = i * 100d / steps;
                OverallProgressBar.Value = p;
                OverallPercentTextBlock.Text = $"{p:0}%";
                await Task.Delay(totalMs / steps).ConfigureAwait(true);
            }

            await Task.Delay(60).ConfigureAwait(true);
            OverallProgressBar.Value = 0;
            OverallPercentTextBlock.Text = "0%";
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            _pendingExtractLine = null;
            LogTextBox.Document.Blocks.Clear();
        }

        private void ClearPartitions()
        {
            foreach (var p in _partitions)
            {
                p.PropertyChanged -= PartitionItem_PropertyChanged;
            }
            _partitions.Clear();
            UpdateSelectAllState();
        }

        private static void OnSelectAllStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var w = (MainWindow)d;
            if (w._suppressSelectAllApply) return;
            if (e.NewValue is not bool b) return;
            w.ApplySelectAllToView(b);
        }

        private void ApplySelectAllToView(bool selected)
        {
            _suppressSelectAllApply = true;
            _bulkSelectionInProgress = true;
            try
            {
                var view = CollectionViewSource.GetDefaultView(_partitions);
                foreach (var obj in view)
                {
                    if (obj is PartitionItem p) p.IsSelected = selected;
                }
            }
            finally
            {
                _bulkSelectionInProgress = false;
                _suppressSelectAllApply = false;
                UpdateSelectAllState();
            }
        }

        private void PartitionItem_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_bulkSelectionInProgress) return;
            if (!string.Equals(e.PropertyName, nameof(PartitionItem.IsSelected), StringComparison.Ordinal)) return;
            UpdateSelectAllState();
        }

        private void UpdateSelectAllState()
        {
            var view = CollectionViewSource.GetDefaultView(_partitions);
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

            _suppressSelectAllApply = true;
            try
            {
                if (SelectAllState != state) SelectAllState = state;
            }
            finally
            {
                _suppressSelectAllApply = false;
            }
        }

        private void AppendLog(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendLog(message));
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

        private void AppendExportLog(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendExportLog(message));
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
                AppendExtractStart($"{name}.img...");
                return;
            }

            if (msg.StartsWith(EndPrefix, StringComparison.Ordinal))
            {
                AppendExtractOk();
                return;
            }

            AppendLog(message ?? string.Empty);
        }

        private void AppendExtractStart(string text)
        {
            _pendingExtractLine = new Paragraph { Margin = new Thickness(0) };
            _pendingExtractLine.Inlines.Add(new Run($"[提取]{text}") { Foreground = System.Windows.Media.Brushes.Black });
            LogTextBox.Document.Blocks.Add(_pendingExtractLine);
            LogTextBox.ScrollToEnd();
        }

        private void AppendExtractOk()
        {
            var p = _pendingExtractLine;
            if (p is null)
            {
                AppendLog("OK");
                return;
            }

            p.Inlines.Add(new Run(" ") { Foreground = System.Windows.Media.Brushes.Black });
            p.Inlines.Add(new Run("OK") { Foreground = System.Windows.Media.Brushes.Green });
            _pendingExtractLine = null;
            LogTextBox.ScrollToEnd();
        }

        private void SetStatus(string status)
        {
            StatusTextBlock.Text = status;
        }

        private void UpdateStatusUi()
        {
            if (!_stopwatch.IsRunning)
            {
                ElapsedTextBlock.Text = _stopwatch.Elapsed.ToString(@"hh\:mm\:ss");
                SpeedTextBlock.Text = "读取: 0 B/s";
                return;
            }

            ElapsedTextBlock.Text = _stopwatch.Elapsed.ToString(@"hh\:mm\:ss");

            long nowBytes = _ctx?.Reader.BytesRead ?? 0;
            var nowTime = _stopwatch.Elapsed;

            long deltaBytes = nowBytes - _lastBytes;
            double deltaSeconds = (nowTime - _lastTime).TotalSeconds;
            if (deltaSeconds <= 0) deltaSeconds = 0.5;

            double bps = deltaBytes / deltaSeconds;
            SpeedTextBlock.Text = $"读取: {ToReadableSpeed(bps)}/s";

            _lastBytes = nowBytes;
            _lastTime = nowTime;
        }

        private static string ToReadableSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond >= 1024d * 1024d * 1024d) return $"{bytesPerSecond / (1024d * 1024d * 1024d):0.0} GB";
            if (bytesPerSecond >= 1024d * 1024d) return $"{bytesPerSecond / (1024d * 1024d):0.0} MB";
            if (bytesPerSecond >= 1024d) return $"{bytesPerSecond / 1024d:0.0} KB";
            return $"{bytesPerSecond:0} B";
        }

        private async Task CloseOpenedReadersAsync()
        {
            if (_payloadReader is not null && !ReferenceEquals(_payloadReader, _sourceReader))
            {
                await _payloadReader.DisposeAsync().ConfigureAwait(false);
            }
            _payloadReader = null;

            if (_sourceReader is not null)
            {
                await _sourceReader.DisposeAsync().ConfigureAwait(false);
            }
            _sourceReader = null;
            _ctx = null;
        }

        protected override async void OnClosed(EventArgs e)
        {
            await CloseOpenedReadersAsync().ConfigureAwait(true);
            base.OnClosed(e);
        }

        private void PartitionsDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {

        }

        private void InputPathTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {

        }

        private void OutputDirTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {

        }

        private void InputPathTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (string.Equals(InputPathTextBox.Text, InputPlaceholderText, StringComparison.Ordinal))
            {
                InputPathTextBox.Clear();
            }
        }

        private void OutputDirTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (string.Equals(OutputDirTextBox.Text, OutputPlaceholderText, StringComparison.Ordinal))
            {
                OutputDirTextBox.Clear();
            }
        }
    }
}
