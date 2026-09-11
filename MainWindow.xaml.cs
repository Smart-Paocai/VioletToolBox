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
using SharpVectors.Converters;
using System.IO.Compression;
using System.Net.Sockets;
using IOPath = System.IO.Path;
using IOFile = System.IO.File;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using WpfCursors = System.Windows.Input.Cursors;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPoint = System.Windows.Point;
using SmartTool;
using WpfApp1.Avb;
using test1;
using OPFlashTool.Services;

namespace WpfApp1
{
    using System.Windows.Data;
    using System.Globalization;

    public class EqualsToParameterConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }

    public class ProgressBarIndicatorWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3 ||
                !TryToDouble(values[0], out double value) ||
                !TryToDouble(values[1], out double maximum) ||
                !TryToDouble(values[2], out double actualWidth) ||
                maximum <= 0 ||
                actualWidth <= 0)
            {
                return 0d;
            }

            double ratio = Math.Clamp(value / maximum, 0, 1);
            return actualWidth * ratio;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return targetTypes.Select(_ => Binding.DoNothing).ToArray();
        }

        private static bool TryToDouble(object value, out double result)
        {
            if (value is double d)
            {
                result = d;
                return true;
            }

            if (value is IConvertible convertible)
                return double.TryParse(convertible.ToString(CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result);

            result = 0;
            return false;
        }
    }

    public sealed class SystemZoneLogTextBox : System.Windows.Controls.RichTextBox
    {
        private static readonly Regex TimestampRegex = new(
            @"^\[(?<timestamp>[^\]]+)\]\s*",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly System.Windows.Media.Brush TimestampBrush = CreateBrush(0x94, 0xA3, 0xB8);
        private static readonly System.Windows.Media.Brush BodyBrush = CreateBrush(0x33, 0x41, 0x55);
        private static readonly System.Windows.Media.Brush SecondaryBrush = CreateBrush(0x64, 0x74, 0x8B);
        private static readonly System.Windows.Media.Brush ActionBrush = CreateBrush(0x7C, 0x3A, 0xED);
        private static readonly System.Windows.Media.Brush SuccessBrush = CreateBrush(0x16, 0xA3, 0x4A);
        private static readonly System.Windows.Media.Brush ErrorBrush = CreateBrush(0xDC, 0x26, 0x26);
        private static readonly System.Windows.Media.Brush WarningBrush = CreateBrush(0xD9, 0x77, 0x06);
        private static readonly System.Windows.Media.Brush InfoBrush = CreateBrush(0x25, 0x63, 0xEB);

        private string _plainText = string.Empty;

        public string Text
        {
            get => _plainText;
            set
            {
                string nextText = value ?? string.Empty;
                bool isAppend = _plainText.Length > 0
                    && nextText.StartsWith(_plainText, StringComparison.Ordinal);

                if (isAppend)
                {
                    AppendStyledText(nextText.Substring(_plainText.Length));
                }
                else
                {
                    Document.Blocks.Clear();
                    AppendStyledText(nextText);
                }

                _plainText = nextText;
            }
        }

        public SystemZoneLogTextBox()
        {
            Document.PagePadding = new Thickness(0);
            Document.ColumnWidth = 10000;
            Document.ColumnGap = 0;
            Document.TextAlignment = TextAlignment.Left;
        }

        private void AppendStyledText(string text)
        {
            string normalizedText = text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');

            foreach (string line in normalizedText.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var paragraph = new Paragraph
                {
                    Margin = new Thickness(0, 0.5, 0, 0.5),
                    LineHeight = 19
                };

                Match timestampMatch = TimestampRegex.Match(line);
                string message = line;
                if (timestampMatch.Success)
                {
                    paragraph.Inlines.Add(new Run(timestampMatch.Value)
                    {
                        Foreground = TimestampBrush
                    });
                    message = line.Substring(timestampMatch.Length);
                }

                LogLineStyle style = GetLogLineStyle(message);
                paragraph.Inlines.Add(new Run(message)
                {
                    Foreground = style.Brush,
                    FontWeight = style.IsEmphasized ? FontWeights.SemiBold : FontWeights.Normal
                });
                Document.Blocks.Add(paragraph);
            }
        }

        private static LogLineStyle GetLogLineStyle(string message)
        {
            if (ContainsAny(message, "错误", "失败", "无法", "异常", "拒绝", "error", "failed", "denied"))
            {
                return new LogLineStyle(ErrorBrush, true);
            }

            if (ContainsAny(message, "警告", "请先", "未获取ROOT权限", "未检测到"))
            {
                return new LogLineStyle(WarningBrush, true);
            }

            if (ContainsAny(message, "成功", "完成", "已删除", "已新建", "已授予"))
            {
                return new LogLineStyle(SuccessBrush, true);
            }

            if (ContainsAny(message, "开始", "正在", "检查"))
            {
                return new LogLineStyle(ActionBrush, true);
            }

            if (ContainsAny(message, "源:", "目标:", "路径:", "详情:", "无需打开", "加载根目录"))
            {
                return new LogLineStyle(SecondaryBrush, false);
            }

            if (ContainsAny(message, "信息", "提示"))
            {
                return new LogLineStyle(InfoBrush, false);
            }

            return new LogLineStyle(BodyBrush, false);
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
        }

        private static System.Windows.Media.Brush CreateBrush(byte red, byte green, byte blue)
        {
            var brush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }

        private readonly record struct LogLineStyle(System.Windows.Media.Brush Brush, bool IsEmphasized);
    }

    public class AppPackageItem
    {
        public bool IsSelected { get; set; }
        public string PackageName { get; set; } = "";
        public string AppName { get; set; } = "";
        public string Version { get; set; } = "";
    }

    public class StorageViewModel : INotifyPropertyChanged
    {
        private double _storageUsage;
        private double _memoryUsage;
        private double _totalStorage;
        private double _totalMemory;

        public double StorageUsage
        {
            get => _storageUsage;
            set
            {
                _storageUsage = value;
                OnPropertyChanged(nameof(StorageUsage));
                OnPropertyChanged(nameof(StorageText));
                OnPropertyChanged(nameof(StorageDashArray));
                OnPropertyChanged(nameof(StorageUsedGB));
            }
        }

        public double MemoryUsage
        {
            get => _memoryUsage;
            set
            {
                _memoryUsage = value;
                OnPropertyChanged(nameof(MemoryUsage));
                OnPropertyChanged(nameof(MemoryText));
                OnPropertyChanged(nameof(MemoryDashArray));
                OnPropertyChanged(nameof(MemoryUsedGB));
            }
        }

        public double StorageUsedGB => (_storageUsage / 100) * _totalStorage;
        public double MemoryUsedGB => (_memoryUsage / 100) * _totalMemory;

        public string StorageText => $"{StorageUsedGB:F2}GB/{_totalStorage}GB";
        public string MemoryText => $"{MemoryUsedGB:F0}GB/{_totalMemory:F0}GB";

        private const double CircleCircumference = 471.239;
        public double StrokeThickness => 12;
        private double CircumferenceUnits => CircleCircumference / StrokeThickness;
        public DoubleCollection StorageDashArray => new DoubleCollection { CircumferenceUnits * (_storageUsage / 100), CircumferenceUnits * (1 - _storageUsage / 100) };
        public DoubleCollection MemoryDashArray => new DoubleCollection { CircumferenceUnits * (_memoryUsage / 100), CircumferenceUnits * (1 - _memory_usage_safe) };

        private double _memory_usage_safe => Math.Max(0, Math.Min(100, _memoryUsage));

        public void SetTotalMemory(double gb)
        {
            _totalMemory = gb;
            OnPropertyChanged(nameof(MemoryText));
            OnPropertyChanged(nameof(MemoryUsedGB));
            OnPropertyChanged(nameof(MemoryDashArray));
        }

        public void SetTotalStorage(double gb)
        {
            _totalStorage = gb;
            OnPropertyChanged(nameof(StorageText));
            OnPropertyChanged(nameof(StorageUsedGB));
            OnPropertyChanged(nameof(StorageDashArray));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }


    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private string _CurrentView = "Home";
        public string CurrentView
        {
            get => _CurrentView;
            set
            {
                if (_CurrentView != value)
                {
                    _CurrentView = value;
                    OnPropertyChanged(nameof(CurrentView));
                }
            }
        }
        private StorageViewModel _storageViewModel;
        private DispatcherTimer? _storageTimer;
        public event PropertyChangedEventHandler PropertyChanged;

        private void CopyTextBlockContent(object sender, MouseButtonEventArgs e)
        {
            var tb = sender as TextBlock;
            if (tb == null) return;
            var text = tb.Text;
            if (string.IsNullOrEmpty(text) && tb.Inlines != null)
            {
                text = string.Concat(tb.Inlines.Select(il =>
                {
                    if (il is Run r) return r.Text;
                    if (il is Span s) return string.Concat(s.Inlines.OfType<Run>().Select(r => r.Text));
                    return string.Empty;
                }));
            }
            if (!string.IsNullOrEmpty(text))
            {
                try
                {
                    System.Windows.Clipboard.SetText(text);
                }
                catch
                {
                }
            }
            e.Handled = true;
        }
        private string GetTextFromTextBlock(TextBlock tb)
        {
            if (tb == null) return string.Empty;
            var text = tb.Text;
            if (string.IsNullOrEmpty(text) && tb.Inlines != null)
            {
                text = string.Concat(tb.Inlines.OfType<Run>().Select(r => r.Text));
            }
            return text ?? string.Empty;
        }
        private void SaveDeviceInfoText_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            var serial = GetTextFromTextBlock(DeviceSerialText).Trim();
            if (string.IsNullOrWhiteSpace(serial) || serial == "--") serial = "--";
            foreach (var ch in IOPath.GetInvalidFileNameChars())
            {
                serial = serial.Replace(ch, '_');
            }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                FileName = serial + ".txt"
            };
            var result = dlg.ShowDialog();
            if (result == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine("设备状态: " + GetTextFromTextBlock(DeviceStatusText));
                sb.AppendLine("版本信息: " + GetTextFromTextBlock(VersionInfoText));
                sb.AppendLine("连接类型: " + GetTextFromTextBlock(ConnectionTypeText));
                sb.AppendLine("CPU厂家: " + GetTextFromTextBlock(CpuManufacturerText));
                sb.AppendLine("设备序列号: " + GetTextFromTextBlock(DeviceSerialText));
                sb.AppendLine("设备名称: " + GetTextFromTextBlock(DeviceModelText));
                sb.AppendLine("CPU名称: " + GetTextFromTextBlock(CpuNameText));
                sb.AppendLine("设备代号: " + GetTextFromTextBlock(DeviceCodeText));
                sb.AppendLine("操作系统: " + GetTextFromTextBlock(WindowsVersionText));
                sb.AppendLine("安卓版本: " + GetTextFromTextBlock(AndroidVersionText));
                sb.AppendLine("解锁状态: " + GetTextFromTextBlock(UnlockStatusText));
                sb.AppendLine("A/B分区: " + GetTextFromTextBlock(ABPartitionText));
                sb.AppendLine("内核版本: " + GetTextFromTextBlock(KernelVersionText));
                sb.AppendLine("构建日期: " + GetTextFromTextBlock(BuildDateText));
                sb.AppendLine("CPU代号: " + GetTextFromTextBlock(CpuCodeNameText));
                IOFile.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            }
        }
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void ClearOtherSideMenuItemsSelection(HandyControl.Controls.SideMenuItem selectedItem)
        {
            var allItems = new HandyControl.Controls.SideMenuItem[]
            {
                HomeButton, ScreenMirrorButton, AboutToolButton,
                BasicFlashButton, FastbootVisualizationButton, OugaFlashButton,
                EdlFlashButton, ColorOSAssistantButton, HiddenEnvironmentButton,
                SystemZoneButton, AutorootButton, AppManagementButton,
                AndroidGeneralButton, PayloadButton, BackupAssistantButton,
                DownloadZoneButton, RomDownload, VioletDownload
            };

            foreach (var item in allItems)
            {
                if (item != null && item != selectedItem)
                {
                    item.IsSelected = false;
                }
            }
        }

        private void SideMenu_SelectionChanged(object sender, HandyControl.Data.FunctionEventArgs<object> e)
        {
            // 对于 Freedom 模式，点击已经选中的项也应该触发切换逻辑
            // 另外，对于一级菜单项，我们需要检查它是否被点击，即使用户只是点击了它的标题区域
            if (e.Info is HandyControl.Controls.SideMenuItem item)
            {
                ClearOtherSideMenuItemsSelection(item);

                switch (item.Name)
                {
                    case "HomeButton": HomeButton_Click(this, null); break;
                    case "ScreenMirrorButton": ScreenMirrorButton_Click(this, null); break;
                    case "BasicFlashButton": BasicFlashButton_Click(this, null); break;
                    case "FastbootVisualizationButton": FastbootVisualizationButton_Click(this, null); break;
                    case "HiddenEnvironmentButton": HiddenEnvironmentButton_Click(this, null); break;
                    case "SystemZoneButton": SystemZoneButton_Click(this, null); break;
                    case "OugaFlashButton": OugaFlashButton_Click(this, null); break;
                    case "AutorootButton": AutorootButton_Click(this, null); break;
                    case "AppManagementButton": AppManagementButton_Click(this, null); break;
                    case "AndroidGeneralButton": AndroidGeneralButton_Click(this, null); break;
                    case "DownloadZoneButton": DownloadZoneButton_Click(this, null); break;
                    case "PayloadButton": PayloadButton_Click(this, null); break;
                    case "RomDownload": RomDownloadButton_Click(this, null); break;
                    case "VioletDownload": VioletDownloadButton_Click(this, null); break;
                    case "EdlFlashButton": EdlFlashButton_Click(this, null); break;
                    case "ColorOSAssistantButton": ColorOSAssistantButton_Click(this, null); break;
                    case "BackupAssistantButton": BackupAssistantButton_Click(this, null); break;
                    case "AboutToolButton": AboutToolButton_Click(this, null); break;
                }
            }
        }
        
        // 为了解决需要点击两次的问题，我们添加对一级菜单的直接点击响应
        private void DirectItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is HandyControl.Controls.SideMenuItem item)
            {
                // 先取消其他所有项的选中状态
                ClearOtherSideMenuItemsSelection(item);
                
                // 设置当前项为选中状态，并触发选中事件
                item.IsSelected = true;
                
                // 强制触发对应的视图切换
                switch (item.Name)
                {
                    case "HomeButton": HomeButton_Click(this, null); break;
                    case "ScreenMirrorButton": ScreenMirrorButton_Click(this, null); break;
                    case "AboutToolButton": AboutToolButton_Click(this, null); break;
                    case "BasicFlashButton": BasicFlashButton_Click(this, null); break;
                    case "FastbootVisualizationButton": FastbootVisualizationButton_Click(this, null); break;
                    case "OugaFlashButton": OugaFlashButton_Click(this, null); break;
                    case "EdlFlashButton": EdlFlashButton_Click(this, null); break;
                    case "ColorOSAssistantButton": ColorOSAssistantButton_Click(this, null); break;
                    case "HiddenEnvironmentButton": HiddenEnvironmentButton_Click(this, null); break;
                    case "SystemZoneButton": SystemZoneButton_Click(this, null); break;
                    case "AutorootButton": AutorootButton_Click(this, null); break;
                    case "AppManagementButton": AppManagementButton_Click(this, null); break;
                    case "AndroidGeneralButton": AndroidGeneralButton_Click(this, null); break;
                    case "PayloadButton": PayloadButton_Click(this, null); break;
                    case "BackupAssistantButton": BackupAssistantButton_Click(this, null); break;
                    case "DownloadZoneButton": DownloadZoneButton_Click(this, null); break;
                    case "RomDownload": RomDownloadButton_Click(this, null); break;
                    case "VioletDownload": VioletDownloadButton_Click(this, null); break;
                }
            }
        }

        // 解决左侧导航栏无法使用鼠标滚轮滚动的问题
        private void SideMenuScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                // 将鼠标滚轮的滚动量应用到 ScrollViewer 上
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
                e.Handled = true; // 标记事件已处理，防止事件继续向上传递引发其他问题
            }
        }

        private DispatcherTimer? deviceStatusTimer;
        private bool _isDeviceDetectionEnabled = true;
        private int _deviceDetectionVersion;
        private readonly SemaphoreSlim _deviceDetectionLock = new(1, 1);
        private DispatcherTimer? autoMirrorTimer; // 全自动投屏定时器
        private string lastDeviceStatus = "";
        private string lastConnectionType = "";
        private string lastDeviceSerial = "";
        private string lastDeviceModel = "";
        private string lastDeviceCode = "";
        private string lastAndroidVersion = "";
        private string lastUnlockStatus = "";
        private string lastABPartition = "";
        private string lastSelinuxStatus = "";
        private System.Collections.ObjectModel.ObservableCollection<PartitionInfo> allPartitions;
        private readonly HashSet<PartitionInfo> _partitionSummarySubscriptions = new();
        private string? _parsedXiaomiFlashScriptPath;
        private string[]? _parsedXiaomiFlashScriptLines;
        private IReadOnlyList<string> _parsedRawProgramPaths = Array.Empty<string>();
        private string? _parsedRawProgramDisplayText;
        private XiaomiFlashProgressState? _xiaomiFlashProgressState;

        private sealed class XiaomiFlashProgressItem
        {
            public XiaomiFlashProgressItem(string partitionName, long length)
            {
                PartitionName = partitionName;
                Length = length;
            }

            public string PartitionName { get; }
            public long Length { get; }
        }

        private sealed class XiaomiFlashProgressState
        {
            public XiaomiFlashProgressState(IReadOnlyList<XiaomiFlashProgressItem> items)
            {
                Items = items;
                TotalBytes = items.Sum(item => item.Length);
                Elapsed = Stopwatch.StartNew();
            }

            public IReadOnlyList<XiaomiFlashProgressItem> Items { get; }
            public long TotalBytes { get; }
            public Stopwatch Elapsed { get; }
            public int CurrentItemIndex { get; set; } = -1;
            public long CompletedBytes { get; set; }
            public long CurrentItemTransferredBytes { get; set; }
            public long CompletedChunkBytes { get; set; }
            public long CurrentChunkExpectedBytes { get; set; }
            public long CurrentChunkReportedBytes { get; set; }
            public long LastReportedBytes { get; set; }
            public bool CurrentCommandFinished { get; set; }
            public string TransferRate { get; set; } = "0MB/s";
        }

        private enum XiaomiFlashMode
        {
            Traditional,
            SlotA
        }
        private string lastKernelVersion = "";
        private string lastBuildDate = "";
        private string lastCpuManufacturer = "";
        private string lastCpuCodeName = "";
        private string lastWindowsVersion = "";
        private Process? scrcpyProcess = null; // 跟踪scrcpy进程
        private bool isScrcpyStarting = false; // 防止启动过程中重复创建scrcpy进程
        private Window? scrcpyControlBarWindow = null;
        private DispatcherTimer? scrcpyControlBarTimer = null;
        private IntPtr scrcpyMainWindowHandle = IntPtr.Zero;
        private IntPtr scrcpyLocationChangeHook = IntPtr.Zero;
        private WinEventDelegate? scrcpyLocationChangeProc = null;
        private readonly ConcurrentQueue<string> screenMirrorLogQueue = new();
        private DispatcherTimer? screenMirrorLogFlushTimer = null;
        private int screenMirrorLogPumpActive = 0;
        private const int ScreenMirrorLogMaxCharacters = 1_000;
        private const int ScreenMirrorLogMaxBatchCharacters = 4_096;
        private readonly List<string> _mirrorTitleSelectionOrder = new();
        private bool isAutoMirrorEnabled = false; // 全自动投屏开关
        private string currentView = "Home"; 
        private string lastLoadedDirectory = "/sdcard/"; // 存储上次加载的手机目录路径
        private string lastSelectedFileName = ""; // 存储用户最后选中的文件名
        private string currentPath = "/sdcard/"; // 存储当前文件夹路径
        private readonly SemaphoreSlim _systemZoneTransferLock = new(1, 1);
        private FileItem? _systemZoneContextMenuItem;
        private bool _hasAutoLoadedSystemZoneDirectory;
        private CancellationTokenSource? _systemZoneImagePreviewCancellation;
        private string currentFlashingPartition = ""; // 存储当前正在刷入的分区名称
        private StringBuilder fastbootCompleteLog = new StringBuilder(); // 存储所有fastboot命令的完整输出
        private ObservableCollection<AppPackageItem> AppPackages = new ObservableCollection<AppPackageItem>();
        private string? magiskApkPath;
        private string magiskbootPath = string.Empty;
        private string tempDir = string.Empty;
        private const string DriverListSourceUrl = "https://gitee.com/smartpaocai/smart-tool/blob/master/download/qudong.json";
        private const string RootManagerListSourceUrl = "https://gitee.com/smartpaocai/smart-tool/blob/master/download/rootmanager.json";
        private const string Kernel4ListSourceUrl = "https://gitee.com/smartpaocai/smart-tool/blob/master/download/kernel4.json";
        private const string OnePlusAk3ListSourceUrl = "https://gitee.com/smartpaocai/smart-tool/blob/master/download/oneplusak3";
        private const string OujiaAk3ListSourceUrl = "https://gitee.com/smartpaocai/smart-tool/blob/master/download/oujia.json";
        private const string AndroidAk3ListSourceUrl = "https://gitee.com/smartpaocai/smart-tool/blob/master/download/android.json";
        private const string UtilitySoftwareListSourceUrl = "https://gitee.com/smartpaocai/smart-tool/blob/master/download/apk.json";
        private const string UserUploadListSourceUrl = "https://violettool.top/public.json";
        private static readonly HttpClient DriverListHttpClient = new HttpClient();
        private bool _isLoadingDriverList;
        private bool _isLoadingRootManagerList;
        private bool _isLoadingKernel4List;
        private bool _isLoadingOnePlusAk3List;
        private bool _isLoadingOujiaAk3List;
        private bool _isLoadingAndroidAk3List;
        private bool _isLoadingUtilitySoftwareList;
        private bool _isLoadingUserUploadList;

        // 设备序列号集合
        private ObservableCollection<string> deviceSerials = new ObservableCollection<string>();
        public ObservableCollection<string> DeviceSerials
        {
            get { return deviceSerials; }
            set
            {
                deviceSerials = value;
                OnPropertyChanged(nameof(DeviceSerials));
            }
        }

        private async Task<bool> ExecuteFastbootCommandFromScriptWithCustomLog(
            string commandLine,
            string scriptPath,
            string fastbootPath,
            System.Windows.Controls.RichTextBox logTextBox,
            string partitionName,
            string? sourceFileName = null,
            bool trackDetailedPartitionStatus = true,
            bool applyScriptVerificationOptions = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (applyScriptVerificationOptions)
                {
                    commandLine = ApplyFastbootVerificationOptions(commandLine, partitionName);
                }
                string processedCommand = commandLine.Replace("fastboot %*", "fastboot")
                                                    .Replace("%~dp0images/", Path.Combine(scriptPath, "images") + Path.DirectorySeparatorChar)
                                                    .Replace("%~dp0images\\", Path.Combine(scriptPath, "images") + Path.DirectorySeparatorChar);
           
                string[] parts = processedCommand.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    return false;
                

                List<string> args = new List<string>();
                for (int i = 1; i < parts.Length; i++)
                {
                    if (parts[i] == "||") break;
                    args.Add(parts[i]);
                }
                
                if (args.Count == 0) return false;
                
                string command = args[0];
                string selectedSerial = GetSelectedDeviceSerial();
                
                // 多设备处理逻辑
                string finalArguments = string.Join(" ", args);
                if (!string.IsNullOrEmpty(selectedSerial))
                {
                    finalArguments = $"-s {selectedSerial} {finalArguments}";
                }
                
                var processInfo = new ProcessStartInfo
                {
                    FileName = fastbootPath,
                    Arguments = finalArguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                bool isSending = false;
                bool isWriting = false;
                
                using (var process = Process.Start(processInfo))
                {
                    if (process == null)
                    {
                        LogToFastboot("无法启动 fastboot 进程", "Red");
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
                            fastbootCompleteLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] [FASTBOOT OUTPUT] {e.Data}");
                            Dispatcher.Invoke(() =>
                            {
                                ParseFastbootProgress(e.Data);
                                if (trackDetailedPartitionStatus)
                                {
                                    ParseAndLogCustomStatus(e.Data, partitionName, ref isSending, ref isWriting, sourceFileName);
                                }
                            });
                        }
                    };
                        
                    // 实时读取标准错误
                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            errorBuilder.AppendLine(e.Data);
                            fastbootCompleteLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] [FASTBOOT ERROR] {e.Data}");
                            Dispatcher.Invoke(() =>
                            {
                                ParseFastbootProgress(e.Data);
                                if (trackDetailedPartitionStatus)
                                {
                                    ParseAndLogCustomStatus(e.Data, partitionName, ref isSending, ref isWriting, sourceFileName);
                                }
                            });
                        }
                    };
                        
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                        
                    try
                    {
                        await process.WaitForExitAsync(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill(entireProcessTree: true);
                            }
                        }
                        catch
                        {
                        }
                        throw;
                    }
                        
                    string error = errorBuilder.ToString();
                        
                    if (process.ExitCode != 0)
                    {
                        LogFastbootNativeError(error, process.ExitCode);
                        return false;
                    }

                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogToFastboot($"执行fastboot命令失败: {ex.Message}", "Red");
                return false;
            }
        }

        private string ApplyFastbootVerificationOptions(string commandLine, string partitionName)
        {
            if (DisableDmVerityCheckBox?.IsChecked != true ||
                !partitionName.StartsWith("vbmeta", StringComparison.OrdinalIgnoreCase))
            {
                return commandLine;
            }

            return Regex.Replace(
                commandLine,
                @"^\s*fastboot(?:\s+%\*)?",
                "fastboot --disable-verity --disable-verification",
                RegexOptions.IgnoreCase);
        }

        // 处理分区名称，移除_ab后缀以显示实际分区名称
        private string GetDisplayPartitionName(string partitionName)
        {
            if (string.IsNullOrEmpty(partitionName))
                return partitionName;
            if (partitionName.EndsWith("_ab", StringComparison.OrdinalIgnoreCase))
            {
                return partitionName.Substring(0, partitionName.Length - 3);
            }
            
            return partitionName;
        }

        private string GetBasePartitionName(string partitionName)
        {
            if (string.IsNullOrEmpty(partitionName)) return partitionName;
            if (partitionName.EndsWith("_a", StringComparison.OrdinalIgnoreCase) || partitionName.EndsWith("_b", StringComparison.OrdinalIgnoreCase))
            {
                return partitionName.Substring(0, partitionName.Length - 2);
            }
            if (partitionName.EndsWith("_ab", StringComparison.OrdinalIgnoreCase))
            {
                return partitionName.Substring(0, partitionName.Length - 3);
            }
            return partitionName;
        }

        private void ParseAndLogCustomStatus(string output, string partitionName, ref bool isSending, ref bool isWriting, string? sourceFileName = null)
        {
            if (string.IsNullOrEmpty(output)) return;
            
            string lowerOutput = output.ToLower();
            
            // 检测Sending命令开始
            if (lowerOutput.Contains("sending") && lowerOutput.Contains("kb"))
            {
                if (!isSending)
                {
                    string sourceDisplayName = GetFastbootWriteSourceDisplayName(sourceFileName, partitionName);

                    Dispatcher.Invoke(() =>
                    {
                        if (!string.IsNullOrWhiteSpace(partitionName) &&
                            partitionName.EndsWith("_ab", StringComparison.OrdinalIgnoreCase))
                        {
                            string basePart = GetBasePartitionName(partitionName);
                            string partA = $"{basePart}_a";
                            string partB = $"{basePart}_b";

                            foreach (string dest in new[] { partA, partB })
                            {
                                BeginFastbootPendingStep(_pendingFastbootFlashParagraphs, dest, BuildFastbootWriteStepTitle(sourceDisplayName, dest));
                            }
                        }
                        else
                        {
                            string destPartition = partitionName ?? "--";
                            BeginFastbootPendingStep(_pendingFastbootFlashParagraphs, destPartition, BuildFastbootWriteStepTitle(sourceDisplayName, destPartition));
                        }

                        FastbootLogTextBox.ScrollToEnd();
                    });
                    isSending = true;
                }
                return;
            }
            // 检测Writing命令开始
            if (lowerOutput.Contains("writing"))
            {
                if (!isWriting)
                {
                    isWriting = true;
                }
                return;
            }
            
            // 检测分区是否刷入成功
            if (lowerOutput.Contains("flashed successfully"))
            {
                // 提取具体的分区名称
                var match = System.Text.RegularExpressions.Regex.Match(output, @"Partition\s+([^\s]+)\s+flashed successfully", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string flashedPartition = match.Groups[1].Value;
                    Dispatcher.Invoke(() =>
                    {
                        string sourceDisplayName = GetFastbootWriteSourceDisplayName(sourceFileName, partitionName);
                        EndFastbootPendingStepOk(_pendingFastbootFlashParagraphs, flashedPartition, BuildFastbootWriteStepTitle(sourceDisplayName, flashedPartition));
                    });
                }
                return;
            }
            
            // 检测完成信息
            if (lowerOutput.Contains("finished") && lowerOutput.Contains("total time"))
            {
                Dispatcher.Invoke(() =>
                {
                    CompleteFastbootPendingSteps(_pendingFastbootFlashParagraphs, true);
                });
                return;
            }
            
            // 检测错误信息
            if (lowerOutput.Contains("failed") || (lowerOutput.Contains("error") && !lowerOutput.Contains("fastboot error")))
            {
                Dispatcher.Invoke(() =>
                {
                    CompleteFastbootPendingSteps(_pendingFastbootFlashParagraphs, false);
                });
                LogToFastboot($"错误: {output}", "Red");
                return;
            }
        }

        // 获取当前选中的设备序列号
        private string GetSelectedDeviceSerial()
        {
            return Dispatcher.Invoke(() =>
            {
                if (MultiDeviceComboBox.SelectedItem != null)
                {
                    string selectedText = MultiDeviceComboBox.SelectedItem.ToString();
                    if (selectedText.Contains(" ("))
                    {
                        return selectedText.Substring(0, selectedText.IndexOf(" ("));
                    }
                    return selectedText;
                }
                return "";
            });
        }

        private bool isAllPartitionsSelected;
        public bool IsAllPartitionsSelected
        {
            get { return isAllPartitionsSelected; }
            set
            {
                if (isAllPartitionsSelected != value)
                {
                    isAllPartitionsSelected = value;
                    OnPropertyChanged(nameof(IsAllPartitionsSelected));
                }
            }
        }

        public ICommand ToggleAllPartitionsCommand { get; }

        public MainWindow()
        {
            // 初始化所有分区集合
            allPartitions = new ObservableCollection<PartitionInfo>();
            InitializeComponent();
            allPartitions.CollectionChanged += AllPartitions_CollectionChanged;
            UpdatePartitionSelectionSummary();
            InitializeAutoRootModeUiState();
            UpdateAvbModeFileInputs();
            InitializeVioletDownload();
            
            // 屏幕适配逻辑
            double screenHeight = SystemParameters.WorkArea.Height;
            double screenWidth = SystemParameters.WorkArea.Width;
            
            // 目标设计尺寸
            double targetWidth = 1000;
            double targetHeight = 800;

            // 如果屏幕空间不足（高度不足800或宽度不足1000），则按比例缩小窗口
            if (screenHeight < targetHeight || screenWidth < targetWidth)
            {
                // 计算需要的缩放比例，取较小值以确保完全放入屏幕
                // 留出一点边距（95%）
                double ratioH = screenHeight / targetHeight;
                double ratioW = screenWidth / targetWidth;
                double ratio = Math.Min(ratioH, ratioW) * 0.95;

                this.Width = targetWidth * ratio;
                this.Height = targetHeight * ratio;
            }
            else
            {
                // 屏幕够大，使用标准尺寸
                this.Width = targetWidth;
                this.Height = targetHeight;
            }
            
            // 确保窗口居中
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            
            // 异步增加打开次数
            _ = TryIncrementOpenCountAsync();
            
            InitializeDeviceStatusMonitoring();
            ToggleAllPartitionsCommand = new RelayCommand(ToggleAllPartitions);
            
            // 设置分区表容器
            PartitionTableDataGrid.ItemsSource = allPartitions;
            MultiDeviceComboBox.ItemsSource = DeviceSerials;
            var appListDataGrid = this.FindName("AppListDataGrid") as DataGrid;
            if (appListDataGrid != null)
            {
                appListDataGrid.ItemsSource = AppPackages;
            }
            
            allPartitions.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (PartitionInfo item in e.NewItems)
                    {
                        item.PropertyChanged += (sender, args) =>
                        {
                            if (args.PropertyName == nameof(PartitionInfo.IsSelected))
                            {
                                UpdateSelectAllState();
                            }
                        };
                    }
                }
                UpdateSelectAllState();
            };
            InitializeAutorootPaths();

            // 初始化时显示首页视图，隐藏其他视图
            var homeView = this.FindName("HomeView") as Grid;
            var screenMirrorView = this.FindName("ScreenMirrorView") as Grid;
            var basicFlashView = this.FindName("BasicFlashView") as Grid;
            var fastbootVisualizationView = this.FindName("FastbootVisualizationView") as Grid;
            var hiddenEnvironmentView = this.FindName("HiddenEnvironmentView") as Grid;
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

            if (homeView != null) homeView.Visibility = Visibility.Visible;
            if (screenMirrorView != null) screenMirrorView.Visibility = Visibility.Collapsed;
            if (basicFlashView != null) basicFlashView.Visibility = Visibility.Collapsed;
            if (fastbootVisualizationView != null) fastbootVisualizationView.Visibility = Visibility.Collapsed;
            if (hiddenEnvironmentView != null) hiddenEnvironmentView.Visibility = Visibility.Collapsed;
            if (aboutToolView != null) aboutToolView.Visibility = Visibility.Collapsed;
            if (systemZoneView != null) systemZoneView.Visibility = Visibility.Collapsed;
            if (oujiaFlashView != null) oujiaFlashView.Visibility = Visibility.Collapsed;
            if (autorootView != null) autorootView.Visibility = Visibility.Collapsed;
            if (appManagementView != null) appManagementView.Visibility = Visibility.Collapsed;
            if (androidGeneralView != null) androidGeneralView.Visibility = Visibility.Collapsed;
            if (payloadView != null) payloadView.Visibility = Visibility.Collapsed;
            if (romDownloadView != null) romDownloadView.Visibility = Visibility.Collapsed;
            if (edlFlashView != null) edlFlashView.Visibility = Visibility.Collapsed;
            if (colorOSAssistantView != null) colorOSAssistantView.Visibility = Visibility.Collapsed;
            if (backupAssistantView != null) backupAssistantView.Visibility = Visibility.Collapsed;
            if (violetDownloadView != null) violetDownloadView.Visibility = Visibility.Collapsed;
            
            UpdateButtonStates("Home");
            // 自动启动更新程序
            StartUpdateProgram();
            
            
            _storageViewModel = new StorageViewModel();
            var storageBorder = this.FindName("StorageMemoryBorder") as Border;
            if (storageBorder != null)
            {
                storageBorder.DataContext = _storageViewModel;
            }
            _storageTimer = new DispatcherTimer();
            _storageTimer.Interval = TimeSpan.FromSeconds(10);
            _storageTimer.Tick += async (s, e) => await RefreshStorageMemoryAsync(true);
            _storageTimer.Start();
            this.Loaded += async (s, e) => await RefreshStorageMemoryAsync();
            this.Loaded += async (s, e) => await InitializeBroadcastNoticesAsync();
            this.Loaded += MainWindow_Loaded;
            this.Activated += (s, e) => ClearScrcpyWindowTopMost();
            this.LocationChanged += (s, e) => UpdateScrcpyControlBarPosition();
            this.SizeChanged += (s, e) => UpdateScrcpyControlBarPosition();
            this.StateChanged += (s, e) =>
            {
                ClearScrcpyWindowTopMost();
                UpdateScrcpyControlBarPosition();
            };

            InitializeLanguageUi();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var homeItem = this.FindName("HomeButton") as HandyControl.Controls.SideMenuItem;
            
            // 显示加载遮罩层
            if (this.FindName("LoadingOverlay") is Grid loadingOverlay && this.FindName("LoadingContent") is Grid loadingContent)
            {
                loadingOverlay.Visibility = Visibility.Visible;
                loadingContent.Visibility = Visibility.Visible;
            }

            // 解决 HandyControl 的异步加载延迟问题
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                // 等待 UI 完全稳定
                await Task.Delay(200);

                if (this.FindName("FlashFeatureGroup") is HandyControl.Controls.SideMenuItem flashItem)
                {
                    // 设置焦点并模拟鼠标左键点击
                    flashItem.Focus();
                    flashItem.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
                    flashItem.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
                }
                
                await Task.Delay(150); // 稍微加长等待动画开始的时间
                
                // 将焦点还给主页
                if (homeItem != null)
                {
                    // 模拟点击主页，确保真正触发 SelectionChanged 逻辑
                    homeItem.Focus();
                    homeItem.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
                    homeItem.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
                    homeItem.IsSelected = true;
                }
                
                // 再等一小会儿确保主页渲染完成
                await Task.Delay(100);

                // 隐藏加载遮罩层
                if (this.FindName("LoadingOverlay") is Grid overlay && this.FindName("LoadingContent") is Grid content)
                {
                    overlay.Visibility = Visibility.Collapsed;
                    content.Visibility = Visibility.Collapsed;
                }
                
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private static long ParseMemField(string text, string key)
        {
            var m = Regex.Match(text, key + @"\s*:\s*(\d+)\s*kB", RegexOptions.Multiline);
            return m.Success ? long.Parse(m.Groups[1].Value) : 0;
        }

        private async Task RefreshStorageMemoryAsync(bool silent = false)
        {
            int detectionVersion = _deviceDetectionVersion;
            if (!IsDeviceDetectionCycleCurrent(detectionVersion))
            {
                return;
            }

            try
            {
                string adbPath = GetToolPath("adb.exe");
                if (string.IsNullOrEmpty(adbPath))
                {
                    if (!silent) ShowMessage("未找到adb.exe");
                    return;
                }

                string selectedSerial = GetSelectedDeviceSerial();
                string serialArg = string.IsNullOrEmpty(selectedSerial) ? string.Empty : $"-s {selectedSerial} ";

                var memTask = GetCommandOutput(adbPath, serialArg + "shell cat /proc/meminfo");
                var dfhTask = GetCommandOutput(adbPath, serialArg + "shell df -h");
                var mem = await memTask;
                var dfh = await dfhTask;

                if (!IsDeviceDetectionCycleCurrent(detectionVersion))
                {
                    return;
                }

                long totalKb = ParseMemField(mem, "MemTotal");
                long availKb = ParseMemField(mem, "MemAvailable");
                if (availKb <= 0)
                {
                    long freeKb = ParseMemField(mem, "MemFree");
                    long cachedKb = ParseMemField(mem, "Cached");
                    availKb = freeKb + cachedKb;
                }
                double memTotalGb = totalKb > 0 ? totalKb / 1024.0 / 1024.0 : 0;
                double memUsedPct = (totalKb > 0) ? 100.0 - (availKb * 1.0 / totalKb) * 100.0 : 0;

                string targetLine = null;
                foreach (var line in dfh.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var lt = line.Trim();
                    if (lt.EndsWith("/storage/emulated") || lt.Contains(" /storage/emulated "))
                    {
                        targetLine = lt;
                        break;
                    }
                }
                if (targetLine == null)
                {
                    foreach (var line in dfh.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var lt = line.Trim();
                        if (lt.EndsWith("/data") || lt.Contains(" /data "))
                        {
                            targetLine = lt;
                            break;
                        }
                    }
                }
                if (targetLine == null)
                {
                    foreach (var line in dfh.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var lt = line.Trim();
                        if (lt.Contains("emulated"))
                        {
                            targetLine = lt;
                            break;
                        }
                    }
                }
                double storageTotalGb = 0;
                double storageUsedPct = 0;
                if (!string.IsNullOrEmpty(targetLine))
                {
                    var tokens = Regex.Split(targetLine, "\\s+");
                    if (tokens.Length >= 6)
                    {
                        var n = tokens.Length;
                        var usePctToken = tokens[n - 2];
                        var usedToken = tokens[n - 4];
                        var sizeToken = tokens[n - 5];

                        double ParseHumanGb(string t)
                        {
                            t = t.Trim();
                            if (string.IsNullOrEmpty(t)) return 0;
                            var m = Regex.Match(t, "^([0-9]+(?:\\.[0-9]+)?)\\s*([KMGT])?", RegexOptions.IgnoreCase);
                            if (!m.Success) return 0;
                            var val = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                            var unit = m.Groups[2].Success ? m.Groups[2].Value.ToUpperInvariant() : "";
                            return unit switch
                            {
                                "T" => val * 1024.0,
                                "G" => val,
                                "M" => val / 1024.0,
                                "K" => val / (1024.0 * 1024.0),
                                _ => val
                            };
                        }

                        storageTotalGb = ParseHumanGb(sizeToken);
                        var usedGb = ParseHumanGb(usedToken);
                        var pctMatch = Regex.Match(usePctToken, @"^([0-9]+)\%$");
                        if (pctMatch.Success)
                        {
                            storageUsedPct = double.Parse(pctMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                        }
                        else if (storageTotalGb > 0)
                        {
                            storageUsedPct = usedGb / storageTotalGb * 100.0;
                        }
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    if (!IsDeviceDetectionCycleCurrent(detectionVersion))
                    {
                        return;
                    }

                    if (memTotalGb > 0)
                    {
                        _storageViewModel.SetTotalMemory(memTotalGb);
                        _storageViewModel.MemoryUsage = Math.Max(0, Math.Min(100, memUsedPct));
                    }
                    if (storageTotalGb > 0)
                    {
                        _storageViewModel.SetTotalStorage(storageTotalGb);
                        _storageViewModel.StorageUsage = Math.Max(0, Math.Min(100, storageUsedPct));
                    }
                });
            }
            catch
            {
            }
        }

        private void ToggleAllPartitions(object parameter)
        {
            bool selectAll = parameter is bool ? (bool)parameter : IsAllPartitionsSelected;
            bool adbMode = IsFastbootVisualizationAdbMode();
            foreach (var partition in allPartitions)
            {
                partition.IsSelected = selectAll &&
                                       !IsFastbootVisualizationPartitionProtected(partition.PartitionName, adbMode);
            }
            if (PartitionTableDataGrid.ItemsSource != null)
            {
                PartitionTableDataGrid.Items.Refresh();
            }
        }

        private void UpdateSelectAllState()
        {
            bool adbMode = IsFastbootVisualizationAdbMode();
            List<PartitionInfo> selectablePartitions = allPartitions
                .Where(partition =>
                    !IsFastbootVisualizationPartitionProtected(partition.PartitionName, adbMode))
                .ToList();
            IsAllPartitionsSelected = selectablePartitions.Any() &&
                                      selectablePartitions.All(partition => partition.IsSelected);
        }

        private void AllPartitions_CollectionChanged(
            object? sender,
            System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                foreach (PartitionInfo partition in _partitionSummarySubscriptions.ToArray())
                {
                    partition.PropertyChanged -= PartitionSummary_PropertyChanged;
                }
                _partitionSummarySubscriptions.Clear();

                foreach (PartitionInfo partition in allPartitions)
                {
                    SubscribePartitionSummary(partition);
                }
            }
            else
            {
                if (e.OldItems != null)
                {
                    foreach (PartitionInfo partition in e.OldItems.OfType<PartitionInfo>())
                    {
                        partition.PropertyChanged -= PartitionSummary_PropertyChanged;
                        _partitionSummarySubscriptions.Remove(partition);
                    }
                }

                if (e.NewItems != null)
                {
                    foreach (PartitionInfo partition in e.NewItems.OfType<PartitionInfo>())
                    {
                        SubscribePartitionSummary(partition);
                    }
                }
            }

            UpdatePartitionSelectionSummary();
        }

        private void SubscribePartitionSummary(PartitionInfo partition)
        {
            if (_partitionSummarySubscriptions.Add(partition))
            {
                partition.PropertyChanged += PartitionSummary_PropertyChanged;
            }
        }

        private void PartitionSummary_PropertyChanged(
            object? sender,
            System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PartitionInfo.IsSelected) ||
                e.PropertyName == nameof(PartitionInfo.PartitionSize) ||
                e.PropertyName == nameof(PartitionInfo.FilePath))
            {
                UpdatePartitionSelectionSummary();
            }
        }

        private void UpdatePartitionSelectionSummary()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(UpdatePartitionSelectionSummary);
                return;
            }

            if (PartitionSelectionSummaryTextBlock == null || allPartitions == null)
            {
                return;
            }

            List<PartitionInfo> selectedPartitions = allPartitions
                .Where(partition => partition.IsSelected)
                .ToList();
            long totalBytes = 0;
            bool allSizesKnown = true;

            foreach (PartitionInfo partition in selectedPartitions)
            {
                long partitionBytes = 0;
                bool sizeKnown = false;

                if (!string.IsNullOrWhiteSpace(partition.FilePath) && File.Exists(partition.FilePath))
                {
                    try
                    {
                        partitionBytes = new FileInfo(partition.FilePath).Length;
                        sizeKnown = partitionBytes > 0;
                    }
                    catch
                    {
                    }
                }

                if (!sizeKnown &&
                    !string.IsNullOrWhiteSpace(partition.PartitionSize) &&
                    partition.PartitionSize != "--")
                {
                    partitionBytes = ParsePartitionSizeToBytes(partition.PartitionSize);
                    sizeKnown = partitionBytes > 0;
                }

                if (!sizeKnown)
                {
                    allSizesKnown = false;
                    continue;
                }

                totalBytes += partitionBytes;
            }

            string totalSizeText = selectedPartitions.Count == 0
                ? "0 B"
                : allSizesKnown
                    ? FormatByteSize(totalBytes)
                    : "--";
            PartitionSelectionSummaryTextBlock.Text =
                $"已选 {selectedPartitions.Count}/{allPartitions.Count} · {totalSizeText}";
            PartitionSelectionSummaryTextBlock.Tag =
                $"已选择 {selectedPartitions.Count} / {allPartitions.Count}  |  总计 {totalSizeText}";
        }

        // 强力线刷复选框选中事件处理器
        private void NavigateToUrl(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is TextBlock textBlock && textBlock.Tag is string url)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"打开链接失败: {ex.Message}");
            }
        }
        
        // 启动update.exe更新程序的方法
        private void StartUpdateProgram()
        {
            try
            {
                string updateExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "exe", "update.exe");
                
                if (File.Exists(updateExePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = updateExePath,
                        UseShellExecute = true
                    });
                }
                else
                {
                }
            }
            catch (Exception ex)
            {
            }
        }
        
        // 加载提取的分区文件到DataGrid的方法
        private string FormatFileSize(long bytes)
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

        private async Task ErasePartitionsUsingAdb(
            List<string> selectedPartitions,
            System.Windows.Controls.RichTextBox logTextBox,
            CancellationToken cancellationToken)
        {
            string adbPath = GetAdbPath();
            if (string.IsNullOrEmpty(adbPath))
            {
                LogToFastboot("缺少adb.exe", "Red");
                return;
            }
            
            // 检查root权限
            string suResult = await ExecuteAdbCommandWithOutput(
                "shell su -c \"echo success\"",
                cancellationToken);
            if (!suResult.Contains("success"))
            {
                LogToFastboot("系统模式下擦除分区需ROOT,无法获取权限...", "Red");
                return;
            }
            
            LogToFastbootStyled(
                ("准备擦除 ", "Black", false),
                ($"{selectedPartitions.Count}", "Purple", true),
                (" 个分区", "Black", false));

            var eraseStopwatch = Stopwatch.StartNew();
            int failedPartitionCount = 0;
            
            foreach (var partitionName in selectedPartitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LogFastbootEraseBegin(partitionName);
                
                string partitionPath = $"/dev/block/bootdevice/by-name/{partitionName}";
                
                // 使用dd命令将零数据写入分区来擦除
                string eraseCommand = $"shell su -c \"dd if=/dev/zero of={partitionPath} bs=4096 count=1024\"";
                string result_erase = await ExecuteAdbCommandWithOutput(
                    eraseCommand,
                    CancellationToken.None);
                
                if (result_erase.Contains("error") || result_erase.Contains("failed") || result_erase.Contains("not found"))
                {
                    failedPartitionCount++;
                    LogFastbootEraseEndFail(partitionName);
                    if (!string.IsNullOrWhiteSpace(result_erase))
                    {
                        LogToFastboot(result_erase.Trim(), "Red");
                    }
                }
                else
                {
                    LogFastbootEraseEndOk(partitionName);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            long elapsedSeconds = Math.Max(0, (long)Math.Ceiling(eraseStopwatch.Elapsed.TotalSeconds));
            LogToFastboot(
                $"擦除完成，擦除失败分区{failedPartitionCount}个，耗时{elapsedSeconds}秒",
                failedPartitionCount == 0 ? "Green" : "Orange");
        }
        
        private async Task ErasePartitionsUsingFastboot(
            List<string> selectedPartitions,
            System.Windows.Controls.RichTextBox logTextBox,
            CancellationToken cancellationToken)
        {
            try
            {
                string fastbootPath = GetFastbootPath();
                if (string.IsNullOrEmpty(fastbootPath))
                {
                    LogToFastboot("缺少fastboot.exe", "Red");
                    return;
                }
                
                LogToFastbootStyled(
                    ("准备擦除 ", "Black", false),
                    ($"{selectedPartitions.Count}", "Purple", true),
                    (" 个分区", "Black", false));

                var eraseStopwatch = Stopwatch.StartNew();
                int failedPartitionCount = 0;
                
                // 逐个擦除选中的分区
                foreach (var partitionName in selectedPartitions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    LogFastbootEraseBegin(partitionName);
                    
                    string eraseCommand = $"erase {partitionName}";
                    string result_erase = await ExecuteFastbootCommand(
                        fastbootPath,
                        eraseCommand,
                        cancellationToken: CancellationToken.None);
                    
                    if (result_erase.StartsWith("ERROR_DETECTED") ||
                        result_erase.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ||
                        result_erase.Contains("error", StringComparison.OrdinalIgnoreCase))
                    {
                        failedPartitionCount++;
                        LogFastbootEraseEndFail(partitionName);
                        if (!string.IsNullOrWhiteSpace(result_erase))
                        {
                            LogToFastboot(result_erase.Trim(), "Red");
                        }
                    }
                    else
                    {
                        LogFastbootEraseEndOk(partitionName);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                long elapsedSeconds = Math.Max(0, (long)Math.Ceiling(eraseStopwatch.Elapsed.TotalSeconds));
                LogToFastboot(
                    $"擦除完成，擦除失败分区{failedPartitionCount}个，耗时{elapsedSeconds}秒",
                    failedPartitionCount == 0 ? "Green" : "Orange");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogToFastboot($"擦除分区失败: {ex.Message}", "Red");
            }
        }

        private void InitializeDeviceStatusMonitoring()
        {
            _isDeviceDetectionEnabled = true;
            unchecked
            {
                _deviceDetectionVersion++;
            }

            // 初始化设备状态监控定时器
            deviceStatusTimer = new DispatcherTimer            {
                Interval = TimeSpan.FromSeconds(3) // 每3秒检测一次
            };
            deviceStatusTimer.Tick += async (object? s, EventArgs e) => await CheckDeviceStatus();
            deviceStatusTimer.Start();
            
            _ = CheckDeviceStatus();
        }

        private bool IsDeviceDetectionCycleCurrent(int detectionVersion)
        {
            return _isDeviceDetectionEnabled && detectionVersion == _deviceDetectionVersion;
        }

        private async Task RefreshDeviceStatusImmediatelyAsync(bool terminateRunningTools)
        {
            bool detectionEnabled = _isDeviceDetectionEnabled;
            if (detectionEnabled)
            {
                // 立即使正在执行的旧检测失效，防止 Fastboot 阶段的结果在设备开机后回写主页。
                unchecked
                {
                    _deviceDetectionVersion++;
                }
            }

            if (terminateRunningTools)
            {
                await KillAllAdbAndFastbootProcesses();
            }

            if (!detectionEnabled || !_isDeviceDetectionEnabled)
            {
                return;
            }

            // 等待旧检测退出后直接执行一轮新检测，不依赖下一个三秒定时器周期。
            await CheckDeviceStatus(waitForCurrentDetection: true);
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private async void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // 停止设备状态监控定时器
            deviceStatusTimer?.Stop();
            deviceStatusTimer = null;
            
            // 停止自动投屏定时器（如果存在）
            StopAutoMirrorTimer();
            
            // 先隐藏主窗口
            this.Hide();
            
            // 创建并显示独立的清理对话框
            await ShowCleanupDialogAndExit();
        }

        private async Task ShowCleanupDialogAndExit()
        {
            Window? cleanupDialog = null;
            
            try
            {
                // 创建独立的清理对话框
                cleanupDialog = new Window
                {
                    Title = "感谢使用紫罗兰工具箱",
                    Width = 300,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStyle = WindowStyle.ToolWindow,
                    Topmost = true,
                    ShowInTaskbar = false,
                    Content = new StackPanel
                    {
                        Margin = new Thickness(20),
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "正在清理工具痕迹，请稍等...",
                                FontSize = 14,
                                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                                Margin = new Thickness(0, 20, 0, 20)
                            },
                            new System.Windows.Controls.ProgressBar
                            {
                                IsIndeterminate = true,
                                Height = 20,
                                Margin = new Thickness(0, 10, 0, 0)
                            }
                        }
                    }
                };

                cleanupDialog.Show();
                
                // 强制刷新UI，确保对话框显示
                await Task.Delay(200);
                
                // 终止所有 adb.exe、fastboot.exe 和 scrcpy.exe 进程
                await KillAllAdbAndFastbootProcesses();
                
                // 终止所有 scrcpy 相关进程
                string[] scrcpyCommands = {
                    "/f /im scrcpy.exe",
                    "/f /im scrcpy-server.exe",
                    "/f /im scrcpy*"
                };
                
                foreach (string args in scrcpyCommands)
                {
                    try
                    {
                        ProcessStartInfo taskKillInfo = new ProcessStartInfo
                        {
                            FileName = "taskkill",
                            Arguments = args,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        
                        using (Process? taskKillProcess = Process.Start(taskKillInfo))
                        {
                            taskKillProcess?.WaitForExit(3000); // 等待最多3秒
                        }
                    }
                    catch
                    {
                        // 忽略错误，继续执行
                    }
                }
                
                // 额外等待确保进程完全终止
                await Task.Delay(1000);
                
                // 删除桌面上的Smart Tool Download文件夹
                string downloadPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Smart Tool Download");
                if (System.IO.Directory.Exists(downloadPath))
                {
                    System.IO.Directory.Delete(downloadPath, true);
                }
                
                // 短暂延迟以确保用户能看到清理过程
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                // 如果删除失败，记录错误但不阻止程序关闭
                System.Diagnostics.Debug.WriteLine($"关闭时清理失败: {ex.Message}");
            }
            finally
            {
                // 关闭清理对话框
                cleanupDialog?.Close();
                
                // 确保应用程序完全退出
                System.Windows.Application.Current.Shutdown();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            // 显示主页视图，隐藏其他视图
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

            if (homeView != null) homeView.Visibility = Visibility.Visible;
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
            if (colorOSAssistantView != null) colorOSAssistantView.Visibility = Visibility.Collapsed;
            if (backupAssistantView != null) backupAssistantView.Visibility = Visibility.Collapsed;
            if (violetDownloadView != null) violetDownloadView.Visibility = Visibility.Collapsed;

            // 更新按钮状态
            UpdateButtonStates("Home");
        }

        private void ScreenMirrorButton_Click(object sender, RoutedEventArgs e)
        {
            // 显示投屏视图，隐藏其他视图
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
            if (screenMirrorView != null) screenMirrorView.Visibility = Visibility.Visible;
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
            if (colorOSAssistantView != null) colorOSAssistantView.Visibility = Visibility.Collapsed;
            if (backupAssistantView != null) backupAssistantView.Visibility = Visibility.Collapsed;
            if (violetDownloadView != null) violetDownloadView.Visibility = Visibility.Collapsed;

            // 更新按钮状态
            UpdateButtonStates("ScreenMirror");
        }

        private void BasicFlashButton_Click(object sender, RoutedEventArgs e)
        {
            // 显示基本刷入视图，隐藏其他视图
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
            if (basicFlashView != null) basicFlashView.Visibility = Visibility.Visible;
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
            if (colorOSAssistantView != null) colorOSAssistantView.Visibility = Visibility.Collapsed;
            if (backupAssistantView != null) backupAssistantView.Visibility = Visibility.Collapsed;
            if (violetDownloadView != null) violetDownloadView.Visibility = Visibility.Collapsed;

            // 更新按钮状态
            UpdateButtonStates("BasicFlash");
        }

        private void FastbootVisualizationButton_Click(object sender, RoutedEventArgs e)
        {
            // 显示Fastboot可视化视图，隐藏其他视图
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
            if (fastbootVisualizationView != null) fastbootVisualizationView.Visibility = Visibility.Visible;
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
            if (colorOSAssistantView != null) colorOSAssistantView.Visibility = Visibility.Collapsed;
            if (backupAssistantView != null) backupAssistantView.Visibility = Visibility.Collapsed;
            if (violetDownloadView != null) violetDownloadView.Visibility = Visibility.Collapsed;

            // 更新按钮状态
            UpdateButtonStates("FastbootVisualization");

            // 检查设备连接状态并更新分区操作按钮状态
            UpdatePartitionButtonStates();
        }

        private void DownloadZoneButton_Click(object sender, RoutedEventArgs e)
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
            if (downloadView != null) downloadView.Visibility = Visibility.Visible;
            if (aboutToolView != null) aboutToolView.Visibility = Visibility.Collapsed;
            if (systemZoneView != null) systemZoneView.Visibility = Visibility.Collapsed;
            if (oujiaFlashView != null) oujiaFlashView.Visibility = Visibility.Collapsed;
            if (autorootView != null) autorootView.Visibility = Visibility.Collapsed;
            if (appManagementView != null) appManagementView.Visibility = Visibility.Collapsed;
            if (androidGeneralView != null) androidGeneralView.Visibility = Visibility.Collapsed;
            if (payloadView != null) payloadView.Visibility = Visibility.Collapsed;
            if (romDownloadView != null) romDownloadView.Visibility = Visibility.Collapsed;
            if (edlFlashView != null) edlFlashView.Visibility = Visibility.Collapsed;
            if (colorOSAssistantView != null) colorOSAssistantView.Visibility = Visibility.Collapsed;
            if (backupAssistantView != null) backupAssistantView.Visibility = Visibility.Collapsed;
            if (violetDownloadView != null) violetDownloadView.Visibility = Visibility.Collapsed;

            UpdateButtonStates("DownloadZone");

            currentView = "DownloadZone";

        }

        private void VioletDownloadButton_Click(object sender, RoutedEventArgs e)
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
            if (colorOSAssistantView != null) colorOSAssistantView.Visibility = Visibility.Collapsed;
            if (backupAssistantView != null) backupAssistantView.Visibility = Visibility.Collapsed;
            if (violetDownloadView != null) violetDownloadView.Visibility = Visibility.Visible;

            UpdateButtonStates("VioletDownload");
            currentView = "VioletDownload";
        }

        private void AboutToolButton_Click(object sender, RoutedEventArgs e)
        {
            // 显示关于工具视图，隐藏其他视图
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
            if (aboutToolView != null) aboutToolView.Visibility = Visibility.Visible;
            if (systemZoneView != null) systemZoneView.Visibility = Visibility.Collapsed;
            if (oujiaFlashView != null) oujiaFlashView.Visibility = Visibility.Collapsed;
            if (autorootView != null) autorootView.Visibility = Visibility.Collapsed;
            if (appManagementView != null) appManagementView.Visibility = Visibility.Collapsed;
            if (androidGeneralView != null) androidGeneralView.Visibility = Visibility.Collapsed;
            if (payloadView != null) payloadView.Visibility = Visibility.Collapsed;
            if (romDownloadView != null) romDownloadView.Visibility = Visibility.Collapsed;
            if (edlFlashView != null) edlFlashView.Visibility = Visibility.Collapsed;
            if (colorOSAssistantView != null) colorOSAssistantView.Visibility = Visibility.Collapsed;
            if (backupAssistantView != null) backupAssistantView.Visibility = Visibility.Collapsed;
            if (violetDownloadView != null) violetDownloadView.Visibility = Visibility.Collapsed;

            // 更新按钮状态
            UpdateButtonStates("AboutTool");

        }

        private void AutorootButton_Click(object sender, RoutedEventArgs e)
        {
            // 显示一键ROOT视图，隐藏其他视图
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
            if (autorootView != null) autorootView.Visibility = Visibility.Visible;
            if (appManagementView != null) appManagementView.Visibility = Visibility.Collapsed;
            if (androidGeneralView != null) androidGeneralView.Visibility = Visibility.Collapsed;
            if (payloadView != null) payloadView.Visibility = Visibility.Collapsed;
            if (romDownloadView != null) romDownloadView.Visibility = Visibility.Collapsed;
            if (edlFlashView != null) edlFlashView.Visibility = Visibility.Collapsed;
            if (colorOSAssistantView != null) colorOSAssistantView.Visibility = Visibility.Collapsed;
            if (backupAssistantView != null) backupAssistantView.Visibility = Visibility.Collapsed;
            if (violetDownloadView != null) violetDownloadView.Visibility = Visibility.Collapsed;

            EnsureAndRefreshAutorootTrafficUsage();

            // 更新按钮状态
            UpdateButtonStates("Autoroot");
        }

        // Autoroot功能事件处理方法
        private void BtnSelectMagisk_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"选择{GetAutoRootManagerInfo(GetSelectedAutoRootPatchScheme()).DisplayName}安装包",
                Filter = "APK文件 (*.apk)|*.apk|所有文件 (*.*)|*.*",
                FilterIndex = 1
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var txtMagiskPath = this.FindName("txtMagiskPath") as System.Windows.Controls.TextBox;
                if (txtMagiskPath != null)
                {
                    txtMagiskPath.Text = openFileDialog.FileName;
                    magiskApkPath = openFileDialog.FileName;
                }
            }
        }

        private void BtnSelectBoot_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = IsKernelPatchScheme(GetSelectedAutoRootPatchScheme())
                    ? "选择boot镜像文件"
                    : "选择Boot镜像文件",
                Filter = "镜像文件 (*.img)|*.img|所有文件 (*.*)|*.*",
                FilterIndex = 1
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var txtBootPath = this.FindName("txtBootPath") as System.Windows.Controls.TextBox;
                if (txtBootPath != null)
                {
                    txtBootPath.Text = openFileDialog.FileName;
                    txtBootPath.Foreground = System.Windows.Media.Brushes.Black;
                }
            }
        }

        private void GkiSelectBootButton_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择Boot镜像文件",
                Filter = "镜像文件 (*.img)|*.img|所有文件 (*.*)|*.*",
                FilterIndex = 1
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var box = this.FindName("GkiBootPathTextBox") as System.Windows.Controls.TextBox;
                if (box != null)
                {
                    box.Text = openFileDialog.FileName;
                    box.Foreground = System.Windows.Media.Brushes.Black;
                }
            }
        }

        private void SelectAnykernel3Button_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择AnyKernel3压缩包",
                Filter = "ZIP文件 (*.zip)|*.zip|所有文件 (*.*)|*.*",
                FilterIndex = 1
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var box = this.FindName("GkiAk3PathTextBox") as System.Windows.Controls.TextBox;
                if (box != null)
                {
                    box.Text = openFileDialog.FileName;
                    box.Foreground = System.Windows.Media.Brushes.Black;
                }
            }
        }

        private async void GkiStartBuildButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var bootBox = this.FindName("GkiBootPathTextBox") as System.Windows.Controls.TextBox;
                var ak3Box = this.FindName("GkiAk3PathTextBox") as System.Windows.Controls.TextBox;
                if (bootBox == null || ak3Box == null)
                {
                    System.Windows.MessageBox.Show("缺少输入控件", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                var bootPath = bootBox.Text?.Trim() ?? string.Empty;
                var ak3Path = ak3Box.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(bootPath) || !File.Exists(bootPath))
                {
                    System.Windows.MessageBox.Show("请先选择有效的 Boot 镜像文件（文件名不限）", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                if (string.IsNullOrWhiteSpace(ak3Path) || !File.Exists(ak3Path))
                {
                    System.Windows.MessageBox.Show("请先选择有效的 AnyKernel3 压缩包", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                AppendAutorootLog("开始GKI打包…", "yellow");
                AppendAutorootLog($"boot: {bootPath}");
                AppendAutorootLog($"ak3: {ak3Path}");

                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var magiskbootExe = System.IO.Path.Combine(appDir, "magiskboot.exe");
                var gkiPatchBat = System.IO.Path.Combine(appDir, "GKIPatch.bat");
                var sevenZExe = System.IO.Path.Combine(appDir, "bin", "7z.exe");
                if (!File.Exists(magiskbootExe) || !File.Exists(gkiPatchBat) || !File.Exists(sevenZExe))
                {
                    System.Windows.MessageBox.Show("程序目录缺少必要文件", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    if (!File.Exists(magiskbootExe)) AppendAutorootLog($"缺少文件: {magiskbootExe}", "red");
                    if (!File.Exists(gkiPatchBat)) AppendAutorootLog($"缺少文件: {gkiPatchBat}", "red");
                    if (!File.Exists(sevenZExe)) AppendAutorootLog($"缺少文件: {sevenZExe}", "red");
                    return;
                }

                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var outputDir = System.IO.Path.Combine(desktop, "SmartTool_GKI");
                // Each build owns its directory; stale images/kernels must not be reused.
                var workDir = System.IO.Path.Combine(outputDir, "build-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workDir);
                AppendAutorootLog($"工作目录: {workDir}");

                // Normalize only the working copy, not the user's selected file.
                var bootCopy = System.IO.Path.Combine(workDir, "boot.img");
                var ak3Copy = System.IO.Path.Combine(workDir, "AnyKernel3.zip");
                File.Copy(bootPath, bootCopy, true);
                File.Copy(ak3Path, ak3Copy, true);
                AppendAutorootLog("已复制输入文件到工作目录");

                var magiskbootCopy = System.IO.Path.Combine(workDir, System.IO.Path.GetFileName(magiskbootExe));
                var gkiPatchCopy = System.IO.Path.Combine(workDir, System.IO.Path.GetFileName(gkiPatchBat));
                File.Copy(magiskbootExe, magiskbootCopy, true);
                File.Copy(gkiPatchBat, gkiPatchCopy, true);
                AppendAutorootLog("已复制工具文件到工作目录");

                var extractedDir = System.IO.Path.Combine(workDir, "AK3");
                Directory.CreateDirectory(extractedDir);
                AppendAutorootLog("开始解压AnyKernel3压缩包…");

                var psi7z = new ProcessStartInfo
                {
                    FileName = sevenZExe,
                    Arguments = $"x \"{ak3Copy}\" -o\"{extractedDir}\" -y",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = workDir
                };
                using (var p7z = Process.Start(psi7z))
                {
                    if (p7z == null) throw new InvalidOperationException("无法启动 7-Zip");
                    {
                        var outTask = Task.Run(async () =>
                        {
                            while (true)
                            {
                                var line = await p7z.StandardOutput.ReadLineAsync();
                                if (line == null) break;
                                AppendAutorootLog($"[7z] {line}");
                            }
                        });
                        var errTask = Task.Run(async () =>
                        {
                            while (true)
                            {
                                var line = await p7z.StandardError.ReadLineAsync();
                                if (line == null) break;
                                AppendAutorootLog($"[7z] {line}");
                            }
                        });
                        await Task.WhenAll(outTask, errTask);
                        await p7z.WaitForExitAsync();
                        if (p7z.ExitCode != 0)
                            throw new InvalidOperationException($"AnyKernel3 解压失败，退出码: {p7z.ExitCode}");
                    }
                }
                AppendAutorootLog("解压完成");

                var bootInAk3 = System.IO.Path.Combine(extractedDir, System.IO.Path.GetFileName(bootCopy));
                var magiskInAk3 = System.IO.Path.Combine(extractedDir, System.IO.Path.GetFileName(magiskbootCopy));
                var patchInAk3 = System.IO.Path.Combine(extractedDir, System.IO.Path.GetFileName(gkiPatchCopy));
                File.Copy(bootCopy, bootInAk3, true);
                File.Copy(magiskbootCopy, magiskInAk3, true);
                File.Copy(gkiPatchCopy, patchInAk3, true);
                AppendAutorootLog("已复制文件到AK3目录");

                var psiBat = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/d /c GKIPatch.bat boot.img",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true,
                    WorkingDirectory = extractedDir
                };
                AppendAutorootLog("开始运行GKIPatch脚本…");
                using (var pBat = Process.Start(psiBat))
                {
                    if (pBat == null) throw new InvalidOperationException("无法启动 GKIPatch");
                    // An error-path PAUSE in the batch must not wait for invisible input.
                    pBat.StandardInput.Close();
                    {
                        var outTask = Task.Run(async () =>
                        {
                            while (true)
                            {
                                var line = await pBat.StandardOutput.ReadLineAsync();
                                if (line == null) break;
                                AppendAutorootLog($"[GKIPatch] {line}");
                            }
                        });
                        var errTask = Task.Run(async () =>
                        {
                            while (true)
                            {
                                var line = await pBat.StandardError.ReadLineAsync();
                                if (line == null) break;
                                AppendAutorootLog($"[GKIPatch] {line}");
                            }
                        });
                        await Task.WhenAll(outTask, errTask);
                        await pBat.WaitForExitAsync();
                        if (pBat.ExitCode != 0)
                            throw new InvalidOperationException($"GKIPatch 打包失败，退出码: {pBat.ExitCode}");
                    }
                }
                AppendAutorootLog("脚本执行完成");

                var newBoot = System.IO.Path.Combine(extractedDir, "boot-new.img");
                if (!File.Exists(newBoot) || new FileInfo(newBoot).Length == 0)
                {
                    System.Windows.MessageBox.Show("未生成新镜像", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    AppendAutorootLog("未生成新镜像", "red");
                    return;
                }

                var finalBoot = System.IO.Path.Combine(outputDir, "boot-new.img");
                File.Copy(newBoot, finalBoot, true);
                AppendAutorootLog($"已生成: {finalBoot}", "green");

                // Remove only this build's generated directory, never other output/user files.
                try
                {
                    Directory.Delete(workDir, true);
                    AppendAutorootLog("清理完成");
                }
                catch (Exception cleanupError)
                {
                    AppendAutorootLog($"镜像已生成，临时目录清理失败: {cleanupError.Message}", "yellow");
                }

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{outputDir}\"",
                        UseShellExecute = true
                    });
                }
                catch { }
                AppendAutorootLog("已打开输出目录", "green");
            }
            catch (Exception ex)
            {
                AppendAutorootLog($"错误: {ex.Message}", "red");
            }
        }

        private async Task RunOfflinePatchAsync()
        {
            if (AutoFlashAndInstallCheckBox?.IsChecked == true)
            {
                await RunOfflinePatchAutoFlashAsync();
                return;
            }

            var txtBootPath = this.FindName("txtBootPath") as System.Windows.Controls.TextBox;
            var txtMagiskPath = this.FindName("txtMagiskPath") as System.Windows.Controls.TextBox;
            var startButton = btnAutoRoot;
            object? originalButtonContent = startButton?.Content;
            string patchScheme = GetSelectedAutoRootPatchScheme();
            bool isAlpha = string.Equals(patchScheme, "Alpha", StringComparison.OrdinalIgnoreCase);
            bool isKernelPatch = IsKernelPatchScheme(patchScheme);

            // 验证输入
            if (string.IsNullOrWhiteSpace(txtBootPath?.Text) ||
                txtBootPath.Text == "请选择你的boot.img文件路径:" ||
                txtBootPath.Text == OfflineBootFileHint ||
                txtBootPath.Text == KernelPatchBootFileHint)
            {
                System.Windows.MessageBox.Show(
                    isKernelPatch ? "请先选择 boot.img 文件..." : "请先选择 init_boot 或 boot 文件...",
                    "错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string selectedManagerPath = txtMagiskPath?.Text?.Trim() ?? string.Empty;
            bool downloadManager = string.IsNullOrWhiteSpace(selectedManagerPath) ||
                                   selectedManagerPath == OfflineManagerFileHint;

            if (!File.Exists(txtBootPath.Text))
            {
                System.Windows.MessageBox.Show("Boot文件不存在...", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if ((isAlpha || isKernelPatch) && !downloadManager && !File.Exists(selectedManagerPath))
            {
                System.Windows.MessageBox.Show("管理器安装包不存在...", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                EnsureKernelPatchBootImage(txtBootPath.Text, patchScheme);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, patchScheme, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 检测boot文件名称是否包含特殊符号（括号或空格）
            string bootFileName = IOPath.GetFileName(txtBootPath.Text);
            if (isAlpha && (bootFileName.Contains("(") || bootFileName.Contains(")") || bootFileName.Contains(" ")))
            {
                AppendAutorootLog("检测到boot文件名称存在特殊符号，请修改文件名称重试...", "red");
                return;
            }

            if (isAlpha)
            {
                InitializeAutorootPaths();
            }

            // 禁用按钮防止重复点击
            if (startButton != null)
            {
                startButton.IsEnabled = false;
                startButton.Content = "修补中...";
            }
            SetPatchSchemeSelectionEnabled(false);

            string? managerWorkDirectory = null;
            string? managerTmpRoot = null;
            bool createdManagerTmpRoot = false;

            // 开始修补过程
            try
            {
                string bootPath = txtBootPath.Text;
                string magiskPath = selectedManagerPath;
                if (isAlpha && downloadManager)
                {
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    managerTmpRoot = IOPath.Combine(desktop, "VioletTmp");
                    createdManagerTmpRoot = !Directory.Exists(managerTmpRoot);
                    managerWorkDirectory = IOPath.Combine(managerTmpRoot, "OfflinePatch_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                    Directory.CreateDirectory(managerWorkDirectory);
                    var manager = GetAutoRootManagerInfo(patchScheme);
                    magiskPath = IOPath.Combine(managerWorkDirectory, manager.FileName);
                    AppendAutorootLog($"未选择管理器安装包，开始下载 {manager.DisplayName}...", "yellow");
                    await DownloadAutoRootFileAsync(
                        manager.DownloadUrl,
                        magiskPath,
                        0,
                        20,
                        System.Threading.CancellationToken.None);
                    AppendAutorootLog($"{manager.DisplayName} 下载完成", "green");
                }
                // 修改输出路径为桌面，统一输出到桌面
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string suffix = isAlpha
                    ? "_patched"
                    : string.Equals(patchScheme, "APatch", StringComparison.OrdinalIgnoreCase)
                        ? "_apatch_patched"
                        : string.Equals(patchScheme, "FolkPatch", StringComparison.OrdinalIgnoreCase)
                            ? "_folkpatch_patched"
                            : string.Equals(patchScheme, "ReSukiSU LKM", StringComparison.OrdinalIgnoreCase)
                        ? "_resukisu_lkm_patched"
                        : string.Equals(patchScheme, "SukiSU LKM", StringComparison.OrdinalIgnoreCase)
                            ? "_sukisu_lkm_patched"
                            : "_kernelsu_lkm_patched";
                string outputPath = IOPath.Combine(
                    desktopPath,
                    IOPath.GetFileNameWithoutExtension(bootPath) + suffix + IOPath.GetExtension(bootPath));

                bool success;
                if (isAlpha)
                {
                    success = await ExecuteMagiskPatchAsync(bootPath, outputPath, magiskPath);
                }
                else if (isKernelPatch)
                {
                    await ExecuteKernelPatchAsync(
                        bootPath,
                        outputPath,
                        patchScheme,
                        System.Threading.CancellationToken.None);
                    success = true;
                }
                else
                {
                    string kmi = await ResolveOfflineLkmKmiAsync(
                        System.Threading.CancellationToken.None);
                    await ExecuteLkmPatchAsync(
                        bootPath,
                        outputPath,
                        patchScheme,
                        kmi,
                        System.Threading.CancellationToken.None);
                    success = true;
                }

                if (success)
                {
                    // 修补成功，不弹出成功窗口，只在日志中记录
                    AppendAutorootLog($"修补成功...输出文件：{outputPath}");
                }
                else
                {
                    System.Windows.MessageBox.Show("修补失败...请检查文件和路径是否正确。", "错误", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                AppendAutorootLog($"{patchScheme} 修补失败: {ex.Message}", "red");
                System.Windows.MessageBox.Show($"修补过程中发生错误：{ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(managerWorkDirectory) &&
                    !string.IsNullOrWhiteSpace(managerTmpRoot))
                {
                    TryCleanupAutoRootDirectory(managerWorkDirectory, managerTmpRoot, createdManagerTmpRoot);
                    if (txtMagiskPath != null) txtMagiskPath.Text = OfflineManagerFileHint;
                }

                // 恢复按钮状态
                if (startButton != null)
                {
                    startButton.IsEnabled = true;
                    startButton.Content = originalButtonContent;
                }
                SetPatchSchemeSelectionEnabled(true);
            }
        }

        private async void BtnAutoRoot_Click(object sender, RoutedEventArgs e)
        {
            if (OnePlusAutoRootModeRadioButton?.IsChecked == true)
            {
                await RunOnePlusAutoRootAsync();
                return;
            }

            await RunOfflinePatchAsync();
        }

        private async Task<bool> CheckDeviceConnection()
        {
            try
            {
                string adbPath = GetAdbPath();
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = "devices",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                // 检查是否有设备连接
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("device") && !line.Contains("List of devices"))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> GetKernelVersion()
        {
            try
            {
                string adbPath = GetAdbPath();
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = "shell uname -r",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                return output.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private string DetermineFileType(string kernelVersion)
        {
            try
            {
                // 解析内核版本号
                var match = Regex.Match(kernelVersion, @"(\d+)\.(\d+)\.(\d+)");
                if (match.Success)
                {
                    int major = int.Parse(match.Groups[1].Value);
                    int minor = int.Parse(match.Groups[2].Value);
                    int patch = int.Parse(match.Groups[3].Value);

                    // 5.4内核比较特殊，需要通过修补boot.img来获取root权限
                    if (major == 5 && minor == 4)
                    {
                        return "boot.img";
                    }
                    // 5.10及以下版本的内核需要修补boot.img获得root权限
                    else if (major < 5 || (major == 5 && minor <= 10))
                    {
                        return "boot.img";
                    }
                    // 5.15及以上的内核版本通过修补initboot来获取root权限
                    else if (major > 5 || (major == 5 && minor >= 15))
                    {
                        return "initboot";
                    }
                }
                
                // 默认返回boot.img
                return "boot.img";
            }
            catch
            {
                return "boot.img";
            }
        }

        private async Task WaitForPatchCompletion()
        {
            // 这里需要监控修补过程的完成状态
            // 可以通过检查日志或者文件状态来判断
            await Task.Delay(5000); // 临时等待5秒
        }

        private async Task MovePatchedFileToDesktop(string desktopPath, string fileType)
        {
            try
            {
                // 检查桌面是否已经存在修补后的文件
                string expectedFileName = GetPatchedFileName(fileType);
                string desktopFilePath = IOPath.Combine(desktopPath, expectedFileName);
                
                if (IOFile.Exists(desktopFilePath))
                {
                    AppendAutorootLog($"修补文件已存在于桌面: {expectedFileName}");
                    return;
                }
                
                // 如果桌面没有文件，则在程序目录中搜索
                string sourcePattern = fileType == "boot.img" ? "*boot*patched*.img" : "*init*boot*patched*.img";
                string[] files = Directory.GetFiles(Environment.CurrentDirectory, sourcePattern, SearchOption.AllDirectories);
                
                if (files.Length > 0)
                {
                    string sourceFile = files[0];
                    IOFile.Copy(sourceFile, desktopFilePath, true);
                    AppendAutorootLog($"修补文件已移动到桌面: {expectedFileName}");
                }
                else
                {
                    // 尝试更宽泛的搜索模式
                    string[] allImgFiles = Directory.GetFiles(Environment.CurrentDirectory, "*.img", SearchOption.AllDirectories);
                    var patchedFiles = allImgFiles.Where(f => 
                        IOPath.GetFileName(f).ToLower().Contains("patch") && 
                        (IOPath.GetFileName(f).ToLower().Contains("boot") || IOPath.GetFileName(f).ToLower().Contains("init"))
                    ).ToArray();
                    
                    if (patchedFiles.Length > 0)
                    {
                        string sourceFile = patchedFiles[0];
                        IOFile.Copy(sourceFile, desktopFilePath, true);
                        AppendAutorootLog($"修补文件已移动到桌面: {expectedFileName}");
                    }
                    else
                    {
                        AppendAutorootLog("在程序目录中未找到修补后的文件，但修补过程可能已直接保存到桌面");
                    }
                }
            }
            catch (Exception ex)
            {
                AppendAutorootLog($"处理修补文件时出错: {ex.Message}");
            }
        }

        private string GetPatchedFileName(string fileType)
        {
            return fileType == "boot.img" ? "boot_patched.img" : "init_boot_patched.img";
        }

        private async Task RebootToFastboot()
        {
            try
            {
                string adbPath = GetAdbPath();
                
                // 检测chkFastbootD复选框状态，决定使用哪个重启命令
                string rebootCommand = "reboot bootloader"; // 默认命令
                if (chkFastbootD.IsChecked == true)
                {
                    rebootCommand = "reboot fastboot";
                    AppendAutorootLog("检测到FastbootD模式已选择，使用 'adb reboot fastboot' 命令");
                }
                else
                {
                    AppendAutorootLog("使用标准Fastboot模式，使用 'adb reboot bootloader' 命令");
                }
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = rebootCommand,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                AppendAutorootLog($"重启到fastboot失败: {ex.Message}");
            }
        }

        private async Task<bool> WaitForFastbootDevice()
        {
            int maxAttempts = 60; // 3分钟，每3秒检查一次
            int attempts = 0;

            while (attempts < maxAttempts)
            {
                try
                {
                    string fastbootPath = GetFastbootPath();
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = fastbootPath,
                            Arguments = "devices",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    string output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    if (!string.IsNullOrWhiteSpace(output) && output.Contains("fastboot"))
                    {
                        return true;
                    }
                }
                catch
                {
                    // 忽略错误，继续尝试
                }

                await Task.Delay(3000); // 等待3秒
                attempts++;
            }

            return false;
        }

        private async Task<bool> WaitForFastbootDeviceWithCountdown(
            string fastbootPath,
            int timeoutSeconds,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(fastbootPath) || timeoutSeconds <= 0)
            {
                return false;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var firstCheckProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fastbootPath,
                        Arguments = "devices",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                firstCheckProcess.Start();
                Task<string> firstOutputTask = firstCheckProcess.StandardOutput.ReadToEndAsync();
                await firstCheckProcess.WaitForExitAsync(cancellationToken);
                string firstOutput = await firstOutputTask;

                if (!string.IsNullOrWhiteSpace(firstOutput) && firstOutput.Contains("fastboot", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }

            Paragraph countdownParagraph = null;
            Run countdownRun = null;

            Dispatcher.Invoke(() =>
            {
                countdownParagraph = new Paragraph { Margin = new Thickness(0) };
                countdownRun = new Run();
                countdownParagraph.Inlines.Add(countdownRun);
                FastbootLogTextBox.Document.Blocks.Add(countdownParagraph);
                FastbootLogTextBox.ScrollToEnd();
            });

            try
            {
                for (int remaining = timeoutSeconds; remaining >= 1; remaining--)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Dispatcher.Invoke(() =>
                    {
                        if (countdownRun != null)
                        {
                            countdownRun.Text = $"[{DateTime.Now:HH:mm:ss}] 等待设备...{remaining}s";
                        }
                    });

                    try
                    {
                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = fastbootPath,
                                Arguments = "devices",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            }
                        };

                        process.Start();
                        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                        await process.WaitForExitAsync(cancellationToken);
                        string output = await outputTask;

                        if (!string.IsNullOrWhiteSpace(output) && output.Contains("fastboot", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                    }

                    await Task.Delay(1000, cancellationToken);
                }

                return false;
            }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        if (countdownParagraph != null)
                        {
                            FastbootLogTextBox.Document.Blocks.Remove(countdownParagraph);
                        }
                    }
                    catch
                    {
                    }
                });
            }
        }

        private async Task<bool> FlashPatchedFile(string fileType, string filePath)
        {
            try
            {
                string partition = fileType == "boot.img" ? "boot" : "init_boot";
                string fastbootPath = GetFastbootPath();
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fastbootPath,
                        Arguments = $"flash {partition} \"{filePath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private async Task RebootDevice()
        {
            try
            {
                string fastbootPath = GetFastbootPath();
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fastbootPath,
                        Arguments = "reboot",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                AppendAutorootLog($"重启设备失败: {ex.Message}");
            }
        }

        private async Task KillFastbootProcesses()
        {
            try
            {
                var processes = Process.GetProcessesByName("fastboot");
                foreach (var process in processes)
                {
                    try
                    {
                        process.Kill();
                        await process.WaitForExitAsync();
                    }
                    catch
                    {
                        // 忽略无法结束的进程
                    }
                }
            }
            catch (Exception ex)
            {
                AppendAutorootLog($"结束fastboot进程失败: {ex.Message}");
            }
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            var txtLog = this.FindName("txtLog") as System.Windows.Controls.RichTextBox;
            if (txtLog != null)
            {
                txtLog.Document.Blocks.Clear();
                AppendAutorootLog("日志已清空");
            }
        }

        private void AvbSelectBootButton_Click(object sender, RoutedEventArgs e)
        {
            SelectAvbImageFile(AvbBootPathTextBox, "boot 镜像");
        }

        private void AvbSelectInitBootButton_Click(object sender, RoutedEventArgs e)
        {
            SelectAvbImageFile(AvbInitBootPathTextBox, "boot/init_boot 镜像");
        }

        private void AvbSelectVbmetaButton_Click(object sender, RoutedEventArgs e)
        {
            SelectAvbImageFile(AvbVbmetaPathTextBox, "vbmeta 镜像");
        }

        private void AvbSelectAospVbmetaButton_Click(object sender, RoutedEventArgs e)
        {
            SelectAvbImageFile(AvbAospVbmetaPathTextBox, "vbmeta 镜像");
        }

        private void AvbSelectAospBootButton_Click(object sender, RoutedEventArgs e)
        {
            SelectAvbImageFile(AvbAospBootPathTextBox, "boot 镜像");
        }

        private void AvbSelectAospRecoveryButton_Click(object sender, RoutedEventArgs e)
        {
            SelectAvbImageFile(AvbAospRecoveryPathTextBox, "recovery 镜像");
        }

        private void AvbSelectAospVbmetaSystemButton_Click(object sender, RoutedEventArgs e)
        {
            SelectAvbImageFile(AvbAospVbmetaSystemPathTextBox, "vbmeta_system 镜像");
        }

        private void AvbModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateAvbModeFileInputs();
        }

        private async void AvbAnalyzeVbmetaButton_Click(object sender, RoutedEventArgs e)
        {
            const string Vbmeta117PublicKeySha256 =
                "7728e30f50bfa5cea165f473175a08803f6a8346642b5aa10913e9d9e6defef6";
            const string Vbmeta229PublicKeySha256 =
                "7b34b55104c8f0f48ae3763b1b20990aa9f8f5f0dffb838d7e31ef506c819281";

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择需要分析的 vbmeta.img",
                Filter = "vbmeta 镜像 (vbmeta*.img)|vbmeta*.img|Android 镜像 (*.img)|*.img|所有文件 (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
                FileName = "vbmeta.img"
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var vbmetaPath = dialog.FileName;

            AvbAnalyzeVbmetaButton.IsEnabled = false;
            try
            {
                var rebuilder = new NativeAvbRebuilder(AppendAutorootLog);
                var profile = await Task.Run(() => rebuilder.AnalyzePublicKeys(vbmetaPath));
                AppendAutorootLog(
                    $"vbmeta 主公钥: RSA-{profile.MainKey.KeyBits}, SHA-256: {profile.MainKey.Sha256}");

                if (profile.ChainKeys.TryGetValue("vbmeta_system", out var vbmetaSystemKey))
                {
                    AppendAutorootLog(
                        $"vbmeta_system 链式公钥: RSA-{vbmetaSystemKey.KeyBits}, SHA-256: {vbmetaSystemKey.Sha256}");
                }

                if (string.Equals(
                    profile.MainKey.Sha256,
                    Vbmeta117PublicKeySha256,
                    StringComparison.OrdinalIgnoreCase))
                {
                    AppendAvbAnalysisResult("该版本可使用旧方案", true);
                }
                else if (string.Equals(
                    profile.MainKey.Sha256,
                    Vbmeta229PublicKeySha256,
                    StringComparison.OrdinalIgnoreCase))
                {
                    AppendAvbAnalysisResult("请先使用改回AOSP签名+旧版本abl", false);
                }
                else
                {
                    AppendAutorootLog("未知的公钥，无法识别", "yellow");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppendAutorootLog($"vbmeta 分析失败: {ex.Message}", "red");
            }
            finally
            {
                AvbAnalyzeVbmetaButton.IsEnabled = true;
            }
        }

        private void AppendAvbAnalysisResult(string message, bool oldSchemeAvailable)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendAvbAnalysisResult(message, oldSchemeAvailable));
                return;
            }

            var richTextBox = this.FindName("txtLog") as System.Windows.Controls.RichTextBox;
            if (richTextBox == null) return;

            var accentColor = oldSchemeAvailable
                ? System.Windows.Media.Color.FromRgb(22, 163, 74)
                : System.Windows.Media.Color.FromRgb(217, 119, 6);
            var paragraph = new System.Windows.Documents.Paragraph
            {
                Margin = new System.Windows.Thickness(0, 1, 0, 1),
                LineHeight = 19
            };
            paragraph.Inlines.Add(new System.Windows.Documents.Run($"{DateTime.Now:HH:mm:ss}")
            {
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(148, 163, 184)),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11,
            });
            paragraph.Inlines.Add(new System.Windows.Documents.Run("    "));
            paragraph.Inlines.Add(new System.Windows.Documents.Run(message)
            {
                Foreground = new System.Windows.Media.SolidColorBrush(accentColor),
                FontWeight = FontWeights.Bold
            });

            richTextBox.Document.Blocks.Add(paragraph);
            richTextBox.ScrollToEnd();
        }

        private async void AvbStartSignButton_Click(object sender, RoutedEventArgs e)
        {
            await RunAvbRebuildToolAsync();
        }

        private async Task RunAvbRebuildToolAsync()
        {
            var isChainedMode = IsAvbChainedMode();
            var isNewAospMode = IsAvbNewAospMode();
            var selection = isNewAospMode
                ? ResolveAvbNewAospSelection()
                : ResolveAvbSelection(isChainedMode);
            if (selection == null)
            {
                return;
            }

            var avbRoot = ResolveAvbToolRoot();
            if (string.IsNullOrWhiteSpace(avbRoot))
            {
                AppendAutorootLog("未找到AVB私钥目录", "red");
                System.Windows.MessageBox.Show("未找到AVB私钥目录，请确认程序目录或项目根目录存在 avbtool\\tools\\pem。", "AVB签名", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!Directory.Exists(Path.Combine(avbRoot, "tools", "pem")))
            {
                AppendAutorootLog($"AVB私钥目录不完整: {avbRoot}", "red");
                return;
            }

            var startButton = this.FindName("AvbStartSignButton") as System.Windows.Controls.Button;
            if (startButton != null) startButton.IsEnabled = false;

            try
            {
                var rebuilder = new NativeAvbRebuilder(AppendAutorootLog);
                var outputSelection = await Task.Run(() => CreateAvbOutputSelection(selection));
                AppendAutorootLog($"签名输出目录: {outputSelection.OutputDirectory}", "green");
                AppendAutorootLog("开始自动验证AVB镜像...", "yellow");
                await Task.Run(() => rebuilder.Verify(outputSelection.VbmetaPath, outputSelection.PartitionImages));

                const bool useOriginalSalt = true;
                const int keySelection = 0;
                AppendAutorootLog("开始执行AVB签名...", "yellow");
                if (isNewAospMode)
                {
                    await Task.Run(() => rebuilder.RebuildAospChain(
                        outputSelection.PartitionImages,
                        outputSelection.VbmetaPath!,
                        avbRoot));
                }
                else
                {
                    await Task.Run(() => rebuilder.Rebuild(
                        outputSelection.PartitionImages,
                        outputSelection.VbmetaPath,
                        avbRoot,
                        useOriginalSalt,
                        isChainedMode,
                        keySelection));
                }
            }
            catch (Exception ex)
            {
                AppendAutorootLog($"AVB执行失败: {ex.Message}", "red");
            }
            finally
            {
                if (startButton != null) startButton.IsEnabled = true;
            }
        }

        private static AvbOutputSelection CreateAvbOutputSelection(AvbSelection selection)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop))
            {
                throw new DirectoryNotFoundException("无法获取桌面目录。");
            }

            var outputDirectory = Path.Combine(
                desktop,
                "VioletToolAVB_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(outputDirectory);

            var outputPartitions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in selection.PartitionImages)
            {
                var outputPath = Path.Combine(
                    outputDirectory,
                    "VioletAVB_" + Path.GetFileName(item.Value));
                CopyAvbSourceToOutput(item.Value, outputPath);
                outputPartitions[item.Key] = outputPath;
            }

            string? outputVbmetaPath = null;
            if (!string.IsNullOrWhiteSpace(selection.VbmetaPath))
            {
                outputVbmetaPath = Path.Combine(
                    outputDirectory,
                    "VioletAVB_" + Path.GetFileName(selection.VbmetaPath));
                CopyAvbSourceToOutput(selection.VbmetaPath, outputVbmetaPath);
            }

            return new AvbOutputSelection(outputPartitions, outputVbmetaPath, outputDirectory);
        }

        private static void CopyAvbSourceToOutput(string sourcePath, string outputPath)
        {
            var fullSourcePath = Path.GetFullPath(sourcePath);
            var fullOutputPath = Path.GetFullPath(outputPath);
            if (string.Equals(fullSourcePath, fullOutputPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"请选择 VioletToolAVB 目录外的原始镜像: {Path.GetFileName(sourcePath)}");
            }

            File.Copy(fullSourcePath, fullOutputPath, true);
        }

        private void SelectAvbImageFile(System.Windows.Controls.TextBox target, string displayName)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"选择 {displayName}",
                Filter = "Android镜像 (*.img)|*.img|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                target.Text = dialog.FileName;
                AppendAutorootLog($"已选择 {displayName}: {dialog.FileName}", "green");
            }
        }

        private AvbSelection? ResolveAvbSelection(bool isChainedMode)
        {
            if (isChainedMode)
            {
                var bootPath = AvbBootPathTextBox?.Text?.Trim() ?? string.Empty;
                if (!ValidateAvbSelectedFile(bootPath, "boot 镜像")) return null;

                var rebuilder = new NativeAvbRebuilder(AppendAutorootLog);
                var partitionName = rebuilder.DetectPartitionName(bootPath, new[] { "boot" }, "boot");
                AppendAutorootLog($"识别目标分区: {partitionName}", "green");
                return new AvbSelection(
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [partitionName] = bootPath
                    },
                    null);
            }

            var targetPath = AvbInitBootPathTextBox?.Text?.Trim() ?? string.Empty;
            var vbmetaPath = AvbVbmetaPathTextBox?.Text?.Trim() ?? string.Empty;
            if (!ValidateAvbSelectedFile(targetPath, "boot/init_boot 镜像")) return null;
            if (!ValidateAvbSelectedFile(vbmetaPath, "vbmeta 镜像")) return null;

            var detector = new NativeAvbRebuilder(AppendAutorootLog);
            var fallbackPartition = Path.GetFileNameWithoutExtension(targetPath)
                .Contains("init_boot", StringComparison.OrdinalIgnoreCase)
                ? "init_boot"
                : "boot";
            var detectedPartition = detector.DetectPartitionName(
                targetPath,
                new[] { "boot", "init_boot" },
                fallbackPartition);
            AppendAutorootLog($"识别目标分区: {detectedPartition}", "green");

            return new AvbSelection(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [detectedPartition] = targetPath
                },
                vbmetaPath);
        }

        private AvbSelection? ResolveAvbNewAospSelection()
        {
            var vbmetaPath = AvbAospVbmetaPathTextBox?.Text?.Trim() ?? string.Empty;
            var bootPath = AvbAospBootPathTextBox?.Text?.Trim() ?? string.Empty;
            var recoveryPath = AvbAospRecoveryPathTextBox?.Text?.Trim() ?? string.Empty;
            var vbmetaSystemPath = AvbAospVbmetaSystemPathTextBox?.Text?.Trim() ?? string.Empty;

            if (!ValidateAvbSelectedFile(vbmetaPath, "vbmeta 镜像")) return null;
            if (!ValidateAvbSelectedFile(bootPath, "boot 镜像")) return null;
            if (!ValidateAvbSelectedFile(recoveryPath, "recovery 镜像")) return null;
            if (!ValidateAvbSelectedFile(vbmetaSystemPath, "vbmeta_system 镜像")) return null;

            return new AvbSelection(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["boot"] = bootPath,
                    ["recovery"] = recoveryPath,
                    ["vbmeta_system"] = vbmetaSystemPath
                },
                vbmetaPath);
        }

        private bool ValidateAvbSelectedFile(string path, string displayName)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                System.Windows.MessageBox.Show($"请先选择 {displayName}。", "AVB签名", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private bool IsAvbChainedMode()
        {
            return (AvbModeComboBox?.SelectedIndex ?? 0) == 1;
        }

        private bool IsAvbNewAospMode()
        {
            return (AvbModeComboBox?.SelectedIndex ?? 0) == 2;
        }

        private void UpdateAvbModeFileInputs()
        {
            var isChainedMode = IsAvbChainedMode();
            var isNewAospMode = IsAvbNewAospMode();
            if (AvbChainedFilePanel != null)
            {
                AvbChainedFilePanel.Visibility = isChainedMode && !isNewAospMode
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (AvbNonChainedFilePanel != null)
            {
                AvbNonChainedFilePanel.Visibility = !isChainedMode && !isNewAospMode
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (AvbNewAospFilePanel != null)
            {
                AvbNewAospFilePanel.Visibility = isNewAospMode
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

        }

        private sealed record AvbSelection(
            IReadOnlyDictionary<string, string> PartitionImages,
            string? VbmetaPath);

        private sealed record AvbOutputSelection(
            IReadOnlyDictionary<string, string> PartitionImages,
            string? VbmetaPath,
            string OutputDirectory);

        private string ResolveAvbToolRoot()
        {
            var candidates = new List<string>();
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            candidates.Add(Path.Combine(baseDir, "avbtool"));

            var dir = new DirectoryInfo(baseDir);
            for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                candidates.Add(Path.Combine(dir.FullName, "avbtool"));
            }

            return candidates
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(x => File.Exists(Path.Combine(x, "tools", "pem", "testkey_rsa4096.pem")) &&
                                     File.Exists(Path.Combine(x, "tools", "pem", "testkey_rsa2048.pem"))) ?? string.Empty;
        }

        private void LinkTutorial_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://violettool.top/",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppendAutorootLog($"打开教程链接失败: {ex.Message}", "red");
            }
        }

        private void UploadWebsiteLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AddLogMessage("下载专区", $"打开链接失败: {ex.Message}");
            }
            finally
            {
                e.Handled = true;
            }
        }

        // 复选框事件处理方法
        private void ChkFastboot_Checked(object sender, RoutedEventArgs e)
        {
            // chkFastboot选中时，取消chkFastbootD的选中状态
            var chkFastbootD = this.FindName("chkFastbootD") as System.Windows.Controls.CheckBox;
            if (chkFastbootD != null)
            {
                chkFastbootD.IsChecked = false;
            }
            AppendAutorootLog("已选择Fastboot模式", "yellow");
        }

        private void ChkFastboot_Unchecked(object sender, RoutedEventArgs e)
        {
            // 可以在这里添加取消选中的逻辑
        }

        private void ChkFastbootD_Checked(object sender, RoutedEventArgs e)
        {
            // chkFastbootD选中时，取消chkFastboot的选中状态
            var chkFastboot = this.FindName("chkFastboot") as System.Windows.Controls.CheckBox;
            if (chkFastboot != null)
            {
                chkFastboot.IsChecked = false;
            }
            AppendAutorootLog("已选择FastbootD模式", "yellow");
        }

        private void ChkFastbootD_Unchecked(object sender, RoutedEventArgs e)
        {
            // 可以在这里添加取消选中的逻辑
        }

        private async void SystemZoneButton_Click(object sender, RoutedEventArgs e)
        {
            // 显示系统专区视图，隐藏其他视图
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
            if (oujiaFlashView != null) oujiaFlashView.Visibility = Visibility.Collapsed;
            if (systemZoneView != null) systemZoneView.Visibility = Visibility.Visible;
            if (autorootView != null) autorootView.Visibility = Visibility.Collapsed;
            if (appManagementView != null) appManagementView.Visibility = Visibility.Collapsed;
            if (androidGeneralView != null) androidGeneralView.Visibility = Visibility.Collapsed;
            if (payloadView != null) payloadView.Visibility = Visibility.Collapsed;
            if (romDownloadView != null) romDownloadView.Visibility = Visibility.Collapsed;
            if (edlFlashView != null) edlFlashView.Visibility = Visibility.Collapsed;
            if (colorOSAssistantView != null) colorOSAssistantView.Visibility = Visibility.Collapsed;
            if (backupAssistantView != null) backupAssistantView.Visibility = Visibility.Collapsed;
            if (violetDownloadView != null) violetDownloadView.Visibility = Visibility.Collapsed;

            // 更新按钮状态
            UpdateButtonStates("SystemZone");

            // 本次程序运行中只在首次打开文件传输页时读取默认内部存储。
            // 先置位可避免 SideMenu 的多个事件重复触发并发加载。
            if (!_hasAutoLoadedSystemZoneDirectory)
            {
                _hasAutoLoadedSystemZoneDirectory = true;
                await LoadFileListFromPath("/sdcard/");
            }
        }


        private void UpdateButtonStates(string activeView)
        {
            CurrentView = activeView;
            currentView = activeView;
        }

        private void AppManagementButton_Click(object sender, RoutedEventArgs e)
        {
            // 显示应用管理视图，隐藏其他视图
            var homeView = this.FindName("HomeView") as Grid;
            var screenMirrorView = this.FindName("ScreenMirrorView") as Grid;
            var basicFlashView = this.FindName("BasicFlashView") as Grid;
            var fastbootVisualizationView = this.FindName("FastbootVisualizationView") as Grid;
            var hiddenEnvironmentView = this.FindName("HiddenEnvironmentView") as Grid;
            var aboutToolView = this.FindName("AboutToolView") as Grid;
            var systemZoneView = this.FindName("SystemZoneView") as Grid;
            var oujiaFlashView = this.FindName("OujiaFlashView") as Grid;
            var autorootView = this.FindName("AutorootView") as Grid;
            var appManagementView = this.FindName("AppManagementView") as Grid;
            var androidGeneralView = this.FindName("AndroidGeneralView") as Grid;
            var downloadView = this.FindName("DownloadView") as Grid;
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
            if (aboutToolView != null) aboutToolView.Visibility = Visibility.Collapsed;
            if (systemZoneView != null) systemZoneView.Visibility = Visibility.Collapsed;
            if (oujiaFlashView != null) oujiaFlashView.Visibility = Visibility.Collapsed;
            if (autorootView != null) autorootView.Visibility = Visibility.Collapsed;
            if (appManagementView != null) appManagementView.Visibility = Visibility.Visible;
            if (androidGeneralView != null) androidGeneralView.Visibility = Visibility.Collapsed;
            if (downloadView != null) downloadView.Visibility = Visibility.Collapsed;
            if (payloadView != null) payloadView.Visibility = Visibility.Collapsed;
            if (romDownloadView != null) romDownloadView.Visibility = Visibility.Collapsed;
            if (edlFlashView != null) edlFlashView.Visibility = Visibility.Collapsed;
            if (colorOSAssistantView != null) colorOSAssistantView.Visibility = Visibility.Collapsed;
            if (backupAssistantView != null) backupAssistantView.Visibility = Visibility.Collapsed;
            if (violetDownloadView != null) violetDownloadView.Visibility = Visibility.Collapsed;

            // 更新按钮状态
            UpdateButtonStates("AppManagement");

            // 更新当前界面状态
            currentView = "AppManagement";

        }

        private void AndroidGeneralButton_Click(object sender, RoutedEventArgs e)
        {
            // 显示安卓常用视图，隐藏其他视图
            var homeView = this.FindName("HomeView") as Grid;
            var screenMirrorView = this.FindName("ScreenMirrorView") as Grid;
            var basicFlashView = this.FindName("BasicFlashView") as Grid;
            var fastbootVisualizationView = this.FindName("FastbootVisualizationView") as Grid;
            var hiddenEnvironmentView = this.FindName("HiddenEnvironmentView") as Grid;
            var aboutToolView = this.FindName("AboutToolView") as Grid;
            var systemZoneView = this.FindName("SystemZoneView") as Grid;
            var oujiaFlashView = this.FindName("OujiaFlashView") as Grid;
            var autorootView = this.FindName("AutorootView") as Grid;
            var appManagementView = this.FindName("AppManagementView") as Grid;
            var androidGeneralView = this.FindName("AndroidGeneralView") as Grid;
            var downloadView = this.FindName("DownloadView") as Grid;
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
            if (aboutToolView != null) aboutToolView.Visibility = Visibility.Collapsed;
            if (systemZoneView != null) systemZoneView.Visibility = Visibility.Collapsed;
            if (oujiaFlashView != null) oujiaFlashView.Visibility = Visibility.Collapsed;
            if (autorootView != null) autorootView.Visibility = Visibility.Collapsed;
            if (appManagementView != null) appManagementView.Visibility = Visibility.Collapsed;
            if (androidGeneralView != null) androidGeneralView.Visibility = Visibility.Visible;
            if (downloadView != null) downloadView.Visibility = Visibility.Collapsed;
            if (payloadView != null) payloadView.Visibility = Visibility.Collapsed;
            if (romDownloadView != null) romDownloadView.Visibility = Visibility.Collapsed;
            if (edlFlashView != null) edlFlashView.Visibility = Visibility.Collapsed;
            if (colorOSAssistantView != null) colorOSAssistantView.Visibility = Visibility.Collapsed;
            if (backupAssistantView != null) backupAssistantView.Visibility = Visibility.Collapsed;
            if (violetDownloadView != null) violetDownloadView.Visibility = Visibility.Collapsed;

            // 更新按钮状态
            UpdateButtonStates("AndroidGeneral");

            // 更新当前界面状态
            currentView = "AndroidGeneral";

        }

        private void EdlFlashButton_Click(object sender, RoutedEventArgs e)
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
            if (edlFlashView != null) edlFlashView.Visibility = Visibility.Visible;
            if (colorOSAssistantView != null) colorOSAssistantView.Visibility = Visibility.Collapsed;
            if (backupAssistantView != null) backupAssistantView.Visibility = Visibility.Collapsed;
            if (violetDownloadView != null) violetDownloadView.Visibility = Visibility.Collapsed;

            UpdateButtonStates("EdlFlash");
            currentView = "EdlFlash";
            Dispatcher.BeginInvoke(
                new Action(StartEdlCloudLoaderRefresh),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        // 读取应用列表按钮点击事件：执行 adb shell pm list packages 并填充可勾选列表
        private async void ReadAppListButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var readButton = sender as System.Windows.Controls.Button;
                if (readButton != null) readButton.IsEnabled = false;

                // 根据两个开关状态决定筛选：系统应用(-s) 或 第三方(-3)
                var thirdPartyToggle = this.FindName("AppListSwitchToggle") as System.Windows.Controls.Primitives.ToggleButton;
                var systemToggle = this.FindName("AppListSwitchToggle复制__C_") as System.Windows.Controls.Primitives.ToggleButton;
                string cmd =
                    (systemToggle?.IsChecked == true) ? "shell pm list packages -s" :
                    (thirdPartyToggle?.IsChecked == true) ? "shell pm list packages -3" :
                    "shell pm list packages";
                // 执行 adb 命令获取包列表
                string output = await ExecuteAdbCommandWithOutput(cmd);

                AppPackages.Clear();

                if (!string.IsNullOrWhiteSpace(output))
                {
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var rawLine in lines)
                    {
                        var line = rawLine.Trim();
                        if (string.IsNullOrEmpty(line)) continue;
                        if (line.StartsWith("package:")) line = line.Substring("package:".Length);
                        else if (line.StartsWith("package ")) line = line.Substring("package ".Length);

                        AppPackages.Add(new AppPackageItem { PackageName = line, AppName = line, Version = "", IsSelected = false });
                    }
                }

                if (AppPackages.Count == 0)
                {
                    AppPackages.Add(new AppPackageItem { PackageName = "未获取到包列表或设备未连接。", AppName = "未获取到包列表或设备未连接。", Version = "", IsSelected = false });
                }

                // 根据当前搜索关键词应用过滤（已改用 TextBox）
                var searchTextBox = this.FindName("AppPackageSearchComboBox") as System.Windows.Controls.TextBox;
                ApplyAppPackageFilter(searchTextBox?.Text ?? string.Empty);

                var appListDataGrid = this.FindName("AppListDataGrid") as DataGrid;
                if (appListDataGrid != null)
                {
                    appListDataGrid.ItemsSource = AppPackages;
                    appListDataGrid.Items.Refresh();
                    if (AppPackages.Count > 0)
                    {
                        appListDataGrid.ScrollIntoView(AppPackages[0]);
                    }
                }

                // 在日志窗口显示读取到的应用数量
                int count = AppPackages.Count;
                if (systemToggle?.IsChecked == true)
                {
                    AppendAppManagementLog($"读取到 {count} 个系统应用");
                }
                else if (thirdPartyToggle?.IsChecked == true)
                {
                    AppendAppManagementLog($"读取到 {count} 个用户应用");
                }
                else
                {
                    AppendAppManagementLog($"读取到 {count} 个应用");
                }

                // 推送 aapt-arm-pie 到设备并读取应用名称与版本信息
                try
                {
                    AppendAppManagementLog("正在解析应用信息…");
                    // 可能的本地 aapt-arm-pie 路径
                    var possibleAaptPaths = new List<string>
                    {
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "aapt-arm-pie"),
                        Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "aapt-arm-pie"),
                        Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "SmartTool", "aapt-arm-pie")),
                        Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "aapt-arm-pie")),
                        "C\\Users\\pcnb1\\Desktop\\Debug\\aapt-arm-pie"
                    };

                    string? aaptLocalPath = possibleAaptPaths.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
                    if (!string.IsNullOrEmpty(aaptLocalPath))
                    {
                        // 推送到设备临时目录并赋予执行权限
                        string pushResult = await ExecuteAdbCommandWithOutput($"push \"{aaptLocalPath}\" /data/local/tmp/aapt-arm-pie");
                        await ExecuteAdbCommandWithOutput("shell chmod 755 /data/local/tmp/aapt-arm-pie");

                        // 针对每个包解析名称与版本
                        for (int i = 0; i < AppPackages.Count; i++)
                        {
                            var item = AppPackages[i];
                            var pkg = item.PackageName?.Trim() ?? string.Empty;
                            // 跳过提示项或无效项
                            if (string.IsNullOrWhiteSpace(pkg) || pkg.Contains("未获取到") || pkg.StartsWith("读取失败") || pkg.StartsWith("Error"))
                                continue;

                            string pathOutput = await ExecuteAdbCommandWithOutput($"shell pm path {pkg}");
                            string deviceApkPath = string.Empty;
                            if (!string.IsNullOrWhiteSpace(pathOutput))
                            {
                                var pathLines = pathOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var raw in pathLines)
                                {
                                    var l = raw.Trim();
                                    if (l.StartsWith("package:"))
                                    {
                                        deviceApkPath = l.Substring("package:".Length);
                                        break;
                                    }
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(deviceApkPath))
                            {
                                string badgingOutput = await ExecuteAdbCommandWithOutput($"shell /data/local/tmp/aapt-arm-pie d badging {deviceApkPath}");

                                string parsedName = item.AppName;
                                string parsedVersion = item.Version;

                                try
                                {
                                    var mCn = System.Text.RegularExpressions.Regex.Match(badgingOutput ?? string.Empty, "application-label-zh-CN:'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    var mZh = System.Text.RegularExpressions.Regex.Match(badgingOutput ?? string.Empty, "application-label-zh:'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    var mDef = System.Text.RegularExpressions.Regex.Match(badgingOutput ?? string.Empty, "application-label:'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    var mVer = System.Text.RegularExpressions.Regex.Match(badgingOutput ?? string.Empty, "versionName='([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                                    if (mCn.Success) parsedName = mCn.Groups[1].Value;
                                    else if (mZh.Success) parsedName = mZh.Groups[1].Value;
                                    else if (mDef.Success) parsedName = mDef.Groups[1].Value;

                                    if (mVer.Success) parsedVersion = mVer.Groups[1].Value;
                                }
                                catch { /* 忽略解析异常 */ }

                                item.AppName = string.IsNullOrWhiteSpace(parsedName) ? item.AppName : parsedName;
                                item.Version = string.IsNullOrWhiteSpace(parsedVersion) ? item.Version : parsedVersion;

                                if (appListDataGrid != null && i % 10 == 0)
                                    appListDataGrid.Items.Refresh();
                            }
                        }

                        if (appListDataGrid != null)
                            appListDataGrid.Items.Refresh();

                        AppendAppManagementLog("应用名称与版本解析完成");
                    }
                    else
                    {
                        AppendAppManagementLog("未找到解析应用配置，跳过名称与版本解析");
                    }
                }
                catch (Exception ex2)
                {
                    AppendAppManagementLog($"解析应用信息时出现错误：{ex2.Message}");
                }
            }
            catch (Exception ex)
            {
                AppPackages.Clear();
                AppPackages.Add(new AppPackageItem { PackageName = $"读取失败: {ex.Message}", AppName = $"读取失败: {ex.Message}", Version = "", IsSelected = false });

                var appListDataGrid = this.FindName("AppListDataGrid") as DataGrid;
                if (appListDataGrid != null)
                {
                    appListDataGrid.ItemsSource = AppPackages;
                    appListDataGrid.Items.Refresh();
                }
            }
            finally
            {
                var readButton = sender as System.Windows.Controls.Button;
                if (readButton != null) readButton.IsEnabled = true;
            }
        }

        // 全选/取消全选包名
        private void SelectAllAppPackagesCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            foreach (var item in AppPackages)
            {
                item.IsSelected = true;
            }
            var appListDataGrid = this.FindName("AppListDataGrid") as DataGrid;
            if (appListDataGrid != null)
            {
                appListDataGrid.Items.Refresh();
            }
        }

        private void SelectAllAppPackagesCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (var item in AppPackages)
            {
                item.IsSelected = false;
            }
            var appListDataGrid = this.FindName("AppListDataGrid") as DataGrid;
            if (appListDataGrid != null)
            {
                appListDataGrid.Items.Refresh();
            }
        }

        // 互斥：第三方开关被勾选时，关闭系统开关
        private void AppListThirdPartyToggle_Checked(object sender, RoutedEventArgs e)
        {
            var systemToggle = this.FindName("AppListSwitchToggle复制__C_") as System.Windows.Controls.Primitives.ToggleButton;
            if (systemToggle != null && systemToggle.IsChecked == true)
            {
                systemToggle.IsChecked = false;
            }
        }

        // 互斥：系统开关被勾选时，关闭第三方开关
        private void AppListSystemToggle_Checked(object sender, RoutedEventArgs e)
        {
            var thirdPartyToggle = this.FindName("AppListSwitchToggle") as System.Windows.Controls.Primitives.ToggleButton;
            if (thirdPartyToggle != null && thirdPartyToggle.IsChecked == true)
            {
                thirdPartyToggle.IsChecked = false;
            }
        }

        // 搜索框按键事件：根据关键字过滤包名
        private void AppPackageSearchComboBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                var textBox = sender as System.Windows.Controls.TextBox;
                string keyword = textBox?.Text ?? string.Empty;
                ApplyAppPackageFilter(keyword);
            }
            catch { }
        }

        // 在应用管理日志文本框中追加日志
        private void AppendAppManagementLog(string message)
        {
            try
            {
                var logBox = this.FindName("AppManagementLogTextBox") as System.Windows.Controls.TextBox;
                if (logBox != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
                        if (!string.IsNullOrEmpty(logBox.Text))
                        {
                            logBox.AppendText("\n");
                        }
                        logBox.AppendText(line);
                        logBox.ScrollToEnd();
                    });
                }
            }
            catch { }
        }

        // 卸载选中应用按钮点击事件：逐个卸载已勾选包名
        private async void UninstallSelectedAppsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedPackages = AppPackages
                    .Where(p => p.IsSelected && !string.IsNullOrWhiteSpace(p.PackageName))
                    .Select(p => p.PackageName.Trim())
                    .ToList();

                if (selectedPackages.Count == 0)
                {
                    AddLogMessage("警告", "未选择任何应用，请在列表中勾选需要卸载的包名。");
                    return;
                }

                var button = sender as System.Windows.Controls.Button;
                if (button != null) button.IsEnabled = false;

                AddLogMessage("信息", $"开始卸载 {selectedPackages.Count} 个已选应用...");
                AppendAppManagementLog($"需要卸载 {selectedPackages.Count} 个应用");

                foreach (var pkg in selectedPackages)
                {
                    AddLogMessage("信息", $"正在卸载: {pkg}");
                    string result = await ExecuteAdbCommandWithOutput($"shell pm uninstall {pkg}");

                    if (!string.IsNullOrEmpty(result) && result.IndexOf("Success", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        AddLogMessage("成功", $"卸载成功: {pkg}");
                        AppendAppManagementLog($"卸载成功: {pkg}");
                        // 取消勾选，避免重复操作
                        var item = AppPackages.FirstOrDefault(x => x.PackageName.Equals(pkg, StringComparison.OrdinalIgnoreCase));
                        if (item != null) item.IsSelected = false;
                    }
                    else
                    {
                        AddLogMessage("错误", $"卸载失败: {pkg} - {result}");
                        AppendAppManagementLog($"卸载失败: {pkg} - {result}");
                    }
                }

                // 刷新 DataGrid 展示状态
                var appListDataGrid = this.FindName("AppListDataGrid") as DataGrid;
                appListDataGrid?.Items.Refresh();

                AddLogMessage("成功", "已完成卸载操作。");
                AppendAppManagementLog("已完成卸载操作");

                if (button != null) button.IsEnabled = true;
            }
            catch (Exception ex)
            {
                AddLogMessage("错误", $"卸载过程中发生异常: {ex.Message}");
                var button = sender as System.Windows.Controls.Button;
                if (button != null) button.IsEnabled = true;
            }
        }

        // 冻结选中应用按钮点击事件：逐个执行禁用并汇总结果
        private async void FreezeSelectedAppsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedPackages = AppPackages
                    .Where(p => p.IsSelected && !string.IsNullOrWhiteSpace(p.PackageName))
                    .Select(p => p.PackageName.Trim())
                    .ToList();

                if (selectedPackages.Count == 0)
                {
                    AddLogMessage("警告", "未选择任何应用，请在列表中勾选需要冻结的包名。");
                    return;
                }

                var button = sender as System.Windows.Controls.Button;
                if (button != null) button.IsEnabled = false;

                AddLogMessage("信息", $"开始冻结 {selectedPackages.Count} 个已选应用...");
                AppendAppManagementLog($"需要冻结 {selectedPackages.Count} 个应用");

                foreach (var pkg in selectedPackages)
                {
                    AddLogMessage("信息", $"正在冻结: {pkg}");
                    string result = await ExecuteAdbCommandWithOutput($"shell pm disable-user {pkg}");

                    // 记录命令返回，部分设备返回为空或不同信息，最终以汇总校验为准
                    if (!string.IsNullOrEmpty(result))
                    {
                        if (result.IndexOf("disabled-user", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            result.IndexOf("new state", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            result.IndexOf("success", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            AddLogMessage("成功", $"冻结命令返回成功: {pkg}");
                            AppendAppManagementLog($"冻结命令返回成功: {pkg}");
                        }
                        else
                        {
                            AddLogMessage("信息", $"冻结返回: {pkg} - {result}");
                            AppendAppManagementLog($"冻结返回: {pkg} - {result}");
                        }
                    }
                }

                // 汇总验证：读取已冻结列表，统计最终成功与失败
                string disabledOutput = await ExecuteAdbCommandWithOutput("shell pm list packages -d");
                var disabledSet = new HashSet<string>(
                    (disabledOutput ?? string.Empty)
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => l.StartsWith("package:"))
                        .Select(l => l.Substring("package:".Length))
                        .Where(p => !string.IsNullOrWhiteSpace(p)),
                    StringComparer.OrdinalIgnoreCase);

                int successCount = selectedPackages.Count(p => disabledSet.Contains(p));
                var failedPkgs = selectedPackages.Where(p => !disabledSet.Contains(p)).ToList();

                AddLogMessage("成功", $"已成功冻结 {successCount}/{selectedPackages.Count} 个应用");
                AppendAppManagementLog($"已成功冻结{successCount}/{selectedPackages.Count}个应用");

                if (failedPkgs.Count > 0)
                {
                    string failedList = string.Join(", ", failedPkgs);
                    AddLogMessage("错误", $"冻结失败的应用包名为：{failedList}");
                    AppendAppManagementLog($"冻结失败的应用包名为：{failedList}");
                }

                // 操作完成后取消勾选成功冻结的项，避免重复执行
                foreach (var pkg in selectedPackages.Where(p => disabledSet.Contains(p)))
                {
                    var item = AppPackages.FirstOrDefault(x => x.PackageName.Equals(pkg, StringComparison.OrdinalIgnoreCase));
                    if (item != null) item.IsSelected = false;
                }
                var appListDataGrid = this.FindName("AppListDataGrid") as DataGrid;
                appListDataGrid?.Items.Refresh();

                if (button != null) button.IsEnabled = true;
            }
            catch (Exception ex)
            {
                AddLogMessage("错误", $"冻结过程中发生异常: {ex.Message}");
                AppendAppManagementLog($"冻结过程中发生异常: {ex.Message}");
                var button = sender as System.Windows.Controls.Button;
                if (button != null) button.IsEnabled = true;
            }
        }

        // 解冻选中应用按钮点击事件：逐个启用并汇总结果
        private async void UnfreezeSelectedAppsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedPackages = AppPackages
                    .Where(p => p.IsSelected && !string.IsNullOrWhiteSpace(p.PackageName))
                    .Select(p => p.PackageName.Trim())
                    .ToList();

                if (selectedPackages.Count == 0)
                {
                    AddLogMessage("警告", "未选择任何应用，请在列表中勾选需要解冻的包名。");
                    return;
                }

                var button = sender as System.Windows.Controls.Button;
                if (button != null) button.IsEnabled = false;

                AddLogMessage("信息", $"开始解冻 {selectedPackages.Count} 个已选应用...");
                AppendAppManagementLog($"需要解冻 {selectedPackages.Count} 个应用");

                foreach (var pkg in selectedPackages)
                {
                    AddLogMessage("信息", $"正在解冻: {pkg}");
                    string result = await ExecuteAdbCommandWithOutput($"shell pm enable {pkg}");

                    if (!string.IsNullOrEmpty(result))
                    {
                        if (result.IndexOf("enabled", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            result.IndexOf("new state", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            result.IndexOf("success", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            AddLogMessage("成功", $"解冻命令返回成功: {pkg}");
                            AppendAppManagementLog($"解冻命令返回成功: {pkg}");
                        }
                        else
                        {
                            AddLogMessage("信息", $"解冻返回: {pkg} - {result}");
                            AppendAppManagementLog($"解冻返回: {pkg} - {result}");
                        }
                    }
                }

                // 汇总验证：读取已冻结列表，统计最终成功与失败（成功即不再出现在禁用列表中）
                string disabledOutput = await ExecuteAdbCommandWithOutput("shell pm list packages -d");
                var disabledSet = new HashSet<string>(
                    (disabledOutput ?? string.Empty)
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim())
                        .Where(l => l.StartsWith("package:"))
                        .Select(l => l.Substring("package:".Length))
                        .Where(p => !string.IsNullOrWhiteSpace(p)),
                    StringComparer.OrdinalIgnoreCase);

                int successCount = selectedPackages.Count(p => !disabledSet.Contains(p));
                var failedPkgs = selectedPackages.Where(p => disabledSet.Contains(p)).ToList();

                AddLogMessage("成功", $"已成功解冻 {successCount}/{selectedPackages.Count} 个应用");
                AppendAppManagementLog($"已成功解冻{successCount}/{selectedPackages.Count}个应用");

                if (failedPkgs.Count > 0)
                {
                    string failedList = string.Join(", ", failedPkgs);
                    AddLogMessage("错误", $"解冻失败的应用包名为：{failedList}");
                    AppendAppManagementLog($"解冻失败的应用包名为：{failedList}");
                }

                // 操作完成后取消勾选成功解冻的项，避免重复执行
                foreach (var pkg in selectedPackages.Where(p => !disabledSet.Contains(p)))
                {
                    var item = AppPackages.FirstOrDefault(x => x.PackageName.Equals(pkg, StringComparison.OrdinalIgnoreCase));
                    if (item != null) item.IsSelected = false;
                }
                var appListDataGrid = this.FindName("AppListDataGrid") as DataGrid;
                appListDataGrid?.Items.Refresh();

                if (button != null) button.IsEnabled = true;
            }
            catch (Exception ex)
            {
                AddLogMessage("错误", $"解冻过程中发生异常: {ex.Message}");
                AppendAppManagementLog($"解冻过程中发生异常: {ex.Message}");
                var button = sender as System.Windows.Controls.Button;
                if (button != null) button.IsEnabled = true;
            }
        }

        // 查看冻结应用按钮点击事件：读取已禁用（冻结）的包名并统计数量
        private async void ViewDisabledAppsButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            if (button != null) button.IsEnabled = false;
            try
            {
                AppendAppManagementLog("正在读取冻结应用…");
                string output = await ExecuteAdbCommandWithOutput("shell pm list packages -d");

                var lines = (output ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var disabledPackages = lines
                    .Select(l => l.Trim())
                    .Where(l => l.StartsWith("package:"))
                    .Select(l => l.Substring("package:".Length))
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToList();

                // 用冻结应用填充数据网格绑定集合
                AppPackages.Clear();
                foreach (var pkg in disabledPackages)
                {
                    AppPackages.Add(new AppPackageItem { PackageName = pkg, AppName = pkg, Version = "", IsSelected = false });
                }

                if (AppPackages.Count == 0)
                {
                    AppPackages.Add(new AppPackageItem { PackageName = "未获取到冻结应用或设备未连接。", AppName = "未获取到冻结应用或设备未连接。", Version = "", IsSelected = false });
                }

                // 应用当前搜索关键字过滤
                var searchTextBox = this.FindName("AppPackageSearchComboBox") as System.Windows.Controls.TextBox;
                ApplyAppPackageFilter(searchTextBox?.Text ?? string.Empty);

                // 刷新DataGrid显示
                var appListDataGrid = this.FindName("AppListDataGrid") as DataGrid;
                if (appListDataGrid != null)
                {
                    appListDataGrid.ItemsSource = AppPackages;
                    appListDataGrid.Items.Refresh();
                    if (AppPackages.Count > 0)
                    {
                        appListDataGrid.ScrollIntoView(AppPackages[0]);
                    }
                }

                // 推送 aapt-arm-pie 并解析冻结应用的名称与版本
                try
                {
                    AppendAppManagementLog("正在解析冻结应用信息…");
                    var possibleAaptPaths = new List<string>
                    {
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "aapt-arm-pie"),
                        Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "aapt-arm-pie"),
                        Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "SmartTool", "aapt-arm-pie")),
                        Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "aapt-arm-pie")),
                        "C\\Users\\pcnb1\\Desktop\\Debug\\aapt-arm-pie"
                    };

                    string? aaptLocalPath = possibleAaptPaths.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
                    if (!string.IsNullOrEmpty(aaptLocalPath))
                    {
                        string pushResult = await ExecuteAdbCommandWithOutput($"push \"{aaptLocalPath}\" /data/local/tmp/aapt-arm-pie");
                        await ExecuteAdbCommandWithOutput("shell chmod 755 /data/local/tmp/aapt-arm-pie");

                        for (int i = 0; i < AppPackages.Count; i++)
                        {
                            var item = AppPackages[i];
                            var pkg = item.PackageName?.Trim() ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(pkg) || pkg.Contains("未获取到") || pkg.StartsWith("读取失败") || pkg.StartsWith("Error"))
                                continue;

                            string pathOutput = await ExecuteAdbCommandWithOutput($"shell pm path {pkg}");
                            string deviceApkPath = string.Empty;
                            if (!string.IsNullOrWhiteSpace(pathOutput))
                            {
                                var pathLines = pathOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var raw in pathLines)
                                {
                                    var l = raw.Trim();
                                    if (l.StartsWith("package:"))
                                    {
                                        deviceApkPath = l.Substring("package:".Length);
                                        break;
                                    }
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(deviceApkPath))
                            {
                                string badgingOutput = await ExecuteAdbCommandWithOutput($"shell /data/local/tmp/aapt-arm-pie d badging {deviceApkPath}");
                                string parsedName = item.AppName;
                                string parsedVersion = item.Version;

                                try
                                {
                                    var mCn = System.Text.RegularExpressions.Regex.Match(badgingOutput ?? string.Empty, "application-label-zh-CN:'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    var mZh = System.Text.RegularExpressions.Regex.Match(badgingOutput ?? string.Empty, "application-label-zh:'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    var mDef = System.Text.RegularExpressions.Regex.Match(badgingOutput ?? string.Empty, "application-label:'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    var mVer = System.Text.RegularExpressions.Regex.Match(badgingOutput ?? string.Empty, "versionName='([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                                    if (mCn.Success) parsedName = mCn.Groups[1].Value;
                                    else if (mZh.Success) parsedName = mZh.Groups[1].Value;
                                    else if (mDef.Success) parsedName = mDef.Groups[1].Value;

                                    if (mVer.Success) parsedVersion = mVer.Groups[1].Value;
                                }
                                catch { }

                                item.AppName = string.IsNullOrWhiteSpace(parsedName) ? item.AppName : parsedName;
                                item.Version = string.IsNullOrWhiteSpace(parsedVersion) ? item.Version : parsedVersion;

                                if (appListDataGrid != null && i % 10 == 0)
                                    appListDataGrid.Items.Refresh();
                            }
                        }

                        appListDataGrid?.Items.Refresh();
                        AppendAppManagementLog("冻结应用名称与版本解析完成");
                    }
                    else
                    {
                        AppendAppManagementLog("未找到本地 aapt-arm-pie，跳过冻结应用解析");
                    }
                }
                catch (Exception ex2)
                {
                    AppendAppManagementLog($"解析冻结应用信息时出现错误：{ex2.Message}");
                }

                AppendAppManagementLog($"读取到 {AppPackages.Count} 个已冻结应用");
            }
            catch (Exception ex)
            {
                // 显示错误信息到集合和日志
                AppPackages.Clear();
                AppPackages.Add(new AppPackageItem { PackageName = $"读取冻结应用失败: {ex.Message}", AppName = $"读取冻结应用失败: {ex.Message}", Version = "", IsSelected = false });
                var appListDataGrid = this.FindName("AppListDataGrid") as DataGrid;
                if (appListDataGrid != null)
                {
                    appListDataGrid.ItemsSource = AppPackages;
                    appListDataGrid.Items.Refresh();
                }
                AppendAppManagementLog($"读取冻结应用失败: {ex.Message}");
            }
            finally
            {
                if (button != null) button.IsEnabled = true;
            }
        }

        // 提取APK安装包：选择保存目录，逐个解析路径并拉取到电脑后按包名重命名
        private async void ExtractApkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedPackages = AppPackages
                    .Where(p => p.IsSelected && !string.IsNullOrWhiteSpace(p.PackageName))
                    .Select(p => p.PackageName.Trim())
                    .ToList();

                if (selectedPackages.Count == 0)
                {
                    AddLogMessage("警告", "未选择任何应用，请在列表中勾选需要提取的包名。");
                    return;
                }

                var button = sender as System.Windows.Controls.Button;
                if (button != null) button.IsEnabled = false;

                // 选择保存目录
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dialog.Description = "选择保存APK的文件夹";
                    dialog.ShowNewFolderButton = true;
                    var result = dialog.ShowDialog();
                    if (result != System.Windows.Forms.DialogResult.OK)
                    {
                        AppendAppManagementLog("用户取消了保存路径选择");
                        if (button != null) button.IsEnabled = true;
                        return;
                    }

                    string saveDir = dialog.SelectedPath;
                    AppendAppManagementLog($"保存路径: {saveDir}");

                    int successCount = 0;
                    int failCount = 0;
                    foreach (var pkg in selectedPackages)
                    {
                        try
                        {
                            AppendAppManagementLog($"正在处理: {pkg}");
                            // 获取APK路径（可能返回多行，优先选择包含base.apk的行）
                            string pathOutput = await ExecuteAdbCommandWithOutput($"shell pm path {pkg}");
                            var lines = (pathOutput ?? string.Empty)
                                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(l => l.Trim())
                                .Where(l => l.StartsWith("package:"))
                                .Select(l => l.Substring("package:".Length))
                                .ToList();

                            string? deviceApkPath = lines.FirstOrDefault(l => l.EndsWith("/base.apk", StringComparison.OrdinalIgnoreCase))
                                                     ?? lines.FirstOrDefault();

                            if (string.IsNullOrWhiteSpace(deviceApkPath))
                            {
                                AppendAppManagementLog($"未找到安装包路径: {pkg} - {pathOutput}");
                                failCount++;
                                continue;
                            }

                            var item = AppPackages.FirstOrDefault(p => string.Equals(p.PackageName?.Trim(), pkg, StringComparison.OrdinalIgnoreCase));
                            string baseName = MakeSafeApkFileName(item?.AppName);
                            if (string.IsNullOrWhiteSpace(baseName)) baseName = MakeSafeApkFileName(pkg);
                            string destFile = System.IO.Path.Combine(saveDir, baseName + ".apk");
                            int suffix = 1;
                            while (System.IO.File.Exists(destFile))
                            {
                                destFile = System.IO.Path.Combine(saveDir, $"{baseName}({suffix}).apk");
                                suffix++;
                            }
                            AppendAppManagementLog($"拉取: {deviceApkPath} -> {destFile}");

                            string pullOutput = await ExecuteAdbCommandWithOutput($"pull \"{deviceApkPath}\" \"{destFile}\" ");

                            // 校验本地文件是否存在作为成功依据
                            if (System.IO.File.Exists(destFile))
                            {
                                AppendAppManagementLog($"提取成功: {pkg} -> {destFile}");
                                successCount++;
                                // 不自动取消勾选，避免用户后续继续其他操作；如需取消可改为 true
                            }
                            else
                            {
                                AppendAppManagementLog($"提取失败: {pkg} - {pullOutput}");
                                failCount++;
                            }
                        }
                        catch (Exception innerEx)
                        {
                            AppendAppManagementLog($"处理 {pkg} 失败: {innerEx.Message}");
                            failCount++;
                        }
                    }

                    AppendAppManagementLog($"提取完成，成功 {successCount}，失败 {failCount}");
                }
            }
            catch (Exception ex)
            {
                AddLogMessage("错误", $"提取APK过程中发生异常: {ex.Message}");
            }
            finally
            {
                var button = sender as System.Windows.Controls.Button;
                if (button != null) button.IsEnabled = true;
            }
        }

        private static string MakeSafeApkFileName(string? name)
        {
            var n = (name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(n)) return string.Empty;
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var arr = n.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            var sanitized = new string(arr).Trim().TrimEnd('.');
            return string.IsNullOrWhiteSpace(sanitized) ? string.Empty : sanitized;
        }

        // 小米冻结系统更新：卸载用户0的com.android.updater（保留数据）
        private async void FreezeMiuiUpdaterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as System.Windows.Controls.Button;
                if (button != null) button.IsEnabled = false;

                AppendAppManagementLog("正在冻结系统更新 (com.android.updater)…");
                string output = await ExecuteAdbCommandWithOutput("shell pm uninstall -k --user 0 com.android.updater");

                if (!string.IsNullOrEmpty(output) && output.IndexOf("Success", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    AppendAppManagementLog("冻结系统更新成功");
                    AddLogMessage("成功", "小米系统更新已冻结");
                }
                else
                {
                    AppendAppManagementLog($"冻结系统更新可能失败: {output}");
                    AddLogMessage("错误", $"冻结系统更新失败: {output}");
                }

                if (button != null) button.IsEnabled = true;
            }
            catch (Exception ex)
            {
                AddLogMessage("错误", $"冻结系统更新异常: {ex.Message}");
            }
        }

        // 小米恢复系统更新：安装现有的com.android.updater到用户0
        private async void UnfreezeMiuiUpdaterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as System.Windows.Controls.Button;
                if (button != null) button.IsEnabled = false;

                AppendAppManagementLog("正在恢复系统更新 (com.android.updater)…");
                string output = await ExecuteAdbCommandWithOutput("shell cmd package install-existing com.android.updater");

                // 简单输出判断
                bool installed = (!string.IsNullOrEmpty(output) &&
                                  (output.IndexOf("installed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   output.IndexOf("Success", StringComparison.OrdinalIgnoreCase) >= 0));

                // 进一步验证：查询路径是否存在
                string pathCheck = await ExecuteAdbCommandWithOutput("shell pm path com.android.updater");
                bool hasPath = !string.IsNullOrWhiteSpace(pathCheck) && pathCheck.Contains("package:");

                if (installed || hasPath)
                {
                    AppendAppManagementLog("恢复系统更新成功");
                    AddLogMessage("成功", "小米系统更新已恢复");
                }
                else
                {
                    AppendAppManagementLog($"恢复系统更新可能失败: {output}");
                    AddLogMessage("错误", $"恢复系统更新失败: {output}");
                }

                if (button != null) button.IsEnabled = true;
            }
            catch (Exception ex)
            {
                AddLogMessage("错误", $"恢复系统更新异常: {ex.Message}");
            }
        }

        // 清除应用数据：针对在表格中勾选的应用逐个执行 pm clear
        private async void ClearAppDataButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            if (button != null) button.IsEnabled = false;
            try
            {
                var selectedApps = AppPackages
                    .Where(p => p.IsSelected && !string.IsNullOrWhiteSpace(p.PackageName))
                    .ToList();

                if (selectedApps.Count == 0)
                {
                    AddLogMessage("警告", "未选择任何应用，请在列表中勾选需要清除数据的应用。");
                    return;
                }

                AppendAppManagementLog($"准备清除 {selectedApps.Count} 个应用的数据…");

                int total = selectedApps.Count;
                int success = 0;
                int failed = 0;

                for (int i = 0; i < total; i++)
                {
                    var app = selectedApps[i];
                    var pkg = app.PackageName.Trim();

                    try
                    {
                        AppendAppManagementLog($"[{i + 1}/{total}] 正在清除: {pkg}");
                        string output = await ExecuteAdbCommandWithOutput($"shell pm clear {pkg}");

                        bool ok = !string.IsNullOrEmpty(output) &&
                                  output.IndexOf("Success", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (ok)
                        {
                            AppendAppManagementLog($"[{i + 1}/{total}] 清除成功: {pkg}");
                            success++;
                        }
                        else
                        {
                            AppendAppManagementLog($"[{i + 1}/{total}] 清除失败: {pkg} - {output}");
                            failed++;
                        }
                    }
                    catch (Exception inner)
                    {
                        AppendAppManagementLog($"[{i + 1}/{total}] 清除 {pkg} 异常: {inner.Message}");
                        failed++;
                    }
                }

                AppendAppManagementLog($"清除完成，成功 {success}，失败 {failed}");
            }
            catch (Exception ex)
            {
                AddLogMessage("错误", $"清除应用数据过程中发生异常: {ex.Message}");
            }
            finally
            {
                if (button != null) button.IsEnabled = true;
            }
        }

        // 应用包名过滤
        private void ApplyAppPackageFilter(string keyword)
        {
            ICollectionView view = CollectionViewSource.GetDefaultView(AppPackages);
            if (view == null) return;

            if (string.IsNullOrWhiteSpace(keyword))
            {
                view.Filter = null;
            }
            else
            {
                string k = keyword.Trim();
                view.Filter = o =>
                {
                    if (o is AppPackageItem item && !string.IsNullOrEmpty(item.PackageName))
                    {
                        return item.PackageName.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                    return false;
                };
            }
            view.Refresh();

            // 刷新数据网格显示
            var appListDataGrid = this.FindName("AppListDataGrid") as DataGrid;
            appListDataGrid?.Items.Refresh();
        }

        // 复制包名按钮点击事件：复制已勾选的包名到剪贴板，并记录日志数量
        private void CopySelectedPackagesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedPackages = AppPackages
                    .Where(p => p.IsSelected && !string.IsNullOrWhiteSpace(p.PackageName))
                    .Select(p => p.PackageName.Trim())
                    .ToList();

                if (selectedPackages.Count == 0)
                {
                    AppendAppManagementLog("未选择任何包名");
                    return;
                }

                string textToCopy = selectedPackages.Count == 1
                    ? selectedPackages[0]
                    : string.Join("|", selectedPackages);

                System.Windows.Clipboard.SetText(textToCopy);
                AppendAppManagementLog($"成功复制了 {selectedPackages.Count} 个包名");
            }
            catch (Exception ex)
            {
                AppendAppManagementLog($"复制包名失败: {ex.Message}");
            }
        }
        
        // 根据设备连接状态更新分区操作按钮状态
        private void UpdatePartitionButtonStates()
        {
            var readPartitionButton = this.FindName("ReadPartitionButton") as System.Windows.Controls.Button;
            var backupBasebandButton = this.FindName("BackupBasebandButton") as System.Windows.Controls.Button;
            var backupGptButton = this.FindName("BackupGptButton") as System.Windows.Controls.Button;
            
            if (readPartitionButton != null && backupBasebandButton != null && backupGptButton != null)
            {
                bool adbMode = string.Equals(
                    BottomConnectionTypeText?.Text,
                    "系统",
                    StringComparison.OrdinalIgnoreCase);
                readPartitionButton.IsEnabled = adbMode;
                backupBasebandButton.IsEnabled = adbMode;
                backupGptButton.IsEnabled = adbMode;
            }
        }

        private void PartitionSearchComboBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var comboBox = sender as System.Windows.Controls.ComboBox;
            if (comboBox == null || allPartitions == null) return;

            var searchText = comboBox.Text;
            if (string.IsNullOrWhiteSpace(searchText))
            {
                comboBox.ItemsSource = null;
                comboBox.IsDropDownOpen = false;
                return;
            }

            var filteredPartitions = allPartitions.Where(p => 
                p.PartitionName.IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0
            ).ToList();

            if (filteredPartitions.Any())
            {
                comboBox.ItemsSource = filteredPartitions;
                comboBox.IsDropDownOpen = true;
            }
            else
            {
                comboBox.ItemsSource = null;
                comboBox.IsDropDownOpen = false;
            }
        }

        private void PartitionSearchComboBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            // 允许文本输入
        }

        private void PartitionSearchComboBox_DropDownOpened(object sender, System.EventArgs e)
        {
            var comboBox = sender as System.Windows.Controls.ComboBox;
            if (comboBox == null || allPartitions == null) return;

            // 如果没有搜索文本，显示所有分区
            if (string.IsNullOrWhiteSpace(comboBox.Text))
            {
                comboBox.ItemsSource = allPartitions.ToList();
            }
        }

        private void PartitionSearchComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var comboBox = sender as System.Windows.Controls.ComboBox;
            var dataGrid = this.FindName("PartitionTableDataGrid") as System.Windows.Controls.DataGrid;
            
            if (comboBox?.SelectedItem is PartitionInfo selectedPartition && dataGrid != null)
            {
                // 找到选中的分区在DataGrid容器中的位置
                if (dataGrid.ItemsSource is System.Collections.ObjectModel.ObservableCollection<PartitionInfo> partitions)
                {
                    var targetPartition = partitions.FirstOrDefault(p => p.PartitionName == selectedPartition.PartitionName);
                    if (targetPartition != null)
                    {
                        // 跳转到选中的行
                        dataGrid.SelectedItem = targetPartition;
                        dataGrid.ScrollIntoView(targetPartition);
                        
                        // 自动勾选此分区
                        targetPartition.IsSelected = true;
                        
                        // 清空搜索框
                        comboBox.Text = "";
                        comboBox.SelectedItem = null;
                        comboBox.IsDropDownOpen = false;
                    }
                }
            }
        }

        private void PartitionSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not System.Windows.Controls.TextBox searchTextBox ||
                PartitionTableDataGrid?.ItemsSource == null)
            {
                return;
            }

            string keyword = searchTextBox.Text.Trim();
            ICollectionView view = CollectionViewSource.GetDefaultView(PartitionTableDataGrid.ItemsSource);
            view.Filter = string.IsNullOrWhiteSpace(keyword)
                ? null
                : item => item is PartitionInfo partition &&
                          partition.PartitionName.Contains(keyword, StringComparison.OrdinalIgnoreCase);
            view.Refresh();
        }

        private void PartitionSearchTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape &&
                sender is System.Windows.Controls.TextBox searchTextBox &&
                !string.IsNullOrEmpty(searchTextBox.Text))
            {
                searchTextBox.Clear();
                e.Handled = true;
            }
        }

        private async void RebootPhoneButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 执行adb reboot命令并获取输出
                string adbOutput = await ExecuteAdbCommandWithOutput("reboot");
                
                // 如果输出中包含"found"，说明设备不在adb界面，需要执行fastboot命令
                if (!string.IsNullOrEmpty(adbOutput) && adbOutput.ToLower().Contains("found"))
                {
                    // 等待一段时间让设备重启到fastboot模式
                    await Task.Delay(3000);
                    await ExecuteFastbootCommand("reboot");
                }
                // 如果输出为空，说明设备在adb界面，不需要执行fastboot命令
                
                // 在所有重启命令执行完成后终止adb.exe和fastboot.exe进程
                await KillAllAdbAndFastbootProcesses();
            }
            catch (Exception ex)
            {
            }
        }

        private async void RebootToFastbootButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 执行adb reboot bootloader命令并获取输出
                string adbOutput = await ExecuteAdbCommandWithOutput("reboot bootloader");
                
                // 如果输出中包含"found"，说明设备不在adb界面，需要执行fastboot命令
                if (!string.IsNullOrEmpty(adbOutput) && adbOutput.ToLower().Contains("found"))
                {
                    // 等待一段时间让设备重启到fastboot模式
                    await Task.Delay(3000);
                    await ExecuteFastbootCommand("reboot-bootloader");
                }
                // 如果输出为空，说明设备在adb界面，不需要执行fastboot命令
                
                // 在所有重启命令执行完成后终止adb.exe和fastboot.exe进程
                await KillAllAdbAndFastbootProcesses();
            }
            catch (Exception ex)
            {
            }
        }

        private async void RebootToFastbootDButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 执行adb reboot fastboot命令并获取输出
                string adbOutput = await ExecuteAdbCommandWithOutput("reboot fastboot");
                
                // 如果输出中包含"found"，说明设备不在adb界面，需要执行fastboot命令
                if (!string.IsNullOrEmpty(adbOutput) && adbOutput.ToLower().Contains("found"))
                {
                    // 等待一段时间让设备重启到fastboot模式
                    await Task.Delay(3000);
                    await ExecuteFastbootCommand("reboot fastboot");
                }
                // 如果输出为空，说明设备在adb界面，不需要执行fastboot命令
                
                // 在所有重启命令执行完成后终止adb.exe和fastboot.exe进程
                await KillAllAdbAndFastbootProcesses();
            }
            catch (Exception ex)
            {
            }
        }

        private async void RebootTo9008Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 执行adb reboot edl命令并获取输出
                string adbOutput = await ExecuteAdbCommandWithOutput("reboot edl");
                
                // 如果输出中包含"found"，说明设备不在adb界面，需要执行fastboot命令
                if (!string.IsNullOrEmpty(adbOutput) && adbOutput.ToLower().Contains("found"))
                {
                    // 等待一段时间让设备重启到fastboot模式
                    await Task.Delay(3000);
                    await ExecuteFastbootCommand("oem edl");
                }
                // 如果输出为空，说明设备在adb界面，不需要执行fastboot命令
                
                // 在所有重启命令执行完成后终止adb.exe和fastboot.exe进程
                await KillAllAdbAndFastbootProcesses();
            }
            catch (Exception ex)
            {
            }
        }

        private async void RebootToRecoveryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 执行adb reboot recovery命令并获取输出
                string adbOutput = await ExecuteAdbCommandWithOutput("reboot recovery");
                
                // 如果输出中包含"found"，说明设备不在adb界面，需要执行fastboot命令
                if (!string.IsNullOrEmpty(adbOutput) && adbOutput.ToLower().Contains("found"))
                {
                    // 等待一段时间让设备重启到fastboot模式
                    await Task.Delay(3000);
                    await ExecuteFastbootCommand("reboot recovery");
                }
                // 如果输出为空，说明设备在adb界面，不需要执行fastboot命令
                
                // 在所有重启命令执行完成后终止adb.exe和fastboot.exe进程
                await KillAllAdbAndFastbootProcesses();
            }
            catch (Exception ex)
            {
            }
        }

        private async void SwitchSlotButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 获取fastboot路径
                string fastbootPath = GetFastbootPath();
                if (string.IsNullOrEmpty(fastbootPath))
                {
                    System.Windows.MessageBox.Show("错误：未找到fastboot工具", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 检查设备是否连接
                string deviceCheckResult = await ExecuteFastbootCommand(fastbootPath, "devices");
                if (string.IsNullOrEmpty(deviceCheckResult) || !deviceCheckResult.Contains("fastboot"))
                {
                    System.Windows.MessageBox.Show("请在Fastboot模式下使用此功能", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 获取当前槽位
                string currentSlotOutput = await ExecuteFastbootCommand(fastbootPath, "getvar current-slot");
                
                if (string.IsNullOrEmpty(currentSlotOutput))
                {
                    System.Windows.MessageBox.Show("无法获取当前槽位信息", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 解析当前槽位
                string currentSlot = ExtractFastbootVar(currentSlotOutput, "current-slot");
                
                if (string.IsNullOrEmpty(currentSlot))
                {
                    System.Windows.MessageBox.Show("无法解析当前槽位信息", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 确定目标槽位
                string targetSlot;
                if (currentSlot.ToLower() == "a")
                {
                    targetSlot = "b";
                }
                else if (currentSlot.ToLower() == "b")
                {
                    targetSlot = "a";
                }
                else
                {
                    System.Windows.MessageBox.Show($"未知的槽位: {currentSlot}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 显示商业化确认对话框
                if (ShowHomeSlotSwitchConfirmationDialog(currentSlot, targetSlot))
                {
                    // 执行槽位切换命令
                    string switchResult = await ExecuteFastbootCommand(fastbootPath, $"set_active {targetSlot}");
                    
                    // 显示结果
                    if (switchResult.ToLower().Contains("okay") || switchResult.ToLower().Contains("finished"))
                    {
                        ShowHomeSlotSwitchResultDialog(
                            succeeded: true,
                            currentSlot,
                            targetSlot,
                            switchResult);
                    }
                    else
                    {
                        ShowHomeSlotSwitchResultDialog(
                            succeeded: false,
                            currentSlot,
                            targetSlot,
                            switchResult);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"切换槽位时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ShowHomeSlotSwitchConfirmationDialog(string currentSlot, string targetSlot)
        {
            string currentSlotText = $"{currentSlot.ToUpperInvariant()}槽";
            string targetSlotText = $"{targetSlot.ToUpperInvariant()}槽";

            var dialog = new System.Windows.Window
            {
                Title = "切换槽位",
                Width = 430,
                Height = 285,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Background = System.Windows.Media.Brushes.White,
                FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI")
            };

            var root = new System.Windows.Controls.Grid
            {
                Margin = new Thickness(24, 20, 24, 18)
            };
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

            var headingPanel = new System.Windows.Controls.StackPanel();
            headingPanel.Children.Add(new TextBlock
            {
                Text = "请确认本次槽位切换",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(38, 49, 66))
            });
            headingPanel.Children.Add(new TextBlock
            {
                Text = "切换后，设备将在下次启动时使用目标槽位。",
                Margin = new Thickness(0, 5, 0, 0),
                FontSize = 12,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(100, 116, 139))
            });
            System.Windows.Controls.Grid.SetRow(headingPanel, 0);
            root.Children.Add(headingPanel);

            var slotGrid = new System.Windows.Controls.Grid();
            slotGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            slotGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            {
                Width = new GridLength(48)
            });
            slotGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            var currentPanel = new System.Windows.Controls.StackPanel
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            currentPanel.Children.Add(new TextBlock
            {
                Text = "当前槽位",
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                FontSize = 12,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(100, 116, 139))
            });
            currentPanel.Children.Add(new TextBlock
            {
                Text = currentSlotText,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(51, 65, 85))
            });
            System.Windows.Controls.Grid.SetColumn(currentPanel, 0);
            slotGrid.Children.Add(currentPanel);

            var arrow = new TextBlock
            {
                Text = "→",
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                FontSize = 20,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(148, 163, 184))
            };
            System.Windows.Controls.Grid.SetColumn(arrow, 1);
            slotGrid.Children.Add(arrow);

            var targetPanel = new System.Windows.Controls.StackPanel
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            targetPanel.Children.Add(new TextBlock
            {
                Text = "目标槽位",
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                FontSize = 12,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(100, 116, 139))
            });
            targetPanel.Children.Add(new TextBlock
            {
                Text = targetSlotText,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(124, 58, 237))
            });
            System.Windows.Controls.Grid.SetColumn(targetPanel, 2);
            slotGrid.Children.Add(targetPanel);

            var contentPanel = new System.Windows.Controls.StackPanel();
            contentPanel.Children.Add(slotGrid);
            contentPanel.Children.Add(new TextBlock
            {
                Text = "请确认目标槽位包含可正常启动的系统。",
                Margin = new Thickness(0, 14, 0, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                FontSize = 12,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(217, 119, 6))
            });

            var contentBorder = new System.Windows.Controls.Border
            {
                Margin = new Thickness(0, 14, 0, 14),
                Padding = new Thickness(16, 15, 16, 14),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(226, 232, 240)),
                Background = new SolidColorBrush(MediaColor.FromRgb(250, 251, 253)),
                Child = contentPanel
            };
            System.Windows.Controls.Grid.SetRow(contentBorder, 1);
            root.Children.Add(contentBorder);

            var buttonPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "取消",
                Width = 86,
                Height = 32,
                Margin = new Thickness(0, 0, 10, 0),
                Background = System.Windows.Media.Brushes.White,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(71, 85, 105)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(203, 213, 225)),
                IsCancel = true
            };
            var confirmButton = new System.Windows.Controls.Button
            {
                Content = "确认切换",
                Width = 96,
                Height = 32,
                Background = new SolidColorBrush(MediaColor.FromRgb(190, 112, 225)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(190, 112, 225)),
                Foreground = System.Windows.Media.Brushes.White,
                IsDefault = true
            };
            cancelButton.Click += (_, _) => dialog.DialogResult = false;
            confirmButton.Click += (_, _) => dialog.DialogResult = true;
            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(confirmButton);
            System.Windows.Controls.Grid.SetRow(buttonPanel, 2);
            root.Children.Add(buttonPanel);

            dialog.Content = root;
            return dialog.ShowDialog() == true;
        }

        private void ShowHomeSlotSwitchResultDialog(
            bool succeeded,
            string currentSlot,
            string targetSlot,
            string fastbootOutput)
        {
            string currentSlotText = $"{currentSlot.ToUpperInvariant()}槽";
            string targetSlotText = $"{targetSlot.ToUpperInvariant()}槽";

            var dialog = new System.Windows.Window
            {
                Title = succeeded ? "槽位切换完成" : "槽位切换失败",
                Width = 430,
                SizeToContent = SizeToContent.Height,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Background = System.Windows.Media.Brushes.White,
                FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI")
            };

            var root = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(24, 20, 24, 18)
            };
            root.Children.Add(new TextBlock
            {
                Text = succeeded ? "槽位切换成功" : "槽位切换未完成",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(38, 49, 66))
            });

            var contentPanel = new System.Windows.Controls.StackPanel();
            if (succeeded)
            {
                var slotTransition = new TextBlock
                {
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold
                };
                slotTransition.Inlines.Add(new System.Windows.Documents.Run(currentSlotText)
                {
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(51, 65, 85))
                });
                slotTransition.Inlines.Add(new System.Windows.Documents.Run("   →   ")
                {
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(148, 163, 184))
                });
                slotTransition.Inlines.Add(new System.Windows.Documents.Run(targetSlotText)
                {
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(124, 58, 237))
                });
                contentPanel.Children.Add(slotTransition);
                contentPanel.Children.Add(new TextBlock
                {
                    Text = $"设备将在下次启动时使用 {targetSlotText}。",
                    Margin = new Thickness(0, 12, 0, 0),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(100, 116, 139))
                });
            }
            else
            {
                contentPanel.Children.Add(new TextBlock
                {
                    Text = "Fastboot 未返回明确的切换成功结果，请检查设备连接及槽位状态。",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(217, 119, 6))
                });

                string nativeOutput = string.IsNullOrWhiteSpace(fastbootOutput)
                    ? "未返回原始输出"
                    : fastbootOutput.Trim();
                contentPanel.Children.Add(new TextBlock
                {
                    Text = nativeOutput,
                    Margin = new Thickness(0, 10, 0, 0),
                    MaxHeight = 58,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = nativeOutput,
                    FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono,Microsoft YaHei UI,Consolas"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(MediaColor.FromRgb(220, 38, 38))
                });
            }

            var contentBorder = new System.Windows.Controls.Border
            {
                Margin = new Thickness(0, 14, 0, 18),
                Padding = new Thickness(16, 17, 16, 16),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(226, 232, 240)),
                Background = new SolidColorBrush(MediaColor.FromRgb(250, 251, 253)),
                Child = contentPanel
            };
            root.Children.Add(contentBorder);

            var closeButton = new System.Windows.Controls.Button
            {
                Content = succeeded ? "完成" : "确定",
                Width = 88,
                Height = 32,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Background = new SolidColorBrush(MediaColor.FromRgb(190, 112, 225)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(190, 112, 225)),
                Foreground = System.Windows.Media.Brushes.White,
                IsDefault = true,
                IsCancel = true
            };
            closeButton.Click += (_, _) => dialog.DialogResult = true;
            root.Children.Add(closeButton);

            dialog.Content = root;
            dialog.ShowDialog();
        }

        private async void ForcePortraitButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteAdbCommand("shell settings put system user_rotation 0");
                await ExecuteAdbCommand("shell settings put system accelerometer_rotation 0");
                ShowMessage("已强制设置为竖屏模式");
            }
            catch (Exception ex)
            {
                ShowMessage($"强制竖屏失败: {ex.Message}");
            }
        }

        private async void ForceLandscapeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteAdbCommand("shell settings put system user_rotation 1");
                await ExecuteAdbCommand("shell settings put system accelerometer_rotation 0");
                ShowMessage("已强制设置为横屏模式");
            }
            catch (Exception ex)
            {
                ShowMessage($"强制横屏失败: {ex.Message}");
            }
        }

        private async void AutoRotateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteAdbCommand("shell settings put system accelerometer_rotation 1");
                ShowMessage("已启用自动旋转");
            }
            catch (Exception ex)
            {
                ShowMessage($"启用自动旋转失败: {ex.Message}");
            }
        }

        private async void BackKeyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SendMirrorNavigationKeyAsync(4);
            }
            catch (Exception ex)
            {
                ShowMessage($"返回键发送失败: {ex.Message}");
            }
        }

        private async void HomeKeyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SendMirrorNavigationKeyAsync(3);
            }
            catch (Exception ex)
            {
                ShowMessage($"主页键发送失败: {ex.Message}");
            }
        }

        private async void RecentAppsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SendMirrorNavigationKeyAsync(187);
            }
            catch (Exception ex)
            {
                ShowMessage($"多任务键发送失败: {ex.Message}");
            }
        }

        private async Task SendMirrorNavigationKeyAsync(int keyCode, bool silent = false)
        {
            await ExecuteAdbCommand($"shell input keyevent {keyCode}");

            if (!silent)
            {
                string message = keyCode switch
                {
                    4 => "返回键已发送",
                    3 => "主页键已发送",
                    187 => "多任务键已发送",
                    _ => $"按键 {keyCode} 已发送"
                };
                ShowMessage(message);
            }
        }

        private bool IsScrcpyControlBarEnabled()
        {
            return MirrorNavigationBarToggle?.IsChecked == true;
        }

        private void MirrorNavigationBarToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (scrcpyProcess != null && !scrcpyProcess.HasExited)
            {
                InitializeScrcpyControlBarTracking(scrcpyProcess);
            }
        }

        private void MirrorNavigationBarToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            CloseScrcpyControlBar();
        }

        private void InitializeScrcpyControlBarTracking(Process process)
        {
            if (!IsScrcpyControlBarEnabled())
            {
                CloseScrcpyControlBar();
                return;
            }

            EnsureScrcpyControlBarWindow();
            _ = AttachScrcpyControlBarAsync(process);
        }

        private async Task AttachScrcpyControlBarAsync(Process process)
        {
            try
            {
                IntPtr handle = IntPtr.Zero;

                for (int i = 0; i < 30; i++)
                {
                    if (process.HasExited)
                    {
                        return;
                    }

                    process.Refresh();
                    handle = process.MainWindowHandle;
                    if (handle != IntPtr.Zero && IsWindow(handle))
                    {
                        break;
                    }

                    await Task.Delay(200);
                }

                if (handle == IntPtr.Zero || !IsWindow(handle))
                {
                    return;
                }

                Dispatcher.Invoke(() =>
                {
                    if (!IsScrcpyControlBarEnabled())
                    {
                        CloseScrcpyControlBar();
                        return;
                    }

                    scrcpyMainWindowHandle = handle;
                    RegisterScrcpyLocationChangeHook((uint)process.Id);
                    EnsureScrcpyControlBarWindow();
                    ClearScrcpyWindowTopMost();
                    UpdateScrcpyControlBarPosition();
                    scrcpyControlBarWindow?.Show();
                    StartScrcpyControlBarTimer();
                });
            }
            catch
            {
            }
        }

        private void EnsureScrcpyControlBarWindow()
        {
            if (scrcpyControlBarWindow != null)
            {
                return;
            }

            var container = new Border
            {
                Background = new SolidColorBrush(MediaColor.FromArgb(245, 255, 255, 255)),
                BorderBrush = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString("#FFD8D8D8")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 10, 8)
            };

            var buttonPanel = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            buttonPanel.Children.Add(CreateScrcpyControlBarButton("多任务", "icon_多任务.svg", async () => await SendMirrorNavigationKeyAsync(187, true)));
            buttonPanel.Children.Add(CreateScrcpyControlBarButton("主页", "主页.svg", async () => await SendMirrorNavigationKeyAsync(3, true)));
            buttonPanel.Children.Add(CreateScrcpyControlBarButton("返回", "返回.svg", async () => await SendMirrorNavigationKeyAsync(4, true)));

            container.Child = buttonPanel;

            scrcpyControlBarWindow = new Window
            {
                Width = 292,
                Height = 58,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                Topmost = false,
                AllowsTransparency = true,
                Background = MediaBrushes.Transparent,
                ShowActivated = false,
                Content = container
            };

            scrcpyControlBarWindow.Closed += (s, e) =>
            {
                scrcpyControlBarWindow = null;
            };
        }

        private System.Windows.Controls.Button CreateScrcpyControlBarButton(string text, string iconFileName, Func<Task> onClick)
        {
            string iconPath = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "images", iconFileName);
            var contentPanel = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            if (IOFile.Exists(iconPath))
            {
                contentPanel.Children.Add(new SvgViewbox
                {
                    Source = new Uri(iconPath, UriKind.Absolute),
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(0, 0, 6, 0)
                });
            }

            contentPanel.Children.Add(new TextBlock
            {
                Text = text,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Foreground = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString("#FF1F1F1F"))
            });

            var button = new System.Windows.Controls.Button
            {
                Content = contentPanel,
                Width = 82,
                Height = 34,
                Margin = new Thickness(4, 0, 4, 0),
                Background = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString("#FFF8F9FA")),
                Foreground = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString("#FF1F1F1F")),
                BorderBrush = new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString("#FFE0E0E0")),
                BorderThickness = new Thickness(1),
                Cursor = WpfCursors.Hand,
                FontSize = 13
            };

            button.Click += async (s, e) =>
            {
                try
                {
                    await onClick();
                }
                catch (Exception ex)
                {
                    AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] ERROR: 底部控制条按键发送失败: {ex.Message}\n");
                }
            };

            button.Style = new Style(typeof(System.Windows.Controls.Button))
            {
                Setters =
                {
                    new Setter(System.Windows.Controls.Button.TemplateProperty, new ControlTemplate(typeof(System.Windows.Controls.Button))
                    {
                        VisualTree = BuildScrcpyControlBarButtonTemplate()
                    })
                }
            };

            return button;
        }

        private FrameworkElementFactory BuildScrcpyControlBarButtonTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
            border.AppendChild(presenter);

            return border;
        }

        private void StartScrcpyControlBarTimer()
        {
            if (scrcpyControlBarTimer == null)
            {
                scrcpyControlBarTimer = new DispatcherTimer
                {
                    // Keep a lightweight fallback in case some location change events are missed.
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                scrcpyControlBarTimer.Tick += (s, e) => UpdateScrcpyControlBarPosition();
            }

            scrcpyControlBarTimer.Start();
        }

        private void UpdateScrcpyControlBarPosition()
        {
            if (!IsScrcpyControlBarEnabled())
            {
                scrcpyControlBarWindow?.Hide();
                return;
            }

            if (scrcpyControlBarWindow == null)
            {
                return;
            }

            if (scrcpyMainWindowHandle == IntPtr.Zero || !IsWindow(scrcpyMainWindowHandle))
            {
                scrcpyControlBarWindow.Hide();
                return;
            }

            if (IsIconic(scrcpyMainWindowHandle) || !GetWindowRect(scrcpyMainWindowHandle, out RECT rect))
            {
                scrcpyControlBarWindow.Hide();
                return;
            }

            ClearScrcpyWindowTopMost();

            DpiScale dpi = VisualTreeHelper.GetDpi(scrcpyControlBarWindow);
            double scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
            double scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;
            double barWidthDip = scrcpyControlBarWindow.ActualWidth > 0 ? scrcpyControlBarWindow.ActualWidth : scrcpyControlBarWindow.Width;
            int scrcpyWidthPx = Math.Max(0, rect.Right - rect.Left);
            int barWidthPx = (int)Math.Round(barWidthDip * scaleX);
            int gapPx = Math.Max(4, (int)Math.Round(8 * scaleY));
            int targetLeftPx = rect.Left + Math.Max(0, (scrcpyWidthPx - barWidthPx) / 2);
            int targetTopPx = rect.Bottom + gapPx;

            if (!scrcpyControlBarWindow.IsVisible)
            {
                scrcpyControlBarWindow.Show();
            }

            IntPtr controlBarHandle = new System.Windows.Interop.WindowInteropHelper(scrcpyControlBarWindow).Handle;
            if (controlBarHandle != IntPtr.Zero)
            {
                SetWindowPos(
                    controlBarHandle,
                    HWND_NOTOPMOST,
                    targetLeftPx,
                    targetTopPx,
                    0,
                    0,
                    SWP_NOSIZE | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
            }
            else
            {
                scrcpyControlBarWindow.Left = targetLeftPx / scaleX;
                scrcpyControlBarWindow.Top = targetTopPx / scaleY;
            }
        }

        private void ClearScrcpyWindowTopMost()
        {
            if (scrcpyMainWindowHandle == IntPtr.Zero || !IsWindow(scrcpyMainWindowHandle))
            {
                return;
            }

            SetWindowPos(
                scrcpyMainWindowHandle,
                HWND_NOTOPMOST,
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
        }

        private void CloseScrcpyControlBar()
        {
            scrcpyMainWindowHandle = IntPtr.Zero;
            scrcpyControlBarTimer?.Stop();
            UnregisterScrcpyLocationChangeHook();

            if (scrcpyControlBarWindow != null)
            {
                try
                {
                    scrcpyControlBarWindow.Close();
                }
                catch
                {
                }
                scrcpyControlBarWindow = null;
            }
        }

        private void RegisterScrcpyLocationChangeHook(uint processId)
        {
            UnregisterScrcpyLocationChangeHook();

            scrcpyLocationChangeProc = OnScrcpyLocationChanged;
            scrcpyLocationChangeHook = SetWinEventHook(
                EVENT_OBJECT_LOCATIONCHANGE,
                EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero,
                scrcpyLocationChangeProc,
                processId,
                0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        }

        private void UnregisterScrcpyLocationChangeHook()
        {
            if (scrcpyLocationChangeHook != IntPtr.Zero)
            {
                UnhookWinEvent(scrcpyLocationChangeHook);
                scrcpyLocationChangeHook = IntPtr.Zero;
            }
        }

        private void OnScrcpyLocationChanged(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint idEventThread,
            uint dwmsEventTime)
        {
            if (hwnd != scrcpyMainWindowHandle || idObject != OBJID_WINDOW || idChild != CHILDID_SELF)
            {
                return;
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(UpdateScrcpyControlBarPosition));
        }

        private async void PowerKeyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteAdbCommand("shell input keyevent 26");
                ShowMessage("电源键已发送");
            }
            catch (Exception ex)
            {
                ShowMessage($"电源键发送失败: {ex.Message}");
            }
        }

        private async void VolumeUpButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteAdbCommand("shell input keyevent 24");
                ShowMessage("音量增加键已发送");
            }
            catch (Exception ex)
            {
                ShowMessage($"音量增加键发送失败: {ex.Message}");
            }
        }

        private async void VolumeDownButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteAdbCommand("shell input keyevent 25");
                ShowMessage("音量减少键已发送");
            }
            catch (Exception ex)
            {
                ShowMessage($"音量减少键发送失败: {ex.Message}");
            }
        }

        private async void LockScreenButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteAdbCommand("shell input keyevent 26");
                ShowMessage("设备已锁屏");
            }
            catch (Exception ex)
            {
                ShowMessage($"锁屏失败: {ex.Message}");
            }
        }

        private async void ScreenOnButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteAdbCommand("shell input keyevent 224");
                ShowMessage("屏幕已点亮");
            }
            catch (Exception ex)
            {
                ShowMessage($"屏幕点亮失败: {ex.Message}");
            }
        }

        private async void StartMirrorButton_Click(object sender, RoutedEventArgs e)
        {
            if (isScrcpyStarting || (scrcpyProcess != null && !scrcpyProcess.HasExited))
            {
                AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] 投屏已在启动或运行中，请勿重复启动。\n");
                ShowMessage("投屏已在启动或运行中");
                return;
            }

            isScrcpyStarting = true;
            StartMirrorButton.IsEnabled = false;

            try
            {
                // 获取选中的设备序列号
                string selectedSerial = GetSelectedDeviceSerial();
                
                // 检查是否选择了设备
                if (string.IsNullOrEmpty(selectedSerial))
                {
                    AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] ERROR: 请先选择要投屏的设备！\n");
                    ShowMessage("请先选择要投屏的设备！");
                    return;
                }
                
                // 获取当前应用程序目录
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string scrcpyPath = Path.Combine(appDirectory, "platform-tools", "scrcpy.exe");
                
                // 检查scrcpy.exe是否存在
                if (File.Exists(scrcpyPath))
                {
                    // 获取帧率滑块的值
                    var maxFpsSlider = this.FindName("MaxFpsSlider") as Slider;
                    int maxFps = maxFpsSlider != null ? (int)maxFpsSlider.Value : 60; // 默认60fps
                    
                    // 获取比特率滑块的值
                    var bitrateSlider = this.FindName("BitrateSlider") as Slider;
                    int bitrate = bitrateSlider != null ? (int)bitrateSlider.Value : 8; // 默认8Mbps
                    
                    // 获取窗口大小设置
                    int maxSize = GetWindowMaxSize();
                    
                    string arguments = await BuildScrcpyLaunchArgumentsAsync(selectedSerial, bitrate, maxFps, maxSize);
                    string scrcpyWindowTitle = !string.IsNullOrWhiteSpace(selectedSerial)
                        ? await BuildScrcpyWindowTitleAsync(selectedSerial)
                        : "紫罗兰工具箱";
                    
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = scrcpyPath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        WorkingDirectory = Path.GetDirectoryName(scrcpyPath),
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    
                    scrcpyProcess = Process.Start(startInfo);
                    
                    if (scrcpyProcess != null)
                    {
                        InitializeScrcpyControlBarTracking(scrcpyProcess);
                        // 添加日志到文本框
                        AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] 正在启动scrcpy投屏...\n");
                        AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] 目标设备: {selectedSerial}\n");
                        AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] 标题信息: {scrcpyWindowTitle}\n");
                        AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] 命令: {scrcpyPath} {arguments}\n");
                        
                        // 异步读取标准输出
                        scrcpyProcess.OutputDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] {e.Data}\n");
                            }
                        };
                        
                        // 异步读取错误输出
                        scrcpyProcess.ErrorDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] ERROR: {e.Data}\n");
                            }
                        };
                        
                        // 开始异步读取
                        scrcpyProcess.BeginOutputReadLine();
                        scrcpyProcess.BeginErrorReadLine();
                        
                        // 监听进程退出事件
                        scrcpyProcess.EnableRaisingEvents = true;
                        scrcpyProcess.Exited += (sender, e) =>
                        {
                            AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] scrcpy进程已退出\n");
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                CloseScrcpyControlBar();
                            }));
                        };
                        
                        ShowMessage($"正在启动投屏，目标设备: {selectedSerial}");
                    }
                    else
                    {
                        AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] ERROR: 启动scrcpy失败！\n");
                        ShowMessage("启动投屏失败！");
                    }
                }
                else
                {
                    AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] ERROR: 未找到scrcpy.exe文件，请检查platform-tools目录！\n");
                    ShowMessage("未找到scrcpy.exe文件，请检查platform-tools目录！");
                }
            }
            catch (Exception ex)
            {
                AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] ERROR: 投屏启动失败: {ex.Message}\n");
                ShowMessage($"投屏启动失败: {ex.Message}");
            }
            finally
            {
                isScrcpyStarting = false;
                StartMirrorButton.IsEnabled = true;
            }
        }

        // 处理投屏窗口大小单选按钮选择变化
        private void WindowSizeRadio_Checked(object sender, RoutedEventArgs e)
        {
            var radioButton = sender as System.Windows.Controls.RadioButton;
            var customSizePanel = this.FindName("CustomSizePanel") as StackPanel;
            
            if (radioButton != null && customSizePanel != null)
            {
                string tag = radioButton.Tag?.ToString() ?? "";
                
                // 如果选择了自定义大小，显示输入框
                if (tag == "custom")
                {
                    customSizePanel.Visibility = Visibility.Visible;
                }
                else
                {
                    customSizePanel.Visibility = Visibility.Collapsed;
                }
            }
        }

        // 获取窗口大小参数
        private int GetWindowMaxSize()
        {
            // 检查哪个 RadioButton 被选中
            var radio90 = this.FindName("RadioSize90") as System.Windows.Controls.RadioButton;
            var radio80 = this.FindName("RadioSize80") as System.Windows.Controls.RadioButton;
            var radio70 = this.FindName("RadioSize70") as System.Windows.Controls.RadioButton;
            var radio60 = this.FindName("RadioSize60") as System.Windows.Controls.RadioButton;
            var radioCustom = this.FindName("RadioSizeCustom") as System.Windows.Controls.RadioButton;
            
            if (radio90?.IsChecked == true) return 972;  // 90%
            if (radio80?.IsChecked == true) return 864;  // 80%
            if (radio70?.IsChecked == true) return 756;  // 70%
            if (radio60?.IsChecked == true) return 648;  // 60%
            if (radioCustom?.IsChecked == true)
            {
                // 自定义模式使用独立的窗口宽高参数，不再误用 --max-size。
                return 0;
            }
            
            // 默认返回90%
            return 972;
        }

        private async Task<string> BuildScrcpyWindowTitleAsync(string deviceSerial)
        {
            List<string> selectedOptions = GetMirrorTitleSelectionsInOrder();
            var titleParts = new List<string>();

            foreach (string optionName in selectedOptions)
            {
                string value = optionName switch
                {
                    nameof(MirrorTitleShowDeviceCodeCheckBox) => await GetAdbPropertyAsync("ro.product.device"),
                    nameof(MirrorTitleShowAndroidVersionCheckBox) => await GetAdbPropertyAsync("ro.build.version.release"),
                    nameof(MirrorTitleShowSlotCheckBox) => await GetCurrentBootSlotAsync(),
                    nameof(MirrorTitleShowSerialCheckBox) => deviceSerial,
                    nameof(MirrorTitleShowDeviceNameCheckBox) => await GetAdbPropertyAsync("ro.product.model"),
                    nameof(MirrorTitleShowBuildInfoCheckBox) => await GetBuildInfoAsync(),
                    nameof(MirrorTitleShowUnlockStateCheckBox) => await GetUnlockStateAsync(),
                    nameof(MirrorTitleShowBatteryTempCheckBox) => await GetBatteryTemperatureAsync(),
                    nameof(MirrorTitleShowBatteryLevelCheckBox) => await GetBatteryLevelAsync(),
                    nameof(MirrorTitleShowStorageCheckBox) => await GetStorageSummaryAsync(),
                    _ => string.Empty
                };

                value = GetTitleDisplayValue(value);
                value = FormatMirrorTitlePart(optionName, value);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    titleParts.Add(value);
                }
            }

            if (titleParts.Count == 0)
            {
                titleParts.Add("紫罗兰工具箱");
            }

            string title = string.Join("_", titleParts);
            return title.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private string FormatMirrorTitlePart(string optionName, string value)
        {
            return optionName switch
            {
                nameof(MirrorTitleShowSlotCheckBox) => $"槽位{value.ToUpperInvariant()}",
                nameof(MirrorTitleShowAndroidVersionCheckBox) => $"安卓{value}",
                nameof(MirrorTitleShowSerialCheckBox) => $"序列号{value}",
                nameof(MirrorTitleShowDeviceCodeCheckBox) => $"代号{value}",
                _ => value
            };
        }

        private async Task<string> BuildScrcpyLaunchArgumentsAsync(string? selectedSerial, int bitrate, int maxFps, int maxSize)
        {
            var arguments = new List<string>();

            if (!string.IsNullOrWhiteSpace(selectedSerial))
            {
                string scrcpyWindowTitle = await BuildScrcpyWindowTitleAsync(selectedSerial);
                arguments.Add($"-s {selectedSerial}");
                arguments.Add($"--window-title \"{scrcpyWindowTitle}\"");
            }

            arguments.Add($"--video-bit-rate {bitrate}M");
            arguments.Add($"--max-fps {maxFps}");

            if (maxSize > 0)
            {
                arguments.Add($"--max-size {maxSize}");
            }

            if (RadioSizeCustom?.IsChecked == true)
            {
                if (!int.TryParse(CustomWidthTextBox?.Text, out int customWidth) || customWidth <= 0 ||
                    !int.TryParse(CustomHeightTextBox?.Text, out int customHeight) || customHeight <= 0)
                {
                    throw new InvalidOperationException("自定义投屏宽度和高度必须是大于 0 的整数。");
                }

                arguments.Add($"--window-width {customWidth}");
                arguments.Add($"--window-height {customHeight}");
            }

            if (MirrorClipboardSyncCheckBox?.IsChecked != true)
            {
                arguments.Add("--no-clipboard-autosync");
            }

            if (MirrorStayAwakeCheckBox?.IsChecked == true)
            {
                arguments.Add("--stay-awake");
            }

            if (MirrorFullscreenCheckBox?.IsChecked == true)
            {
                arguments.Add("--fullscreen");
            }

            if (TopmostCheckBox?.IsChecked == true)
            {
                arguments.Add("--always-on-top");
            }

            if (MirrorOrientation90RadioButton?.IsChecked == true)
            {
                arguments.Add("--capture-orientation 90");
            }
            else if (MirrorOrientation180RadioButton?.IsChecked == true)
            {
                arguments.Add("--capture-orientation 180");
            }

            return string.Join(" ", arguments);
        }

        private void MirrorTitleOptionCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.CheckBox checkBox || string.IsNullOrWhiteSpace(checkBox.Name))
            {
                return;
            }

            _mirrorTitleSelectionOrder.Remove(checkBox.Name);

            if (checkBox.IsChecked == true)
            {
                _mirrorTitleSelectionOrder.Add(checkBox.Name);
            }
        }

        private List<string> GetMirrorTitleSelectionsInOrder()
        {
            string[] defaultOrder =
            {
                nameof(MirrorTitleShowDeviceNameCheckBox),
                nameof(MirrorTitleShowSlotCheckBox),
                nameof(MirrorTitleShowAndroidVersionCheckBox),
                nameof(MirrorTitleShowSerialCheckBox),
                nameof(MirrorTitleShowDeviceCodeCheckBox),
                nameof(MirrorTitleShowBuildInfoCheckBox),
                nameof(MirrorTitleShowUnlockStateCheckBox),
                nameof(MirrorTitleShowBatteryTempCheckBox),
                nameof(MirrorTitleShowBatteryLevelCheckBox),
                nameof(MirrorTitleShowStorageCheckBox)
            };

            List<string> checkedNames = defaultOrder
                .Where(IsMirrorTitleOptionChecked)
                .ToList();

            if (_mirrorTitleSelectionOrder.Count == 0)
            {
                return checkedNames;
            }

            _mirrorTitleSelectionOrder.RemoveAll(name => !checkedNames.Contains(name));

            foreach (string name in checkedNames)
            {
                if (!_mirrorTitleSelectionOrder.Contains(name))
                {
                    _mirrorTitleSelectionOrder.Add(name);
                }
            }

            return new List<string>(_mirrorTitleSelectionOrder);
        }

        private bool IsMirrorTitleOptionChecked(string checkBoxName)
        {
            return checkBoxName switch
            {
                nameof(MirrorTitleShowDeviceCodeCheckBox) => MirrorTitleShowDeviceCodeCheckBox?.IsChecked == true,
                nameof(MirrorTitleShowAndroidVersionCheckBox) => MirrorTitleShowAndroidVersionCheckBox?.IsChecked == true,
                nameof(MirrorTitleShowSlotCheckBox) => MirrorTitleShowSlotCheckBox?.IsChecked == true,
                nameof(MirrorTitleShowSerialCheckBox) => MirrorTitleShowSerialCheckBox?.IsChecked == true,
                nameof(MirrorTitleShowDeviceNameCheckBox) => MirrorTitleShowDeviceNameCheckBox?.IsChecked == true,
                nameof(MirrorTitleShowBuildInfoCheckBox) => MirrorTitleShowBuildInfoCheckBox?.IsChecked == true,
                nameof(MirrorTitleShowUnlockStateCheckBox) => MirrorTitleShowUnlockStateCheckBox?.IsChecked == true,
                nameof(MirrorTitleShowBatteryTempCheckBox) => MirrorTitleShowBatteryTempCheckBox?.IsChecked == true,
                nameof(MirrorTitleShowBatteryLevelCheckBox) => MirrorTitleShowBatteryLevelCheckBox?.IsChecked == true,
                nameof(MirrorTitleShowStorageCheckBox) => MirrorTitleShowStorageCheckBox?.IsChecked == true,
                _ => false
            };
        }

        private async Task<string> GetAdbPropertyAsync(string propertyName)
        {
            string output = await ExecuteAdbCommandWithOutput($"shell getprop {propertyName}");
            return NormalizeSingleLineOutput(output);
        }

        private async Task<string> GetBuildInfoAsync()
        {
            string displayId = await GetAdbPropertyAsync("ro.build.display.id");
            if (!string.IsNullOrWhiteSpace(displayId))
            {
                return displayId;
            }

            return await GetAdbPropertyAsync("ro.build.version.incremental");
        }

        private async Task<string> GetCurrentBootSlotAsync()
        {
            string slotSuffix = await GetAdbPropertyAsync("ro.boot.slot_suffix");
            if (!string.IsNullOrWhiteSpace(slotSuffix))
            {
                return slotSuffix.Trim().TrimStart('_');
            }

            string slot = await GetAdbPropertyAsync("ro.boot.slot");
            if (!string.IsNullOrWhiteSpace(slot))
            {
                return slot.Trim().TrimStart('_');
            }

            return "unknown";
        }

        private async Task<string> GetUnlockStateAsync()
        {
            string flashLocked = await GetAdbPropertyAsync("ro.boot.flash.locked");
            if (flashLocked == "0")
            {
                return "已解锁";
            }

            if (flashLocked == "1")
            {
                return "未解锁";
            }

            string deviceState = await GetAdbPropertyAsync("ro.boot.vbmeta.device_state");
            if (deviceState.Equals("unlocked", StringComparison.OrdinalIgnoreCase))
            {
                return "已解锁";
            }

            if (deviceState.Equals("locked", StringComparison.OrdinalIgnoreCase))
            {
                return "未解锁";
            }

            return string.IsNullOrWhiteSpace(deviceState) ? "unknown" : deviceState;
        }

        private async Task<string> GetBatteryLevelAsync()
        {
            string batteryOutput = await ExecuteAdbCommandWithOutput("shell dumpsys battery");
            Match levelMatch = Regex.Match(batteryOutput, @"(?im)^\s*level:\s*(\d+)\s*$");
            return levelMatch.Success ? $"{levelMatch.Groups[1].Value}%" : "unknown";
        }

        private async Task<string> GetBatteryTemperatureAsync()
        {
            string batteryOutput = await ExecuteAdbCommandWithOutput("shell dumpsys battery");
            Match tempMatch = Regex.Match(batteryOutput, @"(?im)^\s*temperature:\s*(-?\d+)\s*$");
            if (tempMatch.Success &&
                double.TryParse(tempMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double temperature))
            {
                return $"{temperature / 10.0:0.#}℃";
            }

            return "unknown";
        }

        private async Task<string> GetStorageSummaryAsync()
        {
            string output = await ExecuteAdbCommandWithOutput("shell df -h /data");
            foreach (string rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (!line.Contains(" /data") && !line.EndsWith("/data", StringComparison.OrdinalIgnoreCase) &&
                    !line.Contains(" /storage/emulated") && !line.EndsWith("/storage/emulated", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string[] tokens = Regex.Split(line, @"\s+")
                    .Where(token => !string.IsNullOrWhiteSpace(token))
                    .ToArray();

                if (tokens.Length >= 4)
                {
                    string size = tokens[1];
                    string used = tokens[2];
                    return $"{used}/{size}";
                }
            }

            return "unknown";
        }

        private string GetTitleDisplayValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        }

        private string NormalizeSingleLineOutput(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return string.Empty;
            }

            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string cleaned = line.Trim();
                if (!string.IsNullOrWhiteSpace(cleaned) &&
                    !cleaned.StartsWith("error", StringComparison.OrdinalIgnoreCase))
                {
                    return cleaned;
                }
            }

            return string.Empty;
        }

        private void StopMirrorButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] 正在停止scrcpy投屏...\n");
                
                // 停止全自动投屏功能
                if (isAutoMirrorEnabled)
                {
                    isAutoMirrorEnabled = false;
                    StopAutoMirrorTimer();
                    
                    // 取消勾选自动投屏复选框
                    if (AutoMirrorCheckBox != null)
                    {
                        AutoMirrorCheckBox.IsChecked = false;
                    }
                    AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] 已停止全自动投屏功能\n");
                }
                
                // 清理跟踪的进程引用
                scrcpyProcess = null;
                CloseScrcpyControlBar();
                
                // 使用taskkill命令强制终止所有scrcpy相关进程
                string[] commands = {
                    "/f /im scrcpy.exe",
                    "/f /im scrcpy-server.exe",
                    "/f /im scrcpy*"
                };
                
                foreach (string args in commands)
                {
                    try
                    {
                        ProcessStartInfo taskKillInfo = new ProcessStartInfo
                        {
                            FileName = "taskkill",
                            Arguments = args,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        };
                        
                        using (Process? taskKillProcess = Process.Start(taskKillInfo))
                        {
                            taskKillProcess?.WaitForExit(5000); // 等待最多5秒
                        }
                    }
                    catch
                    {
                    }
                }
                
                // 额外尝试使用wmic命令终止进程
                try
                {
                    ProcessStartInfo wmicInfo = new ProcessStartInfo
                    {
                        FileName = "wmic",
                        Arguments = "process where \"name like '%scrcpy%'\" delete",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    
                    using (Process? wmicProcess = Process.Start(wmicInfo))
                    {
                        wmicProcess?.WaitForExit(5000);
                    }
                }
                catch
                {
                }
                
                AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] scrcpy投屏已停止\n");
                ShowMessage("投屏已停止");
            }
            catch
            {
            }
        }


        private async void AndroidDriverButton_Click(object sender, RoutedEventArgs e)
        {
            string url = "https://wwim.lanzouo.com/iUb8G32pxaaj";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        private async void MTKDriverButton_Click(object sender, RoutedEventArgs e)
        {
            string url = "https://wwim.lanzouo.com/iMrQe32px92f";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        private async void QualcommDriverButton_Click(object sender, RoutedEventArgs e)
        {
            string url = "https://wwim.lanzouo.com/iOpKy32px8sf";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        private async void LibUSBDriverButton_Click(object sender, RoutedEventArgs e)
        {
            string url = "https://wwim.lanzouo.com/iBeCq32px8tg";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        private async void OPPODriverButton_Click(object sender, RoutedEventArgs e)
        {
            string url = "https://wwim.lanzouo.com/iFmMh32px9wf";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        private void CmdButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string platformToolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools");
                string cmdBatPath = Path.Combine(platformToolsPath, "CMD.bat");
                
                if (File.Exists(cmdBatPath))
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = cmdBatPath,
                        UseShellExecute = true,
                        WorkingDirectory = platformToolsPath
                    };
                    Process.Start(startInfo);
                }
                else
                {
                    ShowMessage($"未找到CMD.bat文件: {cmdBatPath}");
                }
            }
            catch (Exception ex)
            {
                ShowMessage($"打开CMD失败: {ex.Message}");
            }
        }

        private void DeviceManagerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "mmc.exe",
                    Arguments = "devmgmt.msc",
                    UseShellExecute = true,
                    Verb = "runas" // 以管理员权限运行
                };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                // 如果管理员权限失败，尝试普通权限
                try
                {
                    ProcessStartInfo fallbackInfo = new ProcessStartInfo
                    {
                        FileName = "mmc.exe",
                        Arguments = "devmgmt.msc",
                        UseShellExecute = true
                    };
                    Process.Start(fallbackInfo);
                }
                catch
                {
                }
            }
        }

        private void JoinQQGroupButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string qqGroupUrl = "https://qun.qq.com/universal-share/share?ac=1&authKey=0%2FYo9g8At%2BoIzEUeWHD8tfpvheTN1pl0WOB3d1%2F8s44oY8R46Dh1pqutgsCRujMC&busi_data=eyJncm91cENvZGUiOiIxNTk5NzI5NTMiLCJ0b2tlbiI6Ikptcnkrb1ZONUhxQXJzQitjVHNZMGRQZVJJM29sMmdZNXJpcXZJc21ZRU9qZHorNUE1V0owQ2E2ZXZwSU9ieDQiLCJ1aW4iOiIxMjI3MzYzMzQyIn0%3D&data=XJNVJexYjP62pdpl2XDhWErxIhCBqYk2NE3CSNiRmZy5v9PcLijj2T86aWtX5Wf8FRXlb4ma8yEnVOkzVIkjYw&svctype=4&tempid=h5_group_info";
                
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = qqGroupUrl,
                    UseShellExecute = true
                };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                ShowMessage($"打开QQ群链接失败: {ex.Message}");
            }
        }

        private async void FixAdbButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 弹出ROOT权限提示窗口
                MessageBoxResult result = System.Windows.MessageBox.Show(
                    "此功能需要ROOT权限才能正常工作。\n\n请确保您的设备已获取ROOT权限，并给予Shell ROOT权限\n\n是否继续执行修复操作？",
                    "ROOT权限提示",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                FixAdbButton.IsEnabled = false;
                var taskStopwatch = Stopwatch.StartNew();

                string? deviceSerial = await GetAuthorizedAdbDeviceSerialAsync();
                if (string.IsNullOrEmpty(deviceSerial))
                {
                    LogSimpleStatus("错误: 未检测到已授权的 ADB 设备");
                    return;
                }

                LogSimpleStatus($"已连接 {deviceSerial} | ADB");

                string rootOutput = await ExecuteAdbCommandWithOutput("shell su -c \"echo ROOT_OK\"");
                if (!rootOutput.Contains("ROOT_OK", StringComparison.OrdinalIgnoreCase))
                {
                    LogSimpleStatus("[ERROR]设备未授予Shell ROOT权限");
                    return;
                }

                // 依次执行ADB修复命令
                string[] commands = {
                    "shell su -c \"rm -rf /data/local/tmp/\"",
                    "shell su -c \"rm -rf /data/local/Tmp/\"",
                    "shell su -c \"mkdir -p /data/local/tmp/\"",
                    "shell su -c \"chmod 777 /data/local/tmp/\"",
                    "shell su -c \"chcon -R u:object_r:shell_data_file:s0 /data/local/tmp\""
                };

                for (int i = 0; i < commands.Length; i++)
                {
                    await ExecuteAdbCommand(commands[i]);
                    await Task.Delay(500); // 每个命令之间延迟500ms
                }

                LogSimpleStatus("ADB修复完成，重新打开投屏即可.");
                taskStopwatch.Stop();
                LogSimpleStatus($"任务结束，耗时{taskStopwatch.Elapsed.TotalSeconds:F1}秒.");
                System.Windows.MessageBox.Show("ADB异常修复完成！\n\n现在重新打开投屏即可", "修复完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"错误: ADB 运行环境修复失败 | {ex.Message}");
            }
            finally
            {
                FixAdbButton.IsEnabled = true;
            }
        }

        private async void EnableMiuiUsbButton_Click(object sender, RoutedEventArgs e)
        {
            // 显示ROOT权限提示
            var result = System.Windows.MessageBox.Show(
                "此功能需要ROOT权限才能正常工作，请先给予Shell ROOT权限\n\n是否继续执行强开小米USB安全设置？",
                "需要ROOT权限",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                EnableMiuiUsbButton.IsEnabled = false;
                var taskStopwatch = Stopwatch.StartNew();

                string? deviceSerial = await GetAuthorizedAdbDeviceSerialAsync();
                if (string.IsNullOrEmpty(deviceSerial))
                {
                    LogSimpleStatus("错误: 未检测到已授权的 ADB 设备");
                    return;
                }

                LogSimpleStatus($"已连接 {deviceSerial} | ADB");

                string rootOutput = await ExecuteAdbCommandWithOutput("shell su -c \"echo ROOT_OK\"");
                if (!rootOutput.Contains("ROOT_OK", StringComparison.OrdinalIgnoreCase))
                {
                    LogSimpleStatus("[ERROR]设备未授予Shell ROOT权限");
                    return;
                }

                // 执行shell脚本逻辑
                string[] commands = {
                    "shell \"echo '[1/4] 启用 USB 调试 (Security settings)'\"",
                    "shell \"su -c 'if [ \\\"$(getprop persist.security.adbinput)\\\" != \\\"1\\\" ]; then setprop persist.security.adbinput 1; echo \\\"成功：USB 调试已启用\\\"; else echo \\\"提示：USB 调试已启用，无需重复操作\\\"; fi'\"",
                    "shell \"echo '[2/4] 启用 Fastboot 模式'\"",
                    "shell \"su -c 'if [ \\\"$(getprop persist.fastboot.enable)\\\" != \\\"1\\\" ]; then setprop persist.fastboot.enable 1; echo \\\"成功：Fastboot 模式已启用\\\"; else echo \\\"提示：Fastboot 模式已启用，无需重复操作\\\"; fi'\"",
                    "shell \"echo '[3/4] 检查配置文件路径：/data/data/com.miui.securitycenter/shared_prefs/remote_provider_preferences.xml'\"",
                    "shell \"su -c 'XML_FILE=\\\"/data/data/com.miui.securitycenter/shared_prefs/remote_provider_preferences.xml\\\"; if [ -f \\\"$XML_FILE\\\" ]; then echo \\\"成功：配置文件已找到，开始修改配置\\\"; else echo \\\"错误：配置文件未找到，请确保设备已安装相关应用\\\" && exit 1; fi'\"",
                    "shell \"echo '修改配置：启用 USB 安装'\"",
                    "shell \"su -c 'XML_FILE=\\\"/data/data/com.miui.securitycenter/shared_prefs/remote_provider_preferences.xml\\\"; if grep -q \\\"security_adb_install_enable\\\" \\\"$XML_FILE\\\"; then sed -i \\\"/security_adb_install_enable/s/false/true/\\\" \\\"$XML_FILE\\\" && echo \\\"成功：USB 安装已启用\\\"; else sed -i \\\"3a \\\\\\\\    <boolean name=\\\\\\\"security_adb_install_enable\\\\\\\" value=\\\\\\\"true\\\\\\\" />\\\" \\\"$XML_FILE\\\" && echo \\\"成功：插入 USB 安装配置并启用\\\"; fi'\"",
                    "shell \"echo '修改配置：禁用安装拦截'\"",
                    "shell \"su -c 'XML_FILE=\\\"/data/data/com.miui.securitycenter/shared_prefs/remote_provider_preferences.xml\\\"; if grep -q \\\"permcenter_install_intercept_enabled\\\" \\\"$XML_FILE\\\"; then sed -i \\\"/permcenter_install_intercept_enabled/s/true/false/\\\" \\\"$XML_FILE\\\" && echo \\\"成功：安装拦截已禁用\\\"; else sed -i \\\"3a \\\\\\\\    <boolean name=\\\\\\\"permcenter_install_intercept_enabled\\\\\\\" value=\\\\\\\"false\\\\\\\" />\\\" \\\"$XML_FILE\\\" && echo \\\"成功：插入安装拦截配置并禁用\\\"; fi'\"",
                    "shell \"echo '校验配置修改结果：'\"",
                    "shell \"su -c 'XML_FILE=\\\"/data/data/com.miui.securitycenter/shared_prefs/remote_provider_preferences.xml\\\"; grep \\\"security_adb_install_enable\\\" \\\"$XML_FILE\\\"'\"",
                    "shell \"su -c 'XML_FILE=\\\"/data/data/com.miui.securitycenter/shared_prefs/remote_provider_preferences.xml\\\"; grep \\\"permcenter_install_intercept_enabled\\\" \\\"$XML_FILE\\\"'\"",
                    "shell \"echo '[4/4] 重启 com.miui.securitycenter.remote 进程'\"",
                    "shell \"su -c 'PROCESS_ID=$(pidof com.miui.securitycenter.remote); if [ -n \\\"$PROCESS_ID\\\" ]; then kill -9 \\\"$PROCESS_ID\\\" && echo \\\"成功：已重启 com.miui.securitycenter.remote 进程 (PID: $PROCESS_ID)\\\"; else echo \\\"提示：未找到 com.miui.securitycenter.remote 进程，可能不需要重启\\\"; fi'\"",
                    "shell \"echo '========================='\"",
                    "shell \"echo ' ------脚本执行完成 ------'\"",
                    "shell \"echo '========================='\""
                };

                for (int i = 0; i < commands.Length; i++)
                {
                    await ExecuteAdbCommand(commands[i]);
                    await Task.Delay(300); // 每个命令之间延迟300ms
                }

                LogSimpleStatus("强开成功，重启设备后生效.");
                taskStopwatch.Stop();
                LogSimpleStatus($"任务结束，耗时{taskStopwatch.Elapsed.TotalSeconds:F1}秒.");
                System.Windows.MessageBox.Show("小米USB安全设置强开完成！\n\n设置已成功修改，重启设备生效", "操作完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"错误: 小米 USB 安全设置启用失败 | {ex.Message}");
                System.Windows.MessageBox.Show($"操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                EnableMiuiUsbButton.IsEnabled = true;
            }
        }

        // ADB读取一加DDR版本
        private async void ReadOnePlusDdrButton_Click(object sender, RoutedEventArgs e)
        {
            ReadOnePlusDdrButton.IsEnabled = false;
            var taskStopwatch = Stopwatch.StartNew();

            try
            {
                string? deviceSerial = await GetAuthorizedAdbDeviceSerialAsync();
                if (string.IsNullOrEmpty(deviceSerial))
                {
                    LogSimpleStatus("错误: 未检测到已授权的 ADB 设备");
                    return;
                }

                LogSimpleStatus($"已连接 {deviceSerial} | ADB");

                // 执行getprop命令读取DDR类型
                string getDdrCommand = "shell getprop ro.boot.ddr_type";
                string ddrOutput = await ExecuteAdbCommandWithOutput(getDdrCommand);
                string ddrValue = ddrOutput?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(ddrValue))
                {
                    LogSimpleStatus("检测结果为：设备未定义DDR，非一加789系列.");
                }
                else if (ddrValue == "0")
                {
                    LogSimpleStatus("检测结果为：DDR4.");
                }
                else if (ddrValue == "1")
                {
                    LogSimpleStatus("检测结果为：DDR5.");
                }
                else
                {
                    LogSimpleStatus("检测结果为：设备未定义DDR，非一加789系列.");
                }

                taskStopwatch.Stop();
                LogSimpleStatus($"任务结束，耗时{taskStopwatch.Elapsed.TotalSeconds:F1}秒.");
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"错误: DDR 类型读取失败 | {ex.Message}");
            }
            finally
            {
                ReadOnePlusDdrButton.IsEnabled = true;
            }
        }

        // 强开基带调试端口
        private async void EnableDiagPortButton_Click(object sender, RoutedEventArgs e)
        {
            EnableDiagPortButton.IsEnabled = false;
            var taskStopwatch = Stopwatch.StartNew();

            try
            {
                string? deviceSerial = await GetAuthorizedAdbDeviceSerialAsync();
                if (string.IsNullOrEmpty(deviceSerial))
                {
                    LogSimpleStatus("错误: 未检测到已授权的 ADB 设备");
                    return;
                }

                LogSimpleStatus($"已连接 {deviceSerial} | ADB");

                // 申请Root权限
                LogSimpleStatus("检查ROOT权限.");
                string suCommand = "shell \"su -c 'echo Root权限获取成功'\"";
                string suOutput = await ExecuteAdbCommandWithOutput(suCommand);

                if (string.IsNullOrWhiteSpace(suOutput) || !suOutput.Contains("Root权限获取成功"))
                {
                    LogSimpleStatus("[ERROR]设备未授予Shell ROOT权限");
                    return;
                }

                // 开启Diag端口
                string diagCommand = "shell \"su -c 'setprop sys.usb.config diag,adb'\"";
                string diagOutput = await ExecuteAdbCommandWithOutput(diagCommand);

                if (CommandOutputIndicatesFailure(diagOutput))
                {
                    LogSimpleStatus($"错误: 基带调试端口启用失败 | {diagOutput.Trim()}");
                    return;
                }

                LogSimpleStatus("基带调试端口已开启.");
                taskStopwatch.Stop();
                LogSimpleStatus($"任务结束，耗时{taskStopwatch.Elapsed.TotalSeconds:F1}秒.");
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"错误: 基带调试端口启用失败 | {ex.Message}");
            }
            finally
            {
                EnableDiagPortButton.IsEnabled = true;
            }
        }

        private async Task<string?> GetAuthorizedAdbDeviceSerialAsync()
        {
            string deviceOutput = await ExecuteAdbCommandWithOutput("devices");
            var deviceMatch = Regex.Match(
                deviceOutput ?? string.Empty,
                @"(?m)^(\S+)\s+device\s*$",
                RegexOptions.IgnoreCase);
            return deviceMatch.Success ? deviceMatch.Groups[1].Value : null;
        }

        private static bool CommandOutputIndicatesFailure(string? output)
        {
            return !string.IsNullOrWhiteSpace(output) &&
                   (output.Contains("ERROR_DETECTED", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                    output.Contains("denied", StringComparison.OrdinalIgnoreCase));
        }

        // 切换文件传输模式按钮
        private async void SwitchToMtpButton_Click(object sender, RoutedEventArgs e)
        {
            SwitchToMtpButton.IsEnabled = false;
            var taskStopwatch = Stopwatch.StartNew();

            try
            {
                string? deviceSerial = await GetAuthorizedAdbDeviceSerialAsync();
                if (string.IsNullOrEmpty(deviceSerial))
                {
                    LogSimpleStatus("错误: 未检测到已授权的 ADB 设备");
                    return;
                }

                LogSimpleStatus($"已连接 {deviceSerial} | ADB");

                // 切换到MTP模式
                string mtpCommand = "shell svc usb setFunctions mtp";
                string mtpOutput = await ExecuteAdbCommandWithOutput(mtpCommand);

                if (CommandOutputIndicatesFailure(mtpOutput))
                {
                    LogSimpleStatus($"错误: MTP 模式切换失败 | {mtpOutput.Trim()}");
                    return;
                }

                LogSimpleStatus("操作完成 | USB 文件传输模式已启用");
                taskStopwatch.Stop();
                LogSimpleStatus($"任务结束，耗时{taskStopwatch.Elapsed.TotalSeconds:F1}秒.");
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"错误: MTP 模式切换失败 | {ex.Message}");
            }
            finally
            {
                SwitchToMtpButton.IsEnabled = true;
            }
        }

        private async void GenerateOcdtButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                GenerateOcdtButton.IsEnabled = false;
                SuperPackLogRichTextBox.Document.Blocks.Clear();
                AppendOcdtLog("开始生成 OCDT 文件...");

                // 收集参数
                string projIdText = OcdtProjIdTextBox.Text.Trim();
                if (string.IsNullOrEmpty(projIdText) || !int.TryParse(projIdText, out int projId))
                {
                    AppendOcdtLog("错误：请输入有效的数字 Project ID", System.Windows.Media.Brushes.Red);
                    return;
                }

                string platform = ((ComboBoxItem)OcdtSizeComboBox.SelectedItem).Content.ToString();
                string size = platform == "联发科" ? "8mb" : "128kb";
                string variant = ((ComboBoxItem)OcdtVariantComboBox.SelectedItem).Content.ToString();

                // 弹出文件夹选择对话框让用户选择保存的目录
                using (var folderDialog = new System.Windows.Forms.FolderBrowserDialog())
                {
                    folderDialog.Description = "请选择保存生成的 OCDT 镜像的目录";
                    if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    {
                        AppendOcdtLog("已取消生成。");
                        return;
                    }

                    // 在用户选择的目录下创建 violettoolbox_ocdt 文件夹
                    string saveDir = Path.Combine(folderDialog.SelectedPath, "violettoolbox_ocdt");
                    if (!Directory.Exists(saveDir))
                    {
                        Directory.CreateDirectory(saveDir);
                    }

                    string outputPath = Path.Combine(saveDir, $"violet_ocdt_{projId}_{size}.img");

                    await Task.Run(() =>
                    {
                        if (size == "8mb")
                        {
                            Generate8MbOcdt(projId, outputPath, variant, null, null);
                        }
                        else
                        {
                            Generate128KbOcdt(projId, outputPath, variant, null, null);
                        }
                        
                        Dispatcher.Invoke(() => AppendOcdtLog($"已生成: {outputPath}", System.Windows.Media.Brushes.Green));
                    });
                }
            }
            catch (Exception ex)
            {
                AppendOcdtLog($"发生异常：{ex.Message}", System.Windows.Media.Brushes.Red);
            }
            finally
            {
                GenerateOcdtButton.IsEnabled = true;
            }
        }

        private void AppendOcdtLog(string message, System.Windows.Media.Brush color = null)
        {
            var paragraph = new Paragraph(new Run($"[{DateTime.Now:HH:mm:ss}] {message}"));
            if (color != null)
            {
                paragraph.Foreground = color;
            }
            paragraph.Margin = new Thickness(0);
            SuperPackLogRichTextBox.Document.Blocks.Add(paragraph);
            SuperPackLogRichTextBox.ScrollToEnd();
        }

        #region OCDT Generator Logic (C# Port)
        
        private static readonly byte[] GEYIXUE_KEY = Encoding.ASCII.GetBytes("geyixue");

        private static readonly Dictionary<string, int> PROJECT_ID_MAP = new Dictionary<string, int>
        {
            // OnePlus
            {"LE2100", 20828}, {"LE2110", 19825}, {"PGKM10", 21861}, {"PHP110", 22823},
            {"PJE110", 23801}, {"PGZ110", 22801},
            // OPPO
            {"PDEM10", 19065}, {"PDEM30", 19066}, {"PDHM00", 19161}, {"PDPM00", 19015},
            {"PDRM00", 20135}, {"PDSM00", 20131}, {"PDYM20", 20001}, {"PECM20", 20041},
            {"PEDM00", 20061}, {"PEFM00", 20091}, {"PEHM00", 20121}, {"PELM00", 20151},
            {"PENM00", 20161}, {"PEQM00", 20181}, {"PESM10", 21091}, {"PEYM00", 21061},
            {"PFCM00", 21081}, {"PFGM00", 21041}, {"PFJM10", 21031}, {"PFTM20", 21102},
            {"PFVM10", 21037}, {"PGAM10", 21125}, {"PGBM10", 21127}, {"PGCM10", 4256},
            {"PGFM10", 21135}, {"PGJM10", 21143}, {"PHJ110", 22083}, {"PHM110", 22055},
            {"PJB110", 22087}, {"PJU110", 23054}, {"PJV110", 23081},
            // Realme
            {"RMX2117", 20613}, {"RMX3031", 20615}, {"RMX3370", 21619}, {"RMX3372", 21623},
            {"RMX3461", 21644}, {"RMX3560", 21641}, {"RMX3610", 22604}, {"RMX3823", 23603}
        };

        private static readonly Dictionary<string, int> MTK_WITH_OSIG = new Dictionary<string, int>
        {
            {"PDYM20", 20001}, {"PECM20", 20041}, {"PDSM00", 20131}, {"PELM00", 20151}, {"RMX2117", 20613}
        };

        private static readonly Dictionary<string, string> SPECIAL_CONFIG_DB = new Dictionary<string, string>
        {
            {"PGZ110", "67c3979696c256762fb2968757567656979687575676569796875756765697968757567656979687575676569796875756765697"},
            {"RMX3461", "be1397963f125676eed2968757567656979687575676569796875756765697968757567656979687575676569796875756765697"}
        };

        private byte[] HexStringToByteArray(string hex)
        {
            int numberChars = hex.Length;
            byte[] bytes = new byte[numberChars / 2];
            for (int i = 0; i < numberChars; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            return bytes;
        }

        private byte[] GeyixueEncrypt(byte[] data)
        {
            byte[] result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                int xored = data[i] ^ GEYIXUE_KEY[i % GEYIXUE_KEY.Length];
                result[i] = (byte)(((xored << 4) | (xored >> 4)) & 0xFF);
            }
            return result;
        }

        private byte[] GeyixueDecrypt(byte[] data)
        {
            byte[] result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                int rotated = ((data[i] >> 4) | (data[i] << 4)) & 0xFF;
                result[i] = (byte)(rotated ^ GEYIXUE_KEY[i % GEYIXUE_KEY.Length]);
            }
            return result;
        }

        private async void AnalyzeOcdtButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择OCDT镜像文件",
                Filter = "OCDT镜像 (*.img)|*.img|所有文件 (*.*)|*.*",
                FilterIndex = 1,
                CheckFileExists = true
            };

            if (openFileDialog.ShowDialog() != true)
            {
                return;
            }

            AnalyzeOcdtButton.IsEnabled = false;
            var taskStopwatch = Stopwatch.StartNew();

            try
            {
                var fileInfo = new FileInfo(openFileDialog.FileName);
                if (fileInfo.Length < 0x44)
                {
                    LogSimpleStatus("[ERROR]OCDT镜像长度不足，无法读取TDCO配置");
                    return;
                }

                if (fileInfo.Length > 64L * 1024 * 1024)
                {
                    LogSimpleStatus("[ERROR]所选文件超过64MB，不符合OCDT镜像特征");
                    return;
                }

                byte[] data = await File.ReadAllBytesAsync(openFileDialog.FileName);
                LogSimpleStatus($"已选择OCDT镜像 {fileInfo.Name} | {fileInfo.Length / 1024d:F1}KB");

                string magic = Encoding.ASCII.GetString(data, 0, 4);
                if (!string.Equals(magic, "TDCO", StringComparison.Ordinal))
                {
                    LogSimpleStatus("[ERROR]无文件头，项目号为空，无效的OCDT");
                    return;
                }

                ushort version = BitConverter.ToUInt16(data, 4);
                uint dataOffset = BitConverter.ToUInt32(data, 8);
                uint dataLength = BitConverter.ToUInt32(data, 12);
                ulong configEnd = (ulong)dataOffset + dataLength;
                if (dataOffset > (uint)data.Length || configEnd > (ulong)data.Length)
                {
                    LogSimpleStatus("[ERROR]TDCO配置偏移或长度超出文件范围");
                    return;
                }

                byte[] encryptedConfig = new byte[52];
                Buffer.BlockCopy(data, 0x10, encryptedConfig, 0, encryptedConfig.Length);
                byte[] decryptedConfig = GeyixueDecrypt(encryptedConfig);

                int projId1 = decryptedConfig[0] | (decryptedConfig[1] << 8);
                int projId2 = decryptedConfig[4] | (decryptedConfig[5] << 8);
                int projId3 = decryptedConfig[8] | (decryptedConfig[9] << 8);
                bool configAreaBlank = encryptedConfig.All(value => value == 0x00) ||
                                       encryptedConfig.All(value => value == 0xFF);
                bool projectIdEmpty = configAreaBlank ||
                                      (projId1 == 0 && projId2 == 0 && projId3 == 0);
                bool projectIdsMatch = projId1 == projId2 && projId1 == projId3;

                string variant = "standard";
                if (decryptedConfig[2] == 0x02)
                {
                    variant = "fill02";
                }
                else if (decryptedConfig.Skip(22).Any(value => value != 0) &&
                         decryptedConfig.Skip(22).Take(GEYIXUE_KEY.Length).SequenceEqual(GEYIXUE_KEY))
                {
                    variant = "geyixue_fill";
                }
                if (!projectIdsMatch)
                {
                    variant = "mixed_projid";
                }

                bool is8Mb = fileInfo.Length > 1024 * 1024;
                bool hasOsig = data.Length >= 0x1200 &&
                               Encoding.ASCII.GetString(data, 0x1000, 4) == "OSIG";
                uint? osigVersion = hasOsig ? BitConverter.ToUInt32(data, 0x1004) : null;
                string fileType = is8Mb
                    ? hasOsig ? "8MB MTK + OSIG" : "8MB MTK（无OSIG）"
                    : "128KB 高通";

                LogSimpleStatus($"TDCO信息 | 版本 {version} | 偏移 0x{dataOffset:X} | 长度 {dataLength}");
                LogSimpleStatus(configAreaBlank
                    ? "项目号 | 空"
                    : $"项目号 | {projId1}, {projId2}, {projId3}");
                LogSimpleStatus($"项目号一致性 | {(projectIdsMatch ? "一致" : "不一致")}");
                LogSimpleStatus($"配置变体 | {variant}");
                LogSimpleStatus($"文件类型 | {fileType}");

                var matchedDevice = PROJECT_ID_MAP.FirstOrDefault(item => item.Value == projId1);
                if (!string.IsNullOrEmpty(matchedDevice.Key))
                {
                    LogSimpleStatus($"匹配设备 | {matchedDevice.Key} | Project ID {matchedDevice.Value}");
                    if (MTK_WITH_OSIG.ContainsKey(matchedDevice.Key))
                    {
                        LogSimpleStatus("警告: 该机型属于旧款MTK设备，需要有效OSIG签名");
                    }
                }
                else
                {
                    LogSimpleStatus($"匹配设备 | 未收录 | Project ID {projId1}");
                }

                if (hasOsig)
                {
                    byte[] deviceId = new byte[16];
                    Buffer.BlockCopy(data, 0x1010, deviceId, 0, deviceId.Length);

                    byte[] md5Bytes = new byte[32];
                    Buffer.BlockCopy(data, 0x1020, md5Bytes, 0, md5Bytes.Length);
                    string md5Text = Encoding.ASCII.GetString(md5Bytes).TrimEnd('\0');
                    bool md5IsText = md5Text.All(character => character >= 32 && character <= 126);

                    byte[] tdcoCopy = new byte[0x44];
                    Buffer.BlockCopy(data, 0x1040, tdcoCopy, 0, tdcoCopy.Length);
                    bool hasTdcoCopy = tdcoCopy.Any(value => value != 0);

                    byte[] signature = new byte[0x100];
                    Buffer.BlockCopy(data, 0x1100, signature, 0, signature.Length);
                    bool hasRsaSignature = signature.Any(value => value != 0);

                    bool hasBackupOsig = data.Length >= 0x2200 &&
                                         Encoding.ASCII.GetString(data, 0x2000, 4) == "OSIG";

                    LogSimpleStatus($"OSIG信息 | 版本 {osigVersion} | Device ID {Convert.ToHexString(deviceId).ToLowerInvariant()}");
                    LogSimpleStatus($"OSIG MD5 | {(md5IsText && !string.IsNullOrWhiteSpace(md5Text) ? md5Text : Convert.ToHexString(md5Bytes).ToLowerInvariant())}");
                    LogSimpleStatus($"TDCO副本 | {(hasTdcoCopy ? "存在" : "全零")}");
                    LogSimpleStatus($"RSA签名 | {(hasRsaSignature ? "存在（256字节）" : "不存在")}");
                    LogSimpleStatus($"备份OSIG | {(hasBackupOsig ? "存在" : "不存在")}");
                }
                else
                {
                    LogSimpleStatus("OSIG信息 | 不存在");
                }

                if (projectIdEmpty)
                {
                    LogSimpleStatus("[ERROR]检测结果 | 项目号为空，OCDT无效");
                }
                else
                {
                    LogSimpleStatus("检测结果 | 项目号非空，OCDT有效");
                }

                taskStopwatch.Stop();
                LogSimpleStatus($"任务结束，耗时{taskStopwatch.Elapsed.TotalSeconds:F1}秒.");
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"[ERROR]OCDT分析失败 | {ex.Message}");
            }
            finally
            {
                AnalyzeOcdtButton.IsEnabled = true;
            }
        }

        private byte[] GenerateOcdtConfig(int projIdNum, string strId, string variant = "standard")
        {
            if (!string.IsNullOrEmpty(strId) && SPECIAL_CONFIG_DB.ContainsKey(strId))
            {
                return HexStringToByteArray(SPECIAL_CONFIG_DB[strId]);
            }

            if (variant == "mixed_projid")
            {
                foreach (var kvp in PROJECT_ID_MAP)
                {
                    if (kvp.Value == projIdNum && SPECIAL_CONFIG_DB.ContainsKey(kvp.Key))
                    {
                        return HexStringToByteArray(SPECIAL_CONFIG_DB[kvp.Key]);
                    }
                }
            }

            byte[] plain = new byte[52];
            byte lo = (byte)(projIdNum & 0xFF);
            byte hi = (byte)((projIdNum >> 8) & 0xFF);

            plain[0] = lo; plain[1] = hi;
            plain[4] = lo; plain[5] = hi;
            plain[8] = lo; plain[9] = hi;

            if (variant == "fill02")
            {
                plain[2] = 0x02;
                plain[6] = 0x02;
                plain[10] = 0x02;
            }
            else if (variant == "geyixue_fill")
            {
                for (int i = 22; i < 52; i++)
                {
                    plain[i] = GEYIXUE_KEY[i % GEYIXUE_KEY.Length];
                }
            }

            return GeyixueEncrypt(plain);
        }

        private string Generate8MbOcdt(int projIdNum, string outputPath, string variant = "standard", byte[] osigBackup = null, string strId = null)
        {
            byte[] data = new byte[8 * 1024 * 1024];

            // TDCO Header
            Buffer.BlockCopy(Encoding.ASCII.GetBytes("TDCO"), 0, data, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)0x0001), 0, data, 4, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)0x0000), 0, data, 6, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((uint)0x10), 0, data, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((uint)0x34), 0, data, 12, 4);

            byte[] config = GenerateOcdtConfig(projIdNum, strId, variant);
            Buffer.BlockCopy(config, 0, data, 0x10, config.Length);

            bool needsOsig = false;
            if (!string.IsNullOrEmpty(strId) && MTK_WITH_OSIG.ContainsKey(strId))
            {
                needsOsig = true;
            }
            else if (MTK_WITH_OSIG.ContainsValue(projIdNum))
            {
                needsOsig = true;
            }

            if (needsOsig)
            {
                if (osigBackup != null)
                {
                    if (osigBackup.Length >= 0x1200)
                    {
                        Buffer.BlockCopy(osigBackup, 0x1000, data, 0x1000, 0x200);
                        Dispatcher.Invoke(() => AppendOcdtLog("  使用备份 OSIG 数据"));
                    }
                    else
                    {
                        Dispatcher.Invoke(() => AppendOcdtLog("  警告: OSIG 备份数据不足", System.Windows.Media.Brushes.Orange));
                    }
                }
                else
                {
                    byte[] osigBlock = new byte[0x200];
                    Buffer.BlockCopy(Encoding.ASCII.GetBytes("OSIG"), 0, osigBlock, 0, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes((uint)0), 0, osigBlock, 4, 4);
                    Buffer.BlockCopy(HexStringToByteArray("30000000000000000000000000000000"), 0, osigBlock, 0x10, 16);
                    Buffer.BlockCopy(osigBlock, 0, data, 0x1000, osigBlock.Length);
                    Dispatcher.Invoke(() => {
                        AppendOcdtLog("  生成空 OSIG (Version=0, 无RSA签名)");
                        AppendOcdtLog("  注意: 此设备为旧款MTK，无真实签名可能无法使用", System.Windows.Media.Brushes.Orange);
                    });
                }
            }

            File.WriteAllBytes(outputPath, data);
            return outputPath;
        }

        private string Generate128KbOcdt(int projIdNum, string outputPath, string variant = "standard", byte[] osigData = null, string strId = null)
        {
            byte[] data = new byte[128 * 1024];

            // TDCO Header
            Buffer.BlockCopy(Encoding.ASCII.GetBytes("TDCO"), 0, data, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)0x0001), 0, data, 4, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)0x0000), 0, data, 6, 2);
            Buffer.BlockCopy(BitConverter.GetBytes((uint)0x10), 0, data, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes((uint)0x34), 0, data, 12, 4);

            byte[] config = GenerateOcdtConfig(projIdNum, strId, variant);
            Buffer.BlockCopy(config, 0, data, 0x10, config.Length);

            byte[] osigBlock = new byte[512];
            if (osigData != null)
            {
                int copyLen = Math.Min(osigData.Length, 512);
                Buffer.BlockCopy(osigData, 0, osigBlock, 0, copyLen);
            }
            else
            {
                Buffer.BlockCopy(Encoding.ASCII.GetBytes("OSIG"), 0, osigBlock, 0, 4);
                Buffer.BlockCopy(HexStringToByteArray("30000000000000000000000000000000"), 0, osigBlock, 0x10, 16);
                Buffer.BlockCopy(Encoding.ASCII.GetBytes("TDCO"), 0, osigBlock, 0x50, 4);
                Buffer.BlockCopy(BitConverter.GetBytes((uint)0x0001), 0, osigBlock, 0x54, 4);
                Buffer.BlockCopy(BitConverter.GetBytes((uint)0x10), 0, osigBlock, 0x58, 4);
                Buffer.BlockCopy(BitConverter.GetBytes((uint)0x34), 0, osigBlock, 0x5C, 4);
            }

            Buffer.BlockCopy(osigBlock, 0, data, 0x1000, 512);
            Buffer.BlockCopy(osigBlock, 0, data, 0x2000, 512);

            File.WriteAllBytes(outputPath, data);
            return outputPath;
        }

        #endregion

        private async void ScreenOffButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteAdbCommand("shell input keyevent 223");
                ShowMessage("屏幕已关闭");
            }
            catch (Exception ex)
            {
                ShowMessage($"屏幕关闭失败: {ex.Message}");
            }
        }

        private async void VolumeMuteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteAdbCommand("shell input keyevent 164");
                ShowMessage("静音键已发送");
            }
            catch (Exception ex)
            {
                ShowMessage($"静音失败: {ex.Message}");
            }
        }

        private async void ScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ExecuteAdbCommand("shell screencap -p /sdcard/screenshot.png");
                await ExecuteAdbCommand("pull /sdcard/screenshot.png");
                ShowMessage("截屏已保存到当前目录");
            }
            catch (Exception ex)
            {
                ShowMessage($"截屏失败: {ex.Message}");
            }
        }

        private async Task ExecuteAdbCommand(string command)
        {
            await Task.Run(() =>
            {
                string adbPath = GetToolPath("adb.exe");
                string selectedSerial = GetSelectedDeviceSerial();
                
                // 如果有选中的设备序列号，添加 -s 参数
                string arguments = command;
                if (!string.IsNullOrEmpty(selectedSerial))
                {
                    arguments = $"-s {selectedSerial} {command}";
                }
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit();
            });
        }

        private async Task ExecuteFastbootCommand(string command)
        {
            try
            {
                string programDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string fastbootPath = Path.Combine(programDirectory, "platform-tools", "fastboot.exe");
                string selectedSerial = GetSelectedDeviceSerial();
                
                // 如果有选中的设备序列号，添加 -s 参数
                string arguments = command;
                if (!string.IsNullOrEmpty(selectedSerial))
                {
                    arguments = $"-s {selectedSerial} {command}";
                }
                
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = fastbootPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (Process process = new Process())
                {
                    process.StartInfo = startInfo;
                    process.Start();

                    // 设置10秒超时
                    if (await Task.Run(() => process.WaitForExit(10000)))
                    {
                        string output = await process.StandardOutput.ReadToEndAsync();
                        string error = await process.StandardError.ReadToEndAsync();
                        
                    }
                    else
                    {
                        process.Kill();
                    }
                }
            }
            catch (Exception ex)
            {
            }
        }

        private async Task CheckDeviceStatus(bool waitForCurrentDetection = false)
        {
            // 防止重复检测
            if (!_isDeviceDetectionEnabled) return;

            bool detectionLockTaken;
            if (waitForCurrentDetection)
            {
                await _deviceDetectionLock.WaitAsync();
                detectionLockTaken = true;
            }
            else
            {
                detectionLockTaken = _deviceDetectionLock.Wait(0);
            }
            if (!detectionLockTaken) return;

            if (!_isDeviceDetectionEnabled)
            {
                _deviceDetectionLock.Release();
                return;
            }

            int detectionVersion = _deviceDetectionVersion;
            
            string status;
            string connectionType;
            
            // 不显示"正在检测中"状态，保持界面静默直到检测完成
            
            try
            {
                string adbPath = GetToolPath("adb.exe");
                string fastbootPath = GetToolPath("fastboot.exe");

                // 检查工具是否存在
                if (!File.Exists(adbPath) && !File.Exists(fastbootPath))
                {
                    status = "未连接";
                    connectionType = "--";
                    
                    string windowsVersion = await GetWindowsVersionAsync();
                    if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;
                    if (HasDeviceInfoChanged(status, connectionType, "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", windowsVersion))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            UpdateDeviceInfoUI(status, connectionType, "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", windowsVersion);
                            if (BuildDateText != null) BuildDateText.Text = "--";
                        });
                        UpdateLastDeviceInfo(status, connectionType, "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", windowsVersion);
                    }
                    return;
                }

                // 并行执行ADB和Fastboot设备检测
                var adbTask = GetCommandOutput(adbPath, "devices");
                var fastbootTask = GetCommandOutput(fastbootPath, "devices");
                
                // 等待两个任务完成
                var results = await Task.WhenAll(adbTask, fastbootTask);
                if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;

                string adbDevicesOutput = results[0];
                string fastbootDevicesOutput = results[1];
                
                bool hasAdbDevice = adbDevicesOutput.Contains("\tdevice");
                bool hasFastbootDevice = fastbootDevicesOutput.Contains("fastboot");
                
                // 如果两种设备都没有连接
                if (!hasAdbDevice && !hasFastbootDevice)
                {
                    status = "未连接";
                    connectionType = "等待设备连接...";
                    
                    // 清空设备序列号列表
                    Dispatcher.Invoke(() =>
                    {
                        if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;
                        DeviceSerials.Clear();
                    });
                    
                    string windowsVersion = await GetWindowsVersionAsync();
                    if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;
                    if (HasDeviceInfoChanged(status, connectionType, "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", windowsVersion))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            UpdateDeviceInfoUI(status, connectionType, "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", windowsVersion);
                            if (BuildDateText != null) BuildDateText.Text = "--";
                        });
                        UpdateLastDeviceInfo(status, connectionType, "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", windowsVersion);
                    }
                    return;
                }

                // 收集所有设备序列号（ADB和Fastboot）
                var allDeviceSerials = new List<string>();
                
                // 收集ADB设备序列号
                if (hasAdbDevice)
                {
                    var lines = adbDevicesOutput.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Contains("\tdevice"))
                        {
                            var serial = line.Split('\t')[0].Trim();
                            allDeviceSerials.Add($"{serial} (ADB)");
                        }
                    }
                }
                
                // 收集Fastboot设备序列号
                if (hasFastbootDevice)
                {
                    var lines = fastbootDevicesOutput.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Contains("fastboot"))
                        {
                            var serial = line.Split('\t')[0].Trim();
                            allDeviceSerials.Add($"{serial} (Fastboot)");
                        }
                    }
                }
                
                // 更新设备序列号列表
                if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;
                Dispatcher.Invoke(() =>
                {
                    if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;

                    // 保存当前选中的设备序列号
                    string currentSelectedSerial = MultiDeviceComboBox.SelectedItem as string;
                    
                    DeviceSerials.Clear();
                    foreach (var serial in allDeviceSerials)
                    {
                        DeviceSerials.Add(serial);
                    }
                    
                    // 尝试恢复之前选中的设备
                    if (!string.IsNullOrEmpty(currentSelectedSerial) && DeviceSerials.Contains(currentSelectedSerial))
                    {
                        MultiDeviceComboBox.SelectedItem = currentSelectedSerial;
                    }
                    // 如果之前选中的设备不存在或没有选中项，选中第一个
                    else if (MultiDeviceComboBox.SelectedItem == null && DeviceSerials.Count > 0)
                    {
                        MultiDeviceComboBox.SelectedIndex = 0;
                    }
                });

                // 根据当前选中的设备显示详细信息
                string selectedDevice = "";
                Dispatcher.Invoke(() =>
                {
                    selectedDevice = MultiDeviceComboBox.SelectedItem as string ?? "";
                });

                if (hasAdbDevice && (selectedDevice.Contains("(ADB)") || !selectedDevice.Contains("(Fastboot)")))
                {
                    // 获取选中的ADB设备序列号
                    string deviceSerial = "--";
                    if (selectedDevice.Contains("(ADB)"))
                    {
                        deviceSerial = selectedDevice.Replace(" (ADB)", "").Trim();
                    }
                    else
                    {
                        // 如果没有选中特定设备，使用第一个ADB设备
                        var lines = adbDevicesOutput.Split('\n');
                        foreach (var line in lines)
                        {
                            if (line.Contains("\tdevice"))
                            {
                                deviceSerial = line.Split('\t')[0].Trim();
                                break;
                            }
                        }
                    }
                    
                    // 并行获取设备信息（使用-s参数指定设备）
                    var deviceModelTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell getprop ro.product.model");
                    var deviceCodeTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell getprop ro.product.device");
                    var androidVersionTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell getprop ro.build.version.release");
                    var unlockStatusTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell getprop ro.boot.unlocked");
                    var flashLockedTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell getprop ro.boot.flash.locked");
                    var slotSuffixTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell getprop ro.boot.slot_suffix");
                    var selinuxStatusTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell getenforce");
                    var kernelVersionTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell uname -r");
                    var cpuInfoTask = GetCommandOutput(adbPath, $"-s {deviceSerial} shell cat /proc/cpuinfo");
                    
                    // 等待所有任务完成
                    var deviceInfoResults = await Task.WhenAll(
                        deviceModelTask, deviceCodeTask, androidVersionTask, 
                        unlockStatusTask, slotSuffixTask, selinuxStatusTask, kernelVersionTask, flashLockedTask, cpuInfoTask
                    );
                    if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;
                    
                    string deviceModel = deviceInfoResults[0];
                    string deviceCode = deviceInfoResults[1];
                    string androidVersion = deviceInfoResults[2];
                    string unlockStatus = deviceInfoResults[3];
                    string slotSuffix = deviceInfoResults[4];
                    string selinuxStatus = deviceInfoResults[5];
                    string kernelVersion = deviceInfoResults[6];
                    string flashLocked = deviceInfoResults[7];
                    string cpuInfo = deviceInfoResults[8];

                    string procVersion = await GetCommandOutput(adbPath, $"-s {deviceSerial} shell cat /proc/version");
                    if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;

                    string buildDate = ExtractBuildDateFromProcVersion(procVersion).Trim();
                    if (string.IsNullOrEmpty(buildDate)) buildDate = "--";
                    if (buildDate != lastBuildDate)
                    {
                        Dispatcher.Invoke(() => { if (BuildDateText != null) BuildDateText.Text = buildDate; });
                        lastBuildDate = buildDate;
                    }

                    // 解析电池信息
                    string batteryOutput = await GetCommandOutput(adbPath, $"-s {deviceSerial} shell dumpsys battery");
                    if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;
                    UpdateBatteryFromDumpsysOutput(batteryOutput, detectionVersion);
                    
                    // 解析CPU制造商和代号
                    string cpuManufacturer = "--";
                    string cpuCodeName = "--";
                    if (!string.IsNullOrEmpty(cpuInfo))
                    {
                        if (cpuInfo.Contains("MediaTek"))
                        {
                            cpuManufacturer = "联发科";
                        }
                        else if (cpuInfo.Contains("Qualcomm"))
                        {
                            cpuManufacturer = "高通骁龙";
                        }
                        
                        // 解析CPU代号 - 从Hardware行提取SM或MT开头的代号
                        var cpuInfoLines = cpuInfo.Split('\n');
                        foreach (var cpuLine in cpuInfoLines)
                        {
                            if (cpuLine.StartsWith("Hardware", StringComparison.OrdinalIgnoreCase))
                            {
                                var hardwareLine = cpuLine.Trim();
                                // 使用正则表达式匹配SM后跟数字或MT后跟数字的模式
                                var match = System.Text.RegularExpressions.Regex.Match(hardwareLine, @"(SM\d+|MT\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                if (match.Success)
                                {
                                    cpuCodeName = match.Value.ToUpper();
                                }
                                break;
                            }
                        }
                    }
                    
                    // 使用多种方法检测解锁状态
                    string unlockText;
                    string trimmedUnlockStatus = unlockStatus.Trim();
                    string trimmedFlashLocked = flashLocked.Trim();
                    
                    // 调试输出：显示实际获取到的值
                    System.Diagnostics.Debug.WriteLine($"ro.boot.unlocked原始值: '{unlockStatus}'");
                    System.Diagnostics.Debug.WriteLine($"ro.boot.unlocked修剪后: '{trimmedUnlockStatus}'");
                    System.Diagnostics.Debug.WriteLine($"ro.boot.flash.locked原始值: '{flashLocked}'");
                    System.Diagnostics.Debug.WriteLine($"ro.boot.flash.locked修剪后: '{trimmedFlashLocked}'");
                    
                    // 优先使用ro.boot.unlocked
                    if (trimmedUnlockStatus == "1")
                    {
                        unlockText = "已解锁";
                    }
                    else if (trimmedUnlockStatus == "0")
                    {
                        unlockText = "未解锁";
                    }
                    // 如果ro.boot.unlocked不可用，使用ro.boot.flash.locked
                    else if (trimmedFlashLocked == "0")
                    {
                        unlockText = "已解锁";
                    }
                    else if (trimmedFlashLocked == "1")
                    {
                        unlockText = "未解锁";
                    }
                    else
                    {
                        // 两个属性都不可用
                        unlockText = "--";
                    }
                    
                    string slotText = slotSuffix.Trim() == "_a" ? "A槽位" :
                                     slotSuffix.Trim() == "_b" ? "B槽位" : "--";
                    string selinuxText = NormalizeSelinuxStatus(selinuxStatus);
                    
                    status = "已连接";
                    connectionType = "系统";
                    
                    string trimmedModel = deviceModel.Trim();
                    string trimmedCode = deviceCode.Trim();
                    string trimmedAndroidVersion = androidVersion.Trim();
                    string trimmedKernelVersion = kernelVersion.Trim();
                    
                    string windowsVersion = await GetWindowsVersionAsync();
                    if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;
                    if (HasDeviceInfoChanged(status, connectionType, deviceSerial, trimmedModel, trimmedCode, trimmedAndroidVersion, unlockText, slotText, selinuxText, trimmedKernelVersion, cpuManufacturer, cpuCodeName, windowsVersion))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            UpdateDeviceInfoUI(
                                status, 
                                connectionType, 
                                deviceSerial,
                                trimmedModel, 
                                trimmedCode, 
                                trimmedAndroidVersion, 
                                unlockText, 
                                slotText, 
                                selinuxText,
                                trimmedKernelVersion,
                                cpuManufacturer,
                                cpuCodeName,
                                windowsVersion
                            );
                        });
                        UpdateLastDeviceInfo(status, connectionType, deviceSerial, trimmedModel, trimmedCode, trimmedAndroidVersion, unlockText, slotText, selinuxText, trimmedKernelVersion, cpuManufacturer, cpuCodeName, windowsVersion);
                    }
                    return;
                }

                // 处理Fastboot设备（使用前面已检查的结果）
                else if (hasFastbootDevice && selectedDevice.Contains("(Fastboot)"))
                {
                    // 获取选中的Fastboot设备序列号
                    string deviceSerial = selectedDevice.Replace(" (Fastboot)", "").Trim();
                    
                    // 并行获取Fastboot设备信息（使用-s参数指定设备）
                    var productInfoTask = GetCommandOutput(fastbootPath, $"-s {deviceSerial} getvar product");
                    var unlockInfoTask = GetCommandOutput(fastbootPath, $"-s {deviceSerial} getvar unlocked");
                    var deviceInfoTask = GetCommandOutput(fastbootPath, $"-s {deviceSerial} oem device-info");
                    var slotInfoTask = GetCommandOutput(fastbootPath, $"-s {deviceSerial} getvar current-slot");
                    
                    // 等待所有任务完成
                    var fastbootInfoResults = await Task.WhenAll(
                        productInfoTask, unlockInfoTask, deviceInfoTask, slotInfoTask
                    );
                    if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;
                    
                    string productInfo = fastbootInfoResults[0];
                    string unlockInfo = fastbootInfoResults[1];
                    string deviceInfo = fastbootInfoResults[2];
                    string slotInfo = fastbootInfoResults[3];
                    
                    string productName = ExtractFastbootVar(productInfo, "product");
                    string unlockStatus = ExtractFastbootVar(unlockInfo, "unlocked");
                    string currentSlot = ExtractFastbootVar(slotInfo, "current-slot");
                    
                    // 调试输出：显示fastboot获取到的值
                    System.Diagnostics.Debug.WriteLine($"fastboot oem device-info输出: '{deviceInfo}'");
                    System.Diagnostics.Debug.WriteLine($"fastboot getvar unlocked输出: '{unlockInfo}'");
                    System.Diagnostics.Debug.WriteLine($"提取的unlocked值: '{unlockStatus}'");
                    
                    // 优先使用 fastboot oem device-info 检测解锁状态
                    string unlockText = "--";
                    if (deviceInfo.Contains("Device unlocked: true"))
                    {
                        unlockText = "已解锁";
                        System.Diagnostics.Debug.WriteLine("使用device-info检测到已解锁");
                    }
                    else if (deviceInfo.Contains("Device unlocked: false"))
                    {
                        unlockText = "未解锁";
                        System.Diagnostics.Debug.WriteLine("使用device-info检测到未解锁");
                    }
                    else
                    {
                        // 回退到 getvar unlocked 方法
                        System.Diagnostics.Debug.WriteLine("device-info方法失败，回退到getvar方法");
                        unlockText = unlockStatus == "yes" ? "已解锁" :
                                    unlockStatus == "no" ? "未解锁" : "--";
                        System.Diagnostics.Debug.WriteLine($"最终解锁状态: '{unlockText}'");
                    }
                    
                    string slotText = currentSlot == "a" ? "A槽位" :
                                     currentSlot == "b" ? "B槽位" : "--";
                    
                    status = "已连接";
                    connectionType = "Fastboot";
                    
                    string windowsVersion = await GetWindowsVersionAsync();
                    if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;
                    if (HasDeviceInfoChanged(status, connectionType, deviceSerial, productName, productName, "--", unlockText, slotText, "--", "--", "--", "--", windowsVersion))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            UpdateDeviceInfoUI(
                                status, 
                                connectionType, 
                                deviceSerial,
                                productName, 
                                productName, 
                                "--", 
                                unlockText, 
                                slotText, 
                                "--",
                                "--",
                                "--",
                                "--",
                                windowsVersion
                            );
                            if (BuildDateText != null) BuildDateText.Text = "--";
                        });
                        UpdateLastDeviceInfo(status, connectionType, deviceSerial, productName, productName, "--", unlockText, slotText, "--", "--", "--", "--", windowsVersion);
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;

                status = $"检测失败: {ex.Message}";
                connectionType = "错误";
                
                string windowsVersion = await GetWindowsVersionAsync();
                if (!IsDeviceDetectionCycleCurrent(detectionVersion)) return;
                if (HasDeviceInfoChanged(status, connectionType, "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", windowsVersion))
                {
                    Dispatcher.Invoke(() =>
                    {
                        UpdateDeviceInfoUI(status, connectionType, "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", windowsVersion);
                        if (BuildDateText != null) BuildDateText.Text = "--";
                    });
                    UpdateLastDeviceInfo(status, connectionType, "--", "--", "--", "--", "--", "--", "--", "--", "--", "--", windowsVersion);
                }
            }
            finally
            {
                _deviceDetectionLock.Release();
            }
        }

        private string NormalizeSelinuxStatus(string rawStatus)
        {
            string trimmedStatus = rawStatus.Trim();
            if (trimmedStatus.Equals("Enforcing", StringComparison.OrdinalIgnoreCase))
            {
                return "严格模式";
            }

            if (trimmedStatus.Equals("Permissive", StringComparison.OrdinalIgnoreCase))
            {
                return "宽容模式";
            }

            if (trimmedStatus.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
            {
                return "已关闭";
            }

            return "--";
        }

        private void UpdateSelinuxStatusColor(string selinuxStatus)
        {
            if (SelinuxStatusText == null)
            {
                return;
            }

            if (selinuxStatus == "严格模式")
            {
                SelinuxStatusText.Foreground = System.Windows.Media.Brushes.Green;
            }
            else if (selinuxStatus == "宽容模式")
            {
                SelinuxStatusText.Foreground = System.Windows.Media.Brushes.Red;
            }
            else if (selinuxStatus == "已关闭")
            {
                SelinuxStatusText.Foreground = System.Windows.Media.Brushes.DarkOrange;
            }
            else
            {
                SelinuxStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243));
            }
        }

        private void UpdateDeviceInfoUI(string status, string connectionType, string serial, string model, string code, string androidVersion, string unlockStatus, string abPartition, string selinuxStatus, string kernelVersion, string cpuManufacturer, string cpuCodeName, string windowsVersion)
        {
            SetLocalizedText(DeviceStatusText, status);
            SetLocalizedText(ConnectionTypeText, connectionType);
            DeviceSerialText.Text = serial;
            DeviceModelText.Text = model;
            DeviceCodeText.Text = code;
            AndroidVersionText.Text = androidVersion;
            SetLocalizedText(UnlockStatusText, unlockStatus);
            SetLocalizedText(ABPartitionText, abPartition);
            SetLocalizedText(SelinuxStatusText, selinuxStatus);
            KernelVersionText.Text = kernelVersion;
            CpuManufacturerText.Text = cpuManufacturer;
            CpuCodeNameText.Text = cpuCodeName;
            WindowsVersionText.Text = windowsVersion;
            UpdateSelinuxStatusColor(selinuxStatus);
            
            // 根据CPU代号更新CPU名称
            CpuNameText.Text = GetCpuNameByCode(cpuCodeName);
            
            // 更新版本信息
            if (status == "已连接" && connectionType == "系统" && !string.IsNullOrEmpty(code) && code != "--")
            {
                _ = UpdateVersionInfoAsync(code, _deviceDetectionVersion);
            }
            else
            {
                VersionInfoText.Text = "--";
            }
            
            // 更新底部的设备类型显示文本
            if (BottomConnectionTypeText != null)
            {
                SetLocalizedText(BottomConnectionTypeText, connectionType);
            }
            UpdateBottomConnectionStatusIndicator(status, connectionType);
            
            // 更新状态文字颜色
            UpdateStatusTextColor(status, connectionType);
            
            // 更新分区操作按钮状态
            UpdatePartitionButtonStates();
            UpdateXiaomiScriptOnlyOptionsState();
        }
        
        private bool HasDeviceInfoChanged(string status, string connectionType, string serial, string model, string code, string androidVersion, string unlockStatus, string abPartition, string selinuxStatus, string kernelVersion, string cpuManufacturer, string cpuCodeName, string windowsVersion)
        {
            return lastDeviceStatus != status ||
                   lastConnectionType != connectionType ||
                   lastDeviceSerial != serial ||
                   lastDeviceModel != model ||
                   lastDeviceCode != code ||
                   lastAndroidVersion != androidVersion ||
                   lastUnlockStatus != unlockStatus ||
                   lastABPartition != abPartition ||
                   lastSelinuxStatus != selinuxStatus ||
                   lastKernelVersion != kernelVersion ||
                   lastCpuManufacturer != cpuManufacturer ||
                   lastCpuCodeName != cpuCodeName ||
                   lastWindowsVersion != windowsVersion;
        }
        
        private void UpdateLastDeviceInfo(string status, string connectionType, string serial, string model, string code, string androidVersion, string unlockStatus, string abPartition, string selinuxStatus, string kernelVersion, string cpuManufacturer, string cpuCodeName, string windowsVersion)
        {
            lastDeviceStatus = status;
            lastConnectionType = connectionType;
            lastDeviceSerial = serial;
            lastDeviceModel = model;
            lastDeviceCode = code;
            lastAndroidVersion = androidVersion;
            lastUnlockStatus = unlockStatus;
            lastABPartition = abPartition;
            lastSelinuxStatus = selinuxStatus;
            lastKernelVersion = kernelVersion;
            lastCpuManufacturer = cpuManufacturer;
            lastCpuCodeName = cpuCodeName;
            lastWindowsVersion = windowsVersion;
        }
        
        private async Task UpdateVersionInfoAsync(string deviceCode, int detectionVersion)
        {
            try
            {
                string command;
                // 根据设备代号首字母大小写决定使用哪个命令
                if (!string.IsNullOrEmpty(deviceCode) && char.IsUpper(deviceCode[0]))
                {
                    // 首字母大写，使用 ro.build.display.id
                    command = "shell getprop ro.build.display.id";
                }
                else
                {
                    // 首字母小写，使用 ro.build.version.incremental
                    command = "shell getprop ro.build.version.incremental";
                }
                
                string adbPath = GetToolPath("adb.exe");
                if (string.IsNullOrEmpty(adbPath))
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (IsDeviceDetectionCycleCurrent(detectionVersion))
                        {
                            VersionInfoText.Text = "--";
                        }
                    });
                    return;
                }
                
                // 获取选中的设备序列号
                string selectedSerial = GetSelectedDeviceSerial();
                string finalCommand = command;
                
                // 如果有选中的设备序列号，添加 -s 参数
                if (!string.IsNullOrEmpty(selectedSerial))
                {
                    finalCommand = $"-s {selectedSerial} {command}";
                }
                
                string versionInfo = await GetCommandOutput(adbPath, finalCommand);
                string trimmedVersionInfo = versionInfo.Trim();
                
                // 在UI线程中更新界面
                Dispatcher.Invoke(() =>
                {
                    if (IsDeviceDetectionCycleCurrent(detectionVersion))
                    {
                        VersionInfoText.Text = string.IsNullOrEmpty(trimmedVersionInfo) ? "--" : trimmedVersionInfo;
                    }
                });
            }
            catch (Exception ex)
            {
                // 发生错误时显示未知
                Dispatcher.Invoke(() =>
                {
                    if (IsDeviceDetectionCycleCurrent(detectionVersion))
                    {
                        VersionInfoText.Text = "--";
                    }
                });
            }
        }
        
        private void UpdateStatusTextColor(string status, string connectionType)
        {
            if (DeviceStatusText != null)
            {
                if (status == "正在检测中")
                {
                    // 正在检测 - 黄色
                    DeviceStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 165, 0)); // #FFA500
                }
                else if (status == "已连接")
                {
                    // 已连接 - 绿色
                    DeviceStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69)); // #28A745
                }
                else
                {
                    // 未连接 - 红色
                    DeviceStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // #DC3545
                }
            }
        }

        private void UpdateBottomConnectionStatusIndicator(string status, string connectionType)
        {
            if (BottomConnectionStatusIndicator == null)
            {
                return;
            }

            bool isOnline = status == "已连接" &&
                            !string.IsNullOrWhiteSpace(connectionType) &&
                            connectionType != "--";

            BottomConnectionStatusIndicator.Visibility = isOnline
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        
        private string ExtractFastbootVar(string output, string varName)
        {
            try
            {
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    string trimmedLine = line.Trim();
                    
                    // 处理多种可能的fastboot输出格式
                    // 格式1: varName: value
                    if (trimmedLine.Contains($"{varName}:"))
                    {
                        var parts = trimmedLine.Split(':');
                        if (parts.Length >= 2)
                        {
                            string value = parts[1].Trim();
                            System.Diagnostics.Debug.WriteLine($"提取变量 {varName}: '{value}' (格式1)");
                            return value;
                        }
                    }
                    
                    // 格式2: (bootloader) varName: value
                    if (trimmedLine.Contains("(bootloader)") && trimmedLine.Contains($"{varName}:"))
                    {
                        int colonIndex = trimmedLine.IndexOf(':');
                        if (colonIndex > 0 && colonIndex < trimmedLine.Length - 1)
                        {
                            string value = trimmedLine.Substring(colonIndex + 1).Trim();
                            System.Diagnostics.Debug.WriteLine($"提取变量 {varName}: '{value}' (格式2)");
                            return value;
                        }
                    }
                }
                System.Diagnostics.Debug.WriteLine($"未找到变量 {varName}");
                return "--";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"提取变量 {varName} 时出错: {ex.Message}");
                return "--";
            }
        }

        private static bool IsMissingFastbootValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ||
                   value.Equals("--", StringComparison.OrdinalIgnoreCase);
        }

        private string ExtractBuildDateFromProcVersion(string output)
        {
            // 内核版本构建时间逻辑
            try
            {
                if (string.IsNullOrWhiteSpace(output)) return string.Empty;
                var mFull = System.Text.RegularExpressions.Regex.Match(output, @"#\d+[^\n]*\d{2}:\d{2}:\d{2}[^\n]*\d{4}");
                if (mFull.Success) return mFull.Value.Trim();
                int idx = output.IndexOf('#');
                if (idx >= 0)
                {
                    string tail = output.Substring(idx).Trim();
                    var parts = tail.Split('\n');
                    if (parts.Length > 0) return parts[0].Trim();
                }
                return output.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }
        
        private async Task<string> GetCommandOutput(
            string fileName,
            string arguments,
            CancellationToken cancellationToken = default)
        {
            Process? process = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(cancellationToken);
                string output = await outputTask;
                string error = await errorTask;

                // 合并标准输出和标准错误，因为fastboot很多信息输出到stderr
                string combinedOutput = output + "\n" + error;
                System.Diagnostics.Debug.WriteLine($"命令: {fileName} {arguments}");
                System.Diagnostics.Debug.WriteLine($"完整输出: '{combinedOutput}'");
                return combinedOutput;
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (process != null && !process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                }
                throw;
            }
            catch
            {
                return string.Empty;
            }
            finally
            {
                process?.Dispose();
            }
        }

        private string GetToolPath(string toolName)
        {
            // 可能的路径列表，按优先级排序
            var possiblePaths = new List<string>
            {
                // 1. 应用程序同目录下的platform-tools（最高优先级）
                Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "platform-tools", toolName),
                
                // 2. 应用程序目录下的platform-tools
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools", toolName),
                
                // 3. 应用程序上级目录的platform-tools
                Path.Combine(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.FullName ?? "", "platform-tools", toolName),
                
                // 4. 解决方案根目录的platform-tools
                Path.Combine(Directory.GetParent(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory)?.FullName ?? "")?.FullName ?? "", "platform-tools", toolName),
                
                // 5. 系统PATH中的工具
                toolName,
                
                // 6. Android SDK默认路径
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", toolName),
                
                // 7. 用户目录下的Android SDK
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Android", "Sdk", "platform-tools", toolName)
            };

            // 检查每个可能的路径
            foreach (var path in possiblePaths)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return path;
                }
            }

            // 如果都找不到，返回工具名（让系统在PATH中查找）
            return toolName;
        }

        // 根据 dumpsys battery 输出更新电池控件
        private void UpdateBatteryFromDumpsysOutput(string output, int detectionVersion)
        {
            if (!IsDeviceDetectionCycleCurrent(detectionVersion))
            {
                return;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(output)) return;

                int level = TryGetIntFromBattery(output, "level");
                int scale = TryGetIntFromBattery(output, "scale");
                int tempTenth = TryGetIntFromBattery(output, "temperature");

                // 判断是否在充电
                bool isCharging =
                    Regex.IsMatch(output, @"AC powered:\s*true", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(output, @"USB powered:\s*true", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(output, @"Wireless powered:\s*true", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(output, @"status:\s*2", RegexOptions.IgnoreCase) ||
                    (TryGetIntFromBattery(output, "plugged") > 0);

                double tempC = tempTenth > 0 ? tempTenth / 10.0 : double.NaN;
                string tempText = tempTenth > 0 ? $"{tempC:F1} °C" : "--";

                Dispatcher.Invoke(() =>
                {
                    if (!IsDeviceDetectionCycleCurrent(detectionVersion))
                    {
                        return;
                    }

                    if (BatteryControl != null)
                    {
                        BatteryControl.Maximum = scale > 0 ? scale : 100;
                        BatteryControl.Value = Math.Clamp(level, 0, BatteryControl.Maximum);
                        BatteryControl.IsCharging = isCharging;
                        BatteryControl.TemperatureText = tempText;
                    }
                });
            }
            catch
            {
                // 忽略解析异常，避免影响主流程
            }
        }

        private static int TryGetIntFromBattery(string output, string key)
        {
            var m = Regex.Match(output, key + @":\s*(\d+)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                if (int.TryParse(m.Groups[1].Value, out int v))
                    return v;
            }
            return 0;
        }

        private void ShowMessage(string message)
        {
    }

    private sealed class DriverFileItem
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string IconSource { get; set; } = "images/exe.svg";
    }

    private async void DriverTile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (_isLoadingDriverList || _isLoadingRootManagerList || _isLoadingKernel4List || _isLoadingOnePlusAk3List || _isLoadingOujiaAk3List || _isLoadingAndroidAk3List || _isLoadingUtilitySoftwareList || _isLoadingUserUploadList)
        {
            return;
        }

        _isLoadingDriverList = true;
        try
        {
            AddDownloadLogMessage("信息", "正在获取刷机驱动列表...");

            DriverTileView.Visibility = Visibility.Collapsed;
            DriverListView.Visibility = Visibility.Visible;
            DriverFileListBox.Visibility = Visibility.Collapsed;
            DriverLoadingView.Visibility = Visibility.Visible;

            var items = await LoadDriverFileItemsAsync(DriverListSourceUrl);
            if (items.Count == 0)
            {
                AddDownloadLogMessage("警告", "未解析到任何驱动文件条目");
                DriverLoadingView.Visibility = Visibility.Collapsed;
                DriverListView.Visibility = Visibility.Collapsed;
                DriverTileView.Visibility = Visibility.Visible;
                return;
            }

            foreach (var item in items)
            {
                item.IconSource = "images/exe.svg";
            }

            DriverFileListBox.ItemsSource = items;
            DriverLoadingView.Visibility = Visibility.Collapsed;
            DriverFileListBox.Visibility = Visibility.Visible;
            AddDownloadLogMessage("成功", $"已加载 {items.Count} 个驱动条目");
        }
        catch (Exception ex)
        {
            AddDownloadLogMessage("错误", $"获取驱动列表失败: {ex.Message}");
            DriverLoadingView.Visibility = Visibility.Collapsed;
            DriverListView.Visibility = Visibility.Collapsed;
            DriverTileView.Visibility = Visibility.Visible;
        }
        finally
        {
            _isLoadingDriverList = false;
        }

        e.Handled = true;
    }

    private async void RootManagerTile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (_isLoadingRootManagerList || _isLoadingDriverList || _isLoadingKernel4List || _isLoadingOnePlusAk3List || _isLoadingOujiaAk3List || _isLoadingAndroidAk3List || _isLoadingUtilitySoftwareList || _isLoadingUserUploadList)
        {
            return;
        }

        _isLoadingRootManagerList = true;
        try
        {
            AddDownloadLogMessage("信息", "正在获取ROOT管理器列表...");

            DriverTileView.Visibility = Visibility.Collapsed;
            DriverListView.Visibility = Visibility.Visible;
            DriverFileListBox.Visibility = Visibility.Collapsed;
            DriverLoadingView.Visibility = Visibility.Visible;

            var items = await LoadDriverFileItemsAsync(RootManagerListSourceUrl);
            if (items.Count == 0)
            {
                AddDownloadLogMessage("警告", "未解析到任何ROOT管理器文件条目");
                DriverLoadingView.Visibility = Visibility.Collapsed;
                DriverListView.Visibility = Visibility.Collapsed;
                DriverTileView.Visibility = Visibility.Visible;
                return;
            }

            foreach (var item in items)
            {
                item.IconSource = "images/安卓.svg";
            }

            DriverFileListBox.ItemsSource = items;
            DriverLoadingView.Visibility = Visibility.Collapsed;
            DriverFileListBox.Visibility = Visibility.Visible;
            AddDownloadLogMessage("成功", $"已加载 {items.Count} 个ROOT管理器条目");
        }
        catch (Exception ex)
        {
            AddDownloadLogMessage("错误", $"获取ROOT管理器列表失败: {ex.Message}");
            DriverLoadingView.Visibility = Visibility.Collapsed;
            DriverListView.Visibility = Visibility.Collapsed;
            DriverTileView.Visibility = Visibility.Visible;
        }
        finally
        {
            _isLoadingRootManagerList = false;
        }

        e.Handled = true;
    }

    private async void Kernel4Tile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (_isLoadingKernel4List || _isLoadingDriverList || _isLoadingRootManagerList || _isLoadingOnePlusAk3List || _isLoadingOujiaAk3List || _isLoadingAndroidAk3List || _isLoadingUtilitySoftwareList || _isLoadingUserUploadList)
        {
            return;
        }

        _isLoadingKernel4List = true;
        try
        {
            AddDownloadLogMessage("信息", "正在获取4系内核AK3列表...");

            DriverTileView.Visibility = Visibility.Collapsed;
            DriverListView.Visibility = Visibility.Visible;
            DriverFileListBox.Visibility = Visibility.Collapsed;
            DriverLoadingView.Visibility = Visibility.Visible;

            var items = await LoadDriverFileItemsAsync(Kernel4ListSourceUrl);
            if (items.Count == 0)
            {
                AddDownloadLogMessage("警告", "未解析到任何4系内核AK3文件条目");
                DriverLoadingView.Visibility = Visibility.Collapsed;
                DriverListView.Visibility = Visibility.Collapsed;
                DriverTileView.Visibility = Visibility.Visible;
                return;
            }

            foreach (var item in items)
            {
                item.IconSource = "images/压缩包.svg";
            }

            DriverFileListBox.ItemsSource = items;
            DriverLoadingView.Visibility = Visibility.Collapsed;
            DriverFileListBox.Visibility = Visibility.Visible;
            AddDownloadLogMessage("成功", $"已加载 {items.Count} 个4系内核AK3条目");
        }
        catch (Exception ex)
        {
            AddDownloadLogMessage("错误", $"获取4系内核AK3列表失败: {ex.Message}");
            DriverLoadingView.Visibility = Visibility.Collapsed;
            DriverListView.Visibility = Visibility.Collapsed;
            DriverTileView.Visibility = Visibility.Visible;
        }
        finally
        {
            _isLoadingKernel4List = false;
        }

        e.Handled = true;
    }

    private async void OnePlusAk3Tile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (_isLoadingOnePlusAk3List || _isLoadingDriverList || _isLoadingRootManagerList || _isLoadingKernel4List || _isLoadingOujiaAk3List || _isLoadingAndroidAk3List || _isLoadingUtilitySoftwareList || _isLoadingUserUploadList)
        {
            return;
        }

        _isLoadingOnePlusAk3List = true;
        try
        {
            AddDownloadLogMessage("信息", "正在获取一加专用AK3列表...");

            DriverTileView.Visibility = Visibility.Collapsed;
            DriverListView.Visibility = Visibility.Visible;
            DriverFileListBox.Visibility = Visibility.Collapsed;
            DriverLoadingView.Visibility = Visibility.Visible;

            var items = await LoadDriverFileItemsAsync(OnePlusAk3ListSourceUrl);
            if (items.Count == 0)
            {
                AddDownloadLogMessage("警告", "未解析到任何一加专用AK3文件条目");
                DriverLoadingView.Visibility = Visibility.Collapsed;
                DriverListView.Visibility = Visibility.Collapsed;
                DriverTileView.Visibility = Visibility.Visible;
                return;
            }

            foreach (var item in items)
            {
                item.IconSource = "images/压缩包.svg";
            }

            DriverFileListBox.ItemsSource = items;
            DriverLoadingView.Visibility = Visibility.Collapsed;
            DriverFileListBox.Visibility = Visibility.Visible;
            AddDownloadLogMessage("成功", $"已加载 {items.Count} 个一加专用AK3条目");
        }
        catch (Exception ex)
        {
            AddDownloadLogMessage("错误", $"获取一加专用AK3列表失败: {ex.Message}");
            DriverLoadingView.Visibility = Visibility.Collapsed;
            DriverListView.Visibility = Visibility.Collapsed;
            DriverTileView.Visibility = Visibility.Visible;
        }
        finally
        {
            _isLoadingOnePlusAk3List = false;
        }

        e.Handled = true;
    }

    private async void OujiaAk3Tile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (_isLoadingOujiaAk3List || _isLoadingDriverList || _isLoadingRootManagerList || _isLoadingKernel4List || _isLoadingOnePlusAk3List || _isLoadingAndroidAk3List || _isLoadingUtilitySoftwareList || _isLoadingUserUploadList)
        {
            return;
        }

        _isLoadingOujiaAk3List = true;
        try
        {
            AddDownloadLogMessage("信息", "正在获取欧加通用AK3列表...");

            DriverTileView.Visibility = Visibility.Collapsed;
            DriverListView.Visibility = Visibility.Visible;
            DriverFileListBox.Visibility = Visibility.Collapsed;
            DriverLoadingView.Visibility = Visibility.Visible;

            var items = await LoadDriverFileItemsAsync(OujiaAk3ListSourceUrl);
            if (items.Count == 0)
            {
                AddDownloadLogMessage("警告", "未解析到任何欧加通用AK3文件条目");
                DriverLoadingView.Visibility = Visibility.Collapsed;
                DriverListView.Visibility = Visibility.Collapsed;
                DriverTileView.Visibility = Visibility.Visible;
                return;
            }

            foreach (var item in items)
            {
                item.IconSource = "images/压缩包.svg";
            }

            DriverFileListBox.ItemsSource = items;
            DriverLoadingView.Visibility = Visibility.Collapsed;
            DriverFileListBox.Visibility = Visibility.Visible;
            AddDownloadLogMessage("成功", $"已加载 {items.Count} 个欧加通用AK3条目");
        }
        catch (Exception ex)
        {
            AddDownloadLogMessage("错误", $"获取欧加通用AK3列表失败: {ex.Message}");
            DriverLoadingView.Visibility = Visibility.Collapsed;
            DriverListView.Visibility = Visibility.Collapsed;
            DriverTileView.Visibility = Visibility.Visible;
        }
        finally
        {
            _isLoadingOujiaAk3List = false;
        }

        e.Handled = true;
    }

    private async void AndroidAk3Tile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (_isLoadingAndroidAk3List || _isLoadingDriverList || _isLoadingRootManagerList || _isLoadingKernel4List || _isLoadingOnePlusAk3List || _isLoadingOujiaAk3List || _isLoadingUtilitySoftwareList || _isLoadingUserUploadList)
        {
            return;
        }

        _isLoadingAndroidAk3List = true;
        try
        {
            AddDownloadLogMessage("信息", "正在获取安卓通用AK3列表...");

            DriverTileView.Visibility = Visibility.Collapsed;
            DriverListView.Visibility = Visibility.Visible;
            DriverFileListBox.Visibility = Visibility.Collapsed;
            DriverLoadingView.Visibility = Visibility.Visible;

            var items = await LoadDriverFileItemsAsync(AndroidAk3ListSourceUrl);
            if (items.Count == 0)
            {
                AddDownloadLogMessage("警告", "未解析到任何安卓通用AK3文件条目");
                DriverLoadingView.Visibility = Visibility.Collapsed;
                DriverListView.Visibility = Visibility.Collapsed;
                DriverTileView.Visibility = Visibility.Visible;
                return;
            }

            foreach (var item in items)
            {
                item.IconSource = "images/压缩包.svg";
            }

            DriverFileListBox.ItemsSource = items;
            DriverLoadingView.Visibility = Visibility.Collapsed;
            DriverFileListBox.Visibility = Visibility.Visible;
            AddDownloadLogMessage("成功", $"已加载 {items.Count} 个安卓通用AK3条目");
        }
        catch (Exception ex)
        {
            AddDownloadLogMessage("错误", $"获取安卓通用AK3列表失败: {ex.Message}");
            DriverLoadingView.Visibility = Visibility.Collapsed;
            DriverListView.Visibility = Visibility.Collapsed;
            DriverTileView.Visibility = Visibility.Visible;
        }
        finally
        {
            _isLoadingAndroidAk3List = false;
        }

        e.Handled = true;
    }

    private async void UtilitySoftwareIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (_isLoadingUtilitySoftwareList || _isLoadingDriverList || _isLoadingRootManagerList || _isLoadingKernel4List || _isLoadingOnePlusAk3List || _isLoadingOujiaAk3List || _isLoadingAndroidAk3List || _isLoadingUserUploadList)
        {
            return;
        }

        _isLoadingUtilitySoftwareList = true;
        try
        {
            AddDownloadLogMessage("信息", "正在获取实用软件列表...");

            DriverTileView.Visibility = Visibility.Collapsed;
            DriverListView.Visibility = Visibility.Visible;
            DriverFileListBox.Visibility = Visibility.Collapsed;
            DriverLoadingView.Visibility = Visibility.Visible;

            var items = await LoadDriverFileItemsAsync(UtilitySoftwareListSourceUrl);
            if (items.Count == 0)
            {
                AddDownloadLogMessage("警告", "未解析到任何实用软件条目");
                DriverLoadingView.Visibility = Visibility.Collapsed;
                DriverListView.Visibility = Visibility.Collapsed;
                DriverTileView.Visibility = Visibility.Visible;
                return;
            }

            foreach (var item in items)
            {
                item.IconSource = "images/压缩包.svg";
            }

            DriverFileListBox.ItemsSource = items;
            DriverLoadingView.Visibility = Visibility.Collapsed;
            DriverFileListBox.Visibility = Visibility.Visible;
            AddDownloadLogMessage("成功", $"已加载 {items.Count} 个实用软件条目");
        }
        catch (Exception ex)
        {
            AddDownloadLogMessage("错误", $"获取实用软件列表失败: {ex.Message}");
            DriverLoadingView.Visibility = Visibility.Collapsed;
            DriverListView.Visibility = Visibility.Collapsed;
            DriverTileView.Visibility = Visibility.Visible;
        }
        finally
        {
            _isLoadingUtilitySoftwareList = false;
        }

        e.Handled = true;
    }

    private async void UserUploadIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (_isLoadingUserUploadList || _isLoadingDriverList || _isLoadingRootManagerList || _isLoadingKernel4List || _isLoadingOnePlusAk3List || _isLoadingOujiaAk3List || _isLoadingAndroidAk3List || _isLoadingUtilitySoftwareList)
        {
            return;
        }

        _isLoadingUserUploadList = true;
        try
        {
            AddDownloadLogMessage("信息", "正在获取用户上传列表...");

            DriverTileView.Visibility = Visibility.Collapsed;
            DriverListView.Visibility = Visibility.Visible;
            DriverFileListBox.Visibility = Visibility.Collapsed;
            DriverLoadingView.Visibility = Visibility.Visible;

            var items = await LoadDriverFileItemsAsync(UserUploadListSourceUrl);
            if (items.Count == 0)
            {
                AddDownloadLogMessage("警告", "未解析到任何用户上传条目");
                DriverLoadingView.Visibility = Visibility.Collapsed;
                DriverListView.Visibility = Visibility.Collapsed;
                DriverTileView.Visibility = Visibility.Visible;
                return;
            }

            foreach (var item in items)
            {
                item.IconSource = "images/压缩包.svg";
            }

            DriverFileListBox.ItemsSource = items;
            DriverLoadingView.Visibility = Visibility.Collapsed;
            DriverFileListBox.Visibility = Visibility.Visible;
            AddDownloadLogMessage("成功", $"已加载 {items.Count} 个用户上传条目");
        }
        catch (Exception ex)
        {
            AddDownloadLogMessage("错误", $"获取用户上传列表失败: {ex.Message}");
            DriverLoadingView.Visibility = Visibility.Collapsed;
            DriverListView.Visibility = Visibility.Collapsed;
            DriverTileView.Visibility = Visibility.Visible;
        }
        finally
        {
            _isLoadingUserUploadList = false;
        }

        e.Handled = true;
    }

    private void NekoDownloaderIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var exePath = Path.Combine(baseDir, "exe", "Neko.exe");
            if (!File.Exists(exePath))
            {
                AddDownloadLogMessage("错误", $"未找到 Neko.exe: {exePath}");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath)
            });
            AddDownloadLogMessage("成功", "已启动 Neko 下载器");
        }
        catch (Exception ex)
        {
            AddDownloadLogMessage("错误", $"启动 Neko 下载器失败: {ex.Message}");
        }

        e.Handled = true;
    }

    private void NdmDownloaderIcon_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidatePaths = new[]
            {
                Path.Combine(baseDir, "NDM.exe"),
                Path.Combine(baseDir, "exe", "NDM.exe")
            };

            var exePath = candidatePaths.FirstOrDefault(File.Exists);
            if (string.IsNullOrEmpty(exePath))
            {
                AddDownloadLogMessage("错误", $"未找到 NDM.exe，已尝试路径: {string.Join("; ", candidatePaths)}");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath)
            });
            AddDownloadLogMessage("成功", "已启动 NDM 下载器");
        }
        catch (Exception ex)
        {
            AddDownloadLogMessage("错误", $"启动 NDM 下载器失败: {ex.Message}");
        }

        e.Handled = true;
    }

    private void BackToDriverTileButton_Click(object sender, RoutedEventArgs e)
    {
        DriverLoadingView.Visibility = Visibility.Collapsed;
        DriverFileListBox.Visibility = Visibility.Visible;
        DriverListView.Visibility = Visibility.Collapsed;
        DriverTileView.Visibility = Visibility.Visible;
    }

    private static string ToGiteeRawUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        if (url.Contains("/raw/", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        var i = url.IndexOf("/blob/", StringComparison.OrdinalIgnoreCase);
        if (i < 0)
        {
            return url;
        }

        return url.Substring(0, i) + "/raw/" + url.Substring(i + "/blob/".Length);
    }

    private async Task<List<DriverFileItem>> LoadDriverFileItemsAsync(string sourceUrl)
    {
        var candidates = new[]
        {
            ToGiteeRawUrl(sourceUrl),
            sourceUrl
        }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        string? lastText = null;
        foreach (var url in candidates)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) SmartTool");
                request.Headers.TryAddWithoutValidation("Accept", "application/json,text/plain,*/*");

                using var response = await DriverListHttpClient.SendAsync(request);
                var text = await response.Content.ReadAsStringAsync();
                lastText = text;

                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (text.Contains("<html", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var items = ParseDriverFileItems(text);
                if (items.Count > 0)
                {
                    return items;
                }
            }
            catch
            {
            }
        }

        if (!string.IsNullOrWhiteSpace(lastText))
        {
            if (lastText.Contains("无法连接", StringComparison.OrdinalIgnoreCase) ||
                lastText.Contains("人机验证", StringComparison.OrdinalIgnoreCase) ||
                lastText.Contains("Failed to fetch", StringComparison.OrdinalIgnoreCase))
            {
                AddDownloadLogMessage("错误", "列表源返回了人机验证/网络错误提示，暂时无法解析");
            }
        }

        return new List<DriverFileItem>();
    }

    private static List<DriverFileItem> ParseDriverFileItems(string text)
    {
        var items = TryParseDriverFileItemsFromJson(text);
        if (items.Count > 0)
        {
            return items;
        }

        items = TryParseDriverFileItemsFromLooseJson(text);
        if (items.Count > 0)
        {
            return items;
        }

        return ParseDriverFileItemsFromKeyValueText(text);
    }

    private static List<DriverFileItem> TryParseDriverFileItemsFromJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                return ParseDriverFileItemsFromJsonArray(root);
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (TryGetArrayProperty(root, out var files, "files", "items", "drivers", "list", "data"))
                {
                    return ParseDriverFileItemsFromJsonArray(files);
                }

                var single = ParseDriverFileItemFromJsonObject(root);
                if (single != null)
                {
                    return new List<DriverFileItem> { single };
                }
            }
        }
        catch
        {
        }

        return new List<DriverFileItem>();
    }

    private static List<DriverFileItem> ParseDriverFileItemsFromJsonArray(JsonElement array)
    {
        var items = new List<DriverFileItem>();
        foreach (var el in array.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var item = ParseDriverFileItemFromJsonObject(el);
            if (item == null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Url))
            {
                continue;
            }

            items.Add(item);
        }

        return items;
    }

    private static DriverFileItem? ParseDriverFileItemFromJsonObject(JsonElement obj)
    {
        var name = CleanParsedValue(TryGetStringProperty(obj, "name", "Name", "filename", "fileName", "title"));
        var url = CleanParsedValue(TryGetStringProperty(obj, "url", "Url", "link", "downloadUrl", "download"));

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return new DriverFileItem
        {
            Name = name,
            Url = url
        };
    }

    private static string CleanParsedValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var v = value.Trim();
        v = v.Trim('`', '"', '\'');
        if (v.Contains('`'))
        {
            v = v.Replace("`", "").Trim();
        }

        return v;
    }

    private static List<DriverFileItem> TryParseDriverFileItemsFromLooseJson(string text)
    {
        try
        {
            var items = new List<DriverFileItem>();
            var matches = Regex.Matches(
                text,
                "\\{[^\\{\\}]*?\"name\"\\s*:\\s*\"(?<name>[^\"]+)\"[^\\{\\}]*?\"url\"\\s*:\\s*\"(?<url>[^\"]+)\"[^\\{\\}]*?\\}",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match m in matches)
            {
                var name = CleanParsedValue(m.Groups["name"].Value);
                var url = CleanParsedValue(m.Groups["url"].Value);
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                items.Add(new DriverFileItem { Name = name, Url = url });
            }

            return items;
        }
        catch
        {
            return new List<DriverFileItem>();
        }
    }

    private static bool TryGetArrayProperty(JsonElement obj, out JsonElement array, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Array)
            {
                array = prop;
                return true;
            }
        }

        array = default;
        return false;
    }

    private static string? TryGetStringProperty(JsonElement obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (!obj.TryGetProperty(name, out var prop))
            {
                continue;
            }

            if (prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }

        return null;
    }

    private static List<DriverFileItem> ParseDriverFileItemsFromKeyValueText(string text)
    {
        var items = new List<DriverFileItem>();

        string? currentName = null;
        string? currentUrl = null;

        foreach (var rawLine in Regex.Split(text, "\r\n|\r|\n"))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var nameMatch = Regex.Match(line, @"^\s*name\s*[:：]\s*(.+)\s*$", RegexOptions.IgnoreCase);
            if (nameMatch.Success)
            {
                currentName = nameMatch.Groups[1].Value.Trim();
                currentName = currentName.Trim('`', '"', '\'');
            }

            var urlMatch = Regex.Match(line, @"^\s*url\s*[:：]\s*(.+)\s*$", RegexOptions.IgnoreCase);
            if (urlMatch.Success)
            {
                currentUrl = urlMatch.Groups[1].Value.Trim();
                currentUrl = currentUrl.Trim('`', '"', '\'');
            }
            else if (line.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                     line.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                currentUrl = line.Trim('`', '"', '\'');
            }

            if (!string.IsNullOrWhiteSpace(currentName) && !string.IsNullOrWhiteSpace(currentUrl))
            {
                items.Add(new DriverFileItem
                {
                    Name = currentName,
                    Url = currentUrl
                });
                currentName = null;
                currentUrl = null;
            }
        }

        return items;
    }

    private async Task DownloadAndOpenDriver(string url, string fileName)
    {
        try
        {
            AddDownloadLogMessage("信息", $"正在下载 {fileName}...");
            
            // 创建drivers驱动存放文件夹
            string driversPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "drivers");
            if (!Directory.Exists(driversPath))
            {
                Directory.CreateDirectory(driversPath);
            }
            
            string filePath = Path.Combine(driversPath, fileName);
            
            using (HttpClient client = new HttpClient())
            {
                // 设置超时时间10秒
                client.Timeout = TimeSpan.FromMinutes(10);
                
                // 下载文件
                byte[] fileBytes = await client.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(filePath, fileBytes);
            }
            
            AddDownloadLogMessage("成功", $"{fileName} 下载完成，正在打开...");
            
            // 自动打开下载的exe文件
            if (File.Exists(filePath) && Path.GetExtension(filePath).ToLower() == ".exe")
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
                AddDownloadLogMessage("成功", $"{fileName} 已启动");
            }
            else
            {
                AddDownloadLogMessage("警告", $"文件下载完成，但无法自动打开: {filePath}");
            }
        }
        catch (Exception ex)
        {
            AddDownloadLogMessage("错误", $"下载 {fileName} 失败: {ex.Message}");
        }
    }

    private void DriverFileListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DriverFileListBox.SelectedItem is not DriverFileItem item)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.Url))
        {
            AddDownloadLogMessage("错误", "下载链接为空");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = item.Url,
                UseShellExecute = true
            });
            AddDownloadLogMessage("信息", $"已打开链接: {item.Url}");
        }
        catch (Exception ex)
        {
            AddDownloadLogMessage("错误", $"打开链接失败: {ex.Message}");
        }
    }
    
    // Windows API声明
        private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
        private const int OBJID_WINDOW = 0;
        private const int CHILDID_SELF = 0;
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_ASYNCWINDOWPOS = 0x4000;

        private delegate void WinEventDelegate(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint idEventThread,
            uint dwmsEventTime);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc,
            uint idProcess,
            uint idThread,
            uint dwFlags);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void BootFilePathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Boot文件路径文本框内容变化事件处理
        }

        // 选择Boot镜像文件
        private void SelectBootFileButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择镜像文件",
                Filter = "镜像文件 (*.img)|*.img|所有文件 (*.*)|*.*",
                FilterIndex = 1
            };

            if (openFileDialog.ShowDialog() == true)
            {
                BootFilePathTextBox.Text = openFileDialog.FileName;
                LogSimpleStatus($"已选择镜像{openFileDialog.FileName}");
            }
        }
        private enum FastbootProcessorType
        {
            Unknown,
            Qualcomm,
            MediaTek
        }

        private const int MediaTekBootloaderFlowMaxAttempts = 20;

        private static bool IsGuidedBootloaderCommand(string fastbootArgs)
        {
            return string.Equals(fastbootArgs, "flashing unlock", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fastbootArgs, "flashing lock", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBootloaderUnlockCommand(string fastbootArgs)
        {
            return fastbootArgs.EndsWith("unlock", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<FastbootProcessorType> DetectFastbootProcessorTypeAsync(string fastbootPath)
        {
            try
            {
                // Fastboot 没有统一的 SoC 查询变量，因此按分区表中的 LK 分区识别 MTK。
                // 检测命令保持静默，避免 getvar all 的大量输出污染用户日志。
                string partitionTable = await ExecuteFastbootCommand(
                    fastbootPath,
                    "getvar all",
                    showNativeOutput: false,
                    parseStatusOutput: false);

                if (string.IsNullOrWhiteSpace(partitionTable) ||
                    CommandOutputIndicatesFailure(partitionTable))
                {
                    return FastbootProcessorType.Unknown;
                }

                bool hasLkPartition = Regex.IsMatch(
                    partitionTable,
                    @"(?im)\b(?:partition-size|partition-type|has-slot):lk(?:_[ab])?\s*:",
                    RegexOptions.CultureInvariant);

                return hasLkPartition
                    ? FastbootProcessorType.MediaTek
                    : FastbootProcessorType.Qualcomm;
            }
            catch
            {
                return FastbootProcessorType.Unknown;
            }
        }

        private async Task ExecuteBootloaderCommandOnceAsync(
            string fastbootPath,
            string selectedCommand,
            string fastbootArgs)
        {
            LogSimpleStatus($"> {selectedCommand}");
            string commandResult = await ExecuteFastbootCommand(
                fastbootPath,
                fastbootArgs,
                showNativeOutput: true);

            if (string.IsNullOrWhiteSpace(commandResult))
            {
                LogSimpleStatus("警告: Fastboot 未返回任何输出");
            }
        }

        private async Task ExecuteMediaTekBootloaderFlowAsync(
            string fastbootPath,
            string selectedCommand,
            string fastbootArgs)
        {
            bool isUnlock = IsBootloaderUnlockCommand(fastbootArgs);
            string expectedFlow = isUnlock ? "Start unlock flow" : "Start lock flow";
            const string operationTip = "请盯住当前手机界面，正常情况下应该是有两串小英文显示\"fastboot mode\"，当界面英文突然变多时，请连续短按两次音量上键（音量+）.";

            for (int attempt = 1; attempt <= MediaTekBootloaderFlowMaxAttempts; attempt++)
            {
                LogSimpleStatus(operationTip);
                LogSimpleStatus($"> {selectedCommand}");

                string commandResult = await ExecuteFastbootCommand(
                    fastbootPath,
                    fastbootArgs,
                    showNativeOutput: true);

                if (CommandOutputIndicatesFailure(commandResult))
                {
                    LogSimpleStatus("[ERROR]Fastboot 指令执行失败，已停止重试");
                    return;
                }

                if (commandResult.Contains(expectedFlow, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (attempt < MediaTekBootloaderFlowMaxAttempts)
                {
                    await Task.Delay(500);
                }
            }

            LogSimpleStatus($"[ERROR]连续发送 {MediaTekBootloaderFlowMaxAttempts} 次指令后仍未进入 Bootloader 操作界面，已停止重试");
        }

        private async void LockBLButton_Click(object sender, RoutedEventArgs e)
        {
            LockBLButton.IsEnabled = false;
            try
            {
                // 获取ComboBox中选择的命令
                if (UnlockBLComboBox.SelectedItem is not ComboBoxItem selectedItem ||
                    string.IsNullOrWhiteSpace(selectedItem.Content?.ToString()))
                {
                    LogSimpleStatus("请先选择要执行的命令...");
                    return;
                }

                string selectedCommand = selectedItem.Content.ToString()!;
                 
                // 先检测设备连接
                string fastbootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools", "fastboot.exe");
                string deviceCheckResult = await ExecuteFastbootCommand(fastbootPath, "devices");
                
                if (string.IsNullOrWhiteSpace(deviceCheckResult) || !deviceCheckResult.Contains("\t"))
                {
                    LogSimpleStatus("未检测到设备，请确保设备处于Fastboot并已安装驱动...");
                    return;
                }
                 
                // 从完整命令中提取fastboot参数
                string fastbootArgs = selectedCommand.StartsWith("fastboot ", StringComparison.OrdinalIgnoreCase)
                    ? selectedCommand["fastboot ".Length..].Trim()
                    : selectedCommand.Trim();

                if (!IsGuidedBootloaderCommand(fastbootArgs))
                {
                    await ExecuteBootloaderCommandOnceAsync(fastbootPath, selectedCommand, fastbootArgs);
                    return;
                }

                FastbootProcessorType processorType = await DetectFastbootProcessorTypeAsync(fastbootPath);
                switch (processorType)
                {
                    case FastbootProcessorType.Qualcomm:
                        LogSimpleStatus("处理器类型：高通骁龙");
                        string actionText = IsBootloaderUnlockCommand(fastbootArgs)
                            ? "UNLOCK THE BOOTLOADER"
                            : "LOCK THE BOOTLOADER";
                        LogSimpleStatus($"请按两下音量下键，选择第二个\"{actionText}\"，然后按电源键确认.");
                        await ExecuteBootloaderCommandOnceAsync(fastbootPath, selectedCommand, fastbootArgs);
                        break;

                    case FastbootProcessorType.MediaTek:
                        LogSimpleStatus("处理器类型：联发科");
                        await ExecuteMediaTekBootloaderFlowAsync(fastbootPath, selectedCommand, fastbootArgs);
                        break;

                    default:
                        LogSimpleStatus("处理器类型：检测失败（跳过）");
                        await ExecuteBootloaderCommandOnceAsync(fastbootPath, selectedCommand, fastbootArgs);
                        break;
                }
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"命令执行失败: {ex.Message}");
            }
            finally
            {
                LockBLButton.IsEnabled = true;
            }
        }

        // 刷入Boot镜像
        private async void FlashBootButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 验证文件路径
                string bootFilePath = BootFilePathTextBox.Text.Trim();
                if (string.IsNullOrEmpty(bootFilePath))
                {
                    LogSimpleStatus("错误: 请先选择镜像文件");
                    return;
                }

                if (!File.Exists(bootFilePath))
                {
                    LogSimpleStatus("错误: 选择的镜像文件不存在");
                    return;
                }

                // 获取选择的设备序列号，确保整个过程使用同一设备
                string targetDeviceSerial = GetSelectedDeviceSerial();
                
                // 如果没有勾选"等待FB设备"且没有选择设备，则报错
                if (string.IsNullOrEmpty(targetDeviceSerial) && WaitForFastbootCheckBox.IsChecked != true)
                {
                    LogSimpleStatus("错误: 请先选择目标设备或勾选'等待FB设备'");
                    return;
                }

                // 禁用按钮防止重复操作
                FlashBootButton.IsEnabled = false;
                
                // 显示并初始化进度条
                bootflash.Visibility = Visibility.Visible;
                bootflash.Value = 0;
                UpdateTransferRateText("0MB/s");

                string selectedPartition = "boot";
                if (PartitionComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    selectedPartition = selectedItem.Content?.ToString() ?? "boot";
                }

                var imageInfo = new FileInfo(bootFilePath);
                var totalStopwatch = Stopwatch.StartNew();
                LogSimpleStatus($"{imageInfo.Name} > {selectedPartition}.img | {imageInfo.Length / 1024d / 1024d:F0}MB");

                // 检查Fastboot设备连接
                // 使用程序同目录中的flash文件夹中的fastboot.exe
                string fastbootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools", "fastboot.exe");
                
                // 检查fastboot.exe是否存在
                if (!File.Exists(fastbootPath))
                {
                    LogSimpleStatus($"错误: 未找到fastboot.exe文件，请确保文件存在于: {fastbootPath}");
                    bootflash.Value = 0;
                    UpdateTransferRateText("0MB/s");
                    FlashBootButton.IsEnabled = true;
                    return;
                }
                
                // 检查是否需要等待Fastboot设备
                bool deviceDetected = false;
                if (WaitForFastbootCheckBox.IsChecked == true)
                {
                    AppendFlashCountdown("等待Fastboot设备...60s");
                    
                    // 等待60秒检测设备
                    for (int i = 0; i < 60; i++)
                    {
                        // 如果没有指定设备序列号，检测任意Fastboot设备
                        string deviceCheckResult;
                        if (string.IsNullOrEmpty(targetDeviceSerial))
                        {
                            deviceCheckResult = await ExecuteFastbootCommand(fastbootPath, "devices");
                        }
                        else
                        {
                            deviceCheckResult = await ExecuteFastbootCommandWithSerial(fastbootPath, "devices", targetDeviceSerial);
                        }
                        
                        if (!string.IsNullOrEmpty(deviceCheckResult) && deviceCheckResult.Contains("fastboot"))
                        {
                            deviceDetected = true;
                            
                            // 如果之前没有设备序列号，从检测结果中提取
                            if (string.IsNullOrEmpty(targetDeviceSerial))
                            {
                                var lines = deviceCheckResult.Split('\n');
                                foreach (var line in lines)
                                {
                                    if (line.Contains("fastboot"))
                                    {
                                        targetDeviceSerial = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0];
                                        break;
                                    }
                                }
                            }
                            
                            LogSimpleStatus($"已连接 {targetDeviceSerial} | 用时 {i + 1} 秒");
                            break;
                        }

                        await Task.Delay(1000); // 等待1秒
                        UpdateFlashCountdown($"等待Fastboot设备...{59 - i}s");
                    }
                    
                    if (!deviceDetected)
                    {
                        LogSimpleStatus("[失败] 连接超时，60 秒内未检测到 Fastboot 设备");
                        bootflash.Value = 0;
                        UpdateTransferRateText("0MB/s");
                        FlashBootButton.IsEnabled = true;
                        return;
                    }

                    // 检测到设备后等待片刻，避免设备刚进入 Fastboot 时立即刷入导致连接不稳定
                    AppendFlashCountdown("等待设备连接稳定...3s");
                    for (int remaining = 2; remaining >= 0; remaining--)
                    {
                        await Task.Delay(1000);
                        UpdateFlashCountdown($"等待设备连接稳定...{remaining}s");
                    }
                }
                else
                {
                    LogSimpleStatus($"[设备] 正在验证 {targetDeviceSerial}");
                    // 不等待，直接检查设备连接
                    if (string.IsNullOrEmpty(targetDeviceSerial))
                    {
                        LogSimpleStatus("错误: 请先选择目标设备");
                        bootflash.Value = 0;
                        UpdateTransferRateText("0MB/s");
                        FlashBootButton.IsEnabled = true;
                        return;
                    }
                    
                    string deviceCheckResult = await ExecuteFastbootCommandWithSerial(fastbootPath, "devices", targetDeviceSerial);
                    
                    if (string.IsNullOrEmpty(deviceCheckResult) || !deviceCheckResult.Contains("fastboot"))
                    {
                        LogSimpleStatus($"[失败] Fastboot 设备 {targetDeviceSerial} 不可用");
                        bootflash.Value = 0;
                        UpdateTransferRateText("0MB/s");
                        FlashBootButton.IsEnabled = true;
                        return;
                    }
                    
                    deviceDetected = true;
                }

                // 检测到设备处于fastboot模式后，自动停止设备检测
                // 创建一个模拟的按钮对象来调用Button_Click_1方法（停止检测设备）
                var simulatedStopButton = new System.Windows.Controls.Button();
                simulatedStopButton.Content = "停止检测设备";
                Button_Click_1(simulatedStopButton, null);

                bootflash.Value = 10; // 设备检测完成
                bootflash.Value = 20;

                // 执行刷入命令，进度将由fastboot实时输出控制
                string flashCommand = $"flash {selectedPartition} \"{bootFilePath}\""; 
                string flashResult = await ExecuteFastbootCommandWithSerial(fastbootPath, flashCommand, targetDeviceSerial);

                if (flashResult.StartsWith("ERROR_DETECTED"))
                {
                    LogSimpleStatus($"[失败] {GetFastbootErrorSummary(flashResult)}");
                    bootflash.Value = 0;
                    UpdateTransferRateText("0MB/s");
                    // 错误时也不隐藏进度条
                }
                else if (flashResult.Contains("OKAY", StringComparison.OrdinalIgnoreCase) ||
                         flashResult.Contains("finished", StringComparison.OrdinalIgnoreCase))
                {
                    LogSimpleStatus($"Flashing {selectedPartition}.img...OK");

                    // 检查是否需要自动重启
                    if (AutoRebootCheckBox.IsChecked == true)
                    {
                        LogSimpleStatus("[Rebooting]发送重启命令...");
                        bootflash.Value = 95;
                        
                        string rebootResult = await ExecuteFastbootCommandWithSerial(fastbootPath, "reboot", targetDeviceSerial);
                        
                        if (string.IsNullOrEmpty(rebootResult) ||
                            rebootResult.Contains("finished", StringComparison.OrdinalIgnoreCase) ||
                            rebootResult.Contains("OKAY", StringComparison.OrdinalIgnoreCase))
                        {
                        }
                        else
                        {
                            // 如果重启失败，显示错误信息
                            LogSimpleStatus($"[警告] 重启命令状态未知 | {GetFastbootErrorSummary(rebootResult)}");
                        }
                        
                        // 在执行完fastboot reboot命令后终止所有fastboot.exe进程
                        await KillAllFastbootProcesses();
                    }
                    
                    // 完成进度条
                    bootflash.Value = 100;
                    totalStopwatch.Stop();
                    LogSimpleStatus($"任务结束,耗时{totalStopwatch.Elapsed.TotalSeconds:F1}秒.");
                    await Task.Delay(2000); // 显示完成状态2秒
                    
                    // 镜像刷入完成后，自动开始设备检测
                    // 创建一个模拟的按钮对象来调用Button_Click_1方法（开始检测设备）
                    var simulatedStartButton = new System.Windows.Controls.Button();
                    simulatedStartButton.Content = "开始检测设备";
                    Button_Click_1(simulatedStartButton, null);
                    
                    // 刷写完成后进度条不隐藏，只重置为0
                    bootflash.Value = 0;
                    UpdateTransferRateText("0MB/s");
                }
                else if (flashResult.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    LogSimpleStatus($"[失败] {GetFastbootErrorSummary(flashResult)}");
                    bootflash.Value = 0;
                    UpdateTransferRateText("0MB/s");
                    // 失败时也不隐藏进度条
                }
                else
                {
                    LogSimpleStatus($"[警告] Fastboot 未返回明确结果 | {GetFastbootErrorSummary(flashResult)}");
                    // 如果没有明确的成功或失败标识，假设成功
                    bootflash.Value = 100;
                    await Task.Delay(2000);
                    
                    // 镜像刷入完成后，自动开始设备检测
                    // 创建一个模拟的按钮对象来调用Button_Click_1方法（开始检测设备）
                    var simulatedStartButton = new System.Windows.Controls.Button();
                    simulatedStartButton.Content = "开始检测设备";
                    Button_Click_1(simulatedStartButton, null);
                    
                    // 完成后不隐藏进度条，只重置为0
                    bootflash.Value = 0;
                    UpdateTransferRateText("0MB/s");
                }
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"[失败] 刷写过程中发生异常 | {ex.Message}");
                //发生异常时也不隐藏进度条，只重置为0
                bootflash.Value = 0;
                UpdateTransferRateText("0MB/s");
            }
            finally
            {
                // 重新启用按钮
                FlashBootButton.IsEnabled = true;
            }
        }

        private static string GetFastbootErrorSummary(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return "Fastboot 未返回详细信息";
            }

            var lines = output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !line.Equals("ERROR_DETECTED", StringComparison.OrdinalIgnoreCase))
                .ToList();

            string? detail = lines.FirstOrDefault(line =>
                line.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("cannot", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("invalid", StringComparison.OrdinalIgnoreCase));

            detail ??= lines.FirstOrDefault();
            return string.IsNullOrWhiteSpace(detail) ? "Fastboot 未返回详细信息" : detail;
        }

        // 执行Fastboot命令的辅助方法（带指定序列号）
        private async Task<string> ExecuteFastbootCommandWithSerial(string fastbootPath, string arguments, string deviceSerial)
        {
            try
            {
                bool showNativeOutput = arguments.TrimStart().StartsWith("flash ", StringComparison.OrdinalIgnoreCase);

                return await Task.Run(() =>
                {
                    object nativeOutputLock = new object();
                    bool hasNativeProgressLine = false;

                    void ShowNativeFastbootLine(string line)
                    {
                        if (!showNativeOutput) return;

                        bool isTransferProgress = Regex.IsMatch(
                            line,
                            @"\(\d+(?:\.\d+)?%\).*\b(?:B/s|KB/s|MB/s|GB/s)\b",
                            RegexOptions.IgnoreCase);

                        lock (nativeOutputLock)
                        {
                            if (isTransferProgress)
                            {
                                if (hasNativeProgressLine)
                                {
                                    UpdateFlashNativeProgress(line);
                                }
                                else
                                {
                                    AppendFlashNativeProgress(line);
                                }
                            }
                            else
                            {
                                AppendToFlashLogTextBox(line);
                            }

                            hasNativeProgressLine = isTransferProgress;
                        }
                    }

                    // 使用指定的设备序列号，添加 -s 参数
                    string finalArguments = arguments;
                    if (!string.IsNullOrEmpty(deviceSerial))
                    {
                        finalArguments = $"-s {deviceSerial} {arguments}";
                    }
                    
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = fastbootPath,
                            Arguments = finalArguments,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };
                    
                    StringBuilder outputBuilder = new StringBuilder();
                    bool hasError = false;
                    
                    // 实时读取输出并解析百分比
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            outputBuilder.AppendLine(e.Data);
                            ShowNativeFastbootLine(e.Data);

                            // 将fastboot输出记录到全局日志中
                            fastbootCompleteLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] [FASTBOOT OUTPUT] {e.Data}");
                            
                            // 检测错误关键字
                            if (e.Data.ToLower().Contains("error"))
                            {
                                hasError = true;
                            }
                            
                            // 解析百分比并更新进度条
                            ParseProgressAndUpdateBar(e.Data);
                        }
                    };
                    
                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            outputBuilder.AppendLine(e.Data);
                            ShowNativeFastbootLine(e.Data);

                            // 将fastboot错误输出记录到全局日志中
                            fastbootCompleteLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] [FASTBOOT ERROR] {e.Data}");
                            
                            // 检测是否为真正的错误信息
                            bool isRealError = e.Data.ToLower().Contains("error") || 
                                             e.Data.ToLower().Contains("failed") || 
                                             e.Data.ToLower().Contains("cannot") ||
                                             e.Data.ToLower().Contains("invalid");
                            
                            // 如果是真正的错误，添加错误前缀；否则正常显示
                            if (isRealError)
                            {
                                hasError = true;
                            }
                            
                            // 解析百分比并更新进度条
                            ParseProgressAndUpdateBar(e.Data);
                        }
                    };
                    
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();
                    
                    string result = outputBuilder.ToString();
                    
                    // 如果检测到错误，在返回值中标记
                    if (hasError)
                    {
                        result = "ERROR_DETECTED\n" + result;
                    }
                    
                    return result;
                });
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"执行命令失败: {ex.Message}");
                return $"执行命令失败: {ex.Message}";
            }
        }

        // 执行Fastboot命令的辅助方法
        private async Task<string> ExecuteFastbootCommand(
            string fastbootPath,
            string arguments,
            bool showNativeOutput = false,
            bool parseStatusOutput = true,
            CancellationToken cancellationToken = default)
        {
            Process? process = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string selectedSerial = GetSelectedDeviceSerial();
                
                // 如果有选中的设备序列号，添加 -s 参数
                string finalArguments = arguments;
                if (!string.IsNullOrEmpty(selectedSerial))
                {
                    finalArguments = $"-s {selectedSerial} {arguments}";
                }
                
                process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fastbootPath,
                        Arguments = finalArguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                
                StringBuilder outputBuilder = new StringBuilder();
                bool hasError = false;
                int outputLineCount = 0;
                const int MAX_OUTPUT_LINES = 1000; // 限制输出行数，避免内存占用过大
                
                // 实时读取输出并解析百分比
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        // 【已隐藏】不再显示fastboot原生输出到日志
                        // Dispatcher.BeginInvoke(new Action(() => 
                        // {
                        //     LogToOugaFlash($"[FASTBOOT] {e.Data}", "Gray");
                        // }));
                        
                        // 限制输出缓冲区大小，只保留最近的输出
                        if (outputLineCount < MAX_OUTPUT_LINES)
                        {
                            outputBuilder.AppendLine(e.Data);
                            outputLineCount++;
                        }
                        else if (outputLineCount == MAX_OUTPUT_LINES)
                        {
                            outputBuilder.AppendLine("... (输出过多，已省略部分内容) ...");
                            outputLineCount++;
                        }

                        if (showNativeOutput)
                        {
                            AppendToFlashLogTextBox(e.Data);
                        }
                        
                        // 只记录关键日志，不记录每一行进度
                        if (!e.Data.Contains("%") && !e.Data.Contains("MB/s"))
                        {
                            fastbootCompleteLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] [FASTBOOT OUTPUT] {e.Data}");
                        }
                        
                        // 检测错误关键字
                        if (e.Data.ToLower().Contains("error"))
                        {
                            hasError = true;
                        }
                        
                        if (!showNativeOutput && parseStatusOutput)
                        {
                            // 解析百分比并更新进度条（不阻塞）
                            ParseProgressAndUpdateBar(e.Data);

                            // 只在关键状态时更新UI，减少UI消息队列压力
                            if (e.Data.Contains("Sending") || e.Data.Contains("Writing") ||
                                e.Data.Contains("OKAY") || e.Data.Contains("Finished") ||
                                e.Data.ToLower().Contains("error"))
                            {
                                Dispatcher.BeginInvoke(new Action(() => ParseAndLogSimpleStatus(e.Data)));
                            }
                        }
                    }
                };
                
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        // 【已隐藏】不再显示fastboot错误输出到日志
                        // Dispatcher.BeginInvoke(new Action(() => 
                        // {
                        //     LogToOugaFlash($"[FASTBOOT ERROR] {e.Data}", "Red");
                        // }));
                        
                        // 限制输出缓冲区大小
                        if (outputLineCount < MAX_OUTPUT_LINES)
                        {
                            outputBuilder.AppendLine(e.Data);
                            outputLineCount++;
                        }

                        if (showNativeOutput)
                        {
                            AppendToFlashLogTextBox(e.Data);
                        }
                        
                        // 记录错误日志
                        fastbootCompleteLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] [FASTBOOT ERROR] {e.Data}");
                        
                        // 检测错误关键字
                        if (e.Data.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                            e.Data.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
                        {
                            hasError = true;
                            if (!showNativeOutput && parseStatusOutput)
                            {
                                Dispatcher.BeginInvoke(new Action(() => LogSimpleStatus($"错误: {e.Data}")));
                            }
                        }
                        else if (!showNativeOutput && parseStatusOutput)
                        {
                            // 只在关键状态时更新UI
                            if (e.Data.Contains("Sending") || e.Data.Contains("Writing") || 
                                e.Data.Contains("OKAY") || e.Data.Contains("Finished"))
                            {
                                Dispatcher.BeginInvoke(new Action(() => ParseAndLogSimpleStatus(e.Data)));
                            }
                        }
                        
                        if (!showNativeOutput && parseStatusOutput)
                        {
                            // 解析百分比并更新进度条
                            ParseProgressAndUpdateBar(e.Data);
                        }
                    }
                };
                
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                
                // 等待进程完成；停止页面操作时只终止本次命令进程。
                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                    // 确保所有异步输出都已处理完成
                    process.WaitForExit();
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                    }
                    throw;
                }
                
                // 给一点时间让所有事件处理器完成
                await Task.Delay(100, cancellationToken);
                
                string result = outputBuilder.ToString();
                
                // 如果检测到错误，在返回值中标记
                if (hasError)
                {
                    result = "ERROR_DETECTED\n" + result;
                }
                
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"执行命令失败: {ex.Message}");
                return $"执行命令失败: {ex.Message}";
            }
            finally
            {
                process?.Dispose();
            }
        }

        // 解析fastboot输出中的百分比并更新进度条
        private void ParseProgressAndUpdateBar(string output)
        {
            if (string.IsNullOrEmpty(output)) return;
            
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // 尝试解析百分比
                    var percentageMatch = System.Text.RegularExpressions.Regex.Match(output, @"\((\d+(?:\.\d+)?)%\)|(\d+(?:\.\d+)?)%");
                    if (percentageMatch.Success)
                    {
                        // 获取匹配的百分比值（可能在第1组或第2组）
                        string percentageStr = percentageMatch.Groups[1].Success ? percentageMatch.Groups[1].Value : percentageMatch.Groups[2].Value;
                        
                        if (double.TryParse(percentageStr, out double percentage))
                        {
                            // 确保百分比在有效范围内
                            if (percentage >= 0 && percentage <= 100)
                            {
                                bootflash.Value = (int)Math.Round(percentage);
                                
                                // 同时更新FlashProgressBar
                                UpdateProgressBarValue(percentage);
                            }
                        }
                    }
                    
                    // 尝试解析传输速率，兼容 B/s、KB/s、MB/s、GB/s
                    var speedMatch = System.Text.RegularExpressions.Regex.Match(
                        output,
                        @"(\d+(?:\.\d+)?)\s*(B/s|KB/s|MB/s|GB/s)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (speedMatch.Success)
                    {
                        string speed = speedMatch.Groups[1].Value;
                        string unit = speedMatch.Groups[2].Value.ToUpperInvariant();
                        if (double.TryParse(speed, out double speedValue))
                        {
                            UpdateTransferRateText($"{speedValue:F2}{unit}");
                            System.Diagnostics.Debug.WriteLine($"[速率更新] {speedValue:F2}{unit}");
                        }
                    }
                    
                    // 如果没有找到百分比，则根据关键字设置固定进度
                    if (output.Contains("Writing") || output.Contains("Sending"))
                    {
                        // 根据不同的fastboot阶段更新进度条
                        if (output.Contains("Sending"))
                        {
                            // 开始发送数据阶段，设置进度为30%
                            if (bootflash.Value < 30)
                            {
                                bootflash.Value = 30;
                                if (FlashProgressBar != null && FlashProgressBar.Value < 30)
                                {
                                    UpdateProgressBarValue(30);
                                }
                            }
                        }
                        else if (output.Contains("Writing"))
                        {
                            // 开始写入阶段，设置进度为70%
                            if (bootflash.Value < 70)
                            {
                                bootflash.Value = 70;
                                if (FlashProgressBar != null && FlashProgressBar.Value < 70)
                                {
                                    UpdateProgressBarValue(70);
                                }
                            }
                        }
                    }
                    else if (output.Contains("OKAY"))
                    {
                        // 操作成功完成
                        bootflash.Value = 90; // 设置为90%，等待最终完成
                        UpdateProgressBarValue(90);
                    }
                    else if (output.Contains("Finished") || output.Contains("镜像刷入成功"))
                    {
                        // 刷入完全完成
                        bootflash.Value = 100;
                        UpdateProgressBarValue(100);
                        UpdateTransferRateText("完成");
                    }
                }));
            }
            catch (Exception ex)
            {
                // 静默处理解析错误，避免影响主流程
                System.Diagnostics.Debug.WriteLine($"解析进度时出错: {ex.Message}");
            }
        }

        // 向日志文本框添加消息的辅助方法
        private void LogToFlashTextBox(string message)
        {
            AppendStyledFlashLog(message, FlashLogDefaultBrush);
        }

        private static readonly System.Windows.Media.Brush FlashLogTimeBrush =
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184));
        private static readonly System.Windows.Media.Brush FlashLogDefaultBrush =
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85));
        private static readonly System.Windows.Media.Brush FlashLogInfoBrush =
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235));
        private static readonly System.Windows.Media.Brush FlashLogWaitingBrush =
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246));
        private static readonly System.Windows.Media.Brush FlashLogSuccessBrush =
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 163, 74));
        private static readonly System.Windows.Media.Brush FlashLogWarningBrush =
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(217, 119, 6));
        private static readonly System.Windows.Media.Brush FlashLogRebootBrush =
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(124, 58, 237));
        private static readonly System.Windows.Media.Brush FlashLogErrorBrush =
            new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));

        private Run? _flashCountdownRun;
        private Run? _flashNativeProgressTimeRun;
        private Run? _flashNativeProgressRun;

        private void AppendStyledFlashLog(
            string message,
            System.Windows.Media.Brush messageBrush,
            bool bold = false)
        {
            if (FlashLogTextBox == null) return;

            Dispatcher.Invoke(() =>
            {
                var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
                paragraph.Inlines.Add(new Run($"[{DateTime.Now:HH:mm:ss}] ")
                {
                    Foreground = FlashLogTimeBrush
                });
                paragraph.Inlines.Add(new Run(message)
                {
                    Foreground = messageBrush,
                    FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal
                });
                FlashLogTextBox.Document.Blocks.Add(paragraph);
                FlashLogTextBox.ScrollToEnd();
            });
        }

        // 简化的状态日志方法，只显示关键状态信息
        private void LogSimpleStatus(string message)
        {
            System.Windows.Media.Brush brush = FlashLogDefaultBrush;
            bool bold = false;

            if (message.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("错误", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("失败", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogErrorBrush;
                bold = true;
            }
            else if (message.Contains("警告", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogWarningBrush;
                bold = true;
            }
            else if (message.StartsWith("Flashing ", StringComparison.OrdinalIgnoreCase) ||
                     message.StartsWith("任务结束", StringComparison.OrdinalIgnoreCase) ||
                     message.StartsWith("操作完成", StringComparison.OrdinalIgnoreCase) ||
                     message.StartsWith("ADB修复完成", StringComparison.OrdinalIgnoreCase) ||
                     message.StartsWith("强开成功", StringComparison.OrdinalIgnoreCase) ||
                     message.StartsWith("基带调试端口已开启", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogSuccessBrush;
                bold = true;
            }
            else if (message.StartsWith("[Rebooting]", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogRebootBrush;
                bold = true;
            }
            else if (message.StartsWith("已连接", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogInfoBrush;
                bold = true;
            }
            else if (message.StartsWith("检测结果", StringComparison.OrdinalIgnoreCase) ||
                     message.StartsWith("处理器类型", StringComparison.OrdinalIgnoreCase) ||
                     message.StartsWith("检查ROOT权限", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogInfoBrush;
                bold = true;
            }
            else if (message.StartsWith("请按", StringComparison.OrdinalIgnoreCase) ||
                     message.StartsWith("请盯住", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogWarningBrush;
                bold = true;
            }
            else if (message.StartsWith("正在", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogInfoBrush;
            }
            else if (message.StartsWith("> fastboot", StringComparison.OrdinalIgnoreCase) ||
                     message.StartsWith("> adb", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogInfoBrush;
                bold = true;
            }

            AppendStyledFlashLog(message, brush, bold);
        }

        private void AppendFlashCountdown(string message)
        {
            if (FlashLogTextBox == null) return;

            Dispatcher.Invoke(() =>
            {
                var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
                paragraph.Inlines.Add(new Run($"[{DateTime.Now:HH:mm:ss}] ")
                {
                    Foreground = FlashLogTimeBrush
                });
                _flashCountdownRun = new Run(message)
                {
                    Foreground = FlashLogWaitingBrush,
                    FontWeight = FontWeights.SemiBold
                };
                paragraph.Inlines.Add(_flashCountdownRun);
                FlashLogTextBox.Document.Blocks.Add(paragraph);
                FlashLogTextBox.ScrollToEnd();
            });
        }

        private void UpdateFlashCountdown(string message)
        {
            Dispatcher.Invoke(() =>
            {
                if (_flashCountdownRun != null)
                {
                    _flashCountdownRun.Text = message;
                    FlashLogTextBox.ScrollToEnd();
                }
            });
        }

        private void AppendFlashNativeProgress(string message)
        {
            if (FlashLogTextBox == null) return;

            Dispatcher.Invoke(() =>
            {
                var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
                _flashNativeProgressTimeRun = new Run($"[{DateTime.Now:HH:mm:ss}] ")
                {
                    Foreground = FlashLogTimeBrush
                };
                _flashNativeProgressRun = new Run(message)
                {
                    Foreground = FlashLogInfoBrush,
                    FontWeight = FontWeights.SemiBold
                };
                paragraph.Inlines.Add(_flashNativeProgressTimeRun);
                paragraph.Inlines.Add(_flashNativeProgressRun);
                FlashLogTextBox.Document.Blocks.Add(paragraph);
                FlashLogTextBox.ScrollToEnd();
            });
        }

        private void UpdateFlashNativeProgress(string message)
        {
            Dispatcher.Invoke(() =>
            {
                if (_flashNativeProgressTimeRun != null && _flashNativeProgressRun != null)
                {
                    _flashNativeProgressTimeRun.Text = $"[{DateTime.Now:HH:mm:ss}] ";
                    _flashNativeProgressRun.Text = message;
                    FlashLogTextBox.ScrollToEnd();
                }
            });
        }
        
        // 向FlashLogTextBox添加内容并自动滚动的辅助方法
        private void AppendToFlashLogTextBox(string content)
        {
            System.Windows.Media.Brush brush = FlashLogDefaultBrush;
            bool bold = false;

            if (content.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogErrorBrush;
                bold = true;
            }
            else if (content.Contains("OKAY", StringComparison.OrdinalIgnoreCase) ||
                     content.StartsWith("Finished", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogSuccessBrush;
            }
            else if (content.StartsWith("Sending", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogInfoBrush;
            }
            else if (content.StartsWith("Writing", StringComparison.OrdinalIgnoreCase))
            {
                brush = FlashLogRebootBrush;
            }

            AppendStyledFlashLog(content, brush, bold);
        }
        
        // 解析fastboot输出并显示简化状态信息
        private void ParseAndLogSimpleStatus(string output)
        {
            if (string.IsNullOrEmpty(output)) return;
            
            string lowerOutput = output.ToLower();
            
            // 检测分区刷入开始
            if (lowerOutput.Contains("sending") && lowerOutput.Contains("kb"))
            {
                // 提取分区名称
                var match = System.Text.RegularExpressions.Regex.Match(output, @"Sending '([^']+)'");
                if (match.Success)
                {
                    string partitionName = match.Groups[1].Value;
                    currentFlashingPartition = partitionName; // 保存当前刷入的分区名称
                    LogSimpleStatus($"准备刷入到{partitionName}分区...");
                }
                return;
            }
            
            // 检测分区刷入进行中
            if (lowerOutput.Contains("writing") && lowerOutput.Contains("%"))
            {
                // 提取分区名称和进度
                var match = System.Text.RegularExpressions.Regex.Match(output, @"([^:]+):\s*[\d\.]+\s*MB/[\d\.]+\s*MB\s*\(([\d\.]+)%\)");
                if (match.Success)
                {
                    string partitionName = match.Groups[1].Value.Trim();
                    string progress = match.Groups[2].Value;
                    currentFlashingPartition = partitionName; // 更新当前刷入的分区名称
                    LogSimpleStatus($"正在刷入到{partitionName}分区...");
                }
                else if (!string.IsNullOrEmpty(currentFlashingPartition))
                {
                    // 使用之前保存的分区名称
                    LogSimpleStatus($"正在刷入到{currentFlashingPartition}分区...");
                }
                else
                {
                    // 如果无法提取具体信息，使用通用格式
                    LogSimpleStatus("正在刷入到分区...");
                }
                return;
            }
            
            // 检测刷入完成 - 只在finished完成时记录成功，避免重复
            if (lowerOutput.Contains("finished") && lowerOutput.Contains("total time"))
            {
                // 使用当前刷入的分区名称
                if (!string.IsNullOrEmpty(currentFlashingPartition))
                {
                    LogSimpleStatus($"{currentFlashingPartition}镜像刷入成功...");
                }
                else
                {
                    LogSimpleStatus("镜像刷入成功...");
                }
                // 清空当前分区名称，为下次刷入做准备
                currentFlashingPartition = "";
                return;
            }
            
            // 检测单个操作完成但不记录成功信息，避免重复
            if (lowerOutput.Contains("okay") && !lowerOutput.Contains("finished"))
            {
                return;
            }
            
            // 检测错误信息
            if (lowerOutput.Contains("failed") || lowerOutput.Contains("error"))
            {
                LogSimpleStatus($"错误: {output}");
                return;
            }
        }
        
        // 查找父级控件的辅助方法
        private T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            
            T parent = parentObject as T;
            if (parent != null)
                return parent;
            else
                return FindParent<T>(parentObject);
        }

        // 全自动投屏复选框选中事件
        private void AutoMirrorCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            isAutoMirrorEnabled = true;
            StartAutoMirrorTimer();
        }

        // 全自动投屏复选框取消选中事件
        private void AutoMirrorCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            isAutoMirrorEnabled = false;
            StopAutoMirrorTimer();
        }

        // 启动全自动投屏定时器
        private void StartAutoMirrorTimer()
        {
            if (autoMirrorTimer == null)
            {
                autoMirrorTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2) // 每2秒检查一次
                };
                autoMirrorTimer.Tick += AutoMirrorTimer_Tick;
            }
            autoMirrorTimer.Start();
        }

        // 停止全自动投屏定时器
        private void StopAutoMirrorTimer()
        {
            autoMirrorTimer?.Stop();
        }

        // 全自动投屏定时器事件
        private void AutoMirrorTimer_Tick(object? sender, EventArgs e)
        {
            if (isAutoMirrorEnabled && !IsScrcpyProcessRunning())
            {
                StartScrcpyProcess();
            }
        }

        private bool IsScrcpyProcessRunning()
        {
            try
            {
                var scrcpyProcesses = Process.GetProcessesByName("scrcpy");
                return scrcpyProcesses.Length > 0;
            }
            catch
            {
                return false;
            }
        }
        private async void StartScrcpyProcess()
        {
            if (isScrcpyStarting || (scrcpyProcess != null && !scrcpyProcess.HasExited))
            {
                return;
            }

            isScrcpyStarting = true;

            try
            {
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string scrcpyPath = Path.Combine(appDirectory, "platform-tools", "scrcpy.exe");
                if (File.Exists(scrcpyPath))
                {
                    var maxFpsSlider = this.FindName("MaxFpsSlider") as Slider;
                    int maxFps = maxFpsSlider != null ? (int)maxFpsSlider.Value : 60;
                    var bitrateSlider = this.FindName("BitrateSlider") as Slider;
                    int bitrate = bitrateSlider != null ? (int)bitrateSlider.Value : 8; 
                    
                    int maxSize = GetWindowMaxSize();
                    string selectedSerial = GetSelectedDeviceSerial();

                    // 设备重连时，设备列表和选中项的恢复可能晚于自动投屏定时器。
                    // 此时必须等待，不能以空序列号启动，否则不会生成自定义窗口标题。
                    if (string.IsNullOrWhiteSpace(selectedSerial))
                    {
                        return;
                    }

                    string arguments = await BuildScrcpyLaunchArgumentsAsync(selectedSerial, bitrate, maxFps, maxSize);
                    
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = scrcpyPath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        WorkingDirectory = Path.GetDirectoryName(scrcpyPath),
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    scrcpyProcess = Process.Start(startInfo);
                    
                    if (scrcpyProcess != null)
                    {
                        Process startedProcess = scrcpyProcess;
                        InitializeScrcpyControlBarTracking(startedProcess);

                        AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] 全自动投屏已触发，正在启动scrcpy...\n");
                        AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] 目标设备: {selectedSerial}\n");
                        AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] 命令: {scrcpyPath} {arguments}\n");

                        startedProcess.OutputDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrWhiteSpace(e.Data))
                            {
                                AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] [scrcpy] {e.Data}\n");
                            }
                        };

                        startedProcess.ErrorDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrWhiteSpace(e.Data))
                            {
                                AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] [scrcpy] {e.Data}\n");
                            }
                        };

                        startedProcess.EnableRaisingEvents = true;
                        startedProcess.Exited += (sender, e) =>
                        {
                            AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] 自动投屏scrcpy进程已退出，等待设备重新连接...\n");
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                CloseScrcpyControlBar();
                                if (ReferenceEquals(scrcpyProcess, startedProcess))
                                {
                                    scrcpyProcess = null;
                                }
                            }));
                        };

                        startedProcess.BeginOutputReadLine();
                        startedProcess.BeginErrorReadLine();
                    }
                    else
                    {
                        AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] ERROR: 自动投屏启动scrcpy失败！\n");
                    }
                }
                else
                {
                    AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] ERROR: 未找到scrcpy.exe，请检查platform-tools目录！\n");
                }
            }
            catch (Exception ex)
            {
                AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] ERROR: 自动投屏启动失败: {ex.Message}\n");
            }
            finally
            {
                isScrcpyStarting = false;
            }
        }

        private async void ExecuteFastbootCommandButton_Click(object sender, RoutedEventArgs e)
        {
            var commandTextBox = this.FindName("FastbootCommandTextBox") as System.Windows.Controls.TextBox;
            var logTextBox = this.FindName("FastbootLogTextBox") as System.Windows.Controls.RichTextBox;
            
            if (commandTextBox != null && logTextBox != null)
            {
                string command = commandTextBox.Text.Trim();
                if (!string.IsNullOrEmpty(command))
                {
                    await ExecuteFastbootCommand(command);
                    string result = "命令已执行";
                    
                    if (result.StartsWith("ERROR_DETECTED"))
                    {
                        LogToFastboot("命令执行失败：检测到错误信息！", "Red");
                    }
                    else
                    {
                        LogToFastboot("命令执行完成", "Green");
                    }
                }
                else
                {
                    LogToFastboot("请输入fastboot命令", "Yellow");
                }
            }
        }

        private void ClearFastbootLogButton_Click(object sender, RoutedEventArgs e)
        {
            var logTextBox = this.FindName("FastbootLogTextBox") as System.Windows.Controls.RichTextBox;
            if (logTextBox != null)
            {
                logTextBox.Document.Blocks.Clear();
                logTextBox.ScrollToHome();
            }
        }

        private async void ReadPartitionTableButton_Click(object sender, RoutedEventArgs e)
        {
            var logTextBox = this.FindName("FastbootLogTextBox") as System.Windows.Controls.RichTextBox;
            var dataGrid = this.FindName("PartitionTableDataGrid") as DataGrid;
            CancellationTokenSource? operationCancellation = null;
            
            if (logTextBox != null && dataGrid != null)
            {
                try
                {
                    string fastbootPath = GetFastbootPath();
                    if (string.IsNullOrEmpty(fastbootPath))
                    {
                        LogToFastboot("缺少fastboot.exe", "Red");
                        return;
                    }
                    
                    operationCancellation = BeginFastbootVisualizationOperation();
                    CancellationToken cancellationToken = operationCancellation.Token;
                    ReadPartitionTableButton.IsEnabled = false;

                    bool deviceReady;
                    _fastbootVisualizationWaitingForDevice = true;
                    try
                    {
                        deviceReady = await WaitForFastbootDeviceWithCountdown(
                            fastbootPath,
                            60,
                            cancellationToken);
                    }
                    finally
                    {
                        _fastbootVisualizationWaitingForDevice = false;
                    }
                    if (!deviceReady)
                    {
                        LogToFastboot("连接超时...", "Red");
                        return;
                    }

                    string result = await ExecuteFastbootCommand(
                        fastbootPath,
                        "getvar all",
                        cancellationToken: cancellationToken);
                    
                    if (result.StartsWith("ERROR_DETECTED"))
                    {
                        LogToFastboot("命令执行失败", "Red");
                        return;
                    }
                    
                    var partitions = ParsePartitionInfo(result);
                    var filteredPartitions = partitions.Where(p => 
                        !p.PartitionName.ToLower().Contains("slot") &&
                        !new[] { "sda", "sdb", "sdc", "sdd", "sde", "sdf" }.Contains(p.PartitionName.ToLower())
                    ).ToList();
                    
                    await ShowPartitionTableWithRefreshAsync(dataGrid, filteredPartitions);
                    ApplyFastbootVisualizationPartitionProtectionState(adbMode: false);
                    
                    string deviceSerial = GetSelectedDeviceSerial();
                    if (string.IsNullOrWhiteSpace(deviceSerial))
                    {
                        deviceSerial = ExtractFastbootVar(result, "serialno");
                        if (IsMissingFastbootValue(deviceSerial))
                        {
                            string devicesOutput = await GetCommandOutput(fastbootPath, "devices", cancellationToken);
                            string firstFastbootLine = devicesOutput
                                .Split('\n')
                                .Select(l => l.Trim())
                                .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l) && l.Contains("fastboot", StringComparison.OrdinalIgnoreCase)) ?? "";

                            if (!string.IsNullOrWhiteSpace(firstFastbootLine))
                            {
                                deviceSerial = firstFastbootLine
                                    .Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                    .FirstOrDefault() ?? "";
                            }
                        }
                    }
                    if (IsMissingFastbootValue(deviceSerial)) deviceSerial = "--";

                    LogToFastbootStyled(
                        ("等待设备...", "Black", false),
                        ($"[{deviceSerial}]", "Purple", true));
                    LogToFastbootStyled(
                        ("读取分区表成功,共", "Black", false),
                        ($"{partitions.Count}", "Purple", true),
                        ("个分区.", "Black", false));

                    // getvar all 已包含绝大多数设备信息。优先复用本次结果，
                    // 仅对缺失字段启动补充命令，避免重复创建五个 fastboot 进程。
                    string productName = ExtractFastbootVar(result, "product");
                    string unlockStatus = ExtractFastbootVar(result, "unlocked");
                    string deviceInfo = result;
                    string currentSlot = ExtractFastbootVar(result, "current-slot");
                    string isUserspace = ExtractFastbootVar(result, "is-userspace");
                    string serialArgPrefix = deviceSerial == "--" ? "" : $"-s {deviceSerial} ";
                    Task<string>? productFallbackTask = IsMissingFastbootValue(productName)
                        ? GetCommandOutput(fastbootPath, $"{serialArgPrefix}getvar product", cancellationToken)
                        : null;
                    bool hasUnlockFlag = result.Contains("Device unlocked: true", StringComparison.OrdinalIgnoreCase) ||
                                         result.Contains("Device unlocked: false", StringComparison.OrdinalIgnoreCase);
                    Task<string>? unlockFallbackTask = IsMissingFastbootValue(unlockStatus) && !hasUnlockFlag
                        ? GetCommandOutput(fastbootPath, $"{serialArgPrefix}getvar unlocked", cancellationToken)
                        : null;
                    Task<string>? deviceInfoFallbackTask = IsMissingFastbootValue(unlockStatus) && !hasUnlockFlag
                        ? GetCommandOutput(fastbootPath, $"{serialArgPrefix}oem device-info", cancellationToken)
                        : null;
                    Task<string>? slotFallbackTask = IsMissingFastbootValue(currentSlot)
                        ? GetCommandOutput(fastbootPath, $"{serialArgPrefix}getvar current-slot", cancellationToken)
                        : null;
                    Task<string>? userspaceFallbackTask = IsMissingFastbootValue(isUserspace)
                        ? GetCommandOutput(fastbootPath, $"{serialArgPrefix}getvar is-userspace", cancellationToken)
                        : null;

                    var fallbackTasks = new[]
                    {
                        productFallbackTask,
                        unlockFallbackTask,
                        deviceInfoFallbackTask,
                        slotFallbackTask,
                        userspaceFallbackTask
                    }.Where(task => task != null).Cast<Task<string>>().ToArray();
                    if (fallbackTasks.Length > 0)
                    {
                        await Task.WhenAll(fallbackTasks);
                    }

                    if (productFallbackTask != null)
                        productName = ExtractFastbootVar(await productFallbackTask, "product");
                    if (unlockFallbackTask != null)
                        unlockStatus = ExtractFastbootVar(await unlockFallbackTask, "unlocked");
                    if (deviceInfoFallbackTask != null)
                        deviceInfo = await deviceInfoFallbackTask;
                    if (slotFallbackTask != null)
                        currentSlot = ExtractFastbootVar(await slotFallbackTask, "current-slot");
                    if (userspaceFallbackTask != null)
                        isUserspace = ExtractFastbootVar(await userspaceFallbackTask, "is-userspace");

                    string unlockText = "--";
                    if (!string.IsNullOrWhiteSpace(deviceInfo) && deviceInfo.Contains("Device unlocked: true", StringComparison.OrdinalIgnoreCase))
                    {
                        unlockText = "已解锁";
                    }
                    else if (!string.IsNullOrWhiteSpace(deviceInfo) && deviceInfo.Contains("Device unlocked: false", StringComparison.OrdinalIgnoreCase))
                    {
                        unlockText = "未解锁";
                    }
                    else if (unlockStatus.Equals("yes", StringComparison.OrdinalIgnoreCase))
                    {
                        unlockText = "已解锁";
                    }
                    else if (unlockStatus.Equals("no", StringComparison.OrdinalIgnoreCase))
                    {
                        unlockText = "未解锁";
                    }

                    string slotText = currentSlot.Equals("a", StringComparison.OrdinalIgnoreCase) ? "A槽" :
                                      currentSlot.Equals("b", StringComparison.OrdinalIgnoreCase) ? "B槽" : "--";

                    bool inFastbootD = isUserspace.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                                       isUserspace.Equals("true", StringComparison.OrdinalIgnoreCase);

                    LogFastbootDeviceInfo(
                        string.IsNullOrWhiteSpace(productName) ? "--" : productName,
                        unlockText,
                        slotText,
                        inFastbootD ? "FastbootD" : "Fastboot");
                    
                    // 更新Userdata分区大小显示
                }
                catch (OperationCanceledException)
                {
                    LogFastbootVisualizationOperationStopped("读取Fastboot分区表");
                }
                catch (Exception ex)
                {
                    LogToFastboot($"读取超时:...{ex.Message}", "Red");
                }
                finally
                {
                    EndFastbootVisualizationOperation(operationCancellation);
                    ReadPartitionTableButton.IsEnabled = true;
                }
            }
        }
        
        private async void AdbReadPartitionTableButton_Click(object sender, RoutedEventArgs e)
        {
            var logTextBox = this.FindName("FastbootLogTextBox") as System.Windows.Controls.RichTextBox;
            var dataGrid = this.FindName("PartitionTableDataGrid") as DataGrid;
            CancellationTokenSource? operationCancellation = null;
            
            if (logTextBox != null && dataGrid != null)
            {
                try
                {
                    string adbPath = GetAdbPath();
                    if (string.IsNullOrEmpty(adbPath))
                    {
                        LogToFastboot("缺少adb.exe", "Red");
                        return;
                    }

                    operationCancellation = BeginFastbootVisualizationOperation();
                    CancellationToken cancellationToken = operationCancellation.Token;
                    AdbReadPartitionTableButton.IsEnabled = false;

                    string devicesOutput = await GetCommandOutput(adbPath, "devices", cancellationToken);
                    var adbDevices = devicesOutput
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(line => Regex.Match(line.Trim(), @"^(\S+)\s+(device|unauthorized|offline|no permissions)$", RegexOptions.IgnoreCase))
                        .Where(match => match.Success)
                        .Select(match => new
                        {
                            Serial = match.Groups[1].Value,
                            State = match.Groups[2].Value.ToLowerInvariant()
                        })
                        .ToList();

                    string selectedSerial = GetSelectedDeviceSerial();
                    var targetDevice = !string.IsNullOrWhiteSpace(selectedSerial)
                        ? adbDevices.FirstOrDefault(device => device.Serial.Equals(selectedSerial, StringComparison.OrdinalIgnoreCase))
                        : adbDevices.Count == 1 ? adbDevices[0] : null;

                    if (adbDevices.Count == 0 || (!string.IsNullOrWhiteSpace(selectedSerial) && targetDevice == null))
                    {
                        LogToFastbootStyled(
                            ("连接设备...", "Black", false),
                            ("未检测到设备", "Red", true));
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(selectedSerial) && adbDevices.Count > 1)
                    {
                        LogToFastboot("检测到多个ADB设备，请先在设备列表中选择目标设备", "Yellow");
                        return;
                    }

                    if (targetDevice?.State == "unauthorized")
                    {
                        LogToFastboot("ADB设备未授权，请在手机上允许USB调试授权", "Red");
                        return;
                    }

                    if (targetDevice?.State == "offline")
                    {
                        LogToFastboot("ADB设备处于离线状态，请重新连接USB后重试", "Red");
                        return;
                    }

                    if (targetDevice?.State == "no permissions")
                    {
                        LogToFastboot("ADB设备无访问权限，请检查ADB驱动和USB权限", "Red");
                        return;
                    }

                    if (targetDevice?.State != "device")
                    {
                        LogToFastboot("ADB设备当前不可用，请检查设备连接状态", "Red");
                        return;
                    }

                    LogToFastbootStyled(
                        ("连接设备...", "Black", false),
                        ($"[{targetDevice.Serial}]", "Purple", true));

                    string adbSerialPrefix = string.IsNullOrWhiteSpace(targetDevice.Serial)
                        ? string.Empty
                        : $"-s {targetDevice.Serial} ";
                    string rootCheckResult = await GetCommandOutput(
                        adbPath,
                        $"{adbSerialPrefix}shell \"su -c 'id'\"",
                        cancellationToken);
                    if (!Regex.IsMatch(rootCheckResult ?? string.Empty, @"\buid=0\b", RegexOptions.IgnoreCase))
                    {
                        LogToFastbootStyled(
                            ("未授予", "Black", false),
                            ("Shell ROOT", "Red", true),
                            ("权限", "Black", false));
                        return;
                    }

                    LogToFastbootStyled(
                        ("已授予 ", "Black", false),
                        ("Shell ROOT", "Blue", true),
                        (" 权限", "Black", false));

                    (string partitionRoot, List<PartitionInfo> partitions) =
                        await ReadAdbPartitionsFromAvailableRootAsync(
                            adbPath,
                            targetDevice.Serial,
                            cancellationToken);
                    if (partitions.Count == 0)
                    {
                        LogToFastboot(
                            "未找到可读取的 by-name 分区目录，请确认ROOT授权及设备分区路径",
                            "Red");
                        return;
                    }
                    
                    var filteredPartitions = partitions.Where(p => 
                        !p.PartitionName.ToLower().Contains("slot") &&
                        !new[] { "sda", "sdb", "sdc", "sdd", "sde", "sdf" }.Contains(p.PartitionName.ToLower())
                    ).ToList();
                    
                    await ShowPartitionTableWithRefreshAsync(dataGrid, filteredPartitions, cancellationToken);
                    ApplyFastbootVisualizationPartitionProtectionState(adbMode: true);
                    
                    LogToFastbootStyled(
                        ("分区表读取完成", "Green", true),
                        ("，共", "Black", false),
                        ($"{filteredPartitions.Count}", "Purple", true),
                        ("个分区", "Black", false));
                    
                    // 更新Userdata分区大小显示
                }
                catch (OperationCanceledException)
                {
                    LogFastbootVisualizationOperationStopped("读取ADB分区表");
                }
                catch (Exception ex)
                {
                    LogToFastboot($"ADB读取分区表失败: {ex.Message}", "Red");
                }
                finally
                {
                    EndFastbootVisualizationOperation(operationCancellation);
                    AdbReadPartitionTableButton.IsEnabled = true;
                }
            }
        }

        private async Task<(string Root, List<PartitionInfo> Partitions)>
            ReadAdbPartitionsFromAvailableRootAsync(
                string adbPath,
                string deviceSerial,
                CancellationToken cancellationToken = default)
        {
            string serialPrefix = string.IsNullOrWhiteSpace(deviceSerial)
                ? string.Empty
                : $"-s {deviceSerial} ";
            var candidateRoots = new List<string>
            {
                "/dev/block/by-name",
                "/dev/block/bootdevice/by-name"
            };

            async Task<List<PartitionInfo>> TryReadRootAsync(string root)
            {
                string listingOutput = await GetCommandOutput(
                    adbPath,
                    $"{serialPrefix}shell \"su -c 'ls -l {root}'\"",
                    cancellationToken);
                return ParseAdbPartitionInfo(listingOutput);
            }

            // 优先尝试最常见的两个路径，正常设备只需一次 ls 和一次 /proc/partitions。
            foreach (string root in candidateRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                List<PartitionInfo> partitions = await TryReadRootAsync(root);
                if (partitions.Count > 0)
                {
                    return (root, partitions);
                }
            }

            // 仅在常见路径均失败时才执行 find，避免每次读取都扫描 platform 目录。
            string discoveredRootsOutput = await GetCommandOutput(
                adbPath,
                $"{serialPrefix}shell \"su -c 'find /dev/block/platform -name by-name 2>/dev/null'\"",
                cancellationToken);
            IEnumerable<string> discoveredRoots = Regex.Matches(
                    discoveredRootsOutput ?? string.Empty,
                    @"(?m)^(/dev/block/platform/[^\r\n]*/by-name)\s*$",
                    RegexOptions.IgnoreCase)
                .Cast<Match>()
                .Select(match => match.Groups[1].Value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (string root in discoveredRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                List<PartitionInfo> partitions = await TryReadRootAsync(root);
                if (partitions.Count > 0)
                {
                    return (root, partitions);
                }
            }

            return (string.Empty, new List<PartitionInfo>());
        }

        private async Task ShowPartitionTableWithRefreshAsync(
            DataGrid dataGrid,
            IList<PartitionInfo> partitions,
            CancellationToken cancellationToken = default)
        {
            if (dataGrid == null)
            {
                return;
            }

            bool hadItems = false;
            Dispatcher.Invoke(() =>
            {
                if (dataGrid.ItemsSource != allPartitions)
                {
                    dataGrid.ItemsSource = allPartitions;
                }

                hadItems = allPartitions != null && allPartitions.Count > 0;
                if (hadItems)
                {
                    allPartitions.Clear();
                    dataGrid.Items.Refresh();
                }
            });

            if (hadItems)
            {
                await Task.Delay(120, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Dispatcher.Invoke(() =>
            {
                allPartitions.Clear();
                if (partitions != null)
                {
                    foreach (var p in partitions)
                    {
                        allPartitions.Add(p);
                    }
                }
                dataGrid.Items.Refresh();
                DeactivateXiaomiScriptMode();
            });
        }
        
        private async void ErasePartitionButton_Click(object sender, RoutedEventArgs e)
        {
            var logTextBox = this.FindName("FastbootLogTextBox") as System.Windows.Controls.RichTextBox;
            var dataGrid = this.FindName("PartitionTableDataGrid") as DataGrid;
            CancellationTokenSource? operationCancellation = null;
            
            if (logTextBox != null && dataGrid != null)
            {
                // 获取选中的分区
                var selectedPartitions = new List<string>();
                if (dataGrid.ItemsSource is ObservableCollection<PartitionInfo> partitions)
                {
                    foreach (var partition in partitions)
                    {
                        if (partition.IsSelected)
                        {
                            selectedPartitions.Add(partition.PartitionName);
                        }
                    }
                }
                
                if (selectedPartitions.Count == 0)
                {
                    LogToFastboot("请先选择要擦除的分区", "Red");
                    return;
                }
                
                if (!ShowErasePartitionConfirmationDialog(selectedPartitions))
                {
                    LogToFastboot("用户取消了擦除操作", "Yellow");
                    return;
                }
                
                try
                {
                    operationCancellation = BeginFastbootVisualizationOperation(
                        stopAtCommandBoundary: true);
                    CancellationToken cancellationToken = operationCancellation.Token;

                    // 检查设备连接状态
                    string connectionType = BottomConnectionTypeText?.Text ?? "";
                    if (connectionType == "系统")
                    {
                        await ErasePartitionsUsingAdb(selectedPartitions, logTextBox, cancellationToken);
                    }
                    else if (connectionType == "Fastboot")
                    {
                        await ErasePartitionsUsingFastboot(selectedPartitions, logTextBox, cancellationToken);
                    }
                    else
                    {
                        LogToFastboot("设备未连接或连接状态未知，请检查设备连接", "Red");
                    }
                }
                catch (OperationCanceledException)
                {
                    LogFastbootVisualizationOperationStopped("擦除分区");
                }
                catch (Exception ex)
                {
                    LogToFastboot($"擦除分区失败：{ex.Message}", "Red");
                }
                finally
                {
                    EndFastbootVisualizationOperation(operationCancellation);
                }
            }
        }

        private string SelectSaveDirectory(string title)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = title ?? "请选择保存目录";
                dialog.UseDescriptionForTitle = true;
                dialog.ShowNewFolderButton = true;

                var result = dialog.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
                {
                    return dialog.SelectedPath;
                }
            }
            return "";
        }

        private async void ReadPartitionButton_Click(object sender, RoutedEventArgs e)
        {
            var logTextBox = this.FindName("FastbootLogTextBox") as System.Windows.Controls.RichTextBox;
            var dataGrid = this.FindName("PartitionTableDataGrid") as DataGrid;
            var partitionInfos = dataGrid?.ItemsSource as ObservableCollection<PartitionInfo>;
            CancellationTokenSource? operationCancellation = null;
            
            if (logTextBox != null && dataGrid != null)
            {
                // 获取选中的分区
                var selectedPartitions = new List<string>();
                if (partitionInfos != null)
                {
                    foreach (var partition in partitionInfos)
                    {
                        if (partition.IsSelected)
                        {
                            selectedPartitions.Add(partition.PartitionName);
                        }
                    }
                }
                
                if (selectedPartitions.Count == 0)
                {
                    LogToFastboot("请先选择要回读的分区", "Red");
                    return;
                }

                string? outputExtension = ShowPartitionBackupFormatDialog("回读分区");
                if (string.IsNullOrWhiteSpace(outputExtension))
                {
                    LogToFastboot("用户取消了回读分区", "Yellow");
                    return;
                }
                
                try
                {
                    string adbPath = GetToolPath("adb.exe");
                    if (string.IsNullOrEmpty(adbPath))
                    {
                        LogToFastboot("未找到adb工具", "Red");
                        return;
                    }
                    
                    LogToFastboot($"准备回读 {selectedPartitions.Count} 个分区", "Black");

                    string saveDirectory = SelectSaveDirectory("请选择保存回读分区镜像的目录");
                    if (string.IsNullOrWhiteSpace(saveDirectory))
                    {
                        LogToFastboot("用户取消了选择保存目录", "Yellow");
                        return;
                    }

                    LogToFastbootStyled(
                        ("保存路径：", "Black", false),
                        (saveDirectory, "Blue", false));

                    operationCancellation = BeginFastbootVisualizationOperation();
                    CancellationToken cancellationToken = operationCancellation.Token;
                    
                    // 创建手机端保存目录
                    await ExecuteAdbCommandWithOutput(
                        "shell mkdir -p /sdcard/Download",
                        cancellationToken);
                    Directory.CreateDirectory(saveDirectory);

                    ResetOperationTransferDisplay();
                    var successfullyReadImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    
                    // 逐个回读选中的分区
                    foreach (var partitionName in selectedPartitions)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        LogFastbootReadBegin(partitionName);
                        string remoteFilePath = $"/sdcard/Download/{partitionName}.img";
                        try
                        {
                            // 使用dd命令读取分区到手机存储
                            string ddCommand = $"shell \"su -c 'dd if=/dev/block/by-name/{partitionName} of={remoteFilePath}'\"";
                            string result_read = await ExecuteAdbCommandWithOutput(ddCommand, cancellationToken);
                            
                            if (result_read.Contains("Permission denied") || result_read.Contains("failed") || result_read.Contains("error"))
                            {
                                LogFastbootReadEndFail(partitionName);
                                if (!string.IsNullOrWhiteSpace(result_read))
                                {
                                    LogToFastboot(result_read.Trim(), "Red");
                                }
                            }
                            else
                            {
                                // 从手机拉取分区镜像到电脑
                                string outputFile = Path.Combine(saveDirectory, $"{partitionName}{outputExtension}");
                                long currentPartitionBytes = await ResolvePartitionTransferSizeAsync(
                                    remoteFilePath,
                                    partitionInfos?.FirstOrDefault(p => p.PartitionName == partitionName)?.PartitionSize);
                                _transferStartTime = DateTime.Now;
                                ResetOperationTransferDisplay();
                                Stopwatch transferStopwatch = Stopwatch.StartNew();
                                double? lastSmoothedSpeed = null;
                                string result_pull;

                                try
                                {
                                    await ExecuteAdbSyncPullAsync(
                                        remoteFilePath,
                                        outputFile,
                                        (receivedBytes, currentFileTotalBytes) =>
                                        {
                                            long safeCurrentFileTotalBytes = currentFileTotalBytes > 0 ? currentFileTotalBytes : currentPartitionBytes;
                                            long transferredBytes = Math.Min(safeCurrentFileTotalBytes, receivedBytes);
                                            SetOperationTransferProgress(CalculateTransferPercent(transferredBytes, safeCurrentFileTotalBytes));
                                            SetOperationTransferSpeed(CalculateTransferSpeed(transferStopwatch, receivedBytes, ref lastSmoothedSpeed));
                                            UpdateOperationTransferElapsed();
                                        },
                                        currentPartitionBytes,
                                        cancellationToken);

                                    SetOperationTransferProgress(100);
                                    UpdateOperationTransferElapsed();
                                    result_pull = "success";
                                }
                                catch (OperationCanceledException)
                                {
                                    throw;
                                }
                                catch (Exception ex)
                                {
                                    result_pull = $"failed: {ex.Message}";
                                }
                                
                                if (result_pull.Contains("error") || result_pull.Contains("failed"))
                                {
                                    LogFastbootReadEndFail(partitionName);
                                    if (!string.IsNullOrWhiteSpace(result_pull))
                                    {
                                        LogToFastboot(result_pull.Trim(), "Red");
                                    }
                                }
                                else
                                {
                                    LogFastbootReadEndOk(partitionName);
                                    successfullyReadImages[partitionName] = outputFile;
                                }
                            }
                        }
                        finally
                        {
                            await CleanupAdbTemporaryFileAsync(remoteFilePath);
                        }
                    }

                    try
                    {
                        if (selectedPartitions.Count > 0)
                        {
                            CompleteOperationTransferDisplay();
                        }

                        await TryGenerateRawProgramXmlAsync(
                            adbPath,
                            saveDirectory,
                            successfullyReadImages,
                            cancellationToken);

                        if (Directory.Exists(saveDirectory))
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments = $"\"{saveDirectory}\"",
                                UseShellExecute = true
                            });
                        }
                        LogToFastboot("回读完成，已打开保存目录", "Green");
                    }
                    catch
                    {
                        LogToFastboot("回读完成，但打开保存目录失败", "Yellow");
                    }
                }
                catch (OperationCanceledException)
                {
                    LogFastbootVisualizationOperationStopped("回读分区");
                }
                catch (Exception ex)
                {
                    LogToFastboot($"回读分区失败: {ex.Message}", "Red");
                }
                finally
                {
                    EndFastbootVisualizationOperation(operationCancellation);
                }
            }
        }

        private void SelectFileButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as System.Windows.Controls.Button;
            if (button?.DataContext is PartitionInfo partition)
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Image Files (*.img)|*.img|All files (*.*)|*.*"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    partition.FilePath = openFileDialog.FileName;
                }
            }
        }

        private void PartitionTableDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var dataGrid = sender as DataGrid;
            if (dataGrid?.SelectedItem is PartitionInfo partition)
            {
                if (IsFastbootVisualizationPartitionProtected(
                    partition.PartitionName,
                    IsFastbootVisualizationAdbMode()))
                {
                    partition.IsSelected = false;
                    e.Handled = true;
                    return;
                }

                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Image Files (*.img)|*.img|All files (*.*)|*.*"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    partition.FilePath = openFileDialog.FileName;
                    // 自动勾选此行的复选框
                    partition.IsSelected = true;
                }
            }
        }

        private async void WritePartitionButton_Click(object sender, RoutedEventArgs e)
        {
            var logTextBox = this.FindName("FastbootLogTextBox") as System.Windows.Controls.RichTextBox;
            var dataGrid = this.FindName("PartitionTableDataGrid") as DataGrid;
            CancellationTokenSource? operationCancellation = null;
            
            if (logTextBox != null && dataGrid != null)
            {
                // 获取选中的分区
                var selectedPartitions = new List<PartitionInfo>();
                if (dataGrid.ItemsSource is ObservableCollection<PartitionInfo> partitions)
                {
                    foreach (var partition in partitions)
                    {
                        if (partition.IsSelected)
                        {
                            selectedPartitions.Add(partition);
                        }
                    }
                }

                bool adbProtectionMode = IsFastbootVisualizationAdbMode();
                List<PartitionInfo> protectedSelections = selectedPartitions
                    .Where(partition => IsFastbootVisualizationPartitionProtected(
                        partition.PartitionName,
                        adbProtectionMode))
                    .ToList();
                if (protectedSelections.Count > 0)
                {
                    foreach (PartitionInfo protectedPartition in protectedSelections)
                    {
                        protectedPartition.IsSelected = false;
                    }
                    selectedPartitions.RemoveAll(partition => protectedSelections.Contains(partition));
                    LogToFastbootStyled(
                        ("保护分区已启用，已跳过：", "Black", false),
                        (string.Join("、", protectedSelections.Select(partition => partition.PartitionName)), "Orange", true));
                }
                
                if (selectedPartitions.Count == 0)
                {
                    LogToFastboot(
                        protectedSelections.Count > 0
                            ? "所选分区均受保护，本次未执行写入"
                            : "请先选择要写入的分区",
                        protectedSelections.Count > 0 ? "Yellow" : "Red");
                    return;
                }
                
                // 检查是否所有选中的分区都有文件路径
                var partitionsWithoutFile = selectedPartitions.Where(p => string.IsNullOrEmpty(p.FilePath) || !System.IO.File.Exists(p.FilePath)).ToList();
                if (partitionsWithoutFile.Any())
                {
                    LogToFastboot($"以下分区未选择文件或文件不存在: {string.Join(", ", partitionsWithoutFile.Select(p => p.PartitionName))}", "Yellow");
                    return;
                }
                
                bool shouldResumeDeviceDetection = false;
                try
                {
                    operationCancellation = BeginFastbootVisualizationOperation(
                        stopAtCommandBoundary: true);
                    CancellationToken cancellationToken = operationCancellation.Token;
                    // 重置进度条
                    ResetOperationProgressBar();
                    
                    // 计算总文件大小并初始化传输状态
                    _totalBytesToTransfer = 0;
                    foreach (var partition in selectedPartitions)
                    {
                        if (File.Exists(partition.FilePath))
                        {
                            FileInfo fileInfo = new FileInfo(partition.FilePath);
                            _totalBytesToTransfer += fileInfo.Length;
                        }
                    }
                    
                    // 初始化传输状态
                    _currentBytesTransferred = 0;
                    _currentTransferRate = 0;
                    _transferStartTime = DateTime.Now;
                    
                    // 更新UI显示初始状态
                     Dispatcher.Invoke(() =>
                     {
                         SetOperationProgressTag("0MB/s");
                     });
                    
                    // 当前页面选择的是具体的 bat 文件，不能读取“基本刷入”页面的目录输入框。
                    string selectedFlashScript = FlashBatTextBox?.Text?.Trim() ?? "";
                    bool hasXiaomiScript = HasActiveParsedXiaomiScript();
                    
                    // 检查设备连接状态
                    string currentConnectionType = "未知";
                    if (BottomConnectionTypeText != null)
                    {
                        currentConnectionType = GetRawLocalizedText(BottomConnectionTypeText);
                    }

                    // 主页状态存在刷新延迟。除明确的系统模式外，直接通过 fastboot devices
                    // 确认目标设备，避免设备已经连接却仍被“未知”状态拦截。
                    if (currentConnectionType != "系统")
                    {
                        string fastbootSerial = await ResolveConnectedFastbootSerialAsync(cancellationToken);
                        if (string.IsNullOrWhiteSpace(fastbootSerial))
                        {
                            LogToFastbootStyled(
                                ("连接Fastboot设备...", "Black", false),
                                ("Error", "Red", true));
                            return;
                        }

                        currentConnectionType = "Fastboot";
                        LogToFastbootStyled(
                            ("连接Fastboot设备...", "Black", false),
                            ($"[{fastbootSerial}]", "Purple", true));
                    }
                    
                    // 如果检测到设备处于Fastboot状态，先停止设备检测
                    if (currentConnectionType == "Fastboot")
                    {
                        var simulatedStopButton = new System.Windows.Controls.Button
                        {
                            Content = "停止检测设备"
                        };
                        Button_Click_1(simulatedStopButton, new RoutedEventArgs());
                        shouldResumeDeviceDetection = true;
                        LogToFastboot("委托主页停止异步设备检测...Done", "Green");
                    }
                    
                    // 根据是否有小米线刷脚本和设备连接状态选择写入方式
                    if (hasXiaomiScript && currentConnectionType == "Fastboot")
                    {
                        (bool slotAModeAvailable, string slotAModeReason) =
                            await CheckSlotAModeAvailabilityAsync(
                                _parsedXiaomiFlashScriptLines!,
                                cancellationToken);
                        XiaomiFlashMode? selectedMode = ShowXiaomiFlashModeDialog(
                            slotAModeAvailable,
                            slotAModeReason);
                        if (!selectedMode.HasValue)
                        {
                            LogToFastboot("已取消小米线刷", "Black");
                            return;
                        }

                        LogToFastboot(
                            selectedMode == XiaomiFlashMode.SlotA
                                ? "小米线刷模式：Slot_A 精简模式"
                                : "小米线刷模式：传统模式",
                            "Blue");

                        // 使用小米线刷脚本执行刷写
                        bool scriptSucceeded = await ExecuteXiaomiFlashScript(
                            selectedFlashScript,
                            logTextBox,
                            allPartitions.ToList(),
                            selectedMode.Value,
                            cancellationToken);
                        if (!scriptSucceeded)
                        {
                            LogToFastboot("脚本未完全成功，请检查日志；存在分区失败时锁BL会被跳过", "Yellow");
                        }
                    }
                    else if (currentConnectionType == "系统")
                    {
                        // 使用 ADB 命令写入分区
                        await WritePartitionsUsingAdb(selectedPartitions, logTextBox, cancellationToken);
                    }
                    else if (currentConnectionType == "Fastboot")
                    {
                        // 使用 Fastboot 命令写入分区
                        await WritePartitionsUsingFastboot(selectedPartitions, logTextBox, cancellationToken);
                    }
                    else
                    {
                        LogToFastboot("设备未连接或连接状态未知，请确保设备已连接并处于系统模式或Fastboot模式", "Red");
                        return;
                    }
                    
                }
                catch (OperationCanceledException)
                {
                    LogFastbootVisualizationOperationStopped("写入分区");
                }
                catch (Exception ex)
                {
                    LogToFastboot($"写入分区失败: {ex.Message}", "Red");
                }
                finally
                {
                    EndFastbootVisualizationOperation(operationCancellation);
                    if (shouldResumeDeviceDetection)
                    {
                        var simulatedStartButton = new System.Windows.Controls.Button
                        {
                            Content = "开始检测设备"
                        };
                        Button_Click_1(simulatedStartButton, new RoutedEventArgs());
                        LogToFastboot("委托主页开始异步设备检测...Done", "Green");
                    }
                }
            }
        }

        private async Task<string> ResolveConnectedFastbootSerialAsync(
            CancellationToken cancellationToken = default)
        {
            string output = await GetCommandOutput(
                GetFastbootPath(),
                "devices",
                cancellationToken);
            List<string> connectedSerials = Regex.Matches(
                    output ?? string.Empty,
                    @"^\s*(?<serial>\S+)\s+fastboot\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline)
                .Cast<Match>()
                .Select(match => match.Groups["serial"].Value.Trim())
                .Where(serial => !string.IsNullOrWhiteSpace(serial))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string selectedSerial = GetSelectedDeviceSerial();
            string? selectedConnectedSerial = connectedSerials.FirstOrDefault(serial =>
                serial.Equals(selectedSerial, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(selectedConnectedSerial))
            {
                return selectedConnectedSerial;
            }

            return connectedSerials.Count == 1 ? connectedSerials[0] : string.Empty;
        }

        private async Task<(bool Available, string Reason)> CheckSlotAModeAvailabilityAsync(
            string[] scriptLines,
            CancellationToken cancellationToken = default)
        {
            bool scriptActivatesSlotA = scriptLines.Any(line =>
                Regex.IsMatch(
                    line,
                    @"^\s*fastboot\b.*\bset_active\s+_?a\b",
                    RegexOptions.IgnoreCase));
            if (!scriptActivatesSlotA)
            {
                return (false, "BAT 脚本中未找到 set_active a，无法确认脚本面向 A/B 设备。");
            }

            string fastbootPath = GetToolPath("fastboot.exe");
            if (string.IsNullOrWhiteSpace(fastbootPath))
            {
                return (false, "未找到 fastboot.exe，无法确认设备槽位类型。");
            }

            string selectedSerial = GetSelectedDeviceSerial();
            string serialPrefix = string.IsNullOrWhiteSpace(selectedSerial)
                ? string.Empty
                : $"-s {selectedSerial} ";

            string slotCountOutput = await GetCommandOutput(
                fastbootPath,
                $"{serialPrefix}getvar slot-count",
                cancellationToken);
            Match slotCountMatch = Regex.Match(
                slotCountOutput ?? string.Empty,
                @"\bslot-count\s*:\s*(\d+)\b",
                RegexOptions.IgnoreCase);
            if (slotCountMatch.Success &&
                int.TryParse(slotCountMatch.Groups[1].Value, out int slotCount) &&
                slotCount >= 2)
            {
                return (true, $"已确认设备支持 A/B 槽位（slot-count: {slotCount}）。");
            }

            string hasBootSlotOutput = await GetCommandOutput(
                fastbootPath,
                $"{serialPrefix}getvar has-slot:boot",
                cancellationToken);
            if (Regex.IsMatch(
                    hasBootSlotOutput ?? string.Empty,
                    @"\bhas-slot:boot\s*:\s*(?:yes|true)\b",
                    RegexOptions.IgnoreCase))
            {
                return (true, "已确认 boot 分区支持 A/B 槽位。");
            }

            return (false, "设备未返回有效的双槽信息，出于安全考虑不可使用 Slot_A 模式。");
        }

        private XiaomiFlashMode? ShowXiaomiFlashModeDialog(bool slotAModeAvailable, string slotAModeReason)
        {
            var dialog = new System.Windows.Window
            {
                Title = "选择小米线刷模式",
                Owner = this,
                Width = 520,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Background = new SolidColorBrush(MediaColor.FromRgb(250, 251, 253))
            };

            var root = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(22, 18, 22, 20)
            };
            root.Children.Add(new TextBlock
            {
                Text = "请选择本次刷写方式",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(38, 49, 66)),
                Margin = new Thickness(0, 0, 0, 5)
            });
            root.Children.Add(new TextBlock
            {
                Text = "模式只影响 BAT 中以 _ab 结尾的分区目标。",
                FontSize = 12,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(100, 116, 139)),
                Margin = new Thickness(0, 0, 0, 16)
            });

            var traditionalRadio = new System.Windows.Controls.RadioButton
            {
                GroupName = "XiaomiFlashMode",
                IsChecked = true,
                Content = "传统模式",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(51, 65, 85))
            };
            var traditionalCard = CreateXiaomiFlashModeCard(
                traditionalRadio,
                "严格按照官方 BAT 的分区目标执行，保留 _ab 刷写方式。",
                true);
            root.Children.Add(traditionalCard);

            var slotARadio = new System.Windows.Controls.RadioButton
            {
                GroupName = "XiaomiFlashMode",
                IsEnabled = slotAModeAvailable,
                Content = "Slot_A 模式",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(124, 58, 237)),
                ToolTip = slotAModeReason
            };
            var slotACard = CreateXiaomiFlashModeCard(
                slotARadio,
                "Slot_A模式可以精简官方脚本多余步骤，缩短刷写时间",
                slotAModeAvailable);
            slotACard.Margin = new Thickness(0, 10, 0, 0);
            root.Children.Add(slotACard);

            root.Children.Add(new TextBlock
            {
                Text = slotAModeReason,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = new SolidColorBrush(slotAModeAvailable
                    ? MediaColor.FromRgb(22, 163, 74)
                    : MediaColor.FromRgb(217, 119, 6)),
                Margin = new Thickness(4, 9, 4, 0)
            });

            var buttons = new System.Windows.Controls.StackPanel
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
                Margin = new Thickness(0, 0, 10, 0)
            };
            var startButton = new System.Windows.Controls.Button
            {
                Content = "开始刷写",
                Width = 98,
                Height = 32,
                Background = new SolidColorBrush(MediaColor.FromRgb(196, 125, 232)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(196, 125, 232)),
                Foreground = System.Windows.Media.Brushes.White
            };
            cancelButton.Click += (_, _) => dialog.DialogResult = false;
            startButton.Click += (_, _) => dialog.DialogResult = true;
            buttons.Children.Add(cancelButton);
            buttons.Children.Add(startButton);
            root.Children.Add(buttons);
            dialog.Content = root;

            if (dialog.ShowDialog() != true)
            {
                return null;
            }

            return slotARadio.IsChecked == true
                ? XiaomiFlashMode.SlotA
                : XiaomiFlashMode.Traditional;
        }

        private static Border CreateXiaomiFlashModeCard(
            System.Windows.Controls.RadioButton radioButton,
            string description,
            bool isEnabled)
        {
            var content = new System.Windows.Controls.StackPanel();
            content.Children.Add(radioButton);
            content.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(100, 116, 139)),
                Margin = new Thickness(25, 7, 0, 0)
            });

            return new Border
            {
                Padding = new Thickness(14, 12, 14, 12),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(226, 232, 240)),
                Background = new SolidColorBrush(isEnabled
                    ? MediaColor.FromRgb(255, 255, 255)
                    : MediaColor.FromRgb(245, 246, 248)),
                Child = content
            };
        }
        
        private async Task WritePartitionsUsingAdb(
            List<PartitionInfo> selectedPartitions,
            System.Windows.Controls.RichTextBox logTextBox,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string adbPath = GetToolPath("adb.exe");
            if (string.IsNullOrEmpty(adbPath))
            {
                LogToFastboot("未找到adb工具", "Red");
                return;
            }
            
            // 与ADB回读使用相同的设备连接及ROOT授权日志样式。
            var device = await ResolveRootAdbDeviceAsync(adbPath);
            cancellationToken.ThrowIfCancellationRequested();
            if (!device.Success)
            {
                LogToFastboot(device.Error, "Red");
                return;
            }

            LogToFastbootStyled(
                ("连接设备...", "Black", false),
                ($"[{device.Serial}]", "Purple", true));
            LogToFastbootStyled(
                ("已授予 ", "Black", false),
                ("Shell ROOT", "Blue", true),
                (" 权限", "Black", false));
            
            // 创建Download目录
            await ExecuteAdbCommandWithOutput(
                "shell su -c 'mkdir -p /sdcard/Download'",
                cancellationToken);

            long totalBytesToPush = selectedPartitions.Sum(partition => Math.Max(1, GetExistingFileSize(partition.FilePath)));
            if (totalBytesToPush <= 0)
            {
                totalBytesToPush = Math.Max(1, selectedPartitions.Count);
            }

            long completedPushBytes = 0;
            _transferStartTime = DateTime.Now;
            ResetOperationTransferDisplay();
            
            Stopwatch partitionWriteStopwatch = Stopwatch.StartNew();
            bool allWritesSucceeded = true;
            int failedPartitionCount = 0;
            foreach (var partition in selectedPartitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string renamedFileName = $"{partition.PartitionName}.img";
                bool stepFinished = false;
                string deviceDownloadPath = $"/sdcard/Download/{renamedFileName}";
                try
                {
                    // 将文件重命名为分区名称并推送到设备Download目录
                    LogFastbootWriteBegin(renamedFileName, partition.PartitionName);
                    long currentFileSize = Math.Max(1, GetExistingFileSize(partition.FilePath));
                    long completedBytesBeforeCurrent = completedPushBytes;
                    Stopwatch transferStopwatch = Stopwatch.StartNew();
                    double? lastSmoothedSpeed = null;
                    
                    string pushResult;
                    try
                    {
                        await ExecuteAdbSyncPushAsync(
                            partition.FilePath,
                            deviceDownloadPath,
                            (sentBytes, currentFileTotalBytes) =>
                            {
                                long safeCurrentFileTotalBytes = Math.Max(1, currentFileTotalBytes);
                                long transferredBytes = completedBytesBeforeCurrent + Math.Min(safeCurrentFileTotalBytes, sentBytes);
                                SetOperationTransferProgress(CalculateTransferPercent(transferredBytes, totalBytesToPush));
                                SetOperationTransferSpeed(CalculateTransferSpeed(transferStopwatch, sentBytes, ref lastSmoothedSpeed));
                                UpdateOperationTransferElapsed();
                            },
                            cancellationToken: CancellationToken.None);

                        completedPushBytes += currentFileSize;
                        SetOperationTransferProgress(CalculateTransferPercent(completedPushBytes, totalBytesToPush));
                        UpdateOperationTransferElapsed();
                        pushResult = "success";
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        pushResult = $"failed: {ex.Message}";
                    }

                    if (pushResult.Contains("failed") || pushResult.Contains("error"))
                    {
                        LogFastbootWriteEndFail(partition.PartitionName, renamedFileName);
                        stepFinished = true;
                        LogToFastboot(pushResult.Trim(), "Red");
                        allWritesSucceeded = false;
                        failedPartitionCount++;
                        continue;
                    }
                    
                    // 使用dd命令将Download目录中的文件写入到分区
                    string partitionPath = $"/dev/block/by-name/{partition.PartitionName}";
                    string ddCommand = $"shell su -c 'dd if={deviceDownloadPath} of={partitionPath} bs=4096'";
                    string ddResult = await ExecuteAdbCommandWithOutput(ddCommand, CancellationToken.None);
                    
                    if (ddResult.Contains("records in") && ddResult.Contains("records out"))
                    {
                        LogFastbootWriteEndOk(partition.PartitionName, renamedFileName);
                        stepFinished = true;
                    }
                    else if (ddResult.Contains("Permission denied") || ddResult.Contains("Operation not permitted"))
                    {
                        LogFastbootWriteEndFail(partition.PartitionName, renamedFileName);
                        stepFinished = true;
                        LogToFastboot($"分区 {partition.PartitionName} 写入失败: 权限不足", "Red");
                        allWritesSucceeded = false;
                        failedPartitionCount++;
                    }
                    else if (ddResult.Contains("No such file or directory"))
                    {
                        LogFastbootWriteEndFail(partition.PartitionName, renamedFileName);
                        stepFinished = true;
                        LogToFastboot($"分区 {partition.PartitionName} 不存在或路径错误", "Red");
                        allWritesSucceeded = false;
                        failedPartitionCount++;
                    }
                    else
                    {
                        LogFastbootWriteEndFail(partition.PartitionName, renamedFileName);
                        stepFinished = true;
                        LogToFastboot($"分区 {partition.PartitionName} 写入结果未知: {ddResult}", "Yellow");
                        allWritesSucceeded = false;
                        failedPartitionCount++;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (!stepFinished)
                    {
                        LogFastbootWriteEndFail(partition.PartitionName, renamedFileName);
                    }
                    LogToFastboot($"写入分区 {partition.PartitionName} 失败: {ex.Message}", "Red");
                    allWritesSucceeded = false;
                    failedPartitionCount++;
                }
                finally
                {
                    await CleanupAdbTemporaryFileAsync(deviceDownloadPath);
                }
            }

            if (selectedPartitions.Count > 0 && completedPushBytes >= totalBytesToPush)
            {
                CompleteOperationTransferDisplay();
            }

            cancellationToken.ThrowIfCancellationRequested();
            long elapsedSeconds = Math.Max(0, (long)Math.Ceiling(partitionWriteStopwatch.Elapsed.TotalSeconds));
            LogToFastboot(
                $"写入完成，写入失败分区{failedPartitionCount}个，耗时{elapsedSeconds}秒",
                allWritesSucceeded ? "Green" : "Orange");

            // 当前设备仍处于 Android 系统模式，自动重启必须使用 adb reboot。
            if (RestartCheckBox.IsChecked == true)
            {
                string rebootResult = await ExecuteAdbCommandWithOutput("reboot", cancellationToken);
                bool rebootFailed = Regex.IsMatch(
                    rebootResult ?? string.Empty,
                    @"\b(?:error|failed|failure|offline|unauthorized)\b|no devices",
                    RegexOptions.IgnoreCase);
                if (rebootFailed)
                {
                    LogToFastbootStyled(
                        ("重启设备", "Black", false),
                        ("...Error", "Red", true));
                    if (!string.IsNullOrWhiteSpace(rebootResult))
                    {
                        LogToFastboot(rebootResult.Trim(), "Red");
                    }
                }
                else
                {
                    LogToFastbootStyled(
                        ("重启设备", "Black", false),
                        ("...OK", "Green", true));
                }
            }
        }
        
        private async Task WritePartitionsUsingFastboot(
            List<PartitionInfo> selectedPartitions,
            System.Windows.Controls.RichTextBox logTextBox,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fastbootPath = GetFastbootPath();
            if (string.IsNullOrEmpty(fastbootPath))
            {
                LogToFastboot("未找到fastboot工具", "Red");
                return;
            }
            
            // 重置进度条
            ResetOperationProgressBar();
            
            bool allFlashesSucceeded = true;
            foreach (var partition in selectedPartitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // 获取显示用的分区名称（移除_ab后缀）
                string displayPartitionName = GetDisplayPartitionName(partition.PartitionName);
                
                // 重置进度条为当前分区
                ResetOperationProgressBar();
                
                string flashCommand = $"fastboot flash {partition.PartitionName} \"{partition.FilePath}\"";
                string sourceFileName = Path.GetFileName(partition.FilePath);
                bool flashSucceeded = await ExecuteFastbootCommandFromScriptWithCustomLog(
                    flashCommand,
                    "",
                    fastbootPath,
                    logTextBox,
                    partition.PartitionName,
                    sourceFileName,
                    cancellationToken: CancellationToken.None);
                if (!flashSucceeded)
                {
                    allFlashesSucceeded = false;
                    LogToFastboot($"分区 {partition.PartitionName} 刷写失败，已跳过并继续下一个分区", "Red");
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            await ExecutePostFlashCommands(
                fastbootPath,
                logTextBox,
                allowBootloaderLock: false,
                allowScriptOnlyActions: HasActiveParsedRawProgram() &&
                                        string.Equals(
                                            BottomConnectionTypeText?.Text,
                                            "Fastboot",
                                            StringComparison.OrdinalIgnoreCase),
                cancellationToken: cancellationToken);
        }

        private async Task CleanupAdbTemporaryFileAsync(string remotePath)
        {
            if (string.IsNullOrWhiteSpace(remotePath))
            {
                return;
            }

            try
            {
                await ExecuteAdbCommandWithOutput($"shell rm -f \"{remotePath}\"");
            }
            catch (Exception ex)
            {
                LogToFastboot($"清理设备临时文件失败：{remotePath}，{ex.Message}", "Yellow");
            }
        }

        private async void BackupBasebandButton_Click(object sender, RoutedEventArgs e)
        {
            var logTextBox = this.FindName("FastbootLogTextBox") as System.Windows.Controls.RichTextBox;
            var dataGrid = this.FindName("PartitionTableDataGrid") as DataGrid;
            var partitionInfos = dataGrid?.ItemsSource as ObservableCollection<PartitionInfo>;
            CancellationTokenSource? operationCancellation = null;
            
            if (logTextBox == null)
            {
                System.Windows.MessageBox.Show("发生内部错误：找不到日志文本框。");
                return;
            }

            LogToFastboot("开始备份字库（所有分区）...", "Black");

            if (GetRawLocalizedText(BottomConnectionTypeText) != "系统")
            {
                LogToFastboot("设备未处于系统模式，请在系统模式下执行此操作", "Red");
                return;
            }

            string adbPath = GetAdbPath();
            if (string.IsNullOrEmpty(adbPath))
            {
                LogToFastboot("未找到ADB工具", "Red");
                return;
            }

            // 检查root权限
            string suResult = await ExecuteAdbCommandWithOutput("shell su -c \"echo success\"");
            if (!suResult.Contains("success"))
            {
                LogToFastboot("设备没有root权限，无法执行备份", "Red");
                return;
            }

            // 获取分区表信息
            List<string> partitionsToBackup = new List<string>();
            
            if (partitionInfos != null && partitionInfos.Count > 0)
            {
                // 从分区表DataGrid获取所有分区名称，但跳过userdata分区
                foreach (var partition in partitionInfos)
                {
                    if (!string.IsNullOrEmpty(partition.PartitionName))
                    {
                        // 跳过userdata分区
                        if (partition.PartitionName.ToLower() == "userdata")
                        {
                            LogToFastboot($"跳过userdata分区（用户数据分区）", "Red");
                            continue;
                        }
                        partitionsToBackup.Add(partition.PartitionName);
                    }
                }
                LogToFastboot($"从分区表获取到 {partitionsToBackup.Count} 个分区（已跳过userdata）", "Black");
            }
            else
            {
                LogToFastboot("未找到分区表信息，请先读取分区表", "Red");
                return;
            }

            string? outputExtension = ShowPartitionBackupFormatDialog("备份字库");
            if (string.IsNullOrWhiteSpace(outputExtension))
            {
                LogToFastboot("用户取消了备份字库", "Yellow");
                return;
            }

            string baseDirectory = SelectSaveDirectory("请选择保存字库备份的目录");
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                LogToFastboot("用户取消了选择保存目录", "Yellow");
                return;
            }

            string backupDir = System.IO.Path.Combine(baseDirectory, $"SmartTool_{DateTime.Now:yyyyMMdd_HHmmss}");
            try
            {
                System.IO.Directory.CreateDirectory(backupDir);
            }
            catch (Exception ex)
            {
                LogToFastboot($"创建备份目录失败：{ex.Message}", "Red");
                return;
            }

            LogToFastboot($"备份文件将保存在：{backupDir}", "Black");

            operationCancellation = BeginFastbootVisualizationOperation();
            CancellationToken cancellationToken = operationCancellation.Token;
            try
            {
            bool allSucceeded = true;
            int successCount = 0;
            var successfullyBackedUpImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ResetOperationTransferDisplay();

            foreach (string partition in partitionsToBackup)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // 如果是super分区，提醒用户该分区文件较大
                if (partition.ToLower() == "super")
                {
                    LogToFastboot($"注意：super分区文件较大，请耐心等待...", "Red");
                }

                LogFastbootReadBegin(partition);

                string devicePath = $"/sdcard/Download/{partition}.img";
                string localPath = System.IO.Path.Combine(backupDir, $"{partition}{outputExtension}");
                string partitionPath = $"/dev/block/bootdevice/by-name/{partition}";

                // 使用dd命令读取分区
                string ddCommand = $"shell su -c \"dd if={partitionPath} of={devicePath} bs=4096\"";
                string ddResult = await ExecuteAdbCommandWithOutput(ddCommand, cancellationToken);

                if (ddResult.Contains("error") || ddResult.Contains("failed") || ddResult.Contains("not found"))
                {
                    LogFastbootReadEndFail(partition);
                    if (!string.IsNullOrWhiteSpace(ddResult))
                    {
                        LogToFastboot(ddResult.Trim(), "Red");
                    }
                    await CleanupAdbTemporaryFileAsync(devicePath);
                    allSucceeded = false;
                    continue;
                }

                // 使用adb pull将文件从设备拉到电脑
                long currentPartitionBytes = await ResolvePartitionTransferSizeAsync(
                    devicePath,
                    partitionInfos?.FirstOrDefault(p => p.PartitionName == partition)?.PartitionSize);
                _transferStartTime = DateTime.Now;
                ResetOperationTransferDisplay();
                Stopwatch transferStopwatch = Stopwatch.StartNew();
                double? lastSmoothedSpeed = null;
                string pullResult;

                try
                {
                    await ExecuteAdbSyncPullAsync(
                        devicePath,
                        localPath,
                        (receivedBytes, currentFileTotalBytes) =>
                        {
                            long safeCurrentFileTotalBytes = currentFileTotalBytes > 0 ? currentFileTotalBytes : currentPartitionBytes;
                            long transferredBytes = Math.Min(safeCurrentFileTotalBytes, receivedBytes);
                            SetOperationTransferProgress(CalculateTransferPercent(transferredBytes, safeCurrentFileTotalBytes));
                            SetOperationTransferSpeed(CalculateTransferSpeed(transferStopwatch, receivedBytes, ref lastSmoothedSpeed));
                            UpdateOperationTransferElapsed();
                        },
                        currentPartitionBytes,
                        cancellationToken);

                    SetOperationTransferProgress(100);
                    UpdateOperationTransferElapsed();
                    pullResult = "success";
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    pullResult = $"failed: {ex.Message}";
                }

                if (pullResult.Contains("error") || pullResult.Contains("failed"))
                {
                    LogFastbootReadEndFail(partition);
                    if (!string.IsNullOrWhiteSpace(pullResult))
                    {
                        LogToFastboot(pullResult.Trim(), "Red");
                    }
                    allSucceeded = false;
                }
                else
                {
                    LogFastbootReadEndOk(partition);
                    successCount++;
                    successfullyBackedUpImages[partition] = localPath;
                }

                // 删除设备上的临时文件
                await CleanupAdbTemporaryFileAsync(devicePath);
            }

            await TryGenerateRawProgramXmlAsync(
                adbPath,
                backupDir,
                successfullyBackedUpImages,
                cancellationToken);

            if (allSucceeded)
            {
                if (partitionsToBackup.Count > 0)
                {
                    CompleteOperationTransferDisplay();
                }
                LogToFastbootStyled(
                    ("字库备份完成，共计", "Black", false),
                    ($"{successCount}", "Purple", true),
                    ("个分区.", "Black", false));
                LogToFastbootStyled(
                    ("保存路径：", "Black", false),
                    (backupDir, "Blue", false));
            }
            else
            {
                LogToFastboot($"字库备份完成，成功备份 {successCount}/{partitionsToBackup.Count} 个分区，请检查日志", "Red");
            }

            try
            {
                if (Directory.Exists(backupDir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{backupDir}\"",
                        UseShellExecute = true
                    });
                }

            }
            catch
            {
     
            }
            }
            catch (OperationCanceledException)
            {
                LogFastbootVisualizationOperationStopped("备份字库");
            }
            finally
            {
                EndFastbootVisualizationOperation(operationCancellation);
            }
        }
        
        private List<PartitionInfo> ParsePartitionInfo(string fastbootOutput)
        {
            var partitions = new List<PartitionInfo>();
            var lines = fastbootOutput.Split('\n');
            
            foreach (var line in lines)
            {
                // 匹配分区相关的变量
                if (line.Contains("partition-size:") || line.Contains("partition-type:"))
                {
                    var match = Regex.Match(line, @"(partition-size|partition-type):([^:]+):\s*(.+)");
                    if (match.Success)
                    {
                        string varType = match.Groups[1].Value;
                        string partitionName = match.Groups[2].Value.Trim();
                        string value = match.Groups[3].Value.Trim();
                        
                        // 查找是否已存在该分区
                        var existingPartition = partitions.FirstOrDefault(p => p.PartitionName == partitionName);
                        if (existingPartition == null)
                        {
                            existingPartition = new PartitionInfo { PartitionName = partitionName };
                            partitions.Add(existingPartition);
                        }
                        
                        if (varType == "partition-size")
                        {
                            existingPartition.PartitionSize = FormatSize(value);
                        }
                        else if (varType == "partition-type")
                        {
                            existingPartition.PartitionType = value;
                        }
                    }
                }
                // 匹配其他分区相关信息
                else if (line.Contains(":") && (line.Contains("partition") || line.Contains("slot")))
                {
                    var parts = line.Split(':');
                    if (parts.Length >= 2)
                    {
                        string key = parts[0].Trim();
                        string value = string.Join(":", parts.Skip(1)).Trim();
                        
                        // 提取分区名称
                        var partitionMatch = Regex.Match(key, @"([a-zA-Z0-9_-]+)$");
                        if (partitionMatch.Success && !string.IsNullOrEmpty(value))
                        {
                            string partitionName = partitionMatch.Groups[1].Value;
                            
                            // 过滤掉一些非分区信息
                            if (!IsValidPartitionName(partitionName))
                                continue;
                                
                            var existingPartition = partitions.FirstOrDefault(p => p.PartitionName == partitionName);
                            if (existingPartition == null)
                            {
                                existingPartition = new PartitionInfo { PartitionName = partitionName };
                                partitions.Add(existingPartition);
                            }
                            
                            if (string.IsNullOrEmpty(existingPartition.AdditionalInfo))
                            {
                                existingPartition.AdditionalInfo = $"{key}: {value}";
                            }
                            else
                            {
                                existingPartition.AdditionalInfo += $"; {key}: {value}";
                            }
                        }
                    }
                }
            }
            
            return partitions.OrderBy(p => p.PartitionName).ToList();
        }
        
        private bool IsValidPartitionName(string name)
        {
            // 常见的Android分区名称
            var validPartitions = new HashSet<string>
            {
                "boot", "recovery", "system", "vendor", "userdata", "cache", "persist",
                "modem", "bluetooth", "dsp", "aboot", "rpm", "sbl1", "tz", "hyp",
                "product", "odm", "vbmeta", "dtbo", "super", "metadata", "misc",
                "init_boot", "vendor_boot", "boot_a", "boot_b", "system_a", "system_b",
                "vendor_a", "vendor_b", "product_a", "product_b", "odm_a", "odm_b"
            };
            
            return validPartitions.Contains(name.ToLower()) || 
                   name.EndsWith("_a") || name.EndsWith("_b") ||
                   Regex.IsMatch(name, @"^[a-zA-Z][a-zA-Z0-9_-]*$");
        }
        
        private string FormatSize(string sizeStr)
        {
            string normalized = sizeStr.Trim();
            System.Globalization.NumberStyles style = normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? System.Globalization.NumberStyles.HexNumber
                : System.Globalization.NumberStyles.Integer;
            if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(2);
            }

            if (long.TryParse(normalized, style, System.Globalization.CultureInfo.InvariantCulture, out long size))
            {
                return FormatByteSize(size);
            }
            return sizeStr;
        }

        private static string FormatByteSize(long size)
        {
            if (size >= 1024L * 1024 * 1024)
                return $"{size / (1024.0 * 1024.0 * 1024.0):F2} GB";
            if (size >= 1024L * 1024)
                return $"{size / (1024.0 * 1024.0):F2} MB";
            if (size >= 1024)
                return $"{size / 1024.0:F2} KB";
            return $"{size} B";
        }
        
        private string GetFastbootPath()
        {
            try
            {
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                // 优先使用flash文件夹中的fastboot.exe
                string fastbootPath = Path.Combine(appDirectory, "platform-tools", "fastboot.exe");
                
                if (File.Exists(fastbootPath))
                {
                    return fastbootPath;
                }
                
                // 尝试其他可能的路径
                string[] possiblePaths = {
                    Path.Combine(appDirectory, "platform-tools", "fastboot.exe"),
                    Path.Combine(appDirectory, "fastboot.exe"),
                    "fastboot.exe" // 系统PATH中的fastboot
                };
                
                foreach (string path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
                
                return "fastboot"; // 假设在系统PATH中
            }
            catch
            {
                return "fastboot";
            }
        }
        
        private string GetAdbPath()
        {
            try
            {
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string adbPath = Path.Combine(appDirectory, "platform-tools", "adb.exe");
                
                if (File.Exists(adbPath))
                {
                    return adbPath;
                }
                
                // 尝试其他可能的路径
                string[] possiblePaths = {
                    Path.Combine(appDirectory, "adb.exe"),
                    Path.Combine(appDirectory, "tools", "adb.exe"),
                    "adb.exe" // 系统PATH中的adb
                };
                
                foreach (string path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
                
                // 如果都找不到，仍然使用程序目录中的adb.exe路径
                return Path.Combine(appDirectory, "platform-tools", "adb.exe");
            }
            catch
            {
                // 异常情况下也返回程序目录中的adb.exe路径
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(appDirectory, "platform-tools", "adb.exe");
            }
        }
        
        private List<PartitionInfo> ParseAdbPartitionInfo(string adbOutput)
        {
            var partitions = new Dictionary<string, PartitionInfo>(StringComparer.OrdinalIgnoreCase);
            var lines = adbOutput.Split('\n');
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) ||
                    trimmedLine.StartsWith("total", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 首选新格式：partitionName|sizeInBytes|resolvedDevicePath
                string[] stableParts = trimmedLine.Split('|', 3, StringSplitOptions.None);
                if (stableParts.Length == 3)
                {
                    string partitionName = stableParts[0].Trim();
                    string sizeText = stableParts[1].Trim();
                    string devicePath = stableParts[2].Trim();
                    if (!Regex.IsMatch(partitionName, @"^[A-Za-z0-9._-]+$"))
                    {
                        continue;
                    }

                    long.TryParse(sizeText, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out long sizeBytes);
                    partitions[partitionName] = new PartitionInfo
                    {
                        PartitionName = partitionName,
                        PartitionSize = sizeBytes > 0 ? FormatByteSize(sizeBytes) : "--",
                        PartitionType = "Block",
                        AdditionalInfo = $"Device: {devicePath}"
                    };
                    continue;
                }

                // 兼容旧的 ls -l 输出：只依赖“名称 -> 目标”，不再依赖日期字段位置。
                Match symlinkMatch = Regex.Match(trimmedLine, @"^(?<left>.+?)\s+->\s+(?<target>\S+)\s*$");
                if (!symlinkMatch.Success)
                {
                    continue;
                }

                string[] leftParts = symlinkMatch.Groups["left"].Value
                    .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (leftParts.Length == 0)
                {
                    continue;
                }

                string fallbackPartitionName = leftParts[^1];
                if (!Regex.IsMatch(fallbackPartitionName, @"^[A-Za-z0-9._-]+$"))
                {
                    continue;
                }

                partitions[fallbackPartitionName] = new PartitionInfo
                {
                    PartitionName = fallbackPartitionName,
                    PartitionSize = "--",
                    PartitionType = "Block",
                    AdditionalInfo = $"Device: {symlinkMatch.Groups["target"].Value}"
                };
            }
            
            return partitions.Values.OrderBy(p => p.PartitionName).ToList();
        }

        private void FastbootLogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void FastbootLogTextBox_TextChanged_1(object sender, TextChangedEventArgs e)
        {

        }

        // 选择小米刷机包文件夹
        private void SelectXiaomiFlashScriptButton_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择小米刷机包文件夹",
                ShowNewFolderButton = false
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var xiaomiFlashScriptPathTextBox = this.FindName("XiaomiFlashScriptPathTextBox") as System.Windows.Controls.TextBox;
                if (xiaomiFlashScriptPathTextBox != null)
                {
                    xiaomiFlashScriptPathTextBox.Text = folderDialog.SelectedPath;
                }
                LogToFlashTextBox($"已选择小米刷机包: {folderDialog.SelectedPath}");
            }
        }

        // 开始小米线刷
        private async void StartXiaomiFlashButton_Click(object sender, RoutedEventArgs e)
        {
            // 标志变量，确保失败提示只弹出一次
            bool hasShownFailureMessage = false;
            // Missmatching检测相关变量
            bool hasMissmatchingDetected = false;
            System.Windows.Threading.DispatcherTimer? missmatchingTimer = null;
            
            try
            {
                // 验证刷机包路径
                var xiaomiFlashScriptPathTextBox = this.FindName("XiaomiFlashScriptPathTextBox") as System.Windows.Controls.TextBox;
                if (xiaomiFlashScriptPathTextBox == null)
                {
                    LogToFlashTextBox("错误: 未找到小米刷机包路径输入框控件");
                    return;
                }
                string packagePath = xiaomiFlashScriptPathTextBox.Text.Trim();
                if (string.IsNullOrEmpty(packagePath))
                {
                    LogToFlashTextBox("错误: 请先选择小米刷机包");
                    return;
                }

                if (!Directory.Exists(packagePath))
                {
                    LogToFlashTextBox("错误: 选择的刷机包文件夹不存在");
                    return;
                }

                // 检查复选框状态并确定要执行的bat文件
                var completeWipeCheckBox = this.FindName("CompleteWipeCheckBox") as System.Windows.Controls.CheckBox;
                var keepDataCheckBox = this.FindName("KeepDataCheckBox") as System.Windows.Controls.CheckBox;
                var wipeAndLockBLCheckBox = this.FindName("WipeAndLockBLCheckBox") as System.Windows.Controls.CheckBox;

                string batFileName = "";
                string operationType = "";

                if (completeWipeCheckBox?.IsChecked == true)
                {
                    batFileName = "flash_all.bat";
                    operationType = "完全清除数据刷机";
                }
                else if (keepDataCheckBox?.IsChecked == true)
                {
                    batFileName = "flash_all_except_storage.bat";
                    operationType = "保留数据刷机";
                }
                else if (wipeAndLockBLCheckBox?.IsChecked == true)
                {
                    batFileName = "flash_all_lock.bat";
                    operationType = "完全清除数据并回锁BL";
                }
                else
                {
                    LogToFlashTextBox("错误: 请选择一种刷机模式");
                    return;
                }

                string scriptPath = Path.Combine(packagePath, batFileName);
                if (!File.Exists(scriptPath))
                {
                    LogToFlashTextBox($"错误: 在刷机包中未找到 {batFileName} 文件");
                    return;
                }

                LogToFlashTextBox($"选择的刷机模式: {operationType}");
                LogToFlashTextBox($"将执行脚本: {batFileName}");

                // 禁用按钮防止重复操作
var startXiaomiFlashButton = this.FindName("StartXiaomiFlashButton") as System.Windows.Controls.Button;
if (startXiaomiFlashButton != null)
{
    startXiaomiFlashButton.IsEnabled = false;
}
                
                // 重置并显示进度条
                if (bootflash != null)
                {
                    _xiaomiFlashProgressState = null;
                    bootflash.Value = 0;
                    bootflash.Tag = "0MB/s  |  Time:0s";
                    bootflash.Visibility = Visibility.Visible;
                }
                
                LogToFlashTextBox("开始小米线刷操作...");

                // 获取程序根目录的flash文件夹
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string flashDirectory = Path.Combine(appDirectory, "platform-tools");

                // 如果flash文件夹不存在则创建
                if (!Directory.Exists(flashDirectory))
                {
                    try
                    {
                        Directory.CreateDirectory(flashDirectory);
                        LogToFlashTextBox($"已创建flash文件夹: {flashDirectory}");
                    }
                    catch (Exception createEx)
                    {
                        LogToFlashTextBox($"错误: 无法创建flash文件夹: {createEx.Message}");
                        return;
                    }
                }

                LogToFlashTextBox($"程序根目录: {appDirectory}");
                LogToFlashTextBox($"Flash目录: {flashDirectory}");
                LogToFlashTextBox($"刷机包目录: {packagePath}");
                LogToFlashTextBox($"脚本路径: {scriptPath}");

                InitializeXiaomiFlashProgress(scriptPath);

                // 在flash文件夹中通过cmd执行脚本路径
                LogToFlashTextBox("即将开始小米线刷...");
                
                await Task.Run(() =>
                {
                    try
                    {
                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "cmd.exe",
                                Arguments = $"/c \"{scriptPath}\"",
                                WorkingDirectory = flashDirectory,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true
                            }
                        };

                        // 实时读取输出
                        process.OutputDataReceived += (s, args) =>
                        {
                            if (!string.IsNullOrEmpty(args.Data))
                            {
                                Dispatcher.Invoke(() => 
                                {
                                    LogToFlashTextBox($"[线刷] {args.Data}");
                                    
                                    // 解析进度百分比
                                    ParseXiaomiFlashProgress(args.Data);
                                    
                                    // 如果有新日志且之前检测到Missmatching，取消定时器
                                    if (hasMissmatchingDetected && missmatchingTimer != null)
                                    {
                                        missmatchingTimer.Stop();
                                        missmatchingTimer = null;
                                        hasMissmatchingDetected = false;
                                    }
                                    
                                    // 检测Missmatching字样，启动10秒定时器
                                    if (args.Data.Contains("Missmatching") && !hasMissmatchingDetected)
                                    {
                                        hasMissmatchingDetected = true;
                                        
                                        // 创建10秒定时器
                                        missmatchingTimer = new System.Windows.Threading.DispatcherTimer();
                                        missmatchingTimer.Interval = TimeSpan.FromSeconds(10);
                                        missmatchingTimer.Tick += (timerSender, timerArgs) =>
                                        {
                                            missmatchingTimer.Stop();
                                            missmatchingTimer = null;
                                            hasMissmatchingDetected = false;
                                            
                                            // 10秒后仍无新日志，弹出警告
                                            System.Windows.MessageBox.Show("检测到Fastboot蜡笔了！\n\n请按以下步骤操作：\n1. 长按电源键加音量减\n2. 重新进入Fastboot\n3. 重新点击开始线刷",
                                                          "警告", 
                                                          MessageBoxButton.OK, 
                                                          MessageBoxImage.Warning);
                                            
                                            // 重新启用按钮，让用户可以再次尝试
                                            var startXiaomiFlashButton = this.FindName("StartXiaomiFlashButton") as System.Windows.Controls.Button;
                                            if (startXiaomiFlashButton != null)
                                            {
                                                startXiaomiFlashButton.IsEnabled = true;
                                            }
                                        };
                                        missmatchingTimer.Start();
                                    }
                                    else
                                    {
                                        // 将输出数据转换为小写以便检查
                                        string lowerData = args.Data.ToLower();
                                        
                                        // 检测FAILED或error等失败关键词（只弹出一次）
                                        if (!hasShownFailureMessage && (lowerData.Contains("failed") || lowerData.Contains("error") || 
                                            lowerData.Contains("失败") || lowerData.Contains("错误")))
                                        {
                                            hasShownFailureMessage = true;
                                            System.Windows.MessageBox.Show($"线刷失败！\n\n错误信息：{args.Data}\n\n连接不稳定，设备断开了，请重新进入Fastboot再试",
                                                          "小米线刷失败", 
                                                          MessageBoxButton.OK, 
                                                          MessageBoxImage.Error);
                                        }
                                        // 检测Rebooting字样，弹出刷机完成提示
                                        else if (lowerData.Contains("rebooting"))
                                        {
                                            System.Windows.MessageBox.Show("刷机完成！\n\n设备正在重启，请等待手机重启完成。",
                                                          "小米线刷", 
                                                          MessageBoxButton.OK, 
                                                          MessageBoxImage.Information);
                                        }
                                    }
                                });
                            }
                        };

                        process.ErrorDataReceived += (s, args) =>
                        {
                            if (!string.IsNullOrEmpty(args.Data))
                            {
                                Dispatcher.Invoke(() => 
                                {
                                    LogToFlashTextBox($"[状态] {args.Data}");
                                    
                                    // 解析进度百分比
                                    ParseXiaomiFlashProgress(args.Data);
                                    
                                    // 如果有新日志且之前检测到Missmatching，取消定时器
                                    if (hasMissmatchingDetected && missmatchingTimer != null)
                                    {
                                        missmatchingTimer.Stop();
                                        missmatchingTimer = null;
                                        hasMissmatchingDetected = false;
                                    }
                                    
                                    // 检测2>&1字样，提示重新进入fastboot
                                    if (args.Data.Contains("2>&1"))
                                    {
                                        System.Windows.MessageBox.Show("检测到设备未正确进入Fastboot模式！\n\n请按以下步骤操作：\n1. 断开设备连接\n2. 重新进入Fastboot模式\n3. 连接设备后再点击开始线刷",
                                                      "需要重新进入Fastboot模式", 
                                                      MessageBoxButton.OK, 
                                                      MessageBoxImage.Warning);
                                    }
                                    else
                                    {
                                        // 将输出数据转换为小写以便检查
                                        string lowerData = args.Data.ToLower();
                                        
                                        // 检测FAILED或error等失败关键词（只弹出一次）
                                        if (!hasShownFailureMessage && (lowerData.Contains("failed") || lowerData.Contains("error") || 
                                            lowerData.Contains("失败") || lowerData.Contains("错误")))
                                        {
                                            hasShownFailureMessage = true;
                                            System.Windows.MessageBox.Show($"线刷失败！\n\n错误信息：{args.Data}\n\n请检查设备连接状态、驱动程序或刷机包是否正确。",
                                                          "小米线刷失败", 
                                                          MessageBoxButton.OK, 
                                                          MessageBoxImage.Error);
                                        }
                                        // 检测Rebooting字样，弹出刷机完成提示
                                        else if (lowerData.Contains("rebooting"))
                                        {
                                            System.Windows.MessageBox.Show("刷机完成！\n\n设备正在重启，请等待手机重启完成。",
                                                          "小米线刷完成", 
                                                          MessageBoxButton.OK, 
                                                          MessageBoxImage.Information);
                                        }
                                    }
                                });
                            }
                        };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        process.WaitForExit();

                        Dispatcher.Invoke(() =>
                        {
                            if (process.ExitCode == 0)
                            {
                                CompleteXiaomiFlashProgress(succeeded: true);
                                LogToFlashTextBox("小米线刷操作完成！");
                            }
                            else
                            {
                                CompleteXiaomiFlashProgress(succeeded: false);
                                LogToFlashTextBox($"小米线刷操作完成，退出代码: {process.ExitCode}");
                            }
                        });
                    }
                    catch (Exception processEx)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            CompleteXiaomiFlashProgress(succeeded: false);
                            LogToFlashTextBox($"执行脚本时发生错误: {processEx.Message}");
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                CompleteXiaomiFlashProgress(succeeded: false);
                LogToFlashTextBox($"小米线刷过程中发生错误: {ex.Message}");
            }
            finally
            {
                // 重新启用按钮
var startXiaomiFlashButton = this.FindName("StartXiaomiFlashButton") as System.Windows.Controls.Button;
if (startXiaomiFlashButton != null)
{
    startXiaomiFlashButton.IsEnabled = true;
}
            }
        }

        private void TextBox_TextChanged_1()
        {

        }

        private void InitializeXiaomiFlashProgress(string scriptPath)
        {
            _xiaomiFlashProgressState = CreateXiaomiFlashProgressState(scriptPath);

            if (bootflash == null)
            {
                return;
            }

            bootflash.Value = 0;
            bootflash.Tag = "0MB/s  |  Time:0s";
        }

        private static XiaomiFlashProgressState? CreateXiaomiFlashProgressState(string scriptPath)
        {
            try
            {
                string? scriptDirectory = IOPath.GetDirectoryName(scriptPath);
                if (string.IsNullOrWhiteSpace(scriptDirectory))
                {
                    return null;
                }

                var items = new List<XiaomiFlashProgressItem>();
                foreach (string line in IOFile.ReadAllLines(scriptPath, Encoding.Default))
                {
                    Match flashMatch = Regex.Match(
                        line,
                        @"(?i)\bfastboot(?:\.exe)?\b.*?\bflash\s+(?<partition>[^\s""']+)\s+(?<image>""[^""]+""|[^\s|&]+)");
                    if (!flashMatch.Success)
                    {
                        continue;
                    }

                    string partitionName = flashMatch.Groups["partition"].Value.Trim();
                    string imageToken = flashMatch.Groups["image"].Value.Trim().Trim('"', '\'');
                    string imagePath = Regex.Replace(
                        imageToken,
                        @"%~dp0",
                        _ => scriptDirectory + IOPath.DirectorySeparatorChar,
                        RegexOptions.IgnoreCase);

                    if (!IOPath.IsPathRooted(imagePath))
                    {
                        imagePath = IOPath.Combine(scriptDirectory, imagePath);
                    }

                    imagePath = IOPath.GetFullPath(imagePath);
                    if (!IOFile.Exists(imagePath))
                    {
                        return null;
                    }

                    items.Add(new XiaomiFlashProgressItem(
                        partitionName,
                        new FileInfo(imagePath).Length));
                }

                return items.Count > 0 && items.Any(item => item.Length > 0)
                    ? new XiaomiFlashProgressState(items)
                    : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"创建小米线刷总进度计划时出错: {ex.Message}");
                return null;
            }
        }

        // 解析小米线刷 fastboot 输出，按脚本内全部镜像的总字节数计算全局进度。
        private void ParseXiaomiFlashProgress(string output)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(output) || bootflash == null)
                {
                    return;
                }

                XiaomiFlashProgressState? state = _xiaomiFlashProgressState;
                if (state == null || state.TotalBytes <= 0)
                {
                    ParseXiaomiFlashPartitionProgressFallback(output);
                    return;
                }

                Match speedMatch = Regex.Match(
                    output,
                    @"(?<speed>\d+(?:\.\d+)?)\s*(?<unit>GB/s|MB/s|KB/s|B/s)",
                    RegexOptions.IgnoreCase);
                if (speedMatch.Success)
                {
                    state.TransferRate =
                        $"{speedMatch.Groups["speed"].Value}{speedMatch.Groups["unit"].Value}";
                }

                Match sendingMatch = Regex.Match(
                    output,
                    @"Sending(?:\s+sparse)?\s+'(?<partition>[^']+)'(?:\s+\d+/\d+)?\s*\((?<size>\d+(?:\.\d+)?)\s*(?<unit>GB|MB|KB|B)\)",
                    RegexOptions.IgnoreCase);
                if (sendingMatch.Success)
                {
                    string partitionName = sendingMatch.Groups["partition"].Value.Trim();
                    if (ActivateXiaomiFlashItem(state, partitionName))
                    {
                        state.CurrentChunkExpectedBytes = ConvertXiaomiSizeToBytes(
                            sendingMatch.Groups["size"].Value,
                            sendingMatch.Groups["unit"].Value);
                        state.CurrentChunkReportedBytes = 0;
                    }
                }

                Match transferMatch = Regex.Match(
                    output,
                    @"^\s*(?<partition>[^:]+):\s*(?<current>\d+(?:\.\d+)?)\s*(?<currentUnit>GB|MB|KB|B)\s*/\s*(?<total>\d+(?:\.\d+)?)\s*(?<totalUnit>GB|MB|KB|B)\s*\(",
                    RegexOptions.IgnoreCase);
                if (transferMatch.Success)
                {
                    string partitionName = transferMatch.Groups["partition"].Value.Trim();
                    if (EnsureXiaomiFlashItemActive(state, partitionName))
                    {
                        long currentBytes = ConvertXiaomiSizeToBytes(
                            transferMatch.Groups["current"].Value,
                            transferMatch.Groups["currentUnit"].Value);
                        long totalBytes = ConvertXiaomiSizeToBytes(
                            transferMatch.Groups["total"].Value,
                            transferMatch.Groups["totalUnit"].Value);

                        if (state.CurrentChunkExpectedBytes <= 0)
                        {
                            state.CurrentChunkExpectedBytes = totalBytes;
                        }

                        state.CurrentChunkReportedBytes =
                            Math.Max(state.CurrentChunkReportedBytes, currentBytes);
                        SetXiaomiCurrentItemTransferredBytes(
                            state,
                            state.CompletedChunkBytes + state.CurrentChunkReportedBytes);
                    }
                }

                if (Regex.IsMatch(output, @"^\s*Writing\s+'", RegexOptions.IgnoreCase) &&
                    state.CurrentItemIndex >= 0 &&
                    !state.CurrentCommandFinished)
                {
                    state.CurrentChunkReportedBytes = Math.Max(
                        state.CurrentChunkReportedBytes,
                        state.CurrentChunkExpectedBytes);
                    SetXiaomiCurrentItemTransferredBytes(
                        state,
                        state.CompletedChunkBytes + state.CurrentChunkReportedBytes);
                }

                if (Regex.IsMatch(output, @"^\s*Finished\.", RegexOptions.IgnoreCase))
                {
                    CompleteCurrentXiaomiFlashItem(state);
                }

                UpdateXiaomiFlashProgressUi(state);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"解析小米线刷总进度时出错: {ex.Message}");
            }
        }

        private static bool ActivateXiaomiFlashItem(
            XiaomiFlashProgressState state,
            string partitionName)
        {
            if (state.CurrentItemIndex >= 0 &&
                !state.CurrentCommandFinished &&
                string.Equals(
                    state.Items[state.CurrentItemIndex].PartitionName,
                    partitionName,
                    StringComparison.OrdinalIgnoreCase))
            {
                CompleteCurrentXiaomiFlashChunk(state);
                return true;
            }

            if (state.CurrentItemIndex >= 0 && !state.CurrentCommandFinished)
            {
                CompleteCurrentXiaomiFlashItem(state);
            }

            int searchStart = Math.Max(0, state.CurrentItemIndex + 1);
            int nextItemIndex = -1;
            for (int index = searchStart; index < state.Items.Count; index++)
            {
                if (string.Equals(
                    state.Items[index].PartitionName,
                    partitionName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    nextItemIndex = index;
                    break;
                }
            }

            if (nextItemIndex < 0)
            {
                return false;
            }

            state.CurrentItemIndex = nextItemIndex;
            state.CurrentItemTransferredBytes = 0;
            state.CompletedChunkBytes = 0;
            state.CurrentChunkExpectedBytes = 0;
            state.CurrentChunkReportedBytes = 0;
            state.CurrentCommandFinished = false;
            return true;
        }

        private static bool EnsureXiaomiFlashItemActive(
            XiaomiFlashProgressState state,
            string partitionName)
        {
            if (state.CurrentItemIndex >= 0 &&
                !state.CurrentCommandFinished &&
                string.Equals(
                    state.Items[state.CurrentItemIndex].PartitionName,
                    partitionName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return ActivateXiaomiFlashItem(state, partitionName);
        }

        private static void CompleteCurrentXiaomiFlashChunk(XiaomiFlashProgressState state)
        {
            if (state.CurrentItemIndex < 0 || state.CurrentCommandFinished)
            {
                return;
            }

            long chunkBytes = Math.Max(
                state.CurrentChunkExpectedBytes,
                state.CurrentChunkReportedBytes);
            long itemLength = state.Items[state.CurrentItemIndex].Length;
            state.CompletedChunkBytes = Math.Min(
                itemLength,
                state.CompletedChunkBytes + chunkBytes);
            state.CurrentItemTransferredBytes = Math.Max(
                state.CurrentItemTransferredBytes,
                state.CompletedChunkBytes);
            state.CurrentChunkExpectedBytes = 0;
            state.CurrentChunkReportedBytes = 0;
        }

        private static void CompleteCurrentXiaomiFlashItem(XiaomiFlashProgressState state)
        {
            if (state.CurrentItemIndex < 0 || state.CurrentCommandFinished)
            {
                return;
            }

            XiaomiFlashProgressItem currentItem = state.Items[state.CurrentItemIndex];
            state.CompletedBytes = Math.Min(
                state.TotalBytes,
                state.CompletedBytes + currentItem.Length);
            state.CurrentItemTransferredBytes = 0;
            state.CompletedChunkBytes = 0;
            state.CurrentChunkExpectedBytes = 0;
            state.CurrentChunkReportedBytes = 0;
            state.CurrentCommandFinished = true;
        }

        private static void SetXiaomiCurrentItemTransferredBytes(
            XiaomiFlashProgressState state,
            long transferredBytes)
        {
            if (state.CurrentItemIndex < 0 || state.CurrentCommandFinished)
            {
                return;
            }

            long itemLength = state.Items[state.CurrentItemIndex].Length;
            state.CurrentItemTransferredBytes = Math.Max(
                state.CurrentItemTransferredBytes,
                Math.Min(itemLength, Math.Max(0, transferredBytes)));
        }

        private void UpdateXiaomiFlashProgressUi(XiaomiFlashProgressState state)
        {
            if (bootflash == null || state.TotalBytes <= 0)
            {
                return;
            }

            long transferredBytes = state.CompletedBytes;
            if (!state.CurrentCommandFinished)
            {
                transferredBytes += state.CurrentItemTransferredBytes;
            }

            state.LastReportedBytes = Math.Max(
                state.LastReportedBytes,
                Math.Min(state.TotalBytes, transferredBytes));

            bootflash.Value = Math.Clamp(
                state.LastReportedBytes * 100d / state.TotalBytes,
                0d,
                100d);
            bootflash.Tag =
                $"{state.TransferRate}  |  Time:{(int)state.Elapsed.Elapsed.TotalSeconds}s";
        }

        private void CompleteXiaomiFlashProgress(bool succeeded)
        {
            XiaomiFlashProgressState? state = _xiaomiFlashProgressState;
            if (state != null)
            {
                state.Elapsed.Stop();
                state.TransferRate = "0MB/s";
                if (succeeded)
                {
                    state.LastReportedBytes = state.TotalBytes;
                }

                UpdateXiaomiFlashProgressUi(state);
            }
            else if (bootflash != null)
            {
                if (succeeded)
                {
                    bootflash.Value = 100;
                }

                bootflash.Tag = "0MB/s  |  Time:0s";
            }
        }

        private static long ConvertXiaomiSizeToBytes(string valueText, string unit)
        {
            if (!double.TryParse(
                valueText.Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double value))
            {
                return 0;
            }

            double multiplier = unit.ToUpperInvariant() switch
            {
                "GB" => 1024d * 1024d * 1024d,
                "MB" => 1024d * 1024d,
                "KB" => 1024d,
                _ => 1d
            };

            return Math.Max(
                0,
                (long)Math.Round(value * multiplier, MidpointRounding.AwayFromZero));
        }

        // 解析小米线刷fastboot输出并更新bootflash进度条
        private void ParseXiaomiFlashPartitionProgressFallback(string output)
        {
            try
            {
                if (string.IsNullOrEmpty(output) || bootflash == null)
                    return;

                double currentValue = bootflash.Value;

                // 匹配百分比格式: (XX.X%) 或 (XX%)
                // 示例: "modem_ab: 3.9 MB/266.6 MB (1.5%) [raw] 38.75 MB/s"
                var percentMatch = System.Text.RegularExpressions.Regex.Match(output, @"\((\d+(?:\.\d+)?)%\)");
                if (percentMatch.Success)
                {
                    string percentStr = percentMatch.Groups[1].Value;
                    // 使用InvariantCulture确保小数点正确解析
                    if (double.TryParse(percentStr.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double percent))
                    {
                        // 检查是否是小文件（KB级别）的100%，如果是则忽略
                        // 匹配格式: "partition_name: XX KB/XX KB (100.0%)" 或 "partition_name: XXX B/XXX B (100.0%)"
                        bool isSmallFile = System.Text.RegularExpressions.Regex.IsMatch(output, @"\d+\s*(B|KB)/\d+\s*(B|KB)\s*\(100\.0%\)");
                        
                        if (percent == 100.0 && isSmallFile)
                        {
                            // 忽略小文件的100%，避免进度条过早到达100%
                            return;
                        }
                        
                        // 如果当前进度已经很高（>90%），但新的百分比很低（<50%），说明开始传输新的大文件
                        // 这时应该重置进度条
                        if (currentValue > 90 && percent < 50)
                        {
                            bootflash.Value = percent;
                            return;
                        }
                        
                        // 正常情况：只增不减
                        if (percent > currentValue)
                        {
                            bootflash.Value = Math.Min(100, percent);
                        }
                    }
                    return;
                }

                // 如果没有匹配到百分比，检测关键操作阶段
                string lowerOutput = output.ToLower();
                
                // 只在真正重启时才设置为100%（检测"Rebooting"单独出现）
                if (System.Text.RegularExpressions.Regex.IsMatch(output, @"^\s*Rebooting\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    bootflash.Value = 100;
                    return;
                }
                
                // 以下只在进度很低时才更新
                if (currentValue < 5)
                {
                    // 开始操作
                    if (lowerOutput.Contains("target reported max download size"))
                    {
                        bootflash.Value = 1;
                    }
                    // 擦除操作
                    else if (lowerOutput.Contains("erasing") && !lowerOutput.Contains("erase successfully"))
                    {
                        bootflash.Value = 3;
                    }
                    // 发送数据中
                    else if (lowerOutput.Contains("sending") && !lowerOutput.Contains("okay"))
                    {
                        bootflash.Value = 5;
                    }
                }
            }
            catch (Exception ex)
            {
                // 静默处理解析错误，不影响主流程
                System.Diagnostics.Debug.WriteLine($"解析小米线刷进度时出错: {ex.Message}");
            }
        }
        
        // 日志窗口相关方法
        
        // 添加日志消息的辅助方法
        private void AddLogMessage(string level, string message)
        {
            var logTextBlock = this.FindName("LogTextBlock") as TextBlock;
            var logScrollViewer = this.FindName("LogScrollViewer") as ScrollViewer;
            
            if (logTextBlock != null)
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                
                // 在UI线程中更新日志
                Dispatcher.Invoke(() =>
                {
                    // 创建新的Run元素用于添加带颜色的文本
                    var run = new Run($"[{timestamp}] {message}\n");
                    
                    // 根据消息内容设置颜色
                    if (message.Contains("安装成功") || message.Contains("安装完成"))
                    {
                        // 绿色显示安装成功和安装完成
                        run.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69)); // #28A745
                    }
                    else if (level == "成功")
                    {
                        // 绿色显示成功消息
                        run.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69)); // #28A745
                    }
                    else if (level == "错误")
                    {
                        // 红色显示错误消息
                        run.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // #DC3545
                    }
                    else if (level == "警告")
                    {
                        // 橙色显示警告消息
                        run.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 165, 0)); // #FFA500
                    }
                    else if (level == "信息")
                    {
                        // 蓝色显示信息消息
                        run.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(23, 162, 184)); // #17A2B8
                    }
                    else
                    {
                        // 默认颜色
                        run.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51)); // #333
                    }
                    
                    // 将Run添加到TextBlock的Inlines集合中
                    logTextBlock.Inlines.Add(run);
                    
                    // 自动滚动到底部
                    if (logScrollViewer != null)
                    {
                        logScrollViewer.ScrollToEnd();
                    }
                });
            }
        }

        private void AddDownloadLogMessage(string level, string message)
        {
            AddLogMessage(level, message);
        }
        
        // 辅助方法：查找可视化子元素
        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T)
                {
                    return (T)child;
                }
                else
                {
                    T? childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null)
                        return childOfChild;
                }
            }
            return null;
        }



        private void OfficialMaskComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void txtBootPath_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void CheckBox10_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void BatteryControl_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private void RichTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void CheckBox_Checked_3()
        {

        }

        private void CheckBox_Checked_4()
        {

        }

        private void FastbootLogTextBox_TextChanged_3(object sender, TextChangedEventArgs e)
        {

        }

        private void TextBox_TextChanged_6(object sender, TextChangedEventArgs e)
        {

        }

        private void CheckBox_Checked_5(object sender, RoutedEventArgs e)
        {

        }

        private void EdlSkipDataCheckBox_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void EdlGenerateProgramCheckBox_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void EdlSkipDataCheckBox_Checked_1(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBox_Checked_6(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBox_Checked_7(object sender, RoutedEventArgs e)
        {

        }


        private void CheckBox_Checked_8(object sender, RoutedEventArgs e)
        {

        }

        private void txtLog_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void AvbInitBootPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void LogBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }


    }

    // 文件项数据模型
    public class FileItem : System.ComponentModel.INotifyPropertyChanged
    {
        private ImageSource? _previewImage;

        public string Name { get; set; } = "";
        public string TimeText { get; set; } = "";
        public bool IsPlaceholder { get; set; }
        public bool IsFile { get; set; }
        public bool IsArchive { get; set; }
        public bool IsImageFile { get; set; }
        public bool IsPreviewableImage { get; set; }
        public ImageSource? PreviewImage
        {
            get => _previewImage;
            set
            {
                if (ReferenceEquals(_previewImage, value))
                {
                    return;
                }

                _previewImage = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(PreviewImage)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HasImagePreview)));
            }
        }

        public bool HasImagePreview => PreviewImage != null;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    // 分区信息数据模型
    public class PartitionInfo : System.ComponentModel.INotifyPropertyChanged
    {
        private bool isSelected;
        private string partitionName = "";
        private string partitionSize = "--";
        private string partitionType = "--";
        private string filePath = "";
        private string additionalInfo = "--";

        public bool IsSelected
        {
            get { return isSelected; }
            set
            {
                if (isSelected != value)
                {
                    isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public string PartitionName
        {
            get { return partitionName; }
            set
            {
                if (partitionName != value)
                {
                    partitionName = value;
                    OnPropertyChanged(nameof(PartitionName));
                    OnPropertyChanged(nameof(IsBasebandFingerprintProtected));
                    OnPropertyChanged(nameof(IsAdbDataProtected));
                }
            }
        }

        public bool IsBasebandFingerprintProtected =>
            MainWindow.IsFastbootVisualizationBasebandProtectedPartitionLabel(PartitionName);

        public bool IsAdbDataProtected =>
            MainWindow.IsEdlDataPartitionLabel(PartitionName);

        public string PartitionSize
        {
            get { return partitionSize; }
            set
            {
                if (partitionSize != value)
                {
                    partitionSize = value;
                    OnPropertyChanged(nameof(PartitionSize));
                }
            }
        }

        public string PartitionType
        {
            get { return partitionType; }
            set
            {
                if (partitionType != value)
                {
                    partitionType = value;
                    OnPropertyChanged(nameof(PartitionType));
                }
            }
        }

        public string FilePath
        {
            get { return filePath; }
            set
            {
                if (filePath != value)
                {
                    filePath = value;
                    OnPropertyChanged(nameof(FilePath));
                }
            }
        }

        public string AdditionalInfo
        {
            get { return additionalInfo; }
            set
            {
                if (additionalInfo != value)
                {
                    additionalInfo = value;
                    OnPropertyChanged(nameof(AdditionalInfo));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
    
    // CPU代号到名称的映射方法
public partial class MainWindow : Window
    {
        private void SocialMediaButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is string url)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"无法打开链接：{ex.Message}");
                }
            }
        }
    private string GetCpuNameByCode(string cpuCode)
    {
        if (string.IsNullOrEmpty(cpuCode) || cpuCode == "--" || cpuCode == "--")
        {
            return "--";
        }
        
        var cpuMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // 骁龙系列
            { "SM4375", "骁龙4 Gen 1" },
            { "SM4450", "骁龙4 Gen 2" },
            { "SM4635", "骁龙4s Gen 2" },
            { "SM6450", "骁龙6 Gen 1" },
            { "SM6475-AB", "骁龙6 Gen 3" },
            { "SM6650", "骁龙6 Gen 4" },
            { "SM7435-AB", "骁龙7s Gen 2" },
            { "SM7475-AB", "骁龙7+ Gen 2" },
            { "SM7675", "骁龙7+ Gen 3" },
            { "SM8450", "骁龙8 Gen 1" },
            { "SM8475", "骁龙8+ Gen 1" },
            { "SM8550", "骁龙8 Gen 2" },
            { "SM8635", "骁龙8s Gen 3" },
            { "SM8650", "骁龙8 Gen 3" },
            { "SM8750", "骁龙8至尊 (Elite)" },
            { "SM8850", "骁龙8至尊2 (Elite 2)" },
            { "SM8850s", "骁龙8至尊2s (Elite 2s)" },
            { "MSM8998", "骁龙835" },
            { "SDM845", "骁龙845" },
            { "SM8150", "骁龙855" },
            { "SM8250", "骁龙865" },
            { "SM8350", "骁龙888" },
            { "SDM710", "骁龙710" },
            { "SM7250-AB", "骁龙765G" },
            { "SM7325", "骁龙778G" },
            { "MSM8953", "骁龙625" },
            { "SDM660", "骁龙660" },
            { "SM6350", "骁龙690" },
            
            // 天玑系列
            { "MT6833", "天玑700/6020/6100+" },
            { "MT6853", "天玑720" },
            { "MT6873", "天玑800" },
            { "MT6853T", "天玑800U" },
            { "MT6877TT", "天玑1080/7050" },
            { "MT6891", "天玑1100/8020" },
            { "MT6893", "天玑1200" },
            { "MT6893Z", "天玑1300/8050" },
            { "MT6895T", "天玑8100" },
            { "MT6896", "天玑8200" },
            { "MT6897", "天玑8300" },
            { "MT6899", "天玑8400" },
            { "MT6983", "天玑9000" },
            { "MT6985", "天玑9200" },
            { "MT6989", "天玑9300" },
            { "MT6991", "天玑9400" }
        };
        
        return cpuMapping.TryGetValue(cpuCode, out string cpuName) ? cpuName : "未知";
    }
    
    private async Task<string> GetWindowsVersionAsync()
    {
        try
        {
            string wmicPath = "wmic";
            var processStartInfo = new ProcessStartInfo
            {
                FileName = wmicPath,
                Arguments = "os get Caption,OSArchitecture,Version /format:csv",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };
            
            using (var process = new Process { StartInfo = processStartInfo })
            {
                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (line.Contains("Microsoft Windows") && line.Contains(","))
                        {
                            var parts = line.Split(',');
                            if (parts.Length >= 4)
                            {
                                string caption = parts[1]?.Trim();
                                string architecture = parts[2]?.Trim();
                                string version = parts[3]?.Trim();
                                
                                if (!string.IsNullOrEmpty(caption))
                                {
                                    // 提取Windows版本号，去掉中文后缀（专业版、旗舰版等）
                                    var match = System.Text.RegularExpressions.Regex.Match(caption, @"Windows\s+(\d+(?:\.\d+)?)");
                                    if (match.Success)
                                    {
                                        return $"Windows {match.Groups[1].Value}";
                                    }
                                    // 如果无法匹配到版本号，返回原始caption
                                    return caption;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"获取Windows版本失败: {ex.Message}");
        }
        
        return "--";
    }
    
    // 传入文件到手机按钮点击事件
    private async void Button_Click_1(object sender, RoutedEventArgs e)
    {
       try
        {
            // 获取按钮对象并检查其内容来区分功能
            if (sender is System.Windows.Controls.Button button)
            {
                string action = button.Tag as string ?? "";
                if (string.IsNullOrEmpty(action))
                {
                    if (button.Content is string s) action = s;
                    else if (button.Content is System.Windows.Controls.TextBlock tb) action = tb.Text;
                    else if (button.Content is System.Windows.Controls.Panel panel)
                    {
                        var childTb = panel.Children.OfType<System.Windows.Controls.TextBlock>().FirstOrDefault();
                        action = childTb?.Text ?? (button.Content?.ToString() ?? "");
                    }
                    else
                    {
                        action = button.Content?.ToString() ?? "";
                    }
                }
                
                if (action == "解包Payload")
                {
                    await HandlePayloadUnpack();
                }
                else if (action == "传入文件到手机")
                {
                    // 传入文件到手机：禁用按钮避免重复点击
                    button.IsEnabled = false;
                    bool transferLockAcquired = _systemZoneTransferLock.Wait(0);
                    try
                    {
                        if (!transferLockAcquired)
                        {
                            FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 已有文件传输任务正在进行，请稍后再试";
                            FileTransferLogTextBox.ScrollToEnd();
                            return;
                        }

                        await HandleFileTransfer();
                    }
                    finally
                    {
                        if (transferLockAcquired)
                        {
                            _systemZoneTransferLock.Release();
                        }

                        button.IsEnabled = true;
                    }
                }
                else if (action == "开始检测设备")
                {
                    HandleStartDeviceDetection();
                }
                else if (action == "停止检测设备")
                {
                    HandleStopDeviceDetection();
                }
                else if (action == "读分区表")
                {
                    await HandleReadPartitionTable();
                }
            }
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 操作过程中发生异常: {ex.Message}";
                FileTransferLogTextBox.ScrollToEnd();
            });
        }
    }
    
        private sealed class RichTextBoxLogBuffer : IDisposable
        {
            private readonly System.Windows.Controls.RichTextBox _richTextBox;
            private readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
            private readonly DispatcherTimer _timer;
            private readonly int _maxCharacters;
            private int _approxCharacterCount;
            private bool _disposed;

            public RichTextBoxLogBuffer(System.Windows.Controls.RichTextBox richTextBox, TimeSpan interval, int maxCharacters)
            {
                _richTextBox = richTextBox;
                _maxCharacters = Math.Max(0, maxCharacters);
                _timer = new DispatcherTimer(DispatcherPriority.Background, richTextBox.Dispatcher)
                {
                    Interval = interval
                };
                _timer.Tick += TimerOnTick;
                _timer.Start();
            }

            public void Enqueue(string text)
            {
                if (_disposed) return;
                if (string.IsNullOrEmpty(text)) return;
                _queue.Enqueue(text);
            }

            private void TimerOnTick(object? sender, EventArgs e)
            {
                Flush();
            }

            public void Flush()
            {
                if (_disposed) return;
                if (_queue.IsEmpty) return;

                if (_richTextBox.Dispatcher.CheckAccess())
                {
                    FlushCore();
                    return;
                }

                _richTextBox.Dispatcher.Invoke(FlushCore);
            }

            private void FlushCore()
            {
                if (_disposed) return;
                if (_queue.IsEmpty) return;

                var sb = new StringBuilder();
                while (_queue.TryDequeue(out var item))
                {
                    sb.Append(item);
                }

                if (sb.Length == 0) return;

                if (_maxCharacters > 0 && _approxCharacterCount + sb.Length > _maxCharacters)
                {
                    _richTextBox.Document.Blocks.Clear();
                    _approxCharacterCount = 0;
                }

                var text = sb.ToString().Replace("\r\n", "\n");
                var lines = text.Split('\n');
                foreach (var line in lines)
                {
                    if (string.IsNullOrEmpty(line))
                    {
                        continue;
                    }

                    var paragraph = new Paragraph { Margin = new Thickness(0) };
                    paragraph.Inlines.Add(new Run(line));
                    _richTextBox.Document.Blocks.Add(paragraph);
                    _approxCharacterCount += line.Length;
                }

                _richTextBox.ScrollToEnd();
            }

            public void Dispose()
            {
                if (_disposed) return;
                if (_richTextBox.Dispatcher.CheckAccess())
                {
                    DisposeCore();
                    return;
                }

                _richTextBox.Dispatcher.Invoke(DisposeCore);
            }

            private void DisposeCore()
            {
                if (_disposed) return;
                _disposed = true;
                _timer.Stop();
                _timer.Tick -= TimerOnTick;
                FlushCore();
            }
        }

        private const string AdbServerHost = "127.0.0.1";
        private const int AdbServerPort = 5037;
        private const int AdbSyncChunkSize = 64 * 1024;
        private static readonly Regex AnsiEscapeSequenceRegex = new Regex(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);
        private static readonly Regex AdbTransferPercentRegex = new Regex(@"(?<!\d)(?<percent>\d{1,3})%", RegexOptions.Compiled);

        private static string SanitizeProcessLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return string.Empty;
            var cleaned = AnsiEscapeSequenceRegex.Replace(line, string.Empty);
            cleaned = cleaned.Replace("\0", string.Empty);
            return cleaned;
        }

        private static void ConfigureHiddenRedirectedProcess(ProcessStartInfo startInfo)
        {
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            if (startInfo.StandardOutputEncoding == null || startInfo.StandardErrorEncoding == null)
            {
                Encoding encoding;
                try
                {
                    var oemCodePage = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
                    encoding = Encoding.GetEncoding(oemCodePage);
                }
                catch
                {
                    encoding = Encoding.UTF8;
                }

                startInfo.StandardOutputEncoding ??= encoding;
                startInfo.StandardErrorEncoding ??= encoding;
            }
        }

        private void SetSystemZoneTransferProgress(double progress)
        {
            Dispatcher.Invoke(() =>
            {
                if (SystemZoneProgressBar == null)
                {
                    return;
                }

                SystemZoneProgressBar.Visibility = Visibility.Visible;
                SystemZoneProgressBar.Minimum = 0;
                SystemZoneProgressBar.Maximum = 100;
                double normalizedProgress = Math.Max(0, Math.Min(100, progress));
                SystemZoneProgressBar.Value = normalizedProgress;
                UpdateSystemZoneProgressBarTag();
            });
        }

        private void UpdateSystemZoneProgressBarTag(string? indexText = null, string? speedText = null)
        {
            if (SystemZoneProgressBar == null)
            {
                return;
            }

            string currentIndex = "0/0";
            string currentSpeed = "0.00 B/s";
            string? currentTag = SystemZoneProgressBar.Tag?.ToString();
            if (!string.IsNullOrWhiteSpace(currentTag))
            {
                string[] parts = currentTag.Split(new[] { "    " }, StringSplitOptions.None);
                if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
                {
                    currentIndex = parts[0];
                }

                if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    currentSpeed = parts[1];
                }
            }

            SystemZoneProgressBar.Tag = $"{indexText ?? currentIndex}    {speedText ?? currentSpeed}";
        }

        private void SetSystemZoneTransferFileProgress(int currentFileIndex, int totalFileCount)
        {
            Dispatcher.Invoke(() =>
            {
                if (SystemZoneProgressBar == null)
                {
                    return;
                }

                int safeTotal = Math.Max(0, totalFileCount);
                int safeCurrent = safeTotal == 0 ? 0 : Math.Max(0, Math.Min(currentFileIndex, safeTotal));
                UpdateSystemZoneProgressBarTag(indexText: $"{safeCurrent}/{safeTotal}");
            });
        }

        private void SetSystemZoneTransferSpeed(double bytesPerSecond)
        {
            Dispatcher.Invoke(() =>
            {
                if (SystemZoneProgressBar == null)
                {
                    return;
                }

                double safeBytesPerSecond = Math.Max(0, bytesPerSecond);
                UpdateSystemZoneProgressBarTag(speedText: FormatTransferSpeed(safeBytesPerSecond));
            });
        }

        private static string FormatTransferSpeed(double bytesPerSecond)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytesPerSecond;
            int unitIndex = 0;

            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024.0;
                unitIndex++;
            }

            return $"{value:F2} {units[unitIndex]}/s";
        }

        private static double CalculateTransferSpeed(Stopwatch stopwatch, long transferredBytes, ref double? lastSmoothedSpeed)
        {
            double elapsedSeconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 1e-6);
            double currentSpeed = transferredBytes > 0 ? transferredBytes / elapsedSeconds : 0;

            if (lastSmoothedSpeed.HasValue)
            {
                currentSpeed = lastSmoothedSpeed.Value * 0.7 + currentSpeed * 0.3;
            }

            lastSmoothedSpeed = currentSpeed;
            return currentSpeed;
        }

        private string BuildAdbArguments(string command)
        {
            string selectedSerial = GetSelectedDeviceSerial();
            return string.IsNullOrWhiteSpace(selectedSerial) ? command : $"-s {selectedSerial} {command}";
        }

        private static string ExtractTransferSummary(string stdOut, string stdErr)
        {
            var merged = $"{stdOut}\n{stdErr}";
            var lines = merged
                .Replace("\r", "\n")
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => SanitizeProcessLine(line).Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Where(line => !AdbTransferPercentRegex.IsMatch(line))
                .ToList();

            return lines.Count == 0 ? string.Empty : lines[^1];
        }

        private static double CalculateTransferPercent(long transferredBytes, long totalBytes)
        {
            if (totalBytes <= 0)
            {
                return 0;
            }

            long safeTransferredBytes = Math.Max(0, Math.Min(totalBytes, transferredBytes));
            return (safeTransferredBytes * 100d) / totalBytes;
        }

        private static long GetExistingFileSize(string filePath)
        {
            try
            {
                return File.Exists(filePath) ? new FileInfo(filePath).Length : 0L;
            }
            catch
            {
                return 0L;
            }
        }

        private static bool IsAdbDeviceDisconnectedError(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            string normalized = message.Trim().ToLowerInvariant();
            return normalized.Contains("no devices/emulators found")
                || normalized.Contains("no devices found")
                || normalized.Contains("device offline")
                || normalized.Contains("device not found")
                || normalized.Contains("more than one device/emulator")
                || normalized.Contains("unauthorized")
                || normalized.Contains("cannot connect to daemon")
                || normalized.Contains("failed to get feature set")
                || normalized.Contains("closed");
        }

        private static byte[] GetLittleEndianBytes(int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return bytes;
        }

        private static async Task<byte[]> ReceiveExactAsync(
            Stream stream,
            int length,
            CancellationToken cancellationToken = default)
        {
            byte[] buffer = new byte[length];
            int offset = 0;

            while (offset < length)
            {
                int read = await stream.ReadAsync(buffer, offset, length - offset, cancellationToken);
                if (read <= 0)
                {
                    throw new IOException("ADB 连接被意外关闭。");
                }

                offset += read;
            }

            return buffer;
        }

        private static async Task SendAdbRequestAsync(
            Stream stream,
            string payload,
            CancellationToken cancellationToken = default)
        {
            byte[] data = Encoding.UTF8.GetBytes(payload);
            byte[] header = Encoding.ASCII.GetBytes(data.Length.ToString("x4"));
            await stream.WriteAsync(header, 0, header.Length, cancellationToken);
            await stream.WriteAsync(data, 0, data.Length, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        private static async Task ReadAdbStatusAsync(
            Stream stream,
            CancellationToken cancellationToken = default)
        {
            string status = Encoding.ASCII.GetString(await ReceiveExactAsync(stream, 4, cancellationToken));
            if (status == "OKAY")
            {
                return;
            }

            if (status == "FAIL")
            {
                string hexLength = Encoding.ASCII.GetString(await ReceiveExactAsync(stream, 4, cancellationToken));
                int messageLength = int.Parse(hexLength, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                string message = Encoding.UTF8.GetString(await ReceiveExactAsync(stream, messageLength, cancellationToken));
                throw new InvalidOperationException($"ADB FAIL: {message}");
            }

            throw new InvalidOperationException($"未知 ADB 状态: {status}");
        }

        private static async Task SendSyncPacketAsync(
            Stream stream,
            string ident,
            int length,
            CancellationToken cancellationToken = default)
        {
            if (ident.Length != 4)
            {
                throw new ArgumentException("SYNC ident 必须是 4 个字符。", nameof(ident));
            }

            byte[] identBytes = Encoding.ASCII.GetBytes(ident);
            byte[] lengthBytes = GetLittleEndianBytes(length);
            await stream.WriteAsync(identBytes, 0, identBytes.Length, cancellationToken);
            await stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, cancellationToken);
        }

        private static async Task ReadSyncStatusAsync(
            Stream stream,
            CancellationToken cancellationToken = default)
        {
            string ident = Encoding.ASCII.GetString(await ReceiveExactAsync(stream, 4, cancellationToken));
            int length = BitConverter.ToInt32(await ReceiveExactAsync(stream, 4, cancellationToken), 0);

            if (ident == "OKAY")
            {
                return;
            }

            if (ident == "FAIL")
            {
                string message = Encoding.UTF8.GetString(await ReceiveExactAsync(stream, length, cancellationToken));
                throw new InvalidOperationException($"SYNC FAIL: {message}");
            }

            throw new InvalidOperationException($"未知 SYNC 响应: ident={ident}, length={length}");
        }

        private async Task EnsureAdbServerRunningAsync(CancellationToken cancellationToken = default)
        {
            string adbPath = GetToolPath("adb.exe");
            if (string.IsNullOrWhiteSpace(adbPath))
            {
                throw new FileNotFoundException("未找到 adb.exe");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = adbPath,
                Arguments = "start-server",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                }
                throw;
            }
        }

        private async Task ExecuteAdbSyncPushAsync(
            string localFilePath,
            string remotePath,
            Action<long, long>? progressCallback = null,
            int mode = 0x1A4,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(localFilePath))
            {
                throw new FileNotFoundException($"找不到本地文件: {localFilePath}");
            }

            await EnsureAdbServerRunningAsync(cancellationToken);

            string selectedSerial = GetSelectedDeviceSerial();
            long totalSize = new FileInfo(localFilePath).Length;
            int mtime = (int)new FileInfo(localFilePath).LastWriteTimeUtc.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            string sendTarget = $"{remotePath},{mode}";

            using var client = new TcpClient();
            await client.ConnectAsync(AdbServerHost, AdbServerPort, cancellationToken);
            client.ReceiveTimeout = 30000;
            client.SendTimeout = 30000;

            using NetworkStream stream = client.GetStream();

            await SendAdbRequestAsync(stream, string.IsNullOrWhiteSpace(selectedSerial) ? "host:transport-any" : $"host:transport:{selectedSerial}", cancellationToken);
            await ReadAdbStatusAsync(stream, cancellationToken);

            await SendAdbRequestAsync(stream, "sync:", cancellationToken);
            await ReadAdbStatusAsync(stream, cancellationToken);

            byte[] sendTargetBytes = Encoding.UTF8.GetBytes(sendTarget);
            await SendSyncPacketAsync(stream, "SEND", sendTargetBytes.Length, cancellationToken);
            await stream.WriteAsync(sendTargetBytes, 0, sendTargetBytes.Length, cancellationToken);

            long sent = 0;
            byte[] buffer = new byte[AdbSyncChunkSize];

            using FileStream fileStream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            while (true)
            {
                int read = await fileStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (read <= 0)
                {
                    break;
                }

                await SendSyncPacketAsync(stream, "DATA", read, cancellationToken);
                await stream.WriteAsync(buffer, 0, read, cancellationToken);
                sent += read;
                progressCallback?.Invoke(sent, totalSize);
            }

            await SendSyncPacketAsync(stream, "DONE", mtime, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            await ReadSyncStatusAsync(stream, cancellationToken);
            progressCallback?.Invoke(totalSize, totalSize);
        }

        private async Task ExecuteAdbSyncPullAsync(
            string remotePath,
            string localFilePath,
            Action<long, long>? progressCallback = null,
            long? expectedTotalSize = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnsureAdbServerRunningAsync(cancellationToken);

            long totalSize = await TryGetRemoteFileSizeAsync(remotePath) ?? Math.Max(0L, expectedTotalSize ?? 0L);
            string selectedSerial = GetSelectedDeviceSerial();

            string? localDirectory = Path.GetDirectoryName(localFilePath);
            if (!string.IsNullOrWhiteSpace(localDirectory))
            {
                Directory.CreateDirectory(localDirectory);
            }

            using var client = new TcpClient();
            await client.ConnectAsync(AdbServerHost, AdbServerPort, cancellationToken);
            client.ReceiveTimeout = 30000;
            client.SendTimeout = 30000;

            using NetworkStream stream = client.GetStream();

            await SendAdbRequestAsync(stream, string.IsNullOrWhiteSpace(selectedSerial) ? "host:transport-any" : $"host:transport:{selectedSerial}", cancellationToken);
            await ReadAdbStatusAsync(stream, cancellationToken);

            await SendAdbRequestAsync(stream, "sync:", cancellationToken);
            await ReadAdbStatusAsync(stream, cancellationToken);

            byte[] remotePathBytes = Encoding.UTF8.GetBytes(remotePath);
            await SendSyncPacketAsync(stream, "RECV", remotePathBytes.Length, cancellationToken);
            await stream.WriteAsync(remotePathBytes, 0, remotePathBytes.Length, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            long received = 0;

            try
            {
                using FileStream fileStream = new FileStream(localFilePath, FileMode.Create, FileAccess.Write, FileShare.None);

                while (true)
                {
                    string ident = Encoding.ASCII.GetString(await ReceiveExactAsync(stream, 4, cancellationToken));
                    int length = BitConverter.ToInt32(await ReceiveExactAsync(stream, 4, cancellationToken), 0);

                    if (ident == "DATA")
                    {
                        byte[] data = await ReceiveExactAsync(stream, length, cancellationToken);
                        await fileStream.WriteAsync(data, 0, data.Length, cancellationToken);
                        received += data.Length;
                        progressCallback?.Invoke(received, totalSize);
                        continue;
                    }

                    if (ident == "DONE")
                    {
                        break;
                    }

                    if (ident == "FAIL")
                    {
                        string message = Encoding.UTF8.GetString(await ReceiveExactAsync(stream, length, cancellationToken));
                        throw new InvalidOperationException($"SYNC FAIL: {message}");
                    }

                    throw new InvalidOperationException($"未知 SYNC 响应: ident={ident}, length={length}");
                }

                await fileStream.FlushAsync(cancellationToken);
            }
            catch
            {
                try
                {
                    if (File.Exists(localFilePath))
                    {
                        File.Delete(localFilePath);
                    }
                }
                catch
                {
                }

                throw;
            }

            progressCallback?.Invoke(Math.Max(received, totalSize), Math.Max(totalSize, received));
        }

        // ADB 的 SYNC 服务始终以 shell 身份运行，不能直接读写 /data 等 Root 路径。
        // Root 模式下先使用 shell 可访问的临时文件完成 SYNC，再由 su 完成最终复制。
        private const string SystemZoneStagingDirectory = "/data/local/tmp";

        private static string CreateSystemZoneStagingPath()
        {
            return $"{SystemZoneStagingDirectory}/violet-systemzone-{Guid.NewGuid():N}";
        }

        private async Task ExecuteSystemZonePushAsync(
            string localFilePath,
            string remotePath,
            Action<long, long>? progressCallback = null,
            int mode = 0x1A4,
            CancellationToken cancellationToken = default)
        {
            if (!IsSystemZoneRootDirectorySelected())
            {
                await ExecuteAdbSyncPushAsync(localFilePath, remotePath, progressCallback, mode, cancellationToken);
                return;
            }

            string stagingPath = CreateSystemZoneStagingPath();
            long totalSize = new FileInfo(localFilePath).Length;
            try
            {
                await ExecuteAdbSyncPushAsync(
                    localFilePath,
                    stagingPath,
                    (sentBytes, currentFileTotalBytes) =>
                    {
                        long safeTotalBytes = Math.Max(1, currentFileTotalBytes);
                        // 保留最后一小段进度给 Root 复制，避免文件尚未落到目标路径就显示完成。
                        long stagedBytes = Math.Min(safeTotalBytes, sentBytes) * 95 / 100;
                        progressCallback?.Invoke(stagedBytes, safeTotalBytes);
                    },
                    mode,
                    cancellationToken);

                string copyCommand =
                    $"cp -f -- {QuoteAndroidShellArgument(stagingPath)} {QuoteAndroidShellArgument(remotePath)}";
                await ExecuteSystemZoneRootCommandAsync(copyCommand);
                progressCallback?.Invoke(totalSize, totalSize);
            }
            finally
            {
                await DeleteSystemZoneStagingFileAsync(stagingPath);
            }
        }

        private async Task ExecuteSystemZonePullAsync(
            string remotePath,
            string localFilePath,
            Action<long, long>? progressCallback = null,
            long? expectedTotalSize = null,
            CancellationToken cancellationToken = default)
        {
            if (!IsSystemZoneRootDirectorySelected())
            {
                await ExecuteAdbSyncPullAsync(
                    remotePath,
                    localFilePath,
                    progressCallback,
                    expectedTotalSize,
                    cancellationToken);
                return;
            }

            string stagingPath = CreateSystemZoneStagingPath();
            try
            {
                // 使 staging 文件可被 shell 身份的 ADB SYNC 服务读取。
                string copyCommand =
                    $"cp -f -- {QuoteAndroidShellArgument(remotePath)} {QuoteAndroidShellArgument(stagingPath)} && " +
                    $"chmod 0644 -- {QuoteAndroidShellArgument(stagingPath)}";
                await ExecuteSystemZoneRootCommandAsync(copyCommand);
                await ExecuteAdbSyncPullAsync(
                    stagingPath,
                    localFilePath,
                    progressCallback,
                    expectedTotalSize,
                    cancellationToken);
            }
            finally
            {
                await DeleteSystemZoneStagingFileAsync(stagingPath);
            }
        }

        private async Task ExecuteSystemZoneRootCommandAsync(string rootCommand)
        {
            var result = await RunSystemZoneAdbCommandAsync(
                $"shell su -c {QuoteAndroidShellArgument(rootCommand)}");
            if (result.ExitCode == 0 && string.IsNullOrWhiteSpace(result.StdErr))
            {
                return;
            }

            string message = string.IsNullOrWhiteSpace(result.StdErr)
                ? $"ADB 退出码 {result.ExitCode}"
                : result.StdErr.Trim();
            throw new InvalidOperationException(message);
        }

        // 文件管理操作必须与当前加载位置使用相同的权限上下文。
        // 内部存储保持 shell；根目录模式统一通过 su 执行。
        private Task<(int ExitCode, string StdOut, string StdErr)> RunSystemZoneFileCommandAsync(
            string shellCommand)
        {
            string adbCommand = IsSystemZoneRootDirectorySelected()
                ? $"shell su -c {QuoteAndroidShellArgument(shellCommand)}"
                : $"shell {shellCommand}";
            return RunSystemZoneAdbCommandAsync(adbCommand);
        }

        private async Task DeleteSystemZoneStagingFileAsync(string stagingPath)
        {
            try
            {
                await RunSystemZoneAdbCommandAsync(
                    $"shell rm -f -- {QuoteAndroidShellArgument(stagingPath)}");
            }
            catch
            {
                // 临时文件清理失败不应掩盖原始传输错误；临时文件名是每次随机生成的。
            }
        }

        private static long? ParseRemoteFileSize(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            var lines = output
                .Replace("\r", "\n")
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => SanitizeProcessLine(line).Trim());

            foreach (string line in lines)
            {
                if (long.TryParse(line, out long directSize))
                {
                    return directSize;
                }

                Match lsMatch = Regex.Match(line, @"^\S+\s+\d+\s+\S+\s+\S+\s+(?<size>\d+)\s+");
                if (lsMatch.Success && long.TryParse(lsMatch.Groups["size"].Value, out long listedSize))
                {
                    return listedSize;
                }
            }

            return null;
        }

        private async Task<long?> TryGetRemoteFileSizeAsync(string remotePath)
        {
            if (string.IsNullOrWhiteSpace(remotePath))
            {
                return null;
            }

            string escapedRemotePath = remotePath.Replace("\"", "\\\"");
            string[] commands =
            {
                $"shell stat -c %s \"{escapedRemotePath}\"",
                $"shell toybox stat -c %s \"{escapedRemotePath}\"",
                $"shell ls -ln \"{escapedRemotePath}\""
            };

            foreach (string command in commands)
            {
                long? size = ParseRemoteFileSize(await ExecuteAdbCommandWithOutput(command));
                if (size.HasValue)
                {
                    return size;
                }
            }

            return null;
        }

        private async Task<long> ResolvePartitionTransferSizeAsync(string remotePath, string? partitionSizeText)
        {
            long? remoteFileSize = await TryGetRemoteFileSizeAsync(remotePath);
            if (remoteFileSize.HasValue && remoteFileSize.Value > 0)
            {
                return remoteFileSize.Value;
            }

            if (!string.IsNullOrWhiteSpace(partitionSizeText) && partitionSizeText != "--")
            {
                long parsedSize = ParsePartitionSizeToBytes(partitionSizeText);
                if (parsedSize > 0)
                {
                    return parsedSize;
                }
            }

            return 1;
        }

        private static async Task ReadTransferStreamAsync(
            StreamReader reader,
            StringBuilder outputBuilder,
            Action<int>? reportPercent)
        {
            var buffer = new char[256];
            var rollingWindow = new StringBuilder();
            int lastReportedPercent = -1;

            while (true)
            {
                int read = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                string chunk = new string(buffer, 0, read);
                outputBuilder.Append(chunk);

                if (reportPercent == null)
                {
                    continue;
                }

                rollingWindow.Append(chunk);
                if (rollingWindow.Length > 512)
                {
                    rollingWindow.Remove(0, rollingWindow.Length - 512);
                }

                int latestPercent = lastReportedPercent;
                foreach (Match match in AdbTransferPercentRegex.Matches(rollingWindow.ToString()))
                {
                    if (int.TryParse(match.Groups["percent"].Value, out int parsedPercent))
                    {
                        latestPercent = Math.Max(latestPercent, Math.Min(100, parsedPercent));
                    }
                }

                if (latestPercent > lastReportedPercent)
                {
                    lastReportedPercent = latestPercent;
                    reportPercent(latestPercent);
                }
            }
        }

        private async Task<(int ExitCode, string StdOut, string StdErr)> RunAdbTransferWithProgressAsync(
            ProcessStartInfo startInfo,
            Action<int>? reportPercent = null,
            Func<Task<int?>>? pollPercent = null)
        {
            ConfigureHiddenRedirectedProcess(startInfo);

            var stdOutBuilder = new StringBuilder();
            var stdErrBuilder = new StringBuilder();
            object progressLock = new object();
            int lastPercent = -1;

            void SafeReportPercent(int percent)
            {
                lock (progressLock)
                {
                    if (percent <= lastPercent)
                    {
                        return;
                    }

                    lastPercent = percent;
                }

                reportPercent?.Invoke(percent);
            }

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            Task stdOutTask = ReadTransferStreamAsync(process.StandardOutput, stdOutBuilder, SafeReportPercent);
            Task stdErrTask = ReadTransferStreamAsync(process.StandardError, stdErrBuilder, SafeReportPercent);
            Task pollingTask = pollPercent == null
                ? Task.CompletedTask
                : Task.Run(async () =>
                {
                    while (!process.HasExited)
                    {
                        try
                        {
                            int? polledPercent = await pollPercent();
                            if (polledPercent.HasValue)
                            {
                                SafeReportPercent(polledPercent.Value);
                            }
                        }
                        catch
                        {
                        }

                        if (process.HasExited)
                        {
                            break;
                        }

                        await Task.Delay(250);
                    }
                });
            Task waitForExitTask = process.WaitForExitAsync();

            await Task.WhenAll(stdOutTask, stdErrTask, waitForExitTask, pollingTask);

            if (process.ExitCode == 0)
            {
                reportPercent?.Invoke(100);
            }

            return (process.ExitCode, stdOutBuilder.ToString(), stdErrBuilder.ToString());
        }

        private async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessWithLiveLogAsync(
            ProcessStartInfo startInfo,
            RichTextBoxLogBuffer logBuffer,
            TimeSpan? timeout = null,
            string? stillRunningMessage = null,
            TimeSpan? stillRunningInterval = null)
        {
            ConfigureHiddenRedirectedProcess(startInfo);

            var stdOutBuilder = new StringBuilder();
            var stdErrBuilder = new StringBuilder();

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            logBuffer.Enqueue($"[{DateTime.Now:HH:mm:ss}] 启动命令: {startInfo.FileName} {startInfo.Arguments}\n");
            if (!string.IsNullOrWhiteSpace(startInfo.WorkingDirectory))
            {
                logBuffer.Enqueue($"[{DateTime.Now:HH:mm:ss}] 工作目录: {startInfo.WorkingDirectory}\n");
            }

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                var message = ex.InnerException?.Message ?? ex.Message;
                logBuffer.Enqueue($"[{DateTime.Now:HH:mm:ss}] 启动失败: {message}\n");
                logBuffer.Flush();
                return (-1, string.Empty, $"启动失败: {message}");
            }
            var stdoutTask = PumpReaderAsync(process.StandardOutput, false, stdOutBuilder, logBuffer);
            var stderrTask = PumpReaderAsync(process.StandardError, true, stdErrBuilder, logBuffer);

            var exitTask = process.WaitForExitAsync();

            Task? tickerTask = null;
            using var tickerCts = new CancellationTokenSource();
            if (!string.IsNullOrWhiteSpace(stillRunningMessage))
            {
                var interval = stillRunningInterval ?? TimeSpan.FromSeconds(5);
                tickerTask = Task.Run(async () =>
                {
                    while (!tickerCts.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(interval, tickerCts.Token).ConfigureAwait(false);
                        }
                        catch
                        {
                            break;
                        }

                        if (!process.HasExited)
                        {
                            logBuffer.Enqueue($"[{DateTime.Now:HH:mm:ss}] {stillRunningMessage}\n");
                        }
                    }
                }, tickerCts.Token);
            }

            Task completed;
            if (timeout.HasValue && timeout.Value > TimeSpan.Zero)
            {
                completed = await Task.WhenAny(exitTask, Task.Delay(timeout.Value)).ConfigureAwait(false);
                if (completed != exitTask)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    catch
                    {
                    }

                    tickerCts.Cancel();
                    if (tickerTask != null)
                    {
                        try { await tickerTask.ConfigureAwait(false); } catch { }
                    }

                    try { await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false); } catch { }
                    logBuffer.Flush();
                    return (-1, stdOutBuilder.ToString(), $"运行超时({timeout.Value.TotalSeconds:0}s)：{startInfo.FileName} {startInfo.Arguments}");
                }
            }
            else
            {
                await exitTask.ConfigureAwait(false);
            }

            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            tickerCts.Cancel();
            if (tickerTask != null)
            {
                try { await tickerTask.ConfigureAwait(false); } catch { }
            }

            logBuffer.Flush();
            return (process.ExitCode, stdOutBuilder.ToString(), stdErrBuilder.ToString());
        }

        private static async Task PumpReaderAsync(
            StreamReader reader,
            bool isError,
            StringBuilder builder,
            RichTextBoxLogBuffer logBuffer)
        {
            var buffer = new char[1024];
            var carry = string.Empty;

            while (true)
            {
                var read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (read <= 0) break;

                var chunk = new string(buffer, 0, read);
                var combined = carry.Length > 0 ? carry + chunk : chunk;

                var sanitized = SanitizeProcessLine(combined);
                sanitized = sanitized.Replace("\r\n", "\n").Replace('\r', '\n');

                if (combined.IndexOf('\u001b') >= 0 || carry.IndexOf('\u001b') >= 0)
                {
                    var lastEsc = combined.LastIndexOf('\u001b');
                    if (lastEsc >= 0 && combined.Length - lastEsc < 32)
                    {
                        carry = combined.Substring(lastEsc);
                    }
                    else
                    {
                        carry = string.Empty;
                    }
                }
                else
                {
                    carry = string.Empty;
                }

                builder.Append(sanitized);
                if (isError)
                {
                    logBuffer.Enqueue($"[{DateTime.Now:HH:mm:ss}] 错误: {sanitized}");
                    if (!sanitized.EndsWith("\n", StringComparison.Ordinal))
                    {
                        logBuffer.Enqueue("\n");
                    }
                }
                else
                {
                    logBuffer.Enqueue($"[{DateTime.Now:HH:mm:ss}] {sanitized}");
                    if (!sanitized.EndsWith("\n", StringComparison.Ordinal))
                    {
                        logBuffer.Enqueue("\n");
                    }
                }
            }
        }


        // 文件拖放到ListBox时的DragOver事件处理
        private void FileListTextBox_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            // 检查拖放的数据是否包含文件
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                e.Effects = System.Windows.DragDropEffects.Copy;
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }
            e.Handled = true;
        }

        // 文件拖放到ListBox时的Drop事件处理
        private async void FileListTextBox_Drop(object sender, System.Windows.DragEventArgs e)
        {
            bool transferLockAcquired = false;
            try
            {
                if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                {
                    return;
                }

                string[] droppedPaths = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                if (droppedPaths == null || droppedPaths.Length == 0)
                {
                    return;
                }

                transferLockAcquired = _systemZoneTransferLock.Wait(0);
                if (!transferLockAcquired)
                {
                    FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 已有文件传输任务正在进行，本次拖放未执行";
                    FileTransferLogTextBox.ScrollToEnd();
                    return;
                }

                var pushFiles = new List<(string LocalPath, string RemotePath, long Size)>();
                var remoteDirectories = new HashSet<string>(StringComparer.Ordinal);
                int preparationFailed = 0;

                foreach (string droppedPath in droppedPaths)
                {
                    try
                    {
                        if (File.Exists(droppedPath))
                        {
                            string remotePath = CombineAndroidPath(lastLoadedDirectory, Path.GetFileName(droppedPath));
                            pushFiles.Add((droppedPath, remotePath, Math.Max(0, GetExistingFileSize(droppedPath))));
                        }
                        else if (Directory.Exists(droppedPath))
                        {
                            string directoryName = Path.GetFileName(droppedPath.TrimEnd(
                                Path.DirectorySeparatorChar,
                                Path.AltDirectorySeparatorChar));
                            string remoteRoot = CombineAndroidPath(lastLoadedDirectory, directoryName);
                            AddLocalDirectoryToSystemZonePushPlan(
                                droppedPath,
                                remoteRoot,
                                pushFiles,
                                remoteDirectories);
                        }
                        else
                        {
                            throw new FileNotFoundException($"找不到本地文件或文件夹: {droppedPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        preparationFailed++;
                        FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 无法读取拖放项目: {droppedPath}";
                        FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 错误: {ex.Message}";
                        FileTransferLogTextBox.ScrollToEnd();
                    }
                }

                int totalFiles = pushFiles.Count;
                int progressTotal = Math.Max(1, totalFiles);
                int success = 0;
                int failed = 0;
                long totalBytes = pushFiles.Sum(item => item.Size);
                if (totalBytes <= 0)
                {
                    totalBytes = progressTotal;
                }

                long transferredCompletedBytes = 0;
                SetSystemZoneTransferFileProgress(0, progressTotal);
                SetSystemZoneTransferProgress(0);
                SetSystemZoneTransferSpeed(0);

                FileTransferLogTextBox.Text +=
                    $"\n[{DateTime.Now:HH:mm:ss}] 检测到拖放项目 {droppedPaths.Length} 个，包含文件 {totalFiles} 个、目录 {remoteDirectories.Count} 个";
                FileTransferLogTextBox.ScrollToEnd();

                try
                {
                    await CreateSystemZoneRemoteDirectoriesAsync(remoteDirectories);
                }
                catch (Exception ex)
                {
                    preparationFailed++;
                    FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 创建设备目录结构失败: {ex.Message}";
                    FileTransferLogTextBox.ScrollToEnd();
                }

                for (int i = 0; i < totalFiles; i++)
                {
                    var pushFile = pushFiles[i];
                    long completedBytesBeforeCurrent = transferredCompletedBytes;
                    Stopwatch transferStopwatch = Stopwatch.StartNew();
                    double? lastSmoothedSpeed = null;

                    SetSystemZoneTransferFileProgress(i + 1, progressTotal);
                    SetSystemZoneTransferSpeed(0);
                    FileTransferLogTextBox.Text +=
                        $"\n[{DateTime.Now:HH:mm:ss}] [{i + 1}/{totalFiles}] 开始传输: {Path.GetFileName(pushFile.LocalPath)}";
                    FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 源: {pushFile.LocalPath}";
                    FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 目标: {pushFile.RemotePath}";
                    FileTransferLogTextBox.ScrollToEnd();

                    try
                    {
                        await ExecuteSystemZonePushAsync(
                            pushFile.LocalPath,
                            pushFile.RemotePath,
                            (sentBytes, currentFileTotalBytes) =>
                            {
                                long safeFileTotalBytes = Math.Max(1, currentFileTotalBytes);
                                long transferredBytes = completedBytesBeforeCurrent + Math.Min(safeFileTotalBytes, sentBytes);
                                SetSystemZoneTransferProgress(CalculateTransferPercent(transferredBytes, totalBytes));
                                double currentSpeed = CalculateTransferSpeed(transferStopwatch, sentBytes, ref lastSmoothedSpeed);
                                SetSystemZoneTransferSpeed(currentSpeed);
                            });

                        success++;
                        transferredCompletedBytes += pushFile.Size;
                        SetSystemZoneTransferProgress(CalculateTransferPercent(transferredCompletedBytes, totalBytes));
                        FileTransferLogTextBox.Text +=
                            $"\n[{DateTime.Now:HH:mm:ss}] [{i + 1}/{totalFiles}] 传输成功: {Path.GetFileName(pushFile.LocalPath)}";
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        SetSystemZoneTransferProgress(Math.Min(
                            99,
                            CalculateTransferPercent(transferredCompletedBytes, totalBytes)));
                        FileTransferLogTextBox.Text +=
                            $"\n[{DateTime.Now:HH:mm:ss}] [{i + 1}/{totalFiles}] 传输失败: {Path.GetFileName(pushFile.LocalPath)}";
                        FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 错误: {ex.Message}";
                    }

                    FileTransferLogTextBox.ScrollToEnd();
                }

                bool allSucceeded = failed == 0 && preparationFailed == 0;
                SetSystemZoneTransferFileProgress(progressTotal, progressTotal);
                SetSystemZoneTransferProgress(
                    allSucceeded
                        ? 100
                        : Math.Min(99, CalculateTransferPercent(transferredCompletedBytes, totalBytes)));
                SetSystemZoneTransferSpeed(0);

                FileTransferLogTextBox.Text +=
                    $"\n[{DateTime.Now:HH:mm:ss}] 拖放传输完成：文件成功 {success}，文件失败 {failed}，项目准备失败 {preparationFailed}，目录 {remoteDirectories.Count} 个";
                FileTransferLogTextBox.ScrollToEnd();

                await LoadFileListFromPath(currentPath);
            }
            catch (Exception ex)
            {
                SetSystemZoneTransferSpeed(0);
                Dispatcher.Invoke(() =>
                {
                    FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 拖放传输过程中发生异常: {ex.Message}";
                    FileTransferLogTextBox.ScrollToEnd();
                });
            }
            finally
            {
                if (transferLockAcquired)
                {
                    _systemZoneTransferLock.Release();
                }
            }
        }

        private static string CombineAndroidPath(string directoryPath, string name)
        {
            return directoryPath.TrimEnd('/') + "/" + name.Replace('\\', '/');
        }

        private static void AddLocalDirectoryToSystemZonePushPlan(
            string localRoot,
            string remoteRoot,
            List<(string LocalPath, string RemotePath, long Size)> pushFiles,
            HashSet<string> remoteDirectories)
        {
            var pendingDirectories = new Stack<(string LocalPath, string RemotePath)>();
            pendingDirectories.Push((localRoot, remoteRoot));

            while (pendingDirectories.Count > 0)
            {
                var current = pendingDirectories.Pop();
                remoteDirectories.Add(current.RemotePath);

                foreach (string entryPath in Directory.EnumerateFileSystemEntries(current.LocalPath))
                {
                    FileAttributes attributes = File.GetAttributes(entryPath);
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }

                        string childRemotePath = CombineAndroidPath(current.RemotePath, Path.GetFileName(entryPath));
                        pendingDirectories.Push((entryPath, childRemotePath));
                        continue;
                    }

                    string remoteFilePath = CombineAndroidPath(current.RemotePath, Path.GetFileName(entryPath));
                    pushFiles.Add((entryPath, remoteFilePath, Math.Max(0, GetExistingFileSize(entryPath))));
                }
            }
        }

        private async Task CreateSystemZoneRemoteDirectoriesAsync(IEnumerable<string> remoteDirectories)
        {
            const int maxCommandLength = 6000;
            bool useRoot = IsSystemZoneRootDirectorySelected();
            var command = new StringBuilder("mkdir -p");

            async Task FlushAsync()
            {
                if (command.Length <= "mkdir -p".Length)
                {
                    return;
                }

                string adbCommand = useRoot
                    ? $"shell su -c {QuoteAndroidShellArgument(command.ToString())}"
                    : $"shell {command}";
                var result = await RunSystemZoneAdbCommandAsync(adbCommand);
                if (result.ExitCode != 0 || !string.IsNullOrWhiteSpace(result.StdErr))
                {
                    string message = string.IsNullOrWhiteSpace(result.StdErr)
                        ? $"ADB 退出码 {result.ExitCode}"
                        : result.StdErr.Trim();
                    throw new InvalidOperationException(message);
                }

                command.Clear();
                command.Append("mkdir -p");
            }

            foreach (string remoteDirectory in remoteDirectories
                .OrderBy(path => path.Count(character => character == '/'))
                .ThenBy(path => path, StringComparer.Ordinal))
            {
                string argument = " " + QuoteAndroidShellArgument(remoteDirectory);
                if (command.Length + argument.Length > maxCommandLength)
                {
                    await FlushAsync();
                }

                command.Append(argument);
            }

            await FlushAsync();
        }

        // 处理文件传输功能
        private async Task HandleFileTransfer()
        {
        try
        {
            // 打开文件选择对话框
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog()
            {
                Title = "选择要传输到手机的文件",
                Filter = "所有文件 (*.*)|*.*",
                Multiselect = true
            };
            
            if (openFileDialog.ShowDialog() == true)
            {
                var files = openFileDialog.FileNames ?? Array.Empty<string>();

                if (files.Length == 0)
                {
                    return;
                }

                int total = files.Length;
                int success = 0;
                int failed = 0;
                long totalBytes = files.Sum(GetExistingFileSize);
                if (totalBytes <= 0)
                {
                    totalBytes = total;
                }

                long transferredCompletedBytes = 0;
                SetSystemZoneTransferFileProgress(0, total);
                SetSystemZoneTransferProgress(0);
                SetSystemZoneTransferSpeed(0);

                for (int i = 0; i < total; i++)
                {
                    string selectedFilePath = files[i];
                    string fileName = Path.GetFileName(selectedFilePath);
                    long currentFileSize = Math.Max(0, GetExistingFileSize(selectedFilePath));
                    long completedBytesBeforeCurrent = transferredCompletedBytes;
                    Stopwatch transferStopwatch = Stopwatch.StartNew();
                    double? lastSmoothedSpeed = null;

                    // 构建目标路径（手机当前目录）
                    string targetPath = lastLoadedDirectory.EndsWith("/") ? lastLoadedDirectory + fileName : lastLoadedDirectory + "/" + fileName;

                    // 在文件传输日志窗口显示本次传输信息
                    Dispatcher.Invoke(() =>
                    {
                        FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] [{i + 1}/{total}] 开始传输: {fileName}";
                        FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 源: {selectedFilePath}";
                        FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 目标: {targetPath}";
                        FileTransferLogTextBox.ScrollToEnd();
                    });
                    SetSystemZoneTransferFileProgress(i + 1, total);
                    SetSystemZoneTransferSpeed(0);

                    try
                    {
                        await ExecuteSystemZonePushAsync(
                            selectedFilePath,
                            targetPath,
                            (sentBytes, currentFileTotalBytes) =>
                            {
                                long safeFileTotalBytes = Math.Max(1, currentFileTotalBytes);
                                long transferredBytes = completedBytesBeforeCurrent + Math.Min(safeFileTotalBytes, sentBytes);
                                SetSystemZoneTransferProgress(CalculateTransferPercent(transferredBytes, totalBytes));
                                double currentSpeed = CalculateTransferSpeed(transferStopwatch, sentBytes, ref lastSmoothedSpeed);
                                SetSystemZoneTransferSpeed(currentSpeed);
                            });

                        success++;
                        transferredCompletedBytes += currentFileSize;
                        SetSystemZoneTransferProgress(CalculateTransferPercent(transferredCompletedBytes, totalBytes));

                        Dispatcher.Invoke(() =>
                        {
                            FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] [{i + 1}/{total}] 传输成功: {fileName}";
                            FileTransferLogTextBox.ScrollToEnd();
                        });
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        SetSystemZoneTransferProgress(Math.Min(
                            99,
                            CalculateTransferPercent(transferredCompletedBytes, totalBytes)));
                        Dispatcher.Invoke(() =>
                        {
                            FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] [{i + 1}/{total}] 传输失败: {fileName}";
                            FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 错误: {ex.Message}";
                            FileTransferLogTextBox.ScrollToEnd();
                        });
                    }
                }

                SetSystemZoneTransferFileProgress(total, total);
                SetSystemZoneTransferProgress(
                    failed == 0 && success == total
                        ? 100
                        : Math.Min(99, CalculateTransferPercent(transferredCompletedBytes, totalBytes)));
                SetSystemZoneTransferSpeed(0);

                // 汇总统计
                Dispatcher.Invoke(() =>
                {
                    FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 传输完成：成功 {success}，失败 {failed}（共 {total}）";
                    FileTransferLogTextBox.ScrollToEnd();
                });

                if (success > 0)
                {
                    await LoadFileListFromPath(currentPath);
                }
            }
        }
        catch (Exception ex)
        {
            SetSystemZoneTransferFileProgress(0, 0);
            SetSystemZoneTransferProgress(0);
            SetSystemZoneTransferSpeed(0);
            Dispatcher.Invoke(() =>
            {
                FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 传输过程中发生异常: {ex.Message}";
                FileTransferLogTextBox.ScrollToEnd();
            });
        }
    }

    private async void Button_Click_2(object sender, RoutedEventArgs e)
    {
        bool transferLockAcquired = false;
        try
        {
            if (sender is System.Windows.Controls.Button transferOutButton)
            {
                transferOutButton.IsEnabled = false;
            }

            var selectedFiles = FileListTextBox.SelectedItems
                .OfType<FileItem>()
                .Where(item => item.IsFile)
                .ToList();

            if (selectedFiles.Count == 0)
            {
                Dispatcher.Invoke(() =>
                {
                    FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 请先选择要传出的文件";
                    FileTransferLogTextBox.ScrollToEnd();
                });
                return;
            }

            transferLockAcquired = _systemZoneTransferLock.Wait(0);
            if (!transferLockAcquired)
            {
                FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 已有文件传输任务正在进行，请稍后再试";
                FileTransferLogTextBox.ScrollToEnd();
                return;
            }
            
            // 打开文件夹选择对话框
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "选择文件保存路径";
                dialog.ShowNewFolderButton = true;
                
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string savePath = dialog.SelectedPath;
                    int total = selectedFiles.Count;
                    int success = 0;
                    int failed = 0;

                    for (int i = 0; i < total; i++)
                    {
                        string selectedFileName = selectedFiles[i].Name;
                        string phoneFilePath = lastLoadedDirectory.EndsWith("/") ? lastLoadedDirectory + selectedFileName : lastLoadedDirectory + "/" + selectedFileName;
                        string computerFilePath = System.IO.Path.Combine(savePath, selectedFileName);
                        Stopwatch transferStopwatch = Stopwatch.StartNew();
                        double? lastSmoothedSpeed = null;

                        SetSystemZoneTransferFileProgress(i + 1, total);
                        SetSystemZoneTransferProgress(0);
                        SetSystemZoneTransferSpeed(0);

                        Dispatcher.Invoke(() =>
                        {
                            FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] [{i + 1}/{total}] 开始传出文件: {selectedFileName}";
                            FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 手机路径: {phoneFilePath}";
                            FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 电脑路径: {computerFilePath}";
                            FileTransferLogTextBox.ScrollToEnd();
                        });

                        try
                        {
                            await ExecuteSystemZonePullAsync(
                                phoneFilePath,
                                computerFilePath,
                                (receivedBytes, totalBytes) =>
                                {
                                    long safeTotalBytes = Math.Max(1, totalBytes);
                                    SetSystemZoneTransferProgress(CalculateTransferPercent(receivedBytes, safeTotalBytes));
                                    double currentSpeed = CalculateTransferSpeed(transferStopwatch, receivedBytes, ref lastSmoothedSpeed);
                                    SetSystemZoneTransferSpeed(currentSpeed);
                                });

                            success++;
                            SetSystemZoneTransferProgress(100);
                            Dispatcher.Invoke(() =>
                            {
                                FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] [{i + 1}/{total}] 文件传出成功: {selectedFileName}";
                                FileTransferLogTextBox.ScrollToEnd();
                            });
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            SetSystemZoneTransferProgress(success * 100d / total);
                            Dispatcher.Invoke(() =>
                            {
                                FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] [{i + 1}/{total}] 文件传出失败: {selectedFileName}";
                                FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 错误: {ex.Message}";
                                FileTransferLogTextBox.ScrollToEnd();
                            });
                        }
                    }

                    SetSystemZoneTransferFileProgress(total, total);
                    SetSystemZoneTransferProgress(
                        failed == 0 && success == total
                            ? 100
                            : success * 100d / total);
                    SetSystemZoneTransferSpeed(0);
                    Dispatcher.Invoke(() =>
                    {
                        FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 批量传出完成：成功 {success}，失败 {failed}（共 {total}）";
                        FileTransferLogTextBox.ScrollToEnd();
                    });
                }
            }
        }
        catch (Exception ex)
        {
            SetSystemZoneTransferFileProgress(0, 0);
            SetSystemZoneTransferProgress(0);
            SetSystemZoneTransferSpeed(0);
            Dispatcher.Invoke(() =>
            {
                FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 传出过程中发生异常: {ex.Message}";
                FileTransferLogTextBox.ScrollToEnd();
            });
         }
        finally
        {
            if (transferLockAcquired)
            {
                _systemZoneTransferLock.Release();
            }

            if (sender is System.Windows.Controls.Button transferOutButton)
            {
                transferOutButton.IsEnabled = true;
            }
        }
    }

         private void FileListTextBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
         {
             if (sender is not System.Windows.Controls.ListBox listBox)
             {
                 e.Handled = true;
                 return;
             }

             System.Windows.Point mousePosition = Mouse.GetPosition(listBox);
             DependencyObject? hit = listBox.InputHitTest(mousePosition) as DependencyObject;
             ListBoxItem? itemContainer = hit as ListBoxItem;
             itemContainer ??= hit == null ? null : FindParent<ListBoxItem>(hit);

             bool isOverScrollBar = hit != null
                 && (hit is System.Windows.Controls.Primitives.ScrollBar
                     || FindParent<System.Windows.Controls.Primitives.ScrollBar>(hit) != null);
             if (isOverScrollBar)
             {
                 e.Handled = true;
                 return;
             }

             FileItem? contextItem = itemContainer?.DataContext as FileItem;
             bool hasItemActions = contextItem != null && !contextItem.IsPlaceholder;
             _systemZoneContextMenuItem = hasItemActions ? contextItem : null;

             SystemZoneNewFolderMenuItem.Visibility = hasItemActions ? Visibility.Collapsed : Visibility.Visible;
             SystemZoneNewFileMenuItem.Visibility = hasItemActions ? Visibility.Collapsed : Visibility.Visible;
             SystemZoneRenameMenuItem.Visibility = hasItemActions ? Visibility.Visible : Visibility.Collapsed;
             SystemZoneDeleteMenuItem.Visibility = hasItemActions && !contextItem!.IsFile
                 ? Visibility.Visible
                 : Visibility.Collapsed;

             if (!hasItemActions)
             {
                 listBox.UnselectAll();
             }
         }

         private async void CreateSystemZoneFolderMenuItem_Click(object sender, RoutedEventArgs e)
         {
             await CreateSystemZoneEntryAsync(isDirectory: true);
         }

         private async void CreateSystemZoneFileMenuItem_Click(object sender, RoutedEventArgs e)
         {
             await CreateSystemZoneEntryAsync(isDirectory: false);
         }

         private async void RenameSystemZoneItemMenuItem_Click(object sender, RoutedEventArgs e)
         {
             FileItem? item = _systemZoneContextMenuItem;
             if (item == null || item.IsPlaceholder)
             {
                 return;
             }

             string? newName;
             while (true)
             {
                 newName = ShowSystemZoneNameDialog(
                     isDirectory: !item.IsFile,
                     initialName: item.Name,
                     isRename: true);
                 if (newName == null)
                 {
                     return;
                 }

                 newName = newName.Trim();
                 string? validationError = ValidateSystemZoneEntryName(newName);
                 if (validationError == null)
                 {
                     break;
                 }

                 System.Windows.MessageBox.Show(
                     validationError,
                     "重命名",
                     MessageBoxButton.OK,
                     MessageBoxImage.Warning);
             }

             if (string.Equals(newName, item.Name, StringComparison.Ordinal))
             {
                 return;
             }

             bool transferLockAcquired = _systemZoneTransferLock.Wait(0);
             if (!transferLockAcquired)
             {
                 FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 已有文件传输任务正在进行，暂时无法重命名";
                 FileTransferLogTextBox.ScrollToEnd();
                 return;
             }

             try
             {
                 string oldRemotePath = CombineAndroidPath(currentPath, item.Name);
                 string newRemotePath = CombineAndroidPath(currentPath, newName);
                 string quotedOldPath = QuoteAndroidShellArgument(oldRemotePath);
                 string quotedNewPath = QuoteAndroidShellArgument(newRemotePath);
                 const string existsMarker = "__VIOLET_ENTRY_EXISTS__";
                 string command =
                     $"if [ -e {quotedNewPath} ]; then echo {existsMarker}; else mv {quotedOldPath} {quotedNewPath}; fi";
                 var result = await RunSystemZoneFileCommandAsync(command);

                 if (result.StdOut.Contains(existsMarker, StringComparison.Ordinal))
                 {
                     System.Windows.MessageBox.Show(
                         $"当前目录已经存在名为“{newName}”的项目。",
                         "重命名",
                         MessageBoxButton.OK,
                         MessageBoxImage.Information);
                     return;
                 }

                 if (result.ExitCode != 0 || !string.IsNullOrWhiteSpace(result.StdErr))
                 {
                     string errorMessage = string.IsNullOrWhiteSpace(result.StdErr)
                         ? $"ADB 退出码 {result.ExitCode}"
                         : result.StdErr.Trim();
                     throw new InvalidOperationException(errorMessage);
                 }

                 FileTransferLogTextBox.Text +=
                     $"\n[{DateTime.Now:HH:mm:ss}] 重命名成功: {item.Name} → {newName}";
                 FileTransferLogTextBox.ScrollToEnd();
                 _systemZoneContextMenuItem = null;
                 await LoadFileListFromPath(currentPath);
             }
             catch (Exception ex)
             {
                 FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 重命名失败: {ex.Message}";
                 FileTransferLogTextBox.ScrollToEnd();
                 System.Windows.MessageBox.Show(ex.Message, "重命名失败", MessageBoxButton.OK, MessageBoxImage.Error);
             }
             finally
             {
                 _systemZoneTransferLock.Release();
             }
         }

         private async void DeleteSystemZoneItemMenuItem_Click(object sender, RoutedEventArgs e)
         {
             FileItem? item = _systemZoneContextMenuItem;
             if (item == null || item.IsPlaceholder)
             {
                 return;
             }

             string itemType = item.IsFile ? "文件" : "文件夹";
             MessageBoxResult confirmation = System.Windows.MessageBox.Show(
                 $"确定要删除{itemType}“{item.Name}”吗？{(item.IsFile ? string.Empty : "\n文件夹内的全部内容也会被删除。")}",
                 $"删除{itemType}",
                 MessageBoxButton.YesNo,
                 MessageBoxImage.Warning);
             if (confirmation != MessageBoxResult.Yes)
             {
                 return;
             }

             bool transferLockAcquired = _systemZoneTransferLock.Wait(0);
             if (!transferLockAcquired)
             {
                 FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 已有文件传输任务正在进行，暂时无法删除";
                 FileTransferLogTextBox.ScrollToEnd();
                 return;
             }

             try
             {
                 string remotePath = CombineAndroidPath(currentPath, item.Name);
                 string quotedRemotePath = QuoteAndroidShellArgument(remotePath);
                 string command = item.IsFile
                     ? $"rm -f -- {quotedRemotePath}"
                     : $"rm -rf -- {quotedRemotePath}";
                 var result = await RunSystemZoneFileCommandAsync(command);
                 if (result.ExitCode != 0 || !string.IsNullOrWhiteSpace(result.StdErr))
                 {
                     string errorMessage = string.IsNullOrWhiteSpace(result.StdErr)
                         ? $"ADB 退出码 {result.ExitCode}"
                         : result.StdErr.Trim();
                     throw new InvalidOperationException(errorMessage);
                 }

                 FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 已删除{itemType}: {remotePath}";
                 FileTransferLogTextBox.ScrollToEnd();
                 _systemZoneContextMenuItem = null;
                 await LoadFileListFromPath(currentPath);
             }
             catch (Exception ex)
             {
                 FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 删除失败: {ex.Message}";
                 FileTransferLogTextBox.ScrollToEnd();
                 System.Windows.MessageBox.Show(ex.Message, "删除失败", MessageBoxButton.OK, MessageBoxImage.Error);
             }
             finally
             {
                 _systemZoneTransferLock.Release();
             }
         }

         private async Task CreateSystemZoneEntryAsync(bool isDirectory)
         {
             string? entryName;
             while (true)
             {
                 entryName = ShowSystemZoneNameDialog(isDirectory);
                 if (entryName == null)
                 {
                     return;
                 }

                 entryName = entryName.Trim();
                 string? validationError = ValidateSystemZoneEntryName(entryName);
                 if (validationError == null)
                 {
                     break;
                 }

                 System.Windows.MessageBox.Show(
                     validationError,
                     isDirectory ? "新建文件夹" : "新建文件",
                     MessageBoxButton.OK,
                     MessageBoxImage.Warning);
             }

             bool transferLockAcquired = _systemZoneTransferLock.Wait(0);
             if (!transferLockAcquired)
             {
                 FileTransferLogTextBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 已有文件传输任务正在进行，暂时无法新建项目";
                 FileTransferLogTextBox.ScrollToEnd();
                 return;
             }

             try
             {
                 string remotePath = CombineAndroidPath(currentPath, entryName);
                 string quotedRemotePath = QuoteAndroidShellArgument(remotePath);
                 const string existsMarker = "__VIOLET_ENTRY_EXISTS__";
                 string createCommand = isDirectory
                     ? $"mkdir {quotedRemotePath}"
                     : $"touch {quotedRemotePath}";
                 string command =
                     $"if [ -e {quotedRemotePath} ]; then echo {existsMarker}; else {createCommand}; fi";
                 var result = await RunSystemZoneFileCommandAsync(command);

                 if (result.StdOut.Contains(existsMarker, StringComparison.Ordinal))
                 {
                     System.Windows.MessageBox.Show(
                         $"当前目录已经存在名为“{entryName}”的项目。",
                         isDirectory ? "新建文件夹" : "新建文件",
                         MessageBoxButton.OK,
                         MessageBoxImage.Information);
                     return;
                 }

                 if (result.ExitCode != 0 || !string.IsNullOrWhiteSpace(result.StdErr))
                 {
                     string errorMessage = string.IsNullOrWhiteSpace(result.StdErr)
                         ? $"ADB 退出码 {result.ExitCode}"
                         : result.StdErr.Trim();
                     throw new InvalidOperationException(errorMessage);
                 }

                 FileTransferLogTextBox.Text +=
                     $"\n[{DateTime.Now:HH:mm:ss}] 已新建{(isDirectory ? "文件夹" : "文件")}: {remotePath}";
                 FileTransferLogTextBox.ScrollToEnd();
                 await LoadFileListFromPath(currentPath);
             }
             catch (Exception ex)
             {
                 FileTransferLogTextBox.Text +=
                     $"\n[{DateTime.Now:HH:mm:ss}] 新建{(isDirectory ? "文件夹" : "文件")}失败: {ex.Message}";
                 FileTransferLogTextBox.ScrollToEnd();
                 System.Windows.MessageBox.Show(
                     ex.Message,
                     isDirectory ? "新建文件夹失败" : "新建文件失败",
                     MessageBoxButton.OK,
                     MessageBoxImage.Error);
             }
             finally
             {
                 _systemZoneTransferLock.Release();
             }
         }

         private string? ShowSystemZoneNameDialog(
             bool isDirectory,
             string? initialName = null,
             bool isRename = false)
         {
             string itemType = isDirectory ? "文件夹" : "文件";
             var dialog = new Window
             {
                 Title = isRename ? $"重命名{itemType}" : $"新建{itemType}",
                 Owner = this,
                 Width = 420,
                 Height = 190,
                 ResizeMode = ResizeMode.NoResize,
                 WindowStartupLocation = WindowStartupLocation.CenterOwner,
                 ShowInTaskbar = false,
                 Background = MediaBrushes.White
             };

             var layout = new Grid { Margin = new Thickness(22, 18, 22, 18) };
             layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
             layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
             layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

             var prompt = new TextBlock
             {
                 Text = isRename ? $"请输入新的{itemType}名称：" : $"请输入{itemType}名称：",
                 FontSize = 14,
                 Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(65, 65, 65)),
                 Margin = new Thickness(0, 0, 0, 10)
             };
             Grid.SetRow(prompt, 0);
             layout.Children.Add(prompt);

             var nameTextBox = new System.Windows.Controls.TextBox
             {
                 Text = initialName ?? (isDirectory ? "新建文件夹" : "新建文件.txt"),
                 Height = 34,
                 FontSize = 14,
                 Padding = new Thickness(8, 5, 8, 5),
                 BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(154, 122, 243)),
                 BorderThickness = new Thickness(1)
             };
             Grid.SetRow(nameTextBox, 1);
             layout.Children.Add(nameTextBox);

             var buttons = new StackPanel
             {
                 Orientation = System.Windows.Controls.Orientation.Horizontal,
                 HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                 VerticalAlignment = VerticalAlignment.Bottom,
                 Margin = new Thickness(0, 18, 0, 0)
             };
             var cancelButton = new System.Windows.Controls.Button
             {
                 Content = "取消",
                 Width = 82,
                 Height = 30,
                 Margin = new Thickness(0, 0, 10, 0)
             };
             var createButton = new System.Windows.Controls.Button
             {
                 Content = isRename ? "确定" : "创建",
                 Width = 82,
                 Height = 30,
                 Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(154, 122, 243)),
                 Foreground = MediaBrushes.White,
                 IsDefault = true
             };
             cancelButton.Click += (_, _) => dialog.DialogResult = false;
             createButton.Click += (_, _) => dialog.DialogResult = true;
             buttons.Children.Add(cancelButton);
             buttons.Children.Add(createButton);
             Grid.SetRow(buttons, 2);
             layout.Children.Add(buttons);

             dialog.Content = layout;
             dialog.ContentRendered += (_, _) =>
             {
                 nameTextBox.Focus();
                 nameTextBox.SelectAll();
             };

             return dialog.ShowDialog() == true ? nameTextBox.Text : null;
         }

         private static string? ValidateSystemZoneEntryName(string entryName)
         {
             if (string.IsNullOrWhiteSpace(entryName))
             {
                 return "名称不能为空。";
             }

             if (entryName is "." or "..")
             {
                 return "不能使用“.”或“..”作为名称。";
             }

             if (entryName.IndexOfAny(new[] { '/', '\\', '\r', '\n', '\0' }) >= 0)
             {
                 return "名称不能包含斜杠、反斜杠或换行符。";
             }

             if (Encoding.UTF8.GetByteCount(entryName) > 255)
             {
                 return "名称过长，UTF-8 编码后不能超过 255 字节。";
             }

             return null;
         }

         // FileListTextBox选择变化事件处理程序
         private void FileListTextBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
         {
             var selectedFileItem = FileListTextBox.SelectedItems
                 .OfType<FileItem>()
                 .LastOrDefault(item => item.IsFile);

             lastSelectedFileName = selectedFileItem?.Name ?? "";
         }

         private void FileListTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
         {
             if (e.OriginalSource is not DependencyObject source)
             {
                 return;
             }

             ListBoxItem itemContainer = source as ListBoxItem ?? FindParent<ListBoxItem>(source);
             if (itemContainer?.DataContext is not FileItem fileItem)
             {
                 return;
             }

             if (!fileItem.IsFile)
             {
                 itemContainer.IsSelected = false;
                 if (e.ClickCount == 2)
                 {
                     _ = OpenSystemZoneFolderAsync(fileItem);
                 }
                 e.Handled = true;
                 return;
             }

             bool nextSelectedState = !itemContainer.IsSelected;
             itemContainer.IsSelected = nextSelectedState;
             itemContainer.Focus();
             e.Handled = true;
         }

         private async Task OpenSystemZoneFolderAsync(FileItem selectedFileItem)
         {
             string folderName = selectedFileItem.Name;

             string newPath;
             if (currentPath == "/sdcard/")
             {
                 newPath = $"/sdcard/{folderName}/";
             }
             else
             {
                 newPath = currentPath.TrimEnd('/') + $"/{folderName}/";
             }

             await LoadFileListFromPath(newPath);

             currentPath = newPath;
             lastLoadedDirectory = newPath;
             UpdateSystemZoneCurrentPathDisplay();
         }

         private void UpdateSystemZoneCurrentPathDisplay()
         {
             if (SystemZoneCurrentPathTextBlock != null)
             {
                 SystemZoneCurrentPathTextBlock.Text = currentPath;
             }
         }

         private async Task LoadFileListFromPath(string path)
         {
             CancelSystemZoneImagePreviewLoading();
             SetSystemZoneDirectoryLoading(true);
             try
             {
                 // 查找ListBox控件
                 var listBox = this.FindName("FileListTextBox") as System.Windows.Controls.ListBox;
                 if (listBox == null)
                 {
                     listBox = FindVisualChild<System.Windows.Controls.ListBox>(this);
                 }
 
                 if (listBox != null)
                 {
                     listBox.Items.Clear();
                 }

                 await Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
                 List<FileItem>? detailedItems = await TryLoadSystemZoneFileDetailsAsync(path);
                 if (detailedItems != null)
                 {
                     if (listBox != null)
                     {
                         listBox.Items.Clear();
                         if (detailedItems.Count == 0)
                         {
                             listBox.Items.Add(new FileItem { Name = "未找到文件或目录为空", IsFile = false, IsPlaceholder = true });
                         }
                         else
                         {
                             foreach (FileItem item in detailedItems
                                 .OrderBy(item => item.IsFile)
                                 .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
                             {
                                 listBox.Items.Add(item);
                             }
                         }
                     }

                     lastLoadedDirectory = path;
                     currentPath = path;
                     UpdateSystemZoneCurrentPathDisplay();

                     if (string.Equals(path, "/sdcard/DCIM/Camera/", StringComparison.Ordinal))
                     {
                         StartSystemZoneImagePreviewLoading(path, detailedItems);
                     }

                     return;
                 }
 
                string adbPath = GetToolPath("adb.exe");
 
                 // 创建进程启动信息
                 ProcessStartInfo startInfo = new ProcessStartInfo
                 {
                     FileName = adbPath,
                    Arguments = BuildAdbArguments($"shell ls \"{path}\""),
                     UseShellExecute = false,
                     RedirectStandardOutput = true,
                     RedirectStandardError = true,
                     CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                 };
 
                 // 启动进程
                 using (Process process = new Process())
                 {
                     process.StartInfo = startInfo;
                     process.Start();
 
                     // 读取输出
                     string output = await process.StandardOutput.ReadToEndAsync();
                     string error = await process.StandardError.ReadToEndAsync();
 
                     await process.WaitForExitAsync();
 
                     // 更新ListBox内容
                     if (listBox != null)
                     {
                         listBox.Items.Clear();
                         
                         if (!string.IsNullOrEmpty(error))
                         {
                             listBox.Items.Add(new FileItem { Name = $"错误: {error}", IsFile = false, IsPlaceholder = true });
                         }
                         else if (!string.IsNullOrEmpty(output))
                         {
                             // 将输出按行分割，每个文件名作为一个项目添加到ListBox
                             string[] files = output.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                             var fileItems = new List<FileItem>();
                             
                             foreach (string file in files)
                             {
                                 if (!string.IsNullOrWhiteSpace(file))
                                 {
                                     string fileName = file.Trim();
                                     // 判断是否为文件（包含.符号）
                                     bool isFile = fileName.Contains(".");
                                     string ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
                                     bool isArchive = ext == ".7z" || ext == ".zip" || ext == ".rar" || ext == ".tgz";
                                     bool isImageFile = ext == ".img";
                                     fileItems.Add(new FileItem { Name = fileName, IsFile = isFile, IsArchive = isArchive, IsImageFile = isImageFile });
                                 }
                             }
                             
                             // 排序：文件夹优先（IsFile = false），然后是文件（IsFile = true）
                             var sortedItems = fileItems.OrderBy(item => item.IsFile).ThenBy(item => item.Name);
                             
                             // 添加到 ListBox
                             foreach (var item in sortedItems)
                             {
                                 listBox.Items.Add(item);
                             }
                             
                             // 更新文件传输路径和当前路径
                             lastLoadedDirectory = path;
                             currentPath = path;
                             UpdateSystemZoneCurrentPathDisplay();
                         }
                         else
                         {
                             listBox.Items.Add(new FileItem { Name = "未找到文件或目录为空", IsFile = false, IsPlaceholder = true });
                         }
                     }
                 }
             }
             catch (Exception ex)
             {
                 // 查找ListBox并显示错误信息
                 var listBox = this.FindName("FileListTextBox") as System.Windows.Controls.ListBox;
                 if (listBox == null)
                 {
                     listBox = FindVisualChild<System.Windows.Controls.ListBox>(this);
                 }
 
                 if (listBox != null)
                 {
                     listBox.Items.Clear();
                     listBox.Items.Add(new FileItem { Name = $"执行命令时发生错误: {ex.Message}", IsFile = false, IsPlaceholder = true });
                 }
             }
             finally
             {
                 SetSystemZoneDirectoryLoading(false);
             }
         }

         private void StartSystemZoneImagePreviewLoading(string directoryPath, IEnumerable<FileItem> items)
         {
             var cancellation = new CancellationTokenSource();
             _systemZoneImagePreviewCancellation = cancellation;
             _ = LoadSystemZoneImagePreviewsAsync(directoryPath, items, cancellation.Token);
         }

         private void CancelSystemZoneImagePreviewLoading()
         {
             if (_systemZoneImagePreviewCancellation == null)
             {
                 return;
             }

            _systemZoneImagePreviewCancellation.Cancel();
            _systemZoneImagePreviewCancellation = null;
         }

         private async Task LoadSystemZoneImagePreviewsAsync(
             string directoryPath,
             IEnumerable<FileItem> items,
             CancellationToken cancellationToken)
         {
             using var concurrency = new SemaphoreSlim(4, 4);
             try
             {
                 Task[] previewTasks = items
                     .Where(item => item.IsFile && item.IsPreviewableImage)
                     .Select(async item =>
                     {
                         bool enteredConcurrency = false;
                         await concurrency.WaitAsync(cancellationToken);
                         enteredConcurrency = true;
                         try
                         {
                             var preview = await LoadSystemZoneImagePreviewAsync(
                                 CombineAndroidPath(directoryPath, item.Name),
                                 cancellationToken);
                             if (preview != null
                                 && !cancellationToken.IsCancellationRequested
                                 && string.Equals(currentPath, directoryPath, StringComparison.Ordinal))
                             {
                                 item.PreviewImage = preview;
                             }
                         }
                         finally
                         {
                             if (enteredConcurrency)
                             {
                                 concurrency.Release();
                             }
                         }
                     })
                     .ToArray();

                 await Task.WhenAll(previewTasks);
             }
             catch (OperationCanceledException)
             {
                 // 切换目录后不再继续读取旧目录的缩略图。
             }
         }

         private async Task<System.Windows.Media.Imaging.BitmapImage?> LoadSystemZoneImagePreviewAsync(
             string remotePath,
             CancellationToken cancellationToken)
         {
             return await Task.Run(async () =>
             {
                 Process? process = null;
                 try
                 {
                     string adbPath = GetToolPath("adb.exe");
                     var startInfo = new ProcessStartInfo
                     {
                         FileName = adbPath,
                         UseShellExecute = false,
                         RedirectStandardOutput = true,
                         RedirectStandardError = true,
                         CreateNoWindow = true
                     };
                     string selectedSerial = GetSelectedDeviceSerial();
                     if (!string.IsNullOrWhiteSpace(selectedSerial))
                     {
                         startInfo.ArgumentList.Add("-s");
                         startInfo.ArgumentList.Add(selectedSerial);
                     }

                     // exec-out 不经过 Android Shell，路径必须作为原始参数传入，不能套 Shell 引号。
                     startInfo.ArgumentList.Add("exec-out");
                     startInfo.ArgumentList.Add("cat");
                     startInfo.ArgumentList.Add("--");
                     startInfo.ArgumentList.Add(remotePath);

                     process = new Process { StartInfo = startInfo };
                     process.Start();
                     await using var stream = new MemoryStream();
                     Task<string> errorTask = process.StandardError.ReadToEndAsync();
                     await process.StandardOutput.BaseStream.CopyToAsync(stream, cancellationToken);
                     await process.WaitForExitAsync(cancellationToken);
                     await errorTask;
                     if (process.ExitCode != 0 || stream.Length == 0)
                     {
                         return null;
                     }

                     stream.Position = 0;
                     var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                     bitmap.BeginInit();
                     bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                     bitmap.DecodePixelWidth = 144;
                     bitmap.StreamSource = stream;
                     bitmap.EndInit();
                     bitmap.Freeze();
                     return bitmap;
                 }
                 catch (OperationCanceledException)
                 {
                     try
                     {
                         if (process != null && !process.HasExited)
                         {
                             process.Kill(entireProcessTree: true);
                         }
                     }
                     catch
                     {
                     }

                     throw;
                 }
                 catch
                 {
                     return null;
                 }
                 finally
                 {
                     process?.Dispose();
                 }
             }, cancellationToken);
         }

         private void SetSystemZoneDirectoryLoading(bool isLoading)
         {
             if (SystemZoneLoadingOverlay != null)
             {
                 SystemZoneLoadingOverlay.Visibility = isLoading
                     ? Visibility.Visible
                     : Visibility.Collapsed;
             }
         }

         private async Task<List<FileItem>?> TryLoadSystemZoneFileDetailsAsync(string path)
         {
             // /sdcard 通常是指向 /storage/emulated/0 的符号链接。
             // 保留末尾斜杠，确保 find 遍历链接目标中的内容，而不是只检查链接本身。
             string normalizedPath = path.EndsWith('/') ? path : path + "/";
             string quotedPath = QuoteAndroidShellArgument(normalizedPath);

             const string statFormat = "%n|%Y|%f";
             string findCommand =
                 $"find {quotedPath} -mindepth 1 -maxdepth 1 -exec stat -c '{statFormat}' {{}} \\;";
             string command = IsSystemZoneRootDirectorySelected()
                 ? $"shell su -c {QuoteAndroidShellArgument(findCommand)}"
                 : $"shell {findCommand}";
             var result = await RunSystemZoneAdbCommandAsync(command);

             if (result.ExitCode != 0 || !string.IsNullOrWhiteSpace(result.StdErr))
             {
                 return null;
             }

             if (string.IsNullOrWhiteSpace(result.StdOut))
             {
                 return new List<FileItem>();
             }

             var items = new List<FileItem>();
             foreach (string rawLine in result.StdOut.Split(
                 new[] { '\r', '\n' },
                 StringSplitOptions.RemoveEmptyEntries))
             {
                 if (!TryParseSystemZoneFileDetail(rawLine, out FileItem item))
                 {
                     return null;
                 }

                 items.Add(item);
             }

             return items;
         }

         private async Task<(int ExitCode, string StdOut, string StdErr)> RunSystemZoneAdbCommandAsync(
             string command)
         {
             string adbPath = GetToolPath("adb.exe");
             var startInfo = new ProcessStartInfo
             {
                 FileName = adbPath,
                 Arguments = BuildAdbArguments(command),
                 UseShellExecute = false,
                 RedirectStandardOutput = true,
                 RedirectStandardError = true,
                 CreateNoWindow = true,
                 StandardOutputEncoding = Encoding.UTF8,
                 StandardErrorEncoding = Encoding.UTF8
             };

             using var process = new Process { StartInfo = startInfo };
             process.Start();
             Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
             Task<string> errorTask = process.StandardError.ReadToEndAsync();
             await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync());
             return (process.ExitCode, await outputTask, await errorTask);
         }

         private static bool TryParseSystemZoneFileDetail(
             string line,
             out FileItem item)
         {
             item = null!;
             int modeSeparator = line.LastIndexOf('|');
             int modifiedSeparator = modeSeparator > 0
                 ? line.LastIndexOf('|', modeSeparator - 1)
                 : -1;
             int pathEnd = modifiedSeparator;

             if (pathEnd <= 0 || modifiedSeparator <= 0 || modeSeparator <= modifiedSeparator)
             {
                 return false;
             }

             string fullPath = line.Substring(0, pathEnd);
             string modifiedText = line.Substring(modifiedSeparator + 1, modeSeparator - modifiedSeparator - 1);
             string modeText = line.Substring(modeSeparator + 1).Trim();

             if (!long.TryParse(modifiedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long modifiedEpoch)
                 || !int.TryParse(modeText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int mode))
             {
                 return false;
             }

             string trimmedPath = fullPath.TrimEnd('/');
             int lastSlash = trimmedPath.LastIndexOf('/');
             string fileName = lastSlash >= 0 ? trimmedPath.Substring(lastSlash + 1) : trimmedPath;
             if (string.IsNullOrWhiteSpace(fileName))
             {
                 return false;
             }

             bool isDirectory = (mode & 0xF000) == 0x4000;
             string extension = Path.GetExtension(fileName).ToLowerInvariant();
             string timeText = FormatSystemZoneFileTime(modifiedEpoch);

             item = new FileItem
             {
                 Name = fileName,
                 TimeText = isDirectory ? string.Empty : timeText,
                 IsFile = !isDirectory,
                 IsArchive = extension == ".7z" || extension == ".zip" || extension == ".rar" || extension == ".tgz",
                 IsImageFile = extension == ".img",
                 IsPreviewableImage = !isDirectory && IsSystemZonePreviewableImageFile(fileName)
             };
             return true;
         }

         private static bool IsSystemZonePreviewableImageFile(string fileName)
         {
             return Path.GetExtension(fileName).ToLowerInvariant() switch
             {
                 ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" => true,
                 _ => false
             };
         }

         private static string FormatSystemZoneFileTime(long unixSeconds)
         {
             if (unixSeconds <= 0)
             {
                 return string.Empty;
             }

             try
             {
                 DateTimeOffset localTime = DateTimeOffset
                     .FromUnixTimeSeconds(unixSeconds)
                     .ToLocalTime();
                 return $"{localTime:yyyy-MM-dd HH:mm}";
             }
             catch (ArgumentOutOfRangeException)
             {
                 return string.Empty;
             }
         }

        private bool IsSystemZoneRootDirectorySelected()
        {
            return SystemZoneRootDirectoryRadioButton?.IsChecked == true;
        }

        private async Task<bool> HasSystemZoneRootAccessAsync()
        {
            var result = await RunSystemZoneAdbCommandAsync("shell su -c id");
            return result.ExitCode == 0
                && result.StdOut.Contains("uid=0(root)", StringComparison.Ordinal);
        }

        private void ShowSystemZoneLoadError(string message)
        {
            if (FindName("FileListTextBox") is System.Windows.Controls.ListBox listBox)
            {
                listBox.Items.Clear();
            }

            string logMessage = $"[{DateTime.Now:HH:mm:ss}] 错误: {message}";
            FileTransferLogTextBox.Text += $"\n{logMessage}";
            FileTransferLogTextBox.ScrollToEnd();
        }

        private async void BackButton_Click(object sender, RoutedEventArgs e)
        {
            string topLevelPath = IsSystemZoneRootDirectorySelected() ? "/" : "/sdcard/";
            if (currentPath != topLevelPath)
            {
                int lastSlash = currentPath.TrimEnd('/').LastIndexOf('/');
                string parentPath = currentPath.Substring(0, lastSlash + 1);
                if (string.IsNullOrEmpty(parentPath))
                {
                    parentPath = topLevelPath;
                }
                await LoadFileListFromPath(parentPath);
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadFileListFromPath(currentPath);
        }

        private async void LoadSystemZoneLocationButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsSystemZoneRootDirectorySelected())
            {
                if (!await HasSystemZoneRootAccessAsync())
                {
                    ShowSystemZoneLoadError("Shell无法获取ROOT权限，请确保手机端已授予Shell ROOT权限。");
                    return;
                }

                await LoadFileListFromPath("/");
                return;
            }

            if (SystemZoneCameraDirectoryRadioButton?.IsChecked == true)
            {
                await LoadFileListFromPath("/sdcard/DCIM/Camera/");
                return;
            }

            await LoadFileListFromPath("/sdcard/");
        }

        private async void DeleteSelectedFilesButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedFiles = FileListTextBox.SelectedItems
                .OfType<FileItem>()
                .Where(item => item.IsFile)
                .ToList();

            if (selectedFiles.Count == 0)
            {
                System.Windows.MessageBox.Show(
                    "请先选择要删除的文件。",
                    "删除文件",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (sender is System.Windows.Controls.Button deleteButton)
            {
                deleteButton.IsEnabled = false;
            }

            int successCount = 0;
            int failedCount = 0;
            using var progressCancellation = new CancellationTokenSource();
            Task progressAnimation = AnimateDeleteProgressAsync(
                selectedFiles.Count,
                progressCancellation.Token);

            try
            {
                FileTransferLogTextBox.Text +=
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 开始删除 {selectedFiles.Count} 个文件\n";
                FileTransferLogTextBox.ScrollToEnd();

                foreach (FileItem file in selectedFiles)
                {
                    SetSystemZoneTransferFileProgress(
                        successCount + failedCount + 1,
                        selectedFiles.Count);

                    string remotePath = currentPath.TrimEnd('/') + "/" + file.Name;
                    var commandResult = await RunSystemZoneFileCommandAsync(
                        $"rm -f -- {QuoteAndroidShellArgument(remotePath)}");
                    bool succeeded = commandResult.ExitCode == 0
                        && string.IsNullOrWhiteSpace(commandResult.StdErr);
                    string result = string.IsNullOrWhiteSpace(commandResult.StdErr)
                        ? commandResult.StdOut
                        : commandResult.StdErr;

                    if (succeeded)
                    {
                        successCount++;
                        FileTransferLogTextBox.Text +=
                            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 删除成功: {file.Name}\n";
                    }
                    else
                    {
                        failedCount++;
                        FileTransferLogTextBox.Text +=
                            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 删除失败: {file.Name}，{result.Trim()}\n";
                    }

                    FileTransferLogTextBox.ScrollToEnd();
                }

                progressCancellation.Cancel();
                await progressAnimation;
                if (failedCount == 0)
                {
                    await CompleteDeleteProgressAsync(selectedFiles.Count);
                }
                else
                {
                    SetSystemZoneTransferFileProgress(selectedFiles.Count, selectedFiles.Count);
                    SetSystemZoneTransferProgress(Math.Min(
                        99,
                        successCount * 100d / selectedFiles.Count));
                    UpdateSystemZoneProgressBarTag(speedText: "删除未全部完成");
                }
                await LoadFileListFromPath(currentPath);

                FileTransferLogTextBox.Text +=
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 删除完成：成功 {successCount}，失败 {failedCount}\n";
                FileTransferLogTextBox.ScrollToEnd();

                if (failedCount > 0)
                {
                    System.Windows.MessageBox.Show(
                        $"删除完成：成功 {successCount} 个，失败 {failedCount} 个。\n请查看传输日志了解失败原因。",
                        "删除文件",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                progressCancellation.Cancel();
                await progressAnimation;

                FileTransferLogTextBox.Text +=
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 删除文件时发生错误: {ex.Message}\n";
                FileTransferLogTextBox.ScrollToEnd();

                System.Windows.MessageBox.Show(
                    $"删除文件失败：{ex.Message}",
                    "删除文件",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                if (sender is System.Windows.Controls.Button button)
                {
                    button.IsEnabled = true;
                }
            }
        }

        private async Task AnimateDeleteProgressAsync(int totalFileCount, CancellationToken cancellationToken)
        {
            SetSystemZoneTransferProgress(0);
            SetSystemZoneTransferFileProgress(0, totalFileCount);
            UpdateSystemZoneProgressBarTag(speedText: "正在删除...");

            double progress = 0;
            try
            {
                while (progress < 90)
                {
                    await Task.Delay(35, cancellationToken);
                    progress += Math.Max(0.6, (90 - progress) * 0.055);
                    SetSystemZoneTransferProgress(Math.Min(progress, 90));
                    UpdateSystemZoneProgressBarTag(speedText: "正在删除...");
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task CompleteDeleteProgressAsync(int totalFileCount)
        {
            double start = Math.Max(90, SystemZoneProgressBar?.Value ?? 90);
            for (double progress = start; progress < 100; progress += 2)
            {
                SetSystemZoneTransferProgress(progress);
                UpdateSystemZoneProgressBarTag(speedText: "正在完成...");
                await Task.Delay(20);
            }

            SetSystemZoneTransferProgress(100);
            SetSystemZoneTransferFileProgress(totalFileCount, totalFileCount);
            UpdateSystemZoneProgressBarTag(speedText: "删除完成");
        }

        private static string QuoteAndroidShellArgument(string value)
        {
            return "'" + value.Replace("'", "'\\''") + "'";
        }

        private async void InstallApkButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button installButton)
            {
                installButton.IsEnabled = false;
            }

            try
            {
                Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
                openFileDialog.Filter = "APK 安装包 (*.apk;*.apk.*)|*.apk;*.apk.*|所有文件 (*.*)|*.*";
                openFileDialog.Multiselect = true; // 启用多文件选择

                if (openFileDialog.ShowDialog() == true)
                {
                    string[] apkPaths = openFileDialog.FileNames
                        .Where(IsRecognizedApkPackagePath)
                        .ToArray();
                    int ignoredFileCount = openFileDialog.FileNames.Length - apkPaths.Length;

                    if (apkPaths.Length == 0)
                    {
                        System.Windows.MessageBox.Show(
                            "请选择 .apk 或 .apk.数字 格式的安装包。",
                            "安装 APK",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    if (ignoredFileCount > 0)
                    {
                        FileTransferLogTextBox.Text +=
                            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 已忽略 {ignoredFileCount} 个无法识别的文件\n";
                    }

                    string adbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools", "adb.exe");
                    string selectedSerial = GetSelectedDeviceSerial();

                    if (!File.Exists(adbPath))
                    {
                        FileTransferLogTextBox.Text += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 错误: 找不到ADB工具: {adbPath}\n请确保platform-tools文件夹存在于程序目录中。\n";
                        FileTransferLogTextBox.ScrollToEnd();
                        return;
                    }

                    // 检查ROOT权限
                    FileTransferLogTextBox.Text += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 检查ROOT权限...\n";
                    FileTransferLogTextBox.ScrollToEnd();

                    string rootCheckResult = await ExecuteAdbCommandWithOutput(
                        "shell \"su -c 'echo root_check'\"");
                    bool hasRoot = rootCheckResult.Contains("root_check");

                    if (hasRoot)
                    {
                        FileTransferLogTextBox.Text += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ROOT权限已授予，将使用静默安装\n";
                    }
                    else
                    {
                        FileTransferLogTextBox.Text += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 未获取ROOT权限，将使用普通安装\n";
                    }
                    FileTransferLogTextBox.ScrollToEnd();

                    FileTransferLogTextBox.Text += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 开始批量安装APK，共选择了 {apkPaths.Length} 个文件\n";
                    FileTransferLogTextBox.ScrollToEnd();

                    // 异步排队安装每个APK文件
                    await InstallApksSequentially(apkPaths, adbPath, selectedSerial, hasRoot);
                }
            }
            finally
            {
                if (sender is System.Windows.Controls.Button button)
                {
                    button.IsEnabled = true;
                }
            }
        }

        private static bool IsRecognizedApkPackagePath(string path)
        {
            string fileName = Path.GetFileName(path);
            return Regex.IsMatch(
                fileName,
                @"\.apk(?:\.\d+)?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        
        private async Task InstallApksSequentially(string[] apkPaths, string adbPath, string selectedSerial, bool hasRoot)
        {
            int totalFiles = apkPaths.Length;
            int currentIndex = 0;
            int successCount = 0;
            int failedCount = 0;

            SetSystemZoneTransferFileProgress(0, totalFiles);
            SetSystemZoneTransferProgress(0);
            UpdateSystemZoneProgressBarTag(speedText: "准备安装...");

            foreach (string apkPath in apkPaths)
            {
                currentIndex++;
                string fileName = Path.GetFileName(apkPath);
                bool installedSuccessfully = false;
                using var progressCancellation = new CancellationTokenSource();
                Task progressAnimation = AnimateApkInstallProgressAsync(
                    currentIndex,
                    totalFiles,
                    progressCancellation.Token);
                
                FileTransferLogTextBox.Text += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 正在安装 ({currentIndex}/{totalFiles}): {fileName}\n";
                FileTransferLogTextBox.ScrollToEnd();
                
                try
                {
                    if (hasRoot)
                    {
                        // 使用ROOT静默安装
                        string tempPath = $"/data/local/tmp/violet_install_{currentIndex}.apk";
                        
                        // 推送APK到设备
                        string pushResult = await ExecuteAdbCommandWithOutput(
                            $"push \"{apkPath}\" \"{tempPath}\"");
                        if (pushResult.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                            pushResult.Contains("failed", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException($"APK 推送失败: {pushResult.Trim()}");
                        }

                        string installResult;
                        try
                        {
                            // 临时文件名只包含 ASCII 字母、数字和下划线，避免 shell 特殊字符。
                            installResult = await ExecuteAdbCommandWithOutput(
                                $"shell \"su -c 'pm install -r -g {tempPath}'\"");
                        }
                        finally
                        {
                            await ExecuteAdbCommand(
                                $"shell \"su -c 'rm -f {tempPath}'\"");
                        }
                        
                        // 判断安装结果
                        installedSuccessfully = installResult.Contains(
                            "Success",
                            StringComparison.OrdinalIgnoreCase);
                        string status = installedSuccessfully ? "成功" : "失败";
                        
                        FileTransferLogTextBox.Text += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {fileName} 安装{status}\n";
                        if (!installedSuccessfully)
                        {
                            FileTransferLogTextBox.Text += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 详情: {installResult.Trim()}\n";
                        }
                    }
                    else
                    {
                        // 使用普通安装
                        string arguments = $"install \"{apkPath}\"";
                        if (!string.IsNullOrEmpty(selectedSerial))
                        {
                            arguments = $"-s {selectedSerial} install \"{apkPath}\"";
                        }
                        
                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = adbPath,
                                Arguments = arguments,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                            }
                        };
                        
                        process.Start();
                        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                        Task<string> errorTask = process.StandardError.ReadToEndAsync();
                        await process.WaitForExitAsync();

                        string output = await outputTask;
                        string error = await errorTask;
                        
                        string result = string.IsNullOrEmpty(error) ? output : $"{output}\n错误信息: {error}";
                        
                        installedSuccessfully =
                            process.ExitCode == 0 &&
                            output.Contains("Success", StringComparison.OrdinalIgnoreCase);
                        string status = installedSuccessfully ? "成功" : "失败";
                        
                        FileTransferLogTextBox.Text += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {fileName} 安装{status}: {result.Trim()}\n";
                    }

                    if (installedSuccessfully)
                    {
                        successCount++;
                    }
                    else
                    {
                        failedCount++;
                    }
                    
                    FileTransferLogTextBox.ScrollToEnd();
                }
                catch (Exception ex)
                {
                    failedCount++;
                    FileTransferLogTextBox.Text += $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 安装 {fileName} 时发生错误: {ex.Message}\n";
                    FileTransferLogTextBox.ScrollToEnd();
                }
                finally
                {
                    progressCancellation.Cancel();
                    await progressAnimation;
                    await CompleteApkInstallFileProgressAsync(currentIndex, totalFiles);
                }
                
                // 在安装下一个APK之前稍作延迟
                if (currentIndex < totalFiles)
                {
                    await Task.Delay(500);
                }
            }
            
            SetSystemZoneTransferProgress(100);
            SetSystemZoneTransferFileProgress(totalFiles, totalFiles);
            UpdateSystemZoneProgressBarTag(speedText: "安装完成");
            FileTransferLogTextBox.Text +=
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 批量APK安装完成：成功 {successCount}，失败 {failedCount}（共 {totalFiles}）\n";
            FileTransferLogTextBox.ScrollToEnd();
        }

        private async Task AnimateApkInstallProgressAsync(
            int currentFileIndex,
            int totalFileCount,
            CancellationToken cancellationToken)
        {
            double start = (currentFileIndex - 1) * 100d / totalFileCount;
            double target = currentFileIndex * 100d / totalFileCount;
            double simulatedTarget = Math.Max(start, target - Math.Min(2, 100d / totalFileCount * 0.15));
            double progress = start;

            SetSystemZoneTransferFileProgress(currentFileIndex, totalFileCount);
            SetSystemZoneTransferProgress(start);
            UpdateSystemZoneProgressBarTag(speedText: "正在安装...");

            try
            {
                while (progress < simulatedTarget)
                {
                    await Task.Delay(40, cancellationToken);
                    progress += Math.Max(0.25, (simulatedTarget - progress) * 0.055);
                    SetSystemZoneTransferProgress(Math.Min(progress, simulatedTarget));
                    UpdateSystemZoneProgressBarTag(speedText: "正在安装...");
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task CompleteApkInstallFileProgressAsync(
            int currentFileIndex,
            int totalFileCount)
        {
            double target = currentFileIndex * 100d / totalFileCount;
            double current = SystemZoneProgressBar?.Value ?? target;
            double step = Math.Max(0.5, (target - current) / 5);

            while (current < target)
            {
                current = Math.Min(target, current + step);
                SetSystemZoneTransferProgress(current);
                UpdateSystemZoneProgressBarTag(speedText: "正在完成...");
                await Task.Delay(20);
            }

            SetSystemZoneTransferFileProgress(currentFileIndex, totalFileCount);
        }

         private async void FileListTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
         {
             if (e.OriginalSource is not DependencyObject source)
             {
                 return;
             }

             ListBoxItem itemContainer = source as ListBoxItem ?? FindParent<ListBoxItem>(source);
             if (itemContainer?.DataContext is FileItem selectedFileItem)
             {
                 // 只处理文件夹的双击事件
                 if (!selectedFileItem.IsFile)
                 {
                     await OpenSystemZoneFolderAsync(selectedFileItem);
                 }
             }
         }

        private void SelectOfpFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择 OPPO/Realme OFP 固件",
                Filter = "OFP 固件 (*.ofp)|*.ofp|所有文件 (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                OfpFilePathTextBox.Text = dialog.FileName;
                OfpExtractProgressBar.Value = 0;
                AppendSuperLog($"[OFP] 已选择固件: {dialog.FileName}");
            }
        }

        private async void StartOfpExtractButton_Click(object sender, RoutedEventArgs e)
        {
            string sourcePath = OfpFilePathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                System.Windows.MessageBox.Show(
                    "请先选择有效的 OFP 固件文件。",
                    "解包 OFP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string defaultOutputDirectory = Path.Combine(
                Path.GetDirectoryName(sourcePath) ?? AppDomain.CurrentDomain.BaseDirectory,
                Path.GetFileNameWithoutExtension(sourcePath) + "_解包");

            using var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择 OFP 解包输出目录",
                SelectedPath = Directory.Exists(defaultOutputDirectory)
                    ? defaultOutputDirectory
                    : Path.GetDirectoryName(sourcePath)
            };

            if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            {
                return;
            }

            string outputDirectory = Path.Combine(
                folderDialog.SelectedPath,
                Path.GetFileNameWithoutExtension(sourcePath) + "_解包");

            StartOfpExtractButton.IsEnabled = false;
            SelectOfpFileButton.IsEnabled = false;
            OfpExtractProgressBar.Value = 0;
            AppendSuperLog("=== 开始解包 OFP ===");
            AppendSuperLog($"[OFP] 源文件: {sourcePath}");
            AppendSuperLog($"[OFP] 输出目录: {outputDirectory}");
            AppendSuperLog("[OFP] 正在读取固件...");

            var progress = new Progress<OfpExtractProgress>(value =>
            {
                OfpExtractProgressBar.Value = Math.Clamp(value.Percent, 0, 100);
                AppendSuperLog($"[OFP] {value.Message} ({value.Percent:0}%)");
            });

            try
            {
                await OppoOfpExtractor.ExtractAsync(sourcePath, outputDirectory, progress);
                int superFileCount = Directory
                    .EnumerateFiles(outputDirectory, "super*", SearchOption.AllDirectories)
                    .Count();
                if (superFileCount > 1)
                {
                    try
                    {
                        SetSelectedOfpSuperSegments(
                            OfpSegmentedSuperMerger.FindSegments(outputDirectory));
                    }
                    catch
                    {
                        _selectedOfpSuperSegments = Array.Empty<string>();
                        OfpSuperSegmentsTextBox.Text = "请选择 Super 分段文件";
                    }
                }
                OfpExtractProgressBar.Value = 100;
                AppendSuperLog(superFileCount > 0
                    ? $"COLOR:Green|[OFP] 解包完成，已提取 {superFileCount} 个 super 文件"
                    : "COLOR:Orange|[OFP] 解包完成，未发现 super 文件");
                AppendSuperLog("=== OFP 解包任务结束 ===");

                System.Windows.MessageBox.Show(
                    superFileCount > 0
                        ? $"OFP 解包完成，共提取 {superFileCount} 个 super 文件。\n\n输出目录：{outputDirectory}"
                        : $"OFP 解包完成，但文件表中未发现 super 文件。\n请检查 ProFile.xml 和 super_map.csv.txt。\n\n输出目录：{outputDirectory}",
                    "解包 OFP",
                    MessageBoxButton.OK,
                    superFileCount > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);

                Process.Start(new ProcessStartInfo
                {
                    FileName = outputDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppendSuperLog($"COLOR:Red|[OFP] 解包失败: {ex.Message}");
                System.Windows.MessageBox.Show(
                    $"OFP 解包失败：{ex.Message}",
                    "解包 OFP",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                StartOfpExtractButton.IsEnabled = true;
                SelectOfpFileButton.IsEnabled = true;
            }
        }

        private string[] _selectedOfpSuperSegments = Array.Empty<string>();

        private void SetSelectedOfpSuperSegments(IReadOnlyList<string> segments)
        {
            _selectedOfpSuperSegments = segments.ToArray();
            string fileNames = string.Join(
                "、",
                _selectedOfpSuperSegments.Select(Path.GetFileName));
            OfpSuperSegmentsTextBox.Text =
                $"已选择 {_selectedOfpSuperSegments.Length} 个：{fileNames}";
        }

        private void SelectOfpSuperPartsFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择 Super 分段文件",
                Filter = "Super 分段镜像 (super*.img;super*.bin;super*.raw;super*.sparse)|super*.img;super*.bin;super*.raw;super*.sparse|镜像文件 (*.img;*.bin;*.raw;*.sparse)|*.img;*.bin;*.raw;*.sparse|所有文件 (*.*)|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                IReadOnlyList<string> segments;
                IReadOnlyList<OfpSuperVariant> variants =
                    OfpSegmentedSuperMerger.FindMappedVariants(dialog.FileNames);
                if (variants.Count > 0)
                {
                    OfpSuperVariant? variant = variants.Count == 1
                        ? variants[0]
                        : ShowOfpSuperVariantDialog(variants);
                    if (variant == null)
                        return;

                    segments = variant.SegmentPaths;
                    AppendSuperLog(
                        $"COLOR:Purple|[OFP Super] 已选择版本：{variant.DisplayName}");
                }
                else
                {
                    segments =
                        OfpSegmentedSuperMerger.ValidateAndSortSegments(dialog.FileNames);
                }

                SetSelectedOfpSuperSegments(segments);
                AppendSuperLog($"[OFP Super] 已识别 {segments.Count} 个分段");
            }
            catch (Exception ex)
            {
                _selectedOfpSuperSegments = Array.Empty<string>();
                OfpSuperSegmentsTextBox.Text = "请选择 Super 分段文件";
                AppendSuperLog($"COLOR:Orange|[OFP Super] {ex.Message}");
            }
        }

        private OfpSuperVariant? ShowOfpSuperVariantDialog(
            IReadOnlyList<OfpSuperVariant> variants)
        {
            var window = new System.Windows.Window
            {
                Title = "选择 Super 版本",
                Owner = this,
                Width = 390,
                Height = 190,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Background = System.Windows.Media.Brushes.White
            };
            var root = new System.Windows.Controls.Grid
            {
                Margin = new Thickness(18)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var message = new TextBlock
            {
                Text = "检测到多个运营商版本，请选择需要合并的 Super：",
                Foreground = new SolidColorBrush(MediaColor.FromRgb(51, 51, 51)),
                TextWrapping = TextWrapping.Wrap
            };
            System.Windows.Controls.Grid.SetRow(message, 0);

            var selector = new System.Windows.Controls.ComboBox
            {
                ItemsSource = variants,
                DisplayMemberPath = nameof(OfpSuperVariant.DisplayName),
                SelectedIndex = 0,
                Height = 32,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            System.Windows.Controls.Grid.SetRow(selector, 2);

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "取消",
                Width = 82,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var confirmButton = new System.Windows.Controls.Button
            {
                Content = "确认",
                Width = 82,
                Height = 30,
                Background = new SolidColorBrush(MediaColor.FromRgb(224, 188, 245)),
                BorderBrush = new SolidColorBrush(MediaColor.FromRgb(224, 188, 245)),
                Foreground = System.Windows.Media.Brushes.White
            };
            cancelButton.Click += (_, _) => window.DialogResult = false;
            confirmButton.Click += (_, _) => window.DialogResult = true;
            buttons.Children.Add(cancelButton);
            buttons.Children.Add(confirmButton);
            System.Windows.Controls.Grid.SetRow(buttons, 4);

            root.Children.Add(message);
            root.Children.Add(selector);
            root.Children.Add(buttons);
            window.Content = root;

            return window.ShowDialog() == true
                ? selector.SelectedItem as OfpSuperVariant
                : null;
        }

        private async void StartMergeOfpSuperButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedOfpSuperSegments.Length < 2)
            {
                AppendSuperLog("COLOR:Red|[OFP Super] 请至少选择两个 Super 分段文件");
                return;
            }

            string outputDirectory =
                Path.GetDirectoryName(_selectedOfpSuperSegments[0])
                ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string outputPath = Path.Combine(outputDirectory, "super_merged.img");

            if (File.Exists(outputPath))
            {
                MessageBoxResult overwrite = System.Windows.MessageBox.Show(
                    $"输出文件已存在，是否覆盖？\n\n{outputPath}",
                    "合并 OFP 分段 Super",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (overwrite != MessageBoxResult.Yes)
                    return;
            }

            string originalButtonText = StartMergeOfpSuperButton.Content?.ToString() ?? "开始合并";
            StartMergeOfpSuperButton.IsEnabled = false;
            SelectOfpSuperSegmentsButton.IsEnabled = false;
            StartMergeOfpSuperButton.Content = "准备中";
            AppendSuperLog("=== 开始合并 OFP 分段 Super ===");
            AppendSuperLog($"[OFP Super] 分段数量: {_selectedOfpSuperSegments.Length}");
            AppendSuperLog($"[OFP Super] 输出文件: {outputPath}");

            double lastDisplayedPercent = -1;
            var progress = new Progress<OfpSuperMergeProgress>(value =>
            {
                double percent = Math.Clamp(value.Percent, 0, 100);
                if (percent >= 100 || percent - lastDisplayedPercent >= 1)
                {
                    StartMergeOfpSuperButton.Content = $"{percent:0}%";
                    lastDisplayedPercent = percent;
                }
                if (!string.IsNullOrWhiteSpace(value.Message))
                    AppendSuperLog($"[OFP Super] {value.Message}");
            });

            try
            {
                OfpSuperMergeResult result = await OfpSegmentedSuperMerger.MergeAsync(
                    _selectedOfpSuperSegments,
                    outputPath,
                    progress);
                string outputSize = FormatFileSize(result.OutputLength);
                AppendSuperLog(
                    $"COLOR:Green|[OFP Super] 合并完成：{result.SegmentCount} 个分段，完整大小 {outputSize}");
                if (result.SourceIsSparse)
                {
                    AppendSuperLog(result.OutputUsesNtfsSparse
                        ? "[OFP Super] 已启用 NTFS 稀疏输出，未写入区域不占用实际磁盘空间"
                        : "COLOR:Orange|[OFP Super] 当前文件系统不支持稀疏输出，文件将占用完整空间");
                }
                AppendSuperLog($"COLOR:Green|[OFP Super] 保存路径: {result.OutputPath}");
                AppendSuperLog("=== OFP 分段 Super 合并任务结束 ===");
            }
            catch (Exception ex)
            {
                AppendSuperLog($"COLOR:Red|[OFP Super] 合并失败: {ex.Message}");
            }
            finally
            {
                StartMergeOfpSuperButton.Content = originalButtonText;
                StartMergeOfpSuperButton.IsEnabled = true;
                SelectOfpSuperSegmentsButton.IsEnabled = true;
            }
        }

        private void SelectOpsFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择一加 OPS 固件",
                Filter = "OPS 固件 (*.ops)|*.ops|所有文件 (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                OpsFilePathTextBox.Text = dialog.FileName;
                OpsExtractProgressBar.Value = 0;
                AppendSuperLog($"[OPS] 已选择固件: {dialog.FileName}");
            }
        }

        private async void StartOpsExtractButton_Click(object sender, RoutedEventArgs e)
        {
            string sourcePath = OpsFilePathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                System.Windows.MessageBox.Show(
                    "请先选择有效的 OPS 固件文件。",
                    "解包 OPS",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string defaultOutputDirectory = Path.Combine(
                Path.GetDirectoryName(sourcePath) ?? AppDomain.CurrentDomain.BaseDirectory,
                Path.GetFileNameWithoutExtension(sourcePath) + "_解包");

            using var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择 OPS 解包输出目录",
                SelectedPath = Directory.Exists(defaultOutputDirectory)
                    ? defaultOutputDirectory
                    : Path.GetDirectoryName(sourcePath)
            };

            if (folderDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            {
                return;
            }

            string outputDirectory = Path.Combine(
                folderDialog.SelectedPath,
                Path.GetFileNameWithoutExtension(sourcePath) + "_解包");

            StartOpsExtractButton.IsEnabled = false;
            SelectOpsFileButton.IsEnabled = false;
            OpsExtractProgressBar.Value = 0;
            AppendSuperLog("=== 开始解包 OPS ===");
            AppendSuperLog($"[OPS] 源文件: {sourcePath}");
            AppendSuperLog($"[OPS] 输出目录: {outputDirectory}");
            AppendSuperLog("[OPS] 正在识别 MBox 密钥...");

            var progress = new Progress<OpsExtractProgress>(value =>
            {
                OpsExtractProgressBar.Value = Math.Clamp(value.Percent, 0, 100);
                AppendSuperLog($"[OPS] {value.Message} ({value.Percent:0}%)");
            });

            try
            {
                await OnePlusOpsExtractor.ExtractAsync(sourcePath, outputDirectory, progress);
                int extractedFileCount = Directory
                    .EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
                    .Count();
                OpsExtractProgressBar.Value = 100;
                AppendSuperLog($"COLOR:Green|[OPS] 解包完成，共输出 {extractedFileCount} 个文件");
                AppendSuperLog("=== OPS 解包任务结束 ===");

                System.Windows.MessageBox.Show(
                    $"OPS 解包完成，共输出 {extractedFileCount} 个文件。\n\n输出目录：{outputDirectory}",
                    "解包 OPS",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                Process.Start(new ProcessStartInfo
                {
                    FileName = outputDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppendSuperLog($"COLOR:Red|[OPS] 解包失败: {ex.Message}");
                System.Windows.MessageBox.Show(
                    $"OPS 解包失败：{ex.Message}",
                    "解包 OPS",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                StartOpsExtractButton.IsEnabled = true;
                SelectOpsFileButton.IsEnabled = true;
            }
        }

        private void SelectImageDirButton_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "请选择包含需要刷入镜像的文件夹",
                ShowNewFolderButton = false
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string selectedPath = folderDialog.SelectedPath;
                var tb = this.FindName("ImageDirTextBox") as System.Windows.Controls.TextBox;
                if (tb != null)
                {
                    tb.Text = selectedPath;
                    tb.Foreground = System.Windows.Media.Brushes.Black;
                }
            }
        }

        private void GenerateFlashScriptButton_Click(object sender, RoutedEventArgs e)
        {
            var tb = this.FindName("ImageDirTextBox") as System.Windows.Controls.TextBox;
            var logBox = this.FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
            void Log(string msg)
            {
                if (logBox != null)
                {
                    logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
                    logBox.ScrollToEnd();
                }
            }

            string folderPath = (tb?.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(folderPath) || !System.IO.Directory.Exists(folderPath))
            {
                Log("错误：请选择有效的镜像目录");
                return;
            }

            Log($"开始生成脚本，目录：{folderPath}");

            // 收集镜像文件（支持 .img 和 .bin）
            string[] imgFiles = Array.Empty<string>();
            string[] binFiles = Array.Empty<string>();
            try
            {
                imgFiles = System.IO.Directory.GetFiles(folderPath, "*.img", SearchOption.TopDirectoryOnly);
                binFiles = System.IO.Directory.GetFiles(folderPath, "*.bin", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                Log($"错误：扫描镜像文件失败 - {ex.Message}");
                return;
            }

            var imageFiles = imgFiles.Concat(binFiles).ToList();
            if (imageFiles.Count == 0)
            {
                Log("提示：未在所选目录中发现镜像文件（.img 或 .bin）");
                return;
            }

            // 检测是否勾选“排除敏感文件”
            bool excludeSensitive = false;
            var genBtn = this.FindName("GenerateFlashScriptButton") as System.Windows.Controls.Button;
            var parentGrid = genBtn?.Parent as System.Windows.Controls.Grid;
            var sensitiveCb = parentGrid?.Children.OfType<System.Windows.Controls.CheckBox>()
                .FirstOrDefault(cb => (cb.Content?.ToString() ?? string.Empty) == "排除敏感文件");
            if (sensitiveCb?.IsChecked == true) excludeSensitive = true;

            if (excludeSensitive)
            {
                var sensitiveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "persist", "ocdt", "fsc", "fsg", "modemst1", "modemst2", "persistbak" };
                int before = imageFiles.Count;
                imageFiles = imageFiles.Where(f => !sensitiveNames.Contains(System.IO.Path.GetFileNameWithoutExtension(f))).ToList();
                int filtered = before - imageFiles.Count;
                Log($"已排除敏感镜像 {filtered} 个");
            }

            Log($"检测到镜像 {imageFiles.Count} 个，开始生成脚本...");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001>nul");
            sb.AppendLine("echo 请将手机重启到fastboot模式");
            sb.AppendLine("echo 正在检测fastboot设备...");
            sb.AppendLine(":wait_device");
            sb.AppendLine("fastboot devices | findstr /R /C:\"^[0-9A-Za-z]\" >nul");
            sb.AppendLine("if errorlevel 1 (");
            sb.AppendLine("  echo 未检测到设备，3秒后重试...");
            sb.AppendLine("  timeout /t 3 /nobreak >nul");
            sb.AppendLine("  goto wait_device");
            sb.AppendLine(")");
            sb.AppendLine("echo 已检测到fastboot设备，开始刷入...");

            foreach (var file in imageFiles)
            {
                string partition = System.IO.Path.GetFileNameWithoutExtension(file);
                sb.AppendLine($"fastboot flash {partition} \"{file}\"");
            }

            sb.AppendLine("echo 所有分区刷入完成");
            sb.AppendLine("echo 操作完成");
            sb.AppendLine("pause");

            string scriptPath = System.IO.Path.Combine(folderPath, "flash.bat");
            try
            {
                System.IO.File.WriteAllText(scriptPath, sb.ToString(), System.Text.Encoding.UTF8);
                Log($"脚本已生成：{scriptPath}");
                System.Diagnostics.Process.Start("explorer.exe", folderPath);
            }
            catch (Exception ex)
            {
                Log($"错误：脚本生成失败 - {ex.Message}");
            }
        }

        private void CheckBox_Checked_3(object sender, RoutedEventArgs e)
        {
        }

        private void CheckBox_Checked_4(object sender, RoutedEventArgs e)
        {
        }

        private void SelectUnpackedFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "请选择解包好的文件夹",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var tb = FindName("UnpackedFolderTextBox") as System.Windows.Controls.TextBox;
                if (tb != null)
                {
                    tb.Text = dialog.SelectedPath;
                    tb.Foreground = System.Windows.Media.Brushes.Black;
                }
            }
        }

        private async void StartConvertFlashButton_Click(object sender, RoutedEventArgs e)
        {
            var tb = FindName("UnpackedFolderTextBox") as System.Windows.Controls.TextBox;
            var logBox = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
            void Log(string m)
            {
                if (logBox != null)
                {
                    logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {m}\n");
                    logBox.ScrollToEnd();
                }
            }

            string dir = (tb?.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(dir) || !System.IO.Directory.Exists(dir))
            {
                Log("错误：请选择有效的解包目录");
                return;
            }

            var files = new List<string>();
            try
            {
                files.AddRange(System.IO.Directory.GetFiles(dir, "*.img", SearchOption.TopDirectoryOnly));
                files.AddRange(System.IO.Directory.GetFiles(dir, "*.bin", SearchOption.TopDirectoryOnly));
            }
            catch (Exception ex)
            {
                Log($"错误：扫描目录失败 - {ex.Message}");
                return;
            }

            if (files.Count == 0)
            {
                Log("提示：目录中未发现镜像文件（.img 或 .bin）");
                return;
            }

            string fastbootPath = GetFastbootPath();
            Log("检测设备连接...");
            if (!await CheckFastbootDevice())
            {
                Log("错误：未检测到fastboot设备");
                return;
            }
            bool inFbd = await CheckFastbootdMode();

            var superFile = files.FirstOrDefault(f => System.IO.Path.GetFileNameWithoutExtension(f).Equals("super", StringComparison.OrdinalIgnoreCase));
            var modemFile = files.FirstOrDefault(f => System.IO.Path.GetFileNameWithoutExtension(f).Equals("modem", StringComparison.OrdinalIgnoreCase) || System.IO.Path.GetFileNameWithoutExtension(f).Equals("modem_ab", StringComparison.OrdinalIgnoreCase));

            var fastbootList = new List<string>();
            if (superFile != null) fastbootList.Add(superFile);
            if (modemFile != null && !fastbootList.Contains(modemFile)) fastbootList.Add(modemFile);

            var others = files.Where(f => !fastbootList.Contains(f)).ToList();

            if (fastbootList.Count > 0)
            {
                if (inFbd)
                {
                    Log("重启到Fastboot模式...");
                    await ExecuteFastbootCommandLive(fastbootPath, "reboot-bootloader", s => { if (logBox != null) { logBox.AppendText(s + "\n"); logBox.ScrollToEnd(); } });
                    for (int i = 0; i < 6; i++)
                    {
                        await Task.Delay(5000);
                        if (await CheckFastbootDevice()) break;
                    }
                }
                Log("在Fastboot模式刷写关键分区...");
                foreach (var f in fastbootList)
                {
                    string part = System.IO.Path.GetFileNameWithoutExtension(f);
                    Log($"fastboot flash {part} -> {f}");
                    await ExecuteFastbootCommandLive(fastbootPath, $"flash {part} \"{f}\"", s => { if (logBox != null) { logBox.AppendText(s + "\n"); logBox.ScrollToEnd(); } });
                }
            }

            if (others.Count > 0)
            {
                Log("重启到FastbootD模式...");
                if (!await RebootToFastbootd())
                {
                    Log("错误：无法进入FastbootD模式");
                    return;
                }
                bool ok = false;
                for (int i = 0; i < 6; i++)
                {
                    await Task.Delay(5000);
                    if (await CheckFastbootdMode()) { ok = true; break; }
                }
                if (!ok)
                {
                    Log("错误：FastbootD模式检测失败");
                    return;
                }
                Log("在FastbootD模式刷写其余分区...");
                foreach (var f in others)
                {
                    string part = System.IO.Path.GetFileNameWithoutExtension(f);
                    Log($"fastboot flash {part} -> {f}");
                    await ExecuteFastbootCommandLive(fastbootPath, $"flash {part} \"{f}\"", s => { if (logBox != null) { logBox.AppendText(s + "\n"); logBox.ScrollToEnd(); } });
                }
            }

            var grid = (sender as System.Windows.Controls.Button)?.Parent as System.Windows.Controls.Grid;
            bool needWipe = grid?.Children.OfType<System.Windows.Controls.CheckBox>().FirstOrDefault(cb => (cb.Content?.ToString() ?? string.Empty) == "清除数据")?.IsChecked == true;
            bool needReboot = grid?.Children.OfType<System.Windows.Controls.CheckBox>().FirstOrDefault(cb => (cb.Content?.ToString() ?? string.Empty) == "自动重启")?.IsChecked == true;

            if (needWipe)
            {
                Log("执行清除数据...");
                await ExecuteFastbootCommandLive(fastbootPath, "reboot-bootloader", s => { if (logBox != null) { logBox.AppendText(s + "\n"); logBox.ScrollToEnd(); } });
                for (int i = 0; i < 6; i++) { await Task.Delay(3000); if (await CheckFastbootDevice()) break; }
                await ExecuteFastbootCommandLive(fastbootPath, "erase frp", s => { if (logBox != null) { logBox.AppendText(s + "\n"); logBox.ScrollToEnd(); } });
                await ExecuteFastbootCommandLive(fastbootPath, "erase metadata", s => { if (logBox != null) { logBox.AppendText(s + "\n"); logBox.ScrollToEnd(); } });
                await ExecuteFastbootCommandLive(fastbootPath, "erase userdata", s => { if (logBox != null) { logBox.AppendText(s + "\n"); logBox.ScrollToEnd(); } });
                await ExecuteFastbootCommandLive(fastbootPath, "-w", s => { if (logBox != null) { logBox.AppendText(s + "\n"); logBox.ScrollToEnd(); } });
                Log("清除数据完成");
            }

            if (needReboot)
            {
                Log("自动重启设备...");
                await ExecuteFastbootCommandLive(fastbootPath, "reboot", s => { if (logBox != null) { logBox.AppendText(s + "\n"); logBox.ScrollToEnd(); } });
            }

            Log("刷写流程完成");
        }

        private async Task HandleReadPartitionTable()
        {
            try
            {
                string adbPath = GetToolPath("adb.exe");
                string serial = GetSelectedDeviceSerial();
                string args = string.IsNullOrWhiteSpace(serial)
                    ? "shell ls -l /dev/block/by-name/"
                    : $"-s {serial} shell ls -l /dev/block/by-name/";
                string output = await GetCommandOutput(adbPath, args);
                var names = new List<string>();
                using (var reader = new StringReader(output))
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        int arrow = line.IndexOf("->");
                        if (arrow > 0)
                        {
                            string left = line.Substring(0, arrow).TrimEnd();
                            int lastSpace = left.LastIndexOf(' ');
                            if (lastSpace >= 0 && lastSpace < left.Length - 1)
                            {
                                string name = left.Substring(lastSpace + 1).Trim();
                                if (!string.IsNullOrWhiteSpace(name))
                                    names.Add(name);
                            }
                        }
                    }
                }
                names = names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
                Dispatcher.Invoke(() =>
                {
                    var combo = FindName("ReadPartitionComboBox") as System.Windows.Controls.ComboBox;
                    if (combo != null)
                    {
                        combo.ItemsSource = null;
                        combo.Items.Clear();
                        combo.ItemsSource = names;
                        if (names.Count > 0)
                        {
                            combo.IsEditable = false;
                            combo.IsReadOnly = false;
                            combo.SelectedIndex = 0;
                        }
                        else
                        {
                            combo.IsEditable = true;
                            combo.IsReadOnly = true;
                            combo.Text = "未读取到分区";
                        }
                    }
                    var logBox = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                    if (logBox != null)
                    {
                        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已读取分区表，共 {names.Count} 项\n");
                        logBox.ScrollToEnd();
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    var logBox = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                    if (logBox != null)
                    {
                        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 错误：{ex.Message}\n");
                        logBox.ScrollToEnd();
                    }
                });
            }
        }

        private async void ReadOutPartitionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var combo = FindName("ReadPartitionComboBox") as System.Windows.Controls.ComboBox;
                string name = combo?.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(name)) name = combo?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(name) || name.Contains("读取分区表") || name.Contains("未读取到分区"))
                {
                    var logBox = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                    if (logBox != null)
                    {
                        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 错误：未选择有效分区\n");
                        logBox.ScrollToEnd();
                    }
                    return;
                }
                string defaultDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "选择保存路径",
                    FileName = $"{name}.img",
                    Filter = "镜像文件 (*.img)|*.img|所有文件 (*.*)|*.*",
                    InitialDirectory = defaultDir
                };
                bool? dialogResult = sfd.ShowDialog();
                if (dialogResult != true || string.IsNullOrWhiteSpace(sfd.FileName))
                {
                    var logBox = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                    if (logBox != null)
                    {
                        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已取消保存\n");
                        logBox.ScrollToEnd();
                    }
                    return;
                }
                string outPath = sfd.FileName;
                string adbPath = GetToolPath("adb.exe");
                string serial = GetSelectedDeviceSerial();
                string args = string.IsNullOrWhiteSpace(serial)
                    ? $"exec-out dd if=/dev/block/by-name/{name}"
                    : $"-s {serial} exec-out dd if=/dev/block/by-name/{name}";
                var start = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                await Task.Run(() =>
                {
                    var p = new Process { StartInfo = start };
                    using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        p.Start();
                        p.StandardOutput.BaseStream.CopyTo(fs);
                        p.WaitForExit();
                    }
                    string err = p.StandardError.ReadToEnd();
                    if (!string.IsNullOrEmpty(err))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            var logBox = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                            if (logBox != null)
                            {
                                logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 警告：{err}\n");
                                logBox.ScrollToEnd();
                            }
                        });
                    }
                });
                Dispatcher.Invoke(() =>
                {
                    var logBox = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                    if (logBox != null)
                    {
                        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已提取 {name} 到: {outPath}\n");
                        logBox.ScrollToEnd();
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    var logBox = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                    if (logBox != null)
                    {
                        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 错误：{ex.Message}\n");
                        logBox.ScrollToEnd();
                    }
                });
            }
        }
        private async Task ExecuteFastbootCommandLive(string fastbootPath, string arguments, Action<string> onLine)
        {
            try
            {
                await Task.Run(() =>
                {
                    string selectedSerial = GetSelectedDeviceSerial();
                    string finalArguments = arguments;
                    if (!string.IsNullOrEmpty(selectedSerial)) finalArguments = $"-s {selectedSerial} {arguments}";
                    var p = new Process
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
                    p.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Dispatcher.Invoke(() => onLine(e.Data)); };
                    p.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Dispatcher.Invoke(() => onLine(e.Data)); };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    p.WaitForExit();
                });
            }
            catch
            {
            }
        }
        
        private async void ReadOutPartitionButton_Click_New(object sender, RoutedEventArgs e)
        {
            try
            {
                var combo = FindName("ReadPartitionComboBox") as System.Windows.Controls.ComboBox;
                string name = combo?.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(name)) name = combo?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(name) || name.Contains("读取分区表") || name.Contains("未读取到分区"))
                {
                    var logBox = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                    if (logBox != null)
                    {
                        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 错误：未选择有效分区\n");
                        logBox.ScrollToEnd();
                    }
                    return;
                }
                string defaultDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "选择保存路径",
                    FileName = $"{name}.img",
                    Filter = "镜像文件 (*.img)|*.img|所有文件 (*.*)|*.*",
                    InitialDirectory = defaultDir
                };
                bool? dialogResult = sfd.ShowDialog();
                if (dialogResult != true || string.IsNullOrWhiteSpace(sfd.FileName))
                {
                    var logCancel = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                    if (logCancel != null)
                    {
                        logCancel.AppendText($"[{DateTime.Now:HH:mm:ss}] 已取消保存\n");
                        logCancel.ScrollToEnd();
                    }
                    return;
                }
                string outPath = sfd.FileName;
                string adbPath = GetToolPath("adb.exe");
                string serial = GetSelectedDeviceSerial();
                string devicePath = $"/sdcard/{name}_original.img";
                string baseCmd = $"dd if=/dev/block/by-name/{name} of={devicePath} bs=4096 && sync";
                string copyArgsSu = string.IsNullOrWhiteSpace(serial)
                    ? $"shell su -c \"{baseCmd}\""
                    : $"-s {serial} shell su -c \"{baseCmd}\"";
                string copyArgsNoSu = string.IsNullOrWhiteSpace(serial)
                    ? $"shell {baseCmd}"
                    : $"-s {serial} shell {baseCmd}";
                string noSuResultFirst = await GetCommandOutput(adbPath, copyArgsNoSu);
                string lsCheck = await GetCommandOutput(adbPath, string.IsNullOrWhiteSpace(serial) ? $"shell ls -l {devicePath}" : $"-s {serial} shell ls -l {devicePath}");
                if (string.IsNullOrWhiteSpace(lsCheck) || !lsCheck.Contains(name + "_original.img"))
                {
                    string suResult = await GetCommandOutput(adbPath, copyArgsSu);
                    lsCheck = await GetCommandOutput(adbPath, string.IsNullOrWhiteSpace(serial) ? $"shell ls -l {devicePath}" : $"-s {serial} shell ls -l {devicePath}");
                    if (string.IsNullOrWhiteSpace(lsCheck) || !lsCheck.Contains(name + "_original.img"))
                    {
                        var logFail = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                        if (logFail != null)
                        {
                            logFail.AppendText($"[{DateTime.Now:HH:mm:ss}] 设备端生成失败，尝试直流到电脑...\n");
                            logFail.ScrollToEnd();
                        }
                        string execArgs = string.IsNullOrWhiteSpace(serial)
                            ? $"exec-out dd if=/dev/block/by-name/{name} bs=4096"
                            : $"-s {serial} exec-out dd if=/dev/block/by-name/{name} bs=4096";
                        try
                        {
                            await Task.Run(() =>
                            {
                                var p = new Process
                                {
                                    StartInfo = new ProcessStartInfo
                                    {
                                        FileName = adbPath,
                                        Arguments = execArgs,
                                        UseShellExecute = false,
                                        RedirectStandardOutput = true,
                                        RedirectStandardError = true,
                                        CreateNoWindow = true
                                    }
                                };
                                using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
                                {
                                    p.Start();
                                    p.StandardOutput.BaseStream.CopyTo(fs);
                                    p.WaitForExit();
                                }
                                string err = p.StandardError.ReadToEnd();
                                if (!string.IsNullOrEmpty(err))
                                {
                                    Dispatcher.Invoke(() =>
                                    {
                                        var logBox = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                                        if (logBox != null)
                                        {
                                            logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 警告：{err}\n");
                                            logBox.ScrollToEnd();
                                        }
                                    });
                                }
                            });
                            bool okStream = false;
                            try
                            {
                                okStream = System.IO.File.Exists(outPath) && new System.IO.FileInfo(outPath).Length > 0;
                            }
                            catch { okStream = false; }
                            if (!okStream)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    var logBox = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                                    if (logBox != null)
                                    {
                                        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 错误：直流失败，未生成文件\n");
                                        logBox.ScrollToEnd();
                                    }
                                });
                            }
                            else
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    var logBox = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                                    if (logBox != null)
                                    {
                                        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已提取 {name} 到: {outPath}\n");
                                        logBox.ScrollToEnd();
                                    }
                                });
                            }
                        }
                        catch (Exception ex2)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                var logBox = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                                if (logBox != null)
                                {
                                    logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 错误：直流异常：{ex2.Message}\n");
                                    logBox.ScrollToEnd();
                                }
                            });
                        }
                        return;
                    }
                }
                string pullArgs = string.IsNullOrWhiteSpace(serial)
                    ? $"pull {devicePath} \"{outPath}\""
                    : $"-s {serial} pull {devicePath} \"{outPath}\"";
                string pullResult = await GetCommandOutput(adbPath, pullArgs);
                bool ok = false;
                try
                {
                    ok = System.IO.File.Exists(outPath) && new System.IO.FileInfo(outPath).Length > 0;
                }
                catch
                {
                    ok = false;
                }
                if (!ok)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var logBox = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                        if (logBox != null)
                        {
                            logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 错误：拉取失败，未生成文件。adb输出：{pullResult}\n");
                            logBox.ScrollToEnd();
                        }
                    });
                    return;
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        var logBox = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                        if (logBox != null)
                        {
                            logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已提取 {name} 到: {outPath}\n");
                            logBox.ScrollToEnd();
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    var logBox = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                    if (logBox != null)
                    {
                        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 错误：{ex.Message}\n");
                        logBox.ScrollToEnd();
                    }
                });
            }
        }
        
        private async void WriteSelectedPartitionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var combo = FindName("ReadPartitionComboBox") as System.Windows.Controls.ComboBox;
                string? partition = combo?.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(partition)) partition = combo?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(partition) || partition.Contains("读取分区表") || partition.Contains("未读取到分区"))
                {
                    var log = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                    if (log != null)
                    {
                        log.AppendText($"[{DateTime.Now:HH:mm:ss}] 错误：未选择有效分区\n");
                        log.ScrollToEnd();
                    }
                    return;
                }
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择要写入的镜像文件",
                    Filter = "镜像文件 (*.img)|*.img|所有文件 (*.*)|*.*",
                    Multiselect = false
                };
                bool? r = ofd.ShowDialog();
                if (r != true || string.IsNullOrWhiteSpace(ofd.FileName) || !System.IO.File.Exists(ofd.FileName))
                {
                    var log = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                    if (log != null)
                    {
                        log.AppendText($"[{DateTime.Now:HH:mm:ss}] 已取消或文件无效\n");
                        log.ScrollToEnd();
                    }
                    return;
                }
                string localPath = ofd.FileName;
                string fileName = System.IO.Path.GetFileName(localPath);
                string remotePath = $"/tmp/{fileName}";
                string adbPath = GetToolPath("adb.exe");
                string serial = GetSelectedDeviceSerial();
                string pushArgs = string.IsNullOrWhiteSpace(serial)
                    ? $"push \"{localPath}\" \"/tmp/\""
                    : $"-s {serial} push \"{localPath}\" \"/tmp/\"";
                string pushResult = await GetCommandOutput(adbPath, pushArgs);
                string checkTmp = await GetCommandOutput(adbPath, string.IsNullOrWhiteSpace(serial) ? $"shell ls -l {remotePath}" : $"-s {serial} shell ls -l {remotePath}");
                if (string.IsNullOrWhiteSpace(checkTmp) || (!checkTmp.Contains(fileName)))
                {
                    var log = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                    if (log != null)
                    {
                        log.AppendText($"[{DateTime.Now:HH:mm:ss}] 错误：推送失败，adb输出：{pushResult}\n");
                        log.ScrollToEnd();
                    }
                    return;
                }
                string ddCmd = $"dd if={remotePath} of=/dev/block/by-name/{partition} bs=4096 && sync";
                string ddArgsNoSu = string.IsNullOrWhiteSpace(serial)
                    ? $"shell {ddCmd}"
                    : $"-s {serial} shell {ddCmd}";
                string ddOutNoSu = await GetCommandOutput(adbPath, ddArgsNoSu);
                bool needSu = false;
                if (!string.IsNullOrEmpty(ddOutNoSu))
                {
                    string lower = ddOutNoSu.ToLowerInvariant();
                    if (lower.Contains("permission denied") || lower.Contains("operation not permitted") || lower.Contains("read-only file system"))
                    {
                        needSu = true;
                    }
                }
                else
                {
                    needSu = true;
                }
                if (needSu)
                {
                    string ddArgsSu = string.IsNullOrWhiteSpace(serial)
                        ? $"shell su -c \"{ddCmd}\""
                        : $"-s {serial} shell su -c \"{ddCmd}\"";
                    string ddOutSu = await GetCommandOutput(adbPath, ddArgsSu);
                    var log = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                    if (log != null)
                    {
                        if (string.IsNullOrWhiteSpace(ddOutSu) || ddOutSu.Contains("su: inaccessible", StringComparison.OrdinalIgnoreCase) || ddOutSu.Contains("not found", StringComparison.OrdinalIgnoreCase))
                        {
                            log.AppendText($"[{DateTime.Now:HH:mm:ss}] 错误：写入失败，su不可用或权限不足。非su输出：{ddOutNoSu} su输出：{ddOutSu}\n");
                        }
                        else
                        {
                            log.AppendText($"[{DateTime.Now:HH:mm:ss}] 写入完成\n");
                        }
                        log.ScrollToEnd();
                    }
                }
                else
                {
                    var log = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                    if (log != null)
                    {
                        log.AppendText($"[{DateTime.Now:HH:mm:ss}] 写入完成\n");
                        log.ScrollToEnd();
                    }
                }
            }
            catch (Exception ex)
            {
                var log = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                if (log != null)
                {
                    log.AppendText($"[{DateTime.Now:HH:mm:ss}] 错误：{ex.Message}\n");
                    log.ScrollToEnd();
                }
            }
        }
        
        private async void PushZipToSdcardButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择要推送的卡刷包",
                    Filter = "卡刷包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
                    Multiselect = false
                };
                bool? r = ofd.ShowDialog();
                if (r != true || string.IsNullOrWhiteSpace(ofd.FileName) || !System.IO.File.Exists(ofd.FileName))
                {
                    var log = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                    if (log != null)
                    {
                        log.AppendText($"[{DateTime.Now:HH:mm:ss}] 已取消或文件无效\n");
                        log.ScrollToEnd();
                    }
                    return;
                }
                string localZip = ofd.FileName;
                string adbPath = GetToolPath("adb.exe");
                string devicesOut = await GetCommandOutput(adbPath, "devices");
                bool hasRecovery = !string.IsNullOrWhiteSpace(devicesOut) && devicesOut.IndexOf("recovery", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!hasRecovery)
                {
                    var log = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                    if (log != null)
                    {
                        log.AppendText($"[{DateTime.Now:HH:mm:ss}] 未检测到 recovery 设备\n");
                        log.ScrollToEnd();
                    }
                    return;
                }
                string serial = GetSelectedDeviceSerial();
                string pushArgs = string.IsNullOrWhiteSpace(serial)
                    ? $"push \"{localZip}\" \"/sdcard/\""
                    : $"-s {serial} push \"{localZip}\" \"/sdcard/\"";
                string pushOut = await GetCommandOutput(adbPath, pushArgs);
                string fileName = System.IO.Path.GetFileName(localZip);
                string lsOut = await GetCommandOutput(adbPath, string.IsNullOrWhiteSpace(serial) ? $"shell ls -l \"/sdcard/{fileName}\"" : $"-s {serial} shell ls -l \"/sdcard/{fileName}\"");
                bool ok = !string.IsNullOrWhiteSpace(lsOut) && lsOut.IndexOf(fileName, StringComparison.OrdinalIgnoreCase) >= 0;
                var logFinal = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                if (logFinal != null)
                {
                    if (ok)
                    {
                        logFinal.AppendText($"[{DateTime.Now:HH:mm:ss}] 已推送卡刷包到设备：/sdcard/{fileName}\n");
                    }
                    else
                    {
                        logFinal.AppendText($"[{DateTime.Now:HH:mm:ss}] 错误：推送失败。adb输出：{pushOut}\n");
                    }
                    logFinal.ScrollToEnd();
                }
            }
            catch (Exception ex)
            {
                var log = FindName("SuperPackLogRichTextBox") as System.Windows.Controls.RichTextBox;
                if (log != null)
                {
                    log.AppendText($"[{DateTime.Now:HH:mm:ss}] 错误：{ex.Message}\n");
                    log.ScrollToEnd();
                }
            }
        }
        
        private void PartitionSelectAllCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            bool adbMode = IsFastbootVisualizationAdbMode();
            foreach (var partition in allPartitions)
            {
                partition.IsSelected =
                    !IsFastbootVisualizationPartitionProtected(partition.PartitionName, adbMode);
            }
            PartitionTableDataGrid.Items.Refresh();
        }

        private void PartitionSelectAllCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (var partition in allPartitions)
            {
                partition.IsSelected = false;
            }
            PartitionTableDataGrid.Items.Refresh();
        }

        private bool IsFastbootVisualizationAdbMode()
        {
            return string.Equals(
                BottomConnectionTypeText?.Text,
                "系统",
                StringComparison.OrdinalIgnoreCase);
        }

        private bool IsFastbootVisualizationPartitionProtected(string? partitionName, bool adbMode)
        {
            if (ProtectPartitionCheckBox?.IsChecked != true || string.IsNullOrWhiteSpace(partitionName))
            {
                return false;
            }

            if (IsFastbootVisualizationBasebandProtectedPartitionLabel(partitionName))
            {
                return true;
            }

            return adbMode && IsEdlDataPartitionLabel(partitionName);
        }

        internal static bool IsFastbootVisualizationBasebandProtectedPartitionLabel(string? partitionName)
        {
            if (string.IsNullOrWhiteSpace(partitionName))
            {
                return false;
            }

            string normalizedName = StripEdlSlotSuffix(partitionName.Trim());
            return !normalizedName.Equals("persist", StringComparison.OrdinalIgnoreCase) &&
                   IsEdlBasebandFingerprintBackupPartitionLabel(normalizedName);
        }

        private void ApplyFastbootVisualizationPartitionProtectionState(bool adbMode)
        {
            if (ProtectPartitionCheckBox?.IsChecked != true)
            {
                return;
            }

            foreach (PartitionInfo partition in allPartitions)
            {
                if (IsFastbootVisualizationPartitionProtected(partition.PartitionName, adbMode))
                {
                    partition.IsSelected = false;
                }
            }

            PartitionTableDataGrid?.Items.Refresh();
            UpdateSelectAllState();
        }

        private void ProtectPartitionCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            ApplyFastbootVisualizationPartitionProtectionState(IsFastbootVisualizationAdbMode());
        }

        private void PartitionSelectionCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox checkBox &&
                checkBox.DataContext is PartitionInfo partition &&
                partition.IsSelected &&
                IsFastbootVisualizationPartitionProtected(
                    partition.PartitionName,
                    IsFastbootVisualizationAdbMode()))
            {
                partition.IsSelected = false;
                e.Handled = true;
            }
        }

        private void SelectFlashBatButton_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.Title = "选择刷写文件";
            openFileDialog.Filter = "刷写文件 (*.bat;rawprogram*.xml)|*.bat;rawprogram*.xml|小米线刷脚本 (*.bat)|*.bat|RawProgram XML (rawprogram*.xml)|rawprogram*.xml";
            openFileDialog.FilterIndex = 1;
            openFileDialog.RestoreDirectory = true;
            openFileDialog.Multiselect = true;

            if (openFileDialog.ShowDialog() == true)
            {
                string[] selectedFiles = openFileDialog.FileNames;
                bool containsBat = selectedFiles.Any(file =>
                    Path.GetExtension(file).Equals(".bat", StringComparison.OrdinalIgnoreCase));
                bool allRawPrograms = selectedFiles.All(file =>
                    Path.GetExtension(file).Equals(".xml", StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileName(file).StartsWith("rawprogram", StringComparison.OrdinalIgnoreCase));

                if (containsBat)
                {
                    if (selectedFiles.Length != 1 ||
                        !Path.GetExtension(selectedFiles[0]).Equals(".bat", StringComparison.OrdinalIgnoreCase))
                    {
                        LogToFastboot("小米线刷BAT只能单独选择一个，不能与RawProgram混选", "Yellow");
                        return;
                    }

                    FlashBatTextBox.Text = selectedFiles[0];
                    // 解析小米线刷脚本中的分区信息
                    if (ParseXiaomiFlashScript(selectedFiles[0]))
                    {
                        UpdateXiaomiScriptOnlyOptionsState();
                        LogToFastboot($"已选择小米线刷脚本：{selectedFiles[0]}", "Blue");
                    }
                }
                else if (allRawPrograms)
                {
                    string displayText = string.Join("; ", selectedFiles);
                    FlashBatTextBox.Text = displayText;
                    LogToFastbootStyled(
                        ("用户已选择RawProgram文件，共", "Black", false),
                        ($"{selectedFiles.Length}", "Purple", true),
                        ("个", "Black", false));
                    ParseRawProgramXmlFiles(selectedFiles, displayText);
                }
                else
                {
                    LogToFastboot("请选择一个BAT，或同时选择一个或多个rawprogram*.xml", "Yellow");
                }
            }
        }

        private void FlashBatTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateXiaomiScriptOnlyOptionsState();
        }

        private void FlashBatTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            const string placeholderText = "flash_all*.bat 或 rawprogram*.xml";
            if (sender is System.Windows.Controls.TextBox textBox &&
                string.Equals(textBox.Text, placeholderText, StringComparison.Ordinal))
            {
                textBox.Clear();
            }
        }

        private bool ParseRawProgramXmlFiles(IReadOnlyCollection<string> xmlPaths, string displayText)
        {
            try
            {
                DeactivateXiaomiScriptMode();
                var parsedPartitions = new List<PartitionInfo>();
                var unsupportedSegmentedLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int skippedGptCount = 0;
                int skippedInvalidCount = 0;

                foreach (string xmlPath in xmlPaths
                             .Select(Path.GetFullPath)
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var document = System.Xml.Linq.XDocument.Load(xmlPath);
                    string xmlDirectory = Path.GetDirectoryName(xmlPath) ?? string.Empty;
                    int loadedFileCount = 0;
                    var programElements = document
                        .Descendants()
                        .Where(element => element.Name.LocalName.Equals("program", StringComparison.OrdinalIgnoreCase));

                    foreach (var program in programElements)
                    {
                        string label = ((string?)program.Attribute("label") ?? string.Empty).Trim();
                        string fileName = ((string?)program.Attribute("filename") ?? string.Empty).Trim().Trim('"');
                        string fileSectorOffset = ((string?)program.Attribute("file_sector_offset") ?? "0").Trim();
                        if (IsRawProgramGptEntry(label, fileName))
                        {
                            skippedGptCount++;
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(fileName))
                        {
                            skippedInvalidCount++;
                            continue;
                        }

                        if (unsupportedSegmentedLabels.Contains(label) ||
                            !ulong.TryParse(fileSectorOffset, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsedFileSectorOffset) ||
                            parsedFileSectorOffset != 0)
                        {
                            unsupportedSegmentedLabels.Add(label);
                            parsedPartitions.RemoveAll(partition =>
                                partition.PartitionName.Equals(label, StringComparison.OrdinalIgnoreCase));
                            skippedInvalidCount++;
                            continue;
                        }

                        string imagePath = Path.IsPathFullyQualified(fileName)
                            ? fileName
                            : Path.GetFullPath(Path.Combine(xmlDirectory, fileName.Replace('/', Path.DirectorySeparatorChar)));
                        if (!File.Exists(imagePath))
                        {
                            unsupportedSegmentedLabels.Add(label);
                            skippedInvalidCount++;
                            continue;
                        }

                        if (parsedPartitions.Any(partition =>
                                partition.PartitionName.Equals(label, StringComparison.OrdinalIgnoreCase)))
                        {
                            unsupportedSegmentedLabels.Add(label);
                            skippedInvalidCount++;
                            LogToFastboot($"RawProgram分区包含多个物理片段，已跳过：{label}", "Yellow");
                            parsedPartitions.RemoveAll(partition =>
                                partition.PartitionName.Equals(label, StringComparison.OrdinalIgnoreCase));
                            continue;
                        }

                        parsedPartitions.Add(new PartitionInfo
                        {
                            PartitionName = label,
                            PartitionSize = FormatFileSize(new FileInfo(imagePath).Length),
                            PartitionType = "RawProgram分区",
                            FilePath = imagePath,
                            IsSelected = true
                        });
                        loadedFileCount++;
                    }

                    LogToFastbootStyled(
                        ($"已加载{Path.GetFileName(xmlPath)}，共", "Black", false),
                        ($"{loadedFileCount}", "Purple", true),
                        ("个文件", "Black", false));
                }

                allPartitions.Clear();
                foreach (PartitionInfo partition in parsedPartitions)
                {
                    allPartitions.Add(partition);
                }
                PartitionTableDataGrid.ItemsSource = allPartitions;
                PartitionTableDataGrid.Items.Refresh();
                ApplyFastbootVisualizationPartitionProtectionState(IsFastbootVisualizationAdbMode());
                UpdateSelectAllState();

                if (parsedPartitions.Count == 0)
                {
                    LogToFastboot("所选RawProgram中没有可用于分区刷写的完整镜像条目", "Red");
                    return false;
                }

                _parsedRawProgramPaths = xmlPaths
                    .Select(Path.GetFullPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                _parsedRawProgramDisplayText = displayText;
                UpdateXiaomiScriptOnlyOptionsState();

                bool adbMode = IsFastbootVisualizationAdbMode();
                bool protectionEnabled = ProtectPartitionCheckBox?.IsChecked == true;
                int skippedDataPartitionCount = protectionEnabled && adbMode
                    ? parsedPartitions.Count(partition => IsEdlDataPartitionLabel(partition.PartitionName))
                    : 0;
                int skippedBasebandPartitionCount = protectionEnabled
                    ? parsedPartitions.Count(partition =>
                        IsFastbootVisualizationBasebandProtectedPartitionLabel(partition.PartitionName))
                    : 0;

                var skippedCategories = new List<(int Count, string Label)>();
                if (skippedGptCount > 0)
                {
                    skippedCategories.Add((skippedGptCount, "个GPT物理条目"));
                }
                if (skippedDataPartitionCount > 0)
                {
                    skippedCategories.Add((skippedDataPartitionCount, "个数据分区"));
                }
                if (skippedBasebandPartitionCount > 0)
                {
                    skippedCategories.Add((skippedBasebandPartitionCount, "个基带相关分区"));
                }

                var summarySegments = new List<(string Text, string Color, bool Emphasized)>
                {
                    ("本次刷写分区数共", "Black", false),
                    ($"{parsedPartitions.Count}", "Purple", true),
                    ("个", "Black", false)
                };
                if (skippedCategories.Count > 0)
                {
                    summarySegments.Add(("，本次刷写将跳过", "Black", false));
                    for (int index = 0; index < skippedCategories.Count; index++)
                    {
                        if (index > 0)
                        {
                            summarySegments.Add((
                                index == skippedCategories.Count - 1 ? "和" : "、",
                                "Black",
                                false));
                        }

                        summarySegments.Add(($"{skippedCategories[index].Count}", "Purple", true));
                        summarySegments.Add((skippedCategories[index].Label, "Black", false));
                    }
                }
                summarySegments.Add(("。", "Black", false));
                LogToFastbootStyled(summarySegments.ToArray());

                if (adbMode && (skippedDataPartitionCount > 0 || skippedBasebandPartitionCount > 0))
                {
                    LogToFastboot(
                        "开机模式下写入数据分区和基带相关分区可能导致数据丢失或设备无限重启；如有相关需求，请将设备重启到Fastboot模式下刷写相关分区",
                        "Yellow");
                }
                return true;
            }
            catch (Exception ex)
            {
                DeactivateXiaomiScriptMode();
                allPartitions.Clear();
                PartitionTableDataGrid?.Items.Refresh();
                LogToFastboot($"RawProgram解析失败：{ex.Message}", "Red");
                return false;
            }
        }

        private static bool IsRawProgramGptEntry(string label, string fileName)
        {
            string normalizedLabel = label.Trim();
            string normalizedFile = Path.GetFileName(fileName.Trim());
            return normalizedLabel.Equals("PrimaryGPT", StringComparison.OrdinalIgnoreCase) ||
                   normalizedLabel.Equals("BackupGPT", StringComparison.OrdinalIgnoreCase) ||
                   normalizedLabel.Equals("GPT", StringComparison.OrdinalIgnoreCase) ||
                   normalizedFile.StartsWith("gpt_main", StringComparison.OrdinalIgnoreCase) ||
                   normalizedFile.StartsWith("gpt_backup", StringComparison.OrdinalIgnoreCase);
        }

        private void SkipCrcCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ApplySkipCrcSelectionState();
        }

        private void ApplySkipCrcSelectionState()
        {
            if (!HasActiveParsedXiaomiScript() || SkipCrcCheckBox == null || allPartitions == null)
            {
                return;
            }

            bool selectCrcPartitions = SkipCrcCheckBox.IsChecked != true;
            foreach (PartitionInfo partition in allPartitions.Where(partition =>
                         IsCrcListPartition(partition.PartitionName)))
            {
                partition.IsSelected = selectCrcPartitions;
            }

            PartitionTableDataGrid?.Items.Refresh();
            UpdateSelectAllState();
        }

        private void UpdateXiaomiScriptOnlyOptionsState()
        {
            bool hasParsedScript = HasActiveParsedXiaomiScript();
            bool hasRawProgramInFastboot = HasActiveParsedRawProgram() &&
                                           string.Equals(
                                               BottomConnectionTypeText?.Text,
                                               "Fastboot",
                                               StringComparison.OrdinalIgnoreCase);

            SetFlashOptionState(SwitchSlotACheckBox, hasParsedScript || hasRawProgramInFastboot, false);
            SetFlashOptionState(KeepUserDataCheckBox, hasParsedScript || hasRawProgramInFastboot, false);
            SetFlashOptionState(LockBootloaderCheckBox, hasParsedScript, false);
            SetFlashOptionState(DisableDmVerityCheckBox, hasParsedScript, false);
            SetFlashOptionState(SkipCrcCheckBox, hasParsedScript, true);

            if (hasParsedScript)
            {
                ApplySkipCrcSelectionState();
            }
        }

        private static void SetFlashOptionState(
            System.Windows.Controls.CheckBox? option,
            bool isEnabled,
            bool disabledCheckedState)
        {
            if (option == null)
            {
                return;
            }

            option.IsEnabled = isEnabled;
            if (!isEnabled)
            {
                option.IsChecked = disabledCheckedState;
            }
        }

        private bool HasActiveParsedXiaomiScript()
        {
            try
            {
                string currentPath = FlashBatTextBox?.Text?.Trim() ?? string.Empty;
                return _parsedXiaomiFlashScriptLines != null &&
                       Path.IsPathFullyQualified(currentPath) &&
                       File.Exists(currentPath) &&
                       string.Equals(Path.GetExtension(currentPath), ".bat", StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(
                           _parsedXiaomiFlashScriptPath,
                           Path.GetFullPath(currentPath),
                           StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool HasActiveParsedRawProgram()
        {
            try
            {
                string currentPath = FlashBatTextBox?.Text?.Trim() ?? string.Empty;
                return _parsedRawProgramPaths.Count > 0 &&
                       _parsedRawProgramPaths.All(path =>
                           File.Exists(path) &&
                           Path.GetFileName(path).StartsWith("rawprogram", StringComparison.OrdinalIgnoreCase)) &&
                       string.Equals(
                           _parsedRawProgramDisplayText,
                           currentPath,
                           StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private void DeactivateXiaomiScriptMode()
        {
            _parsedXiaomiFlashScriptPath = null;
            _parsedXiaomiFlashScriptLines = null;
            _parsedRawProgramPaths = Array.Empty<string>();
            _parsedRawProgramDisplayText = null;
            UpdateXiaomiScriptOnlyOptionsState();
        }

        private bool ParseXiaomiFlashScript(string scriptPath)
        {
            try
            {
                _parsedXiaomiFlashScriptPath = null;
                _parsedXiaomiFlashScriptLines = null;
                _parsedRawProgramPaths = Array.Empty<string>();
                _parsedRawProgramDisplayText = null;

                // 清空现有分区列表
                allPartitions.Clear();
                
                // 读取脚本文件内容
                string[] lines = System.IO.File.ReadAllLines(scriptPath);
                
                foreach (string line in lines)
                {
                    // 查找fastboot flash命令行
                    if (line.Trim().StartsWith("fastboot") && line.Contains("flash"))
                    {
                        // 解析fastboot flash命令
                        string[] parts = line.Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        
                        // 找到flash关键字的位置
                        int flashIndex = -1;
                        for (int i = 0; i < parts.Length; i++)
                        {
                            if (parts[i] == "flash")
                            {
                                flashIndex = i;
                                break;
                            }
                        }
                        
                        // 如果找到flash关键字且后面有分区名称
                        if (flashIndex >= 0 && flashIndex + 1 < parts.Length)
                        {
                            string partitionName = parts[flashIndex + 1];
                            
                            // 获取镜像文件路径（如果存在）
                            string imagePath = "";
                            if (flashIndex + 2 < parts.Length)
                            {
                                imagePath = parts[flashIndex + 2];
                                // 移除可能的路径前缀
                                if (imagePath.Contains("%~dp0images/"))
                                {
                                    imagePath = imagePath.Replace("%~dp0images/", "");
                                }
                                else if (imagePath.Contains("%~dp0images\\"))
                                {
                                    imagePath = imagePath.Replace("%~dp0images\\", "");
                                }
                            }
                            
                            // 检查是否为 modem_ab 分区，如果是则分解为 modem_a 和 modem_b 两个分区
                            if (partitionName.Equals("modem_ab", StringComparison.OrdinalIgnoreCase))
                            {
                                // 创建 modem_a 分区
                                PartitionInfo partitionA = new PartitionInfo
                                {
                                    PartitionName = "modem_a",
                                    PartitionSize = "未知",
                                    PartitionType = "Flash分区",
                                    FilePath = imagePath,
                                    IsSelected = false
                                };
                                
                                // 创建 modem_b 分区
                                PartitionInfo partitionB = new PartitionInfo
                                {
                                    PartitionName = "modem_b",
                                    PartitionSize = "未知",
                                    PartitionType = "Flash分区",
                                    FilePath = imagePath,
                                    IsSelected = false
                                };
                                
                                // 检查是否已存在相同名称的分区（避免重复）
                                bool existsA = false, existsB = false;
                                foreach (var existingPartition in allPartitions)
                                {
                                    if (existingPartition.PartitionName.Equals("modem_a", StringComparison.OrdinalIgnoreCase))
                                        existsA = true;
                                    if (existingPartition.PartitionName.Equals("modem_b", StringComparison.OrdinalIgnoreCase))
                                        existsB = true;
                                }
                                
                                if (!existsA)
                                {
                                    allPartitions.Add(partitionA);
                                }
                                if (!existsB)
                                {
                                    allPartitions.Add(partitionB);
                                }
                            }
                            else
                            {
                                // 普通分区处理
                                PartitionInfo partition = new PartitionInfo
                                {
                                    PartitionName = partitionName,
                                    PartitionSize = "未知", // 脚本中通常不包含大小信息
                                    PartitionType = "Flash分区",
                                    FilePath = imagePath,
                                    IsSelected = false
                                };
                                
                                // 检查是否已存在相同名称的分区（避免重复）
                                bool exists = false;
                                foreach (var existingPartition in allPartitions)
                                {
                                    if (existingPartition.PartitionName == partitionName)
                                    {
                                        exists = true;
                                        break;
                                    }
                                }
                                
                                if (!exists)
                                {
                                    allPartitions.Add(partition);
                                }
                            }
                        }
                    }
                }
                
                // 解析分区文件的实际存放路径
                string scriptDirectory = Path.GetDirectoryName(scriptPath) ?? "";
                if (string.IsNullOrEmpty(scriptDirectory))
                {
                    throw new InvalidOperationException("无法解析脚本目录");
                }
                string imagesDirectory = Path.Combine(scriptDirectory, "images");
                
                foreach (var partition in allPartitions)
                {
                    if (!string.IsNullOrEmpty(partition.FilePath))
                    {
                        // 构建完整的文件路径
                        string fullImagePath = Path.Combine(imagesDirectory, partition.FilePath);
                        
                        // 检查文件是否存在并更新路径
                        if (File.Exists(fullImagePath))
                        {
                            partition.FilePath = fullImagePath;
                            
                            // 尝试获取文件大小
                            try
                            {
                                FileInfo fileInfo = new FileInfo(fullImagePath);
                                long fileSizeBytes = fileInfo.Length;
                                
                                // 转换为可读的文件大小格式
                                string fileSize;
                                if (fileSizeBytes >= 1024 * 1024 * 1024)
                                {
                                    fileSize = $"{fileSizeBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
                                }
                                else if (fileSizeBytes >= 1024 * 1024)
                                {
                                    fileSize = $"{fileSizeBytes / (1024.0 * 1024.0):F2} MB";
                                }
                                else if (fileSizeBytes >= 1024)
                                {
                                    fileSize = $"{fileSizeBytes / 1024.0:F2} KB";
                                }
                                else
                                {
                                    fileSize = $"{fileSizeBytes} B";
                                }
                                
                                partition.PartitionSize = fileSize;
                            }
                            catch
                            {
                                partition.PartitionSize = "无法获取";
                            }
                        }
                        else
                        {
                            // 文件不存在，标记为缺失
                            partition.PartitionSize = "文件缺失";
                            partition.FilePath = fullImagePath + " (缺失)";
                        }
                    }
                }
                
                // 刷新数据网格显示
                PartitionTableDataGrid.Items.Refresh();
                
                // 自动勾选所有分区
                foreach (var partition in allPartitions)
                {
                    partition.IsSelected = true;
                }
                ApplyFastbootVisualizationPartitionProtectionState(adbMode: false);
                 
                // 根据脚本内容和文件名自动勾选相应的复选框
                AutoSelectCheckBoxes(scriptPath, lines);
                _parsedXiaomiFlashScriptPath = Path.GetFullPath(scriptPath);
                _parsedXiaomiFlashScriptLines = lines;
                return true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"解析小米线刷脚本时出错：{ex.Message}", "解析错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void AutoSelectCheckBoxes(string scriptPath, string[] lines)
        {
            try
            {
                // 始终自动勾选"自动重启"复选框
                RestartCheckBox.IsChecked = true;
                
                // 获取脚本文件名
                string scriptFileName = Path.GetFileName(scriptPath);
                
                // 如果脚本名为flash_all.bat，自动勾选"清除数据"
                if (string.Equals(scriptFileName, "flash_all.bat", StringComparison.OrdinalIgnoreCase))
                {
                    KeepUserDataCheckBox.IsChecked = true;
                }
                
                // 检查脚本内容
                string scriptContent = string.Join(" ", lines).ToLower();
                
                // 如果脚本中有"set_active a"字样，自动勾选"切换A槽"
                if (scriptContent.Contains("set_active a"))
                {
                    SwitchSlotACheckBox.IsChecked = true;
                }
                
                // 如果脚本中有"oem lock"字样，自动勾选"锁定BL"和"清除数据"
                if (scriptContent.Contains("oem lock"))
                {
                    LockBootloaderCheckBox.IsChecked = true;
                    KeepUserDataCheckBox.IsChecked = true;
                }
            }
            catch (Exception ex)
            {
                // 如果自动勾选过程中出现错误，记录但不影响主流程
                System.Diagnostics.Debug.WriteLine($"自动勾选复选框时出错：{ex.Message}");
            }
        }

        private async Task<bool> ExecuteXiaomiFlashScript(
            string scriptFilePath,
            System.Windows.Controls.RichTextBox logTextBox,
            IReadOnlyCollection<PartitionInfo> availablePartitions,
            XiaomiFlashMode flashMode,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(scriptFilePath))
                {
                    logTextBox.AppendText("[Error] 所选小米线刷脚本不存在\n");
                    logTextBox.ScrollToEnd();
                    return false;
                }

                string? scriptPath = Path.GetDirectoryName(scriptFilePath);
                if (string.IsNullOrWhiteSpace(scriptPath))
                {
                    LogToFastboot("无法确定脚本所在目录", "Red");
                    return false;
                }

                string normalizedScriptPath = Path.GetFullPath(scriptFilePath);
                if (_parsedXiaomiFlashScriptLines == null ||
                    !string.Equals(_parsedXiaomiFlashScriptPath, normalizedScriptPath, StringComparison.OrdinalIgnoreCase))
                {
                    LogToFastboot("脚本尚未解析或已发生变化，请重新选择 BAT 脚本", "Red");
                    return false;
                }
                
                // 直接执行选择脚本时已经解析并缓存的命令，不在点击写入后重复解析。
                return await ParseAndExecuteFastbootCommands(
                    _parsedXiaomiFlashScriptLines,
                    scriptPath,
                    logTextBox,
                    availablePartitions,
                    flashMode,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logTextBox.AppendText($"[Error] 执行小米线刷脚本失败: {ex.Message}\n");
                logTextBox.ScrollToEnd();
                return false;
            }
        }
        
        private async Task<bool> ParseAndExecuteFastbootCommands(
            string[] lines,
            string scriptPath,
            System.Windows.Controls.RichTextBox logTextBox,
            IReadOnlyCollection<PartitionInfo> availablePartitions,
            XiaomiFlashMode flashMode,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fastbootPath = GetToolPath("fastboot.exe");
            if (string.IsNullOrEmpty(fastbootPath))
            {
                LogToFastboot("缺少 fastboot.exe，无法执行刷写", "Red");
                return false;
            }
            
            string imagesPath = Path.Combine(scriptPath, "images");
            if (!Directory.Exists(imagesPath))
            {
                LogToFastboot("脚本目录中未找到 images 文件夹", "Red");
                return false;
            }

            // 写入分区按钮必须至少有一条仍被用户选中的 flash 任务。
            // 先做此检查，防止所有分区都取消后仍执行脚本中的 erase/oem 等命令。
            bool hasSelectedFlashCommand = lines.Any(line =>
            {
                Match match = Regex.Match(line, @"\bflash\s+([^\s""']+)", RegexOptions.IgnoreCase);
                return match.Success &&
                       FindPartitionSelectionForScript(match.Groups[1].Value.Trim(), availablePartitions)?.IsSelected == true;
            });
            if (!hasSelectedFlashCommand)
            {
                LogToFastboot("没有选中可执行的脚本刷写任务", "Red");
                return false;
            }

            Stopwatch partitionWriteStopwatch = Stopwatch.StartNew();
            bool foundFlashCommand = false;
            bool allFlashCommandsSucceeded = true;
            bool allAdditionalCommandsSucceeded = true;
            int failedPartitionCount = 0;
            int executedFlashCommandCount = 0;
            bool? shouldFlashCrcPartitions = null;
            foreach (string line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string trimmedLine = line.Trim();
                
                // 跳过注释和空行
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("::") || trimmedLine.StartsWith("@echo"))
                    continue;
                
                // 按 BAT 顺序执行 erase、flash 和允许的 OEM 命令。
                // metadata/userdata、切槽、锁BL和重启仍由页面复选框统一控制，避免脚本绕过用户选择。
                if (trimmedLine.StartsWith("fastboot", StringComparison.OrdinalIgnoreCase))
                {
                    Match eraseMatch = Regex.Match(trimmedLine, @"\berase\s+([^\s""']+)", RegexOptions.IgnoreCase);
                    if (eraseMatch.Success)
                    {
                        string erasePartition = eraseMatch.Groups[1].Value.Trim();
                        // metadata/userdata 属于页面“清除数据”选项，避免脚本与后置逻辑重复执行。
                        if (erasePartition.Equals("metadata", StringComparison.OrdinalIgnoreCase) ||
                            erasePartition.Equals("userdata", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        PartitionInfo? erasePartitionInfo = FindPartitionSelectionForScript(erasePartition, availablePartitions);
                        if (erasePartitionInfo != null && !erasePartitionInfo.IsSelected)
                        {
                            continue;
                        }

                        string eraseTarget = GetXiaomiFlashTargetPartition(erasePartition, flashMode);
                        LogFastbootEraseBegin(eraseTarget);
                        bool eraseSucceeded = await ExecuteSingleFastbootCommand(
                            fastbootPath,
                            $"erase {eraseTarget}",
                            logTextBox,
                            cancellationToken: CancellationToken.None);
                        if (eraseSucceeded)
                        {
                            LogFastbootEraseEndOk(eraseTarget);
                        }
                        else
                        {
                            allAdditionalCommandsSucceeded = false;
                            LogFastbootEraseEndFail(eraseTarget);
                        }
                        continue;
                    }

                    Match oemMatch = Regex.Match(trimmedLine, @"\boem\s+([^\s|&]+)", RegexOptions.IgnoreCase);
                    if (oemMatch.Success && oemMatch.Groups[1].Value.Equals("cdms", StringComparison.OrdinalIgnoreCase))
                    {
                        LogToFastboot("[OEM] 执行 cdms...", "Black");
                        if (!await ExecuteSingleFastbootCommand(
                                fastbootPath,
                                "oem cdms",
                                logTextBox,
                                cancellationToken: CancellationToken.None))
                        {
                            allAdditionalCommandsSucceeded = false;
                            LogToFastboot("OEM cdms 执行失败", "Red");
                        }
                        else
                        {
                            LogToFastboot("[OEM] cdms...OK", "Green");
                        }
                        continue;
                    }

                    Match flashMatch = Regex.Match(trimmedLine, @"\bflash\s+([^\s""']+)", RegexOptions.IgnoreCase);
                    if (!flashMatch.Success)
                    {
                        continue;
                    }

                    string partitionName = flashMatch.Groups[1].Value.Trim();
                    PartitionInfo? matchingPartition = FindPartitionSelectionForScript(partitionName, availablePartitions);
                    if (matchingPartition?.IsSelected != true)
                    {
                        continue;
                    }

                    foundFlashCommand = true;
                    if (IsCrcListPartition(partitionName))
                    {
                        if (!shouldFlashCrcPartitions.HasValue)
                        {
                            if (SkipCrcCheckBox?.IsChecked == true)
                            {
                                shouldFlashCrcPartitions = false;
                                LogToFastboot("已按选项跳过 CRC 分区：crclist、sparsecrclist", "Black");
                            }
                            else
                            {
                                shouldFlashCrcPartitions = await DeviceRequiresCrcPartitionsAsync(
                                    fastbootPath,
                                    cancellationToken);
                                if (shouldFlashCrcPartitions != true)
                                {
                                    LogToFastboot("设备未返回 crc: 1，已跳过 crclist、sparsecrclist", "Black");
                                }
                            }
                        }

                        if (shouldFlashCrcPartitions != true)
                        {
                            continue;
                        }
                    }

                    string? sourceFileName = Path.GetFileName(matchingPartition.FilePath);
                    string targetPartitionName = GetXiaomiFlashTargetPartition(partitionName, flashMode);
                    string effectiveCommandLine = ReplaceFastbootPartitionArgument(
                        trimmedLine,
                        "flash",
                        targetPartitionName);
                    executedFlashCommandCount++;
                    LogFastbootWriteBegin(sourceFileName, targetPartitionName);
                    bool commandSucceeded = await ExecuteFastbootCommandFromScript(
                        effectiveCommandLine,
                        scriptPath,
                        fastbootPath,
                        logTextBox,
                        targetPartitionName,
                        sourceFileName,
                        CancellationToken.None);
                    if (!commandSucceeded)
                    {
                        allFlashCommandsSucceeded = false;
                        failedPartitionCount++;
                        LogFastbootWriteEndFail(targetPartitionName, sourceFileName);
                    }
                    else
                    {
                        LogFastbootWriteEndOk(targetPartitionName, sourceFileName);
                    }
                }
            }

            partitionWriteStopwatch.Stop();
            cancellationToken.ThrowIfCancellationRequested();

            if (!foundFlashCommand)
            {
                LogToFastboot("所选脚本中未找到可执行的 fastboot flash 命令", "Red");
                return false;
            }

            if (executedFlashCommandCount == 0)
            {
                LogToFastboot("所选分区均已按条件跳过，没有执行刷写；后置操作已取消", "Yellow");
                return false;
            }

            long elapsedSeconds = Math.Max(0, (long)Math.Ceiling(partitionWriteStopwatch.Elapsed.TotalSeconds));
            LogToFastboot(
                $"写入完成，写入失败分区{failedPartitionCount}个，耗时{elapsedSeconds}秒",
                allFlashCommandsSucceeded ? "Green" : "Orange");
            
            // 按照优先级顺序执行复选框对应的命令：清除数据 -> 切换A槽 -> 锁定BL -> 自动重启
            if (!allFlashCommandsSucceeded)
            {
                LogToFastboot("脚本存在刷写失败项，锁BL操作将被强制跳过", "Yellow");
            }

            bool postActionsSucceeded = await ExecutePostFlashCommands(
                fastbootPath,
                logTextBox,
                allowBootloaderLock: allFlashCommandsSucceeded && allAdditionalCommandsSucceeded,
                forceSwitchSlotA: flashMode == XiaomiFlashMode.SlotA,
                cancellationToken: cancellationToken);
            return allFlashCommandsSucceeded && allAdditionalCommandsSucceeded && postActionsSucceeded;
        }

        private static string GetXiaomiFlashTargetPartition(
            string partitionName,
            XiaomiFlashMode flashMode)
        {
            if (flashMode == XiaomiFlashMode.SlotA &&
                partitionName.EndsWith("_ab", StringComparison.OrdinalIgnoreCase))
            {
                return partitionName.Substring(0, partitionName.Length - 3) + "_a";
            }

            return partitionName;
        }

        private static string ReplaceFastbootPartitionArgument(
            string commandLine,
            string command,
            string targetPartition)
        {
            Match match = Regex.Match(
                commandLine,
                $@"\b{Regex.Escape(command)}\s+(?<partition>[^\s""']+)",
                RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return commandLine;
            }

            System.Text.RegularExpressions.Group partitionGroup = match.Groups["partition"];
            return commandLine.Remove(partitionGroup.Index, partitionGroup.Length)
                              .Insert(partitionGroup.Index, targetPartition);
        }

        private static bool IsCrcListPartition(string partitionName)
        {
            return partitionName.Equals("crclist", StringComparison.OrdinalIgnoreCase) ||
                   partitionName.Equals("sparsecrclist", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<bool> DeviceRequiresCrcPartitionsAsync(
            string fastbootPath,
            CancellationToken cancellationToken)
        {
            string selectedSerial = GetSelectedDeviceSerial();
            string arguments = string.IsNullOrWhiteSpace(selectedSerial)
                ? "getvar crc"
                : $"-s {selectedSerial} getvar crc";
            string output = await GetCommandOutput(fastbootPath, arguments, cancellationToken);
            return Regex.IsMatch(
                output ?? string.Empty,
                @"^\s*(?:\(bootloader\)\s*)?crc:\s*1\b",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);
        }

        private static PartitionInfo? FindPartitionSelectionForScript(
            string scriptPartitionName,
            IReadOnlyCollection<PartitionInfo> availablePartitions)
        {
            PartitionInfo? exactMatch = availablePartitions.FirstOrDefault(partition =>
                partition.PartitionName.Equals(scriptPartitionName, StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null)
            {
                return exactMatch;
            }

            string normalizedName = scriptPartitionName;
            if (scriptPartitionName.EndsWith("_a", StringComparison.OrdinalIgnoreCase) ||
                scriptPartitionName.EndsWith("_b", StringComparison.OrdinalIgnoreCase))
            {
                normalizedName = scriptPartitionName.Substring(0, scriptPartitionName.Length - 2);
                return availablePartitions.FirstOrDefault(partition =>
                    partition.PartitionName.Equals($"{normalizedName}_ab", StringComparison.OrdinalIgnoreCase) ||
                    partition.PartitionName.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));
            }

            if (scriptPartitionName.EndsWith("_ab", StringComparison.OrdinalIgnoreCase))
            {
                normalizedName = scriptPartitionName.Substring(0, scriptPartitionName.Length - 3);
                return availablePartitions.FirstOrDefault(partition =>
                    partition.PartitionName.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));
            }

            return availablePartitions.FirstOrDefault(partition =>
                partition.PartitionName.Equals($"{scriptPartitionName}_ab", StringComparison.OrdinalIgnoreCase));
        }

        private async Task<bool> ExecutePostFlashCommands(
            string fastbootPath,
            System.Windows.Controls.RichTextBox logTextBox,
            bool allowBootloaderLock = true,
            bool allowScriptOnlyActions = true,
            bool forceSwitchSlotA = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (allowScriptOnlyActions && KeepUserDataCheckBox.IsChecked == true)
                {
                    LogToFastboot("[Erase] 开始清除数据...");
                    
                    // 执行 fastboot erase metadata
                    if (!await ExecuteSingleFastbootCommand(
                            fastbootPath,
                            "erase metadata",
                            logTextBox,
                            cancellationToken: CancellationToken.None))
                    {
                        LogToFastboot("清除 metadata 失败，已停止后续操作", "Red");
                        return false;
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    // 执行 fastboot erase userdata
                    if (!await ExecuteSingleFastbootCommand(
                            fastbootPath,
                            "erase userdata",
                            logTextBox,
                            cancellationToken: CancellationToken.None))
                    {
                        LogToFastboot("清除 userdata 失败，已停止后续操作", "Red");
                        return false;
                    }
                    
                    LogToFastboot("[Done] 数据清除完成...");
                }

                cancellationToken.ThrowIfCancellationRequested();
                
                // 2. 切换A槽
                if (allowScriptOnlyActions &&
                    (forceSwitchSlotA || SwitchSlotACheckBox.IsChecked == true))
                {
                    LogToFastboot("正在切换到A槽...");
                    if (!await ExecuteSingleFastbootCommand(
                            fastbootPath,
                            "set_active a",
                            logTextBox,
                            showSuccessfulNativeOutput: true,
                            cancellationToken: CancellationToken.None))
                    {
                        LogToFastboot("切换A槽失败，已停止后续操作", "Red");
                        return false;
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                
                // 3. 锁定BL
                if (allowScriptOnlyActions && LockBootloaderCheckBox.IsChecked == true)
                {
                    if (!allowBootloaderLock)
                    {
                        LogToFastboot("检测到分区刷写失败，为避免设备无法启动，已跳过锁定Bootloader", "Yellow");
                    }
                    else
                    {
                        LogToFastboot("[Lock] 锁定Bootloader...");
                        if (!await ExecuteSingleFastbootCommand(
                                fastbootPath,
                                "oem lock",
                                logTextBox,
                                cancellationToken: CancellationToken.None))
                        {
                            LogToFastboot("锁定Bootloader失败，已停止后续操作", "Red");
                            return false;
                        }
                        LogToFastboot("[Done] Bootloader已锁定");
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                
                // 4. 自动重启
                if (RestartCheckBox.IsChecked == true)
                {
                    LogToFastboot("正在重启设备...");
                    if (!await ExecuteSingleFastbootCommand(
                            fastbootPath,
                            "reboot",
                            logTextBox,
                            showSuccessfulNativeOutput: true,
                            cancellationToken: CancellationToken.None))
                    {
                        LogToFastboot("设备重启命令发送失败", "Red");
                        return false;
                    }
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogToFastboot($"执行刷写后操作失败: {ex.Message}", "Red");
                return false;
            }
        }
        
        private async Task<bool> ExecuteSingleFastbootCommand(
            string fastbootPath,
            string arguments,
            System.Windows.Controls.RichTextBox logTextBox,
            bool showSuccessfulNativeOutput = false,
            CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string selectedSerial = GetSelectedDeviceSerial();
                
                // 如果有选中的设备序列号，添加 -s 参数
                string finalArguments = arguments;
                if (!string.IsNullOrEmpty(selectedSerial))
                {
                    finalArguments = $"-s {selectedSerial} {arguments}";
                }
                
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = fastbootPath,
                    Arguments = finalArguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(fastbootPath)
                };
                
                using (Process process = new Process { StartInfo = startInfo })
                {
                    process.Start();

                    Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                    Task<string> errorTask = process.StandardError.ReadToEndAsync();
                    try
                    {
                        await process.WaitForExitAsync(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill(entireProcessTree: true);
                            }
                        }
                        catch
                        {
                        }
                        throw;
                    }
                    string output = await outputTask;
                    string error = await errorTask;
                    
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        fastbootCompleteLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] [FASTBOOT OUTPUT] {output.Trim()}");
                    }

                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        fastbootCompleteLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] [FASTBOOT ERROR] {error.Trim()}");
                    }

                    if (process.ExitCode != 0)
                    {
                        string nativeError = string.Join(
                            Environment.NewLine,
                            new[] { error?.Trim(), output?.Trim() }
                                .Where(text => !string.IsNullOrWhiteSpace(text)));
                        if (!string.IsNullOrWhiteSpace(nativeError))
                        {
                            LogToFastboot(nativeError, "Red");
                        }
                        LogToFastboot($"fastboot 退出代码：{process.ExitCode}", "Red");
                        return false;
                    }

                    if (showSuccessfulNativeOutput)
                    {
                        string nativeOutput = string.Join(
                            Environment.NewLine,
                            new[] { error?.Trim(), output?.Trim() }
                                .Where(text => !string.IsNullOrWhiteSpace(text)));
                        if (!string.IsNullOrWhiteSpace(nativeOutput))
                        {
                            LogToFastboot(nativeOutput, "Green");
                        }
                    }

                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogToFastboot($"[错误] 执行fastboot命令失败: {ex.Message}", "Red");
                return false;
            }
        }

        private Task<bool> ExecuteFastbootCommandFromScript(
            string commandLine,
            string scriptPath,
            string fastbootPath,
            System.Windows.Controls.RichTextBox logTextBox,
            string partitionName,
            string? sourceFileName,
            CancellationToken cancellationToken)
        {
            // 脚本刷写与手动分区刷写统一使用可视刷写日志处理器，
            // 避免状态被写入 BasicFlash 的 FlashLogTextBox。
            return ExecuteFastbootCommandFromScriptWithCustomLog(
                commandLine,
                scriptPath,
                fastbootPath,
                logTextBox,
                partitionName,
                sourceFileName,
                trackDetailedPartitionStatus: false,
                applyScriptVerificationOptions: true,
                cancellationToken: cancellationToken);
        }

        private void LogToFastboot(string message, string color = "Black")
        {
            Dispatcher.Invoke(() =>
            {
                string normalizedMessage = Regex.Replace(message ?? string.Empty, @"^\[(Done|Flash|Prepare|Warning|Error)\]\s*", string.Empty, RegexOptions.IgnoreCase);
                var paragraph = CreateFastbootLogParagraph();
                AppendFastbootTimestamp(paragraph);
                paragraph.Inlines.Add(new Run(normalizedMessage)
                {
                    Foreground = GetFastbootMessageBrush(color)
                });
                FastbootLogTextBox.Document.Blocks.Add(paragraph);
                FastbootLogTextBox.ScrollToEnd();
            });
        }

        private void LogToFastbootStyled(
            params (string Text, string Color, bool Emphasized)[] segments)
        {
            Dispatcher.Invoke(() =>
            {
                var paragraph = CreateFastbootLogParagraph();
                AppendFastbootTimestamp(paragraph);
                foreach ((string text, string color, bool emphasized) in segments)
                {
                    paragraph.Inlines.Add(new Run(text)
                    {
                        Foreground = GetFastbootMessageBrush(color),
                        FontWeight = emphasized ? FontWeights.SemiBold : FontWeights.Normal
                    });
                }

                FastbootLogTextBox.Document.Blocks.Add(paragraph);
                FastbootLogTextBox.ScrollToEnd();
            });
        }

        private void LogFastbootDeviceInfo(
            string productName,
            string unlockStatus,
            string slot,
            string mode)
        {
            Dispatcher.Invoke(() =>
            {
                var labelBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184));
                var valueBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139));

                void AddDeviceInfoLine(string label, string value)
                {
                    var line = CreateFastbootLogParagraph();
                    AppendFastbootTimestamp(line);
                    line.Inlines.Add(new Run(label)
                    {
                        Foreground = labelBrush,
                        FontWeight = FontWeights.SemiBold
                    });
                    line.Inlines.Add(new Run(string.IsNullOrWhiteSpace(value) ? "--" : value)
                    {
                        Foreground = valueBrush
                    });
                    FastbootLogTextBox.Document.Blocks.Add(line);
                }

                AddDeviceInfoLine("设备代号：", productName);
                AddDeviceInfoLine("解锁状态：", unlockStatus);
                AddDeviceInfoLine("当前槽位：", slot);
                AddDeviceInfoLine("运行模式：", mode);
                FastbootLogTextBox.ScrollToEnd();
            });
        }

        private void LogFastbootNativeError(string nativeOutput, int exitCode)
        {
            Dispatcher.Invoke(() =>
            {
                string[] errorLines = (nativeOutput ?? string.Empty)
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line =>
                        line.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("remote:", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("cannot", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("denied", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (errorLines.Length == 0)
                {
                    errorLines = new[] { $"fastboot exited with code {exitCode}" };
                }

                foreach (string errorLine in errorLines)
                {
                    var paragraph = CreateFastbootLogParagraph();
                    AppendFastbootTimestamp(paragraph);
                    paragraph.Inlines.Add(new Run("[Fastboot] ")
                    {
                        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38)),
                        FontWeight = FontWeights.SemiBold
                    });
                    paragraph.Inlines.Add(new Run(errorLine)
                    {
                        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38))
                    });
                    FastbootLogTextBox.Document.Blocks.Add(paragraph);
                }

                FastbootLogTextBox.ScrollToEnd();
            });
        }

        private static Paragraph CreateFastbootLogParagraph()
        {
            return new Paragraph
            {
                Margin = new Thickness(0, 0.5, 0, 0.5),
                LineHeight = 19
            };
        }

        private static SolidColorBrush GetFastbootMessageBrush(string color)
        {
            return color.ToLowerInvariant() switch
            {
                "red" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38)),
                "orange" or "yellow" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(217, 119, 6)),
                "green" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 163, 74)),
                "blue" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235)),
                "purple" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(124, 58, 237)),
                _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139))
            };
        }

        private static void AppendFastbootTimestamp(Paragraph paragraph)
        {
            paragraph.Inlines.Add(new Run($"[{DateTime.Now:HH:mm:ss} ] ")
            {
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184))
            });
        }

        private static string EnsureImgSuffix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "--";
            }

            return value.EndsWith(".img", StringComparison.OrdinalIgnoreCase) ? value : $"{value}.img";
        }

        private string GetFastbootWriteSourceDisplayName(string? sourceFileName, string partitionName)
        {
            string sourceDisplayName = !string.IsNullOrWhiteSpace(sourceFileName)
                ? IOPath.GetFileName(sourceFileName)
                : GetBasePartitionName(partitionName);

            if (string.IsNullOrWhiteSpace(sourceDisplayName))
            {
                sourceDisplayName = "--";
            }

            return EnsureImgSuffix(sourceDisplayName);
        }

        private string BuildFastbootWriteStepTitle(string sourceDisplayName, string partitionName)
        {
            return $"{sourceDisplayName} → {EnsureImgSuffix(partitionName)}";
        }

        private static string BuildFastbootReadStepTitle(string partitionName)
        {
            return $"{EnsureImgSuffix(partitionName)} → 指定目录";
        }

        private static string BuildFastbootEraseStepTitle(string partitionName)
        {
            return partitionName;
        }

        private void BeginFastbootPendingStep(Dictionary<string, Paragraph> pendingParagraphs, string key, string title)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                key = title;
            }

            if (pendingParagraphs.ContainsKey(key))
            {
                return;
            }

            string operation = GetFastbootOperationLabel(pendingParagraphs, title);
            var paragraph = CreateFastbootLogParagraph();
            paragraph.Tag = (operation, title);
            AppendFastbootTimestamp(paragraph);
            paragraph.Inlines.Add(new Run($"[{operation}] ")
            {
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(124, 58, 237)),
                FontWeight = FontWeights.SemiBold
            });
            paragraph.Inlines.Add(new Run(title) { Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85)) });
            paragraph.Inlines.Add(new Run(" ...")
            {
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184))
            });
            FastbootLogTextBox.Document.Blocks.Add(paragraph);
            FastbootLogTextBox.ScrollToEnd();
            pendingParagraphs[key] = paragraph;
        }

        private void EndFastbootPendingStepOk(Dictionary<string, Paragraph> pendingParagraphs, string key, string fallbackTitle)
        {
            EndFastbootPendingStep(pendingParagraphs, key, fallbackTitle, true);
        }

        private void EndFastbootPendingStepError(Dictionary<string, Paragraph> pendingParagraphs, string key, string fallbackTitle)
        {
            EndFastbootPendingStep(pendingParagraphs, key, fallbackTitle, false);
        }

        private void EndFastbootPendingStep(Dictionary<string, Paragraph> pendingParagraphs, string key, string fallbackTitle, bool success)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                key = fallbackTitle;
            }

            if (pendingParagraphs.TryGetValue(key, out var paragraph))
            {
                var step = paragraph.Tag is ValueTuple<string, string> stepInfo
                    ? stepInfo
                    : (GetFastbootOperationLabel(pendingParagraphs, fallbackTitle), fallbackTitle);
                RenderCompletedFastbootStep(paragraph, step.Item1, step.Item2, success);
                FastbootLogTextBox.ScrollToEnd();
                pendingParagraphs.Remove(key);
                return;
            }

            var newParagraph = CreateFastbootLogParagraph();
            RenderCompletedFastbootStep(newParagraph, GetFastbootOperationLabel(pendingParagraphs, fallbackTitle), fallbackTitle, success);
            FastbootLogTextBox.Document.Blocks.Add(newParagraph);
            FastbootLogTextBox.ScrollToEnd();
        }

        private string GetFastbootOperationLabel(Dictionary<string, Paragraph> pendingParagraphs, string title)
        {
            if (!ReferenceEquals(pendingParagraphs, _pendingFastbootReadParagraphs))
            {
                return "Flashing";
            }

            return title.Contains("指定目录", StringComparison.Ordinal) ||
                   title.Contains("PC端", StringComparison.Ordinal) ||
                   title.Contains("本地文件", StringComparison.Ordinal)
                ? "Reading"
                : "Erasing";
        }

        private static void RenderCompletedFastbootStep(Paragraph paragraph, string operation, string title, bool success)
        {
            paragraph.Inlines.Clear();
            AppendFastbootTimestamp(paragraph);
            paragraph.Inlines.Add(new Run($"[{operation}] ")
            {
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(124, 58, 237)),
                FontWeight = FontWeights.SemiBold
            });
            paragraph.Inlines.Add(new Run(title) { Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85)) });
            paragraph.Inlines.Add(new Run(success ? " ...OK" : " ...Error")
            {
                Foreground = new SolidColorBrush(success
                    ? System.Windows.Media.Color.FromRgb(22, 163, 74)
                    : System.Windows.Media.Color.FromRgb(220, 38, 38)),
                FontWeight = FontWeights.Bold
            });
        }

        private void CompleteFastbootPendingSteps(Dictionary<string, Paragraph> pendingParagraphs, bool success)
        {
            if (pendingParagraphs.Count <= 0)
            {
                return;
            }

            var pendingParagraphsSnapshot = pendingParagraphs.Values.ToArray();
            foreach (var paragraph in pendingParagraphsSnapshot)
            {
                var step = paragraph.Tag is ValueTuple<string, string> stepInfo
                    ? stepInfo
                    : ("Flashing", "partition");
                RenderCompletedFastbootStep(paragraph, step.Item1, step.Item2, success);
            }

            FastbootLogTextBox.ScrollToEnd();
            pendingParagraphs.Clear();
        }

        private void LogFastbootWriteBegin(string? sourceFileName, string partitionName)
        {
            Dispatcher.Invoke(() =>
            {
                string sourceDisplayName = GetFastbootWriteSourceDisplayName(sourceFileName, partitionName);
                BeginFastbootPendingStep(_pendingFastbootFlashParagraphs, partitionName, BuildFastbootWriteStepTitle(sourceDisplayName, partitionName));
            });
        }

        private void LogFastbootWriteEndOk(string partitionName, string? sourceFileName)
        {
            Dispatcher.Invoke(() =>
            {
                string sourceDisplayName = GetFastbootWriteSourceDisplayName(sourceFileName, partitionName);
                EndFastbootPendingStepOk(_pendingFastbootFlashParagraphs, partitionName, BuildFastbootWriteStepTitle(sourceDisplayName, partitionName));
            });
        }

        private void LogFastbootWriteEndFail(string partitionName, string? sourceFileName)
        {
            Dispatcher.Invoke(() =>
            {
                string sourceDisplayName = GetFastbootWriteSourceDisplayName(sourceFileName, partitionName);
                EndFastbootPendingStepError(_pendingFastbootFlashParagraphs, partitionName, BuildFastbootWriteStepTitle(sourceDisplayName, partitionName));
            });
        }

        private void LogFastbootEraseBegin(string partitionName)
        {
            Dispatcher.Invoke(() =>
            {
                BeginFastbootPendingStep(_pendingFastbootReadParagraphs, partitionName, BuildFastbootEraseStepTitle(partitionName));
            });
        }

        private void LogFastbootEraseEndOk(string partitionName)
        {
            Dispatcher.Invoke(() =>
            {
                EndFastbootPendingStepOk(_pendingFastbootReadParagraphs, partitionName, BuildFastbootEraseStepTitle(partitionName));
            });
        }

        private void LogFastbootEraseEndFail(string partitionName)
        {
            Dispatcher.Invoke(() =>
            {
                EndFastbootPendingStepError(_pendingFastbootReadParagraphs, partitionName, BuildFastbootEraseStepTitle(partitionName));
            });
        }

        private void LogFastbootReadBegin(string partitionName)
        {
            if (string.IsNullOrWhiteSpace(partitionName)) partitionName = "--";
            Dispatcher.Invoke(() =>
            {
                BeginFastbootPendingStep(_pendingFastbootReadParagraphs, partitionName, BuildFastbootReadStepTitle(partitionName));
            });
        }

        private void LogFastbootReadEndOk(string partitionName)
        {
            if (string.IsNullOrWhiteSpace(partitionName)) partitionName = "--";
            Dispatcher.Invoke(() =>
            {
                EndFastbootPendingStepOk(_pendingFastbootReadParagraphs, partitionName, BuildFastbootReadStepTitle(partitionName));
            });
        }

        private void LogFastbootReadEndFail(string partitionName)
        {
            if (string.IsNullOrWhiteSpace(partitionName)) partitionName = "--";
            Dispatcher.Invoke(() =>
            {
                EndFastbootPendingStepError(_pendingFastbootReadParagraphs, partitionName, BuildFastbootReadStepTitle(partitionName));
            });
        }

        private void DeviceDetectionToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Primitives.ToggleButton toggleButton)
            {
                return;
            }

            if (toggleButton.IsChecked == true)
            {
                HandleStartDeviceDetection();
            }
            else
            {
                HandleStopDeviceDetection();
            }
        }

        private void ToolSelfCheckToggle_Click(object sender, RoutedEventArgs e)
        {
            var toggleButton = sender as System.Windows.Controls.Primitives.ToggleButton;
            if (toggleButton != null && toggleButton.IsChecked == true)
            {
                try
                {
                    // 获取当前程序的目录
                    string currentDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
                    string checkFilePath = System.IO.Path.Combine(currentDirectory, "exe", "check.com");
                    
                    if (System.IO.File.Exists(checkFilePath))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = checkFilePath,
                            UseShellExecute = true
                        });
                    }
                    else
                    {
                        System.Windows.MessageBox.Show($"找不到文件: {checkFilePath}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"打开文件时出错: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }

        private void ToolSelfCheckToggle_Checked(object sender, RoutedEventArgs e)
        {
            // 工具自检开关被选中时的处理逻辑
            // 可以在这里添加需要的功能，比如显示状态信息等
        }

        private void BilibiliButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 使用默认浏览器打开哔哩哔哩链接
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://b23.tv/wk4Qp7a",
                    UseShellExecute = true
                });
                
                // 添加日志消息
                AddLogMessage("系统", "已打开哔哩哔哩主页");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"打开链接时出错: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                AddLogMessage("错误", $"打开哔哩哔哩链接失败: {ex.Message}");
            }
        }

        private void CoolApkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 使用默认浏览器打开酷安链接
                Process.Start(new ProcessStartInfo
                {
                    FileName = "http://www.coolapk.com/u/33872028",
                    UseShellExecute = true
                });
                
                // 添加日志消息
                AddLogMessage("系统", "已打开酷安主页");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"打开链接时出错: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                AddLogMessage("错误", $"打开酷安链接失败: {ex.Message}");
            }
        }

        private void GitHubButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 使用默认浏览器打开GitHub链接
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/Smart-Paocai/SmartTool",
                    UseShellExecute = true
                });
                
                // 添加日志消息
                AddLogMessage("系统", "已打开GitHub仓库");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"打开链接时出错: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                AddLogMessage("错误", $"打开GitHub链接失败: {ex.Message}");
            }
        }

        private void RefreshDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DeviceDetectionToggle.IsChecked = false;
                HandleStopDeviceDetection();
                DeviceDetectionToggle.IsChecked = true;
                HandleStartDeviceDetection();
                AddLogMessage("系统", "设备状态已刷新");
            }
            catch (Exception ex)
            {
                AddLogMessage("错误", $"刷新设备状态时出错: {ex.Message}");
            }
        }

        private void WirelessDebugButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new Window1();
                win.Owner = this;
                win.Show();
                AddLogMessage("系统", "已打开无线调试窗口");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"打开无线调试窗口时出错: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                AddLogMessage("错误", $"打开无线调试窗口失败: {ex.Message}");
            }
        }

        private async void RebootCommandComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = sender as System.Windows.Controls.ComboBox;
            if (comboBox == null) return;

            var selectedItem = comboBox.SelectedItem as ComboBoxItem;
            if (selectedItem == null) return;

            string selectedCommand = selectedItem.Content?.ToString() ?? "";

            // 如果选择的是默认项，不执行任何操作
            if (selectedCommand == "更多重启指令↓")
            {
                return;
            }

            try
            {
                // 根据选择的命令执行相应操作
                switch (selectedCommand)
                {
                    case "adb reboot sideload":
                        AddLogMessage("系统", "正在执行: adb reboot sideload");
                        await ExecuteAdbCommand("reboot sideload");
                        break;

                    case "fastboot oem edl":
                        AddLogMessage("系统", "正在执行: fastboot oem edl");
                        await ExecuteFastbootCommand("oem edl");
                        break;

                    case "adb shell reboot -p":
                        AddLogMessage("系统", "正在执行: adb shell reboot -p");
                        await ExecuteAdbCommand("shell reboot -p");
                        break;
                }

                // 执行完命令后，将ComboBox重置为默认选项
                comboBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                AddLogMessage("错误", $"执行命令失败: {ex.Message}");
                // 出错时也重置为默认选项
                comboBox.SelectedIndex = 0;
            }
        }

        private void LinkGuideTextBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // 使用默认浏览器打开指定链接
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://violettool.top/",
                    UseShellExecute = true
                });
                
                // 添加日志消息
                AddLogMessage("系统", "已打开链接指南页面");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"打开链接时出错: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                AddLogMessage("错误", $"打开链接指南失败: {ex.Message}");
            }
        }
    }

    // 设备信息数据模型
    public class DeviceInfo
    {
        public string Model { get; set; } = "";
        public string Version { get; set; } = "";
        public string Type { get; set; } = "";
        public string? DownloadUrl { get; set; } // 匹配JSON中的DownloadUrl字段
    }

    // Autoroot功能辅助方法
    public partial class MainWindow
    {
        private void InitializeAutorootPaths()
        {
            // 设置magiskboot.exe路径
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            magiskbootPath = IOPath.Combine(appDir, "magiskboot.exe");

            // 创建临时目录
            tempDir = IOPath.Combine(IOPath.GetTempPath(), "AutoRoot_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);

            // 检查magiskboot.exe是否存在
            if (!IOFile.Exists(magiskbootPath))
            {
                AppendAutorootLog($"未找到magiskboot.exe文件: {magiskbootPath}");
            }
        }

        private System.Windows.Documents.Paragraph? _autorootFastbootWaitParagraph;
        private System.Windows.Documents.Run? _autorootFastbootWaitRun;

        private void BeginAutorootFastbootWaitCountdown(int seconds)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => BeginAutorootFastbootWaitCountdown(seconds));
                return;
            }

            var richTextBox = this.FindName("txtLog") as System.Windows.Controls.RichTextBox;
            if (richTextBox == null) return;
            _autorootFastbootWaitParagraph = new System.Windows.Documents.Paragraph
            {
                Margin = new System.Windows.Thickness(0, 1, 0, 1),
                LineHeight = 19
            };
            _autorootFastbootWaitParagraph.Inlines.Add(new System.Windows.Documents.Run($"{DateTime.Now:HH:mm:ss}")
            {
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184)),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11
            });
            _autorootFastbootWaitParagraph.Inlines.Add(new System.Windows.Documents.Run("    "));
            _autorootFastbootWaitRun = new System.Windows.Documents.Run
            {
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 41, 59))
            };
            _autorootFastbootWaitParagraph.Inlines.Add(_autorootFastbootWaitRun);
            richTextBox.Document.Blocks.Add(_autorootFastbootWaitParagraph);
            UpdateAutorootFastbootWaitCountdown(seconds);
        }

        private void UpdateAutorootFastbootWaitCountdown(int seconds)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateAutorootFastbootWaitCountdown(seconds));
                return;
            }

            if (_autorootFastbootWaitRun == null) return;
            _autorootFastbootWaitRun.Text = $"等待Fastboot设备...{seconds}s （如果卡在这里请检查数据线或驱动）";
            (this.FindName("txtLog") as System.Windows.Controls.RichTextBox)?.ScrollToEnd();
        }

        private void EndAutorootFastbootWaitCountdown()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(EndAutorootFastbootWaitCountdown);
                return;
            }

            _autorootFastbootWaitParagraph = null;
            _autorootFastbootWaitRun = null;
        }

        private void AppendAutorootLog(string message, string color = "Black")
        {
            if (Dispatcher.CheckAccess())
            {
                var richTextBox = this.FindName("txtLog") as System.Windows.Controls.RichTextBox;
                if (richTextBox != null)
                {
                    string normalized = color?.Trim().ToLowerInvariant() ?? "black";
                    bool warningMessage = message.Contains("失败", StringComparison.OrdinalIgnoreCase) ||
                                          message.Contains("无法", StringComparison.OrdinalIgnoreCase) ||
                                          message.Contains("取消", StringComparison.OrdinalIgnoreCase) ||
                                          message.Contains("仅支持", StringComparison.OrdinalIgnoreCase) ||
                                          message.Contains("无可用", StringComparison.OrdinalIgnoreCase) ||
                                          message.Contains("请手动", StringComparison.OrdinalIgnoreCase);
                    System.Windows.Media.Brush messageBrush = normalized == "red"
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(185, 28, 28))
                        : normalized == "yellow" && warningMessage
                            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 83, 9))
                            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 41, 59));

                    var paragraph = new System.Windows.Documents.Paragraph
                    {
                        Margin = new System.Windows.Thickness(0, 1, 0, 1),
                        LineHeight = 19
                    };
                    paragraph.Inlines.Add(new System.Windows.Documents.Run($"{DateTime.Now:HH:mm:ss}")
                    {
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184)),
                        FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                        FontSize = 11
                    });
                    paragraph.Inlines.Add(new System.Windows.Documents.Run("    "));

                    int separator = message.IndexOf(':');
                    if (separator < 0) separator = message.IndexOf('：');
                    if (message.EndsWith("...OK", StringComparison.Ordinal))
                    {
                        paragraph.Inlines.Add(new System.Windows.Documents.Run(message[..^2])
                        {
                            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 41, 59))
                        });
                        paragraph.Inlines.Add(new System.Windows.Documents.Run("OK")
                        {
                            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 155, 109)),
                            FontWeight = FontWeights.SemiBold
                        });
                    }
                    else if (separator > 0 && separator < 24)
                    {
                        paragraph.Inlines.Add(new System.Windows.Documents.Run(message[..(separator + 1)] + " ")
                        {
                            Foreground = messageBrush,
                            FontWeight = FontWeights.SemiBold
                        });
                        paragraph.Inlines.Add(new System.Windows.Documents.Run(message[(separator + 1)..].TrimStart())
                        {
                            Foreground = messageBrush
                        });
                    }
                    else
                    {
                        paragraph.Inlines.Add(new System.Windows.Documents.Run(message)
                        {
                            Foreground = messageBrush,
                            FontWeight = normalized == "red" ? FontWeights.SemiBold : FontWeights.Normal
                        });
                    }

                    richTextBox.Document.Blocks.Add(paragraph);
                    richTextBox.ScrollToEnd();
                }
            }
            else
            {
                Dispatcher.Invoke(() => AppendAutorootLog(message, color));
            }
        }

        private void ConnectionGuideTextBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://violettool.top/tutorials/connect.html",
                    UseShellExecute = true
                });

                AddLogMessage("系统", "已打开连接指南页面");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"打开连接指南时出错: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                AddLogMessage("错误", $"打开连接指南失败: {ex.Message}");
            }
        }

        private void Usb3PatchTextBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            string batchPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "exe",
                "fix_usb3.bat");

            if (!File.Exists(batchPath))
            {
                System.Windows.MessageBox.Show(
                    $"未找到 USB3.0 补丁文件：\n{batchPath}",
                    "文件缺失",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                AddLogMessage("错误", "未找到USB3.0补丁文件");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{batchPath}\"\"",
                    WorkingDirectory = Path.GetDirectoryName(batchPath) ?? AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = true,
                    Verb = "runas"
                });

                AddLogMessage("系统", "已启动USB3.0兼容性补丁");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                AddLogMessage("系统", "用户取消运行USB3.0兼容性补丁");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"运行 USB3.0 补丁时出错：{ex.Message}",
                    "运行失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                AddLogMessage("错误", $"运行USB3.0补丁失败: {ex.Message}");
            }
        }

        private void AppendAutorootStage(string title, string detail = "")
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendAutorootStage(title, detail));
                return;
            }

            var richTextBox = this.FindName("txtLog") as System.Windows.Controls.RichTextBox;
            if (richTextBox == null) return;
            var paragraph = new System.Windows.Documents.Paragraph
            {
                Margin = new System.Windows.Thickness(0, 7, 0, 4),
                Padding = new System.Windows.Thickness(9, 5, 9, 5),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 246, 248))
            };
            paragraph.Inlines.Add(new System.Windows.Documents.Run(title)
            {
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(71, 85, 105)),
                FontWeight = FontWeights.SemiBold
            });
            if (!string.IsNullOrWhiteSpace(detail))
            {
                paragraph.Inlines.Add(new System.Windows.Documents.Run("    " + detail)
                {
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139)),
                    FontSize = 11
                });
            }
            richTextBox.Document.Blocks.Add(paragraph);
            richTextBox.ScrollToEnd();
        }

        private async Task<bool> ExecuteMagiskPatchAsync(string bootPath, string outputPath, string magiskPath)
        {
            try
            {
                AppendAutorootLog("开始Magisk修补过程...");
                AppendAutorootLog($"Boot文件: {bootPath}");
                AppendAutorootLog($"输出路径: {outputPath}");
                AppendAutorootLog($"Magisk包: {magiskPath}");

                // 创建临时工作目录
                string workDir = IOPath.Combine(tempDir, "magisk_work");
                if (Directory.Exists(workDir))
                {
                    Directory.Delete(workDir, true);
                }
                Directory.CreateDirectory(workDir);
                AppendAutorootLog($"创建工作目录: {workDir}");

                // 步骤1: 从Magisk APK中提取必要文件
                AppendAutorootLog("步骤1: 从Magisk APK中提取必要文件...");
                if (!await ExtractMagiskFiles(magiskPath, workDir))
                {
                    AppendAutorootLog("提取Magisk文件失败");
                    return false;
                }

                // 步骤2: 解包boot镜像
                AppendAutorootLog("步骤2: 解包boot镜像...");
                if (!await RunMagiskbootCommand($"unpack \"{bootPath}\"", workDir))
                {
                    AppendAutorootLog("解包boot镜像失败");
                    return false;
                }

                // 检查ramdisk.cpio是否存在
                string ramdiskPath = IOPath.Combine(workDir, "ramdisk.cpio");
                if (!File.Exists(ramdiskPath))
                {
                    AppendAutorootLog("未找到ramdisk.cpio文件");
                    return false;
                }

                // 步骤3: 测试ramdisk状态
                AppendAutorootLog("步骤3: 测试ramdisk状态...");
                await RunMagiskbootCommand($"cpio \"{ramdiskPath}\" test", workDir);

                // 步骤4: 备份原始ramdisk
                AppendAutorootLog("步骤4: 备份原始ramdisk...");
                string ramdiskOrigPath = IOPath.Combine(workDir, "ramdisk.cpio.orig");
                File.Copy(ramdiskPath, ramdiskOrigPath, true);

                // 步骤5: 创建Magisk配置文件
                AppendAutorootLog("步骤5: 创建Magisk配置文件...");
                await CreateMagiskConfig(workDir, bootPath);

                // 步骤6: 将Magisk文件添加到ramdisk
                AppendAutorootLog("步骤6: 将Magisk文件添加到ramdisk...");
                if (!await AddMagiskToRamdisk(workDir))
                {
                    AppendAutorootLog("添加Magisk文件到ramdisk失败");
                    return false;
                }

                // 步骤7: 重新打包boot镜像
                AppendAutorootLog("步骤7: 重新打包boot镜像...");
                string repackArgs = $"repack \"{bootPath}\" \"{outputPath}\"";
                
                // 如果需要修补vbmeta标志
                var repackEnvVars = new Dictionary<string, string>();
                var chkPatchVbmeta = this.FindName("chkPatchVbmeta") as System.Windows.Controls.CheckBox;
                if (chkPatchVbmeta?.IsChecked == true)
                {
                    repackEnvVars["PATCHVBMETAFLAG"] = "true";
                    AppendAutorootLog("修补vbmeta标志");
                }

                if (!await RunMagiskbootCommandWithEnv(repackArgs, workDir, repackEnvVars))
                {
                    AppendAutorootLog("重新打包boot镜像失败");
                    return false;
                }

                // 检查输出文件是否存在
                if (File.Exists(outputPath))
                {
                    AppendAutorootLog("修补完成！", "green");
                    AppendAutorootLog($"输出文件已保存到: {outputPath}");
                    
                    // 清理工作目录
                    try
                    {
                        Directory.Delete(workDir, true);
                        AppendAutorootLog("清理临时文件完成");
                    }
                    catch
                    {
                        AppendAutorootLog("清理临时文件时出现警告，但不影响修补结果");
                    }
                    
                    return true;
                }
                else
                {
                    AppendAutorootLog("修补失败：输出文件未生成");
                    return false;
                }
            }
            catch (Exception ex)
            {
                AppendAutorootLog($"修补过程中发生错误: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> ExtractMagiskFiles(string magiskPath, string workDir)
        {
            try
            {
                // 获取处理器架构
                string arch = "arm64-v8a"; // 默认arm64
                var cbArchitecture = this.FindName("cbArchitecture") as System.Windows.Controls.ComboBox;
                if (cbArchitecture?.SelectedItem != null)
                {
                    string selectedArch = ((System.Windows.Controls.ComboBoxItem)cbArchitecture.SelectedItem).Content?.ToString() ?? "";
                    switch (selectedArch)
                    {
                        case "arm_64": arch = "arm64-v8a"; break;
                        case "arm_32": arch = "armeabi-v7a"; break;
                        case "x86_64": arch = "x86_64"; break;
                        case "x86_32": arch = "x86"; break;
                        case "riscv_64": arch = "riscv64"; break;
                    }
                }

                AppendAutorootLog($"目标架构: {arch}");

                // 使用.NET的ZipFile类提取文件
                using (var archive = System.IO.Compression.ZipFile.OpenRead(magiskPath))
                {
                    // 提取libmagiskinit.so
                    var magiskInitEntry = archive.Entries.FirstOrDefault(e => e.FullName == $"lib/{arch}/libmagiskinit.so");
                    if (magiskInitEntry != null)
                    {
                        string magiskInitPath = IOPath.Combine(workDir, "magiskinit");
                        magiskInitEntry.ExtractToFile(magiskInitPath, true);
                        AppendAutorootLog("提取magiskinit成功");
                    }

                    // 提取libmagisk.so
                    var magiskEntry = archive.Entries.FirstOrDefault(e => e.FullName == $"lib/{arch}/libmagisk.so");
                    if (magiskEntry != null)
                    {
                        string magiskPath_local = IOPath.Combine(workDir, "libmagisk.so");
                        magiskEntry.ExtractToFile(magiskPath_local, true);
                        AppendAutorootLog("提取libmagisk.so成功");
                    }

                    // 提取stub.apk
                    var stubEntry = archive.Entries.FirstOrDefault(e => e.FullName == "assets/stub.apk");
                    if (stubEntry != null)
                    {
                        string stubPath = IOPath.Combine(workDir, "stub.apk");
                        stubEntry.ExtractToFile(stubPath, true);
                        AppendAutorootLog("提取stub.apk成功");
                    }

                    // 提取libinit-ld.so
                    var initLdEntry = archive.Entries.FirstOrDefault(e => e.FullName == $"lib/{arch}/libinit-ld.so");
                    if (initLdEntry != null)
                    {
                        string initLdPath = IOPath.Combine(workDir, "libinit-ld.so");
                        initLdEntry.ExtractToFile(initLdPath, true);
                        AppendAutorootLog("提取libinit-ld.so成功");
                    }
                }

                // 压缩文件为.xz格式
                await CompressMagiskFiles(workDir);

                return true;
            }
            catch (Exception ex)
            {
                AppendAutorootLog($"提取Magisk文件时发生错误: {ex.Message}");
                return false;
            }
        }

        private async Task CompressMagiskFiles(string workDir)
        {
            // 压缩libmagisk.so为magisk.xz
            string libMagiskPath = IOPath.Combine(workDir, "libmagisk.so");
            if (File.Exists(libMagiskPath))
            {
                await RunMagiskbootCommand($"compress=xz \"{libMagiskPath}\" \"{IOPath.Combine(workDir, "magisk.xz")}\"", workDir);
                AppendAutorootLog("压缩magisk.xz成功");
            }

            // 压缩stub.apk为stub.xz
            string stubPath = IOPath.Combine(workDir, "stub.apk");
            if (File.Exists(stubPath))
            {
                await RunMagiskbootCommand($"compress=xz \"{stubPath}\" \"{IOPath.Combine(workDir, "stub.xz")}\"", workDir);
                AppendAutorootLog("压缩stub.xz成功");
            }

            // 压缩libinit-ld.so为init-ld.xz
            string libInitLdPath = IOPath.Combine(workDir, "libinit-ld.so");
            if (File.Exists(libInitLdPath))
            {
                await RunMagiskbootCommand($"compress=xz \"{libInitLdPath}\" \"{IOPath.Combine(workDir, "init-ld.xz")}\"", workDir);
                AppendAutorootLog("压缩init-ld.xz成功");
            }
        }

        private async Task CreateMagiskConfig(string workDir, string bootPath)
        {
            try
            {
                string configPath = IOPath.Combine(workDir, "config");
                var configLines = new List<string>();

                var chkKeepVerity = this.FindName("chkKeepVerity") as System.Windows.Controls.CheckBox;
                var chkKeepForceEncrypt = this.FindName("chkKeepForceEncrypt") as System.Windows.Controls.CheckBox;

                // 添加配置选项
                configLines.Add($"KEEPVERITY={chkKeepVerity?.IsChecked == true}");
                configLines.Add($"KEEPFORCEENCRYPT={chkKeepForceEncrypt?.IsChecked == true}");
                configLines.Add($"RECOVERYMODE=false"); // 默认false
                
                // 计算SHA1
                string sha1 = await GetBootSha1(bootPath);
                if (!string.IsNullOrEmpty(sha1))
                {
                    configLines.Add($"SHA1={sha1}");
                }

                await File.WriteAllLinesAsync(configPath, configLines);
                AppendAutorootLog("创建Magisk配置文件成功");
            }
            catch (Exception ex)
            {
                AppendAutorootLog($"创建配置文件时发生错误: {ex.Message}");
            }
        }

        private async Task<string> GetBootSha1(string bootPath)
        {
            try
            {
                var result = await RunMagiskbootCommandWithOutput($"sha1 \"{bootPath}\"", IOPath.GetDirectoryName(bootPath) ?? "");
                return result?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private async Task<bool> AddMagiskToRamdisk(string workDir)
        {
            try
            {
                string ramdiskPath = IOPath.Combine(workDir, "ramdisk.cpio");
                
                // 检查必要文件是否存在
                string magiskInitPath = IOPath.Combine(workDir, "magiskinit");
                string magiskXzPath = IOPath.Combine(workDir, "magisk.xz");
                string stubXzPath = IOPath.Combine(workDir, "stub.xz");
                string initLdXzPath = IOPath.Combine(workDir, "init-ld.xz");
                string configPath = IOPath.Combine(workDir, "config");

                if (!File.Exists(magiskInitPath) || !File.Exists(magiskXzPath) || 
                    !File.Exists(stubXzPath) || !File.Exists(initLdXzPath) || !File.Exists(configPath))
                {
                    AppendAutorootLog("缺少必要的Magisk文件");
                    return false;
                }

                // 使用正确的Magisk修补命令 - 一次性执行所有操作
                string patchCommand = $"cpio \"{ramdiskPath}\" " +
                    $"\"add 0750 init magiskinit\" " +
                    $"\"mkdir 0750 overlay.d\" " +
                    $"\"mkdir 0750 overlay.d/sbin\" " +
                    $"\"add 0644 overlay.d/sbin/magisk.xz magisk.xz\" " +
                    $"\"add 0644 overlay.d/sbin/stub.xz stub.xz\" " +
                    $"\"add 0644 overlay.d/sbin/init-ld.xz init-ld.xz\" " +
                    $"\"patch\" " +
                    $"\"backup ramdisk.cpio.orig\" " +
                    $"\"mkdir 000 .backup\" " +
                    $"\"add 000 .backup/.magisk config\"";

                if (!await RunMagiskbootCommand(patchCommand, workDir))
                {
                    AppendAutorootLog("执行Magisk修补命令失败");
                    return false;
                }

                AppendAutorootLog("添加Magisk文件到ramdisk成功");
                return true;
            }
            catch (Exception ex)
            {
                AppendAutorootLog($"添加Magisk文件到ramdisk时发生错误: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> RunMagiskbootCommand(string arguments, string workingDirectory)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = magiskbootPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDirectory
                };

                using (Process? process = Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        string output = await process.StandardOutput.ReadToEndAsync();
                        string error = await process.StandardError.ReadToEndAsync();
                        
                        await process.WaitForExitAsync();
                        
                        if (!string.IsNullOrEmpty(output))
                        {
                            // 将输出信息记录为信息日志，不标记为错误
                            AppendAutorootLog($"[信息] {output.Trim()}");
                        }
                        
                        if (!string.IsNullOrEmpty(error))
                        {
                            // magiskboot的很多正常信息会输出到stderr，需要区分真正的错误
                            // 只有在进程退出码不为0时才标记为错误
                            if (process.ExitCode != 0)
                            {
                                AppendAutorootLog($"错误: {error.Trim()}");
                            }
                            else
                            {
                                // 正常的信息输出，不标记为错误
                                AppendAutorootLog($"[信息] {error.Trim()}");
                            }
                        }
                        
                        return process.ExitCode == 0;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                AppendAutorootLog($"执行magiskboot命令时发生错误: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> RunMagiskbootCommandWithEnv(string arguments, string workingDirectory, Dictionary<string, string> environmentVariables)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = magiskbootPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDirectory
                };

                foreach (var envVar in environmentVariables)
                {
                    startInfo.EnvironmentVariables[envVar.Key] = envVar.Value;
                }

                using (Process? process = Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        string output = await process.StandardOutput.ReadToEndAsync();
                        string error = await process.StandardError.ReadToEndAsync();
                        
                        await process.WaitForExitAsync();
                        
                        if (!string.IsNullOrEmpty(output))
                        {
                            // 将输出信息记录为信息日志，不标记为错误
                            AppendAutorootLog($"[信息] {output.Trim()}");
                        }
                        
                        if (!string.IsNullOrEmpty(error))
                        {
                            // magiskboot的很多正常信息会输出到stderr，需要区分真正的错误
                            // 只有在进程退出码不为0时才标记为错误
                            if (process.ExitCode != 0)
                            {
                                AppendAutorootLog($"错误: {error.Trim()}");
                            }
                            else
                            {
                                // 正常的信息输出，不标记为错误
                                AppendAutorootLog($"[信息] {error.Trim()}");
                            }
                        }
                        
                        return process.ExitCode == 0;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                AppendAutorootLog($"执行magiskboot命令时发生错误: {ex.Message}");
                return false;
            }
        }

        private async Task<string?> RunMagiskbootCommandWithOutput(string arguments, string workingDirectory)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = magiskbootPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDirectory
                };

                using (Process? process = Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        string output = await process.StandardOutput.ReadToEndAsync();
                        await process.WaitForExitAsync();
                        
                        return process.ExitCode == 0 ? output : null;
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
        
        // 处理开始检测设备功能
        private void HandleStartDeviceDetection()
        {
            try
            {
                // 如果定时器已经在运行，先停止它
                if (deviceStatusTimer != null && deviceStatusTimer.IsEnabled)
                {
                    deviceStatusTimer.Stop();
                }
                
                // 重新初始化设备状态监控
                InitializeDeviceStatusMonitoring();
                
                Dispatcher.Invoke(() =>
                {
                    if (DeviceDetectionToggle != null)
                    {
                        DeviceDetectionToggle.IsChecked = true;
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    // 错误处理
                    System.Windows.MessageBox.Show($"启动设备检测时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }
        
        // 处理停止检测设备功能
        private void HandleStopDeviceDetection(bool terminateRunningTools = true)
        {
            try
            {
                _isDeviceDetectionEnabled = false;
                unchecked
                {
                    _deviceDetectionVersion++;
                }

                // 停止设备状态监控定时器
                if (deviceStatusTimer != null)
                {
                    deviceStatusTimer.Stop();
                    deviceStatusTimer = null;
                }
                
                // 某些任务（如全自动 ROOT）需要保留当前 ADB Server，避免在重启命令
                // 发出前因 daemon 重建而短暂丢失已记录的设备序列号。
                if (terminateRunningTools)
                {
                    _ = KillAllAdbAndFastbootProcesses();
                }
                
                // 清空设备状态显示
                Dispatcher.Invoke(() =>
                {
                    if (DeviceDetectionToggle != null)
                    {
                        DeviceDetectionToggle.IsChecked = false;
                    }

                    // 清空设备列表
                    DeviceSerials.Clear();
                    
                    // 清空设备状态文本
                    if (DeviceStatusText != null)
                    {
                        SetLocalizedText(DeviceStatusText, "未检测到设备");
                        DeviceStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // 红色 #DC3545
                    }
                    
                    // 清空设备选择下拉框
                    if (MultiDeviceComboBox != null)
                    {
                        MultiDeviceComboBox.SelectedItem = null;
                    }
                    
                    // 清空设备详细信息
                    if (ConnectionTypeText != null)
                    {
                        SetLocalizedText(ConnectionTypeText, "--");
                    }
                    
                    if (DeviceSerialText != null)
                    {
                        DeviceSerialText.Text = "--";
                    }
                    
                    if (DeviceModelText != null)
                    {
                        DeviceModelText.Text = "--";
                    }
                    
                    if (DeviceCodeText != null)
                    {
                        DeviceCodeText.Text = "--";
                    }
                    
                    if (UnlockStatusText != null)
                    {
                        SetLocalizedText(UnlockStatusText, "--");
                    }
                    
                    if (BottomConnectionTypeText != null)
                    {
                        SetLocalizedText(BottomConnectionTypeText, "--");
                    }
                    UpdateBottomConnectionStatusIndicator("未连接", "--");
                    
                    // 清空A/B分区信息
                    if (ABPartitionText != null)
                    {
                        SetLocalizedText(ABPartitionText, "--");
                    }

                    if (SelinuxStatusText != null)
                    {
                        SetLocalizedText(SelinuxStatusText, "--");
                        SelinuxStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243));
                    }

                    if (CpuManufacturerText != null) CpuManufacturerText.Text = "--";
                    if (CpuCodeNameText != null) CpuCodeNameText.Text = "--";
                    if (CpuNameText != null) CpuNameText.Text = "--";
                    if (WindowsVersionText != null) WindowsVersionText.Text = "--";
                    if (AndroidVersionText != null) AndroidVersionText.Text = "--";
                    if (VersionInfoText != null) VersionInfoText.Text = "--";
                    if (KernelVersionText != null) KernelVersionText.Text = "--";
                    if (BuildDateText != null) BuildDateText.Text = "--";

                    if (BatteryControl != null)
                    {
                        BatteryControl.Maximum = 100;
                        BatteryControl.Value = 0;
                        BatteryControl.IsCharging = false;
                        BatteryControl.TemperatureText = "--";
                    }

                    if (_storageViewModel != null)
                    {
                        _storageViewModel.SetTotalStorage(0);
                        _storageViewModel.StorageUsage = 0;
                        _storageViewModel.SetTotalMemory(0);
                        _storageViewModel.MemoryUsage = 0;
                    }
                    
                    // 重置最后的设备状态和详细信息
                    lastDeviceStatus = "";
                    lastConnectionType = "";
                    lastDeviceSerial = "";
                    lastDeviceModel = "";
                    lastDeviceCode = "";
                    lastAndroidVersion = "";
                    lastUnlockStatus = "";
                    lastABPartition = "";
                    lastSelinuxStatus = "";
                    lastKernelVersion = "";
                    lastBuildDate = "";
                    lastCpuManufacturer = "";
                    lastCpuCodeName = "";
                    lastWindowsVersion = "";
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    // 错误处理
                    System.Windows.MessageBox.Show($"停止设备检测时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        // 检测刷机包机型的方法
        private async void FormatDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            FormatDeviceButton.IsEnabled = false;
            var taskStopwatch = Stopwatch.StartNew();

            try
            {
                // 先检测设备连接
                string fastbootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools", "fastboot.exe");
                string deviceCheckResult = await ExecuteFastbootCommand(
                    fastbootPath, "devices", parseStatusOutput: false);
                var deviceMatch = Regex.Match(
                    deviceCheckResult ?? string.Empty,
                    @"(?m)^(\S+)\s+fastboot\s*$",
                    RegexOptions.IgnoreCase);

                if (!deviceMatch.Success)
                {
                    LogSimpleStatus("错误: 未检测到 Fastboot 设备");
                    return;
                }

                string deviceSerial = deviceMatch.Groups[1].Value;
                LogSimpleStatus($"已连接 {deviceSerial} | Fastboot");
                LogSimpleStatus("正在清除设备数据...");

                // 执行 fastboot erase metadata
                string metadataResult = await ExecuteFastbootCommand(
                    fastbootPath, "erase metadata", parseStatusOutput: false);
                if (CommandOutputIndicatesFailure(metadataResult))
                {
                    LogSimpleStatus($"错误: metadata 分区清除失败 | {GetFastbootErrorSummary(metadataResult)}");
                    return;
                }

                // 执行 fastboot erase userdata
                string userdataResult = await ExecuteFastbootCommand(
                    fastbootPath, "erase userdata", parseStatusOutput: false);
                if (CommandOutputIndicatesFailure(userdataResult))
                {
                    LogSimpleStatus($"错误: userdata 分区清除失败 | {GetFastbootErrorSummary(userdataResult)}");
                    return;
                }

                LogSimpleStatus("操作完成.");
                LogSimpleStatus("[Rebooting]重启设备.");

                // 执行 fastboot reboot
                string rebootResult = await ExecuteFastbootCommand(
                    fastbootPath, "reboot", parseStatusOutput: false);
                if (CommandOutputIndicatesFailure(rebootResult))
                {
                    LogSimpleStatus("警告: 设备数据已清除，但自动重启失败，请手动重启设备");
                }

                taskStopwatch.Stop();
                LogSimpleStatus($"任务结束，耗时{taskStopwatch.Elapsed.TotalSeconds:F1}秒.");
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"错误: 设备数据清除失败 | {ex.Message}");
            }
            finally
            {
                FormatDeviceButton.IsEnabled = true;
            }
        }

        // 擦除谷歌锁按钮点击事件
        private async void EraseGoogleLockButton_Click(object sender, RoutedEventArgs e)
        {
            EraseGoogleLockButton.IsEnabled = false;
            var taskStopwatch = Stopwatch.StartNew();

            try
            {
                // 先检测设备连接
                string fastbootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools", "fastboot.exe");
                string deviceCheckResult = await ExecuteFastbootCommand(
                    fastbootPath, "devices", parseStatusOutput: false);
                var deviceMatch = Regex.Match(
                    deviceCheckResult ?? string.Empty,
                    @"(?m)^(\S+)\s+fastboot\s*$",
                    RegexOptions.IgnoreCase);

                if (!deviceMatch.Success)
                {
                    LogSimpleStatus("错误: 未检测到 Fastboot 设备");
                    return;
                }

                string deviceSerial = deviceMatch.Groups[1].Value;
                LogSimpleStatus($"已连接 {deviceSerial} | Fastboot");

                // 执行 fastboot erase frp
                string frpResult = await ExecuteFastbootCommand(
                    fastbootPath,
                    "erase frp",
                    showNativeOutput: true,
                    parseStatusOutput: false);
                if (CommandOutputIndicatesFailure(frpResult))
                {
                    LogSimpleStatus($"错误: FRP 分区擦除失败 | {GetFastbootErrorSummary(frpResult)}");
                    return;
                }

                LogSimpleStatus("[Rebooting]重启设备.");

                // 执行 fastboot reboot
                string rebootResult = await ExecuteFastbootCommand(
                    fastbootPath, "reboot", parseStatusOutput: false);
                if (CommandOutputIndicatesFailure(rebootResult))
                {
                    LogSimpleStatus("警告: FRP 分区已擦除，但自动重启失败，请手动重启设备");
                }

                taskStopwatch.Stop();
                LogSimpleStatus($"任务结束，耗时{taskStopwatch.Elapsed.TotalSeconds:F1}秒.");
            }
            catch (Exception ex)
            {
                LogSimpleStatus($"错误: FRP 分区擦除失败 | {ex.Message}");
            }
            finally
            {
                EraseGoogleLockButton.IsEnabled = true;
            }
        }

        private void SelectSuperScatterButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.Description = "请选择散包(Images)所在目录";
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (SuperScatterPathTextBox != null)
                {
                    SuperScatterPathTextBox.Text = dialog.SelectedPath;
                    AppendSuperLog($"已选择散包路径: {dialog.SelectedPath}");
                }
            }
        }

        private async void StartSuperPackButton_Click(object sender, RoutedEventArgs e)
        {
            string dir = SuperScatterPathTextBox?.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                System.Windows.MessageBox.Show("请先选择有效的散包路径！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StartSuperPackButton.IsEnabled = false;
            AppendSuperLog("=== 开始构建 Super 镜像 ===");
            
            try
            {
                string binDir = AppDomain.CurrentDomain.BaseDirectory;
                var maker = new SuperMaker(binDir, (msg) => Dispatcher.Invoke(() => AppendSuperLog(msg)));
                
                string outputDir = System.IO.Path.Combine(dir, "IMAGES");
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                bool success = await maker.MakeSuperFromDirectoryAsync(dir, outputDir);
                
                if (success)
                {
                    if (CleanupMyPartitionsCheckBox.IsChecked == true)
                    {
                        try
                        {
                            AppendSuperLog("=== 开始清理残留分区文件/文件夹 ===");
                            var usedPartitions = maker.ProcessedPartitions; // 获取构建过程中用到的分区列表
                            
                            // 1. 清理文件夹
                            var subDirs = Directory.GetDirectories(outputDir);
                            foreach (var subDir in subDirs)
                            {
                                var dirName = new DirectoryInfo(subDir).Name;
                                bool shouldDelete = false;

                                // 规则1: my 开头
                                if (dirName.StartsWith("my", StringComparison.OrdinalIgnoreCase))
                                {
                                    shouldDelete = true;
                                }
                                // 规则2: 在 usedPartitions 列表中
                                else if (usedPartitions.Contains(dirName, StringComparer.OrdinalIgnoreCase))
                                {
                                    shouldDelete = true;
                                }

                                if (shouldDelete)
                                {
                                    Directory.Delete(subDir, true);
                                    AppendSuperLog($"已删除文件夹: {dirName}");
                                }
                            }

                            // 2. 清理文件 (排除 super.img)
                            var files = Directory.GetFiles(outputDir);
                            foreach (var file in files)
                            {
                                var fileName = System.IO.Path.GetFileName(file);
                                var fileNameNoExt = System.IO.Path.GetFileNameWithoutExtension(file);
                                
                                // 排除 super.img
                                if (fileName.Equals("super.img", StringComparison.OrdinalIgnoreCase)) continue;

                                bool shouldDelete = false;

                                // 规则1: my 开头
                                if (fileName.StartsWith("my", StringComparison.OrdinalIgnoreCase))
                                {
                                    shouldDelete = true;
                                }
                                // 规则2: 在 usedPartitions 列表中
                                else if (usedPartitions.Contains(fileNameNoExt, StringComparer.OrdinalIgnoreCase))
                                {
                                    shouldDelete = true;
                                }

                                if (shouldDelete)
                                {
                                    File.Delete(file);
                                    AppendSuperLog($"已删除文件: {fileName}");
                                }
                            }

                            AppendSuperLog("清理完成。");
                        }
                        catch (Exception ex)
                        {
                            AppendSuperLog($"清理失败: {ex.Message}");
                        }
                    }

                    if (FilterXmlCheckBox.IsChecked == true)
                    {
                        try
                        {
                            AppendSuperLog("=== 开始过滤多余 XML 文件 ===");
                            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                            string targetDir = System.IO.Path.Combine(desktopDir, "擦除与重构LUN");
                            if (!Directory.Exists(targetDir))
                            {
                                Directory.CreateDirectory(targetDir);
                            }

                            var blankGptFiles = Directory.GetFiles(outputDir, "rawprogram*_BLANK_GPT.xml");
                            var wipePartFiles = Directory.GetFiles(outputDir, "rawprogram*_WIPE_PARTITIONS.xml");
                            var allFiles = blankGptFiles.Concat(wipePartFiles);

                            foreach (var file in allFiles)
                            {
                                string fileName = System.IO.Path.GetFileName(file);
                                string destPath = System.IO.Path.Combine(targetDir, fileName);
                                if (File.Exists(destPath)) File.Delete(destPath);
                                File.Move(file, destPath);
                                AppendSuperLog($"已移动: {fileName}");
                            }
                            AppendSuperLog($"XML 过滤完成，文件已移动至: {targetDir}");
                        }
                        catch (Exception ex)
                        {
                            AppendSuperLog($"XML 过滤失败: {ex.Message}");
                        }
                    }

                    AppendSuperLog($"打包成功！输出路径: {outputDir}");
                }
                else
                {
                    System.Windows.MessageBox.Show("打包失败，请查看日志。", "失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    AppendSuperLog("打包失败！");
                }
            }
            catch (Exception ex)
            {
                AppendSuperLog($"[异常] {ex.Message}");
                System.Windows.MessageBox.Show($"发生异常: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                StartSuperPackButton.IsEnabled = true;
            }
        }

        private void CleanupMyPartitionsCheckBox_Checked(object sender, RoutedEventArgs e)
        {
        }

        private void AppendSuperLog(string msg)
        {
            if (SuperPackLogRichTextBox == null) return;
            
            Dispatcher.Invoke(() =>
            {
                var para = SuperPackLogRichTextBox.Document.Blocks.FirstBlock as Paragraph;
                if (para == null)
                {
                    para = new Paragraph();
                    SuperPackLogRichTextBox.Document.Blocks.Add(para);
                }
                
                string text = msg;
                System.Windows.Media.Brush? foreground = null;

                if (text.StartsWith("COLOR:"))
                {
                    int pipeIndex = text.IndexOf('|');
                    if (pipeIndex > 6)
                    {
                        string colorName = text.Substring(6, pipeIndex - 6);
                        text = text.Substring(pipeIndex + 1);
                        try
                        {
                            foreground = (System.Windows.Media.Brush?)new BrushConverter().ConvertFromString(colorName);
                        }
                        catch { }
                    }
                }

                bool isAppend = text.Trim() == "OK";
                if (isAppend)
                {
                    if (para.Inlines.LastInline is Run lastRun && lastRun.Text.EndsWith("\n"))
                    {
                        lastRun.Text = lastRun.Text.TrimEnd('\n');
                    }
                }

                string content;
                if (isAppend)
                {
                    content = $"{text}\n";
                }
                else
                {
                    string time = DateTime.Now.ToString("HH:mm:ss");
                    content = $"[{time}] {text}\n";
                }

                var run = new Run(content);
                if (foreground != null)
                {
                    run.Foreground = foreground;
                    run.FontWeight = FontWeights.Bold;
                }
                para.Inlines.Add(run);
                SuperPackLogRichTextBox.ScrollToEnd();
            });
        }

        private async Task TryIncrementOpenCountAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "curl",
                        Arguments = "-X POST https://violettool.top/web-api/open-count/increment -v",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = false,
                        RedirectStandardError = false
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(3000);
                }
                catch
                {
                }
            });
        }
    }
}
