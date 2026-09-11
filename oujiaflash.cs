using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SharpVectors.Converters;
using SmartTool;
using test1;
using OPFlashTool.Services;
using IOPath = System.IO.Path;
using IOFile = System.IO.File;
using MediaColor = System.Windows.Media.Color;
using WpfCursors = System.Windows.Input.Cursors;

namespace WpfApp1
{
    public partial class MainWindow
    {
        private bool _isOugaFlashTaskRunning = false;
        private bool _ougaFlashStopRequested = false;
        private bool _trackOugaFlashPartitionResults = false;
        private int _ougaFlashPartitionSuccessCount = 0;
        private int _ougaFlashPartitionFailureCount = 0;
        private readonly List<string> _ougaFlashFailedPartitionTargets = new();
        private OujiaFlashSessionContext? _activeOujiaFlashSession;
        private static readonly HashSet<string> OujiaLogicalPartitionNames = new(
            new[]
            {
                "system", "odm", "vendor", "product", "system_ext", "system_dlkm",
                "vendor_dlkm", "odm_dlkm", "my_bigball", "my_carrier", "my_company",
                "my_engineering", "my_heytap", "my_manifest", "my_preload",
                "my_product", "my_region", "my_stock"
            },
            StringComparer.OrdinalIgnoreCase);

        private void FixSuperCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (FixSuperCheckBox.IsChecked == true)
            {
                PureFBDCheckBox.IsChecked = false;
                FlashABCheckBox.IsChecked = false;
            }
        }
        
        // 仅FBD复选框选中事件处理器
        private void PureFBDCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (PureFBDCheckBox.IsChecked == true)
            {
                FixSuperCheckBox.IsChecked = false;
                FlashABCheckBox.IsChecked = false;
            }
        }

        // AB通刷复选框选中事件处理器
        private void FlashABCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (FlashABCheckBox.IsChecked == true)
            {
                FixSuperCheckBox.IsChecked = false;
                PureFBDCheckBox.IsChecked = false;
            }
        }

        // 处理文本链接点击事件

        private async Task LoadExtractedPartitionsToDataGrid(string extractPath, string[] partitionNames)
        {
            try
            {
                var partitionList = new List<PartitionInfo>();
                
                AppendOugaFlashParagraphLog("开始解析提取的分区文件...");
                OugaFlashLogTextBox.ScrollToEnd();
                
                foreach (string partitionName in partitionNames)
                {
                    // 查找对应的分区文件
                    string[] possibleExtensions = { ".img", ".bin" };
                    string foundFilePath = null;
                    
                    foreach (string extension in possibleExtensions)
                    {
                        string filePath = Path.Combine(extractPath, partitionName + extension);
                        if (File.Exists(filePath))
                        {
                            foundFilePath = filePath;
                            break;
                        }
                    }
                    
                    if (foundFilePath != null)
                    {
                        // 获取文件大小
                        var fileInfo = new FileInfo(foundFilePath);
                        string fileSize = FormatFileSize(fileInfo.Length);
                        
                        // 创建分区信息对象
                        var partitionInfo = new PartitionInfo
                        {
                            IsSelected = false,
                            PartitionName = partitionName,
                            PartitionSize = fileSize,
                            FilePath = foundFilePath
                        };
                        
                        partitionList.Add(partitionInfo);
                        
                        AppendOugaFlashParagraphLog($"找到分区文件: {partitionName} ({fileSize})");
                    }
                    else
                    {
                        AppendOugaFlashParagraphLog($"警告: 未找到分区文件: {partitionName}", "Yellow");
                    }
                }
                
                // 更新DataGrid
                Dispatcher.Invoke(() =>
                {
                    OugaPartitionTableDataGrid.ItemsSource = new ObservableCollection<PartitionInfo>(partitionList);
                    
                    // 自动全选所有分区
                    if (OugaPartitionTableDataGrid.ItemsSource is ObservableCollection<PartitionInfo> partitions)
                    {
                        foreach (var partition in partitions)
                        {
                            partition.IsSelected = true;
                        }
                        
                        // 更新全选复选框状态
                        var selectAllCheckBox = this.FindName("SelectAllCheckBox") as System.Windows.Controls.CheckBox;
                        if (selectAllCheckBox != null)
                        {
                            selectAllCheckBox.IsChecked = true;
                        }
                        
                        OugaPartitionTableDataGrid.Items.Refresh();
                    }
                });
                
                AppendOugaFlashParagraphLog($"分区文件解析完成，共找到 {partitionList.Count} 个分区文件", "Green");
                OugaFlashLogTextBox.ScrollToEnd();
            }
            catch (Exception ex)
            {
                AppendOugaFlashParagraphLog($"解析分区文件时发生错误: {ex.Message}", "Red");
                OugaFlashLogTextBox.ScrollToEnd();
            }
        }
        
        // 格式化文件大小的辅助方法

        private void OugaPartitionTableDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void OugaPartitionSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not System.Windows.Controls.TextBox searchTextBox ||
                OugaPartitionTableDataGrid?.ItemsSource == null)
            {
                return;
            }

            string keyword = searchTextBox.Text.Trim();
            System.ComponentModel.ICollectionView view =
                System.Windows.Data.CollectionViewSource.GetDefaultView(
                    OugaPartitionTableDataGrid.ItemsSource);
            view.Filter = string.IsNullOrWhiteSpace(keyword)
                ? null
                : item => item is PartitionInfo partition &&
                          partition.PartitionName.Contains(
                              keyword,
                              StringComparison.OrdinalIgnoreCase);
            view.Refresh();
        }

        private void OugaPartitionSearchTextBox_KeyDown(
            object sender,
            System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape &&
                sender is System.Windows.Controls.TextBox searchTextBox &&
                !string.IsNullOrEmpty(searchTextBox.Text))
            {
                searchTextBox.Clear();
                e.Handled = true;
            }
        }

        private void TextBox_TextChanged_2(object sender, TextChangedEventArgs e)
        {

        }

        private void TextBox_TextChanged_3(object sender, TextChangedEventArgs e)
        {

        }

        private void TextBox_TextChanged_4(object sender, TextChangedEventArgs e)
        {

        }

        private void TextBox_TextChanged_5(object sender, TextChangedEventArgs e)
        {

        }

        private void ProgressButton_Checked()
        {

        }


        private void BinUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void BinUrlTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.TextBox? textBox = sender as System.Windows.Controls.TextBox;
            if (textBox != null && textBox.Text == textBox.Tag?.ToString())
            {
                textBox.Text = "";
                textBox.Foreground = System.Windows.Media.Brushes.Black;
            }
        }

        private void BinUrlTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.TextBox? textBox = sender as System.Windows.Controls.TextBox;
            if (textBox != null && string.IsNullOrWhiteSpace(textBox.Text))
            {
                textBox.Text = textBox.Tag?.ToString();
                textBox.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }

        private void PayloadPartitionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 检查是否选择了云解包方案
            if (sender is System.Windows.Controls.ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string selectedContent = selectedItem.Content?.ToString() ?? "";
                if (selectedContent == "云解包方案-从云端提取线刷文件")
                {
                    // 当选择云解包方案时，显示提示信息
                    AppendOugaFlashParagraphLog("已选择云解包方案，点击提取分区按钮开始云端解包", "Blue");
                    OugaFlashLogTextBox.ScrollToEnd();
                }
            }
        }

        private string? ResolveOugaPayloadSourceInput()
        {
            string payloadPath = (PayloadFilePathTextBox.Text ?? string.Empty).Trim().Trim('`');
            if (!string.IsNullOrEmpty(payloadPath) &&
                !string.Equals(payloadPath, "请选择Payload.bin文件或全量包Zip文件...", StringComparison.Ordinal))
            {
                return payloadPath;
            }

            string binUrl = (BinUrlTextBox.Text ?? string.Empty).Trim().Trim('`');
            if (!string.IsNullOrEmpty(binUrl) &&
                !string.Equals(binUrl, "全量包链接 or Payload.bin路径...", StringComparison.Ordinal) &&
                !string.Equals(binUrl, "bin_URL", StringComparison.OrdinalIgnoreCase))
            {
                return binUrl;
            }

            return null;
        }

        private async Task<List<string>> GetPayloadPartitionNamesAsync(string sourceInput)
        {
            Payload_Dumper_C_.Core.IRandomAccessReader? payloadSourceReader = null;
            Payload_Dumper_C_.Core.IRandomAccessReader? payloadReader = null;

            try
            {
                payloadSourceReader = await Payload_Dumper_C_.Core.PayloadProcessing
                    .OpenSourceAsync(sourceInput, System.Threading.CancellationToken.None)
                    .ConfigureAwait(true);

                var openResult = await Payload_Dumper_C_.Core.PayloadProcessing.OpenPayloadReaderAsync(
                        payloadSourceReader,
                        System.Threading.CancellationToken.None,
                        log: message => AppendOugaFlashParagraphLog(message),
                        extractProgress: null,
                        eagerExtract: false)
                    .ConfigureAwait(true);

                payloadReader = openResult.PayloadReader;

                var payloadCtx = await Payload_Dumper_C_.Core.PayloadProcessing
                    .ReadManifestAsync(payloadReader, System.Threading.CancellationToken.None)
                    .ConfigureAwait(true);

                return Payload_Dumper_C_.Core.PayloadProcessing
                    .GetPartitions(payloadCtx)
                    .Select(p => p.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            finally
            {
                if (payloadReader is not null && !ReferenceEquals(payloadReader, payloadSourceReader))
                {
                    await payloadReader.DisposeAsync().ConfigureAwait(true);
                }

                if (payloadSourceReader is not null)
                {
                    await payloadSourceReader.DisposeAsync().ConfigureAwait(true);
                }
            }
        }

        private async Task<List<string>> ExtractPartitionsWithPayloadProcessingAsync(
            string sourceInput,
            IEnumerable<string> requestedPartitions,
            string outputPath,
            string startMessage)
        {
            Payload_Dumper_C_.Core.IRandomAccessReader? payloadSourceReader = null;
            Payload_Dumper_C_.Core.IRandomAccessReader? payloadReader = null;

            try
            {
                Directory.CreateDirectory(outputPath);

                var requested = requestedPartitions
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                AppendOugaFlashParagraphLog(startMessage);
                AppendOugaFlashParagraphLog($"源文件: {sourceInput}");
                AppendOugaFlashParagraphLog($"输出目录: {outputPath}");

                ShowProgressBar();
                ResetProgressBar();
                UpdateTransferRateText("准备中...");

                payloadSourceReader = await Payload_Dumper_C_.Core.PayloadProcessing
                    .OpenSourceAsync(sourceInput, System.Threading.CancellationToken.None)
                    .ConfigureAwait(true);

                var openResult = await Payload_Dumper_C_.Core.PayloadProcessing.OpenPayloadReaderAsync(
                        payloadSourceReader,
                        System.Threading.CancellationToken.None,
                        log: message => AppendOugaFlashParagraphLog(message),
                        extractProgress: null,
                        eagerExtract: false)
                    .ConfigureAwait(true);

                payloadReader = openResult.PayloadReader;

                var payloadCtx = await Payload_Dumper_C_.Core.PayloadProcessing
                    .ReadManifestAsync(payloadReader, System.Threading.CancellationToken.None)
                    .ConfigureAwait(true);

                var available = Payload_Dumper_C_.Core.PayloadProcessing
                    .GetPartitions(payloadCtx)
                    .Select(p => p.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var availableSet = new HashSet<string>(available, StringComparer.OrdinalIgnoreCase);
                var matchedPartitions = requested.Count == 0
                    ? available
                    : requested.Where(availableSet.Contains).ToList();

                var missingPartitions = requested.Count == 0
                    ? new List<string>()
                    : requested.Where(name => !availableSet.Contains(name)).ToList();

                foreach (string missing in missingPartitions)
                {
                    AppendOugaFlashParagraphLog($"警告: 未找到分区 '{missing}'，可能该ROM中不包含此分区", "Yellow");
                }

                if (matchedPartitions.Count == 0)
                {
                    UpdateTransferRateText("未找到分区");
                    return new List<string>();
                }

                AppendOugaFlashParagraphLog($"本次将提取 {matchedPartitions.Count} 个分区: {string.Join(", ", matchedPartitions)}");

                if (payloadCtx.Reader is Payload_Dumper_C_.Core.ZipPayloadLazyReader lazy && !lazy.IsExtracted)
                {
                    AppendOugaFlashParagraphLog("正在预解压 payload.bin...");
                    var unzipProgress = new Progress<double>(p =>
                    {
                        UpdateProgressBarValue(p);
                        UpdateTransferRateText($"解压中 {p:0}%");
                    });
                    await lazy.EnsureExtractedAsync(unzipProgress, System.Threading.CancellationToken.None).ConfigureAwait(true);
                    ResetProgressBar();
                }

                UpdateTransferRateText("提取中...");
                var progress = new Progress<(long DoneOps, long TotalOps)>(p =>
                {
                    if (p.TotalOps <= 0)
                    {
                        return;
                    }

                    double percent = Math.Min(100d, (double)p.DoneOps * 100d / p.TotalOps);
                    UpdateProgressBarValue(percent);
                    UpdateTransferRateText($"提取中 {percent:0}%");
                });

                await Payload_Dumper_C_.Core.PayloadProcessing.ExtractPartitionsAsync(
                        payloadCtx,
                        matchedPartitions.ToHashSet(StringComparer.OrdinalIgnoreCase),
                        outputPath,
                        Environment.ProcessorCount,
                        log: AppendOugaFlashPayloadExportLog,
                        progress: progress,
                        cancellationToken: System.Threading.CancellationToken.None)
                    .ConfigureAwait(true);

                UpdateProgressBarValue(100);
                UpdateTransferRateText("完成");
                return matchedPartitions;
            }
            catch
            {
                AppendOugaFlashExtractFail("失败");
                throw;
            }
            finally
            {
                if (payloadReader is not null && !ReferenceEquals(payloadReader, payloadSourceReader))
                {
                    await payloadReader.DisposeAsync().ConfigureAwait(true);
                }

                if (payloadSourceReader is not null)
                {
                    await payloadSourceReader.DisposeAsync().ConfigureAwait(true);
                }
            }
        }

        // 处理云解包功能
        private async Task HandleCloudExtractionAsync()
        {
            try
            {
                string binUrl = (BinUrlTextBox.Text ?? string.Empty).Trim().Trim('`');
                if (string.IsNullOrEmpty(binUrl) || binUrl == "全量包链接 or Payload.bin路径...")
                {
                    AppendOugaFlashParagraphLog("错误: 请输入有效的全量包链接或Payload.bin路径", "Red");
                    return;
                }

                var folderDialog = new System.Windows.Forms.FolderBrowserDialog()
                {
                    Description = "选择云解包文件的保存路径",
                    ShowNewFolderButton = true
                };

                if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    AppendOugaFlashParagraphLog("用户取消了文件夹选择", "Yellow");
                    return;
                }

                string outputPath = folderDialog.SelectedPath;
                var partitionList = await GetPayloadPartitionNamesAsync(binUrl);

                string partitionsInfoPath = Path.Combine("flash", "output", "partitions_info.json");
                var partitionsInfoDirectory = Path.GetDirectoryName(partitionsInfoPath);
                if (!string.IsNullOrEmpty(partitionsInfoDirectory))
                {
                    Directory.CreateDirectory(partitionsInfoDirectory);
                }

                var partitionInfo = new
                {
                    partitions = partitionList,
                    timestamp = DateTime.Now,
                    source = binUrl
                };

                string jsonContent = System.Text.Json.JsonSerializer.Serialize(partitionInfo, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(partitionsInfoPath, jsonContent);
                AppendOugaFlashParagraphLog($"分区信息已保存到 {partitionsInfoPath}");

                var extractedPartitions = await ExtractPartitionsWithPayloadProcessingAsync(
                    binUrl,
                    partitionList,
                    outputPath,
                    "开始云解包，正在提取所有分区...");

                if (extractedPartitions.Count > 0)
                {
                    AppendOugaFlashParagraphLog($"云解包完成！所有分区已提取到: {outputPath}", "Green");
                    await LoadExtractedPartitionsToDataGrid(outputPath, extractedPartitions.ToArray());
                    FolderPathTextBox.Text = outputPath;
                }
            }
            catch (Exception ex)
            {
                AppendOugaFlashParagraphLog($"云解包过程中发生错误: {ex.Message}", "Red");
            }
        }

        private void OugaFlashLogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void OugaFlashStopPanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is StackPanel stackPanel && stackPanel.RenderTransform is ScaleTransform scaleTransform)
            {
                scaleTransform.ScaleX = 0.96;
                scaleTransform.ScaleY = 0.96;
                stackPanel.Opacity = 0.82;
            }
        }

        private void OugaFlashStopPanel_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is StackPanel stackPanel && stackPanel.RenderTransform is ScaleTransform scaleTransform)
            {
                scaleTransform.ScaleX = 1;
                scaleTransform.ScaleY = 1;
                stackPanel.Opacity = 1;
            }

            if (!_isOugaFlashTaskRunning)
            {
                LogToOugaFlash("当前无可停止的任务", "Orange");
                e.Handled = true;
                return;
            }

            if (_ougaFlashStopRequested)
            {
                LogToOugaFlash("已收到停止请求，将在当前分区刷入完成后结束任务", "Orange");
                e.Handled = true;
                return;
            }

            _ougaFlashStopRequested = true;
            string partitionText = string.IsNullOrWhiteSpace(currentFlashingPartition) ? "当前分区" : currentFlashingPartition;
            LogToOugaFlash($"已请求停止，将在 {partitionText} 刷入完成后终止后续分区任务", "Red");
            e.Handled = true;
        }

        private bool ShouldStopOugaFlashBeforeNextPartition(string nextPartitionName)
        {
            if (!_ougaFlashStopRequested)
            {
                return false;
            }

            string targetName = string.IsNullOrWhiteSpace(nextPartitionName) ? "下一个分区" : nextPartitionName;
            LogToOugaFlash($"已停止任务", "Red");
            MarkOujiaFlashStopped();
            return true;
        }

        private bool ShouldFinalizeOugaFlashStop()
        {
            if (!_ougaFlashStopRequested)
            {
                return false;
            }

            LogToOugaFlash("已停止任务", "Orange");
            MarkOujiaFlashStopped();
            return true;
        }

    private Paragraph? _ougaPayloadPendingExtractLine;

    private static Paragraph CreateOugaFlashLogParagraph()
    {
        return new Paragraph
        {
            Margin = new Thickness(0, 0.5, 0, 0.5),
            LineHeight = 19
        };
    }

    private static SolidColorBrush GetOugaFlashLogBrush(string color)
    {
        return color.ToLowerInvariant() switch
        {
            "red" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38)),
            "orange" or "yellow" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(217, 119, 6)),
            "green" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 163, 74)),
            "blue" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235)),
            "purple" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(124, 58, 237)),
            "gray" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139)),
            _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85))
        };
    }

    private static void AppendOugaFlashTimestamp(Paragraph paragraph)
    {
        paragraph.Inlines.Add(new Run($"[{DateTime.Now:HH:mm:ss} ] ")
        {
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184))
        });
    }

    private static void AppendOugaFlashStyledText(
        Paragraph paragraph,
        string text,
        string color,
        bool emphasized = false,
        bool recognizeOperationTag = false)
    {
        string value = text ?? string.Empty;
        if (recognizeOperationTag)
        {
            Match tagMatch = Regex.Match(value, @"^(?<tag>\[[^\]]+\])(?<body>\s*.*)$");
            if (tagMatch.Success)
            {
                paragraph.Inlines.Add(new Run(tagMatch.Groups["tag"].Value)
                {
                    Foreground = GetOugaFlashLogBrush("Purple"),
                    FontWeight = FontWeights.SemiBold
                });
                value = tagMatch.Groups["body"].Value;
            }
        }

        paragraph.Inlines.Add(new Run(value)
        {
            Foreground = GetOugaFlashLogBrush(color),
            FontWeight = emphasized ? FontWeights.Bold : FontWeights.Normal
        });
    }

    private void AppendOugaFlashParagraphLog(string message, string color = "Black")
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AppendOugaFlashParagraphLog(message, color));
            return;
        }

        var lines = (message ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
        {
            var paragraph = CreateOugaFlashLogParagraph();
            AppendOugaFlashTimestamp(paragraph);
            AppendOugaFlashStyledText(
                paragraph,
                line,
                color,
                emphasized: color.Equals("Green", StringComparison.OrdinalIgnoreCase) ||
                            color.Equals("Red", StringComparison.OrdinalIgnoreCase),
                recognizeOperationTag: true);
            OugaFlashLogTextBox.Document.Blocks.Add(paragraph);
        }

        OugaFlashLogTextBox.ScrollToEnd();
    }

    private void AppendOugaFlashPayloadExportLog(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AppendOugaFlashPayloadExportLog(message));
            return;
        }

        const string StartPrefix = "开始导出:";
        const string EndPrefix = "完成导出:";

        var msg = message?.Trim() ?? string.Empty;
        if (msg.StartsWith(StartPrefix, StringComparison.Ordinal))
        {
            var source = msg[StartPrefix.Length..].Trim();
            var name = source;
            int i = source.IndexOf(' ');
            if (i > 0) name = source[..i];
            AppendOugaFlashExtractStart($"{name}.img...");
            return;
        }

        if (msg.StartsWith(EndPrefix, StringComparison.Ordinal))
        {
            AppendOugaFlashExtractOk();
            return;
        }

        AppendOugaFlashParagraphLog(message ?? string.Empty);
    }

    private void AppendOugaFlashExtractStart(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AppendOugaFlashExtractStart(text));
            return;
        }

        _ougaPayloadPendingExtractLine = CreateOugaFlashLogParagraph();
        AppendOugaFlashTimestamp(_ougaPayloadPendingExtractLine);
        AppendOugaFlashStyledText(_ougaPayloadPendingExtractLine, "[提取] ", "Purple", emphasized: true);
        AppendOugaFlashStyledText(_ougaPayloadPendingExtractLine, text, "Black");
        OugaFlashLogTextBox.Document.Blocks.Add(_ougaPayloadPendingExtractLine);
        OugaFlashLogTextBox.ScrollToEnd();
    }

    private void AppendOugaFlashExtractOk()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(AppendOugaFlashExtractOk);
            return;
        }

        var paragraph = _ougaPayloadPendingExtractLine;
        if (paragraph is null)
        {
            AppendOugaFlashParagraphLog("OK", "Green");
            return;
        }

        AppendOugaFlashStyledText(paragraph, " OK", "Green", emphasized: true);
        _ougaPayloadPendingExtractLine = null;
        OugaFlashLogTextBox.ScrollToEnd();
    }

    private void AppendOugaFlashExtractFail(string text = "失败")
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AppendOugaFlashExtractFail(text));
            return;
        }

        var paragraph = _ougaPayloadPendingExtractLine;
        if (paragraph is null)
        {
            AppendOugaFlashParagraphLog(text, "Red");
            return;
        }

        AppendOugaFlashStyledText(paragraph, $" {text}", "Red", emphasized: true);
        _ougaPayloadPendingExtractLine = null;
        OugaFlashLogTextBox.ScrollToEnd();
    }

    // 处理Payload解包功能
    private async Task HandlePayloadUnpack()
    {
        Payload_Dumper_C_.Core.IRandomAccessReader? payloadSourceReader = null;
        Payload_Dumper_C_.Core.IRandomAccessReader? payloadReader = null;

        try
        {
            // 从TextBox获取Payload.bin文件路径
            string payloadFilePath = PayloadFilePathTextBox.Text.Trim();
            
            // 验证Payload文件路径
            if (string.IsNullOrEmpty(payloadFilePath) || payloadFilePath == "请选择Payload.bin文件或全量包Zip文件...")
            {
                AppendOugaFlashParagraphLog("错误: 请先选择Payload.bin文件或全量包Zip文件", "Red");
                return;
            }
            
            if (!File.Exists(payloadFilePath))
            {
                AppendOugaFlashParagraphLog($"错误: Payload文件不存在: {payloadFilePath}", "Red");
                return;
            }
            
            // 从TextBox获取输出目录路径
            string outputPath = FolderPathTextBox.Text.Trim();
            
            // 验证输出目录路径
            if (string.IsNullOrEmpty(outputPath) || outputPath == "请选择解包好的文件夹或解包输出路径...")
            {
                AppendOugaFlashParagraphLog("错误: 请先选择解包输出目录", "Red");
                return;
            }
            
            // 如果输出目录不存在，创建它
            if (!Directory.Exists(outputPath))
            {
                try
                {
                    Directory.CreateDirectory(outputPath);
                }
                catch (Exception ex)
                {
                    AppendOugaFlashParagraphLog($"错误: 无法创建输出目录: {ex.Message}", "Red");
                    return;
                }
            }
            
            // 检查磁盘可用空间
            long availableSpaceGB = GetAvailableDiskSpaceGB(outputPath);
            if (availableSpaceGB < 20)
            {
                // 显示警告对话框
                MessageBoxResult result = System.Windows.MessageBox.Show(
                    $"警告：目标磁盘可用空间仅有 {availableSpaceGB} GB，建议预留 20 GB 以上的磁盘空间\n\n" +
                    "继续解包可能导致解包不完全，存在变砖风险。\n\n" +
                    "建议清理磁盘空间后再进行解包操作。\n\n" +
                    "是否仍要继续解包？",
                    "磁盘空间不足警告",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.No)
                {
                    AppendOugaFlashParagraphLog("用户取消解包操作（磁盘空间不足）", "Yellow");
                    return;
                }
                else
                {
                    AppendOugaFlashParagraphLog("警告: 磁盘空间不足但用户选择继续解包", "Yellow");
                }
            }
            
            // 检测刷机包机型
            await CheckDeviceModel(payloadFilePath);
            
            // 将解包输出路径设置为用户选择文件夹下的images子文件夹
            outputPath = Path.Combine(outputPath, "images");
            if (!Directory.Exists(outputPath))
            {
                Directory.CreateDirectory(outputPath);
            }

            // 在日志窗口显示开始信息
            AppendOugaFlashParagraphLog("开始解包，输出将实时显示在日志窗口中。");
            AppendOugaFlashParagraphLog($"源文件: {payloadFilePath}");
            AppendOugaFlashParagraphLog($"输出目录: {outputPath}");

            ShowProgressBar();
            ResetProgressBar();
            UpdateTransferRateText("准备中...");

            payloadSourceReader = await Payload_Dumper_C_.Core.PayloadProcessing
                .OpenSourceAsync(payloadFilePath, System.Threading.CancellationToken.None)
                .ConfigureAwait(true);

            var openResult = await Payload_Dumper_C_.Core.PayloadProcessing.OpenPayloadReaderAsync(
                    payloadSourceReader,
                    System.Threading.CancellationToken.None,
                    log: message => AppendOugaFlashParagraphLog(message),
                    extractProgress: null,
                    eagerExtract: false)
                .ConfigureAwait(true);

            payloadReader = openResult.PayloadReader;

            var payloadCtx = await Payload_Dumper_C_.Core.PayloadProcessing
                .ReadManifestAsync(payloadReader, System.Threading.CancellationToken.None)
                .ConfigureAwait(true);

            var partitions = Payload_Dumper_C_.Core.PayloadProcessing
                .GetPartitions(payloadCtx)
                .Select(p => p.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (partitions.Count == 0)
            {
                AppendOugaFlashParagraphLog("未读取到任何可解包分区", "Red");
                return;
            }

            AppendOugaFlashParagraphLog($"发现 {partitions.Count} 个分区: {string.Join(", ", partitions)}", "Green");

            if (payloadCtx.Reader is Payload_Dumper_C_.Core.ZipPayloadLazyReader lazy && !lazy.IsExtracted)
            {
                AppendOugaFlashParagraphLog("正在预解压 payload.bin...");
                var unzipProgress = new Progress<double>(p =>
                {
                    UpdateProgressBarValue(p);
                    UpdateTransferRateText($"解压中 {p:0}%");
                });
                await lazy.EnsureExtractedAsync(unzipProgress, System.Threading.CancellationToken.None).ConfigureAwait(true);
                ResetProgressBar();
            }

            AppendOugaFlashParagraphLog("开始解包分区...");
            UpdateTransferRateText("解包中...");

            var progress = new Progress<(long DoneOps, long TotalOps)>(p =>
            {
                if (p.TotalOps <= 0)
                {
                    return;
                }

                double percent = Math.Min(100d, (double)p.DoneOps * 100d / p.TotalOps);
                UpdateProgressBarValue(percent);
                UpdateTransferRateText($"解包中 {percent:0}%");
            });

            await Payload_Dumper_C_.Core.PayloadProcessing.ExtractPartitionsAsync(
                    payloadCtx,
                    partitions.ToHashSet(StringComparer.OrdinalIgnoreCase),
                    outputPath,
                    Environment.ProcessorCount,
                    log: AppendOugaFlashPayloadExportLog,
                    progress: progress,
                    cancellationToken: System.Threading.CancellationToken.None)
                .ConfigureAwait(true);

            UpdateProgressBarValue(100);
            UpdateTransferRateText("完成");
            AppendOugaFlashParagraphLog("Payload解包成功！", "Green");
            AppendOugaFlashParagraphLog($"解包完成，文件保存在: {outputPath}", "Green");
            Dispatcher.Invoke(() =>
            {
                FolderPathTextBox.Text = outputPath;
                LoadPartitionsFromFolder(outputPath);
            });
        }
        catch (Exception ex)
        {
            AppendOugaFlashExtractFail("失败");
            AppendOugaFlashParagraphLog($"Payload解包过程中发生异常: {ex.Message}", "Red");
        }
        finally
        {
            if (payloadReader is not null && !ReferenceEquals(payloadReader, payloadSourceReader))
            {
                await payloadReader.DisposeAsync().ConfigureAwait(true);
            }

            if (payloadSourceReader is not null)
            {
                await payloadSourceReader.DisposeAsync().ConfigureAwait(true);
            }
        }
        }

        private static List<string> ParsePayloadDumperListOutput(string output)
        {
            var partitions = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddPartition(string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return;
                if (seen.Add(name))
                {
                    partitions.Add(name);
                }
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                return partitions;
            }

            var lineStyle = new Regex(
                @"^\s*(?<name>[A-Za-z0-9_]+)\s+(?<size>\d+(?:\.\d+)?(?:B|KB|MB|GB|TB|PB))\s+(?<bytes>\d+)\s*$",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

            var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("download", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("total bytes read", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("Partition information saved", StringComparison.OrdinalIgnoreCase)) continue;

                var candidate = line;
                var promptIdx = candidate.LastIndexOf('>');
                if (promptIdx >= 0 && promptIdx < candidate.Length - 1)
                {
                    var afterPrompt = candidate[(promptIdx + 1)..].TrimStart();
                    if (afterPrompt.Length > 0)
                    {
                        candidate = afterPrompt;
                    }
                }

                var m = lineStyle.Match(candidate);
                if (m.Success)
                {
                    AddPartition(m.Groups["name"].Value);
                }
            }

            if (partitions.Count > 0)
            {
                return partitions;
            }

            var matches = Regex.Matches(output, @"(\w+)\([^)]+\)");
            foreach (Match match in matches)
            {
                AddPartition(match.Groups[1].Value);
            }

            return partitions;
        }

        private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "请选择包含镜像文件的文件夹",
                ShowNewFolderButton = false
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string selectedPath = folderDialog.SelectedPath;
                
                // 更新TextBox显示选择的路径
                if (FolderPathTextBox != null)
                {
                    FolderPathTextBox.Text = selectedPath;
                }
                
                // 在选择的文件夹内创建images文件夹
                try
                {
                    // 检测用户选择的路径是否已经是images文件夹
                    string folderName = System.IO.Path.GetFileName(selectedPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
                    
                    if (string.Equals(folderName, "images", StringComparison.OrdinalIgnoreCase))
                    {
                        // 如果用户选择的路径本身就是images文件夹，则不再创建子images文件夹
                        AddLogMessage("系统", $"检测到选择的路径本身就是images文件夹，跳过创建: {selectedPath}");
                    }
                    else
                    {
                        // 如果不是images文件夹，则在其内部创建images文件夹
                        string imagesPath = System.IO.Path.Combine(selectedPath, "images");
                        if (!System.IO.Directory.Exists(imagesPath))
                        {
                            System.IO.Directory.CreateDirectory(imagesPath);
                            AddLogMessage("系统", $"已在选择的文件夹内创建images文件夹: {imagesPath}");
                        }
                        else
                        {
                            AddLogMessage("系统", $"images文件夹已存在: {imagesPath}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    AddLogMessage("错误", $"创建images文件夹失败: {ex.Message}");
                }
                
                // 读取文件夹中的镜像文件
                LoadImageFilesFromFolder(selectedPath);
            }
        }

        private void ParseAfterSalesImages(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !System.IO.Directory.Exists(rootPath))
            {
                return;
            }

            var partitions = new System.Collections.ObjectModel.ObservableCollection<PartitionInfo>();
            var priorityPartitions = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 定义目标文件夹
            string[] targetFolders = { "IMAGES", "RADIO" };
            // 定义 IMAGES 下的特殊子文件夹
            string[] specialSubFolders = { "my_bigball", "my_carrier", "my_company", "my_heytap", "my_manifest", "my_preload", "my_region", "my_stock" };

            string FormatFileSize(long bytes)
            {
                string[] sizes = { "B", "KB", "MB", "GB", "TB" };
                double len = bytes;
                int order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len = len / 1024;
                }
                return $"{len:0.##} {sizes[order]}";
            }

            try 
            {
                // 1. 优先处理 IMAGES 下的特殊子文件夹
                string imagesPath = System.IO.Path.Combine(rootPath, "IMAGES");
                if (System.IO.Directory.Exists(imagesPath))
                {
                    foreach (var subFolder in specialSubFolders)
                    {
                        string subFolderPath = System.IO.Path.Combine(imagesPath, subFolder);
                        if (!System.IO.Directory.Exists(subFolderPath)) continue;

                        // 查找该文件夹下的 .img 文件
                        var imgFiles = System.IO.Directory.GetFiles(subFolderPath, "*.img");
                        if (imgFiles.Length > 0)
                        {
                            // 取第一个找到的 img 文件
                            string imgFile = imgFiles[0];
                            long size = new System.IO.FileInfo(imgFile).Length;

                            partitions.Add(new PartitionInfo
                            {
                                PartitionName = subFolder, // 使用子文件夹名作为分区名
                                PartitionSize = FormatFileSize(size),
                                FilePath = imgFile,
                                IsSelected = true
                            });
                            
                            // 记录高优先级分区名
                            priorityPartitions.Add(subFolder);
                        }
                    }
                }

                // 2. 遍历 IMAGES 和 RADIO 文件夹
                foreach (var folderName in targetFolders)
                {
                    string folderPath = System.IO.Path.Combine(rootPath, folderName);
                    if (!System.IO.Directory.Exists(folderPath)) continue;

                    // 获取所有文件
                    var files = System.IO.Directory.GetFiles(folderPath);
                    foreach (var file in files)
                    {
                        string fileName = System.IO.Path.GetFileName(file);
                        string lowerFileName = fileName.ToLowerInvariant();
                        
                        // 过滤规则：必须是 .img 或 .iso
                        if (!lowerFileName.EndsWith(".img") && !lowerFileName.EndsWith(".iso")) continue;

                        string nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(lowerFileName);
                        
                        // 过滤 userdata 和 metadata
                        if (nameWithoutExt == "userdata" || nameWithoutExt == "metadata") continue;

                        // 冲突检测：如果在 IMAGES 文件夹中，且与高优先级分区名冲突（相同或以其开头），则跳过
                        if (folderName == "IMAGES")
                        {
                            bool isConflict = false;
                            foreach (var priorityName in priorityPartitions)
                            {
                                if (nameWithoutExt.StartsWith(priorityName, StringComparison.OrdinalIgnoreCase))
                                {
                                    isConflict = true;
                                    break;
                                }
                            }
                            if (isConflict) continue;
                        }

                        // 创建 PartitionInfo
                        long size = new System.IO.FileInfo(file).Length;
                        
                        partitions.Add(new PartitionInfo
                        {
                            PartitionName = nameWithoutExt,
                            PartitionSize = FormatFileSize(size),
                            FilePath = file,
                            IsSelected = true
                        });
                    }
                }
                
                // 更新 DataGrid
                if (OugaPartitionTableDataGrid != null)
                {
                     OugaPartitionTableDataGrid.ItemsSource = partitions;
                }

                if (OugaFlashLogTextBox != null)
                {
                    AppendOugaFlashParagraphLog($"已加载 {partitions.Count} 个镜像文件", "Green");
                    OugaFlashLogTextBox.ScrollToEnd();
                }
            }
            catch (Exception ex)
            {
                AddLogMessage("错误", $"解析散包文件夹失败: {ex.Message}");
            }
        }

        private void AfterSalesSelectFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "请选择一个散包文件夹",
                ShowNewFolderButton = false
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string selectedPath = folderDialog.SelectedPath;

                // 校验散包文件夹结构
                if (!System.IO.Directory.Exists(System.IO.Path.Combine(selectedPath, "IMAGES")) ||
                    !System.IO.Directory.Exists(System.IO.Path.Combine(selectedPath, "RADIO")))
                {
                    LogToOugaFlash("蜡笔，这不是一个散包文件夹哦！", "Red");
                    return;
                }

                var textBox = this.FindName("AfterSalesFlashPackTextBox") as System.Windows.Controls.TextBox;
                if (textBox != null)
                {
                    textBox.Text = selectedPath;
                }
                ParseAfterSalesImages(selectedPath);
            }
        }

        private void AfterSalesFlashPackTextBox_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                e.Effects = System.Windows.DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void AfterSalesFlashPackTextBox_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    string path = files[0];
                    if (System.IO.Directory.Exists(path))
                    {
                        // 校验散包文件夹结构
                        if (!System.IO.Directory.Exists(System.IO.Path.Combine(path, "IMAGES")) ||
                            !System.IO.Directory.Exists(System.IO.Path.Combine(path, "RADIO")))
                        {
                            LogToOugaFlash("蜡笔，这不是一个散包文件夹哦！", "Red");
                            return;
                        }

                        var textBox = sender as System.Windows.Controls.TextBox;
                        if (textBox != null)
                        {
                            textBox.Text = path;
                        }
                        ParseAfterSalesImages(path);
                    }
                }
            }
        }

        private string GetFlashProgressFilePath()
        {
            string programDirectory = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(programDirectory, "flash_progress.txt");
        }

        private long ParseSizeToBytes(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            var match = Regex.Match(
                text,
                @"^(?<val>\d+(?:\.\d+)?)\s*(?<unit>[KMGTP]?B)$",
                RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return double.TryParse(text, out double value) ? (long)value : 0;
            }

            double size = double.Parse(match.Groups["val"].Value);
            string unit = match.Groups["unit"].Value.ToUpperInvariant();
            return unit switch
            {
                "TB" => (long)(size * 1024L * 1024L * 1024L * 1024L),
                "GB" => (long)(size * 1024L * 1024L * 1024L),
                "MB" => (long)(size * 1024L * 1024L),
                "KB" => (long)(size * 1024L),
                _ => (long)size
            };
        }

        private long ReadFlashedBytesFromFile()
        {
            try
            {
                string path = GetFlashProgressFilePath();
                if (!File.Exists(path)) return 0;

                string[] parts = File.ReadAllText(path).Trim().Split('/');
                return parts.Length > 0 ? ParseSizeToBytes(parts[0].Trim()) : 0;
            }
            catch
            {
                return 0;
            }
        }

        private void WriteFlashProgressFile(long flashedBytes, long totalBytes)
        {
            try
            {
                string content = $"{FormatFileSize(flashedBytes)}/{FormatFileSize(totalBytes)}";
                File.WriteAllText(GetFlashProgressFilePath(), content, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private double _flashProgressPercentage;
        public double FlashProgressPercentage
        {
            get => _flashProgressPercentage;
            set
            {
                _flashProgressPercentage = value;
                OnPropertyChanged(nameof(FlashProgressPercentage));
                OnPropertyChanged(nameof(FlashProgressDashArray));
                OnPropertyChanged(nameof(FlashProgressText));
                if (_flashElapsedTimer != null && _flashElapsedTimer.IsEnabled && value >= 100) _flashElapsedTimer.Stop();
            }
        }
        
        private string _flashProgressDetailText = "等待刷写...";
        public string FlashProgressDetailText
        {
            get => _flashProgressDetailText;
            set
            {
                _flashProgressDetailText = value;
                OnPropertyChanged(nameof(FlashProgressDetailText));
            }
        }

        public string FlashProgressText => $"{FlashProgressPercentage:F1}%";

        private const double FlashCircleCircumference = 471.239;
        public double FlashStrokeThickness => 12;
        private double FlashCircumferenceUnits => FlashCircleCircumference / FlashStrokeThickness;

        public DoubleCollection FlashProgressDashArray =>
            new DoubleCollection {
                FlashCircumferenceUnits * (Math.Max(0, Math.Min(100, _flashProgressPercentage)) / 100),
                FlashCircumferenceUnits * (1 - Math.Max(0, Math.Min(100, _flashProgressPercentage)) / 100)
            };

        private long _lastGlobalProgressBytes = 0;
        private DispatcherTimer _flashElapsedTimer;
        private DateTime _flashStartTime;
        private string _flashElapsedText = "耗时00:00";
        public string FlashElapsedText
        {
            get => _flashElapsedText;
            set
            {
                _flashElapsedText = value;
                OnPropertyChanged(nameof(FlashElapsedText));
            }
        }

        private bool _isUpdatingOugaPackageModeSelection;

        private void ApplyOugaPackageModeSelection(bool useAfterSalesMode)
        {
            if (_isUpdatingOugaPackageModeSelection)
            {
                return;
            }

            _isUpdatingOugaPackageModeSelection = true;
            try
            {
                var fullPackageCheckBox =
                    this.FindName("FullPackageModeCheckBox") as System.Windows.Controls.CheckBox;
                var afterSalesCheckBox =
                    this.FindName("AfterSalesPackageModeCheckBox") as System.Windows.Controls.CheckBox;
                var standardFlashContent = this.FindName("StandardFlashContent") as Grid;
                var afterSalesContent = this.FindName("AfterSalesContent") as Grid;
                var partitionTableValidationSettingPanel =
                    this.FindName("PartitionTableValidationSettingPanel") as FrameworkElement;

                if (fullPackageCheckBox != null)
                {
                    fullPackageCheckBox.IsChecked = !useAfterSalesMode;
                }

                if (afterSalesCheckBox != null)
                {
                    afterSalesCheckBox.IsChecked = useAfterSalesMode;
                }

                if (standardFlashContent != null)
                {
                    standardFlashContent.Visibility = useAfterSalesMode
                        ? Visibility.Collapsed
                        : Visibility.Visible;
                }

                if (afterSalesContent != null)
                {
                    afterSalesContent.Visibility = useAfterSalesMode
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }

                if (partitionTableValidationSettingPanel != null)
                {
                    partitionTableValidationSettingPanel.Visibility = Visibility.Visible;
                    partitionTableValidationSettingPanel.IsEnabled = !useAfterSalesMode;
                }
            }
            finally
            {
                _isUpdatingOugaPackageModeSelection = false;
            }
        }

        private void FullPackageModeCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            ApplyOugaPackageModeSelection(useAfterSalesMode: false);
        }

        private void FullPackageModeCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingOugaPackageModeSelection)
            {
                return;
            }

            var afterSalesCheckBox =
                this.FindName("AfterSalesPackageModeCheckBox") as System.Windows.Controls.CheckBox;
            if (afterSalesCheckBox?.IsChecked != true)
            {
                ApplyOugaPackageModeSelection(useAfterSalesMode: false);
            }
        }

        private void AfterSalesPackageModeCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            ApplyOugaPackageModeSelection(useAfterSalesMode: true);
        }

        private void AfterSalesPackageModeCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingOugaPackageModeSelection)
            {
                return;
            }

            var fullPackageCheckBox =
                this.FindName("FullPackageModeCheckBox") as System.Windows.Controls.CheckBox;
            if (fullPackageCheckBox?.IsChecked != true)
            {
                ApplyOugaPackageModeSelection(useAfterSalesMode: true);
            }
        }

        private bool _isUpdatingOugaPartitionTableValidationSelection;

        private void ApplyOugaPartitionTableValidationSelection(bool isEnabled)
        {
            if (_isUpdatingOugaPartitionTableValidationSelection)
            {
                return;
            }

            _isUpdatingOugaPartitionTableValidationSelection = true;
            try
            {
                var enabledCheckBox =
                    this.FindName("PartitionTableValidationEnabledCheckBox")
                        as System.Windows.Controls.CheckBox;
                var disabledCheckBox =
                    this.FindName("PartitionTableValidationDisabledCheckBox")
                        as System.Windows.Controls.CheckBox;

                if (enabledCheckBox != null)
                {
                    enabledCheckBox.IsChecked = isEnabled;
                }

                if (disabledCheckBox != null)
                {
                    disabledCheckBox.IsChecked = !isEnabled;
                }
            }
            finally
            {
                _isUpdatingOugaPartitionTableValidationSelection = false;
            }
        }

        private void PartitionTableValidationEnabledCheckBox_Checked(
            object sender,
            RoutedEventArgs e)
        {
            ApplyOugaPartitionTableValidationSelection(isEnabled: true);
        }

        private void PartitionTableValidationEnabledCheckBox_Unchecked(
            object sender,
            RoutedEventArgs e)
        {
            if (_isUpdatingOugaPartitionTableValidationSelection)
            {
                return;
            }

            var disabledCheckBox =
                this.FindName("PartitionTableValidationDisabledCheckBox")
                    as System.Windows.Controls.CheckBox;
            if (disabledCheckBox?.IsChecked != true)
            {
                ApplyOugaPartitionTableValidationSelection(isEnabled: true);
            }
        }

        private void PartitionTableValidationDisabledCheckBox_Checked(
            object sender,
            RoutedEventArgs e)
        {
            ApplyOugaPartitionTableValidationSelection(isEnabled: false);
        }

        private void PartitionTableValidationDisabledCheckBox_Unchecked(
            object sender,
            RoutedEventArgs e)
        {
            if (_isUpdatingOugaPartitionTableValidationSelection)
            {
                return;
            }

            var enabledCheckBox =
                this.FindName("PartitionTableValidationEnabledCheckBox")
                    as System.Windows.Controls.CheckBox;
            if (enabledCheckBox?.IsChecked != true)
            {
                ApplyOugaPartitionTableValidationSelection(isEnabled: false);
            }
        }

        private enum OujiaFlashMode
        {
            Normal,
            Force,
            Ab,
            PureFastbootd
        }

        private enum OujiaFlashRunStatus
        {
            Running,
            Success,
            PartialFailure,
            Failed,
            Stopped,
            Cancelled
        }

        private enum OujiaProcessorPlatform
        {
            Qualcomm,
            MediaTek
        }

        private sealed class OujiaFlashSessionContext
        {
            public OujiaFlashSessionContext(
                OujiaFlashMode mode,
                string preferredSerial,
                bool wasDeviceDetectionEnabled,
                bool clearDataRequested,
                bool autoRebootRequested,
                bool partitionTableValidationRequested)
            {
                Mode = mode;
                PreferredSerial = preferredSerial;
                Serial = preferredSerial;
                WasDeviceDetectionEnabled = wasDeviceDetectionEnabled;
                ClearDataRequested = clearDataRequested;
                AutoRebootRequested = autoRebootRequested;
                PartitionTableValidationRequested = partitionTableValidationRequested;
            }

            public OujiaFlashMode Mode { get; }
            public string PreferredSerial { get; }
            public string Serial { get; set; }
            public string CurrentSlot { get; set; } = string.Empty;
            public bool WasDeviceDetectionEnabled { get; }
            public bool ClearDataRequested { get; }
            public bool AutoRebootRequested { get; }
            public bool PartitionTableValidationRequested { get; }
            public HashSet<string> AvailablePartitions { get; } = new(StringComparer.OrdinalIgnoreCase);
            public OujiaFlashRunStatus Status { get; set; } = OujiaFlashRunStatus.Running;
            public List<string> PhaseFailures { get; } = new();
            public bool CompletionLogged { get; set; }
            public bool PartitionSummaryLogged { get; set; }
            public bool WorkflowCompleted { get; set; }
            public bool AutoRebootCancelledForPartitionFailure { get; set; }
        }

        private sealed class OujiaFlashPlan
        {
            public OujiaFlashPlan(
                List<PartitionInfo> partitions,
                Dictionary<string, string> additionalImages,
                long totalBytes,
                string totalSizeText)
            {
                Partitions = partitions;
                AdditionalImages = additionalImages;
                TotalBytes = totalBytes;
                TotalSizeText = totalSizeText;
            }

            public List<PartitionInfo> Partitions { get; }
            public Dictionary<string, string> AdditionalImages { get; }
            public long TotalBytes { get; }
            public string TotalSizeText { get; }
        }

        private async void StartFlashButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isOugaFlashTaskRunning)
            {
                LogToOugaFlash("当前已有欧加线刷任务正在运行", "Orange");
                return;
            }

            if (!ValidateOujiaFlashSourceBeforeStart())
            {
                return;
            }

            await ExecuteOujiaFlashModeAsync(ResolveOujiaFlashMode());
        }

        private bool ValidateOujiaFlashSourceBeforeStart()
        {
            List<PartitionInfo> partitions =
                (OugaPartitionTableDataGrid.ItemsSource as IEnumerable<PartitionInfo>)
                ?.ToList() ?? new List<PartitionInfo>();
            if (partitions.Count == 0)
            {
                LogToOugaFlash(
                    "分区表为空，请先选择解包完成的镜像文件夹后再开始线刷.",
                    "Red");
                return false;
            }

            List<PartitionInfo> selectedPartitions = partitions
                .Where(partition => partition.IsSelected)
                .ToList();
            if (selectedPartitions.Count == 0)
            {
                LogToOugaFlash(
                    "分区表中没有已勾选的镜像文件，请先勾选需要刷写的分区.",
                    "Red");
                return false;
            }

            if (selectedPartitions.Any(IsOujiaUnpackedPackageFile))
            {
                LogToOugaFlash(
                    "检测到未解包的Payload.bin或全量包Zip文件，请先解包Payload并加载镜像分区后再开始线刷.",
                    "Red");
                return false;
            }

            bool hasValidImage = selectedPartitions.Any(partition =>
                !string.IsNullOrWhiteSpace(partition.FilePath) &&
                !partition.FilePath.Contains("(缺失)", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(partition.FilePath));
            if (!hasValidImage)
            {
                LogToOugaFlash(
                    "分区表中没有已勾选且文件有效的镜像，请重新选择解包完成的镜像文件夹.",
                    "Red");
                return false;
            }

            return true;
        }

        private static bool IsOujiaUnpackedPackageFile(PartitionInfo partition)
        {
            string filePath = partition.FilePath?.Trim() ?? string.Empty;
            string fileName = Path.GetFileName(filePath);
            string extension = Path.GetExtension(filePath);
            return fileName.Equals("payload.bin", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);
        }

        private OujiaFlashMode ResolveOujiaFlashMode()
        {
            if (FlashABCheckBox.IsChecked == true)
            {
                return OujiaFlashMode.Ab;
            }

            if (FixSuperCheckBox.IsChecked == true)
            {
                return OujiaFlashMode.Force;
            }

            return PureFBDCheckBox.IsChecked == true
                ? OujiaFlashMode.PureFastbootd
                : OujiaFlashMode.Normal;
        }

        private async Task ExecuteOujiaFlashModeAsync(OujiaFlashMode mode)
        {
            string preferredSerial = GetSelectedDeviceSerial();
            var session = new OujiaFlashSessionContext(
                mode,
                preferredSerial,
                _isDeviceDetectionEnabled,
                ClearDataCheckBox.IsChecked == true,
                AutoRebootOugaCheckBox.IsChecked == true,
                PartitionTableValidationEnabledCheckBox.IsChecked == true);
            _activeOujiaFlashSession = session;

            try
            {
                _isOugaFlashTaskRunning = true;
                _ougaFlashStopRequested = false;
                BeginOugaFlashPartitionResultTracking();
                OugaFlashOverlay.Visibility = Visibility.Visible;
                OugaPartitionTableDataGrid.IsEnabled = false;
                _flashElapsedTimer?.Stop();
                _flashStartTime = DateTime.UtcNow;
                FlashElapsedText = "耗时00:00";
                _flashElapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _flashElapsedTimer.Tick += (s, args) =>
                {
                    var elapsed = DateTime.UtcNow - _flashStartTime;
                    if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
                    FlashElapsedText = $"耗时{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
                };
                _flashElapsedTimer.Start();

                LogStepBegin("委托主页停止异步设备检测");
                if (await PauseOujiaDeviceDetectionAsync())
                {
                    LogStepEndOk();
                }
                else
                {
                    LogStepEndError();
                    MarkOujiaFlashFailure("停止主页异步设备检测", fatal: true);
                    return;
                }

                switch (mode)
                {
                    case OujiaFlashMode.Normal:
                        await ExecuteNormalOujiaFlashWorkflowAsync();
                        break;
                    case OujiaFlashMode.Force:
                        await ExecuteForceOujiaFlashWorkflowAsync();
                        break;
                    case OujiaFlashMode.Ab:
                        await ExecuteAbOujiaFlashWorkflowAsync();
                        break;
                    case OujiaFlashMode.PureFastbootd:
                        await ExecutePureFastbootdOujiaFlashWorkflowAsync();
                        break;
                    default:
                        throw new InvalidOperationException($"不支持的欧加线刷模式：{mode}");
                }
            }
            catch (Exception ex)
            {
                MarkOujiaFlashFailure("线刷流程异常", ex.Message, fatal: true);
                LogToOugaFlash($"线刷过程中发生错误：{ex.Message}", "Red");
                ResetProgressBar();
            }
            finally
            {
                _flashElapsedTimer?.Stop();
                OugaFlashOverlay.Visibility = Visibility.Collapsed;
                OugaPartitionTableDataGrid.IsEnabled = true;

                LogOugaFlashPartitionResultSummary();
                FinalizeOujiaFlashRunStatus();
                LogOugaFlashCompletionResult();

                if (session.WasDeviceDetectionEnabled)
                {
                    try
                    {
                        LogStepBegin("委托主页开始异步设备检测");
                        InitializeDeviceStatusMonitoring();
                        if (DeviceDetectionToggle != null)
                        {
                            DeviceDetectionToggle.IsChecked = true;
                        }
                        LogStepEndOk();
                    }
                    catch (Exception ex)
                    {
                        LogStepEndError();
                        LogToOugaFlash($"恢复设备检测失败：{ex.Message}", "Red");
                    }
                }
                else if (_isDeviceDetectionEnabled)
                {
                    deviceStatusTimer?.Stop();
                    deviceStatusTimer = null;
                    _isDeviceDetectionEnabled = false;
                    if (DeviceDetectionToggle != null)
                    {
                        DeviceDetectionToggle.IsChecked = false;
                    }
                }

                await GenerateFlashLogFile();

                _isOugaFlashTaskRunning = false;
                _ougaFlashStopRequested = false;
                _trackOugaFlashPartitionResults = false;
                bool isError = session.Status != OujiaFlashRunStatus.Success &&
                               session.Status != OujiaFlashRunStatus.PartialFailure;
                if (isError)
                {
                    await Task.Delay(3000);
                }
                HideProgressBar();
                _activeOujiaFlashSession = null;
            }
        }

        private async Task<bool> PauseOujiaDeviceDetectionAsync()
        {
            try
            {
                _isDeviceDetectionEnabled = false;
                unchecked
                {
                    _deviceDetectionVersion++;
                }

                deviceStatusTimer?.Stop();
                deviceStatusTimer = null;
                if (DeviceDetectionToggle != null)
                {
                    DeviceDetectionToggle.IsChecked = false;
                }

                // 必须等待主页残留的 adb/fastboot 查询完全退出，避免与线刷命令竞争。
                await KillAllAdbAndFastbootProcesses();
                return true;
            }
            catch (Exception ex)
            {
                LogToOugaFlash($"停止主页设备检测失败：{ex.Message}", "Red");
                return false;
            }
        }

        private string GetOujiaFlashTargetSerial()
        {
            if (_activeOujiaFlashSession != null)
            {
                if (!string.IsNullOrWhiteSpace(_activeOujiaFlashSession.Serial))
                {
                    return _activeOujiaFlashSession.Serial;
                }

                if (!string.IsNullOrWhiteSpace(_activeOujiaFlashSession.PreferredSerial))
                {
                    return _activeOujiaFlashSession.PreferredSerial;
                }
            }

            return GetSelectedDeviceSerial();
        }

        private void UpdateOujiaFlashSessionDevice(string deviceSerial)
        {
            if (_activeOujiaFlashSession != null && !string.IsNullOrWhiteSpace(deviceSerial))
            {
                _activeOujiaFlashSession.Serial = deviceSerial;
            }
        }

        private void MarkOujiaFlashFailure(
            string phase,
            string? detail = null,
            bool fatal = false)
        {
            if (_activeOujiaFlashSession == null)
            {
                return;
            }

            string message = string.IsNullOrWhiteSpace(detail) ? phase : $"{phase}：{detail}";
            if (!_activeOujiaFlashSession.PhaseFailures.Contains(message, StringComparer.OrdinalIgnoreCase))
            {
                _activeOujiaFlashSession.PhaseFailures.Add(message);
            }

            if (_activeOujiaFlashSession.Status is OujiaFlashRunStatus.Stopped or OujiaFlashRunStatus.Cancelled)
            {
                return;
            }

            _activeOujiaFlashSession.Status = fatal
                ? OujiaFlashRunStatus.Failed
                : OujiaFlashRunStatus.PartialFailure;
        }

        private void MarkOujiaFlashStopped()
        {
            if (_activeOujiaFlashSession != null)
            {
                _activeOujiaFlashSession.Status = OujiaFlashRunStatus.Stopped;
            }
        }

        private void MarkOujiaFlashCancelled()
        {
            if (_activeOujiaFlashSession != null)
            {
                _activeOujiaFlashSession.Status = OujiaFlashRunStatus.Cancelled;
            }
        }

        private void FinalizeOujiaFlashRunStatus()
        {
            if (_activeOujiaFlashSession == null)
            {
                return;
            }

            if (_ougaFlashStopRequested)
            {
                _activeOujiaFlashSession.Status = OujiaFlashRunStatus.Stopped;
                return;
            }

            if (!_activeOujiaFlashSession.WorkflowCompleted)
            {
                if (_activeOujiaFlashSession.Status is not OujiaFlashRunStatus.Stopped and
                    not OujiaFlashRunStatus.Cancelled and
                    not OujiaFlashRunStatus.Failed)
                {
                    _activeOujiaFlashSession.Status = OujiaFlashRunStatus.Failed;
                }
                if (_activeOujiaFlashSession.Status == OujiaFlashRunStatus.Failed &&
                    _activeOujiaFlashSession.PhaseFailures.Count == 0)
                {
                    _activeOujiaFlashSession.PhaseFailures.Add("流程未完成即提前结束");
                }
                return;
            }

            if (_activeOujiaFlashSession.Status == OujiaFlashRunStatus.Running)
            {
                _activeOujiaFlashSession.Status = _ougaFlashPartitionFailureCount > 0
                    ? OujiaFlashRunStatus.PartialFailure
                    : OujiaFlashRunStatus.Success;
            }
            else if (_activeOujiaFlashSession.Status == OujiaFlashRunStatus.Success &&
                     _ougaFlashPartitionFailureCount > 0)
            {
                _activeOujiaFlashSession.Status = OujiaFlashRunStatus.PartialFailure;
            }
        }

        private async Task ExecuteNormalOujiaFlashWorkflowAsync()
        {
            OujiaFlashPlan? plan = CreateOujiaFlashPlan(includeAdditionalImages: false);
            if (plan == null)
            {
                return;
            }
            InitializeOujiaFlashPlan(plan);

            string? fastbootSerial = await PrepareOugaFastbootdDeviceAsync(
                requireExistingFastbootd: false);
            if (string.IsNullOrWhiteSpace(fastbootSerial))
            {
                return;
            }

            OujiaProcessorPlatform? platform =
                await DetectOujiaProcessorPlatformAsync(fastbootSerial);
            if (!platform.HasValue)
            {
                return;
            }

            if (!ValidateOujiaPartitionTableCompatibility(plan.Partitions))
            {
                return;
            }

            string? targetSlot = await EnsureOujiaAbSlotSupportedAsync(fastbootSerial);
            if (string.IsNullOrWhiteSpace(targetSlot))
            {
                return;
            }

            bool isMediaTek = platform.Value == OujiaProcessorPlatform.MediaTek;

            targetSlot = await GetCurrentSlot();
            if (string.IsNullOrWhiteSpace(targetSlot) ||
                !ValidateOujiaFlashTargetCollisions(plan, platform.Value, targetSlot))
            {
                return;
            }

            LogToOugaFlash(
                "请将手机停留在选择语言界面，请勿触碰设备和数据线.",
                "Red");
            if (!await PrepareOujiaCowPartitionsAsync(fastbootSerial))
            {
                return;
            }

            if (isMediaTek)
            {
                if (!await FlashOujiaPlanInFastbootdAsync(plan))
                {
                    return;
                }
            }
            else
            {
                fastbootSerial = await FlashOujiaQualcommStandardPlanAsync(
                    plan,
                    fastbootSerial);
                if (string.IsNullOrWhiteSpace(fastbootSerial))
                {
                    return;
                }
            }

            await ExecuteOujiaPostFlashActionsAsync(
                fastbootSerial,
                isMediaTek,
                plan);
        }

        private async Task ExecuteForceOujiaFlashWorkflowAsync()
        {
            OujiaFlashPlan? plan = CreateOujiaFlashPlan(
                includeAdditionalImages: true,
                additionalImageModeName: "强力线刷");
            if (plan == null)
            {
                return;
            }
            InitializeOujiaFlashPlan(plan);

            string? fastbootSerial = await PrepareOugaFastbootdDeviceAsync(
                requireExistingFastbootd: false);
            if (string.IsNullOrWhiteSpace(fastbootSerial))
            {
                return;
            }

            OujiaProcessorPlatform? platform =
                await DetectOujiaProcessorPlatformAsync(fastbootSerial);
            if (!platform.HasValue)
            {
                return;
            }

            if (!ValidateOujiaPartitionTableCompatibility(plan.Partitions))
            {
                return;
            }

            string? currentSlot = await EnsureOujiaAbSlotSupportedAsync(fastbootSerial);
            if (string.IsNullOrWhiteSpace(currentSlot))
            {
                return;
            }

            bool isMediaTek = platform.Value == OujiaProcessorPlatform.MediaTek;

            const string targetSlot = "a";
            if (!ValidateOujiaFlashTargetCollisions(plan, platform.Value, targetSlot))
            {
                return;
            }

            LogToOugaFlashDual("用户选择：", "Black", "强力线刷模式", "Red");
            IReadOnlyCollection<string> logicalPartitionNames =
                GetOujiaPlanLogicalPartitionNames(plan);
            IReadOnlyCollection<string> logicalTargetNames =
                GetOujiaPlanLogicalTargetNames(plan, targetSlot);

            if (currentSlot.Equals("b", StringComparison.OrdinalIgnoreCase))
            {
                // B槽仍是活动槽时，先安全写完A槽非逻辑分区；modem统一后置。
                LogToOugaFlash(
                    "请将手机停留在选择语言界面，请勿触碰设备和数据线.",
                    "Red");
                if (!await PrepareOujiaCowPartitionsAsync(fastbootSerial))
                {
                    return;
                }

                List<PartitionInfo> preSwitchNonLogicalPartitions = plan.Partitions
                    .Where(partition =>
                    {
                        string baseName = NormalizeOujiaPartitionBaseName(
                            partition.PartitionName);
                        return !OujiaLogicalPartitionNames.Contains(baseName) &&
                               !baseName.Equals("modem", StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();
                int fastbootdTaskCount = plan.Partitions.Count(partition =>
                    isMediaTek ||
                    !NormalizeOujiaPartitionBaseName(partition.PartitionName)
                        .Equals("modem", StringComparison.OrdinalIgnoreCase));
                LogToOugaFlash(
                    $"总结刷写任务，须在FastbootD模式下刷写{fastbootdTaskCount}个分区.");
                var preSwitchResult = await FlashOujiaPartitionsToSlotAsync(
                    preSwitchNonLogicalPartitions,
                    targetSlot,
                    plan);
                if (!preSwitchResult.Completed)
                {
                    return;
                }
                if (!preSwitchResult.AllSucceeded)
                {
                    LogToOugaFlash(
                        "A槽非逻辑分区存在刷写失败，已保留B槽活动并终止强力线刷.",
                        "Red");
                    MarkOujiaFlashFailure(
                        "切槽前A槽非逻辑分区刷写失败，已保留B槽",
                        fatal: true);
                    return;
                }

                if (!await ExecutePowerFlashProcess(
                        currentSlot,
                        logicalPartitionNames,
                        logicalTargetNames))
                {
                    if (!ShouldFinalizeOugaFlashStop())
                    {
                        MarkOujiaFlashFailure("强力线刷预处理失败，停止操作", fatal: true);
                    }
                    return;
                }

                if (!await FlashOujiaAdditionalImagesAsync(plan, "强力线刷"))
                {
                    return;
                }

                List<PartitionInfo> logicalPartitions = plan.Partitions
                    .Where(partition => OujiaLogicalPartitionNames.Contains(
                        NormalizeOujiaPartitionBaseName(partition.PartitionName)))
                    .ToList();
                var logicalFlashResult = await FlashOujiaPartitionsToSlotAsync(
                    logicalPartitions,
                    targetSlot,
                    plan);
                if (!logicalFlashResult.Completed)
                {
                    return;
                }

                fastbootSerial = await FlashForceDeferredModemAsync(
                    plan,
                    fastbootSerial,
                    isMediaTek);
                if (string.IsNullOrWhiteSpace(fastbootSerial))
                {
                    return;
                }
            }
            else
            {
                // 已在A槽时保持原有强力线刷顺序：先重构逻辑分区，再执行完整刷写。
                if (!await ExecutePowerFlashProcess(
                        currentSlot,
                        logicalPartitionNames,
                        logicalTargetNames))
                {
                    if (!ShouldFinalizeOugaFlashStop())
                    {
                        MarkOujiaFlashFailure("强力线刷预处理失败，停止操作", fatal: true);
                    }
                    return;
                }

                LogToOugaFlash(
                    "请将手机停留在选择语言界面，请勿触碰设备和数据线.",
                    "Red");
                if (!await PrepareOujiaCowPartitionsAsync(fastbootSerial) ||
                    !await FlashOujiaAdditionalImagesAsync(plan, "强力线刷"))
                {
                    return;
                }

                if (isMediaTek)
                {
                    if (!await FlashOujiaPlanInFastbootdAsync(plan))
                    {
                        return;
                    }
                }
                else
                {
                    fastbootSerial = await FlashOujiaQualcommStandardPlanAsync(
                        plan,
                        fastbootSerial);
                    if (string.IsNullOrWhiteSpace(fastbootSerial))
                    {
                        return;
                    }
                }
            }

            await ExecuteOujiaPostFlashActionsAsync(
                fastbootSerial,
                isMediaTek,
                plan);
        }

        private async Task ExecutePureFastbootdOujiaFlashWorkflowAsync()
        {
            OujiaFlashPlan? plan = CreateOujiaFlashPlan(
                includeAdditionalImages: true,
                additionalImageModeName: "仅FBD线刷");
            if (plan == null)
            {
                return;
            }
            InitializeOujiaFlashPlan(plan);

            string? fastbootSerial = await PrepareOugaFastbootdDeviceAsync(
                requireExistingFastbootd: true);
            if (string.IsNullOrWhiteSpace(fastbootSerial))
            {
                return;
            }

            OujiaProcessorPlatform? platform =
                await DetectOujiaProcessorPlatformAsync(fastbootSerial);
            if (!platform.HasValue)
            {
                return;
            }

            if (!ValidateOujiaPartitionTableCompatibility(plan.Partitions))
            {
                return;
            }

            string? currentSlot = await EnsureOujiaAbSlotSupportedAsync(fastbootSerial);
            if (string.IsNullOrWhiteSpace(currentSlot))
            {
                return;
            }

            if (platform.Value == OujiaProcessorPlatform.MediaTek)
            {
                LogToOugaFlash(
                    "仅FastbootD线刷不适用于联发科设备，已安全终止任务.",
                    "Red");
                MarkOujiaFlashFailure(
                    "联发科设备不支持仅FastbootD线刷",
                    fatal: true);
                return;
            }

            string targetSlot = currentSlot.Equals("a", StringComparison.OrdinalIgnoreCase)
                ? "b"
                : "a";
            if (!ValidateOujiaFlashTargetCollisions(plan, platform.Value, targetSlot))
            {
                return;
            }

            LogToOugaFlashDual("用户选择：", "Black", "仅FBD线刷模式", "Red");
            if (!await ExecutePureFBDProcess(
                    currentSlot,
                    targetSlot,
                    GetOujiaPlanLogicalPartitionNames(plan),
                    GetOujiaPlanLogicalTargetNames(plan, targetSlot)))
            {
                if (!ShouldFinalizeOugaFlashStop())
                {
                    MarkOujiaFlashFailure("仅FBD预处理失败，停止操作", fatal: true);
                }
                return;
            }

            LogToOugaFlash(
                "请将手机停留在选择语言界面，请勿触碰设备和数据线.",
                "Red");
            if (!await PrepareOujiaCowPartitionsAsync(fastbootSerial) ||
                !await FlashOujiaAdditionalImagesAsync(plan, "仅FBD线刷"))
            {
                return;
            }

            if (!await FlashOujiaPlanInFastbootdAsync(plan))
            {
                return;
            }

            await ExecuteOujiaPostFlashActionsAsync(
                fastbootSerial,
                isMediaTekDevice: false,
                plan);
        }

        private Task ExecuteAbOujiaFlashWorkflowAsync()
        {
            return ExecuteABFlashProcess();
        }

        private OujiaFlashPlan? CreateOujiaFlashPlan(
            bool includeAdditionalImages,
            string additionalImageModeName = "")
        {
            List<PartitionInfo> selectedPartitions = GetSelectedPartitions()
                .Where(partition =>
                    !string.IsNullOrWhiteSpace(partition.FilePath) &&
                    !partition.FilePath.Contains("(缺失)", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(partition.FilePath))
                .ToList();
            if (selectedPartitions.Count == 0)
            {
                LogToOugaFlash(
                    "错误：分区表中没有已勾选且文件有效的镜像",
                    "Red");
                MarkOujiaFlashFailure(
                    "没有可执行的分区刷写任务",
                    fatal: true);
                return null;
            }

            var additionalImages =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (includeAdditionalImages &&
                !TryLoadRequiredOujiaAdditionalImages(
                    additionalImageModeName,
                    out additionalImages))
            {
                return null;
            }

            if (includeAdditionalImages)
            {
                selectedPartitions = selectedPartitions
                    .Where(partition =>
                        !additionalImages.ContainsKey(
                            NormalizeOujiaPartitionBaseName(partition.PartitionName)))
                    .ToList();
            }

            long totalBytes =
                additionalImages.Values.Sum(path => new FileInfo(path).Length) +
                selectedPartitions.Sum(partition =>
                    new FileInfo(partition.FilePath).Length);
            if (totalBytes <= 0)
            {
                MarkOujiaFlashFailure(
                    "刷写任务镜像总大小无效",
                    fatal: true);
                return null;
            }

            return new OujiaFlashPlan(
                selectedPartitions,
                additionalImages,
                totalBytes,
                FormatFileSize(totalBytes));
        }

        private bool TryLoadRequiredOujiaAdditionalImages(
            string modeName,
            out Dictionary<string, string> additionalImages)
        {
            additionalImages =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string tmpDirectory =
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp");
            var missingImages = new List<string>();

            foreach (string partitionName in new[] { "my_company", "my_preload" })
            {
                string imagePath =
                    Path.Combine(tmpDirectory, $"{partitionName}.img");
                if (File.Exists(imagePath))
                {
                    additionalImages[partitionName] = imagePath;
                }
                else
                {
                    missingImages.Add($"tmp\\{partitionName}.img");
                }
            }

            if (missingImages.Count == 0)
            {
                return true;
            }

            string displayMode = string.IsNullOrWhiteSpace(modeName)
                ? "当前刷写模式"
                : modeName;
            LogToOugaFlashTriple(
                $"{displayMode}必须额外刷写 ",
                "Black",
                string.Join("、", missingImages),
                "Red",
                "，文件缺失，已终止任务.",
                "Red");
            MarkOujiaFlashFailure(
                $"{displayMode}缺少必须刷写的双my镜像",
                fatal: true);
            return false;
        }

        private void InitializeOujiaFlashPlan(OujiaFlashPlan plan)
        {
            ResetProgressBar();
            fastbootCompleteLog.Clear();
            _lastGlobalProgressBytes = 0;
            FlashProgressPercentage = 0;
            FlashProgressDetailText = $"0/{plan.TotalSizeText}";
            WriteFlashProgressFile(0, plan.TotalBytes);
        }

        private HashSet<string> GetOujiaPlanLogicalPartitionNames(OujiaFlashPlan plan)
        {
            var logicalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PartitionInfo partition in plan.Partitions)
            {
                string baseName =
                    NormalizeOujiaPartitionBaseName(partition.PartitionName);
                if (OujiaLogicalPartitionNames.Contains(baseName))
                {
                    logicalNames.Add(baseName);
                }
            }
            foreach (string partitionName in plan.AdditionalImages.Keys)
            {
                if (OujiaLogicalPartitionNames.Contains(partitionName))
                {
                    logicalNames.Add(partitionName);
                }
            }
            return logicalNames;
        }

        private HashSet<string> GetOujiaPlanLogicalTargetNames(
            OujiaFlashPlan plan,
            string defaultSlot)
        {
            var targetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PartitionInfo partition in plan.Partitions)
            {
                string baseName = NormalizeOujiaPartitionBaseName(partition.PartitionName);
                if (!OujiaLogicalPartitionNames.Contains(baseName))
                {
                    continue;
                }

                targetNames.Add(BuildOujiaRequestedTargetName(
                    partition.PartitionName,
                    defaultSlot));
            }

            foreach (string partitionName in plan.AdditionalImages.Keys)
            {
                string baseName = NormalizeOujiaPartitionBaseName(partitionName);
                if (OujiaLogicalPartitionNames.Contains(baseName))
                {
                    targetNames.Add(BuildOujiaRequestedTargetName(
                        partitionName,
                        defaultSlot));
                }
            }
            return targetNames;
        }

        private async Task<bool> PrepareOujiaCowPartitionsAsync(string deviceSerial)
        {
            LogStepBegin("解析COW快照分区");
            if (await DeleteCowPartitions(deviceSerial))
            {
                LogStepEndOk();
                LogOkStep("解析分区表");
                return true;
            }

            LogStepEndError();
            MarkOujiaFlashFailure(
                "解析或清理COW快照分区失败",
                fatal: true);
            return false;
        }

        private async Task<bool> FlashOujiaAdditionalImagesAsync(
            OujiaFlashPlan plan,
            string modeName)
        {
            if (plan.AdditionalImages.Count == 0)
            {
                return true;
            }

            LogToOugaFlashTriple(
                "用户选择的模式为：",
                "Black",
                modeName,
                "Red",
                "模式，需额外刷写分区…",
                "Black");

            string slot = await GetCurrentSlot();
            if (string.IsNullOrWhiteSpace(slot))
            {
                MarkOujiaFlashFailure(
                    "无法确认额外分区目标槽位",
                    fatal: true);
                return false;
            }

            foreach (KeyValuePair<string, string> image in plan.AdditionalImages)
            {
                if (!await FlashOujiaPlannedImageAsync(
                        image.Key,
                        image.Value,
                        slot,
                        plan,
                        new FileInfo(image.Value).Length,
                        "[写入镜像]"))
                {
                    MarkOujiaFlashFailure(
                        $"额外分区{image.Key}刷写失败");
                }

                if (ShouldFinalizeOugaFlashStop())
                {
                    return false;
                }
            }

            return true;
        }

        private async Task<bool> FlashOujiaPlanInFastbootdAsync(OujiaFlashPlan plan)
        {
            string slot = await GetCurrentSlot();
            if (string.IsNullOrWhiteSpace(slot))
            {
                MarkOujiaFlashFailure(
                    "无法确认FastbootD刷写目标槽位",
                    fatal: true);
                return false;
            }

            List<PartitionInfo> orderedPartitions = plan.Partitions
                .OrderBy(partition => new FileInfo(partition.FilePath).Length)
                .ToList();
            LogToOugaFlash(
                $"总结刷写任务，须在FastbootD模式下刷写{orderedPartitions.Count}个分区.");

            foreach (PartitionInfo partition in orderedPartitions)
            {
                if (ShouldStopOugaFlashBeforeNextPartition(partition.PartitionName))
                {
                    return false;
                }

                await FlashOujiaPlannedImageAsync(
                    partition.PartitionName,
                    partition.FilePath,
                    slot,
                    plan,
                    new FileInfo(partition.FilePath).Length,
                    "[写入镜像]");
            }
            return !ShouldFinalizeOugaFlashStop();
        }

        private async Task<(bool Completed, bool AllSucceeded)> FlashOujiaPartitionsToSlotAsync(
            IEnumerable<PartitionInfo> partitions,
            string targetSlot,
            OujiaFlashPlan plan)
        {
            bool allSucceeded = true;
            foreach (PartitionInfo partition in partitions
                         .OrderBy(item => new FileInfo(item.FilePath).Length))
            {
                if (ShouldStopOugaFlashBeforeNextPartition(partition.PartitionName))
                {
                    return (false, allSucceeded);
                }

                bool succeeded = await FlashOujiaPlannedImageAsync(
                    partition.PartitionName,
                    partition.FilePath,
                    targetSlot,
                    plan,
                    new FileInfo(partition.FilePath).Length,
                    "[写入镜像]");
                allSucceeded &= succeeded;
            }

            return (!ShouldFinalizeOugaFlashStop(), allSucceeded);
        }

        private async Task<string?> FlashForceDeferredModemAsync(
            OujiaFlashPlan plan,
            string fastbootSerial,
            bool isMediaTek)
        {
            List<PartitionInfo> modemPartitions = plan.Partitions
                .Where(partition => NormalizeOujiaPartitionBaseName(partition.PartitionName)
                    .Equals("modem", StringComparison.OrdinalIgnoreCase))
                .OrderBy(partition => new FileInfo(partition.FilePath).Length)
                .ToList();
            if (modemPartitions.Count == 0)
            {
                return fastbootSerial;
            }

            if (!isMediaTek)
            {
                if (ShouldStopOugaFlashBeforeNextPartition("重启到Fastboot刷写modem") ||
                    !await RebootOugaDeviceAsync(fastbootSerial, toFastbootd: false))
                {
                    return null;
                }

                string? bootloaderSerial = await WaitForOugaFastbootDeviceAsync(
                    "Fastboot",
                    fastbootSerial,
                    requireFastbootd: false);
                if (string.IsNullOrWhiteSpace(bootloaderSerial))
                {
                    return null;
                }
                fastbootSerial = bootloaderSerial;
            }

            foreach (PartitionInfo modemPartition in modemPartitions)
            {
                long imageBytes = new FileInfo(modemPartition.FilePath).Length;
                bool isSlotless = IsOujiaKnownSlotlessPartition(
                    modemPartition.PartitionName);
                if (isMediaTek || isSlotless)
                {
                    if (ShouldStopOugaFlashBeforeNextPartition("modem"))
                    {
                        return null;
                    }
                    await FlashOujiaPlannedImageAsync(
                        modemPartition.PartitionName,
                        modemPartition.FilePath,
                        "a",
                        plan,
                        imageBytes,
                        "[写入镜像]");
                    continue;
                }

                long slotABytes = imageBytes / 2;
                long slotBBytes = imageBytes - slotABytes;
                if (ShouldStopOugaFlashBeforeNextPartition("modem_a"))
                {
                    return null;
                }
                await FlashOujiaPlannedImageAsync(
                    modemPartition.PartitionName,
                    modemPartition.FilePath,
                    "a",
                    plan,
                    slotABytes,
                    "[写入镜像]");

                if (ShouldStopOugaFlashBeforeNextPartition("modem_b"))
                {
                    return null;
                }
                await FlashOujiaPlannedImageAsync(
                    modemPartition.PartitionName,
                    modemPartition.FilePath,
                    "b",
                    plan,
                    slotBBytes,
                    "[写入镜像]");
            }

            return ShouldFinalizeOugaFlashStop() ? null : fastbootSerial;
        }

        private async Task<string?> FlashOujiaQualcommStandardPlanAsync(
            OujiaFlashPlan plan,
            string fastbootSerial)
        {
            List<PartitionInfo> modemPartitions = plan.Partitions
                .Where(partition =>
                    NormalizeOujiaPartitionBaseName(partition.PartitionName)
                        .Equals("modem", StringComparison.OrdinalIgnoreCase) &&
                    !IsOujiaKnownSlotlessPartition(partition.PartitionName))
                .ToList();
            List<PartitionInfo> fastbootdPartitions = plan.Partitions
                .Except(modemPartitions)
                .OrderBy(partition => new FileInfo(partition.FilePath).Length)
                .ToList();

            string slot = await GetCurrentSlot();
            if (string.IsNullOrWhiteSpace(slot))
            {
                MarkOujiaFlashFailure(
                    "无法确认FastbootD刷写目标槽位",
                    fatal: true);
                return null;
            }

            LogToOugaFlash(
                $"总结刷写任务，须在FastbootD模式下刷写{fastbootdPartitions.Count}个分区.");
            foreach (PartitionInfo partition in fastbootdPartitions)
            {
                if (ShouldStopOugaFlashBeforeNextPartition(partition.PartitionName))
                {
                    return null;
                }

                await FlashOujiaPlannedImageAsync(
                    partition.PartitionName,
                    partition.FilePath,
                    slot,
                    plan,
                    new FileInfo(partition.FilePath).Length,
                    "[写入镜像]");
            }

            if (modemPartitions.Count == 0)
            {
                return ShouldFinalizeOugaFlashStop() ? null : fastbootSerial;
            }

            if (ShouldStopOugaFlashBeforeNextPartition("重启到Fastboot刷写modem") ||
                !await RebootOugaDeviceAsync(fastbootSerial, toFastbootd: false))
            {
                return null;
            }

            string? bootloaderSerial = await WaitForOugaFastbootDeviceAsync(
                "Fastboot",
                fastbootSerial,
                requireFastbootd: false);
            if (string.IsNullOrWhiteSpace(bootloaderSerial))
            {
                return null;
            }
            fastbootSerial = bootloaderSerial;

            foreach (PartitionInfo modemPartition in modemPartitions)
            {
                long imageBytes = new FileInfo(modemPartition.FilePath).Length;
                long slotABytes = imageBytes / 2;
                long slotBBytes = imageBytes - slotABytes;

                if (ShouldStopOugaFlashBeforeNextPartition("modem_a"))
                {
                    return null;
                }
                await FlashOujiaPlannedImageAsync(
                    modemPartition.PartitionName,
                    modemPartition.FilePath,
                    "a",
                    plan,
                    slotABytes,
                    "[写入镜像]");

                if (ShouldStopOugaFlashBeforeNextPartition("modem_b"))
                {
                    return null;
                }
                await FlashOujiaPlannedImageAsync(
                    modemPartition.PartitionName,
                    modemPartition.FilePath,
                    "b",
                    plan,
                    slotBBytes,
                    "[写入镜像]");
            }

            return ShouldFinalizeOugaFlashStop() ? null : fastbootSerial;
        }

        private async Task<bool> FlashOujiaPlannedImageAsync(
            string partitionName,
            string imagePath,
            string slot,
            OujiaFlashPlan plan,
            long progressBytes,
            string operationLabel)
        {
            string deviceSerial = GetOujiaFlashTargetSerial();
            string? targetPartition = await ResolveOujiaTargetPartitionNameAsync(
                partitionName,
                slot,
                deviceSerial);
            string displayedTarget = targetPartition ??
                BuildOujiaRequestedTargetName(partitionName, slot);
            LogStepBeginNoTime(
                $"{operationLabel} {Path.GetFileName(imagePath)}  ->{displayedTarget}.img");

            long imageBytes = new FileInfo(imagePath).Length;
            long bytesBeforeImage = _lastGlobalProgressBytes;
            Action<long> progress = transferredBytes =>
            {
                long scaledBytes = imageBytes <= 0
                    ? 0
                    : (long)Math.Min(
                        progressBytes,
                        transferredBytes * ((double)progressBytes / imageBytes));
                long total = Math.Min(
                    bytesBeforeImage + scaledBytes,
                    plan.TotalBytes);
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() =>
                    {
                        if (total > _lastGlobalProgressBytes)
                        {
                            _lastGlobalProgressBytes = total;
                        }
                        FlashProgressPercentage = plan.TotalBytes > 0
                            ? Math.Min(
                                100.0,
                                (double)_lastGlobalProgressBytes / plan.TotalBytes * 100.0)
                            : 0;
                        FlashProgressDetailText =
                            $"{FormatFileSize(_lastGlobalProgressBytes)}/{plan.TotalSizeText}";
                        WriteFlashProgressFile(
                            _lastGlobalProgressBytes,
                            plan.TotalBytes);
                    }));
            };

            bool success = await FlashPartitionToSlot(
                partitionName,
                imagePath,
                slot,
                progress);
            long completedBytes = Math.Min(
                bytesBeforeImage + progressBytes,
                plan.TotalBytes);
            if (completedBytes > _lastGlobalProgressBytes)
            {
                _lastGlobalProgressBytes = completedBytes;
            }
            FlashProgressPercentage = plan.TotalBytes > 0
                ? Math.Min(
                    100.0,
                    (double)_lastGlobalProgressBytes / plan.TotalBytes * 100.0)
                : 0;
            FlashProgressDetailText =
                $"{FormatFileSize(_lastGlobalProgressBytes)}/{plan.TotalSizeText}";
            WriteFlashProgressFile(
                _lastGlobalProgressBytes,
                plan.TotalBytes);

            if (success)
            {
                LogStepEndOk();
            }
            else
            {
                LogStepEndError();
            }
            return success;
        }

        private bool ShouldExecuteOujiaClearData()
        {
            return _activeOujiaFlashSession != null &&
                   ClearDataCheckBox.IsChecked == true;
        }

        private bool ShouldExecuteOujiaAutoReboot()
        {
            return _activeOujiaFlashSession != null &&
                   AutoRebootOugaCheckBox.IsChecked == true;
        }

        private async Task ExecuteOujiaPostFlashActionsAsync(
            string fastbootSerial,
            bool isMediaTekDevice,
            OujiaFlashPlan plan)
        {
            if (ShouldFinalizeOugaFlashStop())
            {
                return;
            }

            LogOugaFlashPartitionResultSummary();
            bool cancelPureFastbootdAutoReboot =
                _activeOujiaFlashSession?.Mode == OujiaFlashMode.PureFastbootd &&
                _ougaFlashPartitionFailureCount > 0;
            if (cancelPureFastbootdAutoReboot)
            {
                _activeOujiaFlashSession!.AutoRebootCancelledForPartitionFailure = true;
                Dispatcher.Invoke(() => AutoRebootOugaCheckBox.IsChecked = false);
            }

            string fastbootPath = GetOugaFastbootExecutablePath();
            if (string.IsNullOrWhiteSpace(fastbootPath) || !File.Exists(fastbootPath))
            {
                MarkOujiaFlashFailure(
                    "收尾操作时缺少fastboot.exe",
                    fatal: true);
                return;
            }

            // FRP必须在FastbootD模式下擦除。高通modem刷写结束后设备通常停在
            // Bootloader Fastboot，因此所有收尾操作先统一确认并切回FastbootD。
            if (!await IsOugaFastbootdAsync(fastbootSerial))
            {
                if (!await RebootOugaDeviceAsync(fastbootSerial, toFastbootd: true))
                {
                    return;
                }

                string? fastbootdSerial = await WaitForOugaFastbootDeviceAsync(
                    "FastbootD",
                    fastbootSerial,
                    requireFastbootd: true);
                if (string.IsNullOrWhiteSpace(fastbootdSerial))
                {
                    return;
                }
                fastbootSerial = fastbootdSerial;

                if (!await EnsureOugaFastbootConnectionStableAsync(
                        fastbootSerial,
                        requireFastbootd: true))
                {
                    return;
                }
            }

            // 所有全量包模式统一在FastbootD中先擦除FRP，再按用户当前选择清除数据。
            LogStepBegin("擦除FRP");
            var frpResult = await ExecuteOugaFastbootCommandAsync(
                fastbootPath,
                fastbootSerial,
                "erase frp");
            if (frpResult.Success)
            {
                LogStepEndOk();
                AppendOugaFastbootNativeOutput(frpResult.Output);
            }
            else
            {
                LogStepEndError();
                AppendOugaFastbootFailureOutput(frpResult.Output);
                MarkOujiaFlashFailure("擦除FRP失败");
            }

            if (ShouldFinalizeOugaFlashStop())
            {
                return;
            }

            if (_activeOujiaFlashSession?.ClearDataRequested == true &&
                !ShouldExecuteOujiaClearData())
            {
                LogToOugaFlash("用户已取消清除数据，本次任务将跳过该步骤.", "Orange");
            }
            else if (ShouldExecuteOujiaClearData())
            {
                LogStepBegin("清除手机数据");
                bool clearDataSuccess = await ExecuteOugaClearDataAsync(
                    isMediaTekDevice,
                    fastbootPath,
                    fastbootSerial);
                if (clearDataSuccess)
                {
                    LogStepEndOk();
                }
                else
                {
                    LogStepEndError();
                    MarkOujiaFlashFailure("清除手机数据失败");
                }
            }

            if (ShouldFinalizeOugaFlashStop())
            {
                return;
            }

            if (_activeOujiaFlashSession?.AutoRebootCancelledForPartitionFailure == true)
            {
                // 仅FBD模式任意分区失败时强制保留FastbootD，最终完成日志统一说明原因。
            }
            else if (_activeOujiaFlashSession?.AutoRebootRequested == true &&
                !ShouldExecuteOujiaAutoReboot())
            {
                LogToOugaFlash("用户已取消自动重启，本次任务将保留当前设备模式.", "Orange");
            }
            else if (ShouldExecuteOujiaAutoReboot())
            {
                LogStepBegin("自动重启设备");
                var rebootResult = await ExecuteOugaFastbootCommandAsync(
                    fastbootPath,
                    fastbootSerial,
                    "reboot");
                if (rebootResult.Success)
                {
                    LogStepEndOk();
                    AppendOugaFastbootNativeOutput(rebootResult.Output);
                }
                else
                {
                    LogStepEndError();
                    AppendOugaFastbootFailureOutput(rebootResult.Output);
                    MarkOujiaFlashFailure("自动重启设备失败");
                }

                LogStepBegin("结束fastboot.exe进程");
                await Task.Delay(3000);
                await KillAllFastbootProcesses();
                LogStepEndOk();
            }

            _lastGlobalProgressBytes = plan.TotalBytes;
            FlashProgressPercentage = 100;
            FlashProgressDetailText =
                $"{plan.TotalSizeText}/{plan.TotalSizeText}";
            WriteFlashProgressFile(plan.TotalBytes, plan.TotalBytes);
            if (_activeOujiaFlashSession != null)
            {
                _activeOujiaFlashSession.WorkflowCompleted = true;
            }
        }


        private List<PartitionInfo> GetSelectedPartitions()
        {
            var selectedPartitions = new List<PartitionInfo>();
            if (OugaPartitionTableDataGrid.ItemsSource is IEnumerable<PartitionInfo> partitionInfos)
            {
                selectedPartitions = partitionInfos.Where(p => p.IsSelected).ToList();
            }
            return selectedPartitions;
        }

        private void BeginOugaFlashPartitionResultTracking()
        {
            _ougaFlashPartitionSuccessCount = 0;
            _ougaFlashPartitionFailureCount = 0;
            _ougaFlashFailedPartitionTargets.Clear();
            _trackOugaFlashPartitionResults = true;
        }

        private bool RecordOugaFlashPartitionResult(
            string partitionName,
            string slot,
            bool success)
        {
            string targetName = string.IsNullOrWhiteSpace(slot)
                ? partitionName
                : BuildOujiaRequestedTargetName(partitionName, slot);
            return RecordOugaFlashPartitionTargetResult(targetName, success);
        }

        private bool RecordOugaFlashPartitionTargetResult(string targetName, bool success)
        {
            if (!_trackOugaFlashPartitionResults)
            {
                return success;
            }

            if (success)
            {
                _ougaFlashPartitionSuccessCount++;
            }
            else
            {
                _ougaFlashPartitionFailureCount++;
                _ougaFlashFailedPartitionTargets.Add(targetName);
            }

            return success;
        }

        private void LogOugaFlashPartitionResultSummary()
        {
            if (_activeOujiaFlashSession?.PartitionSummaryLogged == true)
            {
                return;
            }

            if (_activeOujiaFlashSession != null)
            {
                _activeOujiaFlashSession.PartitionSummaryLogged = true;
            }

            LogToOugaFlashTriple(
                "刷写任务总结：成功 ",
                "Black",
                _ougaFlashPartitionSuccessCount.ToString(),
                "Green",
                $" 个，失败 {_ougaFlashPartitionFailureCount} 个.",
                _ougaFlashPartitionFailureCount > 0 ? "Red" : "Black");

            if (_ougaFlashPartitionFailureCount > 0)
            {
                string failedTargets = string.Join(
                    "、",
                    _ougaFlashFailedPartitionTargets.Distinct(StringComparer.OrdinalIgnoreCase));
                LogToOugaFlashDual("刷写失败分区：", "Black", failedTargets, "Red");
            }
        }

        private void LogOugaFlashCompletionResult()
        {
            if (_activeOujiaFlashSession == null)
            {
                if (_ougaFlashPartitionFailureCount > 0)
                {
                    return;
                }

                LogToOugaFlashDual(
                    "线刷流程结束：",
                    "Black",
                    "所有分区刷写成功！",
                    "Green");
                return;
            }

            if (_activeOujiaFlashSession.CompletionLogged)
            {
                return;
            }

            _activeOujiaFlashSession.CompletionLogged = true;
            string resultText;
            string resultColor;
            if (_activeOujiaFlashSession.WorkflowCompleted &&
                _activeOujiaFlashSession.Mode == OujiaFlashMode.PureFastbootd &&
                _activeOujiaFlashSession.AutoRebootCancelledForPartitionFailure &&
                _ougaFlashPartitionFailureCount > 0)
            {
                string failedPartitionDescription = _ougaFlashPartitionFailureCount == 1
                    ? "有一个分区刷写失败"
                    : $"有{_ougaFlashPartitionFailureCount}个分区刷写失败";
                resultText =
                    $"所有任务执行完成；{failedPartitionDescription}，为了防止变砖，已自动取消自动重启!";
                resultColor = "Red";
            }
            else switch (_activeOujiaFlashSession.Status)
            {
                case OujiaFlashRunStatus.Success:
                    resultText = "所有任务执行成功！";
                    resultColor = "Green";
                    break;
                case OujiaFlashRunStatus.PartialFailure:
                case OujiaFlashRunStatus.Failed:
                    return;
                case OujiaFlashRunStatus.Stopped:
                    resultText = "任务已按用户请求停止.";
                    resultColor = "Orange";
                    break;
                case OujiaFlashRunStatus.Cancelled:
                    resultText = "任务已取消.";
                    resultColor = "Orange";
                    break;
                default:
                    return;
            }

            LogToOugaFlashDual("线刷流程结束：", "Black", resultText, resultColor);
        }

        private bool DetectScatterPackIsMediaTek(string scatterPackPath)
        {
            if (string.IsNullOrWhiteSpace(scatterPackPath))
            {
                return false;
            }

            string[] candidatePaths =
            {
                Path.Combine(scatterPackPath, "lk.img"),
                Path.Combine(scatterPackPath, "IMAGES", "lk.img"),
                Path.Combine(scatterPackPath, "RADIO", "lk.img")
            };

            return candidatePaths.Any(File.Exists);
        }

        private async Task<bool> ExecuteOugaClearDataAsync(
            bool isMediaTekDevice,
            string fastbootPath,
            string deviceSerial = "")
        {
            if (string.IsNullOrWhiteSpace(fastbootPath))
            {
                return false;
            }

            var eraseUserdataResult = await ExecuteOugaFastbootCommandAsync(
                fastbootPath,
                deviceSerial,
                "erase userdata");
            bool eraseUserdataSuccess = eraseUserdataResult.Success;
            if (!eraseUserdataSuccess)
            {
                AppendOugaFastbootFailureOutput(eraseUserdataResult.Output);
            }

            var eraseMetadataResult = await ExecuteOugaFastbootCommandAsync(
                fastbootPath,
                deviceSerial,
                "erase metadata");
            bool eraseMetadataSuccess = eraseMetadataResult.Success;
            if (!eraseMetadataSuccess)
            {
                AppendOugaFastbootFailureOutput(eraseMetadataResult.Output);
            }

            if (isMediaTekDevice)
            {
                return eraseUserdataSuccess && eraseMetadataSuccess;
            }

            var wipeResult = await ExecuteOugaFastbootCommandAsync(
                fastbootPath,
                deviceSerial,
                "-w");
            bool wipeWSuccess = wipeResult.Success;
            if (!wipeWSuccess)
            {
                AppendOugaFastbootFailureOutput(wipeResult.Output);
            }

            string programDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string miscFilePath = Path.Combine(programDirectory, "tmp", "misc.img");
            bool miscSuccess = true;
            if (File.Exists(miscFilePath))
            {
                var miscResult = await ExecuteOugaFastbootCommandAsync(
                    fastbootPath,
                    deviceSerial,
                    $"flash misc \"{miscFilePath}\"");
                miscSuccess = miscResult.Success;
                if (!miscSuccess)
                {
                    AppendOugaFastbootFailureOutput(miscResult.Output);
                }
            }

            bool dataEraseSuccess = wipeWSuccess || (eraseUserdataSuccess && eraseMetadataSuccess);
            return dataEraseSuccess && miscSuccess;
        }

        private string GetOugaFastbootExecutablePath()
        {
            string bundledPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "platform-tools",
                "fastboot.exe");
            return File.Exists(bundledPath) ? bundledPath : GetFastbootPath();
        }

        private static string BuildOugaFastbootArguments(string deviceSerial, string arguments)
        {
            return string.IsNullOrWhiteSpace(deviceSerial)
                ? arguments
                : $"-s \"{deviceSerial.Replace("\"", string.Empty)}\" {arguments}";
        }

        private async Task<string> GetOugaFastbootOutputAsync(
            string deviceSerial,
            string arguments,
            int timeoutSeconds = 10)
        {
            string fastbootPath = GetOugaFastbootExecutablePath();
            if (string.IsNullOrWhiteSpace(fastbootPath) || !File.Exists(fastbootPath))
            {
                return string.Empty;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                return await GetCommandOutput(
                    fastbootPath,
                    BuildOugaFastbootArguments(deviceSerial, arguments),
                    timeout.Token);
            }
            catch (OperationCanceledException)
            {
                return string.Empty;
            }
        }

        private async Task<List<string>> GetConnectedOugaFastbootSerialsAsync()
        {
            string output = await GetOugaFastbootOutputAsync(string.Empty, "devices", 5);
            return Regex.Matches(
                    output ?? string.Empty,
                    @"^\s*(?<serial>\S+)\s+fastboot\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline)
                .Cast<Match>()
                .Select(match => match.Groups["serial"].Value.Trim())
                .Where(serial => !string.IsNullOrWhiteSpace(serial))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private (Paragraph Paragraph, Run ResultRun) BeginOugaDeviceWaitLog(
            string modeName,
            string expectedSerial,
            int timeoutSeconds,
            string? customTitle = null)
        {
            Paragraph? paragraph = null;
            Run? resultRun = null;
            Dispatcher.Invoke(() =>
            {
                paragraph = CreateOugaFlashLogParagraph();
                AppendOugaFlashTimestamp(paragraph);
                string title = string.IsNullOrWhiteSpace(customTitle)
                    ? $"等待{modeName}设备连接..."
                    : customTitle;
                AppendOugaFlashStyledText(paragraph, title, "Black");
                resultRun = new Run($"{timeoutSeconds}s")
                {
                    Foreground = GetOugaFlashLogBrush("Blue"),
                    FontWeight = FontWeights.SemiBold
                };
                paragraph.Inlines.Add(resultRun);
                OugaFlashLogTextBox.Document.Blocks.Add(paragraph);
                OugaFlashLogTextBox.ScrollToEnd();
            });
            return (paragraph!, resultRun!);
        }

        private void CompleteOugaDeviceWaitLog(
            Run resultRun,
            string result,
            bool success)
        {
            Dispatcher.Invoke(() =>
            {
                resultRun.Text = result;
                resultRun.Foreground = GetOugaFlashLogBrush(success ? "Green" : "Red");
                resultRun.FontWeight = FontWeights.Bold;
                OugaFlashLogTextBox.ScrollToEnd();
            });
        }

        private void UpdateOugaDeviceWaitLog(Run resultRun, string result)
        {
            Dispatcher.Invoke(() =>
            {
                resultRun.Text = result;
                resultRun.Foreground = GetOugaFlashLogBrush("Blue");
                resultRun.FontWeight = FontWeights.SemiBold;
                OugaFlashLogTextBox.ScrollToEnd();
            });
        }

        private const int OujiaFastbootDeviceWaitTimeoutSeconds = 120;
        private const int OujiaFastbootStabilityTimeoutSeconds = 30;
        private const int OujiaFastbootStableProbeCount = 3;

        private async Task<string?> WaitForOugaFastbootDeviceAsync(
            string modeName,
            string expectedSerial = "",
            bool? requireFastbootd = null,
            int timeoutSeconds = OujiaFastbootDeviceWaitTimeoutSeconds,
            string? customTitle = null,
            bool showSerialWhenUnbound = true)
        {
            var waitLog = BeginOugaDeviceWaitLog(
                modeName,
                expectedSerial,
                timeoutSeconds,
                customTitle);
            var stopwatch = Stopwatch.StartNew();
            string selectedSerial = GetOujiaFlashTargetSerial();
            int displayedRemainingSeconds = timeoutSeconds;

            while (stopwatch.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
            {
                if (_ougaFlashStopRequested)
                {
                    CompleteOugaDeviceWaitLog(waitLog.ResultRun, "已停止.", false);
                    MarkOujiaFlashStopped();
                    return null;
                }

                Task<(string Serial, bool ModeMatches)> probeTask = Task.Run(async () =>
                {
                    List<string> connectedSerials = await GetConnectedOugaFastbootSerialsAsync();
                    string serial = string.Empty;
                    if (!string.IsNullOrWhiteSpace(expectedSerial))
                    {
                        serial = connectedSerials.FirstOrDefault(item =>
                            item.Equals(expectedSerial, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
                    }
                    else if (!string.IsNullOrWhiteSpace(selectedSerial))
                    {
                        serial = connectedSerials.FirstOrDefault(item =>
                            item.Equals(selectedSerial, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
                    }

                    // 任务一旦绑定了设备序列号，只等待同一台设备重新出现，绝不静默改绑。
                    if (string.IsNullOrWhiteSpace(serial) &&
                        string.IsNullOrWhiteSpace(expectedSerial) &&
                        string.IsNullOrWhiteSpace(selectedSerial) &&
                        connectedSerials.Count == 1)
                    {
                        serial = connectedSerials[0];
                    }

                    bool modeMatches = true;
                    if (!string.IsNullOrWhiteSpace(serial) && requireFastbootd.HasValue)
                    {
                        bool isFastbootd = await IsOugaFastbootdAsync(serial);
                        modeMatches = isFastbootd == requireFastbootd.Value;
                    }

                    return (serial, modeMatches);
                });

                while (!probeTask.IsCompleted &&
                       stopwatch.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
                {
                    if (_ougaFlashStopRequested)
                    {
                        CompleteOugaDeviceWaitLog(waitLog.ResultRun, "已停止.", false);
                        MarkOujiaFlashStopped();
                        return null;
                    }

                    int remainingSeconds = Math.Max(
                        0,
                        timeoutSeconds - (int)Math.Floor(stopwatch.Elapsed.TotalSeconds));
                    if (remainingSeconds != displayedRemainingSeconds)
                    {
                        displayedRemainingSeconds = remainingSeconds;
                        UpdateOugaDeviceWaitLog(waitLog.ResultRun, $"{remainingSeconds}s");
                    }

                    await Task.WhenAny(probeTask, Task.Delay(200));
                }

                if (probeTask.IsCompleted)
                {
                    var probeResult = await probeTask;
                    if (!string.IsNullOrWhiteSpace(probeResult.Serial) && probeResult.ModeMatches)
                    {
                        string result = string.IsNullOrWhiteSpace(expectedSerial) &&
                                        showSerialWhenUnbound
                            ? $"[{probeResult.Serial}]"
                            : "OK";
                        CompleteOugaDeviceWaitLog(waitLog.ResultRun, result, true);
                        UpdateOujiaFlashSessionDevice(probeResult.Serial);
                        return probeResult.Serial;
                    }
                }

                await Task.Delay(200);
            }

            CompleteOugaDeviceWaitLog(waitLog.ResultRun, "等待超时.", false);
            MarkOujiaFlashFailure($"等待{modeName}设备连接超时", fatal: true);
            return null;
        }

        private async Task<bool> EnsureOugaFastbootConnectionStableAsync(
            string deviceSerial,
            string logTitle = "等待设备连接稳定...",
            bool? requireFastbootd = null,
            int timeoutSeconds = OujiaFastbootStabilityTimeoutSeconds,
            int requiredConsecutiveSuccesses = OujiaFastbootStableProbeCount)
        {
            Paragraph? paragraph = null;
            Run? countdownRun = null;
            Dispatcher.Invoke(() =>
            {
                paragraph = CreateOugaFlashLogParagraph();
                AppendOugaFlashTimestamp(paragraph);
                AppendOugaFlashStyledText(paragraph, logTitle, "Black");
                countdownRun = new Run($"{timeoutSeconds}s")
                {
                    Foreground = GetOugaFlashLogBrush("Blue"),
                    FontWeight = FontWeights.SemiBold
                };
                paragraph.Inlines.Add(countdownRun);
                OugaFlashLogTextBox.Document.Blocks.Add(paragraph);
                OugaFlashLogTextBox.ScrollToEnd();
            });

            var stopwatch = Stopwatch.StartNew();
            int consecutiveSuccesses = 0;
            int displayedRemainingSeconds = timeoutSeconds;
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
            {
                if (_ougaFlashStopRequested)
                {
                    CompleteOugaDeviceWaitLog(countdownRun!, "已停止.", false);
                    MarkOujiaFlashStopped();
                    return false;
                }

                int remainingSeconds = Math.Max(
                    0,
                    timeoutSeconds - (int)Math.Floor(stopwatch.Elapsed.TotalSeconds));
                if (remainingSeconds != displayedRemainingSeconds)
                {
                    displayedRemainingSeconds = remainingSeconds;
                    UpdateOugaDeviceWaitLog(countdownRun!, $"{remainingSeconds}s");
                }

                List<string> connectedSerials =
                    await GetConnectedOugaFastbootSerialsAsync();
                bool sameDeviceConnected = connectedSerials.Any(serial =>
                    serial.Equals(deviceSerial, StringComparison.OrdinalIgnoreCase));
                consecutiveSuccesses = sameDeviceConnected
                    ? consecutiveSuccesses + 1
                    : 0;

                if (consecutiveSuccesses >= requiredConsecutiveSuccesses)
                {
                    bool modeMatches = true;
                    if (requireFastbootd.HasValue)
                    {
                        bool isFastbootd = await IsOugaFastbootdAsync(deviceSerial);
                        modeMatches = isFastbootd == requireFastbootd.Value;
                    }

                    if (modeMatches)
                    {
                        CompleteOugaDeviceWaitLog(countdownRun!, "OK", true);
                        return true;
                    }

                    consecutiveSuccesses = 0;
                }

                await Task.Delay(1000);
            }

            CompleteOugaDeviceWaitLog(countdownRun!, "等待超时.", false);
            MarkOujiaFlashFailure(
                $"设备[{deviceSerial}]连接稳定性检查超时",
                fatal: true);
            return false;
        }

        private static bool IsOugaFastbootTransportFailureOutput(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return true;
            }

            return output.Contains("Write to device failed", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("no link", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("Too many links", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("USB Error", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("device disconnected", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("device not found", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("status read failed", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("failed to read", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("failed to write", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("< waiting for", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<bool?> IsOugaFastbootUnlockedAsync(string deviceSerial)
        {
            string unlockedOutput = await GetOugaFastbootOutputAsync(deviceSerial, "getvar unlocked");
            string unlockedValue = ExtractFastbootVar(unlockedOutput, "unlocked");
            if (Regex.IsMatch(unlockedValue, @"^(?:yes|true|1)$", RegexOptions.IgnoreCase))
            {
                return true;
            }
            if (Regex.IsMatch(unlockedValue, @"^(?:no|false|0)$", RegexOptions.IgnoreCase))
            {
                return false;
            }

            // 链路已经失效时，继续执行备用命令不会恢复通信，也不能把未知状态
            // 误判为Bootloader未解锁。
            if (IsOugaFastbootTransportFailureOutput(unlockedOutput))
            {
                return null;
            }

            string deviceInfoOutput = await GetOugaFastbootOutputAsync(deviceSerial, "oem device-info");
            Match deviceUnlockedMatch = Regex.Match(
                deviceInfoOutput ?? string.Empty,
                @"Device\s+unlocked\s*:\s*(true|false)",
                RegexOptions.IgnoreCase);
            if (deviceUnlockedMatch.Success)
            {
                return deviceUnlockedMatch.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
            }

            return null;
        }

        private async Task<string> GetOugaFastbootSlotDisplayAsync(string deviceSerial)
        {
            string currentSlotOutput = await GetOugaFastbootOutputAsync(deviceSerial, "getvar current-slot");
            string currentSlot = ExtractFastbootVar(currentSlotOutput, "current-slot")
                .Trim()
                .TrimStart('_')
                .ToLowerInvariant();
            if (currentSlot == "a") return "A槽";
            if (currentSlot == "b") return "B槽";
            return "未知槽位";
        }

        private async Task<string?> EnsureOujiaAbSlotSupportedAsync(string deviceSerial)
        {
            string currentSlotOutput = await GetOugaFastbootOutputAsync(
                deviceSerial,
                "getvar current-slot",
                15);
            string currentSlot = ExtractFastbootVar(currentSlotOutput, "current-slot")
                .Trim()
                .TrimStart('_')
                .ToLowerInvariant();
            if (currentSlot is "a" or "b")
            {
                if (_activeOujiaFlashSession != null)
                {
                    _activeOujiaFlashSession.CurrentSlot = currentSlot;
                }
                return currentSlot;
            }

            string hasSlotOutput = await GetOugaFastbootOutputAsync(
                deviceSerial,
                "getvar has-slot:boot",
                15);
            string hasSlotValue = ExtractFastbootVar(hasSlotOutput, "has-slot:boot");
            if (Regex.IsMatch(hasSlotValue, @"^(?:no|false|0)$", RegexOptions.IgnoreCase))
            {
                LogToOugaFlash("当前全量包线刷流程仅支持A/B槽设备，已安全终止任务.", "Red");
                MarkOujiaFlashFailure("检测到非A/B槽设备", fatal: true);
                return null;
            }

            LogToOugaFlash("无法可靠读取设备当前启动槽位，已安全终止任务.", "Red");
            MarkOujiaFlashFailure("读取设备启动槽位失败", fatal: true);
            return null;
        }

        private async Task<OujiaProcessorPlatform?> DetectOujiaProcessorPlatformAsync(
            string deviceSerial)
        {
            LogStepBegin("解析分区表/判断处理器类型");
            string fastbootPath = GetOugaFastbootExecutablePath();
            var partitionTableResult = await ExecuteOugaFastbootCommandAsync(
                fastbootPath,
                deviceSerial,
                "getvar all",
                timeoutSeconds: 30);
            if (!partitionTableResult.Success ||
                string.IsNullOrWhiteSpace(partitionTableResult.Output))
            {
                LogStepEndError();
                AppendOugaFastbootFailureOutput(partitionTableResult.Output);
                LogToOugaFlash(
                    IsOugaFastbootTransportFailureOutput(partitionTableResult.Output)
                        ? "无法读取FastbootD分区表，已安全终止任务，此报错是由于你的数据线无法瞬时传输较大数据流信息导致，请更换质量更好的数据线然后重新进入FastbootD后重试."
                        : "无法读取FastbootD分区表，已安全终止任务.",
                    "Red");
                MarkOujiaFlashFailure("读取FastbootD分区表失败", fatal: true);
                return null;
            }

            string partitionTable = partitionTableResult.Output;

            if (_activeOujiaFlashSession != null)
            {
                _activeOujiaFlashSession.AvailablePartitions.Clear();
                _activeOujiaFlashSession.AvailablePartitions.UnionWith(
                    ParseOujiaAvailablePartitionNames(partitionTable));
            }

            bool isMediaTek = Regex.IsMatch(
                partitionTable,
                @"partition-(?:size|type):(?:lk|lk_a|lk_b)\s*:",
                RegexOptions.IgnoreCase);
            LogStepEndOk();

            OujiaProcessorPlatform platform = isMediaTek
                ? OujiaProcessorPlatform.MediaTek
                : OujiaProcessorPlatform.Qualcomm;

            LogStepBegin("校验刷写文件处理器类型");
            (bool packageHasLk, bool packageHasXbl) =
                GetOujiaPackageProcessorFeatureEvidence();

            if (packageHasLk && packageHasXbl)
            {
                LogStepEndError();
                LogToOugaFlash(
                    "已安全终止任务，用户选择的刷写文件中同时包含联发科特征文件lk和高通骁龙特征文件xbl，请使用正确的刷写文件.",
                    "Red");
                MarkOujiaFlashFailure(
                    "刷写文件同时包含lk和xbl",
                    fatal: true);
                return null;
            }

            if (platform == OujiaProcessorPlatform.Qualcomm && packageHasLk)
            {
                LogStepEndError();
                LogToOugaFlash(
                    "已安全终止任务，设备分区表检测结果为高通骁龙（设备连接不稳定会误判），但刷写文件中无高通特征文件xbl，刷写文件与设备处理器不匹配，若确定刷写文件与设备匹配，请检查数据线或连接接口后重试.",
                    "Red");
                MarkOujiaFlashFailure(
                    "设备分区表与刷写文件处理器类型不匹配",
                    "设备检测为高通骁龙，刷写文件仅包含lk",
                    fatal: true);
                return null;
            }

            if (platform == OujiaProcessorPlatform.MediaTek && packageHasXbl)
            {
                LogStepEndError();
                LogToOugaFlash(
                    "已安全终止任务，设备分区表检测结果为联发科（设备连接不稳定会误判），但刷写文件中无联发科特征文件lk，刷写文件与设备处理器不匹配，若确定刷写文件与设备匹配，请检查数据线或连接接口后重试.",
                    "Red");
                MarkOujiaFlashFailure(
                    "设备分区表与刷写文件处理器类型不匹配",
                    "设备检测为联发科，刷写文件仅包含xbl",
                    fatal: true);
                return null;
            }

            if (!packageHasLk && !packageHasXbl)
            {
                LogStepEndError();
                LogToOugaFlash(
                    "已安全终止任务，用户选择的刷写文件中不包含联发科特征文件lk和高通特征文件xbl，刷机包不完整.",
                    "Red");

                bool continueFlashing =
                    ShowOujiaIncompletePackageConfirmationDialog();
                if (!continueFlashing)
                {
                    MarkOujiaFlashFailure(
                        "刷写文件缺少处理器特征文件",
                        "未找到lk或xbl",
                        fatal: true);
                    return null;
                }

                LogToOugaFlash(
                    "用户已确认继续刷入，将继续使用设备分区表检测到的处理器类型.",
                    "Orange");
            }
            else
            {
                LogStepEndOk();
            }

            LogToOugaFlashDual(
                "设备处理器：",
                "Black",
                isMediaTek ? "联发科" : "高通骁龙",
                isMediaTek ? "Purple" : "Blue");
            return platform;
        }

        private (bool HasLk, bool HasXbl) GetOujiaPackageProcessorFeatureEvidence()
        {
            bool hasLk = false;
            bool hasXbl = false;

            if (OugaPartitionTableDataGrid?.ItemsSource is not
                IEnumerable<PartitionInfo> partitionInfos)
            {
                return (false, false);
            }

            foreach (PartitionInfo partition in partitionInfos)
            {
                string filePath = partition.FilePath?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(filePath) ||
                    filePath.Contains("(缺失)", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(filePath))
                {
                    continue;
                }

                string partitionFeatureName = NormalizeOujiaPartitionBaseName(
                    partition.PartitionName);
                string fileFeatureName = NormalizeOujiaPartitionBaseName(
                    Path.GetFileName(filePath));

                hasLk |= partitionFeatureName.Equals(
                             "lk",
                             StringComparison.OrdinalIgnoreCase) ||
                         fileFeatureName.Equals(
                             "lk",
                             StringComparison.OrdinalIgnoreCase);
                hasXbl |= partitionFeatureName.Equals(
                              "xbl",
                              StringComparison.OrdinalIgnoreCase) ||
                          fileFeatureName.Equals(
                              "xbl",
                              StringComparison.OrdinalIgnoreCase);

                if (hasLk && hasXbl)
                {
                    break;
                }
            }

            return (hasLk, hasXbl);
        }

        private bool ShowOujiaIncompletePackageConfirmationDialog()
        {
            if (!Dispatcher.CheckAccess())
            {
                return Dispatcher.Invoke(
                    ShowOujiaIncompletePackageConfirmationDialog);
            }

            var dialog = new Window
            {
                Title = "刷机包完整性提示",
                Owner = this,
                Width = 460,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Background = System.Windows.Media.Brushes.White,
                FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI")
            };

            var root = new StackPanel
            {
                Margin = new Thickness(24, 20, 24, 20)
            };
            root.Children.Add(new TextBlock
            {
                Text = "识别到刷机包可能不完整",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(38, 49, 66)),
                Margin = new Thickness(0, 0, 0, 12)
            });

            var warningCard = new Border
            {
                Background = new SolidColorBrush(MediaColor.FromRgb(250, 251, 253)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 12, 14, 12)
            };
            var warningContent = new StackPanel();
            warningContent.Children.Add(new TextBlock
            {
                Text = "用户选择的刷写文件中未找到联发科特征文件lk或高通骁龙特征文件xbl，是否继续刷入？",
                FontSize = 12,
                LineHeight = 19,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(51, 65, 85))
            });
            warningContent.Children.Add(new TextBlock
            {
                Text = "继续刷入将使用设备分区表检测到的处理器类型，请确认刷机包完整且与当前设备匹配.",
                FontSize = 11.5,
                LineHeight = 18,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(217, 119, 6)),
                Margin = new Thickness(0, 8, 0, 0)
            });
            warningCard.Child = warningContent;
            root.Children.Add(warningCard);

            var buttonPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };
            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "取消刷入",
                Width = 88,
                Height = 32,
                Margin = new Thickness(0, 0, 10, 0),
                Background = System.Windows.Media.Brushes.White,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(71, 85, 105)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(203, 213, 225)),
                IsCancel = true
            };
            var continueButton = new System.Windows.Controls.Button
            {
                Content = "继续刷入",
                Width = 98,
                Height = 32,
                Background = new SolidColorBrush(MediaColor.FromRgb(184, 118, 221)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(184, 118, 221)),
                Foreground = System.Windows.Media.Brushes.White,
                IsDefault = true
            };
            cancelButton.Click += (_, _) => dialog.DialogResult = false;
            continueButton.Click += (_, _) => dialog.DialogResult = true;
            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(continueButton);
            root.Children.Add(buttonPanel);

            dialog.Content = root;
            return dialog.ShowDialog() == true;
        }

        private static HashSet<string> ParseOujiaAvailablePartitionNames(
            string partitionTable)
        {
            var availablePartitions =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(partitionTable))
            {
                return availablePartitions;
            }

            foreach (Match partitionMatch in Regex.Matches(
                         partitionTable,
                         @"partition-(?:size|type):(?<name>[^:\s]+)\s*:",
                         RegexOptions.IgnoreCase))
            {
                string partitionName = partitionMatch.Groups["name"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(partitionName))
                {
                    availablePartitions.Add(partitionName);
                }
            }

            return availablePartitions;
        }

        private bool ValidateOujiaPartitionTableCompatibility(
            IEnumerable<PartitionInfo> flashingPartitions)
        {
            OujiaFlashSessionContext? session = _activeOujiaFlashSession;
            return ValidateOujiaPartitionTableCompatibility(
                flashingPartitions,
                session?.PartitionTableValidationRequested == true,
                session?.AvailablePartitions ??
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private bool ValidateOujiaPartitionTableCompatibility(
            IEnumerable<PartitionInfo> flashingPartitions,
            bool validationRequested,
            HashSet<string> availablePartitions)
        {
            if (!validationRequested)
            {
                return true;
            }

            HashSet<string> requiredNonLogicalPartitions = flashingPartitions
                .Where(partition =>
                    partition != null &&
                    !string.IsNullOrWhiteSpace(partition.FilePath) &&
                    !partition.FilePath.Contains("(缺失)", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(partition.FilePath))
                .Select(partition =>
                    NormalizeOujiaPartitionBaseName(partition.PartitionName))
                .Where(partitionName =>
                    !string.IsNullOrWhiteSpace(partitionName) &&
                    !OujiaLogicalPartitionNames.Contains(partitionName) &&
                    !partitionName.Equals("modem", StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<string> missingPartitions = requiredNonLogicalPartitions
                .Where(partitionName =>
                    !availablePartitions.Contains(partitionName) &&
                    !availablePartitions.Contains($"{partitionName}_a") &&
                    !availablePartitions.Contains($"{partitionName}_b"))
                .OrderBy(partitionName => partitionName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (missingPartitions.Count > 0)
            {
                LogToOugaFlashDual(
                    "分区表效验失败，",
                    "Red",
                    "刷写固件包与设备分区表不匹配，请检查刷机包是否正确，若刷机包正确，可关闭分区表效验功能后，重新点击开始线刷按钮.",
                    "Black");
                MarkOujiaFlashFailure(
                    "分区表效验失败",
                    $"设备缺少分区：{string.Join("、", missingPartitions)}",
                    fatal: true);
                return false;
            }

            LogToOugaFlashDual(
                "分区表效验通过，",
                "Green",
                "固件包与设备分区表匹配.",
                "Black");
            return true;
        }

        private static string NormalizeOujiaPartitionBaseName(string partitionName)
        {
            string normalized = Path.GetFileNameWithoutExtension(partitionName ?? string.Empty).Trim();
            return TryGetOujiaExplicitPartitionSlot(
                normalized,
                out string baseName,
                out _,
                out _)
                ? baseName
                : normalized;
        }

        private static bool TryGetOujiaExplicitPartitionSlot(
            string partitionName,
            out string baseName,
            out string explicitSlot,
            out string exactPartitionName)
        {
            exactPartitionName = Path.GetFileNameWithoutExtension(
                partitionName ?? string.Empty).Trim();
            Match match = Regex.Match(
                exactPartitionName,
                @"^(?<base>.+)_(?<slot>a|b)$",
                RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                baseName = exactPartitionName;
                explicitSlot = string.Empty;
                return false;
            }

            baseName = match.Groups["base"].Value;
            explicitSlot = match.Groups["slot"].Value.ToLowerInvariant();
            return !string.IsNullOrWhiteSpace(baseName);
        }

        private static string BuildOujiaRequestedTargetName(
            string partitionName,
            string slot)
        {
            // 镜像文件名末尾的_a/_b仅表示来源名称；实际写入槽位始终由当前流程决定。
            string normalizedSlot = slot.Trim().TrimStart('_').ToLowerInvariant();
            return $"{NormalizeOujiaPartitionBaseName(partitionName)}_{normalizedSlot}";
        }

        private async Task<bool> OujiaPartitionExistsAsync(string deviceSerial, string partitionName)
        {
            if (string.IsNullOrWhiteSpace(partitionName))
            {
                return false;
            }

            if (_activeOujiaFlashSession?.AvailablePartitions.Contains(partitionName) == true)
            {
                return true;
            }

            string output = await GetOugaFastbootOutputAsync(
                deviceSerial,
                $"getvar partition-size:{partitionName}",
                10);
            bool exists = !IsOugaFastbootFailureOutput(output) &&
                          Regex.IsMatch(
                              output,
                              $@"partition-size:{Regex.Escape(partitionName)}\s*:",
                              RegexOptions.IgnoreCase);
            if (exists && _activeOujiaFlashSession != null)
            {
                _activeOujiaFlashSession.AvailablePartitions.Add(partitionName);
            }
            return exists;
        }

        private async Task<string?> ResolveOujiaTargetPartitionNameAsync(
            string partitionName,
            string slot,
            string deviceSerial)
        {
            string baseName = NormalizeOujiaPartitionBaseName(partitionName);
            string normalizedSlot = slot.Trim().TrimStart('_').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(baseName) || normalizedSlot is not ("a" or "b"))
            {
                return null;
            }

            string slottedCandidate = $"{baseName}_{normalizedSlot}";
            if (await OujiaPartitionExistsAsync(deviceSerial, slottedCandidate))
            {
                return slottedCandidate;
            }

            if (await OujiaPartitionExistsAsync(deviceSerial, baseName))
            {
                return baseName;
            }

            return null;
        }

        private bool IsOujiaKnownSlotlessPartition(string partitionName)
        {
            string baseName = NormalizeOujiaPartitionBaseName(partitionName);
            HashSet<string>? available = _activeOujiaFlashSession?.AvailablePartitions;
            return available != null &&
                   available.Contains(baseName) &&
                   !available.Contains($"{baseName}_a") &&
                   !available.Contains($"{baseName}_b");
        }

        private bool ValidateOujiaFlashTargetCollisions(
            OujiaFlashPlan plan,
            OujiaProcessorPlatform platform,
            string targetSlot)
        {
            string normalizedTargetSlot = targetSlot.Trim().TrimStart('_').ToLowerInvariant();
            if (normalizedTargetSlot is not ("a" or "b"))
            {
                LogToOugaFlash("无法校验刷写目标：目标槽位无效.", "Red");
                MarkOujiaFlashFailure("刷写目标槽位无效", fatal: true);
                return false;
            }

            OujiaFlashMode mode = _activeOujiaFlashSession?.Mode ?? OujiaFlashMode.Normal;
            var targetSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            bool RegisterTarget(string partitionName, string imagePath, string requestedSlot)
            {
                string baseName = NormalizeOujiaPartitionBaseName(partitionName);
                string targetName = IsOujiaKnownSlotlessPartition(baseName)
                    ? baseName
                    : $"{baseName}_{requestedSlot}";

                string sourceName = Path.GetFileName(imagePath);
                if (targetSources.TryGetValue(targetName, out string? existingSource))
                {
                    LogToOugaFlashDual(
                        "目标分区冲突：",
                        "Black",
                        $"{existingSource} 与 {sourceName} 都将写入 {targetName}.img，已安全终止任务.",
                        "Red");
                    MarkOujiaFlashFailure(
                        $"多个镜像映射到目标分区{targetName}",
                        fatal: true);
                    return false;
                }

                targetSources[targetName] = sourceName;
                return true;
            }

            foreach (KeyValuePair<string, string> image in plan.AdditionalImages)
            {
                if (!RegisterTarget(image.Key, image.Value, normalizedTargetSlot))
                {
                    return false;
                }
            }

            foreach (PartitionInfo partition in plan.Partitions)
            {
                string baseName = NormalizeOujiaPartitionBaseName(partition.PartitionName);
                bool isLogical = OujiaLogicalPartitionNames.Contains(baseName);
                bool isSlotless = IsOujiaKnownSlotlessPartition(baseName);

                bool writesBothSlots =
                    (mode == OujiaFlashMode.Ab && !isLogical && !isSlotless) ||
                    ((mode is OujiaFlashMode.Normal or OujiaFlashMode.Force) &&
                     platform == OujiaProcessorPlatform.Qualcomm &&
                     baseName.Equals("modem", StringComparison.OrdinalIgnoreCase) &&
                     !isSlotless);
                if (writesBothSlots)
                {
                    if (!RegisterTarget(partition.PartitionName, partition.FilePath, "a") ||
                        !RegisterTarget(partition.PartitionName, partition.FilePath, "b"))
                    {
                        return false;
                    }
                    continue;
                }

                if (!RegisterTarget(
                        partition.PartitionName,
                        partition.FilePath,
                        normalizedTargetSlot))
                {
                    return false;
                }
            }

            return true;
        }

        private async Task<bool> IsOugaFastbootdAsync(string deviceSerial)
        {
            string userspaceOutput = await GetOugaFastbootOutputAsync(deviceSerial, "getvar is-userspace");
            string userspaceValue = ExtractFastbootVar(userspaceOutput, "is-userspace");
            if (Regex.IsMatch(userspaceValue, @"^(?:yes|true|1)$", RegexOptions.IgnoreCase))
            {
                return true;
            }
            if (Regex.IsMatch(userspaceValue, @"^(?:no|false|0)$", RegexOptions.IgnoreCase))
            {
                return false;
            }

            string allOutput = await GetOugaFastbootOutputAsync(deviceSerial, "getvar all", 15);
            string allUserspaceValue = ExtractFastbootVar(allOutput, "is-userspace");
            if (Regex.IsMatch(allUserspaceValue, @"^(?:yes|true|1)$", RegexOptions.IgnoreCase))
            {
                return true;
            }
            if (Regex.IsMatch(allUserspaceValue, @"^(?:no|false|0)$", RegexOptions.IgnoreCase))
            {
                return false;
            }

            return allOutput.Contains("super-partition-name", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<(bool CanContinue, bool IsFastbootd)> LogAndValidateOugaFastbootDeviceAsync(
            string deviceSerial,
            bool includeFastbootdState)
        {
            bool? unlocked = await IsOugaFastbootUnlockedAsync(deviceSerial);
            if (!unlocked.HasValue)
            {
                LogToOugaFlashDual(
                    "设备解锁状态...",
                    "Black",
                    "检测失败",
                    "Red");
                LogToOugaFlash(
                    "设备Fastboot通信链路堵塞，为了防止变砖，请重新插拔数据线或更换USB接口后重试.",
                    "Red");
                MarkOujiaFlashFailure("设备Fastboot通信链路堵塞", fatal: true);
                return (false, false);
            }

            bool canContinue = unlocked.Value;
            LogToOugaFlashDual(
                "设备解锁状态...",
                "Black",
                canContinue ? "已解锁" : "未解锁",
                canContinue ? "Green" : "Red");
            if (!canContinue)
            {
                LogToOugaFlash("请将BL锁解开后再尝试刷入!!!", "Red");
                MarkOujiaFlashFailure("设备Bootloader未解锁", fatal: true);
                return (false, false);
            }

            string slotDisplay = await GetOugaFastbootSlotDisplayAsync(deviceSerial);
            bool slotKnown = slotDisplay is "A槽" or "B槽";
            LogToOugaFlashDual(
                "设备当前启动槽位：",
                "Black",
                slotDisplay,
                slotKnown ? "Blue" : "Red");
            if (!slotKnown)
            {
                LogToOugaFlash(
                    "为了本次的刷写安全，请确保你的设备为A/B槽位设备，不支持非A/B槽位设备，如果你的设备为A/B槽位设备却出现此报错，请检查数据线或连接接口后重试.",
                    "Red");
                MarkOujiaFlashFailure("设备当前启动槽位未知", fatal: true);
                return (false, false);
            }

            bool isFastbootd = false;
            if (includeFastbootdState)
            {
                isFastbootd = await IsOugaFastbootdAsync(deviceSerial);
                LogToOugaFlashDual(
                    "设备是否处于FastbootD模式...",
                    "Black",
                    isFastbootd ? "是" : "否",
                    isFastbootd ? "Green" : "Blue");
            }

            return (true, isFastbootd);
        }

        private async Task<string?> PrepareOugaFastbootdDeviceAsync(bool requireExistingFastbootd)
        {
            string initialModeName = requireExistingFastbootd ? "FastbootD" : "Fastboot";
            string? detectedSerial = await WaitForOugaFastbootDeviceAsync(
                initialModeName,
                requireFastbootd: requireExistingFastbootd ? true : null);
            if (string.IsNullOrWhiteSpace(detectedSerial))
            {
                return null;
            }

            string deviceSerial = detectedSerial;
            if (!await EnsureOugaFastbootConnectionStableAsync(
                    deviceSerial,
                    requireFastbootd: requireExistingFastbootd ? true : null))
            {
                return null;
            }

            var initialDeviceState = await LogAndValidateOugaFastbootDeviceAsync(
                deviceSerial,
                includeFastbootdState: true);
            if (!initialDeviceState.CanContinue)
            {
                return null;
            }

            if (initialDeviceState.IsFastbootd)
            {
                return deviceSerial;
            }

            if (requireExistingFastbootd)
            {
                LogToOugaFlash("仅FastbootD线刷要求设备已进入FastbootD模式.", "Red");
                MarkOujiaFlashFailure("设备未处于FastbootD模式", fatal: true);
                return null;
            }

            if (!await RebootOugaDeviceAsync(deviceSerial, toFastbootd: true))
            {
                return null;
            }

            string? fastbootdSerial = await WaitForOugaFastbootDeviceAsync(
                "FastbootD",
                deviceSerial,
                requireFastbootd: true);
            if (string.IsNullOrWhiteSpace(fastbootdSerial))
            {
                return null;
            }

            deviceSerial = fastbootdSerial;
            if (!await EnsureOugaFastbootConnectionStableAsync(
                    deviceSerial,
                    requireFastbootd: true))
            {
                return null;
            }

            var fastbootdDeviceState = await LogAndValidateOugaFastbootDeviceAsync(
                deviceSerial,
                includeFastbootdState: false);
            return fastbootdDeviceState.CanContinue ? deviceSerial : null;
        }

        private static bool IsOugaFastbootFailureOutput(string output)
        {
            return string.IsNullOrWhiteSpace(output) ||
                   output.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("cannot", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("invalid", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOujiaLogicalPartitionMissingOutput(string output)
        {
            return output.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("no such partition", StringComparison.OrdinalIgnoreCase) ||
                   output.Contains("unknown partition", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<(bool Success, string Output)> ExecuteOugaFastbootCommandAsync(
            string fastbootPath,
            string deviceSerial,
            string arguments,
            int timeoutSeconds = 60,
            bool acceptOkayOnTimeout = false)
        {
            if (string.IsNullOrWhiteSpace(fastbootPath) || !File.Exists(fastbootPath))
            {
                return (false, "fastboot.exe不存在");
            }

            string finalArguments = BuildOugaFastbootArguments(deviceSerial, arguments);
            var outputBuilder = new StringBuilder();
            object outputLock = new object();
            var okaySignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fastbootPath,
                    Arguments = finalArguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            void CaptureLine(string? line)
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                lock (outputLock)
                {
                    outputBuilder.AppendLine(line);
                }
                if (line.Contains("OKAY", StringComparison.OrdinalIgnoreCase))
                {
                    okaySignal.TrySetResult(true);
                }
            }

            process.OutputDataReceived += (_, eventArgs) => CaptureLine(eventArgs.Data);
            process.ErrorDataReceived += (_, eventArgs) => CaptureLine(eventArgs.Data);

            try
            {
                fastbootCompleteLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] [FASTBOOT CMD] {finalArguments}");
                if (!process.Start())
                {
                    return (false, "无法启动fastboot进程");
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                Task waitTask = process.WaitForExitAsync();
                Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
                Task firstCompletedTask = acceptOkayOnTimeout
                    ? await Task.WhenAny(waitTask, okaySignal.Task, timeoutTask)
                    : await Task.WhenAny(waitTask, timeoutTask);
                bool completed = firstCompletedTask == waitTask;
                bool acknowledgedEarly = acceptOkayOnTimeout && firstCompletedTask == okaySignal.Task;
                int exitCode = -1;
                if (completed)
                {
                    await waitTask;
                    process.WaitForExit();
                    exitCode = process.ExitCode;
                }

                string output;
                lock (outputLock)
                {
                    output = outputBuilder.ToString();
                }

                bool acknowledged = output.Contains("OKAY", StringComparison.OrdinalIgnoreCase);
                bool success = !IsOugaFastbootFailureOutput(output) &&
                               ((completed && exitCode == 0) || (acknowledgedEarly && acknowledged));

                if (!completed)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                            await process.WaitForExitAsync();
                        }
                    }
                    catch
                    {
                    }
                }

                foreach (string line in output.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    fastbootCompleteLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] [FASTBOOT OUTPUT] {line}");
                }

                return (success, output);
            }
            catch (Exception ex)
            {
                fastbootCompleteLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] [FASTBOOT ERROR] {ex.Message}");
                return (false, ex.Message);
            }
        }

        private void AppendOugaFastbootNativeOutput(
            string output,
            Func<string, bool>? filter = null,
            bool isError = false)
        {
            List<string> lines = (output ?? string.Empty)
                .Replace("\r\n", "\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line) &&
                               !line.Equals("ERROR_DETECTED", StringComparison.OrdinalIgnoreCase))
                .Where(line => filter == null || filter(line))
                .ToList();
            if (lines.Count == 0) return;

            Dispatcher.Invoke(() =>
            {
                foreach (string line in lines)
                {
                    var paragraph = CreateOugaFlashLogParagraph();
                    AppendOugaFlashStyledText(paragraph, "    ", "Gray");
                    if (isError)
                    {
                        AppendOugaFlashStyledText(paragraph, line, "Red", emphasized: true);
                    }
                    else
                    {
                        int okayIndex = line.IndexOf("OKAY", StringComparison.OrdinalIgnoreCase);
                        if (okayIndex >= 0)
                        {
                            AppendOugaFlashStyledText(paragraph, line[..okayIndex], "Gray");
                            AppendOugaFlashStyledText(paragraph, line[okayIndex..], "Green", emphasized: true);
                        }
                        else
                        {
                            AppendOugaFlashStyledText(
                                paragraph,
                                line,
                                line.StartsWith("Finished", StringComparison.OrdinalIgnoreCase) ? "Green" : "Gray",
                                emphasized: line.StartsWith("Finished", StringComparison.OrdinalIgnoreCase));
                        }
                    }
                    OugaFlashLogTextBox.Document.Blocks.Add(paragraph);
                }
                OugaFlashLogTextBox.ScrollToEnd();
            });
        }

        private void AppendOugaFastbootFailureOutput(string output)
        {
            List<string> allLines = (output ?? string.Empty)
                .Replace("\r\n", "\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
            List<string> diagnosticLines = allLines
                .Where(line => line.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ||
                               line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                               line.Contains("cannot", StringComparison.OrdinalIgnoreCase) ||
                               line.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
                               line.Contains("remote:", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (diagnosticLines.Count == 0)
            {
                diagnosticLines = allLines.TakeLast(3).ToList();
            }

            AppendOugaFastbootNativeOutput(string.Join(Environment.NewLine, diagnosticLines), isError: true);
        }

        private async Task<bool> RebootOugaDeviceAsync(string deviceSerial, bool toFastbootd)
        {
            string targetMode = toFastbootd ? "FastbootD模式" : "Fastboot";
            LogStepBegin($"重启设备到{targetMode}");
            string fastbootPath = GetOugaFastbootExecutablePath();
            var rebootResult = await ExecuteOugaFastbootCommandAsync(
                fastbootPath,
                deviceSerial,
                toFastbootd ? "reboot fastboot" : "reboot bootloader",
                timeoutSeconds: toFastbootd ? 8 : 30,
                acceptOkayOnTimeout: toFastbootd);
            if (!rebootResult.Success)
            {
                LogStepEndError();
                AppendOugaFastbootFailureOutput(rebootResult.Output);
                MarkOujiaFlashFailure($"重启设备到{targetMode}失败", fatal: true);
                return false;
            }

            LogStepEndOk();
            return true;
        }

        private async Task<bool> CheckFastbootDevice()
        {
            try
            {
                string programDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string fastbootPath = Path.Combine(programDirectory, "platform-tools", "fastboot.exe");
                string command = "devices";
                
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = fastbootPath,
                    Arguments = command,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using Process? process = Process.Start(startInfo);
                if (process is null) return false;

                // 设置10秒超时
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var waitTask = process.WaitForExitAsync();
                    
                if (await Task.WhenAny(waitTask, Task.Delay(10000)) == waitTask)
                {
                    string output = await outputTask;
                    return !string.IsNullOrWhiteSpace(output) && output.Contains("fastboot");
                }

                process.Kill();
                return false;
            }
            catch (Exception)
            {
                // 静默处理fastboot设备检测错误
                return false;
            }
        }

        private async Task<bool> CheckFastbootdMode()
        {
            try
            {
                string programDirectory = AppDomain.CurrentDomain.BaseDirectory;
                // 在FBD模式中使用flash文件夹中的fastboot.exe
                string fastbootPath = Path.Combine(programDirectory, "platform-tools", "fastboot.exe");
                
                // 如果flash文件夹中没有fastboot.exe，使用GetFastbootPath方法获取路径
                if (!File.Exists(fastbootPath))
                {
                    fastbootPath = GetFastbootPath();
                }
                
                string command = "getvar all";
                
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = fastbootPath,
                    Arguments = command,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using Process? process = Process.Start(startInfo);
                if (process is null) return false;

                // 设置15秒超时（getvar all可能需要更长时间）
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                var waitTask = process.WaitForExitAsync();
                    
                if (await Task.WhenAny(waitTask, Task.Delay(15000)) == waitTask)
                {
                    string output = await outputTask;
                    string error = await errorTask;
                    string fullOutput = output + error;
                    bool isFastbootd = fullOutput.Contains("super-partition-name");
                    return isFastbootd;
                }

                process.Kill();
                return false;
            }
            catch (Exception)
            {
                // 静默处理fastbootd模式检测错误
                return false;
            }
        }

        private string? ShowSlotSelectionDialog()
        {
                    string? selectedSlot = null;
            
            Dispatcher.Invoke(() => {
                var window = new Window
                {
                    Title = "选择启动槽位",
                    Width = 350,
                    Height = 220,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ResizeMode = ResizeMode.NoResize,
                    Owner = this,
                    Background = System.Windows.Media.Brushes.White
                };
                
                var stackPanel = new StackPanel { Margin = new Thickness(20) };
                
                var text = new TextBlock { 
                    Text = "您当前选择的是AB通刷模式，请选择一个启动槽位", 
                    TextWrapping = TextWrapping.Wrap, 
                    Margin = new Thickness(0,0,0,20),
                    FontSize = 14
                };
                
                var radioPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
                var radioA = new System.Windows.Controls.RadioButton { Content = "A 槽位", GroupName = "Slots", IsChecked = true, Margin = new Thickness(0,0,20,0), FontSize = 14 };
                var radioB = new System.Windows.Controls.RadioButton { Content = "B 槽位", GroupName = "Slots", FontSize = 14 };
                radioPanel.Children.Add(radioA);
                radioPanel.Children.Add(radioB);
                
                var btnPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0,20,0,0) };
                var btnOk = new System.Windows.Controls.Button { Content = "确定", Width = 80, Height = 30, Margin = new Thickness(10,0,0,0) };
                
                btnOk.Click += (s, e) => {
                    selectedSlot = radioA.IsChecked == true ? "a" : "b";
                    window.DialogResult = true;
                    window.Close();
                };
                
                btnPanel.Children.Add(btnOk);
                
                stackPanel.Children.Add(text);
                stackPanel.Children.Add(radioPanel);
                stackPanel.Children.Add(btnPanel);
                
                window.Content = stackPanel;
                window.Closed += (s, e) => {
                    // 窗口关闭时（包括点击X），如果未选择槽位，则视为取消
                    if (string.IsNullOrEmpty(selectedSlot))
                    {
                        // 这里不需要做额外操作，外层逻辑会检测selectedSlot为null
                    }
                };
                window.ShowDialog();
            });
            
            return selectedSlot;
        }

        private async Task ExecuteABFlashProcess()
        {
            try
            {
                // 1. 弹出槽位选择对话框
                string? targetSlot = ShowSlotSelectionDialog();
                if (string.IsNullOrEmpty(targetSlot))
                {
                    LogToOugaFlash("用户取消了操作", "Orange");
                    MarkOujiaFlashCancelled();
                    return;
                }
                
                LogToOugaFlashDual("用户选择的启动槽位：", "Black", targetSlot.ToUpper(), "Green");

                List<PartitionInfo> selectedPartitions = GetSelectedPartitions()
                    .Where(partition =>
                        !string.IsNullOrWhiteSpace(partition.FilePath) &&
                        !partition.FilePath.Contains("(缺失)", StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(partition.FilePath))
                    .ToList();

                // AB通刷必须附带tmp中的my_company/my_preload，并在逻辑分区
                // 预处理前纳入唯一任务清单；缺少任意一个镜像时禁止开始刷写。
                if (!TryLoadRequiredOujiaAdditionalImages(
                        "AB通刷",
                        out Dictionary<string, string> extraImages))
                {
                    return;
                }

                if (selectedPartitions.Count == 0 && extraImages.Count == 0)
                {
                    LogToOugaFlash("错误：分区表中没有已勾选且文件有效的镜像", "Red");
                    MarkOujiaFlashFailure("没有可执行的AB通刷任务", fatal: true);
                    return;
                }

                List<PartitionInfo> allValidPartitions = selectedPartitions
                    .Where(partition => !extraImages.ContainsKey(
                        NormalizeOujiaPartitionBaseName(partition.PartitionName)))
                    .ToList();

                // AB通刷与其他欧加线刷模式共用设备等待、状态校验和FastbootD切换链路。
                string? preparedFastbootSerial = await PrepareOugaFastbootdDeviceAsync(
                    requireExistingFastbootd: false);
                if (string.IsNullOrWhiteSpace(preparedFastbootSerial))
                {
                    return;
                }
                string fastbootSerial = preparedFastbootSerial;

                // FastbootD稳定后立即读取真实设备分区表判断平台，不能依赖用户是否勾选lk镜像。
                OujiaProcessorPlatform? detectedPlatform =
                    await DetectOujiaProcessorPlatformAsync(fastbootSerial);
                if (!detectedPlatform.HasValue)
                {
                    return;
                }
                bool isMtk = detectedPlatform.Value == OujiaProcessorPlatform.MediaTek;

                if (!ValidateOujiaPartitionTableCompatibility(allValidPartitions))
                {
                    return;
                }

                if (ShouldStopOugaFlashBeforeNextPartition("读取当前槽位"))
                {
                    return;
                }

                // 4. 检测当前槽位；非AB设备不允许进入AB通刷。
                string? currentSlot = await EnsureOujiaAbSlotSupportedAsync(fastbootSerial);
                if (string.IsNullOrEmpty(currentSlot))
                {
                    return;
                }
                LogToOugaFlashDual(
                    "设备当前启动槽位：",
                    "Black",
                    $"{currentSlot.ToUpper()}槽",
                    "Green");

                var validationPlan = new OujiaFlashPlan(
                    allValidPartitions,
                    extraImages,
                    totalBytes: 0,
                    totalSizeText: string.Empty);
                if (!ValidateOujiaFlashTargetCollisions(
                        validationPlan,
                        detectedPlatform.Value,
                        targetSlot))
                {
                    return;
                }

                string fastbootPath = GetOugaFastbootExecutablePath();
                if (string.IsNullOrWhiteSpace(fastbootPath) || !File.Exists(fastbootPath))
                {
                    LogToOugaFlash("错误：缺少fastboot.exe", "Red");
                    MarkOujiaFlashFailure("缺少fastboot.exe", fatal: true);
                    return;
                }
                var logicalPartitionsToPrepare = new HashSet<string>(
                    allValidPartitions
                        .Select(partition =>
                            NormalizeOujiaPartitionBaseName(partition.PartitionName))
                        .Where(OujiaLogicalPartitionNames.Contains),
                    StringComparer.OrdinalIgnoreCase);
                logicalPartitionsToPrepare.UnionWith(
                    extraImages.Keys.Where(OujiaLogicalPartitionNames.Contains));
                HashSet<string> logicalTargetsToPrepare =
                    GetOujiaPlanLogicalTargetNames(validationPlan, targetSlot);

                // 5. 清理COW分区。AB通刷先保留当前活动槽，完成非逻辑分区后再安全切槽。
                LogStepBegin("解析COW快照分区");
                if (!await DeleteCowPartitions(fastbootSerial))
                {
                    LogStepEndError();
                    MarkOujiaFlashFailure("解析或清理COW快照分区失败", fatal: true);
                    return;
                }
                else
                {
                    LogStepEndOk();
                }

                // 8. 刷写主流程：严格以分区表复选框的勾选状态为准。
                // 在开始AB通刷时确保进度条可见并重置
                ShowProgressBar();

                // 重新计算任务总数
                int totalTasks = extraImages.Count;
                foreach (var part in allValidPartitions)
                {
                    string baseName = NormalizeOujiaPartitionBaseName(part.PartitionName);
                    bool isSingleTarget = OujiaLogicalPartitionNames.Contains(baseName) ||
                                          IsOujiaKnownSlotlessPartition(baseName);
                    totalTasks += isSingleTarget ? 1 : 2;
                }

                long totalFlashBytesAB = extraImages.Values.Sum(path => new FileInfo(path).Length) +
                    allValidPartitions.Sum(p => {
                    if (string.IsNullOrEmpty(p.FilePath) || !File.Exists(p.FilePath)) return 0L;
                    long len = new FileInfo(p.FilePath).Length;
                    bool isLogicalHere = OujiaLogicalPartitionNames.Contains(
                        NormalizeOujiaPartitionBaseName(p.PartitionName));
                    bool isSlotlessHere = IsOujiaKnownSlotlessPartition(p.PartitionName);
                    return isLogicalHere || isSlotlessHere
                        ? len
                        : (len * 2);
                });
                _lastGlobalProgressBytes = 0;
                FlashProgressPercentage = 0;
                FlashProgressDetailText = $"0/{FormatFileSize(totalFlashBytesAB)}";
                WriteFlashProgressFile(0, totalFlashBytesAB);

                LogToOugaFlashTriple(
                    "总结刷写任务，AB通刷共 ",
                    "Black",
                    totalTasks.ToString(),
                    "Purple",
                    " 个分区任务.",
                    "Black");

                var abPlan = new OujiaFlashPlan(
                    allValidPartitions,
                    extraImages,
                    totalFlashBytesAB,
                    FormatFileSize(totalFlashBytesAB));

                // 第一阶段：保持当前活动槽不变，先刷完FastbootD中的非逻辑分区。
                // modem统一例外：高通后置到Bootloader，联发科后置但仍在FastbootD刷写。
                List<PartitionInfo> sortedPartitions = allValidPartitions
                    .OrderBy(partition => new FileInfo(partition.FilePath).Length)
                    .ToList();
                foreach (PartitionInfo partition in sortedPartitions)
                {
                    string basePartitionName =
                        NormalizeOujiaPartitionBaseName(partition.PartitionName);
                    bool isLogical =
                        OujiaLogicalPartitionNames.Contains(basePartitionName);
                    bool isSlotless = IsOujiaKnownSlotlessPartition(basePartitionName);
                    bool isModem = basePartitionName.Equals(
                        "modem",
                        StringComparison.OrdinalIgnoreCase);
                    if (isModem && !isSlotless)
                    {
                        // modem不参与切槽前的非逻辑分区阶段。
                        continue;
                    }

                    long imageBytes = new FileInfo(partition.FilePath).Length;
                    if (isLogical)
                    {
                        // 逻辑分区必须等非逻辑分区完成并安全切槽后再处理。
                        continue;
                    }

                    if (isSlotless)
                    {
                        if (ShouldStopOugaFlashBeforeNextPartition(basePartitionName))
                        {
                            return;
                        }
                        await FlashOujiaPlannedImageAsync(
                            partition.PartitionName,
                            partition.FilePath,
                            targetSlot,
                            abPlan,
                            imageBytes,
                            "[写入镜像]");
                        continue;
                    }

                    if (ShouldStopOugaFlashBeforeNextPartition(
                            $"{basePartitionName}_a"))
                    {
                        return;
                    }
                    await FlashOujiaPlannedImageAsync(
                        partition.PartitionName,
                        partition.FilePath,
                        "a",
                        abPlan,
                        imageBytes,
                        "[写入镜像]");

                    if (ShouldStopOugaFlashBeforeNextPartition(
                            $"{basePartitionName}_b"))
                    {
                        return;
                    }
                    await FlashOujiaPlannedImageAsync(
                        partition.PartitionName,
                        partition.FilePath,
                        "b",
                        abPlan,
                        imageBytes,
                        "[写入镜像]");
                }

                if (ShouldFinalizeOugaFlashStop())
                {
                    return;
                }

                // 第二阶段：非逻辑分区（modem除外）完成后，再安全切换活动槽。
                if (!string.Equals(currentSlot, targetSlot, StringComparison.OrdinalIgnoreCase))
                {
                    if (ShouldStopOugaFlashBeforeNextPartition("切换活动槽位"))
                    {
                        return;
                    }

                    string slotSwitchDescription =
                        $"切换槽位：{currentSlot.ToUpper()}槽 → {targetSlot.ToUpper()}槽位";
                    LogStepBegin(slotSwitchDescription);
                    var setActiveResult = await ExecuteOugaFastbootCommandAsync(
                        fastbootPath,
                        fastbootSerial,
                        $"set_active {targetSlot}");
                    if (!setActiveResult.Success)
                    {
                        LogStepEndError();
                        AppendOugaFastbootFailureOutput(setActiveResult.Output);
                        MarkOujiaFlashFailure(slotSwitchDescription + "失败", fatal: true);
                        return;
                    }
                    LogStepEndOk();
                    currentSlot = targetSlot;
                    if (_activeOujiaFlashSession != null)
                    {
                        _activeOujiaFlashSession.CurrentSlot = targetSlot;
                    }

                    // 只删除已勾选任务涉及的逻辑分区，并仅重建目标槽任务。
                    foreach (string name in logicalPartitionsToPrepare)
                    {
                        if (ShouldStopOugaFlashBeforeNextPartition($"{name}_a"))
                        {
                            return;
                        }

                        string partA = $"{name}_a";
                        LogStepBeginNoTime($"删除逻辑分区 {partA}");
                        var resDelA = await ExecuteOugaFastbootCommandAsync(
                            fastbootPath,
                            fastbootSerial,
                            $"delete-logical-partition {partA}");
                        if (resDelA.Success || IsOujiaLogicalPartitionMissingOutput(resDelA.Output))
                        {
                            LogStepEndOk();
                        }
                        else
                        {
                            LogStepEndError();
                            AppendOugaFastbootFailureOutput(resDelA.Output);
                            MarkOujiaFlashFailure($"删除逻辑分区{partA}失败", fatal: true);
                            return;
                        }

                        if (ShouldStopOugaFlashBeforeNextPartition($"{name}_b"))
                        {
                            return;
                        }

                        string partB = $"{name}_b";
                        LogStepBeginNoTime($"删除逻辑分区 {partB}");
                        var resDelB = await ExecuteOugaFastbootCommandAsync(
                            fastbootPath,
                            fastbootSerial,
                            $"delete-logical-partition {partB}");
                        if (resDelB.Success || IsOujiaLogicalPartitionMissingOutput(resDelB.Output))
                        {
                            LogStepEndOk();
                        }
                        else
                        {
                            LogStepEndError();
                            AppendOugaFastbootFailureOutput(resDelB.Output);
                            MarkOujiaFlashFailure($"删除逻辑分区{partB}失败", fatal: true);
                            return;
                        }
                    }

                    foreach (string targetName in logicalTargetsToPrepare)
                    {
                        if (ShouldStopOugaFlashBeforeNextPartition(targetName))
                        {
                            return;
                        }

                        LogStepBeginNoTime($"创建逻辑分区 {targetName}");
                        var resCreate = await ExecuteOugaFastbootCommandAsync(
                            fastbootPath,
                            fastbootSerial,
                            $"create-logical-partition {targetName} 0");
                        if (resCreate.Success)
                        {
                            LogStepEndOk();
                        }
                        else
                        {
                            LogStepEndError();
                            AppendOugaFastbootFailureOutput(resCreate.Output);
                            MarkOujiaFlashFailure($"创建逻辑分区{targetName}失败", fatal: true);
                            return;
                        }
                    }
                }
                else
                {
                    LogToOugaFlash("当前槽位与目标一致，跳过分区重建...", "Green");
                }

                // 第三阶段：切槽完成后，刷入额外逻辑镜像与已勾选逻辑分区。
                foreach (KeyValuePair<string, string> extraImage in extraImages)
                {
                    if (ShouldStopOugaFlashBeforeNextPartition(
                            $"{extraImage.Key}_{targetSlot}"))
                    {
                        return;
                    }

                    bool extraOk = await FlashOujiaPlannedImageAsync(
                        extraImage.Key,
                        extraImage.Value,
                        targetSlot,
                        abPlan,
                        new FileInfo(extraImage.Value).Length,
                        "[额外]");
                    if (!extraOk)
                    {
                        MarkOujiaFlashFailure(
                            $"额外分区{extraImage.Key}_{targetSlot}刷写失败");
                    }
                }

                foreach (PartitionInfo partition in sortedPartitions)
                {
                    string basePartitionName =
                        NormalizeOujiaPartitionBaseName(partition.PartitionName);
                    if (!OujiaLogicalPartitionNames.Contains(basePartitionName))
                    {
                        continue;
                    }

                    if (ShouldStopOugaFlashBeforeNextPartition(
                            $"{basePartitionName}_{targetSlot}"))
                    {
                        return;
                    }

                    await FlashOujiaPlannedImageAsync(
                        partition.PartitionName,
                        partition.FilePath,
                        targetSlot,
                        abPlan,
                        new FileInfo(partition.FilePath).Length,
                        "[写入镜像]");
                }


                if (ShouldFinalizeOugaFlashStop())
                {
                    return;
                }
                
                // modem统一后置：高通进入Bootloader，联发科保持FastbootD，再刷入A/B。
                List<PartitionInfo> modemParts = allValidPartitions.Where(
                    partition =>
                        NormalizeOujiaPartitionBaseName(partition.PartitionName)
                            .Equals("modem", StringComparison.OrdinalIgnoreCase) &&
                        !IsOujiaKnownSlotlessPartition(partition.PartitionName))
                    .ToList();
                if (modemParts.Count > 0)
                {
                    if (!isMtk)
                    {
                        if (ShouldStopOugaFlashBeforeNextPartition(
                                "重启到Fastboot刷写modem") ||
                            !await RebootOugaDeviceAsync(
                                fastbootSerial,
                                toFastbootd: false))
                        {
                            return;
                        }

                        string? bootloaderSerial =
                            await WaitForOugaFastbootDeviceAsync(
                                "Fastboot",
                                fastbootSerial,
                                requireFastbootd: false);
                        if (string.IsNullOrWhiteSpace(bootloaderSerial))
                        {
                            return;
                        }
                        fastbootSerial = bootloaderSerial;
                    }

                    foreach (PartitionInfo modemPart in modemParts)
                    {
                        long modemBytes = new FileInfo(modemPart.FilePath).Length;
                        if (ShouldStopOugaFlashBeforeNextPartition("modem_a"))
                        {
                            return;
                        }
                        await FlashOujiaPlannedImageAsync(
                            modemPart.PartitionName,
                            modemPart.FilePath,
                            "a",
                            abPlan,
                            modemBytes,
                            "[写入镜像]");

                        if (ShouldStopOugaFlashBeforeNextPartition("modem_b"))
                        {
                            return;
                        }
                        await FlashOujiaPlannedImageAsync(
                            modemPart.PartitionName,
                            modemPart.FilePath,
                            "b",
                            abPlan,
                            modemBytes,
                            "[写入镜像]");
                    }
                }

                await ExecuteOujiaPostFlashActionsAsync(
                    fastbootSerial,
                    isMtk,
                    abPlan);

            }
            catch (Exception ex)
            {
                MarkOujiaFlashFailure("AB通刷流程异常", ex.Message, fatal: true);
                LogToOugaFlash($"AB通刷错误: {ex.Message}", "Red");
            }
        }

        private async Task<bool> RebootToFastbootd()
        {
            try
            {
                string programDirectory = AppDomain.CurrentDomain.BaseDirectory;
                // 在FBD模式中使用flash文件夹中的fastboot.exe
                string fastbootPath = Path.Combine(programDirectory, "platform-tools", "fastboot.exe");
                
                // 如果flash文件夹中没有fastboot.exe，使用GetFastbootPath方法获取路径
                if (!File.Exists(fastbootPath))
                {
                    fastbootPath = GetFastbootPath();
                }
                
                string command = BuildOugaFastbootArguments(
                    GetOujiaFlashTargetSerial(),
                    "reboot fastboot");
                
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = fastbootPath,
                    Arguments = command,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using Process? process = Process.Start(startInfo);
                if (process is null) return false;

                // 设置50秒超时
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                var waitTask = process.WaitForExitAsync();
                    
                if (await Task.WhenAny(waitTask, Task.Delay(50000)) == waitTask)
                {
                    string output = await outputTask;
                    string error = await errorTask;
                    return process.ExitCode == 0;
                }

                process.Kill();
                return false;
            }
            catch (Exception)
            {
                // 静默处理重启到fastbootd模式错误
                return false;
            }
        }

        private async Task<bool> RebootToBootloader()
        {
            try
            {
                string programDirectory = AppDomain.CurrentDomain.BaseDirectory;
                // 在FBD模式中使用flash文件夹中的fastboot.exe
                string fastbootPath = Path.Combine(programDirectory, "platform-tools", "fastboot.exe");
                
                // 如果flash文件夹中没有fastboot.exe，使用GetFastbootPath方法获取路径
                if (!File.Exists(fastbootPath))
                {
                    fastbootPath = GetFastbootPath();
                }
                
                string command = BuildOugaFastbootArguments(
                    GetOujiaFlashTargetSerial(),
                    "reboot bootloader");
                
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = fastbootPath,
                    Arguments = command,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using Process? process = Process.Start(startInfo);
                if (process is null) return false;

                // 设置10秒超时
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                var waitTask = process.WaitForExitAsync();
                    
                if (await Task.WhenAny(waitTask, Task.Delay(10000)) == waitTask)
                {
                    string output = await outputTask;
                    string error = await errorTask;
                    return process.ExitCode == 0;
                }

                process.Kill();
                return false;
            }
            catch (Exception)
            {
                // 静默处理重启到bootloader模式错误
                return false;
            }
        }

        private async Task<bool> FlashPartition(string partitionName, string imagePath)
        {
            try
            {
                string programDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string fastbootPath = Path.Combine(programDirectory, "platform-tools", "fastboot.exe");
                string command = $"flash {partitionName} \"{imagePath}\"";
                fastbootCompleteLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] [FLASH BEGIN] {partitionName} <= \"{imagePath}\"");
                fastbootCompleteLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] [FASTBOOT CMD] {command}");
                
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = fastbootPath,
                    Arguments = command,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                var fileName = Path.GetFileName(imagePath);
                LogStepBeginNoTime($"[写入镜像] {fileName}  >  {partitionName}.img");
                using Process? process = Process.Start(startInfo);
                if (process is null)
                {
                    LogStepEndError();
                    fastbootCompleteLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] [FLASH END] {partitionName} (failed to start)");
                    return false;
                }
                    StringBuilder outputBuilder = new StringBuilder();
                    StringBuilder errorBuilder = new StringBuilder();
                    
                    // 实时读取标准输出
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            outputBuilder.AppendLine(e.Data);
                            // 将fastboot输出记录到全局日志中
                            fastbootCompleteLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] [FASTBOOT OUTPUT] {e.Data}");
                            // 只解析进度信息，不显示详细的fastboot输出
                            Dispatcher.Invoke(() =>
                            {
                                ParseFastbootProgress(e.Data);
                            });
                        }
                    };
                    
                    // 实时读取标准错误
                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            errorBuilder.AppendLine(e.Data);
                            // 将fastboot错误输出记录到全局日志中
                            fastbootCompleteLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] [FASTBOOT ERROR] {e.Data}");
                            // 只解析进度信息，不显示详细的fastboot输出
                            Dispatcher.Invoke(() =>
                            {
                                ParseFastbootProgress(e.Data);
                            });
                        }
                    };
                    
                    // 开始异步读取
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    
                    // 旧入口同样不再对分区刷写设置固定总时长超时。
                    await process.WaitForExitAsync().ConfigureAwait(false);
                    process.WaitForExit();

                    string output = outputBuilder.ToString();
                    string error = errorBuilder.ToString();

                    if (process.ExitCode != 0)
                    {
                        LogStepEndError();
                        fastbootCompleteLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] [FLASH END] {partitionName} (exit {process.ExitCode})");
                        return false;
                    }

                    // 调试输出：显示fastboot flash命令的完整输出
                    System.Diagnostics.Debug.WriteLine($"Fastboot flash输出: '{output}'");
                    System.Diagnostics.Debug.WriteLine($"Fastboot flash错误: '{error}'");

                    // 解析槽位信息（同时检查标准输出和错误输出），此处不再重复输出映射，仅结束步骤
                    LogStepEndOk();
                    fastbootCompleteLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] [FLASH END] {partitionName}");

                    return true;
            }
            catch (Exception)
            {
                LogStepEndError();
                return false;
            }
        }

        private string ParseSlotInfo(string output, string partitionName, string imagePath)
        {
            try
            {
                string fileName = Path.GetFileName(imagePath);
                
                // 调试输出：显示正在解析的内容
                System.Diagnostics.Debug.WriteLine($"ParseSlotInfo - 文件名: {fileName}, 分区名: {partitionName}");
                System.Diagnostics.Debug.WriteLine($"ParseSlotInfo - 输出内容: {output}");
                
                // 查找类似 "Sending 'boot_a' (xxxxx KB)..." 或 "Writing 'boot_a'..." 的输出
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    string trimmedLine = line.Trim();
                    System.Diagnostics.Debug.WriteLine($"ParseSlotInfo - 检查行: {trimmedLine}");
                    
                    if (trimmedLine.Contains("Sending '") || trimmedLine.Contains("Writing '"))
                    {
                        // 提取槽位信息，例如从 "Sending 'boot_a' (xxxxx KB)..." 中提取 "boot_a"
                        var match = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"'([^']+)'");
                        if (match.Success)
                        {
                            string targetPartition = match.Groups[1].Value;
                            System.Diagnostics.Debug.WriteLine($"ParseSlotInfo - 找到槽位: {targetPartition}");
                            return $"{fileName}  >  {targetPartition}.img";
                        }
                    }
                }
                
                // 如果没有找到具体的槽位信息，返回默认格式
                System.Diagnostics.Debug.WriteLine($"ParseSlotInfo - 未找到槽位信息，使用默认格式");
                return $"{fileName}  >  {partitionName}.img";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ParseSlotInfo - 异常: {ex.Message}");
                return string.Empty;
            }
        }

        private void LogToOugaFlash(string message, string color = "Black")
        {
            Dispatcher.Invoke(() =>
            {
                string actualMessage = message ?? string.Empty;
                string actualColor = color;

                if (actualMessage.StartsWith("COLOR:", StringComparison.OrdinalIgnoreCase))
                {
                    int pipeIndex = actualMessage.IndexOf('|');
                    if (pipeIndex > 6)
                    {
                        actualColor = actualMessage.Substring(6, pipeIndex - 6);
                        actualMessage = actualMessage[(pipeIndex + 1)..].Trim();
                    }
                }

                bool isAppend = actualMessage.Trim().Equals("OK", StringComparison.OrdinalIgnoreCase);
                if (isAppend && OugaFlashLogTextBox.Document.Blocks.LastBlock is Paragraph lastParagraph)
                {
                    AppendOugaFlashStyledText(lastParagraph, actualMessage, actualColor, emphasized: true);
                }
                else
                {
                    var paragraph = CreateOugaFlashLogParagraph();
                    AppendOugaFlashTimestamp(paragraph);
                    AppendOugaFlashStyledText(
                        paragraph,
                        actualMessage,
                        actualColor,
                        emphasized: actualColor.Equals("Green", StringComparison.OrdinalIgnoreCase) ||
                                    actualColor.Equals("Red", StringComparison.OrdinalIgnoreCase),
                        recognizeOperationTag: true);
                    OugaFlashLogTextBox.Document.Blocks.Add(paragraph);
                }

                OugaFlashLogTextBox.ScrollToEnd();
            });
        }

        private void LogToOugaFlashDual(string part1, string color1, string part2, string color2)
        {
            Dispatcher.Invoke(() =>
            {
                var paragraph = CreateOugaFlashLogParagraph();
                AppendOugaFlashTimestamp(paragraph);
                AppendOugaFlashStyledText(paragraph, part1, color1, recognizeOperationTag: true);
                bool emphasized = part2.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
                                  part2.Equals("YES", StringComparison.OrdinalIgnoreCase) ||
                                  part2.Equals("Error", StringComparison.OrdinalIgnoreCase);
                string effectiveColor = part2.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
                                        part2.Equals("YES", StringComparison.OrdinalIgnoreCase)
                    ? "Green"
                    : color2;
                AppendOugaFlashStyledText(paragraph, part2, effectiveColor, emphasized);
                OugaFlashLogTextBox.Document.Blocks.Add(paragraph);
                OugaFlashLogTextBox.ScrollToEnd();
            });
        }

        private void LogToOugaFlashTriple(string part1, string color1, string part2, string color2, string part3, string color3)
        {
            Dispatcher.Invoke(() =>
            {
                var paragraph = CreateOugaFlashLogParagraph();
                AppendOugaFlashTimestamp(paragraph);
                AppendOugaFlashStyledText(paragraph, part1, color1, recognizeOperationTag: true);
                AppendOugaFlashStyledText(paragraph, part2, color2, recognizeOperationTag: true);
                bool emphasized = part3.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
                                  part3.Equals("YES", StringComparison.OrdinalIgnoreCase) ||
                                  part3.Equals("Error", StringComparison.OrdinalIgnoreCase);
                string effectiveColor = part3.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
                                        part3.Equals("YES", StringComparison.OrdinalIgnoreCase)
                    ? "Green"
                    : color3;
                AppendOugaFlashStyledText(paragraph, part3, effectiveColor, emphasized);
                OugaFlashLogTextBox.Document.Blocks.Add(paragraph);
                OugaFlashLogTextBox.ScrollToEnd();
            });
        }

        private void LogOkStep(string title)
        {
            LogToOugaFlashTriple(title, "Black", "… ", "Black", "OK", "Green");
        }

        private void LogErrorStep(string title)
        {
            LogToOugaFlashTriple(title, "Black", "… ", "Black", "Error", "Red");
        }

        

        private System.Windows.Documents.Paragraph? _pendingStepParagraph;
        private readonly Dictionary<string, System.Windows.Documents.Paragraph> _pendingFastbootFlashParagraphs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, System.Windows.Documents.Paragraph> _pendingFastbootReadParagraphs = new(StringComparer.OrdinalIgnoreCase);

        private static bool TryBuildOugaFastbootWriteStepTitle(string title, out string normalizedTitle)
        {
            Match match = Regex.Match(
                title ?? string.Empty,
                @"^\[写入镜像\]\s*(?<source>.+?)\s*(?:->|→|>)\s*(?<target>.+?)\s*$",
                RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                normalizedTitle = string.Empty;
                return false;
            }

            string source = Path.GetFileName(match.Groups["source"].Value.Trim());
            string target = Path.GetFileName(match.Groups["target"].Value.Trim());
            normalizedTitle = $"{EnsureImgSuffix(source)} → {EnsureImgSuffix(target)}";
            return true;
        }

        private static void RenderOugaFastbootWriteStep(Paragraph paragraph, string title, bool? success)
        {
            paragraph.Inlines.Clear();
            AppendOugaFlashTimestamp(paragraph);
            AppendOugaFlashStyledText(paragraph, "[Flashing] ", "Purple", emphasized: true);
            AppendOugaFlashStyledText(paragraph, title, "Black");
            if (success.HasValue)
            {
                AppendOugaFlashStyledText(
                    paragraph,
                    success.Value ? " ...OK" : " ...Error",
                    success.Value ? "Green" : "Red",
                    emphasized: true);
            }
            else
            {
                AppendOugaFlashStyledText(paragraph, " ...", "Gray");
            }
        }

        private void LogStepBegin(string title)
        {
            Dispatcher.Invoke(() =>
            {
                var paragraph = CreateOugaFlashLogParagraph();
                AppendOugaFlashTimestamp(paragraph);
                AppendOugaFlashStyledText(paragraph, title, "Black", recognizeOperationTag: true);
                AppendOugaFlashStyledText(paragraph, " ...", "Gray");
                OugaFlashLogTextBox.Document.Blocks.Add(paragraph);
                OugaFlashLogTextBox.ScrollToEnd();
                _pendingStepParagraph = paragraph;
            });
        }

        private void LogStepEndOk()
        {
            Dispatcher.Invoke(() =>
            {
                if (_pendingStepParagraph != null)
                {
                    if (_pendingStepParagraph.Tag is ValueTuple<string, string> stepInfo)
                    {
                        RenderOugaFastbootWriteStep(_pendingStepParagraph, stepInfo.Item2, true);
                    }
                    else
                    {
                        AppendOugaFlashStyledText(_pendingStepParagraph, "OK", "Green", emphasized: true);
                    }
                    OugaFlashLogTextBox.ScrollToEnd();
                    _pendingStepParagraph = null;
                }
            });
        }

        private void LogStepEndDone()
        {
            Dispatcher.Invoke(() =>
            {
                if (_pendingStepParagraph != null)
                {
                    if (_pendingStepParagraph.Tag is ValueTuple<string, string> stepInfo)
                    {
                        RenderOugaFastbootWriteStep(_pendingStepParagraph, stepInfo.Item2, true);
                    }
                    else
                    {
                        AppendOugaFlashStyledText(_pendingStepParagraph, "Done", "Green", emphasized: true);
                    }
                    OugaFlashLogTextBox.ScrollToEnd();
                    _pendingStepParagraph = null;
                }
            });
        }

        private void LogStepEndError()
        {
            Dispatcher.Invoke(() =>
            {
                if (_pendingStepParagraph != null)
                {
                    if (_pendingStepParagraph.Tag is ValueTuple<string, string> stepInfo)
                    {
                        RenderOugaFastbootWriteStep(_pendingStepParagraph, stepInfo.Item2, false);
                    }
                    else
                    {
                        AppendOugaFlashStyledText(_pendingStepParagraph, "Error", "Red", emphasized: true);
                    }
                    OugaFlashLogTextBox.ScrollToEnd();
                    _pendingStepParagraph = null;
                }
            });
        }

        private void LogStepBeginNoTime(string title)
        {
            Dispatcher.Invoke(() =>
            {
                bool isFastbootWriteStep = TryBuildOugaFastbootWriteStepTitle(title, out string normalizedTitle);
                var paragraph = CreateOugaFlashLogParagraph();
                if (isFastbootWriteStep)
                {
                    paragraph.Tag = ("Flashing", normalizedTitle);
                    RenderOugaFastbootWriteStep(paragraph, normalizedTitle, null);
                }
                else
                {
                    AppendOugaFlashTimestamp(paragraph);
                    AppendOugaFlashStyledText(paragraph, title, "Black", recognizeOperationTag: true);
                    AppendOugaFlashStyledText(paragraph, " ...", "Gray");
                }
                OugaFlashLogTextBox.Document.Blocks.Add(paragraph);
                OugaFlashLogTextBox.ScrollToEnd();
                _pendingStepParagraph = paragraph;
            });
        }

        private async Task KillAllFastbootProcesses()
        {
            try
            {
                var fastbootProcesses = Process.GetProcessesByName("fastboot");
                int killedCount = 0;
                
                foreach (var process in fastbootProcesses)
                {
                    try
                    {
                        process.Kill();
                        await process.WaitForExitAsync();
                        killedCount++;
                    }
                    catch (Exception)
                    {
                        // 静默处理进程结束失败
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception)
            {
                // 静默处理fastboot进程结束错误
            }
        }

        private async Task KillAllAdbAndFastbootProcesses()
        {
            try
            {
                // 终止所有adb进程
                var adbProcesses = Process.GetProcessesByName("adb");
                foreach (var process in adbProcesses)
                {
                    try
                    {
                        process.Kill();
                        await process.WaitForExitAsync();
                    }
                    catch (Exception)
                    {
                        // 静默处理进程结束失败
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                // 终止所有fastboot进程
                var fastbootProcesses = Process.GetProcessesByName("fastboot");
                foreach (var process in fastbootProcesses)
                {
                    try
                    {
                        process.Kill();
                        await process.WaitForExitAsync();
                    }
                    catch (Exception)
                    {
                        // 静默处理进程结束失败
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception)
            {
                // 静默处理进程结束错误
            }
        }
        
        private async Task GenerateFlashLogFile()
        {
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        // 获取桌面路径
                        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        string downloadsPath = Path.Combine(userProfile, "Downloads");
                        if (!Directory.Exists(downloadsPath)) Directory.CreateDirectory(downloadsPath);
                        
                        // 生成带时间戳的文件名
                        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                        string fileName = $"刷机日志_{timestamp}.txt";
                        string filePath = Path.Combine(downloadsPath, fileName);
                        
                        // 从RichTextBox中提取纯文本内容
                        string logContent = new TextRange(OugaFlashLogTextBox.Document.ContentStart, OugaFlashLogTextBox.Document.ContentEnd).Text;
                        
                        string logFileContent = BuildOujiaFlashLogFileContent(
                            logContent,
                            "全量包模式",
                            FolderPathTextBox?.Text);
                         
                        // 写入文件
                        File.WriteAllText(filePath, logFileContent, Encoding.UTF8);
                        
                        LogToOugaFlash($"刷机日志已自动保存在: {filePath}", "Black");
                    }
                    catch (Exception ex)
                    {
                        // 在日志中显示文件生成失败信息
                        LogToOugaFlash($"生成日志文件失败: {ex.Message}", "Red");
                    }
                });
            }
            catch (Exception)
            {
                // 静默处理错误，避免影响主流程
            }
        }
        
        // AfterSales模式的日志保存方法
        private async Task GenerateAfterSalesFlashLogFile()
        {
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        // 获取下载目录路径
                        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        string downloadsPath = Path.Combine(userProfile, "Downloads");
                        if (!Directory.Exists(downloadsPath)) Directory.CreateDirectory(downloadsPath);
                        
                        // 生成带时间戳的文件名
                        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                        string fileName = $"售后包刷机日志_{timestamp}.txt";
                        string filePath = Path.Combine(downloadsPath, fileName);
                        
                        // 从RichTextBox中提取纯文本内容
                        string logContent = new TextRange(OugaFlashLogTextBox.Document.ContentStart, OugaFlashLogTextBox.Document.ContentEnd).Text;
                        
                        string logFileContent = BuildOujiaFlashLogFileContent(
                            logContent,
                            "售后包模式",
                            AfterSalesFlashPackTextBox?.Text);
                         
                        // 写入文件
                        File.WriteAllText(filePath, logFileContent, Encoding.UTF8);
                        
                        LogToOugaFlash($"刷机日志已自动保存在: {filePath}", "Green");
                    }
                    catch (Exception ex)
                    {
                        // 在日志中显示文件生成失败信息
                        LogToOugaFlash($"生成日志文件失败: {ex.Message}", "Red");
                    }
                });
            }
            catch (Exception)
            {
                // 静默处理错误，避免影响主流程
            }
        }

        private string BuildOujiaFlashLogFileContent(
            string interfaceLogContent,
            string flashMode,
            string? flashSourcePath)
        {
            string toolVersion = JoinQQGroupButton?.Content?.ToString()?.Trim()
                                 ?? "未知版本";
            string normalizedSourcePath = string.IsNullOrWhiteSpace(flashSourcePath)
                ? "未记录"
                : flashSourcePath.Trim();
            string logId = Guid.NewGuid().ToString("N").ToUpperInvariant();

            var logBuilder = new StringBuilder();
            logBuilder.AppendLine("=== 紫罗兰工具箱欧加刷写日志 ===");
            logBuilder.AppendLine($"日志编号: {logId}");
            logBuilder.AppendLine($"生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            logBuilder.AppendLine($"工具版本: {toolVersion}");
            logBuilder.AppendLine($"刷写模式: {flashMode}");
            logBuilder.AppendLine($"刷写文件路径: {normalizedSourcePath}");
            logBuilder.AppendLine("==============================");
            logBuilder.AppendLine();

            logBuilder.AppendLine("=== 界面显示日志 ===");
            logBuilder.AppendLine(interfaceLogContent?.TrimEnd() ?? string.Empty);
            logBuilder.AppendLine();

            logBuilder.AppendLine("=== 完整Fastboot输出 ===");
            logBuilder.AppendLine(
                fastbootCompleteLog.Length > 0
                    ? fastbootCompleteLog.ToString().TrimEnd()
                    : "无fastboot输出记录");
            logBuilder.AppendLine();
            logBuilder.AppendLine("=== 日志结束 ===");

            string integritySource = logBuilder.ToString();
            string integrityHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(integritySource)));
            logBuilder.AppendLine();
            logBuilder.AppendLine("=== 日志完整性信息 ===");
            logBuilder.AppendLine($"SHA-256: {integrityHash}");

            return logBuilder.ToString();
        }
         
        private Dictionary<string, string> ResolveFastbootdRepairImages(
            string imageDirectory,
            bool useAfterSalesPackage)
        {
            string[] repairPartitionNames =
            {
                "boot", "dtbo", "init_boot", "lk", "modem", "recovery",
                "vbmeta", "vbmeta_system", "vbmeta_vendor", "vendor_boot"
            };
            var repairPartitions = new HashSet<string>(
                repairPartitionNames,
                StringComparer.OrdinalIgnoreCase);
            var resolvedImages =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (useAfterSalesPackage)
            {
                foreach (PartitionInfo partition in
                         OugaPartitionTableDataGrid.Items.OfType<PartitionInfo>())
                {
                    if (string.IsNullOrWhiteSpace(partition.FilePath) ||
                        partition.FilePath.Contains(
                            "(缺失)",
                            StringComparison.OrdinalIgnoreCase) ||
                        !File.Exists(partition.FilePath))
                    {
                        continue;
                    }

                    string baseName =
                        NormalizeOujiaPartitionBaseName(partition.PartitionName);
                    if (!repairPartitions.Contains(baseName))
                    {
                        continue;
                    }

                    if (!resolvedImages.TryGetValue(
                            baseName,
                            out string? existingPath) ||
                        GetFastbootdRepairImagePriority(
                            partition.FilePath,
                            baseName) <
                        GetFastbootdRepairImagePriority(existingPath, baseName))
                    {
                        resolvedImages[baseName] = partition.FilePath;
                    }
                }
            }

            string[] candidateDirectories =
            {
                imageDirectory,
                Path.Combine(imageDirectory, "IMAGES"),
                Path.Combine(imageDirectory, "RADIO")
            };
            foreach (string partitionName in repairPartitionNames)
            {
                if (resolvedImages.ContainsKey(partitionName))
                {
                    continue;
                }

                foreach (string candidateDirectory in candidateDirectories)
                {
                    foreach (string extension in new[] { ".img", ".bin" })
                    {
                        string candidatePath =
                            Path.Combine(
                                candidateDirectory,
                                partitionName + extension);
                        if (File.Exists(candidatePath))
                        {
                            resolvedImages[partitionName] = candidatePath;
                            break;
                        }
                    }

                    if (resolvedImages.ContainsKey(partitionName))
                    {
                        break;
                    }
                }
            }

            return resolvedImages;
        }

        private static int GetFastbootdRepairImagePriority(
            string imagePath,
            string partitionName)
        {
            string fileName =
                Path.GetFileNameWithoutExtension(imagePath).Trim();
            if (fileName.Equals(
                    partitionName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }
            if (fileName.Equals(
                    $"{partitionName}_a",
                    StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }
            if (fileName.Equals(
                    $"{partitionName}_b",
                    StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }
            return 3;
        }

        // 修复FastbootD按钮点击事件
        private async void FixFastbootDButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _isOugaFlashTaskRunning = true;
                _ougaFlashStopRequested = false;

                // 禁用按钮防止重复操作
                var button = sender as System.Windows.Controls.Button;
                if (button != null)
                {
                    button.IsEnabled = false;
                }

                ShowProgressBar();
                
                LogToOugaFlash("开始修复FastbootD...", "Blue");
                LogToOugaFlash(
                    "此功能用于无法从Fastboot模式重启到FastbootD模式的情况，需要使用和变砖前系统版本一致的全量包进行修复，若不知道变砖前的系统版本，可使用快捷云提取功能.",
                    "Orange");

                string programDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string fastbootPath = Path.Combine(programDirectory, "platform-tools", "fastboot.exe");
                
                if (!File.Exists(fastbootPath))
                {
                    LogToOugaFlash("错误: 未找到fastboot.exe文件", "Red");
                    return;
                }

                // 1. 与正常线刷一致：等待Bootloader Fastboot并绑定同一台设备。
                string? deviceSerial = await WaitForOugaFastbootDeviceAsync(
                    "Fastboot",
                    requireFastbootd: false,
                    timeoutSeconds: OujiaFastbootDeviceWaitTimeoutSeconds,
                    customTitle: "等待Fastboot设备...",
                    showSerialWhenUnbound: false);
                if (string.IsNullOrWhiteSpace(deviceSerial))
                {
                    return;
                }

                if (!await EnsureOugaFastbootConnectionStableAsync(
                        deviceSerial,
                        "等待设备稳定...",
                        requireFastbootd: false))
                {
                    return;
                }
                
                // 2. 根据当前包模式获取镜像来源。售后包优先使用分区表中
                // 已解析的真实路径，全量包继续使用解包目录。
                bool useAfterSalesPackage =
                    AfterSalesPackageModeCheckBox.IsChecked == true;
                string imageDirectory = useAfterSalesPackage
                    ? AfterSalesFlashPackTextBox.Text.Trim()
                    : FolderPathTextBox.Text.Trim();
                bool isPlaceholderPath = useAfterSalesPackage
                    ? imageDirectory ==
                      "选择一个散包文件夹或将散包拖动到此自动选择..."
                    : imageDirectory.StartsWith(
                        "请选择解包好的文件夹",
                        StringComparison.Ordinal);
                if (string.IsNullOrEmpty(imageDirectory) || isPlaceholderPath)
                {
                    LogToOugaFlash(
                        useAfterSalesPackage
                            ? "错误: 请先选择售后散包文件夹..."
                            : "错误: 请先选择解包好的镜像文件夹...",
                        "Red");
                    return;
                }
                
                if (!Directory.Exists(imageDirectory))
                {
                    LogToOugaFlash("错误: 选择的文件夹路径不存在，请重新选择", "Red");
                    return;
                }
                
                // 3. 判断处理器平台类型
                
                Dictionary<string, string> repairImages =
                    ResolveFastbootdRepairImages(
                        imageDirectory,
                        useAfterSalesPackage);
                bool isMediaTek = repairImages.ContainsKey("lk");
                
                // 4. 根据处理器类型定义需要刷入的分区
                string[] partitionsToFlash;
                if (isMediaTek)
                {
                    partitionsToFlash = new string[] { "boot", "dtbo", "init_boot", "lk", "vbmeta", "vbmeta_system", "vbmeta_vendor", "vendor_boot" };
                }
                else
                {
                    partitionsToFlash = new string[] { "boot", "dtbo", "init_boot", "modem", "recovery", "vbmeta", "vbmeta_system", "vbmeta_vendor", "vendor_boot" };
                }
                LogToOugaFlashDual(
                    "设备处理器：",
                    "Black",
                    isMediaTek ? "联发科" : "高通骁龙",
                    isMediaTek ? "Purple" : "Blue");
                
                // 统计找到的关键分区数量
                int foundPartitions = 0;
                foreach (string partition in partitionsToFlash)
                {
                    if (repairImages.ContainsKey(partition))
                    {
                        foundPartitions++;
                    }
                }
                
                LogToOugaFlashTriple(
                    "找到 ",
                    "Black",
                    foundPartitions.ToString(),
                    "Purple",
                    " 个关键分区...",
                    "Black");
                
                // 5. 刷入分区到AB分区
                int successCount = 0;
                int totalAttempts = 0;
                
                foreach (string partition in partitionsToFlash)
                {
                    if (ShouldStopOugaFlashBeforeNextPartition(partition))
                    {
                        return;
                    }

                    // 查找对应的镜像文件
                    if (!repairImages.TryGetValue(
                            partition,
                            out string? imagePath))
                    {
                        LogToOugaFlash($"警告: 未找到 {partition} 镜像文件，跳过", "Orange");
                        continue;
                    }
                    
                    
                    
                    // 刷入到A分区
                    totalAttempts++;
                    LogStepBeginNoTime(
                        $"[Flashing] {Path.GetFileName(imagePath)} → {partition}_a.img");
                    bool flashAResult = await FlashPartitionToSlot(
                        partition,
                        imagePath,
                        "a",
                        deviceSerialOverride: deviceSerial);
                    if (flashAResult)
                    {
                        successCount++;
                        LogStepEndOk();
                    }
                    else
                    {
                        LogStepEndError();
                    }

                    if (ShouldStopOugaFlashBeforeNextPartition($"{partition}_b"))
                    {
                        return;
                    }
                    
                    // 刷入到B分区
                    totalAttempts++;
                    LogStepBeginNoTime(
                        $"[Flashing] {Path.GetFileName(imagePath)} → {partition}_b.img");
                    bool flashBResult = await FlashPartitionToSlot(
                        partition,
                        imagePath,
                        "b",
                        deviceSerialOverride: deviceSerial);
                    if (flashBResult)
                    {
                        successCount++;
                        LogStepEndOk();
                    }
                    else
                    {
                        LogStepEndError();
                    }
                    
                    // 如果A和B分区都刷入成功，显示完成信息
                    
                }
                
                
                if (successCount == 0)
                {
                    await Task.Delay(3000);
                    LogToOugaFlash("错误: 没有任何分区刷入成功，修复失败", "Red");
                    return;
                }

                if (ShouldFinalizeOugaFlashStop())
                {
                    return;
                }
                
                HideProgressBar();
                
                // 6. 重启到FastbootD模式
                LogStepBegin("重启设备到FastbootD");
                var rebootResult = await ExecuteOugaFastbootCommandAsync(
                    fastbootPath,
                    deviceSerial,
                    "reboot fastboot",
                    timeoutSeconds: 60,
                    acceptOkayOnTimeout: true);
                if (!rebootResult.Success)
                {
                    LogStepEndError();
                    AppendOugaFastbootFailureOutput(rebootResult.Output);
                    return;
                }
                LogStepEndOk();

                // 7. 使用正常线刷的120秒倒计时等待同一设备进入FastbootD。
                string? fastbootdSerial = await WaitForOugaFastbootDeviceAsync(
                    "FastbootD",
                    deviceSerial,
                    requireFastbootd: true,
                    timeoutSeconds: OujiaFastbootDeviceWaitTimeoutSeconds);
                if (string.IsNullOrWhiteSpace(fastbootdSerial))
                {
                    return;
                }

                if (!await EnsureOugaFastbootConnectionStableAsync(
                        fastbootdSerial,
                        "等待设备稳定...",
                        requireFastbootd: true))
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                LogToOugaFlash($"修复FastbootD发生错误: {ex.Message}", "Red");
            }
            finally
            {
                bool shouldRestartDeviceDetection = _ougaFlashStopRequested;

                if (shouldRestartDeviceDetection)
                {
                    try
                    {
                        LogStepBegin("委托主页开始异步设备检测");
                        HandleStartDeviceDetection();
                        LogStepEndOk();
                    }
                    catch (Exception ex)
                    {
                        LogStepEndError();
                        LogToOugaFlash($"恢复设备检测失败：{ex.Message}", "Red");
                    }
                }

                _isOugaFlashTaskRunning = false;
                _ougaFlashStopRequested = false;

                // 重新启用按钮
                var button = sender as System.Windows.Controls.Button;
                if (button != null)
                {
                    button.IsEnabled = true;
                }

                HideProgressBar();
            }
        }

        // 新增方法：刷入分区到指定槽位
        private async Task<bool> FlashPartitionToSlot(
            string partitionName,
            string imagePath,
            string slot,
            Action<long>? progressCallback = null,
            string? deviceSerialOverride = null)
        {
            string targetPartition = string.Empty;
            try
            {
                if (!File.Exists(imagePath))
                {
                    AppendOugaFastbootFailureOutput($"镜像文件不存在：{imagePath}");
                    return RecordOugaFlashPartitionTargetResult(
                        BuildOujiaRequestedTargetName(partitionName, slot),
                        false);
                }

                string fastbootPath = GetOugaFastbootExecutablePath();
                string deviceSerial = string.IsNullOrWhiteSpace(deviceSerialOverride)
                    ? GetOujiaFlashTargetSerial()
                    : deviceSerialOverride;
                if (_activeOujiaFlashSession != null)
                {
                    targetPartition = await ResolveOujiaTargetPartitionNameAsync(
                        partitionName,
                        slot,
                        deviceSerial) ?? string.Empty;
                }
                else
                {
                    targetPartition = BuildOujiaRequestedTargetName(partitionName, slot);
                }

                if (string.IsNullOrWhiteSpace(targetPartition))
                {
                    string unresolvedTarget = BuildOujiaRequestedTargetName(
                        partitionName,
                        slot);
                    AppendOugaFastbootFailureOutput(
                        $"设备分区表中不存在目标分区：{unresolvedTarget}");
                    return RecordOugaFlashPartitionTargetResult(unresolvedTarget, false);
                }

                currentFlashingPartition = targetPartition;
                string command = $"flash {targetPartition} \"{imagePath}\"";
                string commandArguments = BuildOugaFastbootArguments(deviceSerial, command);
                fastbootCompleteLog.AppendLine(
                    $"[{DateTime.Now:HH:mm:ss}] [FLASH BEGIN] {targetPartition} <= \"{imagePath}\"");
                fastbootCompleteLog.AppendLine(
                    $"[{DateTime.Now:HH:mm:ss}] [FASTBOOT CMD] {commandArguments}");

                var outputBuilder = new StringBuilder();
                object outputLock = new();
                object progressLock = new();
                long localFileBytes = new FileInfo(imagePath).Length;
                long commandTotalBytes = localFileBytes;
                long commandAccumulatedBytes = 0;
                long currentChunkExpectedBytes = 0;
                long currentChunkReportedBytes = 0;
                bool commandFailed = false;

                void UpdateProgressFromLine(string line)
                {
                    if (progressCallback == null)
                    {
                        return;
                    }

                    long? reportedBytes = null;
                    lock (progressLock)
                    {
                        Match sendMatch = Regex.Match(
                            line,
                            @"Sending(?:\s+sparse)?\s+'[^']+'(?:\s+\d+/\d+)?\s*\((?<size>\d+(?:\.\d+)?)\s*(?<unit>KB|MB|GB|B)\)",
                            RegexOptions.IgnoreCase);
                        if (sendMatch.Success &&
                            double.TryParse(
                                sendMatch.Groups["size"].Value,
                                NumberStyles.Float,
                                CultureInfo.InvariantCulture,
                                out double sentSize))
                        {
                            string unit = sendMatch.Groups["unit"].Value.ToUpperInvariant();
                            long sentBytes = unit switch
                            {
                                "GB" => (long)(sentSize * 1024 * 1024 * 1024),
                                "MB" => (long)(sentSize * 1024 * 1024),
                                "KB" => (long)(sentSize * 1024),
                                _ => (long)sentSize
                            };
                            commandTotalBytes = localFileBytes > 0 ? localFileBytes : sentBytes;
                            currentChunkExpectedBytes = sentBytes;
                            currentChunkReportedBytes = 0;
                        }

                        Match progressMatch = Regex.Match(
                            line,
                            @"^\s*\S+:\s+(?<cur>\d+(?:\.\d+)?)\s*(?<unit>KB|MB|GB|B)\/",
                            RegexOptions.IgnoreCase);
                        if (progressMatch.Success &&
                            double.TryParse(
                                progressMatch.Groups["cur"].Value,
                                NumberStyles.Float,
                                CultureInfo.InvariantCulture,
                                out double currentSize))
                        {
                            string unit = progressMatch.Groups["unit"].Value.ToUpperInvariant();
                            long currentBytes = unit switch
                            {
                                "GB" => (long)(currentSize * 1024 * 1024 * 1024),
                                "MB" => (long)(currentSize * 1024 * 1024),
                                "KB" => (long)(currentSize * 1024),
                                _ => (long)currentSize
                            };
                            if (currentChunkExpectedBytes > 0)
                            {
                                currentBytes = Math.Min(currentBytes, currentChunkExpectedBytes);
                            }
                            else if (commandTotalBytes > 0)
                            {
                                currentBytes = Math.Min(currentBytes, commandTotalBytes);
                            }

                            long delta = currentBytes - currentChunkReportedBytes;
                            if (delta < 0)
                            {
                                delta = currentBytes;
                            }
                            if (delta > 0)
                            {
                                commandAccumulatedBytes += delta;
                                if (localFileBytes > 0)
                                {
                                    commandAccumulatedBytes = Math.Min(
                                        commandAccumulatedBytes,
                                        localFileBytes);
                                }
                                currentChunkReportedBytes = currentBytes;
                                reportedBytes = commandAccumulatedBytes;
                            }
                        }

                        if (line.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ||
                            Regex.IsMatch(line, @"\berror\b", RegexOptions.IgnoreCase))
                        {
                            commandFailed = true;
                        }

                        // Fastboot会把大镜像拆成多个sparse块。Writing只代表当前块完成，
                        // 不能在第一个Writing时把整张镜像直接记为100%。
                        if (!commandFailed &&
                            line.Contains("Writing '", StringComparison.OrdinalIgnoreCase) &&
                            currentChunkExpectedBytes > currentChunkReportedBytes)
                        {
                            commandAccumulatedBytes +=
                                currentChunkExpectedBytes - currentChunkReportedBytes;
                            if (localFileBytes > 0)
                            {
                                commandAccumulatedBytes = Math.Min(
                                    commandAccumulatedBytes,
                                    localFileBytes);
                            }
                            currentChunkReportedBytes = currentChunkExpectedBytes;
                            reportedBytes = commandAccumulatedBytes;
                        }

                        if (!commandFailed &&
                            line.StartsWith("Finished.", StringComparison.OrdinalIgnoreCase))
                        {
                            long finalBytes = commandTotalBytes > 0
                                ? commandTotalBytes
                                : commandAccumulatedBytes;
                            if (finalBytes > commandAccumulatedBytes)
                            {
                                commandAccumulatedBytes = finalBytes;
                                reportedBytes = commandAccumulatedBytes;
                            }
                        }
                    }

                    if (reportedBytes.HasValue)
                    {
                        progressCallback(reportedBytes.Value);
                    }
                }

                void CaptureLine(string? line, bool isError)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        return;
                    }

                    lock (outputLock)
                    {
                        outputBuilder.AppendLine(line);
                        fastbootCompleteLog.AppendLine(
                            $"[{DateTime.Now:HH:mm:ss}] [{(isError ? "FASTBOOT ERROR" : "FASTBOOT OUTPUT")}] {line}");
                    }

                    UpdateProgressFromLine(line);
                    Dispatcher.BeginInvoke(
                        DispatcherPriority.Background,
                        new Action(() => ParseFastbootProgress(line)));
                }

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fastbootPath,
                        Arguments = commandArguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    }
                };
                process.OutputDataReceived += (_, eventArgs) => CaptureLine(eventArgs.Data, false);
                process.ErrorDataReceived += (_, eventArgs) => CaptureLine(eventArgs.Data, true);

                if (!process.Start())
                {
                    AppendOugaFastbootFailureOutput("无法启动fastboot刷写进程");
                    return RecordOugaFlashPartitionTargetResult(targetPartition, false);
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // 分区刷写不使用固定总时长超时。停止请求只在当前分区自然结束后生效，
                // 避免慢速USB或大镜像超过五分钟时强杀fastboot并中断正在写入的分区。
                await process.WaitForExitAsync().ConfigureAwait(false);
                process.WaitForExit();

                string output;
                lock (outputLock)
                {
                    output = outputBuilder.ToString();
                }

                if (process.ExitCode != 0 || IsOugaFastbootFailureOutput(output))
                {
                    fastbootCompleteLog.AppendLine(
                        $"[{DateTime.Now:HH:mm:ss}] [FLASH END] {targetPartition} (exit {process.ExitCode})");
                    AppendOugaFastbootFailureOutput(output);
                    return RecordOugaFlashPartitionTargetResult(targetPartition, false);
                }

                fastbootCompleteLog.AppendLine(
                    $"[{DateTime.Now:HH:mm:ss}] [FLASH END] {targetPartition}");
                return RecordOugaFlashPartitionTargetResult(targetPartition, true);
            }
            catch (Exception ex)
            {
                AppendOugaFastbootFailureOutput($"fastboot刷写异常：{ex.Message}");
                string failedTarget = string.IsNullOrWhiteSpace(targetPartition)
                    ? BuildOujiaRequestedTargetName(partitionName, slot)
                    : targetPartition;
                return RecordOugaFlashPartitionTargetResult(failedTarget, false);
            }
            finally
            {
                currentFlashingPartition = string.Empty;
            }
        }

        // 选择Payload.bin文件或全量包ZIP文件
        private void SelectPayloadFileButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择Payload.bin文件或全量包ZIP文件",
                Filter = "Payload.bin或ZIP文件 (*.bin;*.zip)|*.bin;*.zip|Payload文件 (*.bin)|*.bin|ZIP压缩包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
                FilterIndex = 1,
                FileName = "Payload.bin"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                PayloadFilePathTextBox.Text = openFileDialog.FileName;
                // 可以添加日志输出
                // LogToFlashTextBox($"已选择Payload文件: {openFileDialog.FileName}");
            }
        }
        
        // 从文件夹加载分区信息
        private void LoadPartitionsFromFolder(string folderPath)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    AppendOugaFlashParagraphLog($"错误: 文件夹不存在: {folderPath}", "Red");
                    OugaFlashLogTextBox.ScrollToEnd();
                    return;
                }
                
                var partitions = new List<PartitionInfo>();
                var imageFiles = Directory.GetFiles(folderPath, "*.img", SearchOption.TopDirectoryOnly);
                
                AppendOugaFlashParagraphLog("正在解析分区文件...");
                OugaFlashLogTextBox.ScrollToEnd();
                
                foreach (var imageFile in imageFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(imageFile);
                    var fileInfo = new FileInfo(imageFile);
                    
                    var partition = new PartitionInfo
                    {
                        PartitionName = fileName,
                        PartitionSize = FormatFileSize(fileInfo.Length),
                        FilePath = imageFile,
                        IsSelected = false
                    };
                    
                    partitions.Add(partition);
                }
                
                // 按分区名称排序
                partitions = partitions.OrderBy(p => p.PartitionName).ToList();
                
                // 更新DataGrid
                OugaPartitionTableDataGrid.ItemsSource = new ObservableCollection<PartitionInfo>(partitions);
                
                // 使用Dispatcher确保在UI线程上执行SelectAll
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    OugaPartitionTableDataGrid.SelectAll();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
                
                AppendOugaFlashParagraphLog($"成功加载 {partitions.Count} 个分区文件", "Green");
                OugaFlashLogTextBox.ScrollToEnd();
            }
            catch (Exception ex)
            {
                AppendOugaFlashParagraphLog($"加载分区文件失败: {ex.Message}", "Red");
                OugaFlashLogTextBox.ScrollToEnd();
            }
        }
        
        // 提取分区按钮点击事件
        private async void ExtractPartitionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Controls.ComboBox? activePartitionComboBox = sender == AfterSalesExtractPartitionButton
                    ? AfterSalesPayloadPartitionComboBox
                    : PayloadPartitionComboBox;

                // 获取用户输入的分区名称（支持下拉选择和直接输入）
                string partitionName = activePartitionComboBox?.Text ?? string.Empty;
                
                if (string.IsNullOrEmpty(partitionName) || partitionName == "分区名")
                {
                    AppendOugaFlashParagraphLog("错误: 请选择或输入要提取的分区名称", "Red");
                    OugaFlashLogTextBox.ScrollToEnd();
                    return;
                }
                
                string selectedOutputPath;
                
                // 检查是否选择了"高通修复FastbootD关键分区"
                if (partitionName == "高通修复FastbootD关键分区")
                {
                    // 自动在桌面创建Smart Tool Download文件夹
                    string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    selectedOutputPath = Path.Combine(desktopPath, "Smart Tool Download");
                    
                    // 确保文件夹存在
                    if (!Directory.Exists(selectedOutputPath))
                    {
                        Directory.CreateDirectory(selectedOutputPath);
                        AppendOugaFlashParagraphLog("已在桌面创建Smart Tool Download文件夹", "Green");
                    }
                    else
                    {
                        AppendOugaFlashParagraphLog("使用现有的Smart Tool Download文件夹", "Gray");
                    }
                    
                    // 自动提取关键分区：boot, recovery, dtbo, modem, vbmeta, vendor_boot
                    string[] criticalPartitions = { "boot", "recovery", "dtbo", "modem", "vbmeta", "vendor_boot", "init_boot", "vbmeta_system", "vbmeta_vendor" };
                    
                    AppendOugaFlashParagraphLog("检测到高通修复FastbootD关键分区模式", "Blue");
                    AppendOugaFlashParagraphLog($"将自动提取以下分区: {string.Join(", ", criticalPartitions)}");
                    AppendOugaFlashParagraphLog($"保存路径: {selectedOutputPath}", "Gray");
                    OugaFlashLogTextBox.ScrollToEnd();

                    string? qcomSourceInput = ResolveOugaPayloadSourceInput();
                    if (string.IsNullOrEmpty(qcomSourceInput))
                    {
                        AppendOugaFlashParagraphLog("错误: 请先选择Payload.bin文件、全量包ZIP文件，或输入有效的全量包链接", "Red");
                        return;
                    }

                    var qcomExtractedPartitions = await ExtractPartitionsWithPayloadProcessingAsync(
                        qcomSourceInput,
                        criticalPartitions,
                        selectedOutputPath,
                        "开始提取高通修复FastbootD关键分区...");

                    if (qcomExtractedPartitions.Count > 0)
                    {
                        AppendOugaFlashParagraphLog("高通修复FastbootD关键分区提取完成！", "Green");
                        await LoadExtractedPartitionsToDataGrid(selectedOutputPath, qcomExtractedPartitions.ToArray());
                        FolderPathTextBox.Text = selectedOutputPath;
                    }
                    return;
                }
                
                // 检查是否选择了"联发科修复FastbootD关键分区"
                if (partitionName == "联发科修复FastbootD关键分区")
                {
                    // 自动在桌面创建Smart Tool Download文件夹
                    string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    selectedOutputPath = Path.Combine(desktopPath, "Smart Tool Download");
                    
                    // 确保文件夹存在
                    if (!Directory.Exists(selectedOutputPath))
                    {
                        Directory.CreateDirectory(selectedOutputPath);
                        AppendOugaFlashParagraphLog("已在桌面创建Smart Tool Download文件夹", "Green");
                    }
                    else
                    {
                        AppendOugaFlashParagraphLog("使用现有的Smart Tool Download文件夹", "Gray");
                    }
                    
                    // 自动提取关键分区：boot，dtbo， init_boot，lk，vbmeta，vbmeta_system，vbmeta_vendor，vendor_boot
                    string[] criticalPartitions = { "boot", "init_boot", "dtbo", "lk", "vbmeta", "vendor_boot", "vbmeta_system", "vbmeta_vendor" };
                    
                    AppendOugaFlashParagraphLog("检测到联发科修复FastbootD关键分区模式", "Blue");
                    AppendOugaFlashParagraphLog($"将自动提取以下分区: {string.Join(", ", criticalPartitions)}");
                    AppendOugaFlashParagraphLog($"保存路径: {selectedOutputPath}", "Gray");
                    OugaFlashLogTextBox.ScrollToEnd();

                    string? mtkSourceInput = ResolveOugaPayloadSourceInput();
                    if (string.IsNullOrEmpty(mtkSourceInput))
                    {
                        AppendOugaFlashParagraphLog("错误: 请先选择Payload.bin文件、全量包ZIP文件，或输入有效的全量包链接", "Red");
                        return;
                    }

                    var mtkExtractedPartitions = await ExtractPartitionsWithPayloadProcessingAsync(
                        mtkSourceInput,
                        criticalPartitions,
                        selectedOutputPath,
                        "开始提取联发科修复FastbootD关键分区...");

                    if (mtkExtractedPartitions.Count > 0)
                    {
                        AppendOugaFlashParagraphLog("联发科修复FastbootD关键分区提取完成！", "Green");
                        await LoadExtractedPartitionsToDataGrid(selectedOutputPath, mtkExtractedPartitions.ToArray());
                        FolderPathTextBox.Text = selectedOutputPath;
                    }
                    return;
                }
                
                // 检查是否选择了"云解包方案-从云端提取线刷文件"
                if (partitionName == "云解包方案-从云端提取线刷文件")
                {
                    await HandleCloudExtractionAsync();
                    return;
                }
                
                // 让用户选择保存路径
                var folderDialog = new System.Windows.Forms.FolderBrowserDialog()
                {
                    Description = "选择提取文件的保存路径",
                    ShowNewFolderButton = true
                };
                
                if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    AppendOugaFlashParagraphLog("用户取消了文件夹选择", "Yellow");
                    OugaFlashLogTextBox.ScrollToEnd();
                    return;
                }
                
                selectedOutputPath = folderDialog.SelectedPath;

                string? sourceInput = ResolveOugaPayloadSourceInput();
                if (string.IsNullOrEmpty(sourceInput))
                {
                    AppendOugaFlashParagraphLog("错误: 请先选择Payload.bin文件、全量包ZIP文件，或输入有效的全量包链接", "Red");
                    return;
                }

                var extractedPartitions = await ExtractPartitionsWithPayloadProcessingAsync(
                    sourceInput,
                    new[] { partitionName },
                    selectedOutputPath,
                    $"开始提取分区: {partitionName}");

                if (extractedPartitions.Count > 0)
                {
                    AppendOugaFlashParagraphLog($"分区 {partitionName} 提取完成！", "Green");
                    await LoadExtractedPartitionsToDataGrid(selectedOutputPath, extractedPartitions.ToArray());
                    FolderPathTextBox.Text = selectedOutputPath;
                }
            }
            catch (Exception ex)
            {
                AppendOugaFlashParagraphLog($"提取分区过程中发生错误: {ex.Message}", "Red");
            }
        }
        
        // 提取单个分区的辅助方法
        private async Task ExtractSinglePartition(string partitionName, string outputPath)
        {
            try
            {
                string? sourceInput = ResolveOugaPayloadSourceInput();
                if (string.IsNullOrEmpty(sourceInput))
                {
                    AppendOugaFlashParagraphLog("错误: 请先选择Payload.bin文件、全量包ZIP文件，或输入有效的全量包链接", "Red");
                    return;
                }

                var extractedPartitions = await ExtractPartitionsWithPayloadProcessingAsync(
                    sourceInput,
                    new[] { partitionName },
                    outputPath,
                    $"开始提取分区: {partitionName}");

                if (extractedPartitions.Count > 0)
                {
                    AppendOugaFlashParagraphLog($"分区 {partitionName} 提取完成！", "Green");
                }
            }
            catch (Exception ex)
            {
                AppendOugaFlashParagraphLog($"提取分区 {partitionName} 时发生错误: {ex.Message}", "Red");
            }
        }

        // 解析fastboot输出中的百分比和传输速率并更新显示
        private void ParseAndUpdateProgress(string output)
        {
            try
            {
                bool progressUpdated = false;
                
                // 首先查找百分比模式，如 "50%" 或 "(50%)" 或 "50.5%"
                var percentageMatch = System.Text.RegularExpressions.Regex.Match(output, @"(\d+(?:\.\d+)?)%");
                
                if (percentageMatch.Success)
                {
                    if (double.TryParse(percentageMatch.Groups[1].Value, out double percentage))
                    {
                        // 确保百分比在0-100范围内
                        percentage = Math.Max(0, Math.Min(100, percentage));
                        
                        Dispatcher.Invoke(() =>
                        {
                            // 更新进度条
                            FlashProgressBar.Value = percentage;
                            FlashProgressBar.IsIndeterminate = false;
                            
                            // 显示进度条容器（如果之前隐藏的话）
                            if (ProgressBarContainer.Visibility == Visibility.Collapsed)
                            {
                                ProgressBarContainer.Visibility = Visibility.Visible;
                            }
                        });
                        
                        // 移除详细日志输出，只更新进度条
                        progressUpdated = true;
                    }
                }
                
                // 然后查找传输速率模式，如 "24.5 MB/s" 或 "1.2GB/s"
                var transferRateMatch = System.Text.RegularExpressions.Regex.Match(output, @"(\d+(?:\.\d+)?)\s*(MB/s|GB/s|KB/s)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                if (transferRateMatch.Success)
                {
                    string rateValue = transferRateMatch.Groups[1].Value;
                    string rateUnit = transferRateMatch.Groups[2].Value;
                    string displayRate = $"{rateValue}{rateUnit}";
                    
                    // 更新传输速率显示
                    Dispatcher.Invoke(() =>
                    {
                        TransferRateTextBlock.Text = displayRate;
                        
                        // 如果没有百分比更新，显示进度条容器
                        if (!progressUpdated && ProgressBarContainer.Visibility == Visibility.Collapsed)
                        {
                            ProgressBarContainer.Visibility = Visibility.Visible;
                        }
                    });
                    
                    // 移除详细日志输出，只更新传输速率显示
                }
                
                // 检查是否包含"Sending"关键字，表示开始传输
                if (output.Contains("Sending"))
                {
                    Dispatcher.Invoke(() =>
                    {
                        // 显示进度条容器并重置显示
                        ProgressBarContainer.Visibility = Visibility.Visible;
                        FlashProgressBar.IsIndeterminate = false;
                        FlashProgressBar.Value = 0;
                        TransferRateTextBlock.Text = "0MB/s";
                    });
                    // 移除详细日志输出
                }
                // 检查是否包含"OKAY"关键字，表示完成
                else if (output.Contains("OKAY"))
                {
                    Dispatcher.Invoke(() =>
                    {
                        // 设置进度条为100%
                        FlashProgressBar.IsIndeterminate = false;
                        FlashProgressBar.Value = 100;
                        TransferRateTextBlock.Text = "刷写成功";
                    });
                    // 移除详细日志输出
                }
            }
            catch (Exception ex)
            {
                // 静默处理解析错误，不影响主要功能
                System.Diagnostics.Debug.WriteLine($"解析进度时出错: {ex.Message}");
            }
        }

        // 重置进度条和传输速率显示
        private void ResetProgressBar()
        {
            Dispatcher.Invoke(() =>
            {
                if (FlashProgressBar != null)
                {
                    FlashProgressBar.Value = 0;
                    FlashProgressBar.IsIndeterminate = false;
                }

                if (TransferRateTextBlock != null)
                {
                    TransferRateTextBlock.Text = "0MB/s";
                }

                if (AfterSalesFlashProgressBar != null)
                {
                    AfterSalesFlashProgressBar.Value = 0;
                    AfterSalesFlashProgressBar.IsIndeterminate = false;
                }

                if (AfterSalesTransferRateTextBlock != null)
                {
                    AfterSalesTransferRateTextBlock.Text = "0MB/s";
                }

                if (bootflash != null)
                {
                    bootflash.Value = 0;
                }
            });
        }

        // 显示进度条和初始化传输速率显示
        private void ShowProgressBar()
        {
            Dispatcher.Invoke(() =>
            {
                // 全量包模式进度条
                if (ProgressBarContainer != null)
                {
                    ProgressBarContainer.Visibility = Visibility.Visible;
                    FlashProgressBar.Value = 0;
                    FlashProgressBar.IsIndeterminate = false;
                    TransferRateTextBlock.Text = "0MB/s";
                }
                
                // 售后包模式进度条
                if (AfterSalesProgressBarContainer != null)
                {
                    AfterSalesProgressBarContainer.Visibility = Visibility.Visible;
                    AfterSalesFlashProgressBar.Value = 0;
                    AfterSalesFlashProgressBar.IsIndeterminate = false;
                    AfterSalesTransferRateTextBlock.Text = "0MB/s";
                }
            });
        }

        private void HideProgressBar()
        {
            Dispatcher.Invoke(() =>
            {
                // 进度条常显，不隐藏，只重置进度值
                
                // 全量包模式进度条
                if (ProgressBarContainer != null)
                {
                    FlashProgressBar.Value = 0;
                    FlashProgressBar.IsIndeterminate = false;
                    TransferRateTextBlock.Text = "0MB/s";
                }
                
                // 售后包模式进度条
                if (AfterSalesProgressBarContainer != null)
                {
                    AfterSalesFlashProgressBar.Value = 0;
                    AfterSalesFlashProgressBar.IsIndeterminate = false;
                    AfterSalesTransferRateTextBlock.Text = "0MB/s";
                }
            });
        }

        // 辅助方法：同时更新两个进度条的值
        private void UpdateProgressBarValue(double value)
        {
            if (FlashProgressBar != null)
            {
                FlashProgressBar.Value = value;
                FlashProgressBar.IsIndeterminate = false;
            }
            if (AfterSalesFlashProgressBar != null)
            {
                AfterSalesFlashProgressBar.Value = value;
                AfterSalesFlashProgressBar.IsIndeterminate = false;
            }
        }

        // 辅助方法：统一更新 BootFlash 区域的速率显示
        private void UpdateTransferRateText(string text)
        {
            Dispatcher.Invoke(() =>
            {
                if (bootflash != null)
                {
                    bootflash.Tag = text;
                }

                if (TransferRateTextBlock != null)
                {
                    TransferRateTextBlock.Text = text;
                }

                if (AfterSalesTransferRateTextBlock != null)
                {
                    AfterSalesTransferRateTextBlock.Text = text;
                }
            });
        }

        private enum ArbDetectionMode
        {
            CurrentDevice,
            FirmwareImage
        }

        private sealed class ArbDetectionSelection
        {
            public ArbDetectionMode Mode { get; init; }
            public string FirmwareImagePath { get; init; } = string.Empty;
        }

        // 先选择检测来源，再使用同一套 C# xbl_config 解析器读取 ARB 索引。
        private async void ArbFuseCheckButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isOugaFlashTaskRunning)
            {
                LogToOugaFlash("当前有刷写任务正在运行，暂时无法执行ARB熔断检测", "Orange");
                return;
            }

            ArbDetectionSelection? detectionSelection = ShowArbDetectionModeDialog();
            if (detectionSelection == null)
            {
                return;
            }

            var button = sender as System.Windows.Controls.Button;
            if (button != null)
            {
                button.IsEnabled = false;
            }

            try
            {
                if (detectionSelection.Mode == ArbDetectionMode.CurrentDevice)
                {
                    await DetectCurrentDeviceArbAsync();
                }
                else
                {
                    await DetectFirmwareImageArbAsync(detectionSelection.FirmwareImagePath);
                }
            }
            catch (Exception ex)
            {
                bool isNoAdbDeviceError =
                    detectionSelection.Mode == ArbDetectionMode.CurrentDevice &&
                    ex.Message.StartsWith(
                        "未检测到ADB设备；Fastboot模式无法可靠回读xbl_config",
                        StringComparison.Ordinal);
                bool isShellRootPermissionError =
                    detectionSelection.Mode == ArbDetectionMode.CurrentDevice &&
                    ex.Message.StartsWith(
                        "未授予Shell ROOT权限",
                        StringComparison.Ordinal);
                if (!isNoAdbDeviceError && !isShellRootPermissionError)
                {
                    LogOugaArbDetail("检测失败：", ex.Message, "Red", emphasized: true);
                }
            }
            finally
            {
                if (button != null)
                {
                    button.IsEnabled = true;
                }
            }
        }

        private ArbDetectionSelection? ShowArbDetectionModeDialog()
        {
            var dialog = new System.Windows.Window
            {
                Title = "ARB熔断检测",
                Owner = this,
                Width = 470,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Background = System.Windows.Media.Brushes.White,
                FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI")
            };

            var root = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(24, 20, 24, 20)
            };

            var currentDeviceRadio = new System.Windows.Controls.RadioButton
            {
                GroupName = "ArbDetectionMode",
                IsChecked = true,
                VerticalAlignment = VerticalAlignment.Center
            };
            var currentDeviceCard = CreateArbDetectionModeCard(
                currentDeviceRadio,
                "检测当前设备是否熔断",
                "设备需要有ROOT，然后打开ROOT管理器，并给予'shell' ROOT权限，点击开始检测后程序会自动检测设备当前设备是否熔断.");
            root.Children.Add(currentDeviceCard);

            var firmwareRadio = new System.Windows.Controls.RadioButton
            {
                GroupName = "ArbDetectionMode",
                VerticalAlignment = VerticalAlignment.Center
            };
            var firmwareCard = CreateArbDetectionModeCard(
                firmwareRadio,
                "检测固件包是否熔断",
                "请选择固件包中的xbl_config.img文件");
            firmwareCard.Margin = new Thickness(0, 10, 0, 0);
            root.Children.Add(firmwareCard);

            string selectedFirmwareImagePath = string.Empty;
            var firmwareFilePathTextBox = new System.Windows.Controls.TextBox
            {
                Height = 32,
                IsReadOnly = true,
                Text = "未选择文件",
                Padding = new Thickness(10, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = System.Windows.Media.Brushes.White,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(148, 163, 184)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(221, 226, 234)),
                BorderThickness = new Thickness(1)
            };

            var selectFirmwareFileButtonContent = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            string folderIconPath = IOPath.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "images",
                "文件夹.svg");
            if (IOFile.Exists(folderIconPath))
            {
                selectFirmwareFileButtonContent.Children.Add(new SvgViewbox
                {
                    Source = new Uri(folderIconPath, UriKind.Absolute),
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(0, 0, 5, 0)
                });
            }
            selectFirmwareFileButtonContent.Children.Add(new TextBlock
            {
                Text = "选择",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = System.Windows.Media.Brushes.White
            });

            var selectFirmwareFileButton = new System.Windows.Controls.Button
            {
                Content = selectFirmwareFileButtonContent,
                Style = OujiaFlashView?.TryFindResource("OugaFlashPrimaryButtonStyle") as Style,
                Width = 84,
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0),
                Background = System.Windows.Media.Brushes.SkyBlue,
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(230, 230, 230)),
                Foreground = System.Windows.Media.Brushes.White,
                Cursor = WpfCursors.Hand
            };

            var firmwareFileGrid = new System.Windows.Controls.Grid
            {
                Margin = new Thickness(0, 8, 0, 0)
            };
            firmwareFileGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            firmwareFileGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            {
                Width = GridLength.Auto
            });
            System.Windows.Controls.Grid.SetColumn(firmwareFilePathTextBox, 0);
            System.Windows.Controls.Grid.SetColumn(selectFirmwareFileButton, 1);
            firmwareFileGrid.Children.Add(firmwareFilePathTextBox);
            firmwareFileGrid.Children.Add(selectFirmwareFileButton);

            var firmwareFileErrorText = new TextBlock
            {
                Text = "请先选择有效的xbl_config.img文件",
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(225, 85, 98)),
                Visibility = Visibility.Collapsed
            };

            var firmwareFilePanel = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(2, 12, 2, 0),
                Visibility = Visibility.Collapsed
            };
            firmwareFilePanel.Children.Add(new TextBlock
            {
                Text = "请选择本地固件包中的xbl.config.img文件：",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(51, 65, 85)),
                TextWrapping = TextWrapping.Wrap
            });
            firmwareFilePanel.Children.Add(firmwareFileGrid);
            firmwareFilePanel.Children.Add(firmwareFileErrorText);
            root.Children.Add(firmwareFilePanel);

            selectFirmwareFileButton.Click += (_, _) =>
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择固件包中的xbl_config镜像",
                    Filter = "XBL配置镜像 (xbl_config*.img)|xbl_config*.img|镜像文件 (*.img)|*.img|所有文件 (*.*)|*.*",
                    FilterIndex = 1,
                    FileName = "xbl_config.img",
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (openFileDialog.ShowDialog(dialog) == true)
                {
                    selectedFirmwareImagePath = openFileDialog.FileName;
                    firmwareFilePathTextBox.Text = selectedFirmwareImagePath;
                    firmwareFilePathTextBox.ToolTip = selectedFirmwareImagePath;
                    firmwareFilePathTextBox.Foreground = new SolidColorBrush(MediaColor.FromRgb(51, 65, 85));
                    firmwareFileErrorText.Visibility = Visibility.Collapsed;
                }
            };

            root.Children.Add(new TextBlock
            {
                Text = "熔断机制只存在于骁龙设备的8Elite处理器及以下，天玑处理器的设备不存在熔断机制，可任意降级，骁龙熔断设备降级非熔断版本会导致黑砖！",
                Margin = new Thickness(2, 12, 2, 0),
                FontSize = 11.5,
                LineHeight = 18,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(217, 119, 6))
            });

            void UpdateCardState()
            {
                bool currentSelected = currentDeviceRadio.IsChecked == true;
                currentDeviceCard.Background = new SolidColorBrush(MediaColor.FromRgb(
                    currentSelected ? (byte)247 : (byte)250,
                    currentSelected ? (byte)242 : (byte)251,
                    currentSelected ? (byte)255 : (byte)253));
                currentDeviceCard.BorderBrush = new SolidColorBrush(currentSelected
                    ? MediaColor.FromRgb(196, 181, 253)
                    : MediaColor.FromRgb(226, 232, 240));

                bool firmwareSelected = firmwareRadio.IsChecked == true;
                firmwareCard.Background = new SolidColorBrush(MediaColor.FromRgb(
                    firmwareSelected ? (byte)247 : (byte)250,
                    firmwareSelected ? (byte)242 : (byte)251,
                    firmwareSelected ? (byte)255 : (byte)253));
                firmwareCard.BorderBrush = new SolidColorBrush(firmwareSelected
                    ? MediaColor.FromRgb(196, 181, 253)
                    : MediaColor.FromRgb(226, 232, 240));
                firmwareFilePanel.Visibility = firmwareSelected
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            currentDeviceRadio.Checked += (_, _) => UpdateCardState();
            firmwareRadio.Checked += (_, _) => UpdateCardState();
            currentDeviceCard.PreviewMouseLeftButtonDown += (_, _) => currentDeviceRadio.IsChecked = true;
            firmwareCard.PreviewMouseLeftButtonDown += (_, _) => firmwareRadio.IsChecked = true;
            UpdateCardState();

            var buttonPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };
            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "取消",
                Width = 88,
                Height = 32,
                Margin = new Thickness(0, 0, 10, 0),
                Background = System.Windows.Media.Brushes.White,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(71, 85, 105)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(203, 213, 225)),
                IsCancel = true
            };
            var confirmButton = new System.Windows.Controls.Button
            {
                Content = "开始检测",
                Width = 98,
                Height = 32,
                Background = new SolidColorBrush(MediaColor.FromRgb(184, 118, 221)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(184, 118, 221)),
                Foreground = System.Windows.Media.Brushes.White,
                IsDefault = true
            };
            cancelButton.Click += (_, _) => dialog.DialogResult = false;
            confirmButton.Click += (_, _) =>
            {
                if (firmwareRadio.IsChecked == true &&
                    (string.IsNullOrWhiteSpace(selectedFirmwareImagePath) ||
                     !IOFile.Exists(selectedFirmwareImagePath)))
                {
                    firmwareFileErrorText.Visibility = Visibility.Visible;
                    return;
                }

                dialog.DialogResult = true;
            };
            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(confirmButton);
            root.Children.Add(buttonPanel);

            dialog.Content = root;
            if (dialog.ShowDialog() != true)
            {
                return null;
            }

            return new ArbDetectionSelection
            {
                Mode = firmwareRadio.IsChecked == true
                    ? ArbDetectionMode.FirmwareImage
                    : ArbDetectionMode.CurrentDevice,
                FirmwareImagePath = selectedFirmwareImagePath
            };
        }

        private static System.Windows.Controls.Border CreateArbDetectionModeCard(
            System.Windows.Controls.RadioButton radioButton,
            string title,
            string description)
        {
            var textPanel = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(10, 0, 0, 0)
            };
            textPanel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(51, 65, 85))
            });
            textPanel.Children.Add(new TextBlock
            {
                Text = description,
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(100, 116, 139))
            });

            var contentGrid = new System.Windows.Controls.Grid();
            contentGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            {
                Width = GridLength.Auto
            });
            contentGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            System.Windows.Controls.Grid.SetColumn(radioButton, 0);
            System.Windows.Controls.Grid.SetColumn(textPanel, 1);
            contentGrid.Children.Add(radioButton);
            contentGrid.Children.Add(textPanel);

            return new System.Windows.Controls.Border
            {
                Padding = new Thickness(14, 13, 14, 13),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(226, 232, 240)),
                Background = new SolidColorBrush(MediaColor.FromRgb(250, 251, 253)),
                Cursor = WpfCursors.Hand,
                Child = contentGrid
            };
        }

        private async Task DetectFirmwareImageArbAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !IOFile.Exists(filePath))
            {
                throw new FileNotFoundException("请选择有效的xbl_config.img文件", filePath);
            }

            LogToOugaFlashDual("xbl_config路径：", "Black", filePath, "Blue");
            XblArbResult result = await Task.Run(() => ParseXblConfigArb(filePath));
            LogOugaArbDetail(
                "OEM元数据：",
                $"Major {result.MajorVersion} / Minor {result.MinorVersion}",
                "Blue");
            LogFirmwareArbResult(result.ArbIndex);
        }

        private async Task DetectCurrentDeviceArbAsync()
        {
            string adbPath = GetToolPath("adb.exe");
            if (string.IsNullOrWhiteSpace(adbPath) || !File.Exists(adbPath))
            {
                throw new FileNotFoundException("未找到adb.exe，无法读取当前设备的xbl_config");
            }

            LogOugaArbStepBegin("连接ADB设备");
            string targetSerial;
            try
            {
                string devicesOutput = await GetCommandOutput(adbPath, "devices");
                var adbDevices = devicesOutput
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => Regex.Match(
                        line.Trim(),
                        @"^(?<serial>\S+)\s+(?<state>device|recovery|unauthorized|offline|no permissions)(?:\s.*)?$",
                        RegexOptions.IgnoreCase))
                    .Where(match => match.Success)
                    .Select(match => new
                    {
                        Serial = match.Groups["serial"].Value,
                        State = match.Groups["state"].Value.ToLowerInvariant()
                    })
                    .ToList();

                string selectedSerial = GetOujiaFlashTargetSerial();
                var targetDevice = !string.IsNullOrWhiteSpace(selectedSerial)
                    ? adbDevices.FirstOrDefault(device =>
                        device.Serial.Equals(selectedSerial, StringComparison.OrdinalIgnoreCase))
                    : adbDevices.Count == 1 ? adbDevices[0] : null;

                if (adbDevices.Count == 0)
                {
                    throw new InvalidOperationException(
                        "未检测到ADB设备；Fastboot模式无法可靠回读xbl_config，请进入系统ROOT或Recovery后重试");
                }

                if (string.IsNullOrWhiteSpace(selectedSerial) && adbDevices.Count > 1)
                {
                    throw new InvalidOperationException("检测到多个ADB设备，请先在主页设备列表中选择目标设备");
                }

                if (targetDevice == null)
                {
                    throw new InvalidOperationException("当前选中的设备不在ADB模式，请重新选择设备后重试");
                }

                if (!targetDevice.State.Equals("device", StringComparison.OrdinalIgnoreCase) &&
                    !targetDevice.State.Equals("recovery", StringComparison.OrdinalIgnoreCase))
                {
                    string stateMessage = targetDevice.State switch
                    {
                        "unauthorized" => "ADB设备未授权，请在手机上允许USB调试",
                        "offline" => "ADB设备处于离线状态，请重新连接USB",
                        "no permissions" => "ADB设备无访问权限，请检查ADB驱动和USB权限",
                        _ => "ADB设备当前不可用"
                    };
                    throw new InvalidOperationException(stateMessage);
                }

                targetSerial = targetDevice.Serial;
                LogOugaArbStepEnd($"[{targetSerial}]", "Blue", emphasized: true);
            }
            catch (Exception ex)
            {
                bool isNoAdbDeviceError = ex.Message.StartsWith(
                    "未检测到ADB设备；Fastboot模式无法可靠回读xbl_config",
                    StringComparison.Ordinal);
                LogOugaArbStepEnd(
                    isNoAdbDeviceError ? "无设备连接" : "Error",
                    "Red",
                    emphasized: true);
                throw;
            }

            string serialPrefix = $"-s {targetSerial} ";
            bool useSu;
            LogOugaArbStepBegin("检查ROOT权限");
            try
            {
                string directIdOutput = await GetCommandOutput(
                    adbPath,
                    $"{serialPrefix}shell id");
                if (Regex.IsMatch(directIdOutput ?? string.Empty, @"\buid=0\b", RegexOptions.IgnoreCase))
                {
                    useSu = false;
                }
                else
                {
                    string suIdOutput = await GetCommandOutput(
                        adbPath,
                        $"{serialPrefix}shell \"su -c 'id'\"");
                    if (!Regex.IsMatch(suIdOutput ?? string.Empty, @"\buid=0\b", RegexOptions.IgnoreCase))
                    {
                        LogOugaArbStepEnd(
                            "未授予Shell ROOT权限",
                            "Red",
                            emphasized: true);
                        throw new UnauthorizedAccessException(
                            "未授予Shell ROOT权限，请在ROOT管理器中给予shell ROOT权限后重试");
                    }

                    useSu = true;
                }

                LogOugaArbStepEnd(
                    "已授予Shell ROOT权限",
                    "Green",
                    emphasized: true);
            }
            catch
            {
                LogOugaArbStepEnd("Error", "Red", emphasized: true);
                throw;
            }

            string partitionListingCommand =
                "for p in /dev/block/by-name /dev/block/bootdevice/by-name; " +
                "do if [ -d $p ]; then ls -1 $p; break; fi; done";
            string partitionListingOutput = await GetCommandOutput(
                adbPath,
                BuildArbAdbShellArguments(targetSerial, partitionListingCommand, useSu));
            string[] partitionNames = partitionListingOutput
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => Regex.IsMatch(line, @"^[A-Za-z0-9._-]+$"))
                .ToArray();

            if (partitionNames.Length == 0)
            {
                throw new InvalidOperationException("无法读取设备分区表，不能判断处理器平台");
            }

            bool isMediatek = partitionNames.Any(name =>
                name.Equals("lk", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("lk_a", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("lk_b", StringComparison.OrdinalIgnoreCase));
            if (isMediatek)
            {
                LogToOugaFlashDual(
                    "设备处理器：",
                    "Black",
                    "联发科；天玑处理器无熔断机制",
                    "Green");
                return;
            }

            LogToOugaFlashDual("设备处理器：", "Black", "高通骁龙", "Blue");

            LogOugaArbStepBegin("读取手机槽位");
            string currentSlot;
            try
            {
                string slotOutput = await GetCommandOutput(
                    adbPath,
                    $"{serialPrefix}shell getprop ro.boot.slot_suffix");
                currentSlot = ParseArbDeviceSlot(slotOutput);

                if (string.IsNullOrWhiteSpace(currentSlot))
                {
                    slotOutput = await GetCommandOutput(
                        adbPath,
                        $"{serialPrefix}shell getprop ro.boot.slot");
                    currentSlot = ParseArbDeviceSlot(slotOutput);
                }

                if (string.IsNullOrWhiteSpace(currentSlot))
                {
                    string bootctlOutput = await GetCommandOutput(
                        adbPath,
                        $"{serialPrefix}shell bootctl get-current-slot");
                    currentSlot = ParseArbDeviceSlot(bootctlOutput);
                }

                if (string.IsNullOrWhiteSpace(currentSlot))
                {
                    throw new InvalidOperationException("无法读取设备当前启动槽位");
                }

                LogOugaArbStepEnd(currentSlot, "Blue", emphasized: true);
            }
            catch
            {
                LogOugaArbStepEnd("Error", "Red", emphasized: true);
                throw;
            }

            string partitionName = $"xbl_config_{currentSlot.ToLowerInvariant()}";
            string[] partitionRoots =
            {
                "/dev/block/by-name",
                "/dev/block/bootdevice/by-name"
            };
            string arbTempRoot = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "tmp",
                "VioletToolboxArb"));
            string tempDirectory = Path.Combine(
                arbTempRoot,
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            string localPath = Path.Combine(tempDirectory, $"{partitionName}.img");
            XblArbResult result;

            try
            {
                string devicePath = string.Empty;
                LogOugaArbStepBegin($"回读{partitionName}");
                try
                {
                    foreach (string root in partitionRoots)
                    {
                        string candidatePath = $"{root}/{partitionName}";
                        string testCommand =
                            $"test -r {candidatePath} && echo VIOLET_ARB_XBL_FOUND";
                        string output = await GetCommandOutput(
                            adbPath,
                            BuildArbAdbShellArguments(targetSerial, testCommand, useSu));
                        if (output.Contains("VIOLET_ARB_XBL_FOUND", StringComparison.Ordinal))
                        {
                            devicePath = candidatePath;
                            break;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(devicePath))
                    {
                        throw new InvalidOperationException(
                            $"未找到当前{currentSlot}槽对应的{partitionName}分区");
                    }

                    await ReadAdbPartitionImageAsync(
                        adbPath,
                        targetSerial,
                        devicePath,
                        localPath,
                        useSu);
                    LogOugaArbStepEnd("OK", "Green", emphasized: true);
                }
                catch
                {
                    LogOugaArbStepEnd("Error", "Red", emphasized: true);
                    throw;
                }

                result = await Task.Run(() => ParseXblConfigArb(localPath));
                LogOugaArbDetail(
                    $"{partitionName}：",
                    $"Major {result.MajorVersion} / Minor {result.MinorVersion} / ARB {result.ArbIndex}",
                    result.ArbIndex > 0 ? "Red" : "Blue",
                    emphasized: result.ArbIndex > 0);
                LogCurrentDeviceArbResult(result.ArbIndex);
            }
            finally
            {
                TryDeleteArbTemporaryFile(localPath);
                try
                {
                    if (Directory.Exists(tempDirectory) &&
                        !Directory.EnumerateFileSystemEntries(tempDirectory).Any())
                    {
                        Directory.Delete(tempDirectory, recursive: false);
                    }

                    if (Directory.Exists(arbTempRoot) &&
                        !Directory.EnumerateFileSystemEntries(arbTempRoot).Any())
                    {
                        Directory.Delete(arbTempRoot, recursive: false);
                    }
                }
                catch
                {
                }
            }
        }

        private static string ParseArbDeviceSlot(string output)
        {
            Match letterMatch = Regex.Match(
                output ?? string.Empty,
                @"(?im)^\s*_?(?<slot>[ab])\s*$",
                RegexOptions.IgnoreCase);
            if (letterMatch.Success)
            {
                return letterMatch.Groups["slot"].Value.ToUpperInvariant();
            }

            Match numberMatch = Regex.Match(
                output ?? string.Empty,
                @"(?m)^\s*(?<slot>[01])\s*$");
            if (!numberMatch.Success)
            {
                return string.Empty;
            }

            return numberMatch.Groups["slot"].Value == "0" ? "A" : "B";
        }

        private static string BuildArbAdbShellArguments(
            string deviceSerial,
            string command,
            bool useSu)
        {
            string serialPrefix = string.IsNullOrWhiteSpace(deviceSerial)
                ? string.Empty
                : $"-s {deviceSerial} ";
            return useSu
                ? $"{serialPrefix}shell \"su -c '{command}'\""
                : $"{serialPrefix}shell \"{command}\"";
        }

        private static async Task ReadAdbPartitionImageAsync(
            string adbPath,
            string deviceSerial,
            string devicePath,
            string localPath,
            bool useSu)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = adbPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            if (!string.IsNullOrWhiteSpace(deviceSerial))
            {
                startInfo.ArgumentList.Add("-s");
                startInfo.ArgumentList.Add(deviceSerial);
            }

            startInfo.ArgumentList.Add("exec-out");
            if (useSu)
            {
                startInfo.ArgumentList.Add("su");
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add($"dd if={devicePath} bs=4096");
            }
            else
            {
                startInfo.ArgumentList.Add("dd");
                startInfo.ArgumentList.Add($"if={devicePath}");
                startInfo.ArgumentList.Add("bs=4096");
            }

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new IOException("无法启动ADB分区读取进程");
            }

            Task<string> errorTask = process.StandardError.ReadToEndAsync();
            await using (var outputFile = new FileStream(
                localPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await process.StandardOutput.BaseStream.CopyToAsync(outputFile);
            }

            await process.WaitForExitAsync();
            string errorOutput = (await errorTask).Trim();
            if (process.ExitCode != 0)
            {
                throw new IOException(string.IsNullOrWhiteSpace(errorOutput)
                    ? $"ADB读取{Path.GetFileName(devicePath)}失败，退出码 {process.ExitCode}"
                    : errorOutput);
            }

            if (!File.Exists(localPath) || new FileInfo(localPath).Length < 64)
            {
                throw new InvalidDataException(string.IsNullOrWhiteSpace(errorOutput)
                    ? "设备返回的xbl_config镜像为空或不完整"
                    : errorOutput);
            }
        }

        private static void TryDeleteArbTemporaryFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch
            {
            }
        }

        private void LogFirmwareArbResult(uint arbIndex)
        {
            Dispatcher.Invoke(() =>
            {
                var paragraph = CreateOugaFlashLogParagraph();
                AppendOugaFlashTimestamp(paragraph);
                AppendOugaFlashStyledText(paragraph, $"ARB索引 {arbIndex}；", "Black");
                AppendOugaFlashStyledText(
                    paragraph,
                    arbIndex > 0 ? "该固件版本已熔断." : "该固件版本未熔断.",
                    arbIndex > 0 ? "Red" : "Green",
                    emphasized: true);
                OugaFlashLogTextBox.Document.Blocks.Add(paragraph);
                OugaFlashLogTextBox.ScrollToEnd();
            });
        }

        private sealed class XblArbResult
        {
            public XblArbResult(uint majorVersion, uint minorVersion, uint arbIndex)
            {
                MajorVersion = majorVersion;
                MinorVersion = minorVersion;
                ArbIndex = arbIndex;
            }

            public uint MajorVersion { get; }
            public uint MinorVersion { get; }
            public uint ArbIndex { get; }
        }

        private static XblArbResult ParseXblConfigArb(string filePath)
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            using var reader = new BinaryReader(stream);

            if (stream.Length < 64)
            {
                throw new InvalidDataException("文件过小，不是有效的xbl_config镜像");
            }

            byte[] elfHeader = reader.ReadBytes(64);
            if (elfHeader.Length != 64 ||
                elfHeader[0] != 0x7F ||
                elfHeader[1] != (byte)'E' ||
                elfHeader[2] != (byte)'L' ||
                elfHeader[3] != (byte)'F')
            {
                throw new InvalidDataException("文件不是有效的ELF镜像");
            }

            if (elfHeader[4] != 2)
            {
                throw new InvalidDataException("xbl_config不是ELF64格式");
            }

            if (elfHeader[5] != 1)
            {
                throw new InvalidDataException("不支持非小端序的ELF镜像");
            }

            ulong programHeaderOffset = ReadUInt64LittleEndian(elfHeader, 0x20);
            ushort programHeaderEntrySize = ReadUInt16LittleEndian(elfHeader, 0x36);
            ushort programHeaderCount = ReadUInt16LittleEndian(elfHeader, 0x38);

            if (programHeaderEntrySize < 56 || programHeaderCount == 0)
            {
                throw new InvalidDataException("ELF程序头信息无效");
            }

            ulong hashSegmentOffset = 0;
            ulong hashSegmentSize = 0;
            for (int index = programHeaderCount - 1; index >= 0; index--)
            {
                ulong entryOffset;
                try
                {
                    entryOffset = checked(
                        programHeaderOffset + ((ulong)index * programHeaderEntrySize));
                }
                catch (OverflowException)
                {
                    throw new InvalidDataException("ELF程序头偏移溢出");
                }

                if (entryOffset > (ulong)stream.Length ||
                    56UL > (ulong)stream.Length - entryOffset)
                {
                    throw new InvalidDataException("ELF程序头超出文件范围");
                }

                stream.Position = (long)entryOffset;
                byte[] programHeader = reader.ReadBytes(56);
                if (programHeader.Length != 56)
                {
                    throw new InvalidDataException("无法完整读取ELF程序头");
                }

                uint segmentType = ReadUInt32LittleEndian(programHeader, 0x00);
                ulong segmentOffset = ReadUInt64LittleEndian(programHeader, 0x08);
                ulong segmentFileSize = ReadUInt64LittleEndian(programHeader, 0x20);

                // 与 arbextract 一致：从后向前取最后一个非空 PT_NULL 段作为 HASH 段。
                if (segmentType == 0 && segmentFileSize > 0)
                {
                    hashSegmentOffset = segmentOffset;
                    hashSegmentSize = segmentFileSize;
                    break;
                }
            }

            if (hashSegmentSize == 0)
            {
                throw new InvalidDataException("未找到xbl_config的HASH段");
            }

            if (hashSegmentOffset > (ulong)stream.Length ||
                hashSegmentSize > (ulong)stream.Length - hashSegmentOffset)
            {
                throw new InvalidDataException("HASH段超出文件范围");
            }

            if (hashSegmentSize > int.MaxValue)
            {
                throw new InvalidDataException("HASH段过大，无法安全解析");
            }

            stream.Position = (long)hashSegmentOffset;
            byte[] hashSegment = reader.ReadBytes((int)hashSegmentSize);
            if ((ulong)hashSegment.Length != hashSegmentSize)
            {
                throw new InvalidDataException("无法完整读取HASH段");
            }

            int metadataHeaderOffset = -1;
            uint commonMetadataSize = 0;
            uint qtiMetadataSize = 0;
            uint oemMetadataSize = 0;

            for (int offset = 0;
                 offset < 0x1000 && offset + 36 <= hashSegment.Length;
                 offset += 4)
            {
                uint version = ReadUInt32LittleEndian(hashSegment, offset);
                uint commonSize = ReadUInt32LittleEndian(hashSegment, offset + 4);
                uint qtiSize = ReadUInt32LittleEndian(hashSegment, offset + 8);
                uint oemSize = ReadUInt32LittleEndian(hashSegment, offset + 12);
                uint hashTableSize = ReadUInt32LittleEndian(hashSegment, offset + 16);

                if (version < 1 || version > 10 ||
                    commonSize > 0x1000 ||
                    oemSize < 12 ||
                    oemSize > 0x4000 ||
                    hashTableSize > 0x4000)
                {
                    continue;
                }

                ulong metadataEnd = (ulong)offset + 36UL + commonSize + qtiSize + oemSize;
                if (metadataEnd > (ulong)hashSegment.Length)
                {
                    continue;
                }

                metadataHeaderOffset = offset;
                commonMetadataSize = commonSize;
                qtiMetadataSize = qtiSize;
                oemMetadataSize = oemSize;
                break;
            }

            if (metadataHeaderOffset < 0)
            {
                throw new InvalidDataException("未找到HASH Table Segment Header");
            }

            ulong oemMetadataOffsetValue =
                (ulong)metadataHeaderOffset + 36UL + commonMetadataSize + qtiMetadataSize;
            if (oemMetadataSize < 12 ||
                oemMetadataOffsetValue > int.MaxValue ||
                oemMetadataOffsetValue + 12UL > (ulong)hashSegment.Length)
            {
                throw new InvalidDataException("OEM Metadata范围无效");
            }

            int oemMetadataOffset = (int)oemMetadataOffsetValue;
            uint majorVersion = ReadUInt32LittleEndian(hashSegment, oemMetadataOffset);
            uint minorVersion = ReadUInt32LittleEndian(hashSegment, oemMetadataOffset + 4);
            uint arbIndex = ReadUInt32LittleEndian(hashSegment, oemMetadataOffset + 8);

            return new XblArbResult(majorVersion, minorVersion, arbIndex);
        }

        private static ushort ReadUInt16LittleEndian(byte[] buffer, int offset)
        {
            return System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(
                buffer.AsSpan(offset, sizeof(ushort)));
        }

        private static uint ReadUInt32LittleEndian(byte[] buffer, int offset)
        {
            return System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
                buffer.AsSpan(offset, sizeof(uint)));
        }

        private static ulong ReadUInt64LittleEndian(byte[] buffer, int offset)
        {
            return System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(
                buffer.AsSpan(offset, sizeof(ulong)));
        }

        private System.Windows.Documents.Paragraph? _pendingOugaArbStepParagraph;

        private void LogOugaArbStepBegin(string title)
        {
            Dispatcher.Invoke(() =>
            {
                var paragraph = CreateOugaFlashLogParagraph();
                AppendOugaFlashTimestamp(paragraph);
                AppendOugaFlashStyledText(paragraph, title, "Black");
                AppendOugaFlashStyledText(paragraph, "...", "Gray");
                OugaFlashLogTextBox.Document.Blocks.Add(paragraph);
                OugaFlashLogTextBox.ScrollToEnd();
                _pendingOugaArbStepParagraph = paragraph;
            });
        }

        private void LogOugaArbStepEnd(
            string result,
            string resultColor,
            bool emphasized = false)
        {
            Dispatcher.Invoke(() =>
            {
                if (_pendingOugaArbStepParagraph == null)
                {
                    return;
                }

                AppendOugaFlashStyledText(
                    _pendingOugaArbStepParagraph,
                    result,
                    resultColor,
                    emphasized);
                OugaFlashLogTextBox.ScrollToEnd();
                _pendingOugaArbStepParagraph = null;
            });
        }

        private void LogCurrentDeviceArbResult(uint arbIndex)
        {
            Dispatcher.Invoke(() =>
            {
                var paragraph = CreateOugaFlashLogParagraph();
                AppendOugaFlashTimestamp(paragraph);
                AppendOugaFlashStyledText(paragraph, $"ARB索引 {arbIndex}；", "Black");
                AppendOugaFlashStyledText(
                    paragraph,
                    arbIndex > 0 ? "该设备已熔断." : "该设备未熔断.",
                    arbIndex > 0 ? "Red" : "Green",
                    emphasized: true);
                OugaFlashLogTextBox.Document.Blocks.Add(paragraph);
                OugaFlashLogTextBox.ScrollToEnd();
            });
        }

        private void LogOugaArbDetail(
            string label,
            string value,
            string valueColor,
            bool emphasized = false)
        {
            Dispatcher.Invoke(() =>
            {
                var paragraph = CreateOugaFlashLogParagraph();
                AppendOugaFlashTimestamp(paragraph);
                AppendOugaFlashStyledText(paragraph, "[ARB检测] ", "Purple", emphasized: true);
                AppendOugaFlashStyledText(paragraph, label, "Black");
                AppendOugaFlashStyledText(paragraph, value, valueColor, emphasized);
                OugaFlashLogTextBox.Document.Blocks.Add(paragraph);
                OugaFlashLogTextBox.ScrollToEnd();
            });
        }

        private void CopyDownloadUrlButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.Button button && button.Tag is string downloadUrl)
                {
                    if (!string.IsNullOrEmpty(downloadUrl))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = downloadUrl,
                            UseShellExecute = true
                        });
                        AddDownloadLogMessage("信息", "已打开下载链接");
                    }
                    else
                    {
                        AddDownloadLogMessage("错误", "下载链接为空");
                    }
                }
            }
            catch (Exception ex)
            {
                AddDownloadLogMessage("错误", $"打开下载链接失败: {ex.Message}");
            }
        }

        private void OpenDownloadUrlButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is System.Windows.Controls.Button button && button.Tag is string downloadUrl)
                {
                    if (!string.IsNullOrEmpty(downloadUrl))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = downloadUrl,
                            UseShellExecute = true
                        });
                        AddDownloadLogMessage("信息", "已打开下载链接");
                    }
                    else
                    {
                        AddDownloadLogMessage("错误", "下载链接为空");
                    }
                }
            }
            catch (Exception ex)
            {
                AddDownloadLogMessage("错误", $"打开下载链接失败: {ex.Message}");
            }
        }

        // 解析fastboot输出并更新OperationProgressBar进度条
        // 传输状态跟踪变量
        private DateTime _transferStartTime = DateTime.Now;
        private long _totalBytesToTransfer = 0;
        private long _currentBytesTransferred = 0;
        private double _currentTransferRate = 0; // MB/s
        private string _operationProgressStatus = "0MB/s";
        
        // 多分区传输的总体积跟踪变量
        private long _totalSelectedPartitionBytes = 0; // 所有已勾选分区的总体积
        private long _remainingPartitionBytes = 0;     // 剩余分区的总体积（动态减少）
        private long _cumulativeBytesTransferred = 0;  // 累计已传输的字节数（跨分区）
        private int _currentPartitionIndex = 0;        // 当前正在传输的分区索引

        
        private void ParseFastbootProgress(string output)
        {
            try
            {
                bool progressUpdated = false;
                
                // 解析传输速率 - fastboot输出格式通常为 "Sending 'partition' (123456 KB)..." 或包含速率信息
                var sendingMatch = System.Text.RegularExpressions.Regex.Match(output, @"Sending\s+'([^']+)'\s+\((\d+)\s+KB\)");
                if (sendingMatch.Success)
                {
                    // 如果是第一次开始传输，计算总体积（不重置开始时间，因为已在WritePartitionButton_Click中设置）
                    if (_totalSelectedPartitionBytes == 0)
                    {
                        _totalSelectedPartitionBytes = CalculateTotalSelectedPartitionSize();
                        _remainingPartitionBytes = _totalSelectedPartitionBytes; // 初始时剩余体积等于总体积
                        _cumulativeBytesTransferred = 0;
                        _currentPartitionIndex = 0;
                    }
                    
                    if (long.TryParse(sendingMatch.Groups[2].Value, out long sizeKB))
                    {
                        _totalBytesToTransfer = sizeKB * 1024; // 转换为字节
                        _currentBytesTransferred = 0;
                    }
                    
                    Dispatcher.Invoke(() =>
                    {
                        OperationProgressBar.Value = 0;
                        UpdateProgressBarValue(0);
                        SetOperationProgressTag("0MB/s");
                         UpdateTransferRateText("0MB/s");
                         if (ProgressBarContainer != null && ProgressBarContainer.Visibility == Visibility.Collapsed)
                         {
                             ProgressBarContainer.Visibility = Visibility.Visible;
                         }
                         if (AfterSalesProgressBarContainer != null && AfterSalesProgressBarContainer.Visibility == Visibility.Collapsed)
                         {
                             AfterSalesProgressBarContainer.Visibility = Visibility.Visible;
                         }
                    });
                    
                    // 初始化传输状态完成
                }
                
                // 解析传输速率信息 - 查找类似 "12.34 MB/s" 的模式
                var rateMatch = System.Text.RegularExpressions.Regex.Match(output, @"([\d\.]+)\s*(MB|KB|GB)/s", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (rateMatch.Success)
                {
                    if (double.TryParse(rateMatch.Groups[1].Value, out double rate))
                    {
                        string unit = rateMatch.Groups[2].Value.ToUpper();
                        // 统一转换为MB/s
                        switch (unit)
                        {
                            case "KB":
                                _currentTransferRate = rate / 1024;
                                break;
                            case "GB":
                                _currentTransferRate = rate * 1024;
                                break;
                            default: // MB
                                _currentTransferRate = rate;
                                break;
                        }
                        
                        // 更新UI显示传输速率和剩余时间
                        Dispatcher.Invoke(() =>
                        {
                            SetOperationProgressTag($"{_currentTransferRate:F2}MB/s");
                             UpdateTransferRateText($"{_currentTransferRate:F2}MB/s");
                        });
                    }
                }
                
                // 首先查找百分比模式，如 "50%" 或 "(50%)" 或 "50.5%"
                var percentageMatch = System.Text.RegularExpressions.Regex.Match(output, @"(\d+(?:\.\d+)?)%");
                
                if (percentageMatch.Success)
                {
                    if (double.TryParse(percentageMatch.Groups[1].Value, out double percentage))
                    {
                        // 确保百分比在0-100范围内
                        percentage = Math.Max(0, Math.Min(100, percentage));
                        
                        // 根据百分比估算当前分区已传输字节数
                        if (_totalBytesToTransfer > 0)
                        {
                            _currentBytesTransferred = (long)(_totalBytesToTransfer * percentage / 100);
                        }
                        
                        // 进度条只显示当前分区的进度
                        Dispatcher.Invoke(() =>
                        {
                            OperationProgressBar.Value = percentage;
                            UpdateProgressBarValue(percentage);
                        });
                        
                        progressUpdated = true;
                    }
                }
                
                // 检查是否包含"OKAY"关键字，表示完成
                if (output.Contains("OKAY"))
                {
                    // 当前分区传输完成，累加到总传输量并从剩余体积中减去
                    if (_totalBytesToTransfer > 0)
                    {
                        _cumulativeBytesTransferred += _totalBytesToTransfer;
                        _remainingPartitionBytes -= _totalBytesToTransfer; // 从剩余体积中减去已完成分区的大小
                        _currentPartitionIndex++;
                    }
                    
                    // 检查是否所有分区都已完成
                    bool allPartitionsComplete = false;
                    if (_totalSelectedPartitionBytes > 0)
                    {
                        // 使用更宽松的判断条件，考虑到浮点数精度问题
                        double completionRatio = (double)_cumulativeBytesTransferred / _totalSelectedPartitionBytes;
                        allPartitionsComplete = completionRatio >= 0.99; // 99%以上认为完成
                    }
                    
                    Dispatcher.Invoke(() =>
                    {
                        if (allPartitionsComplete)
                        {
                            // 所有分区传输完成
                            OperationProgressBar.Value = 100;
                            UpdateProgressBarValue(100);
                            SetOperationProgressTag("已完成");
                             
                             // 重置总体积跟踪变量，为下次传输做准备
                             _totalSelectedPartitionBytes = 0;
                             _remainingPartitionBytes = 0;
                             _cumulativeBytesTransferred = 0;
                             _currentPartitionIndex = 0;
                        }
                        else
                        {
                            // 当前分区完成，进度条重置为0，准备显示下一个分区的进度
                            OperationProgressBar.Value = 0;
                            UpdateProgressBarValue(0);
                            
                            // 重置传输速率和耗时显示
                            SetOperationProgressTag("0MB/s");
                        }
                    });
                }
                // 检查是否包含"Writing"关键字，表示正在写入
                else if (output.Contains("Writing"))
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (!progressUpdated)
                        {
                            OperationProgressBar.Value = 50;
                            UpdateProgressBarValue(50);
                        }
                        SetOperationProgressTag("写入中...");
                         UpdateTransferRateText("写入中...");
                    });
                }
            }
            catch (Exception ex)
            {
                // 静默处理解析错误，不影响主要功能
                System.Diagnostics.Debug.WriteLine($"解析fastboot进度时出错: {ex.Message}");
            }
        }

        // 重置OperationProgressBar进度条
        private void ResetOperationProgressBar()
        {
            Dispatcher.Invoke(() =>
            {
                OperationProgressBar.Value = 0;
            });
        }

        private void ResetOperationTransferDisplay()
        {
            Dispatcher.Invoke(() =>
            {
                OperationProgressBar.Value = 0;

                SetOperationProgressTag("0MB/s");

                if (TransferRateTextBlock != null)
                {
                    TransferRateTextBlock.Text = "0MB/s";
                }
            });
        }

        private void SetOperationTransferProgress(double progress)
        {
            Dispatcher.Invoke(() =>
            {
                if (OperationProgressBar == null)
                {
                    return;
                }

                OperationProgressBar.Minimum = 0;
                OperationProgressBar.Maximum = 100;
                OperationProgressBar.Value = Math.Max(0, Math.Min(100, progress));
            });
        }

        private void SetOperationTransferSpeed(double bytesPerSecond)
        {
            Dispatcher.Invoke(() =>
            {
                string formattedSpeed = FormatTransferSpeed(Math.Max(0, bytesPerSecond));

                SetOperationProgressTag(formattedSpeed);

                if (TransferRateTextBlock != null)
                {
                    TransferRateTextBlock.Text = formattedSpeed;
                }
            });
        }

        private void UpdateOperationTransferElapsed()
        {
            Dispatcher.Invoke(() =>
            {
                RefreshOperationProgressTag();
            });
        }

        private void CompleteOperationTransferDisplay()
        {
            Dispatcher.Invoke(() =>
            {
                OperationProgressBar.Value = 100;

                SetOperationProgressTag("已完成");

                if (TransferRateTextBlock != null)
                {
                    TransferRateTextBlock.Text = "已完成";
                }
            });
        }

        private void SetOperationProgressTag(string status)
        {
            _operationProgressStatus = string.IsNullOrWhiteSpace(status) ? "0MB/s" : status;
            RefreshOperationProgressTag();
        }

        private void RefreshOperationProgressTag()
        {
            if (OperationProgressBar == null)
            {
                return;
            }

            int elapsedSeconds = Math.Max(0, (int)(DateTime.Now - _transferStartTime).TotalSeconds);
            OperationProgressBar.Tag = $"{_operationProgressStatus}  |  Time:{elapsedSeconds}s";
        }

        // 计算所有已勾选分区的总体积（以字节为单位）
        private long CalculateTotalSelectedPartitionSize()
        {
            long totalBytes = 0;
            
            try
            {
                if (PartitionTableDataGrid.ItemsSource is ObservableCollection<PartitionInfo> partitions)
                {
                    foreach (var partition in partitions)
                    {
                        if (partition.IsSelected && !string.IsNullOrEmpty(partition.PartitionSize) && partition.PartitionSize != "--")
                        {
                            totalBytes += ParsePartitionSizeToBytes(partition.PartitionSize);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"计算总体积时出错: {ex.Message}");
            }
            
            return totalBytes;
        }

        // 解析分区大小字符串为字节数
        private long ParsePartitionSizeToBytes(string sizeString)
        {
            try
            {
                if (string.IsNullOrEmpty(sizeString) || sizeString == "--")
                    return 0;

                // 移除空格并转换为大写
                sizeString = sizeString.Trim().ToUpper();
                
                // 使用正则表达式匹配数字和单位
                var match = System.Text.RegularExpressions.Regex.Match(sizeString, @"([\d\.]+)\s*(B|KB|MB|GB|TB)?");
                
                if (match.Success)
                {
                    if (double.TryParse(match.Groups[1].Value, out double size))
                    {
                        string unit = match.Groups[2].Value;
                        
                        // 根据单位转换为字节
                        switch (unit)
                        {
                            case "TB":
                                return (long)(size * 1024 * 1024 * 1024 * 1024);
                            case "GB":
                                return (long)(size * 1024 * 1024 * 1024);
                            case "MB":
                                return (long)(size * 1024 * 1024);
                            case "KB":
                                return (long)(size * 1024);
                            case "B":
                            case "":
                                return (long)size;
                            default:
                                // 如果没有单位，假设是MB
                                return (long)(size * 1024 * 1024);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"解析分区大小时出错: {ex.Message}");
            }
            
            return 0;
        }

        
        



        // 强力线刷处理流程
        private async Task<bool> ExecutePowerFlashProcess(
            string currentSlot,
            IReadOnlyCollection<string> logicalPartitionNames,
            IReadOnlyCollection<string> logicalTargetNames)
        {
            try
            {
                currentSlot = currentSlot.Trim().TrimStart('_').ToLowerInvariant();
                if (currentSlot is not ("a" or "b"))
                {
                    LogToOugaFlash("错误：无法检测当前槽位", "Red");
                    return false;
                }

                if (!string.Equals(currentSlot, "a", StringComparison.OrdinalIgnoreCase))
                {
                    if (ShouldStopOugaFlashBeforeNextPartition("切换设备槽位"))
                    {
                        return false;
                    }

                    if (!await SetActiveSlot("a", currentSlot))
                    {
                        return false;
                    }
                }

                if (ShouldStopOugaFlashBeforeNextPartition("删除逻辑分区"))
                {
                    return false;
                }

                if (!await DeleteLogicalPartitionsForBothSlots(logicalPartitionNames))
                {
                    if (_ougaFlashStopRequested)
                    {
                        return false;
                    }
                    LogErrorStep("修复Super");
                    LogToOugaFlash("蜡笔了，Super真死！", "Red");
                    return false;
                }

                if (ShouldStopOugaFlashBeforeNextPartition("创建逻辑分区"))
                {
                    return false;
                }

                LogToOugaFlash("正在重新创建逻辑分区...", "Yellow");
                if (!await CreateLogicalPartitionTargets(logicalTargetNames))
                {
                    if (_ougaFlashStopRequested)
                    {
                        return false;
                    }
                    LogErrorStep("修复Super");
                    LogToOugaFlash("蜡笔了，Super真死！", "Red");
                    return false;
                }

                if (ShouldFinalizeOugaFlashStop())
                {
                    return false;
                }

                LogOkStep("修复Super");
                return true;
            }
            catch (Exception ex)
            {
                LogToOugaFlash($"强力线刷流程出错：{ex.Message}", "Red");
                return false;
            }
        }

        // 获取当前槽位
        private async Task<string> GetCurrentSlot()
        {
            try
            {
                string programDirectory = AppDomain.CurrentDomain.BaseDirectory;
                // 在FBD模式中使用flash文件夹中的fastboot.exe
                string fastbootPath = Path.Combine(programDirectory, "platform-tools", "fastboot.exe");
                
                // 如果flash文件夹中没有fastboot.exe，使用GetFastbootPath方法获取路径
                if (!File.Exists(fastbootPath))
                {
                    fastbootPath = GetFastbootPath();
                }
                
                string command = BuildOugaFastbootArguments(
                    GetOujiaFlashTargetSerial(),
                    "getvar current-slot");
                
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = fastbootPath,
                    Arguments = command,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using Process? process = Process.Start(startInfo);
                if (process is null)
                {
                    if (_activeOujiaFlashSession?.CurrentSlot is "a" or "b")
                    {
                        return _activeOujiaFlashSession.CurrentSlot;
                    }
                    LogToOugaFlash("启动 fastboot 失败", "Red");
                    return "";
                }

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                var waitTask = process.WaitForExitAsync();
                    
                if (await Task.WhenAny(waitTask, Task.Delay(10000)) == waitTask)
                {
                    string output = await outputTask;
                    string error = await errorTask;
                    string fullOutput = output + error;

                    // 解析输出，查找 current-slot: 
                    var lines = fullOutput.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Contains("current-slot:"))
                        {
                            var parts = line.Split(':');
                            if (parts.Length > 1)
                            {
                                string slot = parts[1].Trim().TrimStart('_').ToLowerInvariant();
                                if (slot is "a" or "b")
                                {
                                    if (_activeOujiaFlashSession != null)
                                    {
                                        _activeOujiaFlashSession.CurrentSlot = slot;
                                    }
                                    return slot;
                                }
                            }
                        }
                    }

                    if (_activeOujiaFlashSession?.CurrentSlot is "a" or "b")
                    {
                        LogToOugaFlash("本次槽位查询无返回，继续使用任务已确认槽位.", "Orange");
                        return _activeOujiaFlashSession.CurrentSlot;
                    }

                    LogToOugaFlash("未找到槽位信息", "Red");
                    return "";
                }

                process.Kill();
                if (_activeOujiaFlashSession?.CurrentSlot is "a" or "b")
                {
                    LogToOugaFlash("获取槽位信息超时，继续使用任务已确认槽位.", "Orange");
                    return _activeOujiaFlashSession.CurrentSlot;
                }

                LogToOugaFlash("获取槽位信息超时", "Red");
                return "";
            }
            catch (Exception ex)
            {
                if (_activeOujiaFlashSession?.CurrentSlot is "a" or "b")
                {
                    LogToOugaFlash("槽位查询异常，继续使用任务已确认槽位.", "Orange");
                    return _activeOujiaFlashSession.CurrentSlot;
                }
                LogToOugaFlash($"获取槽位信息出错：{ex.Message}", "Red");
                return "";
            }
        }

        // 删除逻辑分区
        private async Task<bool> DeleteLogicalPartitions(
            string slot,
            IReadOnlyCollection<string> partitionNames)
        {
            try
            {
                string programDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string fastbootPath = Path.Combine(programDirectory, "platform-tools", "fastboot.exe");
                string selectedSerial = GetOujiaFlashTargetSerial();
                bool allOk = true;

                foreach (string partitionName in partitionNames.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    string fullPartitionName = $"{partitionName}_{slot}";
                    LogToOugaFlash($"删除分区：{fullPartitionName}");
                    
                    string command = BuildOugaFastbootArguments(
                        selectedSerial,
                        $"delete-logical-partition {fullPartitionName}");
                    
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = fastbootPath,
                        Arguments = command,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8
                    };

                    using Process? process = Process.Start(startInfo);
                    if (process is null)
                    {
                        LogToOugaFlash($"删除分区失败：{fullPartitionName}", "Yellow");
                        allOk = false;
                        continue;
                    }

                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();
                    var waitTask = process.WaitForExitAsync();
                        
                    if (await Task.WhenAny(waitTask, Task.Delay(30000)) == waitTask)
                    {
                        string output = await outputTask;
                        string error = await errorTask;

                        if (process.ExitCode == 0)
                        {
                            LogToOugaFlash($"成功删除分区：{fullPartitionName}", "Green");
                        }
                        else
                        {
                            LogToOugaFlash($"删除分区失败：{fullPartitionName} - {error}", "Yellow");
                            allOk = false;
                            // 继续删除其他分区，不中断流程
                        }
                    }
                    else
                    {
                        process.Kill();
                        LogToOugaFlash($"删除分区超时：{fullPartitionName}", "Yellow");
                        allOk = false;
                    }
                    
                    // 每个分区之间稍作延迟
                    await Task.Delay(500);
                }

                LogToOugaFlash(
                    allOk ? "逻辑分区删除完成" : "逻辑分区删除完成，但存在失败项",
                    allOk ? "Green" : "Yellow");
                return allOk;
            }
            catch (Exception ex)
            {
                LogToOugaFlash($"删除逻辑分区出错：{ex.Message}", "Red");
                return false;
            }
        }

        private async Task<bool> CreateLogicalPartitionTargets(
            IReadOnlyCollection<string> targetNames)
        {
            try
            {
                string fastbootPath = GetOugaFastbootExecutablePath();
                string selectedSerial = GetOujiaFlashTargetSerial();
                foreach (string targetName in targetNames.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (ShouldStopOugaFlashBeforeNextPartition(targetName))
                    {
                        return false;
                    }

                    LogStepBeginNoTime($"创建逻辑分区 {targetName}");
                    var createResult = await ExecuteOugaFastbootCommandAsync(
                        fastbootPath,
                        selectedSerial,
                        $"create-logical-partition {targetName} 40",
                        timeoutSeconds: 30);
                    if (createResult.Success)
                    {
                        LogStepEndOk();
                    }
                    else
                    {
                        LogStepEndError();
                        AppendOugaFastbootFailureOutput(createResult.Output);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                LogToOugaFlash($"创建逻辑分区出错：{ex.Message}", "Red");
                return false;
            }
        }

        // 执行仅FBD线刷流程
        private async Task<bool> ExecutePureFBDProcess(
            string currentSlot,
            string targetSlot,
            IReadOnlyCollection<string> logicalPartitionNames,
            IReadOnlyCollection<string> logicalTargetNames)
        {
            try
            {
                Run? countdownRun = null;
                Dispatcher.Invoke(() =>
                {
                    var paragraph = CreateOugaFlashLogParagraph();
                    AppendOugaFlashTimestamp(paragraph);
                    AppendOugaFlashStyledText(
                        paragraph,
                        "仅FastbootD线刷适用于没有Fastboot的高通骁龙设备，天玑处理器设备请勿勾选...",
                        "Red",
                        emphasized: true);
                    countdownRun = new Run("5s")
                    {
                        Foreground = GetOugaFlashLogBrush("Blue"),
                        FontWeight = FontWeights.SemiBold
                    };
                    paragraph.Inlines.Add(countdownRun);
                    OugaFlashLogTextBox.Document.Blocks.Add(paragraph);
                    OugaFlashLogTextBox.ScrollToEnd();
                });

                for (int remainingSeconds = 5; remainingSeconds >= 1; remainingSeconds--)
                {
                    if (_ougaFlashStopRequested)
                    {
                        CompleteOugaDeviceWaitLog(countdownRun!, "已停止.", false);
                        MarkOujiaFlashStopped();
                        return false;
                    }

                    UpdateOugaDeviceWaitLog(countdownRun!, $"{remainingSeconds}s");
                    await Task.Delay(1000);
                }
                CompleteOugaDeviceWaitLog(countdownRun!, "OK", true);

                if (ShouldStopOugaFlashBeforeNextPartition("切换设备槽位"))
                {
                    return false;
                }

                if (!await SetActiveSlot(
                        targetSlot,
                        currentSlot,
                        includeSlotSuffix: true))
                {
                    return false;
                }

                if (ShouldStopOugaFlashBeforeNextPartition("删除逻辑分区"))
                {
                    return false;
                }

                if (!await DeleteLogicalPartitionsForBothSlots(logicalPartitionNames))
                {
                    if (_ougaFlashStopRequested)
                    {
                        return false;
                    }
                    LogToOugaFlash("删除逻辑分区失败", "Red");
                    return false;
                }

                if (ShouldStopOugaFlashBeforeNextPartition("创建逻辑分区"))
                {
                    return false;
                }

                if (!await CreateLogicalPartitionTargets(logicalTargetNames))
                {
                    if (_ougaFlashStopRequested)
                    {
                        return false;
                    }
                    LogToOugaFlash("创建逻辑分区失败", "Red");
                    return false;
                }

                return !ShouldFinalizeOugaFlashStop();
            }
            catch (Exception ex)
            {
                LogToOugaFlash($"仅FBD流程执行出错：{ex.Message}", "Red");
                return false;
            }
        }

        // 设置活动槽位
        private async Task<bool> SetActiveSlot(
            string slot,
            string? sourceSlot = null,
            bool includeSlotSuffix = false)
        {
            string targetSlot = slot.Trim().TrimStart('_').ToLowerInvariant();
            string currentSlot = string.IsNullOrWhiteSpace(sourceSlot)
                ? _activeOujiaFlashSession?.CurrentSlot ?? string.Empty
                : sourceSlot.Trim().TrimStart('_').ToLowerInvariant();
            string operationName = currentSlot is "a" or "b"
                ? includeSlotSuffix
                    ? $"安全切换设备槽位 {currentSlot.ToUpper()}槽 → {targetSlot.ToUpper()}槽"
                    : $"安全切换设备槽位 {currentSlot.ToUpper()} → {targetSlot.ToUpper()}"
                : $"安全切换设备槽位到{targetSlot.ToUpper()}槽";
            LogStepBegin(operationName);
            try
            {
                string selectedSerial = GetOujiaFlashTargetSerial();
                string fastbootPath = GetOugaFastbootExecutablePath();
                var setActiveResult = await ExecuteOugaFastbootCommandAsync(
                    fastbootPath,
                    selectedSerial,
                    $"set_active {targetSlot}",
                    timeoutSeconds: 15);
                if (!setActiveResult.Success)
                {
                    LogStepEndError();
                    AppendOugaFastbootFailureOutput(setActiveResult.Output);
                    return false;
                }

                if (_activeOujiaFlashSession != null)
                {
                    _activeOujiaFlashSession.CurrentSlot = targetSlot;
                }
                LogStepEndOk();
                return true;
            }
            catch (Exception ex)
            {
                LogStepEndError();
                AppendOugaFastbootFailureOutput($"切换设备槽位异常：{ex.Message}");
                return false;
            }
        }

        // 删除A和B槽位的逻辑分区
        private async Task<bool> DeleteLogicalPartitionsForBothSlots(
            IReadOnlyCollection<string> partitions)
        {
            try
            {
                string selectedSerial = GetOujiaFlashTargetSerial();
                string fastbootPath = GetOugaFastbootExecutablePath();
                if (string.IsNullOrWhiteSpace(fastbootPath) || !File.Exists(fastbootPath))
                {
                    return false;
                }

                foreach (string slot in new[] { "a", "b" })
                {
                    foreach (string partition in partitions.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        string fullPartitionName = $"{partition}_{slot}";
                        if (ShouldStopOugaFlashBeforeNextPartition(fullPartitionName))
                        {
                            return false;
                        }

                        LogStepBeginNoTime($"删除逻辑分区 {fullPartitionName}");
                        var deleteResult = await ExecuteOugaFastbootCommandAsync(
                            fastbootPath,
                            selectedSerial,
                            $"delete-logical-partition {fullPartitionName}",
                            timeoutSeconds: 30);
                        if (deleteResult.Success ||
                            IsOujiaLogicalPartitionMissingOutput(deleteResult.Output))
                        {
                            LogStepEndOk();
                        }
                        else
                        {
                            LogStepEndError();
                            AppendOugaFastbootFailureOutput(deleteResult.Output);
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                LogToOugaFlash($"删除逻辑分区出错：{ex.Message}", "Red");
                return false;
            }
        }

        // 额外刷写my_company和my_preload分区的方法
        private async Task<bool> FlashAdditionalPartitions(
            IReadOnlyDictionary<string, string> additionalImages,
            long totalFlashBytes,
            string totalSizeText)
        {
            try
            {
                if (additionalImages.Count == 0)
                {
                    return true;
                }

                string slot = await GetCurrentSlot();
                if (string.IsNullOrWhiteSpace(slot))
                {
                    LogToOugaFlash("无法确认额外分区目标槽位，已跳过额外分区刷写.", "Red");
                    return false;
                }

                bool success = true;
                foreach (var additionalImage in additionalImages)
                {
                    string partitionName = additionalImage.Key;
                    string imagePath = additionalImage.Value;
                    if (ShouldStopOugaFlashBeforeNextPartition(partitionName))
                    {
                        return false;
                    }

                    string fileName = Path.GetFileName(imagePath);
                    string target = $"{partitionName}_{slot}";
                    LogStepBeginNoTime($"[写入镜像] {fileName}  >  {target}.img");
                    long bytesBeforeImage = ReadFlashedBytesFromFile();
                    Action<long> progressAction = transferred =>
                    {
                        long total = totalFlashBytes > 0
                            ? Math.Min(bytesBeforeImage + transferred, totalFlashBytes)
                            : bytesBeforeImage + transferred;
                        Dispatcher.Invoke(() =>
                        {
                            if (total > _lastGlobalProgressBytes)
                            {
                                _lastGlobalProgressBytes = total;
                            }
                            FlashProgressPercentage = totalFlashBytes > 0
                                ? Math.Min(100.0, (double)_lastGlobalProgressBytes / totalFlashBytes * 100.0)
                                : 0;
                            FlashProgressDetailText = $"{FormatFileSize(_lastGlobalProgressBytes)}/{totalSizeText}";
                            WriteFlashProgressFile(_lastGlobalProgressBytes, totalFlashBytes);
                        });
                    };

                    bool ok = await FlashPartitionToSlot(
                        partitionName,
                        imagePath,
                        slot,
                        progressAction);
                    long completedBytes = totalFlashBytes > 0
                        ? Math.Min(bytesBeforeImage + new FileInfo(imagePath).Length, totalFlashBytes)
                        : bytesBeforeImage + new FileInfo(imagePath).Length;
                    WriteFlashProgressFile(completedBytes, totalFlashBytes);
                    if (ok)
                    {
                        LogStepEndOk();
                    }
                    else
                    {
                        LogStepEndError();
                        success = false;
                    }
                }

                if (!success)
                {
                    LogToOugaFlash("刷入失败", "Red");
                }

                return success;
            }
            catch (Exception)
            {
                return false;
            }
        }
        
        // 获取指定路径所在磁盘的可用空间（以GB为单位）
        private long GetAvailableDiskSpaceGB(string path)
        {
            try
            {
                // 获取路径的根目录
                string rootPath = Path.GetPathRoot(path) ?? "";
                if (string.IsNullOrEmpty(rootPath))
                {
                    return 0;
                }
                
                DriveInfo drive = new DriveInfo(rootPath);
                if (drive.IsReady)
                {
                    // 将字节转换为GB
                    return drive.AvailableFreeSpace / (1024 * 1024 * 1024);
                }
                return 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private async Task CheckDeviceModel(string payloadFilePath)
        {
            try
            {
                // 获取payload.bin文件所在的目录
                string? payloadDirectory = Path.GetDirectoryName(payloadFilePath);
                if (string.IsNullOrWhiteSpace(payloadDirectory))
                {
                    Dispatcher.Invoke(() =>
                    {
                        LogToOugaFlash("解析刷机包对应机型失败，跳过解析步骤...", "Yellow");
                    });
                    return;
                }

                string propertiesFilePath = Path.Combine(payloadDirectory, "payload_properties.txt");

                // 显示开始检测日志
                Dispatcher.Invoke(() =>
                {
                    LogToOugaFlash("正在判断刷机包对应的机型...", "Yellow");
                });

                // 检查payload_properties.txt文件是否存在
                if (!File.Exists(propertiesFilePath))
                {
                    Dispatcher.Invoke(() =>
                    {
                        LogToOugaFlash("解析刷机包对应机型失败，跳过解析步骤...", "Yellow");
                    });
                    return;
                }

                // 读取payload_properties.txt文件内容
                string[] lines = await File.ReadAllLinesAsync(propertiesFilePath);
                string? deviceCode = null;

                // 解析ota_target_version
                foreach (string line in lines)
                {
                    if (line.StartsWith("ota_target_version="))
                    {
                        string targetVersion = line.Substring("ota_target_version=".Length).Trim();
                        
                        // 提取设备代号（前6位字符）
                        if (targetVersion.Length >= 6)
                        {
                            deviceCode = targetVersion.Substring(0, 6);
                        }
                        break;
                    }
                }

                if (string.IsNullOrEmpty(deviceCode))
                {
                    Dispatcher.Invoke(() =>
                    {
                        LogToOugaFlash("解析刷机包对应机型失败，跳过解析步骤...", "Yellow");
                    });
                    return;
                }

                // 设备代号对应机型的字典
                var deviceModels = new Dictionary<string, string>
                {
                    ["PLZ110"] = "一加15T",
                    ["PLQ110"] = "一加 ACE 6",
                    ["PLR110"] = "一加 ACE 6T",
                    ["PLK110"] = "一加15",
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

                // 查找对应的机型
                string deviceModel = deviceModels.ContainsKey(deviceCode) ? deviceModels[deviceCode] : "未知机型";

                // 显示检测结果
                Dispatcher.Invoke(() =>
                {
                    LogToOugaFlash($"刷机包对应的机型为{deviceModel}...", "Red");
                    LogToOugaFlash("刷错包会导致设备黑砖，5秒后开始解包...");
                });

                // 等待5秒
                await Task.Delay(5000);
            }
            catch (Exception)
            {
                Dispatcher.Invoke(() =>
                {
                    LogToOugaFlash("解析刷机包对应机型失败，跳过解析步骤...");
                });
            }
        }

        private async void AfterSalesStartFlashButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 停止异步设备检测
                LogStepBegin("委托主页停止异步设备检测");
                HandleStopDeviceDetection();
                LogStepEndOk();
                
                // 检查是否开启全自动救砖模式
                if (AfterSalesAutoBrickRecoveryModeToggle.IsChecked == true)
                {
                    await HandleAutoBrickRecoveryMode();
                    return;
                }
                
                // 获取选中的分区
                var selectedPartitions = OugaPartitionTableDataGrid.ItemsSource as System.Collections.ObjectModel.ObservableCollection<PartitionInfo>;
                if (selectedPartitions == null || selectedPartitions.Count == 0)
                {
                    LogToOugaFlash("请先选择要刷写的分区", "Red");
                    LogStepBegin("委托主页开始异步设备检测");
                    HandleStartDeviceDetection();
                    LogStepEndOk();
                    return;
                }

                var partitionsToFlash = selectedPartitions.Where(p => p.IsSelected).ToList();
                if (partitionsToFlash.Count == 0)
                {
                    LogToOugaFlash("请至少选择一个分区进行刷写", "Red");
                    LogStepBegin("委托主页开始异步设备检测");
                    HandleStartDeviceDetection();
                    LogStepEndOk();
                    return;
                }

                // 初始化进度环
                long totalFlashBytes = partitionsToFlash.Sum(p => {
                    if (string.IsNullOrEmpty(p.FilePath) || !System.IO.File.Exists(p.FilePath)) return 0L;
                    return new System.IO.FileInfo(p.FilePath).Length;
                });
                long currentFlashedBytes = 0;
                _lastGlobalProgressBytes = 0;
                FlashProgressPercentage = 0;

                string totalSizeStr = FormatFileSize(totalFlashBytes);
                FlashProgressDetailText = $"0/{totalSizeStr}";
                WriteFlashProgressFile(0, totalFlashBytes);

                // 显示进度条
                ShowProgressBar();

                // 检查是否开启了FB模式（Fastboot模式）
                if (AfterSalesBootloaderModeToggle.IsChecked == true)
                {
                    LogToOugaFlashDual(
                        "用户选择：",
                        "Black",
                        "售后包Fastboot模式",
                        "Purple");

                    // 获取散包文件夹路径
                    string scatterPackPath = AfterSalesFlashPackTextBox.Text;
                    if (string.IsNullOrWhiteSpace(scatterPackPath) || scatterPackPath == "选择一个散包文件夹或将散包拖动到此自动选择...")
                    {
                        LogToOugaFlash("请先选择散包文件夹", "Red");
                        HideProgressBar();
                        LogStepBegin("委托主页开始异步设备检测");
                        HandleStartDeviceDetection();
                        LogStepEndOk();
                        return;
                    }

                    if (!Directory.Exists(scatterPackPath))
                    {
                        LogToOugaFlash("散包文件夹不存在", "Red");
                        HideProgressBar();
                        LogStepBegin("委托主页开始异步设备检测");
                        HandleStartDeviceDetection();
                        LogStepEndOk();
                        return;
                    }

                    // 自动勾选打包配置
                    CleanupMyPartitionsCheckBox.IsChecked = true;
                    FilterXmlCheckBox.IsChecked = true;

                    // 设置散包路径到SuperScatterPathTextBox
                    SuperScatterPathTextBox.Text = scatterPackPath;

                    // 检测violet特征文件
                    string violetFilePath = Path.Combine(scatterPackPath, "violet");
                    bool needMergeSuper = !File.Exists(violetFilePath);

                    if (needMergeSuper)
                    {
                        // 直接执行打包super逻辑（内联）
                        // 隐藏：LogToOugaFlash("开始合并散包为super...");
                        
                        try
                        {
                            string binDir = AppDomain.CurrentDomain.BaseDirectory;
                        var maker = new SuperMaker(binDir, (msg) => Dispatcher.Invoke(() => LogToOugaFlash(msg)));
                        
                        string outputDir = Path.Combine(scatterPackPath, "IMAGES");
                        if (!Directory.Exists(outputDir))
                        {
                            Directory.CreateDirectory(outputDir);
                        }

                        bool success = await maker.MakeSuperFromDirectoryAsync(scatterPackPath, outputDir);
                        
                        if (!success)
                        {
                            LogToOugaFlash("合并super失败", "Red");
                            HideProgressBar();
                            LogStepBegin("委托主页开始异步设备检测");
                            HandleStartDeviceDetection();
                            LogStepEndOk();
                            return;
                        }

                        // 隐藏：LogToOugaFlash("Super镜像构建成功", "Green");
                        LogStepBegin("构建刷机包");
                        LogStepEndDone();

                        // 清理残留文件
                        if (CleanupMyPartitionsCheckBox.IsChecked == true)
                        {
                            try
                            {
                                // 隐藏：LogToOugaFlash("开始清理残留分区文件/文件夹...");
                                var usedPartitions = maker.ProcessedPartitions;
                                
                                var subDirs = Directory.GetDirectories(outputDir);
                                foreach (var subDir in subDirs)
                                {
                                    var dirName = new DirectoryInfo(subDir).Name;
                                    bool shouldDelete = false;

                                    if (dirName.StartsWith("my", StringComparison.OrdinalIgnoreCase))
                                    {
                                        shouldDelete = true;
                                    }
                                    else if (usedPartitions.Contains(dirName, StringComparer.OrdinalIgnoreCase))
                                    {
                                        shouldDelete = true;
                                    }

                                    if (shouldDelete)
                                    {
                                        Directory.Delete(subDir, true);
                                    }
                                }

                                var files = Directory.GetFiles(outputDir);
                                foreach (var file in files)
                                {
                                    var fileName = Path.GetFileName(file);
                                    var fileNameNoExt = Path.GetFileNameWithoutExtension(file);
                                    
                                    if (fileName.Equals("super.img", StringComparison.OrdinalIgnoreCase)) continue;

                                    bool shouldDelete = false;

                                    if (fileName.StartsWith("my", StringComparison.OrdinalIgnoreCase))
                                    {
                                        shouldDelete = true;
                                    }
                                    else if (usedPartitions.Contains(fileNameNoExt, StringComparer.OrdinalIgnoreCase))
                                    {
                                        shouldDelete = true;
                                    }

                                    if (shouldDelete)
                                    {
                                        File.Delete(file);
                                    }
                                }

                                // 隐藏：LogToOugaFlash("清理完成");
                            }
                            catch (Exception ex)
                            {
                                LogToOugaFlash($"清理失败: {ex.Message}", "Yellow");
                            }
                        }

                        // 过滤XML文件
                        if (FilterXmlCheckBox.IsChecked == true)
                        {
                            try
                            {
                                // 隐藏：LogToOugaFlash("开始过滤多余XML文件...");
                                
                                var blankGptFiles = Directory.GetFiles(outputDir, "rawprogram*_BLANK_GPT.xml");
                                var wipePartFiles = Directory.GetFiles(outputDir, "rawprogram*_WIPE_PARTITIONS.xml");
                                var allFiles = blankGptFiles.Concat(wipePartFiles);

                                foreach (var file in allFiles)
                                {
                                    // 直接删除文件，不再移动到桌面
                                    File.Delete(file);
                                }
                                // 隐藏：LogToOugaFlash($"XML过滤完成，文件已移动至桌面");
                            }
                            catch (Exception ex)
                            {
                                LogToOugaFlash($"XML过滤失败: {ex.Message}", "Yellow");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogToOugaFlash($"合并super失败: {ex.Message}", "Red");
                        HideProgressBar();
                        LogStepBegin("委托主页开始异步设备检测");
                        HandleStartDeviceDetection();
                        LogStepEndOk();
                        return;
                    }

                        // 创建violet标记文件
                        try
                        {
                            File.WriteAllText(violetFilePath, "");
                            LogStepBegin("创建标记文件");
                            LogStepEndOk();
                        }
                        catch (Exception ex)
                        {
                            LogToOugaFlash($"创建violet标记文件失败: {ex.Message}", "Yellow");
                        }
                    }
                    else
                    {
                        LogToOugaFlash("检测到刷机包已合并，跳过制作步骤...", "Green");
                    }

                    // 与全量包模式共用设备等待能力：120秒原地倒计时并绑定同一台设备。
                    string? detectedDeviceSerial = await WaitForOugaFastbootDeviceAsync(
                        "Fastboot",
                        requireFastbootd: false,
                        timeoutSeconds: OujiaFastbootDeviceWaitTimeoutSeconds);
                    if (string.IsNullOrWhiteSpace(detectedDeviceSerial))
                    {
                        HideProgressBar();
                        LogStepBegin("委托主页开始异步设备检测");
                        HandleStartDeviceDetection();
                        LogStepEndOk();
                        return;
                    }

                    string afterSalesDeviceSerial = detectedDeviceSerial;
                    if (!await EnsureOugaFastbootConnectionStableAsync(
                            afterSalesDeviceSerial,
                            "等待设备连接稳定...",
                            requireFastbootd: false))
                    {
                        HideProgressBar();
                        LogStepBegin("委托主页开始异步设备检测");
                        HandleStartDeviceDetection();
                        LogStepEndOk();
                        return;
                    }

                    string fastbootPath = GetFastbootPath();
                    string imagesPath = Path.Combine(scatterPackPath, "IMAGES");
                    string radioPath = Path.Combine(scatterPackPath, "RADIO");

                    // 刷写super分区（不使用槽位后缀）
                    string superPath = Path.Combine(imagesPath, "super.img");
                    if (File.Exists(superPath))
                    {
                        // 先擦除super分区
                        LogStepBegin("擦除super分区");
                        await ExecuteFastbootCommand(fastbootPath, "erase super");
                        LogStepEndOk();
                        
                        LogStepBeginNoTime("[Flashing] super.img → super.img");
                        
                        // 直接使用ExecuteFastbootCommand，它已经支持进度解析
                        string result = await ExecuteFastbootCommand(fastbootPath, $"flash super \"{superPath}\"");
                        
                        if (string.IsNullOrEmpty(result) || result.ToLower().Contains("error") || result.ToLower().Contains("failed"))
                        {
                            LogStepEndError();
                            LogToOugaFlash("刷写super失败", "Red");
                            HideProgressBar();
                            LogStepBegin("委托主页开始异步设备检测");
                            HandleStartDeviceDetection();
                            LogStepEndOk();
                            return;
                        }
                        
                        LogStepEndOk();
                    }
                    else
                    {
                        LogToOugaFlash("未找到super.img文件", "Red");
                        HideProgressBar();
                        LogStepBegin("委托主页开始异步设备检测");
                        HandleStartDeviceDetection();
                        LogStepEndOk();
                        return;
                    }

                    // 刷写完super后等待设备冷却（120秒倒计时）
                    LogToOugaFlash("单次传输数据较大，等待设备冷却中...", "Yellow");
                    
                    // 创建一个段落用于显示倒计时
                    System.Windows.Documents.Paragraph countdownParagraph = null;
                    System.Windows.Documents.Run countdownRun = null;
                    
                    await Dispatcher.InvokeAsync(() =>
                    {
                        countdownParagraph = CreateOugaFlashLogParagraph();
                        AppendOugaFlashTimestamp(countdownParagraph);
                        
                        countdownRun = new System.Windows.Documents.Run("剩余时间：120秒");
                        countdownRun.Foreground = GetOugaFlashLogBrush("Yellow");
                        countdownRun.FontWeight = System.Windows.FontWeights.Bold;
                        countdownParagraph.Inlines.Add(countdownRun);
                        
                        OugaFlashLogTextBox.Document.Blocks.Add(countdownParagraph);
                        OugaFlashLogTextBox.ScrollToEnd();
                    });
                    
                    // 120秒倒计时
                    for (int remaining = 120; remaining > 0; remaining--)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (countdownRun != null)
                            {
                                countdownRun.Text = $"剩余时间：{remaining}秒";
                            }
                        });
                        
                        await Task.Delay(1000);
                    }
                    
                    // 倒计时完成，更新显示
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (countdownRun != null)
                        {
                            countdownRun.Text = "设备冷却完成";
                            countdownRun.Foreground = GetOugaFlashLogBrush("Green");
                        }
                    });

                    // 判断处理器类型
                    string lkPath = Path.Combine(imagesPath, "lk.img");
                    bool isMediaTek = File.Exists(lkPath);
                    
                    if (isMediaTek)
                    {
                        LogToOugaFlashDual(
                            "设备处理器：",
                            "Black",
                            "联发科",
                            "Green");
                    }
                    else
                    {
                        LogToOugaFlashDual(
                            "设备处理器：",
                            "Black",
                            "高通骁龙",
                            "Green");
                    }

                    // 定义需要刷写的分区
                    List<string> partitionsToFlashInFB = new List<string>();
                    
                    if (isMediaTek)
                    {
                        // 联发科设备
                        partitionsToFlashInFB.AddRange(new[] { "boot", "dtbo", "init_boot", "lk", "vbmeta", "vbmeta_system", "vbmeta_vendor", "vendor_boot" });
                    }
                    else
                    {
                        // 高通骁龙设备
                        partitionsToFlashInFB.AddRange(new[] { "boot", "dtbo", "init_boot", "modem", "recovery", "vbmeta", "vbmeta_system", "vbmeta_vendor", "vendor_boot" });
                    }

                    // 在Fastboot模式下刷写关键分区（AB槽位都刷）
                    var flashedPartitions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    
                    foreach (var partitionName in partitionsToFlashInFB)
                    {
                        // 在IMAGES和RADIO中查找镜像文件
                        string imgPath = Path.Combine(imagesPath, $"{partitionName}.img");
                        if (!File.Exists(imgPath))
                        {
                            imgPath = Path.Combine(radioPath, $"{partitionName}.img");
                        }

                        if (File.Exists(imgPath))
                        {
                            // 重置进度条状态（同步确保立即生效）
                            ResetProgressBar();
                            
                            // 刷写到A槽位
                            LogStepBeginNoTime($"[Flashing] {partitionName}.img → {partitionName}_a.img");
                            bool okA = await FlashPartitionToSlot(partitionName, imgPath, "a");
                            if (!okA)
                            {
                                LogStepEndError();
                            }
                            else
                            {
                                LogStepEndOk();
                            }
                            
                            // 等待A槽位刷写完全完成
                            await Task.Delay(500);
                            
                            // 在刷写B槽位前重置进度条（同步确保立即生效）
                            ResetProgressBar();
                            
                            // 刷写到B槽位
                            LogStepBeginNoTime($"[Flashing] {partitionName}.img → {partitionName}_b.img");
                            bool okB = await FlashPartitionToSlot(partitionName, imgPath, "b");
                            if (!okB)
                            {
                                LogStepEndError();
                            }
                            else
                            {
                                LogStepEndOk();
                            }
                            
                            flashedPartitions.Add(partitionName);
                        }
                    }

                    // 使用全量包模式相同的重启命令执行与错误输出策略。
                    if (!await RebootOugaDeviceAsync(
                            afterSalesDeviceSerial,
                            toFastbootd: true))
                    {
                        LogToOugaFlash("无法重启到FastbootD模式", "Red");
                        HideProgressBar();
                        LogStepBegin("委托主页开始异步设备检测");
                        HandleStartDeviceDetection();
                        LogStepEndOk();
                        return;
                    }

                    string? fastbootdSerial = await WaitForOugaFastbootDeviceAsync(
                        "FastbootD",
                        afterSalesDeviceSerial,
                        requireFastbootd: true,
                        timeoutSeconds: OujiaFastbootDeviceWaitTimeoutSeconds);
                    if (string.IsNullOrWhiteSpace(fastbootdSerial))
                    {
                        HideProgressBar();
                        LogStepBegin("委托主页开始异步设备检测");
                        HandleStartDeviceDetection();
                        LogStepEndOk();
                        return;
                    }

                    afterSalesDeviceSerial = fastbootdSerial;
                    if (!await EnsureOugaFastbootConnectionStableAsync(
                            afterSalesDeviceSerial,
                            "等待设备连接稳定...",
                            requireFastbootd: true))
                    {
                        HideProgressBar();
                        LogStepBegin("委托主页开始异步设备检测");
                        HandleStartDeviceDetection();
                        LogStepEndOk();
                        return;
                    }

                    // 获取当前槽位
                    LogToOugaFlash("正在获取设备槽位信息...");
                    string currentSlot = await GetCurrentSlot();
                    if (string.IsNullOrEmpty(currentSlot))
                    {
                        LogToOugaFlash("无法获取设备槽位，默认使用槽位a", "Yellow");
                        currentSlot = "a";
                    }
                    else
                    {
                        LogToOugaFlashDual(
                            "设备当前启动槽位：",
                            "Black",
                            $"{currentSlot.ToUpper()}槽",
                            "Green");
                    }

                    // 解析分区表并删除COW分区
                    LogStepBegin("解析COW快照分区");
                    bool cowOk = await DeleteCowPartitions();
                    if (cowOk) LogStepEndOk(); else LogStepEndError();
                    LogStepBegin("解析分区表");
                    LogStepEndOk();

                    await Task.Delay(1000);

                    // 获取设备分区表
                    string getvarOutput = await ExecuteFastbootCommand(fastbootPath, "getvar all");
                    HashSet<string> devicePartitions =
                        ParseOujiaAvailablePartitionNames(getvarOutput);

                    // 过滤分区列表
                    var excludedPartitions = new HashSet<string>(new[] {
                        "userdata", "metadata", "persist", "system", "odm", "vendor", "product", 
                        "system_ext", "system_dlkm", "vendor_dlkm", "odm_dlkm",
                        "my_bigball", "my_carrier", "my_company", "my_engineering", "my_heytap", 
                        "my_manifest", "my_preload", "my_product", "my_region", "my_stock"
                    }, StringComparer.OrdinalIgnoreCase);

                    // 遍历IMAGES和RADIO文件夹，查找所有.img文件
                    var imagesToFlash = new List<(string partitionName, string filePath)>();
                    
                    foreach (var folderPath in new[] { imagesPath, radioPath })
                    {
                        if (!Directory.Exists(folderPath)) continue;
                        
                        var imgFiles = Directory.GetFiles(folderPath, "*.img");
                        foreach (var imgFile in imgFiles)
                        {
                            string fileName = Path.GetFileNameWithoutExtension(imgFile).ToLower();
                            
                            // 过滤已刷写的分区和排除列表
                            if (flashedPartitions.Contains(fileName) || excludedPartitions.Contains(fileName))
                            {
                                continue;
                            }

                            // 添加槽位后缀
                            string partitionWithSlot = $"{fileName}_{currentSlot}";
                            
                            // 检查是否在设备分区表中
                            if (devicePartitions.Contains(partitionWithSlot))
                            {
                                imagesToFlash.Add((fileName, imgFile));
                            }
                        }
                    }

                    // 按文件大小排序
                    LogStepBegin("对分区刷写任务进行排序");
                    imagesToFlash = imagesToFlash.OrderBy(p => new FileInfo(p.filePath).Length).ToList();
                    LogStepEndOk();

                    LogToOugaFlash(
                        $"总结刷写任务，售后包FastbootD阶段共 {imagesToFlash.Count} 个分区.");

                    // 在FastbootD模式下刷写剩余分区（AB槽位都刷）
                    foreach (var (partitionName, filePath) in imagesToFlash)
                    {
                        var fileName = Path.GetFileName(filePath);
                        
                        // 刷写到A槽位
                        string targetA = $"{partitionName}_a";
                        LogStepBeginNoTime($"[Flashing] {fileName} → {targetA}.img");
                        var okA = await FlashPartitionToSlot(partitionName, filePath, "a");
                        if (okA) LogStepEndOk(); else LogStepEndError();
                        
                        // 刷写到B槽位
                        string targetB = $"{partitionName}_b";
                        LogStepBeginNoTime($"[Flashing] {fileName} → {targetB}.img");
                        var okB = await FlashPartitionToSlot(partitionName, filePath, "b");
                        if (okB) LogStepEndOk(); else LogStepEndError();
                    }

                    // 解析分区表判断最佳启动槽位
                    LogStepBegin("判断最佳启动槽位");
                    
                    string getvarOutputForSlot = await ExecuteFastbootCommand(fastbootPath, "getvar all");
                    var slotPartitions = new[] { "system", "odm", "vendor", "product", "system_ext", "system_dlkm", 
                                                 "vendor_dlkm", "odm_dlkm", "my_bigball", "my_carrier", "my_company", 
                                                 "my_engineering", "my_heytap", "my_manifest", "my_preload", 
                                                 "my_product", "my_region", "my_stock" };
                    
                    long slotASize = 0;
                    long slotBSize = 0;
                    
                    if (!string.IsNullOrEmpty(getvarOutputForSlot))
                    {
                        var lines = getvarOutputForSlot.Split('\n');
                        foreach (var line in lines)
                        {
                            if (line.Contains("partition-size:"))
                            {
                                var match = System.Text.RegularExpressions.Regex.Match(line, @"partition-size:([^:]+):\s*0x([0-9A-Fa-f]+)");
                                if (match.Success)
                                {
                                    string partitionName = match.Groups[1].Value.Trim();
                                    string sizeHex = match.Groups[2].Value.Trim();
                                    
                                    if (long.TryParse(sizeHex, System.Globalization.NumberStyles.HexNumber, null, out long size))
                                    {
                                        // 检查是否是我们关注的分区
                                        foreach (var slotPartition in slotPartitions)
                                        {
                                            if (partitionName.Equals($"{slotPartition}_a", StringComparison.OrdinalIgnoreCase))
                                            {
                                                slotASize += size;
                                                break;
                                            }
                                            else if (partitionName.Equals($"{slotPartition}_b", StringComparison.OrdinalIgnoreCase))
                                            {
                                                slotBSize += size;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    
                    string bestSlot = slotASize >= slotBSize ? "a" : "b";
                    LogStepEndOk();
                    LogToOugaFlashDual($"判断最佳启动槽位为", "Black", $"{bestSlot.ToUpper()}槽", "Green");
                    
                    // 设置启动槽位
                    LogStepBegin($"设置槽位：{bestSlot.ToUpper()}");
                    await ExecuteFastbootCommand(fastbootPath, $"set_active {bestSlot}");
                    LogStepEndDone();

                    // 检查是否需要清除数据或自动重启
                    if (AfterSalesClearDataCheckBox.IsChecked == true || AfterSalesAutoRebootCheckBox.IsChecked == true)
                    {
                        // 在FBD模式下清除数据
                        if (AfterSalesClearDataCheckBox.IsChecked == true)
                        {
                            LogStepBegin("清除手机数据");
                            await ExecuteFastbootCommand(fastbootPath, "erase userdata");
                            await ExecuteFastbootCommand(fastbootPath, "erase metadata");
                            await ExecuteFastbootCommand(fastbootPath, "-w");
                            
                            string programDirectory = AppDomain.CurrentDomain.BaseDirectory;
                            string miscFilePath = Path.Combine(programDirectory, "tmp", "misc.img");
                            if (File.Exists(miscFilePath))
                            {
                                await ExecuteFastbootCommand(fastbootPath, $"flash misc \"{miscFilePath}\"");
                            }
                            LogStepEndOk();

                            LogStepBegin("擦除FRP");
                            await ExecuteFastbootCommand(fastbootPath, "erase frp");
                            LogStepEndOk();
                        }

                        // 自动重启
                        if (AfterSalesAutoRebootCheckBox.IsChecked == true)
                        {
                            LogStepBegin("自动重启设备");
                            await ExecuteFastbootCommand(fastbootPath, "reboot");
                            LogStepEndOk();
                        }
                    }

                    LogStepBegin("结束fastboot.exe进程");
                    LogStepEndOk();
                    
                    HideProgressBar();
                    LogStepBegin("委托主页开始异步设备检测");
                    HandleStartDeviceDetection();
                    LogStepEndOk();
                    return;
                }

                // 检查是否开启了FBD模式
                if (AfterSalesFastbootDModeToggle.IsChecked == true)
                {
                    LogToOugaFlashDual(
                        "用户选择：",
                        "Black",
                        "售后包FastbootD模式",
                        "Purple");

                    // 初次连接允许设备处于Bootloader Fastboot或FastbootD，
                    // 随后始终绑定本次检测到的序列号。
                    string? detectedDeviceSerial = await WaitForOugaFastbootDeviceAsync(
                        "Fastboot",
                        requireFastbootd: null,
                        timeoutSeconds: OujiaFastbootDeviceWaitTimeoutSeconds);
                    if (string.IsNullOrWhiteSpace(detectedDeviceSerial))
                    {
                        HideProgressBar();
                        LogStepBegin("委托主页开始异步设备检测");
                        HandleStartDeviceDetection();
                        LogStepEndOk();
                        return;
                    }

                    string afterSalesDeviceSerial = detectedDeviceSerial;
                    if (!await EnsureOugaFastbootConnectionStableAsync(
                            afterSalesDeviceSerial,
                            "等待设备连接稳定..."))
                    {
                        HideProgressBar();
                        LogStepBegin("委托主页开始异步设备检测");
                        HandleStartDeviceDetection();
                        LogStepEndOk();
                        return;
                    }

                    // 步骤2：检测是否处于fastbootd模式
                    bool isFastbootd = await IsOugaFastbootdAsync(afterSalesDeviceSerial);
                    LogToOugaFlashDual(
                        "设备是否处于FastbootD模式...",
                        "Black",
                        isFastbootd ? "是" : "否",
                        isFastbootd ? "Green" : "Blue");
                    if (!isFastbootd)
                    {
                        if (!await RebootOugaDeviceAsync(
                                afterSalesDeviceSerial,
                                toFastbootd: true))
                        {
                            LogToOugaFlash("无法重启到FastbootD模式，请尝试修复FastbootD", "Red");
                            HideProgressBar();
                            LogStepBegin("委托主页开始异步设备检测");
                            HandleStartDeviceDetection();
                            LogStepEndOk();
                            return;
                        }

                        string? fastbootdSerial = await WaitForOugaFastbootDeviceAsync(
                            "FastbootD",
                            afterSalesDeviceSerial,
                            requireFastbootd: true,
                            timeoutSeconds: OujiaFastbootDeviceWaitTimeoutSeconds);
                        if (string.IsNullOrWhiteSpace(fastbootdSerial))
                        {
                            HideProgressBar();
                            LogStepBegin("委托主页开始异步设备检测");
                            HandleStartDeviceDetection();
                            LogStepEndOk();
                            return;
                        }

                        afterSalesDeviceSerial = fastbootdSerial;
                        if (!await EnsureOugaFastbootConnectionStableAsync(
                                afterSalesDeviceSerial,
                                "等待设备连接稳定...",
                                requireFastbootd: true))
                        {
                            HideProgressBar();
                            LogStepBegin("委托主页开始异步设备检测");
                            HandleStartDeviceDetection();
                            LogStepEndOk();
                            return;
                        }
                    }

                    // 步骤3：获取当前设备的启动槽位
                    LogToOugaFlash("正在获取设备槽位信息...");
                    string currentSlot = await GetCurrentSlot();
                    if (string.IsNullOrEmpty(currentSlot))
                    {
                        LogToOugaFlash("无法获取设备槽位，默认使用槽位a", "Yellow");
                        currentSlot = "a";
                    }
                    else
                    {
                        LogToOugaFlashDual(
                            "设备当前启动槽位：",
                            "Black",
                            $"{currentSlot.ToUpper()}槽",
                            "Green");
                    }

                    // 步骤4：解析分区表并删除COW分区
                    LogStepBegin("解析COW快照分区");
                    bool cowOk = await DeleteCowPartitions();
                    if (cowOk) LogStepEndOk(); else LogStepEndError();
                    LogStepBegin("解析分区表");
                    LogStepEndOk();

                    // 等待设备状态稳定
                    await Task.Delay(1000);

                    // 步骤5：获取解析后的分区表
                    string fastbootPath = GetFastbootPath();
                    string getvarOutput = await ExecuteFastbootCommand(fastbootPath, "getvar all");

                    // 解析分区表中的所有分区名称
                    HashSet<string> devicePartitions =
                        ParseOujiaAvailablePartitionNames(getvarOutput);

                    // 步骤6：匹配分区
                    var modemPartitions = new List<PartitionInfo>();
                    var normalPartitions = new List<PartitionInfo>();

                    LogStepBegin("正在匹配分区");
                    foreach (var partition in partitionsToFlash)
                    {
                        // 分离modem分区
                        if (partition.PartitionName.ToLower().Contains("modem"))
                        {
                            modemPartitions.Add(partition);
                            continue;
                        }

                        // 为分区名添加槽位后缀
                        string partitionWithSlot = $"{partition.PartitionName}_{currentSlot}";

                        // 检查是否在设备分区表中
                        if (devicePartitions.Contains(partitionWithSlot))
                        {
                            normalPartitions.Add(partition);
                        }
                    }
                    LogStepEndOk();

                    // 日志优化：总结刷写任务
                    LogToOugaFlash(
                        $"总结刷写任务，售后包FastbootD阶段 {normalPartitions.Count} 个分区，Fastboot阶段 {modemPartitions.Count} 个分区.");

                    // 对分区进行从小到大排序
                    LogStepBegin("对分区刷写任务进行排序");
                    normalPartitions = normalPartitions.OrderBy(p => {
                        if (string.IsNullOrEmpty(p.FilePath) || !System.IO.File.Exists(p.FilePath))
                            return 0;
                        return new System.IO.FileInfo(p.FilePath).Length;
                    }).ToList();
                    LogStepEndOk();

                    // 刷写普通分区
                    foreach (var partition in normalPartitions)
                    {
                        var fileName = Path.GetFileName(partition.FilePath);
                        string target = $"{partition.PartitionName}_{currentSlot}";
                        LogStepBeginNoTime($"[Flashing] {fileName} → {target}.img");

                        // 定义进度回调
                        currentFlashedBytes = ReadFlashedBytesFromFile();
                        long bytesBeforeThisFile = currentFlashedBytes;
                        Dispatcher.Invoke(() => {
                            if (totalFlashBytes > 0)
                            {
                                _lastGlobalProgressBytes = bytesBeforeThisFile;
                                FlashProgressPercentage = (double)_lastGlobalProgressBytes / totalFlashBytes * 100.0;
                                FlashProgressDetailText = $"{FormatFileSize(_lastGlobalProgressBytes)}/{totalSizeStr}";
                            }
                        });

                        Action<long> progressAction = (transferredBytes) => {
                            long total = bytesBeforeThisFile + transferredBytes;
                            Dispatcher.Invoke(() => {
                                if (totalFlashBytes > 0)
                                {
                                    if (total > _lastGlobalProgressBytes) _lastGlobalProgressBytes = total;
                                    FlashProgressPercentage = (double)_lastGlobalProgressBytes / totalFlashBytes * 100.0;
                                    FlashProgressDetailText = $"{FormatFileSize(_lastGlobalProgressBytes)}/{totalSizeStr}";
                                    WriteFlashProgressFile(_lastGlobalProgressBytes, totalFlashBytes);
                                }
                            });
                        };

                        var ok = await FlashPartitionToSlot(partition.PartitionName, partition.FilePath, currentSlot, progressAction);
                        if (ok) 
                        {
                            LogStepEndOk();
                            if (System.IO.File.Exists(partition.FilePath))
                            {
                                currentFlashedBytes += new System.IO.FileInfo(partition.FilePath).Length;
                                if (totalFlashBytes > 0)
                                {
                                    _lastGlobalProgressBytes = currentFlashedBytes;
                                    FlashProgressPercentage = (double)_lastGlobalProgressBytes / totalFlashBytes * 100.0;
                                    FlashProgressDetailText = $"{FormatFileSize(_lastGlobalProgressBytes)}/{totalSizeStr}";
                                    WriteFlashProgressFile(currentFlashedBytes, totalFlashBytes);
                                }
                            }
                        } 
                        else 
                        {
                            LogStepEndError();
                            if (System.IO.File.Exists(partition.FilePath))
                            {
                                currentFlashedBytes += new System.IO.FileInfo(partition.FilePath).Length;
                                if (totalFlashBytes > 0)
                                {
                                    _lastGlobalProgressBytes = currentFlashedBytes;
                                    FlashProgressPercentage = (double)_lastGlobalProgressBytes / totalFlashBytes * 100.0;
                                    FlashProgressDetailText = $"{FormatFileSize(_lastGlobalProgressBytes)}/{totalSizeStr}";
                                    WriteFlashProgressFile(currentFlashedBytes, totalFlashBytes);
                                }
                            }
                        }
                    }

                    // 步骤7：处理modem分区（需要在Fastboot模式下刷写）
                    if (modemPartitions.Count > 0)
                    {
                        if (!await RebootOugaDeviceAsync(
                                afterSalesDeviceSerial,
                                toFastbootd: false))
                        {
                            LogToOugaFlash("无法重启到Fastboot模式", "Red");
                        }
                        else
                        {
                            string? fastbootSerial = await WaitForOugaFastbootDeviceAsync(
                                "Fastboot",
                                afterSalesDeviceSerial,
                                requireFastbootd: false,
                                timeoutSeconds: OujiaFastbootDeviceWaitTimeoutSeconds);
                            if (!string.IsNullOrWhiteSpace(fastbootSerial))
                            {
                                afterSalesDeviceSerial = fastbootSerial;

                                // 刷写modem分区
                                foreach (var modemPartition in modemPartitions)
                                {
                                    var fileName = Path.GetFileName(modemPartition.FilePath);

                                    // Modem分区需要刷写到A和B两个槽位
                                    LogStepBeginNoTime($"[Flashing] {fileName} → modem_a.img");

                                    currentFlashedBytes = ReadFlashedBytesFromFile();
                                    long bytesBeforeModemA = currentFlashedBytes;
                                    long modemFileSize = new System.IO.FileInfo(modemPartition.FilePath).Length;
                                    long halfModemSize = modemFileSize / 2;

                                    Action<long> progressActionA = (transferredBytes) => {
                                        long total = bytesBeforeModemA + (transferredBytes / 2);
                                        Dispatcher.Invoke(() => {
                                            if (totalFlashBytes > 0)
                                            {
                                                if (total > _lastGlobalProgressBytes) _lastGlobalProgressBytes = total;
                                                FlashProgressPercentage = (double)_lastGlobalProgressBytes / totalFlashBytes * 100.0;
                                                FlashProgressDetailText = $"{FormatFileSize(_lastGlobalProgressBytes)}/{totalSizeStr}";
                                                WriteFlashProgressFile(_lastGlobalProgressBytes, totalFlashBytes);
                                            }
                                        });
                                    };

                                    var okA = await FlashPartitionToSlot("modem", modemPartition.FilePath, "a", progressActionA);
                                    if (okA) 
                                    {
                                        LogStepEndOk();
                                        currentFlashedBytes += halfModemSize;
                                        if (totalFlashBytes > 0)
                                        {
                                            _lastGlobalProgressBytes = currentFlashedBytes;
                                            FlashProgressPercentage = (double)_lastGlobalProgressBytes / totalFlashBytes * 100.0;
                                            FlashProgressDetailText = $"{FormatFileSize(_lastGlobalProgressBytes)}/{totalSizeStr}";
                                            WriteFlashProgressFile(currentFlashedBytes, totalFlashBytes);
                                        }
                                    } 
                                    else 
                                    {
                                        LogStepEndError();
                                    }

                                    // 刷写到槽位B
                                    LogStepBeginNoTime($"[Flashing] {fileName} → modem_b.img");

                                    long bytesBeforeModemB = currentFlashedBytes;

                                    Action<long> progressActionB = (transferredBytes) => {
                                        long total = bytesBeforeModemB + (transferredBytes / 2);
                                        Dispatcher.Invoke(() => {
                                            if (totalFlashBytes > 0)
                                            {
                                                if (total > _lastGlobalProgressBytes) _lastGlobalProgressBytes = total;
                                                FlashProgressPercentage = (double)_lastGlobalProgressBytes / totalFlashBytes * 100.0;
                                                FlashProgressDetailText = $"{FormatFileSize(_lastGlobalProgressBytes)}/{totalSizeStr}";
                                                WriteFlashProgressFile(_lastGlobalProgressBytes, totalFlashBytes);
                                            }
                                        });
                                    };

                                    var okB = await FlashPartitionToSlot("modem", modemPartition.FilePath, "b", progressActionB);
                                    if (okB) 
                                    {
                                        LogStepEndOk();
                                        currentFlashedBytes += halfModemSize;
                                        if (totalFlashBytes > 0)
                                        {
                                            _lastGlobalProgressBytes = currentFlashedBytes;
                                            FlashProgressPercentage = (double)_lastGlobalProgressBytes / totalFlashBytes * 100.0;
                                            FlashProgressDetailText = $"{FormatFileSize(_lastGlobalProgressBytes)}/{totalSizeStr}";
                                            WriteFlashProgressFile(currentFlashedBytes, totalFlashBytes);
                                        }
                                    } 
                                    else 
                                    {
                                        LogStepEndError();
                                    }
                                }
                            }
                            else
                            {
                                LogToOugaFlash(
                                    "等待Fastboot设备连接超时，跳过modem分区刷写",
                                    "Red");
                            }
                        }
                    }

                    // 检查是否需要清除数据或自动重启
                    if (AfterSalesClearDataCheckBox.IsChecked == true || AfterSalesAutoRebootCheckBox.IsChecked == true)
                    {
                        // 与全量包收尾一致：仅当设备不在FastbootD时才执行重启，
                        // 并等待同一序列号重新连接、稳定。
                        bool fastbootdReady =
                            await IsOugaFastbootdAsync(afterSalesDeviceSerial);
                        if (!fastbootdReady)
                        {
                            if (await RebootOugaDeviceAsync(
                                    afterSalesDeviceSerial,
                                    toFastbootd: true))
                            {
                                string? fastbootdSerial =
                                    await WaitForOugaFastbootDeviceAsync(
                                        "FastbootD",
                                        afterSalesDeviceSerial,
                                        requireFastbootd: true,
                                        timeoutSeconds: OujiaFastbootDeviceWaitTimeoutSeconds);
                                if (!string.IsNullOrWhiteSpace(fastbootdSerial))
                                {
                                    afterSalesDeviceSerial = fastbootdSerial;
                                    fastbootdReady =
                                        await EnsureOugaFastbootConnectionStableAsync(
                                            afterSalesDeviceSerial,
                                            "等待设备连接稳定...",
                                            requireFastbootd: true);
                                }
                            }
                        }

                        if (fastbootdReady)
                        {
                            // 在FBD模式下清除数据
                            if (AfterSalesClearDataCheckBox.IsChecked == true)
                            {
                                LogStepBegin("清除手机数据");
                                
                                // 1. erase userdata
                                await ExecuteFastbootCommand(fastbootPath, "erase userdata");
                                
                                // 2. erase metadata
                                await ExecuteFastbootCommand(fastbootPath, "erase metadata");
                                
                                // 3. fastboot -w
                                await ExecuteFastbootCommand(fastbootPath, "-w");
                                
                                // 4. 刷写 misc.img（如果存在，属于清除数据的一部分）
                                string programDirectory = AppDomain.CurrentDomain.BaseDirectory;
                                string miscFilePath = Path.Combine(programDirectory, "tmp", "misc.img");
                                if (File.Exists(miscFilePath))
                                {
                                    await ExecuteFastbootCommand(fastbootPath, $"flash misc \"{miscFilePath}\"");
                                }
                                
                                LogStepEndOk();

                                LogStepBegin("擦除FRP");
                                await ExecuteFastbootCommand(fastbootPath, "erase frp");
                                LogStepEndOk();
                            }

                            // 自动重启
                            if (AfterSalesAutoRebootCheckBox.IsChecked == true)
                            {
                                LogStepBegin("自动重启设备");
                                await ExecuteFastbootCommand(fastbootPath, "reboot");
                                LogStepEndOk();
                            }
                        }
                        else
                        {
                            LogToOugaFlash(
                                "无法确认设备已进入FastbootD模式，跳过清除数据和自动重启操作",
                                "Red");
                        }
                    }

                    // 日志优化：结束fastboot进程
                    LogStepBegin("结束fastboot.exe进程");
                    LogStepEndOk();
                    
                    // 自动保存日志到下载目录
                    await GenerateAfterSalesFlashLogFile();
                }
                else
                {
                    LogToOugaFlash("请开启FBD模式开关", "Red");
                }

                HideProgressBar();
                LogStepBegin("委托主页开始异步设备检测");
                HandleStartDeviceDetection();
                LogStepEndOk();
            }
            catch (Exception ex)
            {
                LogToOugaFlash($"刷写过程中发生错误：{ex.Message}", "Red");
                HideProgressBar();
                
                // 即使出错也保存日志
                await GenerateAfterSalesFlashLogFile();
                
                LogStepBegin("委托主页开始异步设备检测");
                HandleStartDeviceDetection();
                LogStepEndOk();
            }
        }


        private void AfterSalesFastbootDModeToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (AfterSalesBootloaderModeToggle != null && AfterSalesBootloaderModeToggle.IsChecked == true)
            {
                AfterSalesBootloaderModeToggle.IsChecked = false;
            }
            if (AfterSalesAutoBrickRecoveryModeToggle != null && AfterSalesAutoBrickRecoveryModeToggle.IsChecked == true)
            {
                AfterSalesAutoBrickRecoveryModeToggle.IsChecked = false;
            }
        }

        private void AfterSalesFastbootDModeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
        }

        private void AfterSalesBootloaderModeToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (AfterSalesFastbootDModeToggle != null && AfterSalesFastbootDModeToggle.IsChecked == true)
            {
                AfterSalesFastbootDModeToggle.IsChecked = false;
            }
            if (AfterSalesAutoBrickRecoveryModeToggle != null && AfterSalesAutoBrickRecoveryModeToggle.IsChecked == true)
            {
                AfterSalesAutoBrickRecoveryModeToggle.IsChecked = false;
            }
        }

        private void AfterSalesBootloaderModeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
        }

        private void AfterSalesAutoBrickRecoveryModeToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (AfterSalesFastbootDModeToggle != null && AfterSalesFastbootDModeToggle.IsChecked == true)
            {
                AfterSalesFastbootDModeToggle.IsChecked = false;
            }
            if (AfterSalesBootloaderModeToggle != null && AfterSalesBootloaderModeToggle.IsChecked == true)
            {
                AfterSalesBootloaderModeToggle.IsChecked = false;
            }
        }

        private void AfterSalesAutoBrickRecoveryModeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
        }

        private sealed class AutoBrickRecoveryRomDeviceOption
        {
            public string DisplayName { get; init; } = string.Empty;
            public string Brand { get; init; } = "OnePlus";
            public string Series { get; init; } = string.Empty;
            public string ApiDeviceName { get; init; } = string.Empty;
        }

        // 全自动救砖机型取自 ROM 专区的一加售后包目录（2026-08-14 盘点）。
        // 这里有意固定机型与接口参数，避免云端系列/机型列表短时异常影响入口；
        // 具体版本和下载地址仍由 ROM 专区接口动态返回。数字系列仅保留一加 9 及以上，
        // ACE/Pad 等同期及后续产品线继续保留。
        private static readonly IReadOnlyList<AutoBrickRecoveryRomDeviceOption>
            AutoBrickRecoveryRomDevices = new[]
            {
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加ACE6T", Series = "ACE系列", ApiDeviceName = "一加 Ace 6T" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加ACE6", Series = "ACE系列", ApiDeviceName = "一加 Ace 6" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加ACE5Pro", Series = "ACE系列", ApiDeviceName = "一加 Ace 5 Pro" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加ACE5", Series = "ACE系列", ApiDeviceName = "一加 Ace 5" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加ACE5至尊版", Series = "ACE系列", ApiDeviceName = "一加 Ace 5 至尊版" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加ACE5竞速版", Series = "ACE系列", ApiDeviceName = "一加_Ace_5_竞速版" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加ACE3Pro", Series = "ACE系列", ApiDeviceName = "一加 Ace 3 Pro" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加ACE3", Series = "ACE系列", ApiDeviceName = "一加 Ace 3" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加ACE3V", Series = "ACE系列", ApiDeviceName = "一加 Ace 3V" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加ACE2Pro", Series = "ACE系列", ApiDeviceName = "一加 Ace 2 Pro" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加ACE2V", Series = "ACE系列", ApiDeviceName = "一加 Ace 2V" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加ACE2", Series = "ACE系列", ApiDeviceName = "一加Ace_2" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加ACE Pro", Series = "ACE系列", ApiDeviceName = "一加 Ace Pro" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加ACE", Series = "ACE系列", ApiDeviceName = "一加 Ace" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加ACE竞速版", Series = "ACE系列", ApiDeviceName = "一加_Ace_竞速版" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加15", Series = "数字系列", ApiDeviceName = "一加 15" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加13T", Series = "数字系列", ApiDeviceName = "一加13T" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加13", Series = "数字系列", ApiDeviceName = "一加13" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加12", Series = "数字系列", ApiDeviceName = "一加12" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加11", Series = "数字系列", ApiDeviceName = "一加11" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加10Pro", Series = "数字系列", ApiDeviceName = "一加 10 Pro" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加10R", Series = "数字系列", ApiDeviceName = "一加10R_5G" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加9", Series = "数字系列", ApiDeviceName = "OnePlus_9" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加9RT", Series = "数字系列", ApiDeviceName = "OnePlus_9RT_5G" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加9R", Series = "数字系列", ApiDeviceName = "OnePlus_9R_5G" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加9Pro", Series = "数字系列", ApiDeviceName = "OnePlus_9_Pro" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加Pad", Series = "Pad系列", ApiDeviceName = "一加平板" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加Pad2", Series = "Pad系列", ApiDeviceName = "一加平板 2" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加PadPro", Series = "Pad系列", ApiDeviceName = "一加平板_Pro" },
                new AutoBrickRecoveryRomDeviceOption { DisplayName = "一加Pad2Pro", Series = "Pad系列", ApiDeviceName = "一加平板_2_Pro" }
            };

        // 全自动救砖模式处理方法
        private async Task HandleAutoBrickRecoveryMode()
        {
            try
            {
                LogToOugaFlash("启动全自动救砖模式", "Green");
                
                // 1. 显示用户已确认支持自动救砖的机型。
                string selectedModel = await ShowDeviceSelectionDialog(
                    AutoBrickRecoveryRomDevices.Select(item => item.DisplayName).ToList());
                if (string.IsNullOrEmpty(selectedModel))
                {
                    LogToOugaFlash("用户取消了机型选择", "Yellow");
                    LogStepBegin("委托主页开始异步设备检测");
                    HandleStartDeviceDetection();
                    LogStepEndOk();
                    return;
                }

                AutoBrickRecoveryRomDeviceOption? selectedDevice =
                    AutoBrickRecoveryRomDevices.FirstOrDefault(item =>
                        item.DisplayName.Equals(selectedModel, StringComparison.OrdinalIgnoreCase));
                if (selectedDevice == null)
                {
                    LogToOugaFlash("无法识别所选机型，请重新选择", "Red");
                    LogStepBegin("委托主页开始异步设备检测");
                    HandleStartDeviceDetection();
                    LogStepEndOk();
                    return;
                }

                LogToOugaFlashDual(
                    "已选择机型：",
                    "Black",
                    selectedDevice.DisplayName,
                    "Blue");

                // 2. 从ROM专区售后包接口动态加载该机型的所有版本。
                LogStepBegin("加载售后包版本列表");
                List<string> availableVersions;
                try
                {
                    availableVersions = await FetchRomApiVersionsAsync(
                        RomApiPackageTypeAfterSales,
                        selectedDevice.Brand,
                        selectedDevice.Series,
                        selectedDevice.ApiDeviceName,
                        CancellationToken.None).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    LogStepEndError();
                    LogToOugaFlashDual(
                        "加载售后包版本失败：",
                        "Black",
                        ex.Message,
                        "Red");
                    LogStepBegin("委托主页开始异步设备检测");
                    HandleStartDeviceDetection();
                    LogStepEndOk();
                    return;
                }

                if (availableVersions.Count == 0)
                {
                    LogStepEndError();
                    LogToOugaFlash("ROM专区暂未收录该机型的售后包版本", "Red");
                    LogStepBegin("委托主页开始异步设备检测");
                    HandleStartDeviceDetection();
                    LogStepEndOk();
                    return;
                }
                LogStepEndOk();

                string selectedVersion = await ShowAutoBrickRecoveryVersionDialog(
                    selectedDevice.DisplayName,
                    availableVersions);
                if (string.IsNullOrWhiteSpace(selectedVersion))
                {
                    LogToOugaFlash("用户取消了版本选择", "Yellow");
                    LogStepBegin("委托主页开始异步设备检测");
                    HandleStartDeviceDetection();
                    LogStepEndOk();
                    return;
                }

                LogToOugaFlashDual(
                    "已选择售后包版本：",
                    "Black",
                    selectedVersion,
                    "Blue");

                // 3. 从ROM专区接口解析所选版本的实时下载地址。
                LogStepBegin("获取售后包下载链接");
                RomApiDownloadLinkResponse downloadResponse;
                try
                {
                    downloadResponse = await FetchRomApiDownloadLinksAsync(
                        RomApiPackageTypeAfterSales,
                        selectedDevice.Brand,
                        selectedDevice.Series,
                        selectedDevice.ApiDeviceName,
                        selectedVersion,
                        CancellationToken.None).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    LogStepEndError();
                    LogToOugaFlashDual(
                        "获取售后包下载链接失败：",
                        "Black",
                        ex.Message,
                        "Red");
                    LogStepBegin("委托主页开始异步设备检测");
                    HandleStartDeviceDetection();
                    LogStepEndOk();
                    return;
                }

                List<string> availableDownloadLinks = PreferRomArchiveLinks(
                    RomApiPackageTypeAfterSales,
                    selectedDevice.Brand,
                    downloadResponse.Links ?? new List<string>());
                string downloadUrl =
                    availableDownloadLinks.FirstOrDefault(IsLikelyRomArchiveLink) ??
                    availableDownloadLinks.FirstOrDefault() ??
                    string.Empty;
                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    LogStepEndError();
                    LogToOugaFlash("当前版本没有可用的售后包下载链接", "Red");
                    LogStepBegin("委托主页开始异步设备检测");
                    HandleStartDeviceDetection();
                    LogStepEndOk();
                    return;
                }
                LogStepEndOk();

                // 4. 选择保存路径
                var folderDialog = new System.Windows.Forms.FolderBrowserDialog();
                folderDialog.Description = "请选择刷机包下载保存路径";
                if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    LogToOugaFlash("用户取消了路径选择", "Yellow");
                    LogStepBegin("委托主页开始异步设备检测");
                    HandleStartDeviceDetection();
                    LogStepEndOk();
                    return;
                }

                string savePath = folderDialog.SelectedPath;
                LogToOugaFlash($"保存路径：{savePath}", "Green");

                // 5. 检查磁盘空间
                DriveInfo savePathDrive = new DriveInfo(Path.GetPathRoot(savePath));
                DriveInfo appDrive = new DriveInfo(Path.GetPathRoot(AppDomain.CurrentDomain.BaseDirectory));

                long requiredSpace = 30L * 1024 * 1024 * 1024; // 30GB

                if (savePathDrive.AvailableFreeSpace < requiredSpace)
                {
                    LogToOugaFlash($"保存路径磁盘空间不足，需要至少30GB，当前可用：{savePathDrive.AvailableFreeSpace / 1024 / 1024 / 1024}GB", "Red");
                    LogStepBegin("委托主页开始异步设备检测");
                    HandleStartDeviceDetection();
                    LogStepEndOk();
                    return;
                }

                if (appDrive.AvailableFreeSpace < requiredSpace)
                {
                    LogToOugaFlash($"工具所在磁盘空间不足，需要至少30GB，当前可用：{appDrive.AvailableFreeSpace / 1024 / 1024 / 1024}GB", "Red");
                    LogStepBegin("委托主页开始异步设备检测");
                    HandleStartDeviceDetection();
                    LogStepEndOk();
                    return;
                }

                LogToOugaFlash("磁盘空间检查通过", "Green");

                // 6. 下载刷机包
                string fileName = Uri.TryCreate(
                        downloadUrl,
                        UriKind.Absolute,
                        out Uri? downloadUri)
                    ? Path.GetFileName(downloadUri.LocalPath)
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = $"{selectedDevice.ApiDeviceName}_{DateTime.Now:yyyyMMddHHmmss}.zip";
                }
                string zipFilePath = Path.Combine(savePath, fileName);
                 
                LogToOugaFlash("开始下载刷机包...", "Green");
                bool downloadSuccess = await DownloadWithAria2(
                    downloadUrl,
                    zipFilePath,
                    downloadResponse.RequestHeaders);
                
                if (!downloadSuccess)
                {
                    LogToOugaFlash("下载失败", "Red");
                    LogStepBegin("委托主页开始异步设备检测");
                    HandleStartDeviceDetection();
                    LogStepEndOk();
                    return;
                }

                LogToOugaFlash("下载完成", "Green");

                // 7. 解压刷机包
                string extractPath = Path.Combine(savePath, Path.GetFileNameWithoutExtension(fileName));
                LogToOugaFlash("开始解压刷机包...", "Green");
                
                bool extractSuccess = await ExtractWith7z(zipFilePath, extractPath);
                
                if (!extractSuccess)
                {
                    LogToOugaFlash("解压失败", "Red");
                    LogStepBegin("委托主页开始异步设备检测");
                    HandleStartDeviceDetection();
                    LogStepEndOk();
                    return;
                }

                LogToOugaFlash("解压完成", "Green");

                // 8. 删除zip文件
                try
                {
                    File.Delete(zipFilePath);
                    LogToOugaFlash("已删除压缩包", "Green");
                }
                catch (Exception ex)
                {
                    LogToOugaFlash($"删除压缩包失败: {ex.Message}", "Yellow");
                }

                // 9. 查找实际的散包文件夹（可能在子目录中）
                string actualPackPath = extractPath;
                if (Directory.Exists(extractPath))
                {
                    var subDirs = Directory.GetDirectories(extractPath);
                    if (subDirs.Length == 1)
                    {
                        // 如果只有一个子目录，使用该子目录
                        actualPackPath = subDirs[0];
                    }
                }

                // 10. 设置散包路径到文本框
                await Dispatcher.InvokeAsync(() =>
                {
                    AfterSalesFlashPackTextBox.Text = actualPackPath;
                });

                // 11. 加载分区列表
                LogToOugaFlash("正在加载分区列表...", "Green");
                ParseAfterSalesImages(actualPackPath);

                LogToOugaFlash("刷机包准备完成，开始刷写...", "Green");

                // 等待分区加载完成
                await Task.Delay(500);

                // 12. 开启FB模式并继续刷写流程
                await Dispatcher.InvokeAsync(() =>
                {
                    AfterSalesBootloaderModeToggle.IsChecked = true;
                });

                // 等待UI更新
                await Task.Delay(300);

                // 重新触发刷写流程（FB模式）
                AfterSalesStartFlashButton_Click(null, null);
            }
            catch (Exception ex)
            {
                LogToOugaFlash($"全自动救砖模式错误：{ex.Message}", "Red");
                LogStepBegin("委托主页开始异步设备检测");
                HandleStartDeviceDetection();
                LogStepEndOk();
            }
        }

        // 显示设备选择对话框
        private Task<string> ShowDeviceSelectionDialog(List<string> devices)
        {
            return ShowAutoBrickRecoverySelectionDialog(
                "选择设备机型",
                "请选择您的设备机型：",
                devices,
                width: 470,
                height: 560);
        }

        private Task<string> ShowAutoBrickRecoveryVersionDialog(
            string deviceName,
            IReadOnlyList<string> versions)
        {
            return ShowAutoBrickRecoverySelectionDialog(
                "选择售后包版本",
                $"请选择 {deviceName} 的售后包版本：",
                versions,
                width: 720,
                height: 560);
        }

        private async Task<string> ShowAutoBrickRecoverySelectionDialog(
            string title,
            string prompt,
            IReadOnlyList<string> items,
            double width,
            double height)
        {
            string selectedItem = string.Empty;

            await Dispatcher.InvokeAsync(() =>
            {
                var dialog = new Window
                {
                    Title = title,
                    Owner = this,
                    Width = width,
                    Height = height,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    Background = System.Windows.Media.Brushes.White,
                    FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI")
                };

                var rootGrid = new Grid
                {
                    Margin = new Thickness(18)
                };
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var titleTextBlock = new TextBlock
                {
                    Text = title,
                    FontSize = 17,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(38, 49, 66)),
                    Margin = new Thickness(0, 0, 0, 6)
                };
                Grid.SetRow(titleTextBlock, 0);
                rootGrid.Children.Add(titleTextBlock);

                var promptTextBlock = new TextBlock
                {
                    Text = prompt,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(100, 116, 139)),
                    Margin = new Thickness(0, 0, 0, 12)
                };
                Grid.SetRow(promptTextBlock, 1);
                rootGrid.Children.Add(promptTextBlock);

                var listBox = new System.Windows.Controls.ListBox
                {
                    FontSize = 12.5,
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(51, 65, 85)),
                    Background = new SolidColorBrush(MediaColor.FromRgb(250, 251, 253)),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(4),
                    HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
                    ItemsSource = items
                };

                var listBorder = new Border
                {
                    Background = new SolidColorBrush(MediaColor.FromRgb(250, 251, 253)),
                    BorderBrush = new SolidColorBrush(MediaColor.FromRgb(226, 232, 240)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(4),
                    Child = listBox
                };
                Grid.SetRow(listBorder, 2);
                rootGrid.Children.Add(listBorder);

                var buttonPanel = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    Margin = new Thickness(0, 14, 0, 0)
                };
                Grid.SetRow(buttonPanel, 3);

                var cancelButton = new System.Windows.Controls.Button
                {
                    Content = "取消",
                    Width = 88,
                    Height = 32,
                    IsCancel = true,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                if (TryFindResource("OugaFlashSecondaryButtonStyle") is Style secondaryStyle)
                {
                    cancelButton.Style = secondaryStyle;
                }

                var okButton = new System.Windows.Controls.Button
                {
                    Content = "确定",
                    Width = 88,
                    Height = 32,
                    IsDefault = true,
                    IsEnabled = false
                };
                if (TryFindResource("OugaFlashPrimaryButtonStyle") is Style primaryStyle)
                {
                    okButton.Style = primaryStyle;
                }

                void AcceptSelection()
                {
                    if (listBox.SelectedItem is not string value ||
                        string.IsNullOrWhiteSpace(value))
                    {
                        return;
                    }

                    selectedItem = value;
                    dialog.DialogResult = true;
                    dialog.Close();
                }

                listBox.SelectionChanged += (_, _) =>
                {
                    okButton.IsEnabled = listBox.SelectedItem != null;
                };
                listBox.MouseDoubleClick += (_, _) => AcceptSelection();
                okButton.Click += (_, _) => AcceptSelection();
                buttonPanel.Children.Add(cancelButton);
                buttonPanel.Children.Add(okButton);
                rootGrid.Children.Add(buttonPanel);

                dialog.Content = rootGrid;
                dialog.ShowDialog();
            });

            return selectedItem;
        }

        // 使用aria2c下载文件
        private async Task<bool> DownloadWithAria2(
            string url,
            string outputPath,
            IReadOnlyDictionary<string, string>? requestHeaders = null)
        {
            try
            {
                string aria2cPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "exe", "aria2c.exe");
                
                if (!File.Exists(aria2cPath))
                {
                    LogToOugaFlash($"找不到aria2c.exe: {aria2cPath}", "Red");
                    return false;
                }

                string outputDir = Path.GetDirectoryName(outputPath);
                string fileName = Path.GetFileName(outputPath);

                var startInfo = new ProcessStartInfo
                {
                    FileName = aria2cPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("-x");
                startInfo.ArgumentList.Add("16");
                startInfo.ArgumentList.Add("-s");
                startInfo.ArgumentList.Add("16");
                startInfo.ArgumentList.Add("-d");
                startInfo.ArgumentList.Add(outputDir);
                startInfo.ArgumentList.Add("-o");
                startInfo.ArgumentList.Add(fileName);
                if (requestHeaders != null)
                {
                    foreach (KeyValuePair<string, string> header in requestHeaders)
                    {
                        string headerName = header.Key?.Replace("\r", string.Empty)
                            .Replace("\n", string.Empty)
                            .Trim() ?? string.Empty;
                        string headerValue = header.Value?.Replace("\r", string.Empty)
                            .Replace("\n", string.Empty)
                            .Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(headerName) ||
                            string.IsNullOrWhiteSpace(headerValue))
                        {
                            continue;
                        }

                        startInfo.ArgumentList.Add($"--header={headerName}: {headerValue}");
                    }
                }
                startInfo.ArgumentList.Add(url);

                var process = new Process
                {
                    StartInfo = startInfo
                };

                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            LogToOugaFlash($"[下载] {e.Data}", "Gray");
                        }));
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            LogToOugaFlash($"[下载] {e.Data}", "Gray");
                        }));
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await Task.Run(() => process.WaitForExit());

                return process.ExitCode == 0 && File.Exists(outputPath);
            }
            catch (Exception ex)
            {
                LogToOugaFlash($"下载错误: {ex.Message}", "Red");
                return false;
            }
        }

        // 使用7z解压文件
        private async Task<bool> ExtractWith7z(string zipPath, string extractPath)
        {
            try
            {
                string sevenZipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "7z.exe");
                
                if (!File.Exists(sevenZipPath))
                {
                    LogToOugaFlash($"找不到7z.exe: {sevenZipPath}", "Red");
                    return false;
                }

                if (!Directory.Exists(extractPath))
                {
                    Directory.CreateDirectory(extractPath);
                }

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = sevenZipPath,
                        Arguments = $"x \"{zipPath}\" -o\"{extractPath}\" -y",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            LogToOugaFlash($"[解压] {e.Data}", "Gray");
                        }));
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            LogToOugaFlash($"[解压] {e.Data}", "Gray");
                        }));
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await Task.Run(() => process.WaitForExit());

                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                LogToOugaFlash($"解压错误: {ex.Message}", "Red");
                return false;
            }
        }

        // 格式化设备【双清】按钮点击事件
        private async Task<bool> DeleteCowPartitions(string deviceSerial = "")
        {
            try
            {
                string fastbootPath = GetOugaFastbootExecutablePath();
                if (string.IsNullOrEmpty(fastbootPath) || !File.Exists(fastbootPath))
                {
                    LogToOugaFlash("缺少fastboot.exe", "Red");
                    return false;
                }

                string targetSerial = string.IsNullOrWhiteSpace(deviceSerial)
                    ? GetOujiaFlashTargetSerial()
                    : deviceSerial;

                // 执行 fastboot getvar all 命令获取所有变量
                var getvarResult = await ExecuteOugaFastbootCommandAsync(
                    fastbootPath,
                    targetSerial,
                    "getvar all",
                    timeoutSeconds: 30);
                if (!getvarResult.Success)
                {
                    LogToOugaFlash("无法获取设备变量信息...", "Orange");
                    AppendOugaFastbootFailureOutput(getvarResult.Output);
                    return false;
                }

                string getvarOutput = getvarResult.Output;

                // 解析输出，查找cow分区
                var cowPartitions = new List<string>();
                var lines = getvarOutput.Split('\n');
                
                foreach (var line in lines)
                {
                    // 查找分区相关的行，通常格式为 "partition-size:partition_name: size"
                    if (line.Contains("partition-size:") && line.ToLower().Contains("cow"))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(line, @"partition-size:([^:]+):");
                        if (match.Success)
                        {
                            string partitionName = match.Groups[1].Value.Trim();
                            if (!cowPartitions.Contains(partitionName))
                            {
                                cowPartitions.Add(partitionName);
                            }
                        }
                    }
                }

                // 无论是否存在COW分区，执行静默删除流程并返回结果
                if (cowPartitions.Count > 0)
                {
                    foreach (var partitionName in cowPartitions)
                    {
                        string eraseCommand = $"delete-logical-partition {partitionName}";
                        var deleteResult = await ExecuteOugaFastbootCommandAsync(
                            fastbootPath,
                            targetSerial,
                            eraseCommand);
                        if (!deleteResult.Success &&
                            !IsOujiaLogicalPartitionMissingOutput(deleteResult.Output))
                        {
                            AppendOugaFastbootFailureOutput(deleteResult.Output);
                            return false;
                        }
                    }
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        

        private void OugaFlashButton_Click(object sender, RoutedEventArgs e)
        {
            // 显示欧加刷写视图，隐藏其他视图
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
            if (oujiaFlashView != null) oujiaFlashView.Visibility = Visibility.Visible;
            if (autorootView != null) autorootView.Visibility = Visibility.Collapsed;
            if (appManagementView != null) appManagementView.Visibility = Visibility.Collapsed;
            if (androidGeneralView != null) androidGeneralView.Visibility = Visibility.Collapsed;
            if (payloadView != null) payloadView.Visibility = Visibility.Collapsed;
            if (romDownloadView != null) romDownloadView.Visibility = Visibility.Collapsed;
            if (edlFlashView != null) edlFlashView.Visibility = Visibility.Collapsed;
            if (colorOSAssistantView != null) colorOSAssistantView.Visibility = Visibility.Collapsed;
            if (backupAssistantView != null) backupAssistantView.Visibility = Visibility.Collapsed;
            if (violetDownloadView != null) violetDownloadView.Visibility = Visibility.Collapsed;

            // 每次进入欧加线刷页时，统一恢复为全量包模式。
            ApplyOugaPackageModeSelection(useAfterSalesMode: false);

            // 更新按钮状态
            UpdateButtonStates("OugaFlash");
        }

        private void LoadImageFilesFromFolder(string folderPath)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    // 在日志框中显示错误信息
                 AppendOugaFlashParagraphLog("错误: 选择的文件夹不存在！", "Red");
                 OugaFlashLogTextBox.ScrollToEnd();
                    return;
                }
                
                // 支持的镜像文件扩展名
                string[] imageExtensions = { ".img", ".bin", ".raw", ".sparse" };
                
                var imageFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(file => imageExtensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                
                var partitionInfos = new ObservableCollection<PartitionInfo>();
                
                foreach (string filePath in imageFiles)
                {
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    FileInfo fileInfo = new FileInfo(filePath);
                    
                    var partitionInfo = new PartitionInfo
                    {
                        IsSelected = false,
                        PartitionName = fileName,
                        PartitionSize = FormatFileSize(fileInfo.Length),
                        FilePath = filePath
                    };
                    
                    partitionInfos.Add(partitionInfo);
                }
                
                // 更新DataGrid
                if (OugaPartitionTableDataGrid != null)
                {
                    OugaPartitionTableDataGrid.ItemsSource = partitionInfos;
                }
                
                // 自动全选所有加载的镜像
                foreach (var partition in partitionInfos)
                {
                    partition.IsSelected = true;
                }
                
                // 更新全选CheckBox状态
                var selectAllCheckBox = this.FindName("SelectAllCheckBox") as System.Windows.Controls.CheckBox;
                if (selectAllCheckBox != null)
                {
                    selectAllCheckBox.IsChecked = true;
                }
                
                // 在日志框中显示加载结果
                 LogToOugaFlashTriple(
                     "成功加载 ",
                     "Black",
                     partitionInfos.Count.ToString(),
                     "Purple",
                     " 个镜像文件，已自动全选",
                     "Black");
                 OugaFlashLogTextBox.ScrollToEnd();
            }
            catch (Exception ex)
            {
                // 在日志框中显示错误信息
                 AppendOugaFlashParagraphLog($"错误: 加载镜像文件时发生错误: {ex.Message}", "Red");
                 OugaFlashLogTextBox.ScrollToEnd();
            }
        }

        private void SelectAllCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (OugaPartitionTableDataGrid.ItemsSource is IEnumerable<PartitionInfo> partitionInfos)
            {
                foreach (var partition in partitionInfos)
                {
                    partition.IsSelected = true;
                }
                OugaPartitionTableDataGrid.Items.Refresh();
            }
        }

        private void SelectAllCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (OugaPartitionTableDataGrid.ItemsSource is IEnumerable<PartitionInfo> partitionInfos)
            {
                foreach (var partition in partitionInfos)
                {
                    partition.IsSelected = false;
                }
                OugaPartitionTableDataGrid.Items.Refresh();
            }
        }

    }
}
