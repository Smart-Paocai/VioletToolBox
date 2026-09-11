using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Data;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Forms;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using System.ComponentModel;
using Microsoft.Win32;
using System.IO.Compression;
using IOPath = System.IO.Path;
using IOFile = System.IO.File;
using SmartTool;
using test1;
using OPFlashTool.Services;

namespace WpfApp1
{
    public partial class MainWindow
    {
        private DowngradeTool _downgradeTool;
        private int? _selectedColorOSPackageIndex;
        private bool _colorOSQueryOnlyInProgress;
        private bool _suppressColorOSPackageSelectionParsing;
        private CancellationTokenSource? _colorOSVersionInfoCts;
        private DispatcherTimer? _colorOSVersionInfoProgressTimer;
        private readonly Stopwatch _colorOSVersionInfoStopwatch = new();
        private static readonly Regex ColorOSUrlRegex = new Regex(@"https?://\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private void InitializeColorOSAssistant()
        {
            _downgradeTool = new DowngradeTool(LogColorOSMessage);
            _downgradeTool.OnPackagesFound = OnPackagesFound;
            _downgradeTool.OnDownloadProgress = OnDownloadProgress;
        }

        private void ColorOSAssistantButton_Click(object sender, RoutedEventArgs e)
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
            if (payloadView != null) payloadView.Visibility = Visibility.Collapsed;
            if (romDownloadView != null) romDownloadView.Visibility = Visibility.Collapsed;
            if (edlFlashView != null) edlFlashView.Visibility = Visibility.Collapsed;
            if (colorOSAssistantView != null) colorOSAssistantView.Visibility = Visibility.Visible;
            if (backupAssistantView != null) backupAssistantView.Visibility = Visibility.Collapsed;
            if (violetDownloadView != null) violetDownloadView.Visibility = Visibility.Collapsed;

            UpdateButtonStates("ColorOSAssistant");
            currentView = "ColorOSAssistant";
            
            if (_downgradeTool == null)
            {
                InitializeColorOSAssistant();
            }
            EnsureColorOSDefaultDownloadDir();
        }

        private void EnsureColorOSDefaultDownloadDir()
        {
            var txtDownloadDir = this.FindName("TxtDownloadDir") as System.Windows.Controls.TextBox;
            if (txtDownloadDir == null || !string.IsNullOrWhiteSpace(txtDownloadDir.Text))
                return;

            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktopPath))
            {
                desktopPath = IOPath.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Desktop");
            }

            txtDownloadDir.Text = desktopPath;
        }

        private void LogColorOSMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                var logBox = this.FindName("LogBox") as System.Windows.Controls.RichTextBox;
                if (logBox != null)
                {
                    logBox.Document.PagePadding = new Thickness(0);
                    foreach (var line in NormalizeColorOSLogLines(message))
                    {
                        logBox.Document.Blocks.Add(CreateColorOSLogParagraph(line));
                    }
                    logBox.ScrollToEnd();
                }
            });
        }

        private static Paragraph CreateColorOSLogParagraph(string line)
        {
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0, 0.5, 0, 0.5),
                LineHeight = 19
            };

            paragraph.Inlines.Add(new Run($"[{DateTime.Now:HH:mm:ss}] ")
            {
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184))
            });

            string content = line.TrimStart();
            string? semanticTag = null;
            System.Windows.Media.Color semanticColor = System.Windows.Media.Color.FromRgb(124, 58, 237);

            if (content.StartsWith("✓", StringComparison.Ordinal))
            {
                semanticTag = "[成功] ";
                semanticColor = System.Windows.Media.Color.FromRgb(22, 163, 74);
                content = content.Substring(1).TrimStart();
            }
            else if (content.StartsWith("✗", StringComparison.Ordinal)
                     || content.Contains("失败", StringComparison.Ordinal)
                     || content.Contains("错误", StringComparison.Ordinal))
            {
                semanticTag = "[错误] ";
                semanticColor = System.Windows.Media.Color.FromRgb(220, 38, 38);
                content = content.TrimStart('✗', ' ');
            }
            else if (content.StartsWith("⚠", StringComparison.Ordinal))
            {
                semanticTag = "[警告] ";
                semanticColor = System.Windows.Media.Color.FromRgb(217, 119, 6);
                content = content.Substring(1).TrimStart();
            }
            else if (content.StartsWith("[提示]", StringComparison.Ordinal))
            {
                semanticTag = "[提示] ";
                semanticColor = System.Windows.Media.Color.FromRgb(37, 99, 235);
                content = content.Substring("[提示]".Length).TrimStart();
            }
            else if (content.StartsWith("步骤", StringComparison.Ordinal))
            {
                semanticTag = "[步骤] ";
            }
            else if (content.StartsWith("启动", StringComparison.Ordinal)
                     || content.StartsWith("正在", StringComparison.Ordinal))
            {
                semanticTag = "[操作] ";
            }

            if (semanticTag != null)
            {
                paragraph.Inlines.Add(new Run(semanticTag)
                {
                    Foreground = new SolidColorBrush(semanticColor),
                    FontWeight = FontWeights.SemiBold
                });
            }

            AppendColorOSLogText(paragraph, content);
            return paragraph;
        }

        private static void AppendColorOSLogText(Paragraph paragraph, string line)
        {
            int currentIndex = 0;
            foreach (Match match in ColorOSUrlRegex.Matches(line))
            {
                if (match.Index > currentIndex)
                {
                    paragraph.Inlines.Add(CreateColorOSBodyRun(line.Substring(currentIndex, match.Index - currentIndex)));
                }

                paragraph.Inlines.Add(new Run(match.Value)
                {
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(184, 118, 221)),
                    FontWeight = FontWeights.SemiBold
                });

                currentIndex = match.Index + match.Length;
            }

            if (currentIndex < line.Length)
            {
                paragraph.Inlines.Add(CreateColorOSBodyRun(line.Substring(currentIndex)));
            }
        }

        private static Run CreateColorOSBodyRun(string text)
        {
            return new Run(text)
            {
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85))
            };
        }

        private static IEnumerable<string> NormalizeColorOSLogLines(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                yield break;

            foreach (var line in message.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                yield return line.TrimEnd();
            }
        }

        private void OnPackagesFound(List<DowngradeTool.DowngradePackageInfo> packages)
        {
            Dispatcher.Invoke(() =>
            {
                var packagesGrid = this.FindName("PackagesGrid") as DataGrid;
                if (packagesGrid != null)
                {
                    _suppressColorOSPackageSelectionParsing = true;
                    try
                    {
                        packagesGrid.ItemsSource = packages;
                        DowngradeTool.DowngradePackageInfo? selectedPackage = null;
                        if (_selectedColorOSPackageIndex.HasValue)
                        {
                            selectedPackage = packages?
                                .FirstOrDefault(package => package.Index == _selectedColorOSPackageIndex.Value);
                        }
                        else if (!_colorOSQueryOnlyInProgress)
                        {
                            selectedPackage = packages?.FirstOrDefault();
                        }

                        if (selectedPackage != null)
                        {
                            _selectedColorOSPackageIndex = selectedPackage.Index;
                            packagesGrid.SelectedItem = selectedPackage;
                            packagesGrid.ScrollIntoView(selectedPackage);
                        }
                        else
                        {
                            packagesGrid.SelectedItem = null;
                            packagesGrid.UnselectAll();
                        }
                    }
                    finally
                    {
                        _suppressColorOSPackageSelectionParsing = false;
                    }
                }
            });
        }

        private async void PackagesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid dataGrid
                && dataGrid.SelectedItem is DowngradeTool.DowngradePackageInfo selectedPackage)
            {
                _selectedColorOSPackageIndex = selectedPackage.Index;
                if (!_suppressColorOSPackageSelectionParsing)
                {
                    await ReadSelectedColorOSPackageVersionInfoAsync(selectedPackage);
                }
            }
        }

        private void ColorOSPackageCheckBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Controls.CheckBox checkBox
                || checkBox.DataContext is not DowngradeTool.DowngradePackageInfo package)
            {
                return;
            }

            e.Handled = true;
            var packagesGrid = this.FindName("PackagesGrid") as DataGrid;
            if (packagesGrid != null)
            {
                packagesGrid.SelectedItem = package;
                packagesGrid.ScrollIntoView(package);
                _selectedColorOSPackageIndex = package.Index;
            }
        }

        private async Task ReadSelectedColorOSPackageVersionInfoAsync(
            DowngradeTool.DowngradePackageInfo package)
        {
            _colorOSVersionInfoCts?.Cancel();
            var operationCts = new CancellationTokenSource();
            _colorOSVersionInfoCts = operationCts;
            bool parsingSucceeded = false;
            string finalProgressText = "云端版本解析失败";

            try
            {
                if (string.IsNullOrWhiteSpace(package.DownloadUrl))
                {
                    LogColorOSMessage("✗ 当前降级版本没有可解析的云端下载链接。");
                    finalProgressText = "没有可解析的云端链接";
                    return;
                }

                StartColorOSVersionInfoProgress();
                LogColorOSMessage($"正在解析 {package.ColorOSVersion} 的云端版本信息...");
                PayloadVersionDetails versionDetails =
                    await ReadPayloadVersionDetailsFromInputAsync(package.DownloadUrl, operationCts.Token);

                if (!versionDetails.HasAnyValue)
                {
                    LogColorOSMessage("⚠ 当前云端降级包未解析到版本信息。");
                    finalProgressText = "未解析到版本信息";
                    return;
                }

                static string DisplayValue(string value) =>
                    string.IsNullOrWhiteSpace(value) ? "--" : value.Trim();

                LogColorOSMessage("✓ 云端版本信息解析完成");
                if (!string.IsNullOrWhiteSpace(versionDetails.Model))
                {
                    LogColorOSMessage($"手机机型：{versionDetails.Model.Trim()}");
                }
                LogColorOSMessage($"设备代号：{DisplayValue(versionDetails.ProductName)}");
                LogColorOSMessage($"安卓版本：{DisplayValue(versionDetails.AndroidVersion)}");
                LogColorOSMessage($"版本信息：{DisplayValue(versionDetails.VersionName)}");
                LogColorOSMessage($"安全补丁：{DisplayValue(versionDetails.SecurityPatch)}");
                parsingSucceeded = true;
                finalProgressText = "云端版本信息解析完成";
            }
            catch (OperationCanceledException) when (operationCts.IsCancellationRequested)
            {
                finalProgressText = "云端版本解析已取消";
            }
            catch (Exception ex)
            {
                LogColorOSMessage($"✗ 云端版本信息解析失败: {ex.Message}");
                finalProgressText = "云端版本信息解析失败";
            }
            finally
            {
                if (ReferenceEquals(_colorOSVersionInfoCts, operationCts))
                {
                    StopColorOSVersionInfoProgress(parsingSucceeded, finalProgressText);
                    _colorOSVersionInfoCts = null;
                }
                operationCts.Dispose();
            }
        }

        private void StartColorOSVersionInfoProgress()
        {
            var progressBar = this.FindName("DownloadProgress") as System.Windows.Controls.ProgressBar;
            var progressInfo = this.FindName("TxtProgressInfo") as TextBlock;

            _colorOSVersionInfoProgressTimer ??= new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _colorOSVersionInfoProgressTimer.Tick -= ColorOSVersionInfoProgressTimer_Tick;
            _colorOSVersionInfoProgressTimer.Tick += ColorOSVersionInfoProgressTimer_Tick;

            _colorOSVersionInfoStopwatch.Restart();
            if (progressBar != null)
            {
                progressBar.Value = 2;
                progressBar.Tag = "正在解析云端版本  |  Time:0s";
            }
            if (progressInfo != null)
            {
                progressInfo.Text = "正在解析云端版本";
            }
            _colorOSVersionInfoProgressTimer.Start();
        }

        private void ColorOSVersionInfoProgressTimer_Tick(object? sender, EventArgs e)
        {
            var progressBar = this.FindName("DownloadProgress") as System.Windows.Controls.ProgressBar;
            if (progressBar == null)
                return;

            double increment = progressBar.Value < 60
                ? 4
                : progressBar.Value < 84
                    ? 1.5
                    : 0.4;

            progressBar.Value = Math.Min(92, progressBar.Value + increment);
            progressBar.Tag =
                $"正在解析云端版本  |  Time:{(int)_colorOSVersionInfoStopwatch.Elapsed.TotalSeconds}s";
        }

        private void StopColorOSVersionInfoProgress(bool succeeded, string statusText)
        {
            _colorOSVersionInfoProgressTimer?.Stop();
            _colorOSVersionInfoStopwatch.Stop();

            var progressBar = this.FindName("DownloadProgress") as System.Windows.Controls.ProgressBar;
            var progressInfo = this.FindName("TxtProgressInfo") as TextBlock;
            if (progressBar != null)
            {
                progressBar.Value = succeeded ? 100 : 0;
                progressBar.Tag = statusText;
            }
            if (progressInfo != null)
            {
                progressInfo.Text = statusText;
            }
        }

        private void CopyColorOSDownloadLink_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not System.Windows.Controls.Button button
                || button.DataContext is not DowngradeTool.DowngradePackageInfo package
                || string.IsNullOrWhiteSpace(package.DownloadUrl))
            {
                LogColorOSMessage("✗ 当前降级版本没有可复制的下载链接。");
                return;
            }

            try
            {
                System.Windows.Clipboard.SetText(package.DownloadUrl);
                LogColorOSMessage($"✓ 已复制 {package.ColorOSVersion} 的下载链接。");
            }
            catch (Exception ex)
            {
                LogColorOSMessage($"✗ 复制下载链接失败: {ex.Message}");
            }
        }

        private void OnDownloadProgress(double pct, double speed, string text)
        {
            Dispatcher.Invoke(() =>
            {
                var downloadProgress = this.FindName("DownloadProgress") as System.Windows.Controls.ProgressBar;
                var txtProgressInfo = this.FindName("TxtProgressInfo") as TextBlock;
                
                if (downloadProgress != null)
                {
                    downloadProgress.Value = pct;
                    downloadProgress.Tag = text;
                }
                if (txtProgressInfo != null)
                {
                    txtProgressInfo.Text = text;
                }
            });
        }

        private void BtnBrowseDir_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var txtDownloadDir = this.FindName("TxtDownloadDir") as System.Windows.Controls.TextBox;
                if (txtDownloadDir != null)
                {
                    txtDownloadDir.Text = dialog.SelectedPath;
                }
            }
        }

        private void BtnBrowseFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Filter = "ZIP/OZIP Files (*.zip;*.ozip)|*.zip;*.ozip|All Files (*.*)|*.*";
            if (dialog.ShowDialog() == true)
            {
                var txtPushFile = this.FindName("TxtPushFile") as System.Windows.Controls.TextBox;
                if (txtPushFile != null)
                {
                    txtPushFile.Text = dialog.FileName;
                }
            }
        }

        private void BtnBrowseAssistantApk_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择降级助手 APK",
                Filter = "Android APK Files (*.apk)|*.apk|All Files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                var txtAssistantApk =
                    this.FindName("TxtAssistantApk") as System.Windows.Controls.TextBox;
                if (txtAssistantApk != null)
                {
                    txtAssistantApk.Text = dialog.FileName;
                }
            }
        }

        private void BtnDownloadApk_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "https://wwauy.lanzouv.com/i7s3z3l8dmve",
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                LogColorOSMessage($"打开下载链接失败: {ex.Message}");
            }
        }

        private async void BtnQueryOnly_Click(object sender, RoutedEventArgs e)
        {
            await RunDowngradeToolAsync(queryOnly: true);
        }

        private async void StartBtn_Click(object sender, RoutedEventArgs e)
        {
            await RunDowngradeToolAsync(queryOnly: false);
        }

        private async Task RunDowngradeToolAsync(bool queryOnly)
        {
            var pendingVersionInfoCts = _colorOSVersionInfoCts;
            _colorOSVersionInfoCts = null;
            pendingVersionInfoCts?.Cancel();
            _colorOSVersionInfoProgressTimer?.Stop();
            _colorOSVersionInfoStopwatch.Stop();
            _colorOSQueryOnlyInProgress = queryOnly;
            var startBtn = this.FindName("StartBtn") as System.Windows.Controls.Button;
            var btnQueryOnly = this.FindName("BtnQueryOnly") as System.Windows.Controls.Button;
            var logBox = this.FindName("LogBox") as System.Windows.Controls.RichTextBox;
            var packagesGrid = this.FindName("PackagesGrid") as DataGrid;
            var downloadProgress = this.FindName("DownloadProgress") as System.Windows.Controls.ProgressBar;
            var txtProgressInfo = this.FindName("TxtProgressInfo") as TextBlock;
            var txtDownloadDir = this.FindName("TxtDownloadDir") as System.Windows.Controls.TextBox;
            var txtPushFile = this.FindName("TxtPushFile") as System.Windows.Controls.TextBox;
            var txtAssistantApk = this.FindName("TxtAssistantApk") as System.Windows.Controls.TextBox;

            if (startBtn != null) startBtn.IsEnabled = false;
            if (btnQueryOnly != null) btnQueryOnly.IsEnabled = false;
            if (logBox != null) logBox.Document.Blocks.Clear();
            if (packagesGrid != null) packagesGrid.ItemsSource = null;
            if (downloadProgress != null)
            {
                downloadProgress.Value = 0;
                downloadProgress.Tag = "准备就绪";
            }
            if (txtProgressInfo != null) txtProgressInfo.Text = "准备就绪";
            
            LogColorOSMessage(queryOnly ? "启动查询流程..." : "启动降级流程...");

            string selectedAssistantApk = txtAssistantApk?.Text.Trim() ?? "";
            bool installSelectedAssistantApk =
                !string.IsNullOrWhiteSpace(selectedAssistantApk);
            var options = new DowngradeTool.DowngradeOptions
            {
                Port = 8500,  // 默认端口
                PkgIndex = _selectedColorOSPackageIndex ?? 0,
                DownloadDir = txtDownloadDir?.Text.Trim() ?? "",
                QueryOnly = queryOnly,
                PushOnlyFile = txtPushFile?.Text.Trim() ?? "",
                ApkPath = installSelectedAssistantApk
                    ? selectedAssistantApk
                    : IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "OTA_113.apk"),
                InstallApkBeforeQuery = installSelectedAssistantApk
            };
            
            await Task.Run(async () => 
            {
                try
                {
                    await _downgradeTool.RunAsync(options);
                }
                catch (Exception ex)
                {
                    LogColorOSMessage($"发生错误: {ex.Message}\n{ex.StackTrace}");
                }
            });
            
            Dispatcher.Invoke(() => 
            {
                if (startBtn != null) startBtn.IsEnabled = true;
                if (btnQueryOnly != null) btnQueryOnly.IsEnabled = true;
            });
            _colorOSQueryOnlyInProgress = false;
        }
    }
}
