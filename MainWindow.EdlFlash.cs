using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SharpEDL;
using SharpEDL.Auth;
using SharpEDL.DataClass;
using System.Management;
using System.Xml.Linq;
using AFlashTool.Modular.Super;
using AFlashTool.Modular.Super.LpMake;
using Yu.Modular.Qualcomm.Firehose.Parsers;

using EdlPartitionInfo = SharpEDL.DataClass.PartitionInfo;

namespace WpfApp1
{
    public partial class MainWindow
    {
        private readonly ObservableCollection<EdlPartitionViewItem> _edlPartitions = new();
        private readonly ObservableCollection<EdlBuiltInLoaderOption> _edlBuiltInLoaderOptions = new();
        private const string EdlCloudBaseUrl = "https://violettool.top/firehose/";
        private const string EdlOnePlusCloudBaseUrl = EdlCloudBaseUrl + "oneplus/";
        private const string EdlBugFeedbackUploadUrl = "https://violettool.top/firehose/edl-feedback.php";
        private const string EdlBugFeedbackFallbackUploadUrl = "https://violettool.top/web-api/edl-feedback";
        private const int EdlBugFeedbackMaxBytes = 5 * 1024 * 1024;
        internal const string EdlPortOpenFailedPrefix = "__EDL_PORT_OPEN_FAILED__|";
        internal const string EdlLoaderSendTimeoutPrefix = "__EDL_LOADER_SEND_TIMEOUT__|";
        private EdlEngine? _edlEngine;
        private string? _edlFlashPackDir;
        private string[]? _edlFlashPackProgramFiles;
        private double _edlPercent;
        private string _edlSpeedText = "";
        private string _edlCurrentPartitionText = "";
        private bool _edlProgressUpdateQueued;
        private bool _edlSpeedUpdateQueued;
        private bool _edlPartitionUpdateQueued;
        private bool _edlSearchSuppressSelectionChanged;
        private string _edlPartitionSearchLastQuery = "";
        private string? _edlNativeLogPath;
        private readonly object _edlNativeLogLock = new();
        private readonly ConcurrentQueue<string> _edlNativeLogQueue = new();
        private readonly ConcurrentDictionary<string, string> _edlPortDisplayNames =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly object EdlPortEnumerationSync = new();
        private readonly SemaphoreSlim _edlNativeLogSignal = new(0);
        private int _edlNativeLogSignalPending;
        private CancellationTokenSource? _edlNativeLogWriterCancellation;
        private Task? _edlNativeLogWriterTask;
        private readonly SemaphoreSlim _edlOperationLock = new(1, 1);
        private System.Windows.Controls.Button? _edlOpDragButton;
        private System.Windows.Controls.Button? _edlOpDragPendingButton;
        private System.Windows.Point _edlOpDragStartMouse;
        private System.Windows.Point _edlOpDragPendingStartMouse;
        private double _edlOpDragStartLeft;
        private double _edlOpDragStartTop;
        private bool _edlOpDragMoved;
        private DispatcherTimer? _edlOpDragHoldTimer;
        private Paragraph? _edlPendingFlashParagraph;
        private bool _edlPendingFlashUsesArrowFormat;
        private Paragraph? _edlPendingPortProbeParagraph;
        private StackPanel? _edlVersionInfoPanel;
        private EdlOplusLoaderPackage? _edlResolvedOplusLoaderPackage;
        private bool _edlCloudLoadersStarted;
        private bool _edlCloudLoadersLoading;

        private static readonly SolidColorBrush EdlLogTimestampBrush = CreateEdlLogBrush(0x94, 0xA3, 0xB8);
        private static readonly SolidColorBrush EdlLogBodyBrush = CreateEdlLogBrush(0x33, 0x41, 0x55);
        private static readonly SolidColorBrush EdlLogSecondaryBrush = CreateEdlLogBrush(0x64, 0x74, 0x8B);
        private static readonly SolidColorBrush EdlLogActionBrush = CreateEdlLogBrush(0x7C, 0x3A, 0xED);
        private static readonly SolidColorBrush EdlLogSuccessBrush = CreateEdlLogBrush(0x16, 0xA3, 0x4A);
        private static readonly SolidColorBrush EdlLogErrorBrush = CreateEdlLogBrush(0xDC, 0x26, 0x26);
        private static readonly SolidColorBrush EdlLogWarningBrush = CreateEdlLogBrush(0xD9, 0x77, 0x06);
        private static readonly SolidColorBrush EdlLogInfoBrush = CreateEdlLogBrush(0x25, 0x63, 0xEB);

        private static SolidColorBrush CreateEdlLogBrush(byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }

        private static Paragraph CreateEdlLogParagraph()
        {
            return new Paragraph
            {
                Margin = new Thickness(0, 0.5, 0, 0.5),
                LineHeight = 19
            };
        }

        private static void AppendEdlTimestamp(Paragraph paragraph)
        {
            string time = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            paragraph.Inlines.Add(new Run($"[{time}] ") { Foreground = EdlLogTimestampBrush });
        }

        private static void AppendEdlOperationText(Paragraph paragraph, string text)
        {
            string content = text ?? string.Empty;
            int arrowIndex = content.IndexOf("->[", StringComparison.Ordinal);
            if (arrowIndex > 0)
            {
                paragraph.Inlines.Add(new Run(content[..arrowIndex]) { Foreground = EdlLogBodyBrush });
                paragraph.Inlines.Add(new Run("->") { Foreground = EdlLogSecondaryBrush });
                int targetStart = arrowIndex + "->".Length;
                int targetEnd = content.IndexOf(']', targetStart);
                if (targetEnd >= targetStart)
                {
                    paragraph.Inlines.Add(new Run(content[targetStart..(targetEnd + 1)])
                    {
                        Foreground = EdlLogActionBrush,
                        FontWeight = FontWeights.SemiBold
                    });
                    if (targetEnd + 1 < content.Length)
                        paragraph.Inlines.Add(new Run(content[(targetEnd + 1)..]) { Foreground = EdlLogBodyBrush });
                    return;
                }
            }

            if (content.StartsWith("[", StringComparison.Ordinal))
            {
                int tagEnd = content.IndexOf(']');
                if (tagEnd > 0)
                {
                    paragraph.Inlines.Add(new Run(content[..(tagEnd + 1)])
                    {
                        Foreground = EdlLogActionBrush,
                        FontWeight = FontWeights.SemiBold
                    });
                    content = content[(tagEnd + 1)..];
                }
            }

            if (content.Length > 0)
                paragraph.Inlines.Add(new Run(content) { Foreground = EdlLogBodyBrush });
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            InitializeEdlEngine();
        }

        private void InitializeEdlEngine()
        {
            Closed -= MainWindow_EdlClosed;
            Closed += MainWindow_EdlClosed;
            if (EdlLogTextBox != null)
            {
                EdlLogTextBox.Document = new FlowDocument
                {
                    PagePadding = new Thickness(0),
                    ColumnWidth = 10000,
                    ColumnGap = 0,
                    TextAlignment = TextAlignment.Left,
                    FontWeight = FontWeights.Normal,
                    LineHeight = 19
                };
            }
            _edlEngine = new EdlEngine(
                LogEdlMessage,
                UpdateEdlProgress,
                UpdateEdlSpeedText,
                UpdateEdlPortLabel,
                UpdateEdlCurrentPartitionText,
                AppendEdlNativeLog,
                checkedValue =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (EdlSendLoaderCheckBox != null)
                            EdlSendLoaderCheckBox.IsChecked = checkedValue;
                    });
                });
            EdlPartitionDataGrid.ItemsSource = _edlPartitions;
            if (EdlPartitionSearchComboBox != null)
                EdlPartitionSearchComboBox.ItemsSource = BuildEdlPartitionSearchOptions(null);
            ApplyEdlFactoryResetOptionState();
            LoadEdlBuiltInLoaders();
            LoadEdlPorts();
            InitializeEdlNativeLog();
        }

        private void MainWindow_EdlClosed(object? sender, EventArgs e)
        {
            DeleteEdlNativeLog();
        }

        private void InitializeEdlNativeLog()
        {
            string logDir = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "tmp",
                "log");
            Directory.CreateDirectory(logDir);
            string fileName = "VioletToolEdlLog" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt";
            string logPath = Path.Combine(logDir, fileName);
            File.WriteAllText(logPath, string.Empty, new UTF8Encoding(false));

            var cancellation = new CancellationTokenSource();
            lock (_edlNativeLogLock)
            {
                _edlNativeLogPath = logPath;
                _edlNativeLogWriterCancellation = cancellation;
                _edlNativeLogWriterTask = Task.Run(
                    () => RunEdlNativeLogWriterAsync(logPath, cancellation.Token));
            }
        }

        private void DeleteEdlNativeLog()
        {
            string? path;
            CancellationTokenSource? cancellation;
            Task? writerTask;
            lock (_edlNativeLogLock)
            {
                path = _edlNativeLogPath;
                _edlNativeLogPath = null;
                cancellation = _edlNativeLogWriterCancellation;
                writerTask = _edlNativeLogWriterTask;
                _edlNativeLogWriterCancellation = null;
                _edlNativeLogWriterTask = null;
            }

            try { cancellation?.Cancel(); } catch { }
            try { _edlNativeLogSignal.Release(); } catch { }
            try { writerTask?.Wait(TimeSpan.FromSeconds(3)); } catch { }
            cancellation?.Dispose();

            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                string fileName = Path.GetFileName(path) ?? string.Empty;
                if (fileName.StartsWith("VioletToolEdlLog", StringComparison.OrdinalIgnoreCase) &&
                    fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private void AppendEdlNativeLog(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            lock (_edlNativeLogLock)
            {
                if (string.IsNullOrEmpty(_edlNativeLogPath))
                    return;
            }

            _edlNativeLogQueue.Enqueue(
                text.EndsWith(Environment.NewLine, StringComparison.Ordinal)
                    ? text
                    : text + Environment.NewLine);
            if (Interlocked.Exchange(ref _edlNativeLogSignalPending, 1) == 0)
                _edlNativeLogSignal.Release();
        }

        private async Task RunEdlNativeLogWriterAsync(string logPath, CancellationToken cancellationToken)
        {
            try
            {
                await using var stream = new FileStream(
                    logPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 64 * 1024);

                while (!cancellationToken.IsCancellationRequested)
                {
                    await _edlNativeLogSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
                    await Task.Delay(75, cancellationToken).ConfigureAwait(false);
                    Interlocked.Exchange(ref _edlNativeLogSignalPending, 0);

                    while (_edlNativeLogQueue.TryDequeue(out string? line))
                        await writer.WriteAsync(line).ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);

                    if (!_edlNativeLogQueue.IsEmpty &&
                        Interlocked.Exchange(ref _edlNativeLogSignalPending, 1) == 0)
                    {
                        _edlNativeLogSignal.Release();
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Logging must never interrupt an active EDL operation.
            }
            finally
            {
                try
                {
                    if (!_edlNativeLogQueue.IsEmpty)
                    {
                        await using var stream = new FileStream(
                            logPath,
                            FileMode.Append,
                            FileAccess.Write,
                            FileShare.ReadWrite,
                            64 * 1024,
                            FileOptions.Asynchronous | FileOptions.SequentialScan);
                        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 64 * 1024);
                        while (_edlNativeLogQueue.TryDequeue(out string? line))
                            await writer.WriteAsync(line).ConfigureAwait(false);
                        await writer.FlushAsync().ConfigureAwait(false);
                    }
                }
                catch
                {
                }
            }
        }

        internal static bool IsEdlNativeProcess(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;
            string name = Path.GetFileName(fileName);
            return name.Equals("fh_loader.exe", StringComparison.OrdinalIgnoreCase)
                || name.Equals("QSaharaServer.exe", StringComparison.OrdinalIgnoreCase);
        }

        private void LoadEdlBuiltInLoaders()
        {
            if (EdlBuiltInLoaderComboBox == null)
                return;

            _edlBuiltInLoaderOptions.Clear();
            EdlBuiltInLoaderComboBox.ItemsSource = _edlBuiltInLoaderOptions;
            EdlBuiltInLoaderComboBox.SelectedIndex = -1;
            EdlBuiltInLoaderComboBox.Text = "加载云端引导...";
        }

        private void StartEdlCloudLoaderRefresh()
        {
            if (_edlCloudLoadersStarted)
                return;

            _edlCloudLoadersStarted = true;
            SetEdlCloudLoaderLoadingState(true);
            _ = RefreshEdlCloudLoadersAsync();
        }

        private void SetEdlCloudLoaderLoadingState(bool isLoading)
        {
            _edlCloudLoadersLoading = isLoading;
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;

            void UpdateOverlay()
            {
                bool operationRunning = _edlOperationLock.CurrentCount == 0;
                UpdateEdlPartitionOverlayState(operationRunning);
            }

            if (Dispatcher.CheckAccess())
                UpdateOverlay();
            else
                Dispatcher.BeginInvoke((Action)UpdateOverlay);
        }

        private void AddOrReplaceEdlBuiltInLoaderOption(EdlBuiltInLoaderOption option)
        {
            for (int i = 0; i < _edlBuiltInLoaderOptions.Count; i++)
            {
                var existing = _edlBuiltInLoaderOptions[i];
                if (existing.FilePath.Equals(option.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    _edlBuiltInLoaderOptions[i] = option;
                    return;
                }
            }

            _edlBuiltInLoaderOptions.Add(option);
        }

        private async Task RefreshEdlCloudLoadersAsync()
        {
            try
            {
                var options = await FetchEdlCloudLoaderOptionsAsync().ConfigureAwait(false);
                if (options.Count == 0)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (EdlBuiltInLoaderComboBox != null)
                        {
                            EdlBuiltInLoaderComboBox.SelectedIndex = -1;
                            EdlBuiltInLoaderComboBox.Text = "云端引导加载失败";
                        }
                    });
                    return;
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    string? selectedPath = (EdlBuiltInLoaderComboBox?.SelectedItem as EdlBuiltInLoaderOption)?.FilePath;
                    _edlBuiltInLoaderOptions.Clear();

                    foreach (var option in options
                        .OrderBy(item => item.SortOrder)
                        .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
                    {
                        AddOrReplaceEdlBuiltInLoaderOption(option);
                    }

                    if (EdlBuiltInLoaderComboBox != null)
                    {
                        var selectedOption = _edlBuiltInLoaderOptions.FirstOrDefault(item =>
                            !string.IsNullOrWhiteSpace(selectedPath)
                            && item.FilePath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
                        if (selectedOption != null)
                        {
                            EdlBuiltInLoaderComboBox.SelectedItem = selectedOption;
                        }
                        else
                        {
                            EdlBuiltInLoaderComboBox.SelectedIndex = -1;
                            EdlBuiltInLoaderComboBox.Text = "FireHose";
                        }
                    }
                });
            }
            catch
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (EdlBuiltInLoaderComboBox != null)
                    {
                        EdlBuiltInLoaderComboBox.SelectedIndex = -1;
                        EdlBuiltInLoaderComboBox.Text = "云端引导加载失败";
                    }
                });
            }
            finally
            {
                SetEdlCloudLoaderLoadingState(false);
            }
        }

        private static async Task<List<EdlBuiltInLoaderOption>> FetchEdlCloudLoaderOptionsAsync()
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(20);

            var mergedEntries = new Dictionary<string, EdlCloudLoaderEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (string manifestName in new[] { "manifest.php", "index.json", "loaders.json", "manifest.json" })
            {
                string manifestUrl = AddEdlCacheBuster(new Uri(new Uri(EdlCloudBaseUrl), manifestName).ToString());
                string? json = await TryGetStringAsync(client, manifestUrl).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                    continue;

                foreach (EdlCloudLoaderEntry entry in ParseEdlCloudLoaderManifest(json))
                {
                    string brand = NormalizeCloudFolderName(entry.BrandName ?? "oneplus");
                    string folder = NormalizeCloudFolderName(entry.FolderName);
                    if (string.IsNullOrWhiteSpace(brand) || string.IsNullOrWhiteSpace(folder))
                        continue;
                    mergedEntries[brand + "/" + folder] = entry;
                }
            }

            List<EdlCloudLoaderEntry> entries = mergedEntries.Values.ToList();
            if (entries.Count == 0)
                entries = await FetchLegacyOnePlusCloudEntriesAsync(client).ConfigureAwait(false);

            var options = new List<EdlBuiltInLoaderOption>();
            foreach (var entry in entries)
            {
                if (!entry.Enabled)
                    continue;

                string brandName = NormalizeCloudFolderName(entry.BrandName ?? "oneplus");
                string folderName = NormalizeCloudFolderName(entry.FolderName);
                if (string.IsNullOrWhiteSpace(brandName) || string.IsNullOrWhiteSpace(folderName))
                    continue;

                string brandBaseUrl = new Uri(new Uri(EdlCloudBaseUrl), brandName.TrimEnd('/') + "/").ToString();
                string displayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? folderName : entry.DisplayName.Trim();
                if (displayName.StartsWith("[云端]", StringComparison.OrdinalIgnoreCase))
                    displayName = displayName[4..].TrimStart();

                string? downloadUrl = entry.DownloadUrl;
                if (string.IsNullOrWhiteSpace(downloadUrl) && !string.IsNullOrWhiteSpace(entry.ZipFileName))
                {
                    downloadUrl = new Uri(
                        new Uri(brandBaseUrl),
                        folderName.TrimEnd('/') + "/" + entry.ZipFileName.Trim()).ToString();
                }

                if (string.IsNullOrWhiteSpace(downloadUrl)
                    || !await UrlExistsAsync(client, downloadUrl).ConfigureAwait(false))
                {
                    downloadUrl = await ResolveEdlCloudLoaderFileUrlAsync(
                        client,
                        brandBaseUrl,
                        folderName,
                        brandName.Equals("oneplus", StringComparison.OrdinalIgnoreCase)).ConfigureAwait(false);
                }

                if (string.IsNullOrWhiteSpace(downloadUrl))
                    continue;

                bool isOplusPackage = entry.PackageType?.Equals("oplus", StringComparison.OrdinalIgnoreCase) == true
                    || entry.LoaderType?.Equals("oplus_vip", StringComparison.OrdinalIgnoreCase) == true
                    || brandName.Equals("oneplus", StringComparison.OrdinalIgnoreCase);
                string loaderType = string.IsNullOrWhiteSpace(entry.LoaderType)
                    ? isOplusPackage
                        ? "oplus_vip"
                        : entry.PackageType?.Equals("multi", StringComparison.OrdinalIgnoreCase) == true
                            ? "multi"
                            : "single"
                    : entry.LoaderType.Trim();
                string firehoseProfile = string.IsNullOrWhiteSpace(entry.FirehoseProfile)
                    ? isOplusPackage ? "oplus_vip_adaptive" : "generic_direct"
                    : entry.FirehoseProfile.Trim();
                string authType = string.IsNullOrWhiteSpace(entry.AuthType)
                    ? isOplusPackage ? "oplus_vip" : "none"
                    : entry.AuthType.Trim();
                options.Add(EdlBuiltInLoaderOption.CreateRemote(
                    displayName,
                    $"cloud-loader://{brandName}/{folderName}",
                    downloadUrl,
                    isOplusPackage,
                    entry.SortOrder,
                    loaderType,
                    firehoseProfile,
                    authType));
            }

            return options
                .GroupBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private static async Task<List<EdlCloudLoaderEntry>> FetchLegacyOnePlusCloudEntriesAsync(HttpClient client)
        {
            List<EdlCloudLoaderEntry> entries = new();
            foreach (string manifestName in new[] { "manifest.json", "loaders.json", "index.json" })
            {
                string manifestUrl = AddEdlCacheBuster(new Uri(new Uri(EdlOnePlusCloudBaseUrl), manifestName).ToString());
                string? json = await TryGetStringAsync(client, manifestUrl).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                    continue;

                entries = ParseEdlCloudLoaderManifest(json);
                if (entries.Count > 0)
                    break;
            }

            if (entries.Count == 0)
            {
                string? html = await TryGetStringAsync(client, AddEdlCacheBuster(EdlOnePlusCloudBaseUrl)).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(html))
                {
                    entries = ParseEdlCloudLoaderFoldersFromHtml(html, EdlOnePlusCloudBaseUrl)
                        .Select(folder => new EdlCloudLoaderEntry(folder, folder, null, null, "oneplus", "oplus"))
                        .ToList();
                }
            }

            return entries
                .Select(entry => new EdlCloudLoaderEntry(
                    entry.DisplayName,
                    entry.FolderName,
                    entry.ZipFileName,
                    entry.DownloadUrl,
                    "oneplus",
                    "oplus",
                    entry.SortOrder,
                    entry.LoaderType ?? "oplus_vip",
                    entry.FirehoseProfile ?? "oplus_vip_adaptive",
                    entry.AuthType ?? "oplus_vip",
                    entry.Enabled))
                .ToList();
        }

        private static async Task<string?> ResolveEdlCloudLoaderFileUrlAsync(
            HttpClient client,
            string brandBaseUrl,
            string folderName,
            bool preferZip)
        {
            string folderUrl = new Uri(new Uri(brandBaseUrl), folderName.TrimEnd('/') + "/").ToString();
            string? html = await TryGetStringAsync(client, folderUrl).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(html))
            {
                string? fileFromHtml = ParseFirstCloudLoaderUrlFromHtml(html, folderUrl, preferZip);
                if (!string.IsNullOrWhiteSpace(fileFromHtml))
                    return fileFromHtml;
            }

            foreach (string candidate in BuildEdlCloudFileNameCandidates(folderName, preferZip))
            {
                string url = new Uri(new Uri(folderUrl), candidate).ToString();
                if (await UrlExistsAsync(client, url).ConfigureAwait(false))
                    return url;
            }

            return null;
        }

        private static string AddEdlCacheBuster(string url)
        {
            string separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            long stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return url + separator + "_v=" + stamp.ToString(CultureInfo.InvariantCulture);
        }

        private static async Task<string?> TryGetStringAsync(HttpClient client, string url)
        {
            try
            {
                string authorizedUrl = await ResolveEdlAuthorizedUrlAsync(client, url).ConfigureAwait(false);
                using var response = await client.GetAsync(
                    authorizedUrl,
                    HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return null;
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<bool> UrlExistsAsync(HttpClient client, string url)
        {
            try
            {
                string authorizedUrl = await ResolveEdlAuthorizedUrlAsync(client, url).ConfigureAwait(false);
                using var head = new HttpRequestMessage(HttpMethod.Head, authorizedUrl);
                using var headResponse = await client.SendAsync(head, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (headResponse.IsSuccessStatusCode)
                    return true;
            }
            catch
            {
            }

            try
            {
                string authorizedUrl = await ResolveEdlAuthorizedUrlAsync(client, url).ConfigureAwait(false);
                using var response = await client.GetAsync(
                    authorizedUrl,
                    HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static Task<string> ResolveEdlAuthorizedUrlAsync(HttpClient unusedClient, string url)
        {
            if (!TryGetProtectedEdlCloudPath(url, out _))
                return Task.FromResult(url);

            return Task.FromResult(ApplyViolettoolSignIfNeeded(url));
        }

        private static bool TryGetProtectedEdlCloudPath(string url, out string relativePath)
        {
            relativePath = string.Empty;
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
                || !uri.Host.Equals("violettool.top", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            const string firehosePrefix = "/firehose/";
            if (!uri.AbsolutePath.StartsWith(firehosePrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            relativePath = Uri.UnescapeDataString(
                uri.AbsolutePath[firehosePrefix.Length..].TrimStart('/'));
            return !string.IsNullOrWhiteSpace(relativePath);
        }

        private static List<EdlCloudLoaderEntry> ParseEdlCloudLoaderManifest(string json)
        {
            var entries = new List<EdlCloudLoaderEntry>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    AddManifestArrayEntries(root, entries);
                }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    foreach (string propertyName in new[] { "loaders", "items", "folders" })
                    {
                        if (root.TryGetProperty(propertyName, out var array) && array.ValueKind == JsonValueKind.Array)
                        {
                            AddManifestArrayEntries(array, entries);
                            break;
                        }
                    }
                }
            }
            catch
            {
            }

            return entries;
        }

        private static void AddManifestArrayEntries(JsonElement array, List<EdlCloudLoaderEntry> entries)
        {
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    string? folder = item.GetString();
                    if (!string.IsNullOrWhiteSpace(folder))
                        entries.Add(new EdlCloudLoaderEntry(folder, folder, null, null, null, null));
                    continue;
                }

                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                string? displayName = TryGetJsonString(item, "displayName")
                    ?? TryGetJsonString(item, "name")
                    ?? TryGetJsonString(item, "title");
                string? folderName = TryGetJsonString(item, "folder")
                    ?? TryGetJsonString(item, "folderName")
                    ?? TryGetJsonString(item, "name");
                string? zipFile = TryGetJsonString(item, "zip")
                    ?? TryGetJsonString(item, "zipFile")
                    ?? TryGetJsonString(item, "file");
                string? downloadUrl = TryGetJsonString(item, "url")
                    ?? TryGetJsonString(item, "downloadUrl");
                string? brandName = TryGetJsonString(item, "brand")
                    ?? TryGetJsonString(item, "brandName");
                string? packageType = TryGetJsonString(item, "package")
                    ?? TryGetJsonString(item, "packageType")
                    ?? TryGetJsonString(item, "type");
                string? loaderType = TryGetJsonString(item, "loaderType")
                    ?? TryGetJsonString(item, "loader_type");
                string? firehoseProfile = TryGetJsonString(item, "firehoseProfile")
                    ?? TryGetJsonString(item, "firehose_profile");
                string? authType = TryGetJsonString(item, "authType")
                    ?? TryGetJsonString(item, "auth_type");
                bool enabled = TryGetJsonBool(item, "enabled") ?? true;
                int? sortOrder = TryGetJsonInt(item, "order")
                    ?? TryGetJsonInt(item, "sort")
                    ?? TryGetJsonInt(item, "sortOrder");

                if (string.IsNullOrWhiteSpace(folderName) && !string.IsNullOrWhiteSpace(downloadUrl))
                {
                    var uri = new Uri(downloadUrl, UriKind.RelativeOrAbsolute);
                    string[] parts = uri.IsAbsoluteUri
                        ? uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                        : downloadUrl.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                        folderName = Uri.UnescapeDataString(parts[^2]);
                }

                if (!string.IsNullOrWhiteSpace(folderName))
                    entries.Add(new EdlCloudLoaderEntry(
                        displayName ?? folderName,
                        folderName,
                        zipFile,
                        downloadUrl,
                        brandName,
                        packageType,
                        sortOrder,
                        loaderType,
                        firehoseProfile,
                        authType,
                        enabled));
            }
        }

        private static string? TryGetJsonString(JsonElement obj, string propertyName)
        {
            if (!obj.TryGetProperty(propertyName, out var value))
                return null;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        private static int? TryGetJsonInt(JsonElement obj, string propertyName)
        {
            if (!obj.TryGetProperty(propertyName, out var value))
                return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
                return number;
            if (value.ValueKind == JsonValueKind.String
                && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                return number;
            }
            return null;
        }

        private static bool? TryGetJsonBool(JsonElement obj, string propertyName)
        {
            if (!obj.TryGetProperty(propertyName, out var value))
                return null;
            if (value.ValueKind == JsonValueKind.True)
                return true;
            if (value.ValueKind == JsonValueKind.False)
                return false;
            if (value.ValueKind == JsonValueKind.String
                && bool.TryParse(value.GetString(), out bool result))
            {
                return result;
            }
            return null;
        }

        private static List<string> ParseEdlCloudLoaderFoldersFromHtml(string html, string baseUrl)
        {
            var baseUri = new Uri(baseUrl);
            var folders = new List<string>();
            foreach (Match match in Regex.Matches(html, "href\\s*=\\s*[\"'](?<href>[^\"']+)[\"']", RegexOptions.IgnoreCase))
            {
                string href = System.Net.WebUtility.HtmlDecode(match.Groups["href"].Value);
                if (string.IsNullOrWhiteSpace(href) || href.StartsWith("../", StringComparison.Ordinal) || href.StartsWith("#", StringComparison.Ordinal))
                    continue;

                Uri uri;
                try
                {
                    uri = new Uri(baseUri, href);
                }
                catch
                {
                    continue;
                }

                string relative = Uri.UnescapeDataString(baseUri.MakeRelativeUri(uri).ToString());
                if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith("../", StringComparison.Ordinal))
                    continue;

                relative = relative.Split('?', '#')[0].Trim('/');
                if (string.IsNullOrWhiteSpace(relative) || relative.Contains('/'))
                    continue;

                if (!Path.GetExtension(relative).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    folders.Add(relative);
            }

            return folders
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string? ParseFirstCloudLoaderUrlFromHtml(string html, string folderUrl, bool preferZip)
        {
            var folderUri = new Uri(folderUrl);
            var files = new List<string>();
            foreach (Match match in Regex.Matches(html, "href\\s*=\\s*[\"'](?<href>[^\"']+)[\"']", RegexOptions.IgnoreCase))
            {
                string href = System.Net.WebUtility.HtmlDecode(match.Groups["href"].Value);
                string cleanPath = href.Split('?', '#')[0].TrimEnd('/');
                string fileName = Uri.UnescapeDataString(Path.GetFileName(cleanPath));
                string extension = Path.GetExtension(fileName);
                bool supported = extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".elf", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".melf", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".mbn", StringComparison.OrdinalIgnoreCase)
                    || (string.IsNullOrEmpty(extension)
                        && !string.IsNullOrWhiteSpace(fileName)
                        && !fileName.Equals("..", StringComparison.Ordinal));
                if (!supported)
                    continue;

                try
                {
                    files.Add(new Uri(folderUri, href).ToString());
                }
                catch
                {
                }
            }

            return files
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(url => preferZip && Path.GetExtension(new Uri(url).AbsolutePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(url => Path.GetFileName(new Uri(url).AbsolutePath).Contains("firehose", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(url => Path.GetExtension(new Uri(url).AbsolutePath).Equals(".melf", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
        }

        private static IEnumerable<string> BuildEdlCloudFileNameCandidates(string folderName, bool preferZip)
        {
            string token = folderName.Trim().Trim('/');
            if (token.StartsWith("OnePlus_", StringComparison.OrdinalIgnoreCase))
                token = token.Substring("OnePlus_".Length);
            token = Regex.Replace(token.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
            if (string.IsNullOrWhiteSpace(token))
                token = "firehose";

            var candidates = new List<string>
            {
                $"oplus_firehose_{token}.zip",
                $"oneplus_firehose_{token}.zip",
                $"firehose_{token}.zip",
                folderName.Trim().Trim('/') + ".zip",
                "firehose.zip",
                folderName.Trim().Trim('/'),
                folderName.Trim().Trim('/') + ".melf",
                folderName.Trim().Trim('/') + ".elf",
                "prog_firehose_ddr.elf",
                "prog_firehose_ddr.melf",
                "prog_firehose_ddr.mbn"
            };
            IEnumerable<string> orderedCandidates = preferZip
                ? candidates
                : candidates.OrderBy(name => Path.GetExtension(name).Equals(".zip", StringComparison.OrdinalIgnoreCase));
            return orderedCandidates
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static string NormalizeCloudFolderName(string folderName)
        {
            folderName = folderName.Trim().Trim('/');
            if (folderName.Contains("://", StringComparison.Ordinal))
            {
                try
                {
                    string[] parts = new Uri(folderName).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    folderName = parts.LastOrDefault() ?? "";
                }
                catch
                {
                }
            }

            return Uri.UnescapeDataString(folderName);
        }

        private void EdlBuiltInLoaderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EdlBuiltInLoaderComboBox?.SelectedItem is not EdlBuiltInLoaderOption option)
                return;
            if (EdlLoaderTextBox != null)
                EdlLoaderTextBox.Text = option.FilePath;
        }

        private async Task<string> ResolveEdlLoaderPathAsync(
            string loaderPath,
            bool sendLoader,
            bool waitForPortBeforeDownload = true)
        {
            _edlResolvedOplusLoaderPackage = null;
            _edlEngine?.SetLoaderProfile(null, null);
            if (string.IsNullOrWhiteSpace(loaderPath) || !loaderPath.StartsWith("cloud-loader://", StringComparison.OrdinalIgnoreCase))
                return loaderPath;

            var option = EdlBuiltInLoaderComboBox?.Items
                .OfType<EdlBuiltInLoaderOption>()
                .FirstOrDefault(item => string.Equals(item.FilePath, loaderPath, StringComparison.OrdinalIgnoreCase));
            if (option == null || string.IsNullOrWhiteSpace(option.DownloadUrl))
                throw new Exception("未找到云端引导下载配置");

            // Generic cloud loaders also need their Firehose profile. Previously
            // only OPLUS packages carried this metadata into the EDL session.
            _edlEngine?.SetLoaderProfile(option.FirehoseProfile, option.AuthType);

            string cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VioletTool",
                "qc_cache");
            string safeName = Regex.Replace(option.DisplayName, @"[\\/:*?""<>|]+", "_");
            string itemDir = Path.Combine(cacheRoot, safeName);
            string remoteFileName;
            try
            {
                remoteFileName = Uri.UnescapeDataString(Path.GetFileName(new Uri(option.DownloadUrl).AbsolutePath));
            }
            catch
            {
                remoteFileName = "";
            }
            if (string.IsNullOrWhiteSpace(remoteFileName))
                remoteFileName = "loader.bin";
            remoteFileName = Regex.Replace(remoteFileName, @"[\\/:*?""<>|]+", "_");
            string downloadPath = Path.Combine(itemDir, remoteFileName);
            string extractDir = Path.Combine(itemDir, "extract");
            string metadataPath = Path.Combine(itemDir, "cache-metadata.json");
            Directory.CreateDirectory(itemDir);

            string? cachedLoaderPath = await Task.Run(
                () => TryResolveCachedEdlCloudLoader(option, downloadPath, extractDir)).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(cachedLoaderPath))
            {
                EdlCloudCacheState cacheState = await GetEdlCloudCacheStateAsync(
                    option.DownloadUrl,
                    metadataPath).ConfigureAwait(true);
                if (cacheState != EdlCloudCacheState.Stale)
                    return cachedLoaderPath;

                LogEdlMessage("检测到云端引导更新，正在刷新缓存...", null);
                await Task.Run(() =>
                {
                    if (Directory.Exists(extractDir))
                        Directory.Delete(extractDir, true);
                    if (File.Exists(downloadPath))
                        File.Delete(downloadPath);
                    if (File.Exists(metadataPath))
                        File.Delete(metadataPath);
                }).ConfigureAwait(true);
            }

            if (waitForPortBeforeDownload)
                await WaitForEdlPortBeforeCloudLoaderDownloadAsync().ConfigureAwait(true);
            LogEdlMessage("请求服务器...", null);
            LogEdlMessage($"下载云端引导: {option.DisplayName}", null);
            UpdateEdlCurrentPartitionText($"下载云端引导: {option.DisplayName}");
            UpdateEdlSpeedText("0.00 B/s");
            UpdateEdlProgress(0);

            EdlCloudLoaderCacheMetadata downloadedMetadata;
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(5);
                string authorizedDownloadUrl = await ResolveEdlAuthorizedUrlAsync(
                    client,
                    option.DownloadUrl).ConfigureAwait(true);
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    authorizedDownloadUrl);
                request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
                {
                    NoCache = true
                };
                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(true);
                response.EnsureSuccessStatusCode();
                long? totalBytes = response.Content.Headers.ContentLength;
                downloadedMetadata = CreateEdlCloudLoaderCacheMetadata(option.DownloadUrl, response);
                await using var remote = await response.Content.ReadAsStreamAsync().ConfigureAwait(true);
                await using var local = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None);
                byte[] buffer = new byte[128 * 1024];
                long readBytes = 0;
                long lastBytes = 0;
                var speedWatch = Stopwatch.StartNew();
                while (true)
                {
                    int read = await remote.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(true);
                    if (read <= 0)
                        break;
                    await local.WriteAsync(buffer, 0, read).ConfigureAwait(true);
                    readBytes += read;
                    if (totalBytes.HasValue && totalBytes.Value > 0)
                        UpdateEdlProgress(readBytes * 100.0 / totalBytes.Value);
                    if (speedWatch.ElapsedMilliseconds >= 300)
                    {
                        double seconds = Math.Max(0.001, speedWatch.Elapsed.TotalSeconds);
                        UpdateEdlSpeedText(FormatEdlSpeed((readBytes - lastBytes) / seconds));
                        lastBytes = readBytes;
                        speedWatch.Restart();
                    }
                }
                UpdateEdlProgress(100);
            }

            bool isZip = Path.GetExtension(downloadPath).Equals(".zip", StringComparison.OrdinalIgnoreCase);
            string[] files = await Task.Run(() =>
            {
                if (!isZip)
                    return new[] { downloadPath };

                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, true);
                Directory.CreateDirectory(extractDir);
                ZipFile.ExtractToDirectory(downloadPath, extractDir);
                return Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
            }).ConfigureAwait(true);

            await Task.Run(() =>
            {
                string json = JsonSerializer.Serialize(downloadedMetadata);
                File.WriteAllText(metadataPath, json, Encoding.UTF8);
            }).ConfigureAwait(true);

            string? programmerPath = FindEdlCloudProgrammerFile(files, option.RequiresOplusPackage);
            if (string.IsNullOrWhiteSpace(programmerPath))
                throw new Exception("云端引导包中未找到 firehose/programmer 文件");

            if (!option.RequiresOplusPackage)
                return programmerPath;

            string? digestPath = files.FirstOrDefault(file =>
            {
                string name = Path.GetFileNameWithoutExtension(file);
                return name.Contains("digest", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("design", StringComparison.OrdinalIgnoreCase);
            });
            string? signPath = files.FirstOrDefault(file =>
            {
                string name = Path.GetFileNameWithoutExtension(file);
                return name.Contains("sign", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("sig", StringComparison.OrdinalIgnoreCase);
            });

            if (string.IsNullOrWhiteSpace(digestPath))
                throw new Exception("云端引导包中未找到 digest/design 文件");
            if (string.IsNullOrWhiteSpace(signPath))
                throw new Exception("云端引导包中未找到 sign/sig 文件");

            _edlResolvedOplusLoaderPackage = new EdlOplusLoaderPackage(
                programmerPath,
                digestPath,
                signPath,
                extractDir,
                option.DisplayName,
                option.FirehoseProfile,
                option.AuthType);
            return programmerPath;
        }

        private static EdlCloudLoaderCacheMetadata CreateEdlCloudLoaderCacheMetadata(
            string downloadUrl,
            HttpResponseMessage response)
        {
            return new EdlCloudLoaderCacheMetadata
            {
                DownloadUrl = downloadUrl,
                ETag = response.Headers.ETag?.Tag,
                LastModified = response.Content.Headers.LastModified?.ToString("R", CultureInfo.InvariantCulture),
                ContentLength = response.Content.Headers.ContentLength
            };
        }

        private static async Task<EdlCloudCacheState> GetEdlCloudCacheStateAsync(
            string downloadUrl,
            string metadataPath)
        {
            EdlCloudLoaderCacheMetadata? localMetadata;
            try
            {
                if (!File.Exists(metadataPath))
                    return EdlCloudCacheState.Stale;
                string json = await File.ReadAllTextAsync(metadataPath).ConfigureAwait(false);
                localMetadata = JsonSerializer.Deserialize<EdlCloudLoaderCacheMetadata>(json);
                if (localMetadata == null
                    || !string.Equals(localMetadata.DownloadUrl, downloadUrl, StringComparison.OrdinalIgnoreCase))
                {
                    return EdlCloudCacheState.Stale;
                }
            }
            catch
            {
                return EdlCloudCacheState.Stale;
            }

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                string authorizedDownloadUrl = await ResolveEdlAuthorizedUrlAsync(
                    client,
                    downloadUrl).ConfigureAwait(false);
                using var request = new HttpRequestMessage(HttpMethod.Head, authorizedDownloadUrl);
                request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
                {
                    NoCache = true
                };
                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return EdlCloudCacheState.Unknown;

                EdlCloudLoaderCacheMetadata remoteMetadata =
                    CreateEdlCloudLoaderCacheMetadata(downloadUrl, response);
                bool hasComparableValue = false;
                if (!string.IsNullOrWhiteSpace(localMetadata.ETag)
                    && !string.IsNullOrWhiteSpace(remoteMetadata.ETag))
                {
                    hasComparableValue = true;
                    if (!string.Equals(localMetadata.ETag, remoteMetadata.ETag, StringComparison.Ordinal))
                        return EdlCloudCacheState.Stale;
                }
                if (!string.IsNullOrWhiteSpace(localMetadata.LastModified)
                    && !string.IsNullOrWhiteSpace(remoteMetadata.LastModified))
                {
                    hasComparableValue = true;
                    if (!string.Equals(
                        localMetadata.LastModified,
                        remoteMetadata.LastModified,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return EdlCloudCacheState.Stale;
                    }
                }
                if (localMetadata.ContentLength.HasValue && remoteMetadata.ContentLength.HasValue)
                {
                    hasComparableValue = true;
                    if (localMetadata.ContentLength.Value != remoteMetadata.ContentLength.Value)
                        return EdlCloudCacheState.Stale;
                }
                return hasComparableValue ? EdlCloudCacheState.Current : EdlCloudCacheState.Unknown;
            }
            catch
            {
                return EdlCloudCacheState.Unknown;
            }
        }

        private string? TryResolveCachedEdlCloudLoader(
            EdlBuiltInLoaderOption option,
            string downloadPath,
            string extractDir)
        {
            string[] files;
            if (Path.GetExtension(downloadPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(extractDir))
                    return null;
                files = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
            }
            else
            {
                if (!File.Exists(downloadPath))
                    return null;
                files = new[] { downloadPath };
            }

            string? programmerPath = FindEdlCloudProgrammerFile(files, option.RequiresOplusPackage);
            if (string.IsNullOrWhiteSpace(programmerPath))
                return null;
            if (!option.RequiresOplusPackage)
                return programmerPath;

            string? digestPath = files.FirstOrDefault(file =>
            {
                string name = Path.GetFileNameWithoutExtension(file);
                return name.Contains("digest", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("design", StringComparison.OrdinalIgnoreCase);
            });
            string? signPath = files.FirstOrDefault(file =>
            {
                string name = Path.GetFileNameWithoutExtension(file);
                return name.Contains("sign", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("sig", StringComparison.OrdinalIgnoreCase);
            });
            if (string.IsNullOrWhiteSpace(digestPath) || string.IsNullOrWhiteSpace(signPath))
                return null;

            _edlResolvedOplusLoaderPackage = new EdlOplusLoaderPackage(
                programmerPath,
                digestPath,
                signPath,
                extractDir,
                option.DisplayName,
                option.FirehoseProfile,
                option.AuthType);
            return programmerPath;
        }

        private static string? FindEdlCloudProgrammerFile(
            IEnumerable<string> files,
            bool requiresOplusPackage)
        {
            string[] existingFiles = files
                .Where(File.Exists)
                .ToArray();
            if (!requiresOplusPackage)
            {
                string? manifest = existingFiles
                    .Where(file => Path.GetExtension(file).Equals(
                        ".xml",
                        StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(file => Path.GetFileName(file).Equals(
                        "qsahara_device_programmer.xml",
                        StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(SaharaProgrammerManifest.IsManifest);
                if (!string.IsNullOrWhiteSpace(manifest))
                    return manifest;

                string? generatedManifest = TryCreateDefaultMultiImageSaharaManifest(existingFiles);
                if (!string.IsNullOrWhiteSpace(generatedManifest))
                    return generatedManifest;
            }

            return FindEdlCloudFirehoseFile(existingFiles);
        }

        private static string? TryCreateDefaultMultiImageSaharaManifest(IEnumerable<string> files)
        {
            (int ImageId, string FileName)[] imageMap =
            {
                (36, "multi_image_qti.mbn"),
                (37, "multi_image.mbn"),
                (21, "xbl_sc.elf"),
                (60, "signed_firmware_soc_view.elf"),
                (59, "sequencer_ram.elf"),
                (61, "tme_config.elf"),
                (13, "prog_firehose_ddr.elf"),
                (38, "xbl_config_devprg.elf")
            };

            foreach (var group in files
                         .Where(File.Exists)
                         .GroupBy(file => Path.GetDirectoryName(Path.GetFullPath(file)) ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                var names = group
                    .GroupBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(item => item.Key, item => item.First(), StringComparer.OrdinalIgnoreCase);
                if (imageMap.Any(image => !names.ContainsKey(image.FileName)))
                    continue;

                string manifestPath = Path.Combine(group.Key, "qsahara_device_programmer.xml");
                var document = new XDocument(
                    new XDeclaration("1.0", null, null),
                    new XElement("sahara_config",
                        new XElement("chipset", "kaanapali"),
                        new XElement("images",
                            imageMap.Select(image =>
                                new XElement("image",
                                    new XAttribute("image_id", image.ImageId.ToString(CultureInfo.InvariantCulture)),
                                    new XAttribute("image_path", image.FileName))))));
                document.Save(manifestPath);
                return manifestPath;
            }

            return null;
        }

        private static string? FindEdlCloudFirehoseFile(IEnumerable<string> files)
        {
            static bool IsAuxiliaryFile(string file)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                return name.Contains("digest", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("design", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("sign", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("sig", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("multi_image", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("sequencer", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("xbl", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("tme_config", StringComparison.OrdinalIgnoreCase);
            }

            var candidates = files
                .Where(File.Exists)
                .Where(file => !IsAuxiliaryFile(file))
                .ToList();
            return candidates
                .OrderByDescending(file => Path.GetFileName(file).Contains("prog_firehose", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(file => Path.GetFileName(file).Contains("firehose", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(file => Path.GetExtension(file).Equals(".melf", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(file => Path.GetExtension(file).Equals(".elf", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(file => new FileInfo(file).Length)
                .FirstOrDefault();
        }

        private async Task WaitForEdlPortBeforeCloudLoaderDownloadAsync(int timeoutMs = 180000)
        {
            _edlEngine?.ThrowIfStopRequested();
            if (_edlEngine?.HasOpenFirehoseSession != true)
                _edlEngine?.BeginWaitingForEdlPort();

            async Task<bool> IsPortReadyAsync(string candidate)
            {
                if (_edlEngine?.IsCurrentEdlPortOpen(candidate) == true)
                {
                    if (_edlEngine.HasOpenFirehoseSession)
                    {
                        return true;
                    }

                    // A stale in-process handle must not make an enumerated 9008
                    // device look absent to the port waiter.
                    _edlEngine.ReleaseEdlPort();
                }

                bool ready = _edlEngine == null
                    || await Task.Run(() => _edlEngine.IsEdlPortReadyForExternalAccess(candidate)).ConfigureAwait(true);
                if (ready)
                    return true;
                return false;
            }

            async Task AcceptDetectedPortAsync(string candidate)
            {
                if (!await IsPortReadyAsync(candidate).ConfigureAwait(true))
                {
                    AppendEdlNativeLog($"[PORT-WAIT] {candidate} 已枚举但暂时被占用，150ms 后确认一次");
                    await Task.Delay(150).ConfigureAwait(true);
                    _edlEngine?.ThrowIfStopRequested();
                    if (!await IsPortReadyAsync(candidate).ConfigureAwait(true))
                    {
                        UpdateEdlSpeedText(null);
                        throw CreateEdlPortOpenFailedException(candidate);
                    }
                }

                _edlEngine?.AdoptDetectedEdlPort(candidate);
                UpdateEdlSpeedText(null);
                LogEdlMessage($"使用端口 {GetEdlPortDisplayName(candidate)}", null);
                await Task.Delay(100).ConfigureAwait(true);
            }

            string? port = await RunChkdevQcedlAsync().ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(port))
            {
                await AcceptDetectedPortAsync(port).ConfigureAwait(true);
                return;
            }

            LogEdlMessage("等待端口...", null);
            var sw = Stopwatch.StartNew();
            int lastRemainingSeconds = -1;
            while (true)
            {
                _edlEngine?.ThrowIfStopRequested();
                int remainingMs = timeoutMs - (int)sw.ElapsedMilliseconds;
                if (remainingMs <= 0)
                    break;

                int remainingSeconds = Math.Max(0, (int)Math.Ceiling(remainingMs / 1000.0));
                if (remainingSeconds != lastRemainingSeconds)
                {
                    lastRemainingSeconds = remainingSeconds;
                    UpdateEdlSpeedText($"等待端口...{remainingSeconds}s");
                }
                await Task.Delay(Math.Min(500, remainingMs)).ConfigureAwait(true);
                _edlEngine?.ThrowIfStopRequested();
                port = await RunChkdevQcedlAsync().ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(port))
                {
                    await AcceptDetectedPortAsync(port).ConfigureAwait(true);
                    return;
                }
            }

            UpdateEdlSpeedText(null);
            throw new Exception("等待端口超时.");
        }

        private Task<string?> RunChkdevQcedlAsync()
        {
            return Task.Run(() => RunChkdevQcedl());
        }

        private void LoadEdlPorts()
        {
            if (EdlPortComboBox == null)
                return;
            EdlPortComboBox.ItemsSource = BuildEdlPortOptions();
        }

        private List<string> BuildEdlPortOptions()
        {
            var ports = new List<string>();
            foreach (var descriptor in EnumeratePresentQcedlPorts())
            {
                ports.Add(descriptor.PortName);
                _edlPortDisplayNames[descriptor.PortName] = descriptor.DisplayName;
            }
            return ports;
        }

        private void EdlPortComboBox_DropDownOpened(object sender, EventArgs e)
        {
            LoadEdlPorts();
        }

        internal static bool IsEdlBasebandPartitionLabel(string? label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return false;

            string normalized = StripEdlSlotSuffix(label.Trim());
            return IsEdlBasebandFingerprintBackupPartitionLabel(normalized);
        }

        internal static bool IsEdlBasebandFingerprintBackupPartitionLabel(string? label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return false;

            string normalized = StripEdlSlotSuffix(label.Trim());
            if (normalized.Equals("persist", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("modemst1", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("modemst2", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("fsg", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("fsc", StringComparison.OrdinalIgnoreCase))
                return true;

            return normalized.Equals("oplusdycnvbk", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("oplusstanvbk", StringComparison.OrdinalIgnoreCase);
        }

        internal static string StripEdlSlotSuffix(string label)
        {
            if (label.EndsWith("_a", StringComparison.OrdinalIgnoreCase) ||
                label.EndsWith("_b", StringComparison.OrdinalIgnoreCase))
            {
                return label.Substring(0, label.Length - 2);
            }
            return label;
        }

        internal static bool IsEdlDataPartitionLabel(string? label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return false;
            return label.Equals("userdata", StringComparison.OrdinalIgnoreCase)
                || label.Equals("metadata", StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyEdlPartitionProtectionState()
        {
            bool protectSafe = EdlSkipSafeCheckBox?.IsChecked == true;
            bool protectData = EdlSkipDataCheckBox?.IsChecked == true;
            if (protectSafe || protectData)
            {
                foreach (var item in _edlPartitions)
                {
                    if (protectSafe && item.IsBasebandPartition)
                        item.IsSelected = false;
                    if (protectData && item.IsDataPartition)
                        item.IsSelected = false;
                }
            }
            EdlPartitionDataGrid?.Items.Refresh();
        }

        private void ApplyEdlFactoryResetOptionState()
        {
            bool factoryReset = EdlFactoryResetCheckBox?.IsChecked == true;
            if (EdlSkipDataCheckBox != null)
            {
                if (factoryReset)
                {
                    EdlSkipDataCheckBox.IsChecked = false;
                    EdlSkipDataCheckBox.IsEnabled = false;
                }
                else
                {
                    EdlSkipDataCheckBox.IsEnabled = true;
                }
            }
            ApplyEdlPartitionProtectionState();
        }

        private void EdlSkipSafeCheckBox_Checked(object sender, RoutedEventArgs e) => ApplyEdlPartitionProtectionState();

        private void EdlSkipSafeCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            ApplyEdlPartitionProtectionState();

            foreach (var item in _edlPartitions.Where(p => p.IsBasebandPartition))
            {
                string filePath = item.FilePath;
                if (!string.IsNullOrWhiteSpace(filePath)
                    && !filePath.Contains("(缺失)", StringComparison.OrdinalIgnoreCase)
                    && File.Exists(filePath))
                {
                    item.IsSelected = true;
                }
            }

            EdlPartitionDataGrid?.Items.Refresh();
        }

        private void EdlSkipDataCheckBox_Checked_Edl(object sender, RoutedEventArgs e) => ApplyEdlPartitionProtectionState();

        private void EdlSkipDataCheckBox_Unchecked_Edl(object sender, RoutedEventArgs e) => ApplyEdlPartitionProtectionState();

        private void EdlFactoryResetCheckBox_Checked_Edl(object sender, RoutedEventArgs e) => ApplyEdlFactoryResetOptionState();

        private void EdlFactoryResetCheckBox_Unchecked_Edl(object sender, RoutedEventArgs e) => ApplyEdlFactoryResetOptionState();

        private void EdlFactoryResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_edlEngine == null)
                return;

            RunEdlResetAction(async () =>
            {
                string loaderPath = EdlLoaderTextBox?.Text ?? "";
                bool sendLoader = EdlSendLoaderCheckBox?.IsChecked == true;
                bool skipLoaderResolveForOpenFirehose = !sendLoader && _edlEngine.HasOpenFirehoseSession;
                string resolvedLoaderPath = skipLoaderResolveForOpenFirehose
                    ? loaderPath
                    : await ResolveEdlLoaderPathAsync(loaderPath, sendLoader).ConfigureAwait(true);
                var oplusLoaderPackage = _edlResolvedOplusLoaderPackage;

                await Task.Run(() =>
                {
                    ExecuteRebootWithMiscPatch(
                        "misc.img",
                        "重启设备...",
                        resolvedLoaderPath,
                        sendLoader,
                        oplusLoaderPackage);
                }).ConfigureAwait(true);
            });
        }

        private void EdlWriteGptButton_Click(object sender, RoutedEventArgs e)
        {
            if (_edlEngine == null)
                return;

            string[]? chosenFiles;
            try
            {
                chosenFiles = ShowEdlRawProgramSelectionDialog();
            }
            catch (Exception ex)
            {
                LogEdlMessage("打开 RawProgram 文件选择窗口失败: " + ex.Message, "error");
                return;
            }
            if (chosenFiles == null || chosenFiles.Length == 0)
                return;

            string[] selectedFiles = chosenFiles
                .Where(File.Exists)
                .ToArray();
            if (selectedFiles.Length == 0)
            {
                LogEdlMessage("未选择可用的 rawprogram XML", "error");
                return;
            }

            string? firstDir = Path.GetDirectoryName(selectedFiles[0]);
            if (string.IsNullOrWhiteSpace(firstDir))
                return;
            if (selectedFiles.Any(f => !string.Equals(Path.GetDirectoryName(f), firstDir, StringComparison.OrdinalIgnoreCase)))
            {
                LogEdlMessage("请选择同一目录内的 rawprogram XML 文件", "error");
                return;
            }
            _edlFlashPackDir = firstDir;
            _edlFlashPackProgramFiles = selectedFiles;
            if (EdlFlashPackTextBox != null)
                EdlFlashPackTextBox.Text = firstDir;
            int[] targetLuns = GetEdlRawProgramPhysicalLuns(selectedFiles);

            RunEdlAction(async () =>
            {
                string loaderPath = EdlLoaderTextBox?.Text ?? "";
                bool sendLoader = EdlSendLoaderCheckBox?.IsChecked == true;
                bool skipLoaderResolveForOpenFirehose = !sendLoader && _edlEngine.HasOpenFirehoseSession;
                string resolvedLoaderPath = skipLoaderResolveForOpenFirehose
                    ? loaderPath
                    : await ResolveEdlLoaderPathAsync(loaderPath, sendLoader).ConfigureAwait(true);
                var oplusLoaderPackage = _edlResolvedOplusLoaderPackage;

                List<EdlPartitionInfo> refreshedPartitions = await Task.Run(() =>
                {
                    _edlEngine.PrepareForOperation(resolvedLoaderPath, sendLoader, false, oplusLoaderPackage);
                    if (_edlEngine.FirehoseServer == null)
                        throw new Exception("未连接到 Firehose");

                    _edlEngine.DoWriteGptEntriesFromRawprogramFiles(firstDir, selectedFiles);
                    return _edlEngine.ReadGptPartitionsFromDevice(targetLuns);
                }).ConfigureAwait(true);

                LoadEdlPartitions(refreshedPartitions, false);
                EdlPartitionDataGrid?.Items.Refresh();
                LogEdlMessage("分区表流程读取结束...", null);
                int parsedLunCount = refreshedPartitions
                    .Select(partition => partition.Lun)
                    .Distinct()
                    .Count();
                LogEdlMessage($"解析成功LUN数量: {parsedLunCount}", null);
                LogEdlMessage($"分区数: {refreshedPartitions.Count}", null);
            });
        }

        private void EdlBackupGptButton_Click(object sender, RoutedEventArgs e)
        {
            if (_edlEngine == null)
                return;

            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择GPT备份保存目录",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };
            var result = dlg.ShowDialog();
            if (result != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dlg.SelectedPath))
                return;

            string backupDir = Path.Combine(
                dlg.SelectedPath,
                "Violet_gpt_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
            string loaderPath = EdlLoaderTextBox?.Text ?? "";
            bool sendLoader = EdlSendLoaderCheckBox?.IsChecked == true;
            var targetLuns = _edlPartitions
                .Select(item => item.Model.Lun)
                .Distinct()
                .OrderBy(lun => lun)
                .ToList();

            RunEdlAction(async () =>
            {
                bool skipLoaderResolveForOpenFirehose = !sendLoader && _edlEngine.HasOpenFirehoseSession;
                string resolvedLoaderPath = skipLoaderResolveForOpenFirehose
                    ? loaderPath
                    : await ResolveEdlLoaderPathAsync(loaderPath, sendLoader).ConfigureAwait(true);
                var oplusLoaderPackage = _edlResolvedOplusLoaderPackage;

                await Task.Run(() =>
                {
                    _edlEngine.PrepareForOperation(resolvedLoaderPath, sendLoader, false, oplusLoaderPackage);
                    if (_edlEngine.FirehoseServer == null)
                        throw new Exception("未连接到 Firehose");

                    if (targetLuns.Count == 0)
                    {
                        targetLuns = _edlEngine.ReadGptPartitionsFromDevice()
                            .Select(partition => partition.Lun)
                            .Distinct()
                            .OrderBy(lun => lun)
                            .ToList();
                    }
                    if (targetLuns.Count == 0)
                        throw new Exception("未解析到可备份的 LUN");

                    _edlEngine.DoBackupGptMainFiles(backupDir, targetLuns);
                }).ConfigureAwait(true);
            });
        }

        private void EdlBackupModemFingerprintButton_Click(object sender, RoutedEventArgs e)
        {
            if (_edlEngine == null)
                return;

            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择基带指纹备份保存目录",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };
            var result = dlg.ShowDialog();
            if (result != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dlg.SelectedPath))
                return;

            string backupDir = Path.Combine(
                dlg.SelectedPath,
                "Violet_Backup_Modem_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
            string loaderPath = EdlLoaderTextBox?.Text ?? "";
            bool sendLoader = EdlSendLoaderCheckBox?.IsChecked == true;
            var loadedPartitions = _edlPartitions.Select(p => p.Model).ToList();
            List<EdlPartitionInfo>? autoReadPartitions = null;

            RunEdlAction(async () =>
            {
                bool skipLoaderResolveForOpenFirehose = !sendLoader && _edlEngine.HasOpenFirehoseSession;
                string resolvedLoaderPath = skipLoaderResolveForOpenFirehose
                    ? loaderPath
                    : await ResolveEdlLoaderPathAsync(loaderPath, sendLoader).ConfigureAwait(true);
                var oplusLoaderPackage = _edlResolvedOplusLoaderPackage;

                await Task.Run(() =>
                {
                    _edlEngine.PrepareForOperation(resolvedLoaderPath, sendLoader, false, oplusLoaderPackage);
                    if (_edlEngine.FirehoseServer == null)
                        throw new Exception("未连接到 Firehose");

                    var sourcePartitions = loadedPartitions;
                    if (sourcePartitions.Count == 0)
                    {
                        sourcePartitions = _edlEngine.ReadGptPartitionsFromDevice();
                        autoReadPartitions = sourcePartitions;
                    }

                    _edlEngine.DoBackupBasebandFingerprintPartitions(backupDir, sourcePartitions);
                }).ConfigureAwait(true);

                if (autoReadPartitions != null)
                    LoadEdlPartitions(autoReadPartitions, false);
                else
                    EdlPartitionDataGrid?.Items.Refresh();
            });
        }

        private void EdlForceOemButton_Click(object sender, RoutedEventArgs e)
        {
            if (_edlEngine == null)
                return;

            string loaderPath = EdlLoaderTextBox?.Text ?? "";
            bool sendLoader = EdlSendLoaderCheckBox?.IsChecked == true;
            var loadedPartitions = _edlPartitions.Select(p => p.Model).ToList();
            List<EdlPartitionInfo>? autoReadPartitions = null;

            RunEdlAction(async () =>
            {
                bool skipLoaderResolveForOpenFirehose = !sendLoader && _edlEngine.HasOpenFirehoseSession;
                string resolvedLoaderPath = skipLoaderResolveForOpenFirehose
                    ? loaderPath
                    : await ResolveEdlLoaderPathAsync(loaderPath, sendLoader).ConfigureAwait(true);
                var oplusLoaderPackage = _edlResolvedOplusLoaderPackage;

                await Task.Run(() =>
                {
                    _edlEngine.PrepareForOperation(resolvedLoaderPath, sendLoader, false, oplusLoaderPackage);
                    if (_edlEngine.FirehoseServer == null)
                        throw new Exception("未连接到 Firehose");

                    var sourcePartitions = loadedPartitions;
                    if (sourcePartitions.Count == 0)
                    {
                        sourcePartitions = _edlEngine.ReadGptPartitionsFromDevice();
                        autoReadPartitions = sourcePartitions;
                    }

                    EdlPartitionInfo? frp = sourcePartitions
                        .FirstOrDefault(p => p.Label.Equals("frp", StringComparison.OrdinalIgnoreCase));
                    if (frp == null)
                        throw new Exception("未找到 frp 分区");

                    _edlEngine.DoForceEnableOemUnlock(frp);
                }).ConfigureAwait(true);

                if (autoReadPartitions != null)
                    LoadEdlPartitions(autoReadPartitions, false);
                else
                    EdlPartitionDataGrid?.Items.Refresh();
            });
        }

        private void EdlSlotManagementButton_Click(object sender, RoutedEventArgs e)
        {
            if (_edlEngine == null)
                return;

            ShowEdlSlotManagementDialog();
        }

        private void EdlReadVersionInfoButton_Click(object sender, RoutedEventArgs e)
        {
            if (_edlEngine == null)
                return;

            string loaderPath = EdlLoaderTextBox?.Text ?? "";
            bool sendLoader = EdlSendLoaderCheckBox?.IsChecked == true;
            var loadedPartitions = _edlPartitions.Select(item => item.Model).ToList();
            List<EdlPartitionInfo>? autoReadPartitions = null;

            RunEdlAction(async () =>
            {
                bool skipLoaderResolveForOpenFirehose =
                    !sendLoader && _edlEngine.HasOpenFirehoseSession;
                string resolvedLoaderPath = skipLoaderResolveForOpenFirehose
                    ? loaderPath
                    : await ResolveEdlLoaderPathAsync(loaderPath, sendLoader).ConfigureAwait(true);
                var oplusLoaderPackage = _edlResolvedOplusLoaderPackage;

                await Task.Run(() =>
                {
                    _edlEngine.PrepareForOperation(
                        resolvedLoaderPath,
                        sendLoader,
                        false,
                        oplusLoaderPackage);
                    if (_edlEngine.FirehoseServer == null)
                        throw new Exception("未连接到 Firehose");

                    var sourcePartitions = loadedPartitions;
                    if (sourcePartitions.Count == 0)
                    {
                        sourcePartitions = _edlEngine.ReadGptPartitionsFromDevice();
                        autoReadPartitions = sourcePartitions;
                    }

                    _edlEngine.ReadAndLogVersionInformation(sourcePartitions);
                }).ConfigureAwait(true);

                if (autoReadPartitions != null)
                    LoadEdlPartitions(autoReadPartitions, false);
            });
        }

        private void ShowEdlSlotManagementDialog()
        {
            var dialog = new Window
            {
                Title = "槽位管理",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                Background = System.Windows.Media.Brushes.White
            };

            var root = new StackPanel
            {
                Margin = new Thickness(18),
                Width = 330
            };

            root.Children.Add(new TextBlock
            {
                Text = "设备启动槽位",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var statusBorder = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(247, 247, 249)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(224, 224, 224)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var statusGrid = new Grid();
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var currentSlotLabel = new TextBlock
            {
                Text = "当前槽位",
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            };
            var currentSlotValue = new TextBlock
            {
                Text = "未读取",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.Gray,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(currentSlotValue, 1);
            statusGrid.Children.Add(currentSlotLabel);
            statusGrid.Children.Add(currentSlotValue);
            statusBorder.Child = statusGrid;
            root.Children.Add(statusBorder);

            var operationStatus = new TextBlock
            {
                Text = "请先读取设备当前槽位",
                Foreground = System.Windows.Media.Brushes.DimGray,
                Margin = new Thickness(0, 0, 0, 8)
            };
            root.Children.Add(operationStatus);

            var busyProgress = new System.Windows.Controls.ProgressBar
            {
                Height = 3,
                IsIndeterminate = true,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 0, 0, 10)
            };
            root.Children.Add(busyProgress);

            var readButton = new System.Windows.Controls.Button
            {
                Content = "读取当前槽位",
                Height = 34,
                Margin = new Thickness(0, 0, 0, 10)
            };
            root.Children.Add(readButton);

            var switchPanel = new Grid
            {
                Margin = new Thickness(0, 0, 0, 12)
            };
            switchPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            switchPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            switchPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var switchToAButton = new System.Windows.Controls.Button
            {
                Content = "切换至 A 槽位",
                Height = 36,
                IsEnabled = false
            };
            var switchToBButton = new System.Windows.Controls.Button
            {
                Content = "切换至 B 槽位",
                Height = 36,
                IsEnabled = false
            };
            Grid.SetColumn(switchToBButton, 2);
            switchPanel.Children.Add(switchToAButton);
            switchPanel.Children.Add(switchToBButton);
            root.Children.Add(switchPanel);

            root.Children.Add(new TextBlock
            {
                Text = "切换操作会修改所有 LUN 的主 GPT，请确认引导与设备匹配。",
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(210, 40, 40)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            });

            var buttonPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            var closeButton = new System.Windows.Controls.Button
            {
                Content = "关闭",
                Width = 82,
                Height = 30,
                IsCancel = true
            };
            closeButton.Click += (_, _) => dialog.Close();
            buttonPanel.Children.Add(closeButton);
            root.Children.Add(buttonPanel);
            dialog.Content = root;

            bool isBusy = false;
            string? currentSlot = null;

            void RefreshSlotControls()
            {
                readButton.IsEnabled = !isBusy;
                switchToAButton.IsEnabled = !isBusy && currentSlot != null && currentSlot != "a";
                switchToBButton.IsEnabled = !isBusy && currentSlot != null && currentSlot != "b";
                closeButton.IsEnabled = !isBusy;
                busyProgress.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            }

            void SetCurrentSlot(string slot)
            {
                currentSlot = slot.ToLowerInvariant();
                currentSlotValue.Text = currentSlot.ToUpperInvariant();
                currentSlotValue.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 0, 128));
                RefreshSlotControls();
            }

            async Task ReadCurrentSlotAsync()
            {
                isBusy = true;
                operationStatus.Text = "正在读取当前槽位...";
                operationStatus.Foreground = System.Windows.Media.Brushes.DimGray;
                RefreshSlotControls();
                try
                {
                    string slot = await ExecuteEdlSlotOperationAsync(engine => engine.ReadCurrentSlot()).ConfigureAwait(true);
                    SetCurrentSlot(slot);
                    operationStatus.Text = $"当前设备正在使用 {slot.ToUpperInvariant()} 槽位";
                    operationStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 128, 0));
                }
                catch (Exception ex)
                {
                    currentSlot = null;
                    currentSlotValue.Text = "读取失败";
                    currentSlotValue.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(210, 40, 40));
                    operationStatus.Text = ex.Message;
                    operationStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(210, 40, 40));
                    LogEdlMessage(ex.Message, "error");
                }
                finally
                {
                    isBusy = false;
                    RefreshSlotControls();
                }
            }

            async Task SwitchSlotAsync(string targetSlot)
            {
                isBusy = true;
                operationStatus.Text = $"正在切换至 {targetSlot.ToUpperInvariant()} 槽位...";
                operationStatus.Foreground = System.Windows.Media.Brushes.DimGray;
                RefreshSlotControls();
                try
                {
                    string switchedSlot = await ExecuteEdlSlotOperationAsync(engine =>
                    {
                        engine.DoSwitchSlot(targetSlot);
                        return targetSlot;
                    }).ConfigureAwait(true);
                    SetCurrentSlot(switchedSlot);
                    operationStatus.Text = $"槽位切换完成，当前为 {switchedSlot.ToUpperInvariant()} 槽位";
                    operationStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 128, 0));
                }
                catch (Exception ex)
                {
                    operationStatus.Text = ex.Message;
                    operationStatus.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(210, 40, 40));
                    LogEdlMessage(ex.Message, "error");
                }
                finally
                {
                    isBusy = false;
                    RefreshSlotControls();
                }
            }

            readButton.Click += async (_, _) => await ReadCurrentSlotAsync();
            switchToAButton.Click += async (_, _) => await SwitchSlotAsync("a");
            switchToBButton.Click += async (_, _) => await SwitchSlotAsync("b");
            dialog.Closing += (_, e) =>
            {
                if (isBusy)
                    e.Cancel = true;
            };

            dialog.ShowDialog();
        }

        private async Task<T> ExecuteEdlSlotOperationAsync<T>(Func<EdlEngine, T> operation)
        {
            if (_edlEngine == null)
                throw new Exception("EDL 引擎未初始化");

            if (!await _edlOperationLock.WaitAsync(0).ConfigureAwait(true))
                throw new Exception("已有 EDL 操作正在执行，请等待当前任务结束。");

            try
            {
                SetEdlControlsEnabled(false);
                string loaderPath = EdlLoaderTextBox?.Text ?? "";
                bool sendLoader = EdlSendLoaderCheckBox?.IsChecked == true;
                bool skipLoaderResolveForOpenFirehose = !sendLoader && _edlEngine.HasOpenFirehoseSession;
                string resolvedLoaderPath = skipLoaderResolveForOpenFirehose
                    ? loaderPath
                    : await ResolveEdlLoaderPathAsync(loaderPath, sendLoader).ConfigureAwait(true);
                var oplusLoaderPackage = _edlResolvedOplusLoaderPackage;

                UpdateEdlSpeedText(null);
                UpdateEdlProgress(0);
                UpdateEdlCurrentPartitionText(null);
                return await Task.Run(() =>
                {
                    _edlEngine.PrepareForOperation(resolvedLoaderPath, sendLoader, false, oplusLoaderPackage);
                    if (_edlEngine.FirehoseServer == null)
                        throw new Exception("未连接到 Firehose");
                    return operation(_edlEngine);
                }).ConfigureAwait(true);
            }
            finally
            {
                UpdateEdlSpeedText(null);
                UpdateEdlCurrentPartitionText(null);
                SetEdlControlsEnabled(true);
                _edlOperationLock.Release();
            }
        }

        private void EdlFormatLunButton_Click(object sender, RoutedEventArgs e)
        {
            if (_edlEngine == null)
                return;
            if (_edlPartitions.Count == 0)
            {
                System.Windows.MessageBox.Show("请先读取分区表。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var availableLuns = _edlPartitions
                .Select(p => p.Lun)
                .Distinct()
                .OrderBy(lun => lun)
                .ToList();
            if (availableLuns.Count == 0)
            {
                System.Windows.MessageBox.Show("当前分区表中未识别到 LUN。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var targetLuns = ShowEdlFormatLunDialog(availableLuns);
            if (targetLuns == null || targetLuns.Count == 0)
                return;

            var knownPartitions = _edlPartitions.Select(p => p.Model).ToList();
            string loaderPath = EdlLoaderTextBox.Text;
            bool sendLoader = EdlSendLoaderCheckBox.IsChecked == true;
            bool manualAuth = false;

            RunEdlAction(async () =>
            {
                bool skipLoaderResolveForOpenFirehose = !sendLoader && _edlEngine.HasOpenFirehoseSession;
                string resolvedLoaderPath = skipLoaderResolveForOpenFirehose
                    ? loaderPath
                    : await ResolveEdlLoaderPathAsync(loaderPath, sendLoader).ConfigureAwait(true);
                var oplusLoaderPackage = _edlResolvedOplusLoaderPackage;

                await Task.Run(() =>
                {
                    _edlEngine.PrepareForOperation(
                        resolvedLoaderPath,
                        sendLoader,
                        manualAuth,
                        oplusLoaderPackage);
                    if (_edlEngine.FirehoseServer == null)
                        throw new Exception("未连接到 Firehose");

                    _edlEngine.DoFormatLuns(knownPartitions, targetLuns);
                }).ConfigureAwait(true);

                Dispatcher.Invoke(() =>
                {
                    foreach (var item in _edlPartitions.Where(p => targetLuns.Contains(p.Lun)).ToList())
                        _edlPartitions.Remove(item);
                    EdlPartitionDataGrid?.Items.Refresh();
                });
            });
        }

        private List<int>? ShowEdlFormatLunDialog(IList<int> availableLuns)
        {
            var selectedLuns = new List<int>();
            var dialog = new Window
            {
                Title = "格式化LUN",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                Background = System.Windows.Media.Brushes.White
            };

            var root = new StackPanel
            {
                Margin = new Thickness(16),
                Width = 360
            };

            root.Children.Add(new TextBlock
            {
                Text = "此操作将清零所选 LUN 内所有分区的头部，并擦除主、备 GPT。此操作为高危操作，请提前备份重要分区！",
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(210, 40, 40)),
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var checkPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            var boxes = new List<System.Windows.Controls.CheckBox>();
            foreach (int lun in availableLuns)
            {
                var box = new System.Windows.Controls.CheckBox
                {
                    Content = $"LUN{lun}",
                    Tag = lun,
                    IsChecked = true,
                    Margin = new Thickness(0, 4, 0, 4),
                    FontWeight = FontWeights.Bold
                };
                boxes.Add(box);
                checkPanel.Children.Add(box);
            }
            root.Children.Add(checkPanel);

            var buttonPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };

            var okButton = new System.Windows.Controls.Button
            {
                Content = "确认",
                Width = 82,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            okButton.Click += (_, _) =>
            {
                var chosen = boxes
                    .Where(box => box.IsChecked == true)
                    .Select(box => (int)box.Tag)
                    .OrderBy(lun => lun)
                    .ToList();
                if (chosen.Count == 0)
                {
                    System.Windows.MessageBox.Show(dialog, "请至少选择一个 LUN。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                selectedLuns = chosen;
                dialog.DialogResult = true;
            };

            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "取消",
                Width = 82,
                Height = 30,
                IsCancel = true
            };
            cancelButton.Click += (_, _) => dialog.DialogResult = false;

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            root.Children.Add(buttonPanel);

            dialog.Content = root;
            return dialog.ShowDialog() == true ? selectedLuns : null;
        }

        private static double GetSafeCanvasLeft(UIElement element)
        {
            double v = Canvas.GetLeft(element);
            return double.IsNaN(v) ? 0 : v;
        }

        private static double GetSafeCanvasTop(UIElement element)
        {
            double v = Canvas.GetTop(element);
            return double.IsNaN(v) ? 0 : v;
        }

        private void EdlDraggableButton_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button)
                return;
            if (EdlOperationButtonsCanvas == null)
                return;

            _edlOpDragPendingButton = button;
            _edlOpDragPendingStartMouse = e.GetPosition(EdlOperationButtonsCanvas);
            _edlOpDragMoved = false;

            _edlOpDragHoldTimer ??= new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _edlOpDragHoldTimer.Stop();
            _edlOpDragHoldTimer.Tick -= EdlOpDragHoldTimer_Tick;
            _edlOpDragHoldTimer.Tick += EdlOpDragHoldTimer_Tick;
            _edlOpDragHoldTimer.Start();
        }

        private void EdlOpDragHoldTimer_Tick(object? sender, EventArgs e)
        {
            _edlOpDragHoldTimer?.Stop();

            if (_edlOpDragPendingButton == null || EdlOperationButtonsCanvas == null)
                return;
            if (!_edlOpDragPendingButton.IsPressed)
                return;

            _edlOpDragButton = _edlOpDragPendingButton;
            _edlOpDragStartMouse = _edlOpDragPendingStartMouse;
            _edlOpDragStartLeft = GetSafeCanvasLeft(_edlOpDragButton);
            _edlOpDragStartTop = GetSafeCanvasTop(_edlOpDragButton);
            _edlOpDragMoved = false;
            _edlOpDragButton.CaptureMouse();
        }

        private void EdlDraggableButton_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_edlOpDragButton == null)
                return;
            if (EdlOperationButtonsCanvas == null)
                return;
            if (!_edlOpDragButton.IsMouseCaptured)
                return;
            if (e.LeftButton != MouseButtonState.Pressed)
                return;

            System.Windows.Point pos = e.GetPosition(EdlOperationButtonsCanvas);
            double dx = pos.X - _edlOpDragStartMouse.X;
            double dy = pos.Y - _edlOpDragStartMouse.Y;

            if (!_edlOpDragMoved)
            {
                if (Math.Abs(dx) < 4 && Math.Abs(dy) < 4)
                    return;
                _edlOpDragMoved = true;
            }

            double newLeft = _edlOpDragStartLeft + dx;
            double newTop = _edlOpDragStartTop + dy;

            newLeft = Math.Max(0, newLeft);
            newTop = Math.Max(0, newTop);

            double canvasWidth = EdlOperationButtonsCanvas.ActualWidth;
            double canvasHeight = EdlOperationButtonsCanvas.ActualHeight;
            double buttonWidth = _edlOpDragButton.ActualWidth > 0 ? _edlOpDragButton.ActualWidth : _edlOpDragButton.Width;
            double buttonHeight = _edlOpDragButton.ActualHeight > 0 ? _edlOpDragButton.ActualHeight : _edlOpDragButton.Height;

            if (canvasWidth > 0 && buttonWidth > 0)
                newLeft = Math.Min(canvasWidth - buttonWidth, newLeft);
            if (canvasHeight > 0 && buttonHeight > 0)
                newTop = Math.Min(canvasHeight - buttonHeight, newTop);

            Canvas.SetLeft(_edlOpDragButton, newLeft);
            Canvas.SetTop(_edlOpDragButton, newTop);
            e.Handled = true;
        }

        private void EdlDraggableButton_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _edlOpDragHoldTimer?.Stop();
            _edlOpDragPendingButton = null;

            if (_edlOpDragButton == null)
                return;

            if (_edlOpDragButton.IsMouseCaptured)
                _edlOpDragButton.ReleaseMouseCapture();

            if (_edlOpDragMoved)
                e.Handled = true;
            _edlOpDragButton = null;
            _edlOpDragMoved = false;
        }

        private sealed class EdlPartitionSearchOption
        {
            public string PartitionName { get; set; } = "";
        }

        private bool FilterEdlPartitionByImageName(object obj)
        {
            if (obj is not EdlPartitionViewItem p)
                return false;
            var key = EdlPartitionSearchComboBox?.Text?.Trim();
            if (string.IsNullOrEmpty(key))
                return true;
            return p.FileName.Contains(key, StringComparison.OrdinalIgnoreCase)
                || p.Label.Contains(key, StringComparison.OrdinalIgnoreCase);
        }

        private List<EdlPartitionSearchOption> BuildEdlPartitionSearchOptions(string? filter)
        {
            string key = (filter ?? "").Trim();
            IEnumerable<string> names;
            if (string.IsNullOrEmpty(key))
            {
                names = _edlPartitions
                    .Select(p => p.Label?.Trim() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s));
            }
            else
            {
                names = _edlPartitions
                    .Where(p =>
                        p.FileName.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                        p.Label.Contains(key, StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Label?.Trim() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s));
            }

            return names
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Select(n => new EdlPartitionSearchOption { PartitionName = n })
                .ToList();
        }

        private void EdlPartitionSearchClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (EdlPartitionSearchComboBox == null)
                return;

            _edlSearchSuppressSelectionChanged = true;
            try
            {
                EdlPartitionSearchComboBox.Text = "";
                EdlPartitionSearchComboBox.SelectedItem = null;
                EdlPartitionSearchComboBox.ItemsSource = null;
                EdlPartitionSearchComboBox.IsDropDownOpen = false;
                _edlPartitionSearchLastQuery = "";
            }
            finally
            {
                _edlSearchSuppressSelectionChanged = false;
            }

            CollectionViewSource.GetDefaultView(_edlPartitions).Refresh();
            EdlPartitionSearchComboBox.Focus();
        }

        private void EdlPartitionSearchComboBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (sender is not System.Windows.Controls.ComboBox comboBox)
                return;

            if (e.Key is System.Windows.Input.Key.Up
                or System.Windows.Input.Key.Down
                or System.Windows.Input.Key.Left
                or System.Windows.Input.Key.Right
                or System.Windows.Input.Key.Enter
                or System.Windows.Input.Key.Tab
                or System.Windows.Input.Key.LeftShift
                or System.Windows.Input.Key.RightShift
                or System.Windows.Input.Key.LeftCtrl
                or System.Windows.Input.Key.RightCtrl
                or System.Windows.Input.Key.LeftAlt
                or System.Windows.Input.Key.RightAlt)
            {
                return;
            }

            if (e.Key == System.Windows.Input.Key.Escape)
            {
                comboBox.IsDropDownOpen = false;
                return;
            }

            var editableTextBox = GetEditableComboBoxTextBox(comboBox);
            string typedText = editableTextBox?.Text ?? comboBox.Text ?? "";
            int caretIndex = editableTextBox?.CaretIndex ?? typedText.Length;
            int selectionLength = editableTextBox?.SelectionLength ?? 0;
            string searchText = typedText.Trim();
            if (string.Equals(typedText, _edlPartitionSearchLastQuery, StringComparison.Ordinal))
                return;
            _edlPartitionSearchLastQuery = typedText;

            _edlSearchSuppressSelectionChanged = true;
            try
            {
                if (string.IsNullOrWhiteSpace(searchText))
                {
                    comboBox.ItemsSource = null;
                    comboBox.IsDropDownOpen = false;
                    RestoreEdlPartitionSearchText(comboBox, typedText, caretIndex, selectionLength);
                    return;
                }

                var filtered = BuildEdlPartitionSearchOptions(searchText);
                if (filtered.Count > 0)
                {
                    comboBox.ItemsSource = filtered;
                    comboBox.IsDropDownOpen = true;
                }
                else
                {
                    comboBox.ItemsSource = null;
                    comboBox.IsDropDownOpen = false;
                }
            }
            finally
            {
                _edlSearchSuppressSelectionChanged = false;
            }

            RestoreEdlPartitionSearchText(comboBox, typedText, caretIndex, selectionLength);
        }

        private void EdlPartitionSearchComboBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
        }

        private void EdlPartitionSearchComboBox_DropDownOpened(object sender, EventArgs e)
        {
            if (sender is not System.Windows.Controls.ComboBox comboBox)
                return;

            var editableTextBox = GetEditableComboBoxTextBox(comboBox);
            string typedText = editableTextBox?.Text ?? comboBox.Text ?? "";
            int caretIndex = editableTextBox?.CaretIndex ?? typedText.Length;
            int selectionLength = editableTextBox?.SelectionLength ?? 0;

            _edlSearchSuppressSelectionChanged = true;
            try
            {
                comboBox.ItemsSource = BuildEdlPartitionSearchOptions(typedText);
            }
            finally
            {
                _edlSearchSuppressSelectionChanged = false;
            }

            RestoreEdlPartitionSearchText(comboBox, typedText, caretIndex, selectionLength);
        }

        private void EdlPartitionSearchComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_edlSearchSuppressSelectionChanged)
                return;
            if (sender is not System.Windows.Controls.ComboBox comboBox)
                return;
            if (comboBox.SelectedItem is not EdlPartitionSearchOption selected)
                return;

            _edlSearchSuppressSelectionChanged = true;
            try
            {
                comboBox.Text = selected.PartitionName;
                comboBox.SelectedItem = null;
                comboBox.IsDropDownOpen = false;
            }
            finally
            {
                _edlSearchSuppressSelectionChanged = false;
            }
            RestoreEdlPartitionSearchText(comboBox, selected.PartitionName, selected.PartitionName.Length, 0);

            var first = _edlPartitions.FirstOrDefault(p => string.Equals(p.Label, selected.PartitionName, StringComparison.OrdinalIgnoreCase));
            if (first != null && EdlPartitionDataGrid != null)
            {
                bool protectSafe = EdlSkipSafeCheckBox?.IsChecked == true;
                bool protectData = EdlSkipDataCheckBox?.IsChecked == true;
                if (!(protectSafe && first.IsBasebandPartition) && !(protectData && first.IsDataPartition))
                    first.IsSelected = true;
                EdlPartitionDataGrid.SelectedItem = first;
                EdlPartitionDataGrid.UpdateLayout();
                EdlPartitionDataGrid.ScrollIntoView(first);
            }
        }

        private static System.Windows.Controls.TextBox? GetEditableComboBoxTextBox(System.Windows.Controls.ComboBox comboBox)
        {
            comboBox.ApplyTemplate();
            return comboBox.Template.FindName("PART_EditableTextBox", comboBox) as System.Windows.Controls.TextBox;
        }

        private static void RestoreEdlPartitionSearchText(
            System.Windows.Controls.ComboBox comboBox,
            string text,
            int caretIndex,
            int selectionLength)
        {
            comboBox.Dispatcher.BeginInvoke(new Action(() =>
            {
                var textBox = GetEditableComboBoxTextBox(comboBox);
                if (textBox == null)
                {
                    if (string.Equals(comboBox.Text, text, StringComparison.Ordinal))
                        comboBox.Text = text;
                    return;
                }

                if (!string.Equals(textBox.Text, text, StringComparison.Ordinal))
                    return;

                int safeCaretIndex = Math.Clamp(caretIndex, 0, textBox.Text.Length);
                int safeSelectionLength = Math.Clamp(selectionLength, 0, textBox.Text.Length - safeCaretIndex);
                if (textBox.SelectionStart != safeCaretIndex || textBox.SelectionLength != safeSelectionLength)
                    textBox.Select(safeCaretIndex, safeSelectionLength);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void LogEdlMessage(string message, string? level)
        {
            Dispatcher.Invoke(() =>
            {
                if (EdlLogTextBox == null)
                    return;

                string normalized = (message ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
                string[] lines = normalized.Split('\n');
                foreach (string rawLine in lines)
                {
                    string one = (rawLine ?? "").TrimEnd();
                    if (string.IsNullOrWhiteSpace(one))
                        continue;

                    string stripped = StripExternalLogPrefix(one);
                    if (string.IsNullOrWhiteSpace(stripped))
                        continue;

                    if (stripped.StartsWith("__EDL_FLASH_BEGIN__|", StringComparison.OrdinalIgnoreCase))
                    {
                        string prefix = stripped["__EDL_FLASH_BEGIN__|".Length..].Trim();
                        BeginEdlFlashPartitionLine(prefix);
                        continue;
                    }
                    if (stripped.StartsWith("__EDL_PORT_CONNECT_BEGIN__|", StringComparison.OrdinalIgnoreCase))
                    {
                        string portName = stripped["__EDL_PORT_CONNECT_BEGIN__|".Length..].Trim();
                        BeginEdlPortProbeLine(portName);
                        continue;
                    }
                    if (stripped.StartsWith("__EDL_PORT_CONNECT_RESULT__|", StringComparison.OrdinalIgnoreCase))
                    {
                        string protocol = stripped["__EDL_PORT_CONNECT_RESULT__|".Length..].Trim();
                        CompleteEdlPortProbeLine(protocol);
                        continue;
                    }
                    if (stripped.StartsWith("__EDL_VERSION_HEADER__|", StringComparison.OrdinalIgnoreCase))
                    {
                        string content = stripped["__EDL_VERSION_HEADER__|".Length..].Trim();
                        AppendEdlVersionHeader(content);
                        continue;
                    }
                    if (stripped.StartsWith("__EDL_VERSION_ROW__|", StringComparison.OrdinalIgnoreCase))
                    {
                        string content = stripped["__EDL_VERSION_ROW__|".Length..];
                        int separator = content.IndexOf('|');
                        if (separator > 0)
                            AppendEdlVersionRow(content[..separator], content[(separator + 1)..]);
                        continue;
                    }
                    if (stripped.Equals("__EDL_VERSION_DONE__", StringComparison.OrdinalIgnoreCase))
                    {
                        AppendEdlVersionDone();
                        continue;
                    }
                    if (stripped.Equals("__EDL_FLASH_OK__", StringComparison.OrdinalIgnoreCase))
                    {
                        AppendEdlFlashPartitionStatus(true);
                        continue;
                    }
                    if (stripped.Equals("__EDL_FLASH_ERROR__", StringComparison.OrdinalIgnoreCase))
                    {
                        AppendEdlFlashPartitionStatus(false);
                        continue;
                    }

                    foreach (var item in TransformEdlLogLines(stripped, level))
                        AppendEdlLogLine(item.Line, item.Level);
                }

                EdlLogTextBox.ScrollToEnd();
            });
        }

        private FlowDocument EnsureEdlLogDocument()
        {
            FlowDocument? document = EdlLogTextBox?.Document;
            if (document != null)
                return document;

            document = new FlowDocument
            {
                PagePadding = new Thickness(0),
                ColumnWidth = 10000,
                ColumnGap = 0,
                TextAlignment = TextAlignment.Left,
                FontWeight = FontWeights.Normal,
                LineHeight = 19
            };
            if (EdlLogTextBox != null)
                EdlLogTextBox.Document = document;
            return document;
        }

        private void AppendEdlVersionHeader(string deviceSummary)
        {
            if (EdlLogTextBox == null)
                return;

            FlowDocument document = EnsureEdlLogDocument();
            var title = CreateEdlLogParagraph();
            title.Margin = new Thickness(0, 6, 0, 0.5);
            AppendEdlTimestamp(title);
            title.Inlines.Add(new Run("设备版本信息")
            {
                Foreground = EdlLogActionBrush,
                FontWeight = FontWeights.SemiBold
            });
            document.Blocks.Add(title);

            if (!string.IsNullOrWhiteSpace(deviceSummary))
            {
                var device = new Paragraph
                {
                    Margin = new Thickness(18, 1, 0, 5),
                    FontSize = 12.5
                };
                device.Inlines.Add(new Run(deviceSummary)
                {
                    Foreground = EdlLogActionBrush,
                    FontWeight = FontWeights.SemiBold
                });
                document.Blocks.Add(device);
            }

            var panel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical
            };
            var container = new BlockUIContainer(
                new Border
                {
                    Margin = new Thickness(18, 0, 2, 0),
                    BorderBrush = CreateEdlLogBrush(0xE2, 0xE8, 0xF0),
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Child = panel
                })
            {
                Margin = new Thickness(0)
            };
            document.Blocks.Add(container);
            _edlVersionInfoPanel = panel;
            TrimEdlLogDocument(document);
        }

        private void AppendEdlVersionRow(string label, string value)
        {
            if (EdlLogTextBox == null || string.IsNullOrWhiteSpace(value))
                return;

            FlowDocument document = EnsureEdlLogDocument();
            if (_edlVersionInfoPanel == null)
            {
                AppendEdlVersionHeader(string.Empty);
                document = EnsureEdlLogDocument();
            }

            var row = new Grid
            {
                Margin = new Thickness(0),
                MinHeight = 25
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelText = new TextBlock
            {
                Text = label.Trim(),
                Margin = new Thickness(4, 4, 6, 4),
                FontSize = 11,
                Foreground = EdlLogSecondaryBrush,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(labelText, 0);

            var valueText = new TextBlock
            {
                Text = value.Trim(),
                Margin = new Thickness(4, 4, 3, 4),
                FontSize = label.Equals("系统指纹", StringComparison.OrdinalIgnoreCase) ? 10.5 : 11,
                Foreground = EdlLogBodyBrush,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(valueText, 1);

            row.Children.Add(labelText);
            row.Children.Add(valueText);

            var rowBorder = new Border
            {
                BorderBrush = CreateEdlLogBrush(0xE2, 0xE8, 0xF0),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = row
            };
            _edlVersionInfoPanel!.Children.Add(rowBorder);
            TrimEdlLogDocument(document);
        }

        private void AppendEdlVersionDone()
        {
            if (EdlLogTextBox == null)
                return;

            FlowDocument document = EnsureEdlLogDocument();
            var paragraph = new Paragraph
            {
                Margin = new Thickness(18, 4, 0, 7)
            };
            paragraph.Inlines.Add(new Run("读取完成")
            {
                Foreground = EdlLogSuccessBrush,
                FontWeight = FontWeights.SemiBold
            });
            document.Blocks.Add(paragraph);
            _edlVersionInfoPanel = null;
            TrimEdlLogDocument(document);
        }

        private static void TrimEdlLogDocument(FlowDocument document)
        {
            const int maxLines = 2000;
            while (document.Blocks.Count > maxLines)
                document.Blocks.Remove(document.Blocks.FirstBlock);
        }

        private void BeginEdlFlashPartitionLine(string prefix)
        {
            if (EdlLogTextBox == null)
                return;
            FlowDocument doc = EnsureEdlLogDocument();
            var p = CreateEdlLogParagraph();
            AppendEdlTimestamp(p);
            AppendEdlOperationText(p, prefix);
            doc.Blocks.Add(p);

            const int maxLines = 2000;
            while (doc.Blocks.Count > maxLines)
                doc.Blocks.Remove(doc.Blocks.FirstBlock);

            _edlPendingFlashParagraph = p;
            _edlPendingFlashUsesArrowFormat = prefix.Contains("->[", StringComparison.Ordinal);
        }

        private void AppendEdlFlashPartitionStatus(bool ok)
        {
            if (_edlPendingFlashParagraph == null)
                return;

            if (!_edlPendingFlashUsesArrowFormat)
                _edlPendingFlashParagraph.Inlines.Add(new Run(" ") { Foreground = EdlLogBodyBrush });
            _edlPendingFlashParagraph.Inlines.Add(new Run(ok ? "OK" : "Error")
            {
                Foreground = ok ? EdlLogSuccessBrush : EdlLogErrorBrush,
                FontWeight = FontWeights.Bold
            });
            _edlPendingFlashParagraph = null;
            _edlPendingFlashUsesArrowFormat = false;
        }

        private void BeginEdlPortProbeLine(string portName)
        {
            if (EdlLogTextBox == null || _edlPendingPortProbeParagraph != null)
                return;

            FlowDocument doc = EnsureEdlLogDocument();
            var paragraph = CreateEdlLogParagraph();
            AppendEdlTimestamp(paragraph);
            paragraph.Inlines.Add(new Run($"连接串口{portName}...") { Foreground = EdlLogBodyBrush });
            doc.Blocks.Add(paragraph);

            const int maxLines = 2000;
            while (doc.Blocks.Count > maxLines)
                doc.Blocks.Remove(doc.Blocks.FirstBlock);

            _edlPendingPortProbeParagraph = paragraph;
        }

        private void CompleteEdlPortProbeLine(string protocol)
        {
            if (_edlPendingPortProbeParagraph == null)
                return;

            System.Windows.Media.Brush brush = protocol.Equals("Error", StringComparison.OrdinalIgnoreCase)
                ? EdlLogErrorBrush
                : protocol.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                    ? EdlLogWarningBrush
                    : EdlLogActionBrush;
            _edlPendingPortProbeParagraph.Inlines.Add(new Run(protocol)
            {
                Foreground = brush,
                FontWeight = FontWeights.SemiBold
            });
            _edlPendingPortProbeParagraph = null;
        }

        private IEnumerable<(string Line, string? Level)> TransformEdlLogLines(string? rawLine, string? level)
        {
            string line = (rawLine ?? "").TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
                yield break;

            string stripped = (line ?? "").Trim();
            if (string.IsNullOrWhiteSpace(stripped))
                yield break;

            string? effectiveLevel = level;

            if (stripped.StartsWith("[Warn]", StringComparison.OrdinalIgnoreCase))
                effectiveLevel ??= "warn";
            else if (stripped.Contains("WARN:", StringComparison.OrdinalIgnoreCase) || stripped.Contains("WARNING:", StringComparison.OrdinalIgnoreCase))
                effectiveLevel ??= "warn";
            else if (stripped.Contains("ERROR:", StringComparison.OrdinalIgnoreCase))
                effectiveLevel ??= "error";

            if (stripped.Contains("fh_loader 检测到设备仍在 Sahara", StringComparison.OrdinalIgnoreCase))
                yield break;

            if (stripped.Equals("引导发送成功", StringComparison.OrdinalIgnoreCase))
                yield break;

            if (stripped.Contains("NAK: MaxPayloadSizeToTargetInBytes", StringComparison.OrdinalIgnoreCase))
                yield break;

            if (IsEdlNoiseLine(stripped, effectiveLevel))
                yield break;

            yield return (stripped, effectiveLevel);
        }

        private static string StripExternalLogPrefix(string line)
        {
            string s = (line ?? "").Trim();
            if (s.Length == 0)
                return s;

            var m1 = Regex.Match(s, @"^\d{2}\.\d{2}\.\d{4}-\d{2}\.\d{2}\.\d{2}\s*:\s*");
            if (m1.Success)
                s = s[m1.Length..].TrimStart();

            var m2 = Regex.Match(s, @"^\d{2}:\d{2}:\d{2}:\s*");
            if (m2.Success)
                s = s[m2.Length..].TrimStart();

            return s;
        }

        private static bool IsEdlNoiseLine(string line, string? effectiveLevel)
        {
            string s = (line ?? "").Trim();
            if (s.Length == 0)
                return true;

            if (s.StartsWith("[Device]", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                return false;

            if (s.StartsWith("Sahara ", StringComparison.OrdinalIgnoreCase))
                return false;

            if (s.StartsWith("等待设备HELLO数据包", StringComparison.OrdinalIgnoreCase))
                return false;

            if (s.StartsWith("设备连接：", StringComparison.OrdinalIgnoreCase))
                return false;

            if (s.StartsWith("发送引导", StringComparison.OrdinalIgnoreCase))
                return false;

            if (s.StartsWith("引导发送成功", StringComparison.OrdinalIgnoreCase))
                return false;

            if (s.StartsWith("存储类型：", StringComparison.OrdinalIgnoreCase))
                return false;

            if (s.StartsWith("读取 LUN", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("解析 LUN", StringComparison.OrdinalIgnoreCase))
                return false;

            if (s.StartsWith("分区表流程读取结束", StringComparison.OrdinalIgnoreCase))
                return false;

            if (s.StartsWith("耗时", StringComparison.OrdinalIgnoreCase))
                return false;

            if (s.StartsWith("——————", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.StartsWith("使用 QSaharaServer ", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.Contains("QSaharaServer.exe", StringComparison.OrdinalIgnoreCase) || s.Contains("fh_loader.exe", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.StartsWith("************************************************", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.StartsWith("构建日期:", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.StartsWith("二次开发:", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.StartsWith("请勿使用", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.StartsWith("请求参数:", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.StartsWith("运行目录:", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.StartsWith("Sahara mappings:", StringComparison.OrdinalIgnoreCase))
                return true;

            if (Regex.IsMatch(s, @"^\d+:\s+.+\.(mbn|bin|elf)$", RegexOptions.IgnoreCase))
                return true;

            if (s.Contains("参数 -u 或 --portnumber 仅适用于 Windows", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.Contains("FIREHOSE MODE DETECTED", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.Contains("不支持FIREHOSE模式下通讯", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.Contains("Sahara protocol completed", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.Contains("Sahara protocol error", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.Contains("SAHARA_", StringComparison.OrdinalIgnoreCase) || s.Contains("RAW_DATA", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.Contains("FH_LOADER WAS CALLED", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.Contains("Output Dir Already exists", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.Contains("Current working dir", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.Contains("Showing network mappings", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.Contains("Sorting TAGS", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.Contains("Calling fopen", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.Contains("Looking for file", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.Contains("Took", StringComparison.OrdinalIgnoreCase) && s.Contains("seconds", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.StartsWith("Sending <configure>", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.Contains("There is a chance your target is in SAHARA mode", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.StartsWith("Log at '", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.StartsWith("Writing log", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.StartsWith("Base Version:", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.StartsWith("Binary build date:", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.StartsWith("Incremental Build version:", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.StartsWith("Successfully uploaded all images", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.Contains("|  ___", StringComparison.OrdinalIgnoreCase) || s.Contains("_____", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.StartsWith("[portstatus]", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.StartsWith("内存类型:", StringComparison.OrdinalIgnoreCase))
                return true;

            if (s.StartsWith("TargetName:", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private void AppendEdlLogLine(string line, string? level)
        {
            if (EdlLogTextBox == null)
                return;

            FlowDocument doc = EnsureEdlLogDocument();
            var p = CreateEdlLogParagraph();
            string normalized = NormalizeEdlLogLine(line);
            bool hasDoneTag = normalized.StartsWith("[Done]", StringComparison.OrdinalIgnoreCase);
            bool hasWarnTag = normalized.StartsWith("[Warn]", StringComparison.OrdinalIgnoreCase);
            bool hasErrorTag = normalized.StartsWith("[Error]", StringComparison.OrdinalIgnoreCase);
            bool isWarn = hasWarnTag || string.Equals(level, "warn", StringComparison.OrdinalIgnoreCase);
            bool isError = hasErrorTag
                || string.Equals(level, "error", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("Error from device", StringComparison.OrdinalIgnoreCase);
            bool isSuccess = hasDoneTag || string.Equals(level, "success", StringComparison.OrdinalIgnoreCase);
            bool isInfo = string.Equals(level, "info", StringComparison.OrdinalIgnoreCase);

            if (hasDoneTag)
                normalized = normalized["[Done]".Length..].TrimStart();
            else if (hasWarnTag)
                normalized = normalized["[Warn]".Length..].TrimStart();
            else if (hasErrorTag)
                normalized = normalized["[Error]".Length..].TrimStart();
            else if (normalized.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                normalized = normalized["ERROR:".Length..].TrimStart();

            AppendEdlTimestamp(p);
            if (hasDoneTag)
            {
                p.Inlines.Add(new Run("[Done] ")
                {
                    Foreground = EdlLogSuccessBrush,
                    FontWeight = FontWeights.SemiBold
                });
            }
            else if (isWarn)
            {
                p.Inlines.Add(new Run("[Warn] ")
                {
                    Foreground = EdlLogWarningBrush,
                    FontWeight = FontWeights.SemiBold
                });
            }

            Match rawProgramSelection = Regex.Match(
                normalized,
                @"^(已选择 RawProgram 文件，共 )(\d+)( 个)$",
                RegexOptions.IgnoreCase);
            Match rawProgramFile = Regex.Match(
                normalized,
                @"^(已加载)(.+)( 分区数：)(\d+)$",
                RegexOptions.IgnoreCase);
            Match rawProgramPatch = Regex.Match(
                normalized,
                @"^(本次刷写将自动应用刷机包中的Patch文件：)(.+)$",
                RegexOptions.IgnoreCase);

            if (rawProgramSelection.Success)
            {
                p.Inlines.Add(new Run(rawProgramSelection.Groups[1].Value) { Foreground = EdlLogBodyBrush });
                p.Inlines.Add(new Run(rawProgramSelection.Groups[2].Value)
                {
                    Foreground = EdlLogActionBrush,
                    FontWeight = FontWeights.SemiBold
                });
                p.Inlines.Add(new Run(rawProgramSelection.Groups[3].Value) { Foreground = EdlLogBodyBrush });
            }
            else if (rawProgramFile.Success)
            {
                p.Inlines.Add(new Run(rawProgramFile.Groups[1].Value) { Foreground = EdlLogBodyBrush });
                p.Inlines.Add(new Run(rawProgramFile.Groups[2].Value)
                {
                    Foreground = EdlLogActionBrush,
                    FontWeight = FontWeights.SemiBold
                });
                p.Inlines.Add(new Run(rawProgramFile.Groups[3].Value) { Foreground = EdlLogBodyBrush });
                p.Inlines.Add(new Run(rawProgramFile.Groups[4].Value)
                {
                    Foreground = EdlLogActionBrush,
                    FontWeight = FontWeights.SemiBold
                });
            }
            else if (rawProgramPatch.Success)
            {
                p.Inlines.Add(new Run(rawProgramPatch.Groups[1].Value) { Foreground = EdlLogBodyBrush });
                p.Inlines.Add(new Run(rawProgramPatch.Groups[2].Value)
                {
                    Foreground = EdlLogActionBrush,
                    FontWeight = FontWeights.SemiBold
                });
            }
            else if (normalized.StartsWith("设备连接：", StringComparison.OrdinalIgnoreCase))
            {
                string mode = normalized["设备连接：".Length..].Trim();
                p.Inlines.Add(new Run("设备连接：") { Foreground = EdlLogBodyBrush });
                p.Inlines.Add(new Run(mode)
                {
                    Foreground = EdlLogActionBrush,
                    FontWeight = FontWeights.SemiBold
                });
            }
            else if (normalized.StartsWith("LUN数量:", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("解析成功LUN数量:", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("分区数:", StringComparison.OrdinalIgnoreCase))
            {
                int idx = normalized.IndexOf(':');
                string label = normalized[..(idx + 1)];
                string value = normalized[(idx + 1)..].TrimStart();
                p.Inlines.Add(new Run(label + " ") { Foreground = EdlLogBodyBrush });
                p.Inlines.Add(new Run(value)
                {
                    Foreground = EdlLogActionBrush,
                    FontWeight = FontWeights.SemiBold
                });
            }
            else if (normalized.StartsWith("使用端口 ", StringComparison.OrdinalIgnoreCase))
            {
                const string prefix = "使用端口 ";
                p.Inlines.Add(new Run(prefix) { Foreground = EdlLogBodyBrush });
                p.Inlines.Add(new Run(normalized[prefix.Length..])
                {
                    Foreground = EdlLogInfoBrush,
                    FontWeight = FontWeights.SemiBold
                });
            }
            else if (Regex.Match(
                    normalized,
                    @"^设置启动槽位 \(([AB])\s*→\s*([AB])\)\.\.\.\s*(OK|Failed)$",
                    RegexOptions.IgnoreCase) is Match slotMatch
                && slotMatch.Success)
            {
                string sourceSlot = slotMatch.Groups[1].Value.ToUpperInvariant();
                string targetSlot = slotMatch.Groups[2].Value.ToUpperInvariant();
                string status = slotMatch.Groups[3].Value;
                p.Inlines.Add(new Run($"设置启动槽位 ({sourceSlot} → ") { Foreground = EdlLogBodyBrush });
                p.Inlines.Add(new Run(targetSlot)
                {
                    Foreground = EdlLogActionBrush,
                    FontWeight = FontWeights.SemiBold
                });
                p.Inlines.Add(new Run(")... ") { Foreground = EdlLogBodyBrush });
                p.Inlines.Add(new Run(status)
                {
                    Foreground = status.Equals("OK", StringComparison.OrdinalIgnoreCase) ? EdlLogSuccessBrush : EdlLogErrorBrush,
                    FontWeight = FontWeights.Bold
                });
            }
            else if (Regex.Match(
                         normalized,
                         @"^(.*?)(\s*(?:\.\.\.|…)\s*|\s+)(OK|Done|Error|Failed)$",
                         RegexOptions.IgnoreCase) is Match statusMatch
                     && statusMatch.Success)
            {
                string status = statusMatch.Groups[3].Value;
                AppendEdlOperationText(p, statusMatch.Groups[1].Value + statusMatch.Groups[2].Value);
                bool ok = status.Equals("OK", StringComparison.OrdinalIgnoreCase)
                    || status.Equals("Done", StringComparison.OrdinalIgnoreCase);
                p.Inlines.Add(new Run(status)
                {
                    Foreground = ok ? EdlLogSuccessBrush : EdlLogErrorBrush,
                    FontWeight = FontWeights.Bold
                });
            }
            else
            {
                var m = Regex.Match(normalized, @"^(COM)(\d+)(已连接|已断开)$", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    p.Inlines.Add(new Run($"COM{m.Groups[2].Value}") { Foreground = EdlLogBodyBrush });
                    p.Inlines.Add(new Run(m.Groups[3].Value)
                    {
                        Foreground = m.Groups[3].Value == "已连接" ? EdlLogSuccessBrush : EdlLogErrorBrush,
                        FontWeight = FontWeights.SemiBold
                    });
                }
                else if (isError)
                {
                    p.Inlines.Add(new Run(normalized) { Foreground = EdlLogErrorBrush });
                }
                else if (isWarn)
                {
                    p.Inlines.Add(new Run(normalized) { Foreground = EdlLogBodyBrush });
                }
                else if (isSuccess)
                {
                    p.Inlines.Add(new Run(normalized)
                    {
                        Foreground = EdlLogSuccessBrush,
                        FontWeight = FontWeights.SemiBold
                    });
                }
                else if (isInfo)
                {
                    p.Inlines.Add(new Run(normalized)
                    {
                        Foreground = EdlLogInfoBrush,
                        FontWeight = FontWeights.SemiBold
                    });
                }
                else
                {
                    AppendEdlOperationText(p, normalized);
                }
            }

            doc.Blocks.Add(p);

            const int maxLines = 2000;
            while (doc.Blocks.Count > maxLines)
                doc.Blocks.Remove(doc.Blocks.FirstBlock);
        }

        private static string NormalizeEdlLogLine(string line)
        {
            string s = (line ?? "").Trim();
            var m1 = Regex.Match(s, @"^(COM)\s*(\d+)\s*已连接$", RegexOptions.IgnoreCase);
            if (m1.Success)
                return $"COM{m1.Groups[2].Value}已连接";

            var m2 = Regex.Match(s, @"^(COM)\s*(\d+)\s*已断开$", RegexOptions.IgnoreCase);
            if (m2.Success)
                return $"COM{m2.Groups[2].Value}已断开";

            if (s.Equals("获取设备配置", StringComparison.OrdinalIgnoreCase))
                return "获取设备配置...";

            if (s.StartsWith("检测到9008端口:", StringComparison.OrdinalIgnoreCase))
                return "使用端口" + s["检测到9008端口:".Length..].Trim();
            if (s.Equals("发送 OPLUS 签名引导...", StringComparison.OrdinalIgnoreCase))
                return "发送引导...Done";
            if (s.Equals("发送 OPLUS digest...", StringComparison.OrdinalIgnoreCase))
                return "发送Digest...Done";
            if (s.Equals("发送 OPLUS verify command...", StringComparison.OrdinalIgnoreCase))
                return "VIP验证...Done";
            if (s.Equals("发送 OPLUS sign...", StringComparison.OrdinalIgnoreCase))
                return "发送Sign...Done";
            if (s.Equals("发送 OPLUS sha256init command...", StringComparison.OrdinalIgnoreCase))
                return "SHA256初始化...OK";
            if (s.Equals("配置 OPLUS Firehose...", StringComparison.OrdinalIgnoreCase))
                return "配置设备...Done";
            if (s.Equals("OPLUS 引导发送成功", StringComparison.OrdinalIgnoreCase))
                return "准备读取分区表...";

            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }

        private static string FormatEdlSpeed(double bytesPerSecond)
        {
            if (!(bytesPerSecond > 0))
                return "0.00 B/s";
            string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
            double value = bytesPerSecond;
            int unitIndex = 0;
            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024;
                unitIndex++;
            }
            return $"{value:0.00} {units[unitIndex]}";
        }

        private void UpdateEdlProgress(double percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            _edlPercent = percent;
            if (_edlProgressUpdateQueued)
                return;
            _edlProgressUpdateQueued = true;
            Dispatcher.BeginInvoke(() =>
            {
                _edlProgressUpdateQueued = false;
                if (EdlProgressBar == null)
                    return;
                EdlProgressBar.Value = _edlPercent;
                UpdateEdlProgressText();
            }, DispatcherPriority.Render);
        }

        private void UpdateEdlSpeedText(string? speedText)
        {
            _edlSpeedText = speedText ?? "";
            if (_edlSpeedUpdateQueued)
                return;
            _edlSpeedUpdateQueued = true;
            Dispatcher.BeginInvoke(() =>
            {
                _edlSpeedUpdateQueued = false;
                UpdateEdlProgressText();
            }, DispatcherPriority.Background);
        }

        private void UpdateEdlCurrentPartitionText(string? partitionText)
        {
            _edlCurrentPartitionText = partitionText ?? "";
            if (_edlPartitionUpdateQueued)
                return;
            _edlPartitionUpdateQueued = true;
            Dispatcher.BeginInvoke(() =>
            {
                _edlPartitionUpdateQueued = false;
                UpdateEdlProgressText();
            }, DispatcherPriority.Background);
        }

        private void UpdateEdlProgressText()
        {
            if (EdlProgressBar != null)
                EdlProgressBar.Tag = string.IsNullOrWhiteSpace(_edlSpeedText) ? "0.00 B/s" : _edlSpeedText;
            if (EdlProgressTextBlock != null)
                EdlProgressTextBlock.Text = string.IsNullOrWhiteSpace(_edlCurrentPartitionText) ? "" : _edlCurrentPartitionText;
        }

        private void UpdateEdlPortLabel(string? portName)
        {
            Dispatcher.Invoke(() =>
            {
                if (EdlPortComboBox == null)
                    return;
                if (!string.IsNullOrWhiteSpace(portName))
                {
                    var items = EdlPortComboBox.ItemsSource as IEnumerable<string>;
                    if (items == null || !items.Contains(portName, StringComparer.OrdinalIgnoreCase))
                    {
                        var list = BuildEdlPortOptions();
                        if (!list.Contains(portName, StringComparer.OrdinalIgnoreCase))
                            list.Insert(0, portName);
                        EdlPortComboBox.ItemsSource = list;
                    }
                    EdlPortComboBox.Text = portName;
                }
                else if (EdlPortComboBox.SelectedItem == null)
                {
                    EdlPortComboBox.Text = "";
                }
            });
        }

        internal static bool IsExcludedEdlRawProgramFile(string? filePath)
        {
            string fileName = Path.GetFileName(filePath ?? string.Empty);
            return Regex.IsMatch(
                fileName,
                @"^rawprogram[0-5]_(?:BLANK_GPT|WIPE_PARTITIONS)\.xml$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        internal static bool IsSelectableEdlRawProgramFile(string? filePath)
        {
            string fileName = Path.GetFileName(filePath ?? string.Empty);
            return fileName.StartsWith("rawprogram", StringComparison.OrdinalIgnoreCase)
                && fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                && !IsExcludedEdlRawProgramFile(fileName);
        }

        private static bool HasEdlRawProgramSelection(IEnumerable<string>? selectedFiles)
        {
            if (selectedFiles == null)
                return false;

            try
            {
                return selectedFiles.Any(filePath =>
                    File.Exists(filePath) &&
                    IsSelectableEdlRawProgramFile(filePath));
            }
            catch
            {
                return false;
            }
        }

        private static int[] GetEdlRawProgramPhysicalLuns(IEnumerable<string>? selectedFiles)
        {
            if (selectedFiles == null)
                return Array.Empty<int>();

            var luns = new HashSet<int>();
            foreach (string xmlPath in selectedFiles.Where(File.Exists))
            {
                try
                {
                    XDocument document = XDocument.Load(xmlPath);
                    foreach (XElement program in document
                                 .Descendants()
                                 .Where(element => element.Name.LocalName.Equals(
                                     "program",
                                     StringComparison.OrdinalIgnoreCase)))
                    {
                        string value = program.Attribute("physical_partition_number")?.Value?.Trim()
                            ?? string.Empty;
                        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        {
                            if (int.TryParse(
                                    value[2..],
                                    NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture,
                                    out int hexLun)
                                && hexLun >= 0)
                            {
                                luns.Add(hexLun);
                            }
                        }
                        else if (int.TryParse(
                                     value,
                                     NumberStyles.Integer,
                                     CultureInfo.InvariantCulture,
                                     out int lun)
                                 && lun >= 0)
                        {
                            luns.Add(lun);
                        }
                    }
                }
                catch
                {
                    // XML analysis already reports malformed files when the package is selected.
                }
            }

            return luns.OrderBy(lun => lun).ToArray();
        }

        private string[]? ShowEdlRawProgramSelectionDialog()
        {
            string? initialDirectory = Directory.Exists(_edlFlashPackDir)
                ? _edlFlashPackDir
                : null;
            return FilteredOpenFileDialog.Show(
                this,
                "选择 RawProgram XML 文件",
                initialDirectory,
                IsSelectableEdlRawProgramFile);
        }

        private static bool IsEdlRawProgramGptEntry(string? label, string? fileName)
        {
            string normalizedLabel = label?.Trim() ?? string.Empty;
            string normalizedFileName = Path.GetFileName(fileName?.Trim() ?? string.Empty);
            return normalizedLabel.Equals("PrimaryGPT", StringComparison.OrdinalIgnoreCase)
                || normalizedLabel.Equals("BackupGPT", StringComparison.OrdinalIgnoreCase)
                || normalizedFileName.StartsWith("gpt_main", StringComparison.OrdinalIgnoreCase)
                || normalizedFileName.StartsWith("gpt_backup", StringComparison.OrdinalIgnoreCase);
        }

        private void LogEdlRawProgramAnalysis(
            string programDirectory,
            IReadOnlyCollection<string> selectedFiles,
            bool hasDynamicSuperDefinition)
        {
            foreach (string xmlPath in selectedFiles
                         .Select(Path.GetFullPath)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    XDocument document = XDocument.Load(xmlPath);
                    int partitionEntryCount = 0;
                    foreach (XElement program in document
                                 .Descendants()
                                 .Where(element => element.Name.LocalName.Equals("program", StringComparison.OrdinalIgnoreCase)))
                    {
                        string label = program.Attribute("label")?.Value?.Trim() ?? string.Empty;
                        string fileName = program.Attribute("filename")?.Value?.Trim().Trim('"') ?? string.Empty;
                        if (IsEdlRawProgramGptEntry(label, fileName))
                            continue;

                        bool isValidEntry = !string.IsNullOrWhiteSpace(label)
                            && !string.IsNullOrWhiteSpace(program.Attribute("SECTOR_SIZE_IN_BYTES")?.Value)
                            && !string.IsNullOrWhiteSpace(program.Attribute("start_sector")?.Value)
                            && !string.IsNullOrWhiteSpace(program.Attribute("num_partition_sectors")?.Value)
                            && !string.IsNullOrWhiteSpace(program.Attribute("physical_partition_number")?.Value);
                        if (isValidEntry)
                            partitionEntryCount++;
                    }

                    LogEdlMessage(
                        $"已加载{Path.GetFileName(xmlPath)} 分区数：{partitionEntryCount}",
                        null);
                }
                catch (Exception ex)
                {
                    LogEdlMessage($"分析 {Path.GetFileName(xmlPath)} 失败: {ex.Message}", "warn");
                }
            }

            string[] patchFileNames;
            try
            {
                patchFileNames = Directory.GetFiles(programDirectory, "patch*.xml", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name =>
                    {
                        string value = name!;
                        return char.ToUpperInvariant(value[0]) + value[1..];
                    })
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                patchFileNames = Array.Empty<string>();
            }
            if (patchFileNames.Length > 0)
            {
                string patchDisplay = patchFileNames.Length == 1
                    ? patchFileNames[0]
                    : $"{patchFileNames[0]} ~ {patchFileNames[^1]}";
                LogEdlMessage($"本次刷写将自动应用刷机包中的Patch文件：{patchDisplay}", null);
            }
            if (hasDynamicSuperDefinition)
                LogEdlMessage("检测到 Super 动态分区元数据，且用户未合并Super，本次刷写将自动使用Meta免合并功能.", "info");
        }

        private void EdlFlashPackBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_edlEngine == null)
                return;

            string[]? chosenFiles;
            try
            {
                chosenFiles = ShowEdlRawProgramSelectionDialog();
            }
            catch (Exception ex)
            {
                LogEdlMessage("打开 RawProgram 文件选择窗口失败: " + ex.Message, "error");
                return;
            }
            if (chosenFiles == null || chosenFiles.Length == 0)
                return;

            string[] selectedFiles = chosenFiles
                .Where(File.Exists)
                .ToArray();
            if (selectedFiles.Length == 0)
            {
                LogEdlMessage("未选择可用的 rawprogram XML", "error");
                return;
            }

            string? firstDir = Path.GetDirectoryName(selectedFiles[0]);
            if (string.IsNullOrEmpty(firstDir))
                return;
            if (selectedFiles.Any(file =>
                    !string.Equals(
                        Path.GetDirectoryName(file),
                        firstDir,
                        StringComparison.OrdinalIgnoreCase)))
            {
                LogEdlMessage("请选择同一目录内的 rawprogram XML 文件", "error");
                return;
            }

            _edlFlashPackDir = firstDir;
            _edlFlashPackProgramFiles = selectedFiles;
            EdlFlashPackTextBox.Text = firstDir;
            try
            {
                LogEdlMessage($"已选择 RawProgram 文件，共 {selectedFiles.Length} 个", null);
                var result = _edlEngine.ParseFlashPack(firstDir, selectedFiles);
                if (!string.IsNullOrEmpty(result.ProgrammerPath))
                {
                    string existing = EdlLoaderTextBox?.Text?.Trim() ?? "";
                    bool hasConfiguredLoader = !string.IsNullOrWhiteSpace(existing)
                        && (File.Exists(existing)
                            || existing.StartsWith("cloud-loader://", StringComparison.OrdinalIgnoreCase));
                    if (!hasConfiguredLoader)
                        EdlLoaderTextBox.Text = result.ProgrammerPath;
                }
                bool hasDynamicSuperDefinition = DynamicSuperPlanner.FindDefinition(firstDir) != null;
                LogEdlRawProgramAnalysis(
                    firstDir,
                    selectedFiles,
                    hasDynamicSuperDefinition);
                if (_edlPartitions.Count > 0)
                {
                    var programByKey = result.Partitions
                        .GroupBy(p => $"{p.Label}|{p.Lun}", StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                    foreach (var item in _edlPartitions)
                    {
                        string key = $"{item.Model.Label}|{item.Model.Lun}";
                        if (programByKey.TryGetValue(key, out var programPartition))
                        {
                            string? resolved = ResolveExistingPartitionFilePath(firstDir, programPartition.FilePath);
                            bool useDynamicSuper = resolved == null
                                && hasDynamicSuperDefinition
                                && IsEdlSuperPartitionLabel(programPartition.Label);
                            item.SetFilePath(resolved);
                            item.SetDynamicSuperAvailable(useDynamicSuper);
                            item.IsSelected = resolved != null || useDynamicSuper;
                        }
                        else
                        {
                            item.SetFilePath(null);
                            item.SetDynamicSuperAvailable(false);
                            item.IsSelected = false;
                        }
                    }
                    ApplyEdlPartitionProtectionState();
                    EdlPartitionDataGrid.Items.Refresh();
                }
                else
                {
                    LoadEdlPartitionsFromProgram(result.Partitions, firstDir);
                }
            }
            catch (Exception ex)
            {
                LogEdlMessage(ex.Message, "error");
            }
        }
        private void EdlLoaderBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Programmer (*.mbn;*.elf;*.melf;*.xml)|*.mbn;*.elf;*.melf;*.xml|All files (*.*)|*.*",
                Title = "选择引导文件"
            };
            if (dlg.ShowDialog(this) == true)
                EdlLoaderTextBox.Text = dlg.FileName;
        }

        private void LoadEdlPartitions(List<EdlPartitionInfo> partitions, bool selectedByDefault)
        {
            _edlPartitions.Clear();
            foreach (var p in partitions)
            {
                _edlPartitions.Add(new EdlPartitionViewItem(p, selectedByDefault));
            }
            ApplyEdlPartitionProtectionState();
        }

        private void LoadEdlPartitionsFromProgram(List<EdlPartitionInfo> partitions, string programDir)
        {
            _edlPartitions.Clear();
            bool hasDynamicSuperDefinition = DynamicSuperPlanner.FindDefinition(programDir) != null;
            foreach (var p in partitions)
            {
                string? resolved = ResolveExistingPartitionFilePath(programDir, p.FilePath);
                bool useDynamicSuper = resolved == null
                    && hasDynamicSuperDefinition
                    && IsEdlSuperPartitionLabel(p.Label);
                var item = new EdlPartitionViewItem(p, resolved != null || useDynamicSuper);
                item.SetFilePath(resolved);
                item.SetDynamicSuperAvailable(useDynamicSuper);
                _edlPartitions.Add(item);
            }
            ApplyEdlPartitionProtectionState();
        }

        private static bool IsEdlSuperPartitionLabel(string? label)
        {
            return !string.IsNullOrWhiteSpace(label)
                && (label.Equals("super", StringComparison.OrdinalIgnoreCase)
                    || label.StartsWith("super_", StringComparison.OrdinalIgnoreCase));
        }

        private static string? ResolveExistingPartitionFilePath(string programDir, string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;
            if (File.Exists(filePath))
                return Path.GetFullPath(filePath);
            if (!Path.IsPathRooted(filePath))
            {
                string combined = Path.GetFullPath(Path.Combine(programDir, filePath));
                if (File.Exists(combined))
                    return combined;
            }
            return null;
        }

        private async void RunEdlAction(Func<Task> action)
        {
            if (!await _edlOperationLock.WaitAsync(0).ConfigureAwait(true))
            {
                LogEdlMessage("已有 EDL 操作正在执行，请等待当前任务结束。", "warn");
                return;
            }

            try
            {
                SetEdlControlsEnabled(false);
                UpdateEdlSpeedText(null);
                UpdateEdlProgress(0);
                UpdateEdlCurrentPartitionText(null);
                await Dispatcher.Yield(DispatcherPriority.Render);
                await action().ConfigureAwait(true);
                _edlEngine?.ThrowIfStopRequested();
            }
            catch (OperationCanceledException) when (_edlEngine?.IsStopRequested == true)
            {
                UpdateEdlSpeedText(null);
                LogEdlMessage("操作已停止.", "warn");
            }
            catch (Exception ex)
            {
                _edlEngine?.HandleOperationFailure(ex);
                LogEdlMessage("__EDL_PORT_CONNECT_RESULT__|Error", null);
                if (TryBuildEdlPortOpenFailedLog(ex, out string portOpenFailedLog))
                {
                    LogEdlMessage(portOpenFailedLog, "error");
                    return;
                }
                if (TryBuildEdlLoaderSendTimeoutLog(ex, out string loaderSendTimeoutLog))
                {
                    LogEdlMessage(loaderSendTimeoutLog, "error");
                    ResetEdlSendLoaderCheckBox();
                    return;
                }
                if (!string.IsNullOrEmpty(ex.Message) && ex.Message.Contains("串口未打开", StringComparison.OrdinalIgnoreCase))
                {
                    LogEdlMessage("打开串口...", "error");
                    return;
                }
                if (!string.IsNullOrEmpty(ex.Message) && ex.Message.Contains("未选择引导", StringComparison.OrdinalIgnoreCase))
                {
                    LogEdlMessage("请先选择引导...", "error");
                    return;
                }
                LogEdlMessage(ex.Message, "error");
            }
            finally
            {
                UpdateEdlSpeedText(null);
                UpdateEdlCurrentPartitionText(null);
                SetEdlControlsEnabled(true);
                _edlOperationLock.Release();
            }
        }

        private static bool TryBuildEdlPortOpenFailedLog(Exception ex, out string logText)
        {
            logText = "";
            string message = ex.Message ?? "";
            if (!message.StartsWith(EdlPortOpenFailedPrefix, StringComparison.Ordinal))
                return false;
            string portName = message.Substring(EdlPortOpenFailedPrefix.Length).Trim();
            logText = string.IsNullOrWhiteSpace(portName)
                ? "打开端口...Failed"
                : $"打开端口{portName}...Failed";
            return true;
        }

        private static bool TryBuildEdlLoaderSendTimeoutLog(Exception ex, out string logText)
        {
            logText = "";
            string message = ex.Message ?? "";
            if (!message.StartsWith(EdlLoaderSendTimeoutPrefix, StringComparison.Ordinal))
                return false;
            logText = "发送引导超时，请重新进入9008后重试...";
            return true;
        }

        internal static Exception CreateEdlLoaderSendTimeoutException(string? loaderName)
        {
            return new Exception(EdlLoaderSendTimeoutPrefix + (loaderName ?? "").Trim());
        }

        internal static Exception CreateEdlPortOpenFailedException(string? portName)
        {
            return new Exception(EdlPortOpenFailedPrefix + NormalizeEdlPortNameForLog(portName));
        }

        internal static bool LooksLikeNativePortOpenFailed(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.Contains("Failed to open com port handle", StringComparison.OrdinalIgnoreCase)
                || text.Contains("无法打开端口", StringComparison.OrdinalIgnoreCase)
                || text.Contains("端口被占用", StringComparison.OrdinalIgnoreCase)
                || text.Contains("或许是被占用", StringComparison.OrdinalIgnoreCase)
                || text.Contains("请求的资源在使用中", StringComparison.OrdinalIgnoreCase)
                || text.Contains("The requested resource is in use", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Access to the port", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeEdlPortNameForLog(string? portName)
        {
            if (string.IsNullOrWhiteSpace(portName))
                return "";
            var match = Regex.Match(portName, @"COM\s*(\d+)", RegexOptions.IgnoreCase);
            if (match.Success)
                return "COM" + match.Groups[1].Value;
            return portName.Replace(@"\\.\", "", StringComparison.OrdinalIgnoreCase).Trim();
        }

        private void SetEdlControlsEnabled(bool enabled)
        {
            if (!enabled)
                _edlEngine?.BeginOperation();

            Dispatcher.Invoke(() =>
            {
                if (EdlReadGptButton != null) EdlReadGptButton.IsEnabled = enabled;
                if (EdlReadPartButton != null) EdlReadPartButton.IsEnabled = enabled;
                if (EdlWritePartButton != null) EdlWritePartButton.IsEnabled = enabled;
                if (EdlErasePartButton != null) EdlErasePartButton.IsEnabled = enabled;
                if (EdlResetComboBox != null) EdlResetComboBox.IsEnabled = enabled;
                if (EdlFlashPackBrowseButton != null) EdlFlashPackBrowseButton.IsEnabled = enabled;
                if (EdlLoaderBrowseButton != null) EdlLoaderBrowseButton.IsEnabled = enabled;
                if (EdlFactoryResetButton != null) EdlFactoryResetButton.IsEnabled = enabled;
                if (EdlWriteGptButton != null) EdlWriteGptButton.IsEnabled = enabled;
                if (EdlFormatLunButton != null) EdlFormatLunButton.IsEnabled = enabled;
                if (EdlBackupModemFingerprintButton != null) EdlBackupModemFingerprintButton.IsEnabled = enabled;
                if (EdlBackupGptButton != null) EdlBackupGptButton.IsEnabled = enabled;
                if (EdlForceOemButton != null) EdlForceOemButton.IsEnabled = enabled;
                if (EdlSlotManagementButton != null) EdlSlotManagementButton.IsEnabled = enabled;
                if (EdlReadVersionInfoButton != null) EdlReadVersionInfoButton.IsEnabled = enabled;
                if (EdlBuiltInLoaderComboBox != null) EdlBuiltInLoaderComboBox.IsEnabled = enabled;
                if (EdlPortComboBox != null) EdlPortComboBox.IsEnabled = enabled;
                if (EdlSendLoaderCheckBox != null) EdlSendLoaderCheckBox.IsEnabled = enabled;
                if (EdlSkipSafeCheckBox != null) EdlSkipSafeCheckBox.IsEnabled = enabled;
                if (EdlSkipDataCheckBox != null) EdlSkipDataCheckBox.IsEnabled = enabled;
                if (EdlGenerateProgramCheckBox != null) EdlGenerateProgramCheckBox.IsEnabled = enabled;
                if (EdlAutoRebootCheckBox != null) EdlAutoRebootCheckBox.IsEnabled = enabled;
                if (EdlFactoryResetCheckBox != null) EdlFactoryResetCheckBox.IsEnabled = enabled;
                if (EdlPartitionSearchComboBox != null) EdlPartitionSearchComboBox.IsEnabled = enabled;
                if (EdlPartitionDataGrid != null) EdlPartitionDataGrid.IsEnabled = enabled;
                UpdateEdlPartitionOverlayState(!enabled);
            });
        }

        private void UpdateEdlPartitionOverlayState(bool operationRunning)
        {
            bool showOverlay = operationRunning || _edlCloudLoadersLoading;
            if (EdlPartitionContentGrid != null)
            {
                EdlPartitionContentGrid.Opacity = showOverlay ? 0.68 : 1;
                EdlPartitionContentGrid.Effect = showOverlay
                    ? new System.Windows.Media.Effects.BlurEffect
                    {
                        Radius = 4,
                        KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
                        RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
                    }
                    : null;
            }

            if (EdlStopOperationOverlay != null)
                EdlStopOperationOverlay.Visibility = showOverlay ? Visibility.Visible : Visibility.Collapsed;
            if (EdlOperationOverlayText != null)
            {
                EdlOperationOverlayText.Text = operationRunning
                    ? "操作正在进行中，请勿断开数据线..."
                    : "正在加载云端引导中...";
            }
            if (EdlStopOperationButton != null)
            {
                EdlStopOperationButton.Visibility = operationRunning ? Visibility.Visible : Visibility.Collapsed;
                EdlStopOperationButton.IsEnabled = operationRunning;
                EdlStopOperationButton.Content = "停止操作";
            }
        }

        private void EdlStopOperationButton_Click(object sender, RoutedEventArgs e)
        {
            if (_edlEngine == null || !_edlEngine.RequestStop())
                return;

            if (EdlStopOperationButton != null)
            {
                EdlStopOperationButton.IsEnabled = false;
                EdlStopOperationButton.Content = "正在停止...";
            }
            LogEdlMessage("已请求停止，当前分区完成后将结束操作。", "warn");
        }

        private void EdlPartSelectAllCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool selected = EdlPartSelectAllCheckBox.IsChecked == true;
            bool protect = EdlSkipSafeCheckBox?.IsChecked == true;
            bool protectData = EdlSkipDataCheckBox?.IsChecked == true;
            foreach (var item in _edlPartitions)
                item.IsSelected = (protect && item.IsBasebandPartition) || (protectData && item.IsDataPartition) ? false : selected;
            EdlPartitionDataGrid.Items.Refresh();
        }

        private void EdlToggleSelectAllCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool protect = EdlSkipSafeCheckBox?.IsChecked == true;
            bool protectData = EdlSkipDataCheckBox?.IsChecked == true;
            var selectable = _edlPartitions
                .Where(p => !(protect && p.IsBasebandPartition))
                .Where(p => !(protectData && p.IsDataPartition))
                .ToList();
            bool allSelected = selectable.Count > 0 && selectable.All(p => p.IsSelected);
            foreach (var item in selectable)
                item.IsSelected = !allSelected;
            if (protect)
            {
                foreach (var item in _edlPartitions.Where(p => p.IsBasebandPartition))
                    item.IsSelected = false;
            }
            if (protectData)
            {
                foreach (var item in _edlPartitions.Where(p => p.IsDataPartition))
                    item.IsSelected = false;
            }
            EdlPartitionDataGrid.Items.Refresh();
        }

        private void EdlDevMgrButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "devmgmt.msc",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LogEdlMessage("无法打开设备管理器: " + ex.Message, "error");
            }
        }

        private void EdlResetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EdlResetComboBox == null || EdlResetComboBox.SelectedIndex < 0)
                return;

            // 获取选中的选项
            var selectedItem = EdlResetComboBox.SelectedItem as ComboBoxItem;
            if (selectedItem == null)
                return;

            string selectedContent = selectedItem.Content?.ToString() ?? "";
            if (EdlSendLoaderCheckBox != null)
                EdlSendLoaderCheckBox.IsChecked = true;

            // 判断选中的选项
            if (selectedContent == "重启到系统")
            {
                // 执行重启到系统的逻辑
                if (_edlEngine == null || _edlEngine.FirehoseServer == null)
                {
                    System.Windows.MessageBox.Show("请先连接设备并执行一次操作。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    // 重置选择
                    EdlResetComboBox.SelectedIndex = -1;
                    return;
                }
                RunEdlResetAction(async () =>
                {
                    await Task.Run(() =>
                    {
                        try
                        {
                            _edlEngine.FirehoseServer.ResetDevice().CheckAndThrow();
                            LogEdlMessage("设备已重启到系统", null);
                        }
                        finally
                        {
                            _edlEngine.ReleaseEdlPort();
                        }
                    }).ConfigureAwait(true);
                });
            }
            else if (selectedContent == "重启到EDL")
            {
                RunEdlResetAction(async () =>
                {
                    await Task.Run(() =>
                    {
                        LogEdlMessage("__EDL_FLASH_BEGIN__|重启至EDL...", null);
                        try
                        {
                            ExecuteResetToEdl();
                            LogEdlMessage("__EDL_FLASH_OK__", null);
                            LogEdlMessage("__EDL_FLASH_BEGIN__|释放端口...", null);
                            _edlEngine?.ReleaseEdlPort();
                            LogEdlMessage("__EDL_FLASH_OK__", null);
                        }
                        catch
                        {
                            LogEdlMessage("__EDL_FLASH_ERROR__", null);
                            throw;
                        }
                    }).ConfigureAwait(true);
                });
            }
            else if (selectedContent == "重启到Recovery")
            {
                RunEdlResetAction(async () =>
                {
                    await Task.Run(() =>
                    {
                        ExecuteRebootWithMiscPatch("edr.img", "重启到Recovery...");
                    }).ConfigureAwait(true);
                });
            }
            else if (selectedContent == "重启到FastbootD")
            {
                RunEdlResetAction(async () =>
                {
                    await Task.Run(() =>
                    {
                        ExecuteRebootWithMiscPatch("edf.img", "重启到FastbootD...");
                    }).ConfigureAwait(true);
                });
            }
            // 其他选项的逻辑稍后添加

            // 重置 ComboBox 到默认显示状态
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (EdlResetComboBox != null)
                    EdlResetComboBox.SelectedIndex = -1;
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void RunEdlResetAction(Func<Task> action)
        {
            RunEdlAction(async () =>
            {
                try
                {
                    await action().ConfigureAwait(true);
                }
                finally
                {
                    ResetEdlSendLoaderCheckBox();
                }
            });
        }

        private void ResetEdlSendLoaderCheckBox()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (EdlSendLoaderCheckBox != null)
                    EdlSendLoaderCheckBox.IsChecked = true;
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void ExecuteRebootWithMiscPatch(
            string patchFileName,
            string rebootLine,
            string? resolvedLoaderPath = null,
            bool? sendLoaderOverride = null,
            EdlOplusLoaderPackage? oplusLoaderPackage = null)
        {
            if (_edlEngine == null)
                throw new Exception("未初始化 EDL 引擎");

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string patchPath = Path.Combine(baseDir, "tmp", patchFileName);
            if (!File.Exists(patchPath))
                throw new Exception("未找到补丁文件: " + patchPath);

            string loaderPath = "";
            bool sendLoader = false;
            Dispatcher.Invoke(() =>
            {
                loaderPath = EdlLoaderTextBox?.Text ?? "";
                sendLoader = EdlSendLoaderCheckBox?.IsChecked == true;
            });
            if (resolvedLoaderPath != null)
                loaderPath = resolvedLoaderPath;
            if (sendLoaderOverride.HasValue)
                sendLoader = sendLoaderOverride.Value;

            _edlEngine.PrepareForOperation(loaderPath, sendLoader, false, oplusLoaderPackage);
            if (_edlEngine.FirehoseServer == null)
                throw new Exception("未连接到 Firehose");

            EdlPartitionInfo? miscModel = null;
            var miscView = _edlPartitions.FirstOrDefault(p => p.Label.Equals("misc", StringComparison.OrdinalIgnoreCase));
            if (miscView != null)
                miscModel = miscView.Model;
            else
                miscModel = _edlEngine.ReadGptPartitionsFromDevice().FirstOrDefault(p => p.Label.Equals("misc", StringComparison.OrdinalIgnoreCase));

            if (miscModel == null)
                throw new Exception("未找到 misc 分区");

            miscModel.FilePath = patchPath;
            _edlEngine.FlashWithoutProgram(new List<EdlPartitionInfo> { miscModel }, "写入补丁...", true);

            LogEdlMessage($"__EDL_FLASH_BEGIN__|{rebootLine}", null);
            try
            {
                _edlEngine.FirehoseServer.ResetDevice().CheckAndThrow();
                LogEdlMessage("__EDL_FLASH_OK__", null);
            }
            catch
            {
                LogEdlMessage("__EDL_FLASH_ERROR__", null);
                throw;
            }
            finally
            {
                _edlEngine.ReleaseEdlPort();
            }
        }

        private void ExecuteResetToEdl()
        {
            bool firehoseSent = false;
            if (_edlEngine != null)
                _edlEngine.TryRebootToEdlViaFirehose(8000, out firehoseSent);
            if (firehoseSent)
                _edlEngine?.ReleaseEdlPort();
            if (firehoseSent && WaitForEdlPort(8000))
                return;
            string? portName = RunChkdevQcedl();
            if (string.IsNullOrEmpty(portName))
                throw new Exception("没有设备连接");
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string tmpDir = Path.Combine(baseDir, "tmp");
            Directory.CreateDirectory(tmpDir);
            string tmpPath = Path.Combine(tmpDir, "tmp.xml");
            File.WriteAllText(tmpPath, "<?xml version=\"1.0\" ?><data><power value=\"reset_to_edl\" /></data>");

            string exePath = Path.Combine(baseDir, "fh_loader.exe");
            if (!File.Exists(exePath))
                throw new Exception("未找到 fh_loader.exe，请确认程序目录");
            string portArg = portName.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase)
                ? portName
                : @"\\.\\" + portName;
            string[] memoryNames = { "ufs", "emmc", "spinor" };
            foreach (string memory in memoryNames)
            {
                string portTracePath = Path.Combine(tmpDir, "port_trace.txt");
                string args = $"--port={portArg} --memoryname={memory} --sendxml=\"{tmpPath}\" --mainoutputdir=\"{tmpDir}\" --porttracename=\"{portTracePath}\" --noautoconfigure --noprompt";
                ProcessStartInfo startInfo = new ProcessStartInfo(exePath, args)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? baseDir
                };
                var result = RunProcessWithTimeout(startInfo, 15000);
                if (LooksLikeNativePortOpenFailed(result.Output + "\n" + result.Error))
                    throw CreateEdlPortOpenFailedException(portName);
                if (!result.TimedOut && result.ExitCode == 0)
                {
                    _edlEngine?.ReleaseEdlPort();
                    return;
                }
            }

            throw new Exception("重启到EDL失败");
        }

        private bool WaitForEdlPort(int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (FindQcedlPorts().Count > 0)
                    return true;
                Thread.Sleep(200);
            }
            return false;
        }

        private string? RunChkdevQcedl()
        {
            string? manualPort = null;
            if (EdlPortComboBox != null)
                Dispatcher.Invoke(() => manualPort = EdlPortComboBox.Text?.Trim());
            var ports = FindQcedlPorts();
            if (!string.IsNullOrWhiteSpace(manualPort))
            {
                var match = Regex.Match(manualPort, @"COM\s*(\d+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string normalizedManualPort = "COM" + match.Groups[1].Value;
                    if (ports.Contains(normalizedManualPort, StringComparer.OrdinalIgnoreCase))
                        return normalizedManualPort;
                }
            }

            if (ports.Count == 1)
                return ports[0];
            if (ports.Count > 1)
                throw new Exception("检测到多个9008设备，请只保留一台设备");
            return null;
        }

        private string GetEdlPortDisplayName(string portName)
        {
            string normalizedPort = Regex.Match(
                    portName ?? string.Empty,
                    @"COM\s*(\d+)",
                    RegexOptions.IgnoreCase) is { Success: true } match
                ? "COM" + match.Groups[1].Value
                : (portName ?? string.Empty).Trim();
            return _edlPortDisplayNames.TryGetValue(normalizedPort, out string? displayName)
                && !string.IsNullOrWhiteSpace(displayName)
                    ? displayName
                    : normalizedPort;
        }

        private List<string> FindQcedlPorts()
        {
            var ports = new List<string>();
            foreach (var descriptor in EnumeratePresentQcedlPorts())
            {
                ports.Add(descriptor.PortName);
                _edlPortDisplayNames[descriptor.PortName] = descriptor.DisplayName;
            }
            return ports;
        }

        internal static List<(string PortName, string DisplayName)> EnumeratePresentQcedlPorts()
        {
            lock (EdlPortEnumerationSync)
            {
                var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        "SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
                    foreach (var item in searcher.Get())
                    {
                        string? name = item["Name"]?.ToString();
                        string? pnpId = item["PNPDeviceID"]?.ToString();
                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        bool nameMatch = name.Contains(
                                             "Qualcomm HS-USB QDLoader 9008",
                                             StringComparison.OrdinalIgnoreCase)
                                         || name.Contains(
                                             "Quectel QDLoader 9008",
                                             StringComparison.OrdinalIgnoreCase);
                        bool vidPidMatch = !string.IsNullOrWhiteSpace(pnpId)
                            && Regex.IsMatch(
                                pnpId,
                                @"VID_05C6&PID_9008",
                                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                        if (!nameMatch && !vidPidMatch)
                            continue;

                        Match comMatch = Regex.Match(name, @"\(COM\s*(\d+)\)", RegexOptions.IgnoreCase);
                        if (!comMatch.Success)
                            continue;

                        string portName = "COM" + comMatch.Groups[1].Value;
                        found[portName] = name.Trim();
                    }
                }
                catch
                {
                    // Setup may enumerate the port before WMI refreshes. The registry
                    // fallback below is still constrained by the currently present COM list.
                }

                try
                {
                    var presentPorts = new HashSet<string>(
                        SerialPort.GetPortNames().Select(NormalizeEdlPortNameForLog),
                        StringComparer.OrdinalIgnoreCase);
                    using Microsoft.Win32.RegistryKey? usbRoot = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"SYSTEM\CurrentControlSet\Enum\USB",
                        writable: false);
                    if (usbRoot != null)
                    {
                        foreach (string hardwareId in usbRoot.GetSubKeyNames())
                        {
                            if (!hardwareId.Contains("VID_05C6&PID_9008", StringComparison.OrdinalIgnoreCase))
                                continue;

                            using Microsoft.Win32.RegistryKey? hardwareKey = usbRoot.OpenSubKey(hardwareId, writable: false);
                            if (hardwareKey == null)
                                continue;

                            foreach (string instanceName in hardwareKey.GetSubKeyNames())
                            {
                                using Microsoft.Win32.RegistryKey? instanceKey = hardwareKey.OpenSubKey(instanceName, writable: false);
                                using Microsoft.Win32.RegistryKey? parametersKey = instanceKey?.OpenSubKey("Device Parameters", writable: false);
                                string portName = NormalizeEdlPortNameForLog(parametersKey?.GetValue("PortName")?.ToString());
                                if (string.IsNullOrWhiteSpace(portName) || !presentPorts.Contains(portName))
                                    continue;

                                string displayName = instanceKey?.GetValue("FriendlyName")?.ToString()?.Trim()
                                    ?? $"Qualcomm HS-USB QDLoader 9008 ({portName})";
                                found.TryAdd(portName, displayName);
                            }
                        }
                    }
                }
                catch
                {
                    // WMI remains the primary source. Registry access is only a
                    // deterministic fallback for a currently present Qualcomm port.
                }

                return found
                    .Select(item => (PortName: item.Key, DisplayName: item.Value))
                    .OrderBy(item => item.PortName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        private (string Output, string Error, int ExitCode, bool TimedOut) RunProcessWithTimeout(ProcessStartInfo startInfo, int timeoutMs)
        {
            StringBuilder outputBuilder = new StringBuilder();
            StringBuilder errorBuilder = new StringBuilder();
            bool traceNative = IsEdlNativeProcess(startInfo.FileName);
            Stopwatch sw = Stopwatch.StartNew();
            if (traceNative)
            {
                AppendEdlNativeLog($"[PROC] {Path.GetFileName(startInfo.FileName)} {startInfo.Arguments}");
                AppendEdlNativeLog($"[PROC] cwd={startInfo.WorkingDirectory}");
                AppendEdlNativeLog($"[PROC] timeout={timeoutMs}ms");
            }
            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        outputBuilder.AppendLine(e.Data);
                        if (traceNative)
                            AppendEdlNativeLog("[OUT] " + e.Data);
                    }
                };
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        errorBuilder.AppendLine(e.Data);
                        if (traceNative)
                            AppendEdlNativeLog("[ERR] " + e.Data);
                    }
                };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                bool exited = process.WaitForExit(timeoutMs);
                sw.Stop();
                if (!exited)
                {
                    process.Kill(true);
                    process.WaitForExit();
                    if (traceNative)
                    {
                        AppendEdlNativeLog($"[ERR] process timeout after {sw.ElapsedMilliseconds}ms");
                        string output = outputBuilder.ToString().Trim();
                        string error = errorBuilder.ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(output))
                            AppendEdlNativeLog("[OUT/SNAPSHOT] " + output.Replace("\r", "").Replace("\n", "\\n"));
                        if (!string.IsNullOrWhiteSpace(error))
                            AppendEdlNativeLog("[ERR/SNAPSHOT] " + error.Replace("\r", "").Replace("\n", "\\n"));
                    }
                    return (outputBuilder.ToString(), errorBuilder.ToString(), -1, true);
                }
                if (traceNative)
                    AppendEdlNativeLog($"[PROC] exit={process.ExitCode}, elapsed={sw.ElapsedMilliseconds}ms");
                return (outputBuilder.ToString(), errorBuilder.ToString(), process.ExitCode, false);
            }
        }

        private void EdlReadGptButton_Click(object sender, RoutedEventArgs e)
        {
            if (_edlEngine == null)
                return;

            int[] rawProgramLuns = GetEdlRawProgramPhysicalLuns(_edlFlashPackProgramFiles);
            _edlFlashPackDir = null;
            _edlFlashPackProgramFiles = null;
            EdlFlashPackTextBox?.Clear();

            string loaderPath = EdlLoaderTextBox.Text;
            if (string.IsNullOrWhiteSpace(loaderPath))
            {
                LogEdlMessage("请先选择引导...", "error");
                return;
            }
            bool sendLoader = EdlSendLoaderCheckBox.IsChecked == true;
            bool manualAuth = false; // 不启用手动认证
            RunEdlAction(async () =>
            {
                bool waitForPortBeforeRead = !_edlEngine.HasOpenFirehoseSession;
                if (waitForPortBeforeRead)
                    await WaitForEdlPortBeforeCloudLoaderDownloadAsync().ConfigureAwait(true);

                LogEdlMessage(
                    $"__EDL_PORT_CONNECT_BEGIN__|{EdlPortComboBox?.Text ?? "COM"}",
                    null);
                UpdateEdlSpeedText($"正在连接{EdlPortComboBox?.Text ?? "串口"}...");
                await Dispatcher.Yield(DispatcherPriority.Render);
                bool skipLoaderResolveForOpenFirehose = !sendLoader && _edlEngine.HasOpenFirehoseSession;
                string resolvedLoaderPath = skipLoaderResolveForOpenFirehose
                    ? loaderPath
                    : await ResolveEdlLoaderPathAsync(
                        loaderPath,
                        sendLoader,
                        waitForPortBeforeDownload: false).ConfigureAwait(true);
                var sw = Stopwatch.StartNew();
                var parts = await Task.Run(() =>
                {
                    _edlEngine.PrepareForOperation(
                        resolvedLoaderPath,
                        sendLoader,
                        manualAuth,
                        _edlResolvedOplusLoaderPackage);
                    Dispatcher.Invoke(() => _edlPartitions.Clear());
                    return _edlEngine.ReadGptPartitionsFromDevice(rawProgramLuns);
                }).ConfigureAwait(true);
                await Task.Delay(50).ConfigureAwait(true);
                Dispatcher.Invoke(() => LoadEdlPartitions(parts, false));
                LogEdlMessage("分区表流程读取结束...", null);
                int parsedLunCount = parts
                    .Select(partition => partition.Lun)
                    .Distinct()
                    .Count();
                LogEdlMessage($"解析成功LUN数量: {parsedLunCount}", null);
                LogEdlMessage($"分区数: {parts.Count}", null);
                LogEdlMessage($"耗时{sw.Elapsed.TotalSeconds:0.##}秒...", null);
            });
        }

        private void EdlReadPartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_edlEngine == null)
                return;
            if (_edlPartitions.Count == 0)
            {
                System.Windows.MessageBox.Show("请先读取分区表。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择分区导出目录",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };
            var r = dlg.ShowDialog();
            if (r != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dlg.SelectedPath))
                return;
            var deselectedDataPartitions = new List<string>();
            foreach (var item in _edlPartitions.Where(p => p.IsDataPartition))
            {
                if (!item.IsSelected)
                    continue;
                item.IsSelected = false;
                deselectedDataPartitions.Add(item.Label);
            }
            if (deselectedDataPartitions.Count > 0)
            {
                EdlPartitionDataGrid.Items.Refresh();
                string names = string.Join("、", deselectedDataPartitions.Distinct(StringComparer.OrdinalIgnoreCase));
                LogEdlMessage($"检测到勾选 {names}，已自动取消勾选！", "warn");
            }
            var selected = _edlPartitions.Where(p => p.IsSelected).Select(p => p.Model).ToList();
            if (selected.Count == 0)
            {
                System.Windows.MessageBox.Show("请勾选要读取的分区。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            bool generateProgram = EdlGenerateProgramCheckBox.IsChecked == true;
            string loaderPath = EdlLoaderTextBox.Text;
            bool sendLoader = EdlSendLoaderCheckBox.IsChecked == true;
            bool manualAuth = false; // 不启用手动认证
            RunEdlAction(async () =>
            {
                bool skipLoaderResolveForOpenFirehose = !sendLoader && _edlEngine.HasOpenFirehoseSession;
                string resolvedLoaderPath = skipLoaderResolveForOpenFirehose
                    ? loaderPath
                    : await ResolveEdlLoaderPathAsync(loaderPath, sendLoader).ConfigureAwait(true);
                await Task.Run(() =>
                {
                    _edlEngine.PrepareForOperation(
                        resolvedLoaderPath,
                        sendLoader,
                        manualAuth,
                        _edlResolvedOplusLoaderPackage);
                    _edlEngine.DoReadPartitions(dlg.SelectedPath, selected, generateProgram);
                }).ConfigureAwait(true);
            });
        }

        private void EdlWritePartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_edlEngine == null)
                return;
            if (_edlPartitions.Count == 0)
            {
                System.Windows.MessageBox.Show("请先读取分区表或加载刷机包。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string programPath = _edlFlashPackDir ?? "";
            if (string.IsNullOrEmpty(programPath))
            {
                string text = EdlFlashPackTextBox.Text?.Trim() ?? "";
                if (!string.IsNullOrEmpty(text))
                {
                    if (text.Contains(';'))
                    {
                        string first = text.Split(';')[0].Trim();
                        programPath = Path.GetDirectoryName(first) ?? "";
                    }
                    else if (File.Exists(text))
                    {
                        programPath = Path.GetDirectoryName(text) ?? "";
                    }
                    else
                    {
                        programPath = text;
                    }
                }
            }
            bool skipSafe = EdlSkipSafeCheckBox.IsChecked == true;
            bool skipData = EdlSkipDataCheckBox.IsChecked == true;
            var selected = _edlPartitions
                .Where(p => p.IsSelected)
                .Where(p => !(skipSafe && p.IsBasebandPartition))
                .Where(p => !(skipData && p.IsDataPartition))
                .Select(p => p.Model)
                .ToList();
            bool isRawProgramFlashTask = HasEdlRawProgramSelection(_edlFlashPackProgramFiles);
            var bypass = _edlPartitions
                .Where(p => !p.IsSelected || (skipSafe && p.IsBasebandPartition) || (skipData && p.IsDataPartition))
                .Select(p => (p.Model.Label, p.Model.Lun))
                .ToList();
            string loaderPath = EdlLoaderTextBox.Text;
            bool sendLoader = EdlSendLoaderCheckBox.IsChecked == true;
            bool manualAuth = false; // 不启用手动认证
            bool factoryReset = EdlFactoryResetCheckBox?.IsChecked == true;
            bool autoReboot = EdlAutoRebootCheckBox?.IsChecked == true; // 检查是否需要自动重启
            RunEdlAction(async () =>
            {
                bool skipLoaderResolveForOpenFirehose = !sendLoader && _edlEngine.HasOpenFirehoseSession;
                string resolvedLoaderPath = skipLoaderResolveForOpenFirehose
                    ? loaderPath
                    : await ResolveEdlLoaderPathAsync(loaderPath, sendLoader).ConfigureAwait(true);
                await Task.Run(() =>
                {
                    _edlEngine.PrepareForOperation(
                        resolvedLoaderPath,
                        sendLoader,
                        manualAuth,
                        _edlResolvedOplusLoaderPackage);
                    if (string.IsNullOrEmpty(programPath))
                    {
                        _edlEngine.FlashWithoutProgram(selected);
                    }
                    else
                    {
                        string[]? programFiles = _edlFlashPackProgramFiles;
                        if (programFiles != null && programFiles.Length > 0)
                        {
                            var selectedKeys = selected.Select(p => (p.Label, p.Lun)).ToList();
                            var selectedFileOverrides = _edlPartitions
                                .Where(p => p.IsSelected)
                                .Where(p => p.IsFilePathUserSelected)
                                .Where(p => !string.IsNullOrWhiteSpace(p.FilePath))
                                .GroupBy(p => $"{p.Label}|{p.Lun}", StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(
                                    group => group.Key,
                                    group => group.Last().FilePath,
                                    StringComparer.OrdinalIgnoreCase);
                            var devicePartitionTargets = selected
                                .GroupBy(
                                    partition => $"{partition.Label}|{partition.Lun}",
                                    StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(
                                    group => group.Key,
                                    group => group.Last(),
                                    StringComparer.OrdinalIgnoreCase);
                            _edlEngine.DoWriteSelectedPartitionsFromProgram(
                                programPath,
                                programFiles,
                                selectedKeys,
                                skipSafe,
                                skipData,
                                selectedFileOverrides,
                                activateBootLun: isRawProgramFlashTask,
                                devicePartitionTargets: devicePartitionTargets,
                                applyPackagePostActions: isRawProgramFlashTask);
                        }
                        else
                        {
                            _edlEngine.DoWritePartitions(programPath, bypass, skipSafe, skipData);
                        }
                    }
                }).ConfigureAwait(true);

                _edlEngine.ThrowIfStopRequested();
                if (factoryReset)
                {
                    await Task.Delay(300).ConfigureAwait(true);
                    _edlEngine.ThrowIfStopRequested();
                    await Task.Run(() =>
                    {
                        if (_edlEngine?.FirehoseServer == null)
                            throw new Exception("未连接到 Firehose，无法执行自动格式化");

                        var dataParts = _edlPartitions
                            .Where(p => p.IsDataPartition)
                            .Select(p => p.Model)
                            .ToList();
                        if (dataParts.Count == 0)
                            throw new Exception("未找到 userdata/metadata 分区，无法执行自动格式化");

                        LogEdlMessage("格式化设备...", null);
                        _edlEngine.DoEraseOperation(dataParts, true);

                        var misc = _edlPartitions.FirstOrDefault(p => p.Label.Equals("misc", StringComparison.OrdinalIgnoreCase));
                        if (misc == null)
                            throw new Exception("未找到 misc 分区，无法执行自动格式化");

                        string miscPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp", "misc.img");
                        if (!File.Exists(miscPath))
                            throw new Exception("未找到自动格式化所需的 misc.img: " + miscPath);

                        misc.SetFilePath(miscPath);
                        _edlEngine.FlashWithoutProgram(new List<EdlPartitionInfo> { misc.Model }, "写入补丁...", true);
                    }).ConfigureAwait(true);
                }

                _edlEngine.ThrowIfStopRequested();
                if (autoReboot && _edlEngine?.FirehoseServer != null)
                {
                    await Task.Delay(500).ConfigureAwait(true); // 稍微延迟确保刷写完成
                    _edlEngine.ThrowIfStopRequested();
                    await Task.Run(() =>
                    {
                        LogEdlMessage("__EDL_FLASH_BEGIN__|重启设备...", null);
                        try
                        {
                            _edlEngine.FirehoseServer.ResetDevice().CheckAndThrow();
                            LogEdlMessage("__EDL_FLASH_OK__", null);
                        }
                        catch
                        {
                            LogEdlMessage("__EDL_FLASH_ERROR__", null);
                            throw;
                        }
                        finally
                        {
                            _edlEngine.ReleaseEdlPort();
                        }
                    }).ConfigureAwait(true);
                }
            });
        }

        private void EdlErasePartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_edlEngine == null)
                return;
            if (_edlPartitions.Count == 0)
            {
                System.Windows.MessageBox.Show("请先读取分区表。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var selected = _edlPartitions.Where(p => p.IsSelected).Select(p => p.Model).ToList();
            if (selected.Count == 0)
            {
                System.Windows.MessageBox.Show("请勾选要擦除的分区。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (System.Windows.MessageBox.Show("确定要擦除选中的分区吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            string loaderPath = EdlLoaderTextBox.Text;
            bool sendLoader = EdlSendLoaderCheckBox.IsChecked == true;
            bool manualAuth = false; // 不启用手动认证
            RunEdlAction(async () =>
            {
                bool skipLoaderResolveForOpenFirehose = !sendLoader && _edlEngine.HasOpenFirehoseSession;
                string resolvedLoaderPath = skipLoaderResolveForOpenFirehose
                    ? loaderPath
                    : await ResolveEdlLoaderPathAsync(loaderPath, sendLoader).ConfigureAwait(true);
                await Task.Run(() =>
                {
                    _edlEngine.PrepareForOperation(
                        resolvedLoaderPath,
                        sendLoader,
                        manualAuth,
                        _edlResolvedOplusLoaderPackage);
                    _edlEngine.DoErasePartitionHeaders(selected);
                }).ConfigureAwait(true);
            });
        }

        private void EdlPartitionDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is DataGrid dg && dg.SelectedItem is EdlPartitionViewItem item)
            {
                e.Handled = true;
                dg.CommitEdit(DataGridEditingUnit.Cell, true);
                dg.CommitEdit(DataGridEditingUnit.Row, true);

                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "镜像文件 (*.img;*.bin;*.elf)|*.img;*.bin;*.elf|所有文件 (*.*)|*.*"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    item.SetFilePath(openFileDialog.FileName, isUserSelected: true);
                    // 自动勾选此行的复选框
                    bool protectSafe = EdlSkipSafeCheckBox?.IsChecked == true;
                    bool protectData = EdlSkipDataCheckBox?.IsChecked == true;
                    if (!(protectSafe && item.IsBasebandPartition) && !(protectData && item.IsDataPartition))
                        item.IsSelected = true;
                }
            }
        }
    }

    internal class EdlPartitionViewItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _dynamicSuperAvailable;
        private bool _isFilePathUserSelected;

        public EdlPartitionViewItem(EdlPartitionInfo model, bool selected)
        {
            Model = model;
            IsSelected = selected;
        }

        public EdlPartitionInfo Model { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public string Label => Model.Label;

        public int Lun => Model.Lun;

        public string StartSector => Model.StartSector;

        public string SizeDisplay => GetFileSize(Model.SectorLen * Model.BytesPerSector);

        public string FilePath => Model.FilePath ?? "";

        public string FileName => string.IsNullOrEmpty(Model.FilePath)
            ? (_dynamicSuperAvailable ? "Meta免合并" : "")
            : Path.GetFileName(Model.FilePath);

        public bool IsFilePathUserSelected => _isFilePathUserSelected;

        public bool IsBasebandPartition => MainWindow.IsEdlBasebandPartitionLabel(Model.Label);

        public bool IsDataPartition => MainWindow.IsEdlDataPartitionLabel(Model.Label);

        public void SetFilePath(string? filePath, bool isUserSelected = false)
        {
            bool userSelected = isUserSelected && !string.IsNullOrWhiteSpace(filePath);
            if (string.Equals(Model.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                _isFilePathUserSelected = userSelected;
                return;
            }
            Model.FilePath = filePath ?? "";
            _isFilePathUserSelected = userSelected;
            if (!string.IsNullOrWhiteSpace(filePath))
                _dynamicSuperAvailable = false;
            OnPropertyChanged(nameof(FilePath));
            OnPropertyChanged(nameof(FileName));
        }

        public void SetDynamicSuperAvailable(bool available)
        {
            if (_dynamicSuperAvailable == available)
                return;
            _dynamicSuperAvailable = available;
            OnPropertyChanged(nameof(FileName));
        }

        private static string GetFileSize(long sizeInBytes)
        {
            foreach (var unit in new[] { "B", "KB", "MB", "GB" })
            {
                if (sizeInBytes < 1024)
                    return sizeInBytes + unit;
                sizeInBytes /= 1024;
            }
            return sizeInBytes + "GB";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    internal sealed class EdlBuiltInLoaderOption
    {
        public EdlBuiltInLoaderOption(
            string displayName,
            string filePath,
            string? downloadUrl = null,
            bool requiresOplusPackage = false,
            int? sortOrder = null,
            string? loaderType = null,
            string? firehoseProfile = null,
            string? authType = null)
        {
            DisplayName = displayName;
            FilePath = filePath;
            DownloadUrl = downloadUrl;
            RequiresOplusPackage = requiresOplusPackage;
            SortOrder = sortOrder ?? int.MaxValue;
            LoaderType = loaderType;
            FirehoseProfile = firehoseProfile;
            AuthType = authType;
        }

        public static EdlBuiltInLoaderOption CreateRemote(
            string displayName,
            string filePath,
            string downloadUrl,
            bool requiresOplusPackage = false,
            int? sortOrder = null,
            string? loaderType = null,
            string? firehoseProfile = null,
            string? authType = null)
            => new(
                displayName,
                filePath,
                downloadUrl,
                requiresOplusPackage,
                sortOrder,
                loaderType,
                firehoseProfile,
                authType);

        public string DisplayName { get; }

        public string FilePath { get; }

        public string? DownloadUrl { get; }

        public bool RequiresOplusPackage { get; }

        public int SortOrder { get; }

        public string? LoaderType { get; }

        public string? FirehoseProfile { get; }

        public string? AuthType { get; }
    }

    internal sealed class EdlCloudLoaderEntry
    {
        public EdlCloudLoaderEntry(
            string displayName,
            string folderName,
            string? zipFileName,
            string? downloadUrl,
            string? brandName = null,
            string? packageType = null,
            int? sortOrder = null,
            string? loaderType = null,
            string? firehoseProfile = null,
            string? authType = null,
            bool enabled = true)
        {
            DisplayName = displayName;
            FolderName = folderName;
            ZipFileName = zipFileName;
            DownloadUrl = downloadUrl;
            BrandName = brandName;
            PackageType = packageType;
            SortOrder = sortOrder;
            LoaderType = loaderType;
            FirehoseProfile = firehoseProfile;
            AuthType = authType;
            Enabled = enabled;
        }

        public string DisplayName { get; }

        public string FolderName { get; }

        public string? ZipFileName { get; }

        public string? DownloadUrl { get; }

        public string? BrandName { get; }

        public string? PackageType { get; }

        public int? SortOrder { get; }

        public string? LoaderType { get; }

        public string? FirehoseProfile { get; }

        public string? AuthType { get; }

        public bool Enabled { get; }
    }

    internal enum EdlCloudCacheState
    {
        Current,
        Stale,
        Unknown
    }

    internal sealed class EdlCloudLoaderCacheMetadata
    {
        public string? DownloadUrl { get; set; }

        public string? ETag { get; set; }

        public string? LastModified { get; set; }

        public long? ContentLength { get; set; }
    }

    internal sealed class EdlOplusLoaderPackage
    {
        public EdlOplusLoaderPackage(
            string firehosePath,
            string digestPath,
            string signPath,
            string workingDirectory,
            string displayName,
            string? firehoseProfile = null,
            string? authType = null)
        {
            FirehosePath = firehosePath;
            DigestPath = digestPath;
            SignPath = signPath;
            WorkingDirectory = workingDirectory;
            DisplayName = displayName;
            FirehoseProfile = firehoseProfile;
            AuthType = authType;
        }

        public string FirehosePath { get; }

        public string DigestPath { get; }

        public string SignPath { get; }

        public string WorkingDirectory { get; }

        public string DisplayName { get; }

        public string? FirehoseProfile { get; }

        public string? AuthType { get; }
    }

    internal partial class EdlEngine
    {
        private readonly Action<string, string?> _log;
        private readonly Action<string?> _appendNativeLog;
        private readonly Action<bool> _setSendLoaderChecked;
        private readonly Action<double> _updateProgress;
        private readonly Action<string?> _updateSpeedText;
        private readonly Action<string?> _updatePortLabel;
        private readonly Action<string?> _updateCurrentPartitionText;
        private readonly object _speedSmoothingSync = new();
        private double _smoothedSpeedBytesPerSecond;
        private bool _speedSmoothingReady;
        private long _lastSpeedSampleTick;

        private string? _currentPortName;
        private SerialPort? _currentPort;
        private SaharaServer? _saharaServer;
        internal FirehoseServer? FirehoseServer;
        public bool HasOpenFirehoseSession => IsCurrentFirehoseSessionReady;
        private int _firehoseSectorSize = 4096;
        private int _firehoseMaxPayloadSize = 16 * 1024 * 1024;
        private const int GptReadSectors = 256;
        private const int MaxUfsPhysicalLunProbeCount = 8;
        private const int VersionInfoReadDataTimeoutMs = 12000;
        private const int VersionInfoReadFinalAckTimeoutMs = 1500;
        private const int VersionInfoTotalTimeoutMs = 90000;
        private int[] _knownPhysicalLuns = Array.Empty<int>();
        private bool _useOplusReadWithoutSpoof;
        private bool _fastVipProgramWrite;
        private string _requestedFirehoseProfile = "generic_direct";
        private string _requestedAuthType = "none";
        private bool _deferLoaderSendToFirehoseProbe;
        private volatile bool _portDetectStop;
        private int _stopRequested;
        private string? _lastSuccessfulReadStrategyFilename;
        private string? _lastSuccessfulReadStrategyLabel;

        public EdlEngine(Action<string, string?> log, Action<double> updateProgress, Action<string?> updateSpeedText, Action<string?> updatePortLabel, Action<string?> updateCurrentPartitionText, Action<string?> appendNativeLog, Action<bool> setSendLoaderChecked)
        {
            _log = log;
            _updateProgress = updateProgress;
            _updateSpeedText = speedText =>
            {
                if (string.IsNullOrWhiteSpace(speedText)
                    || !IsFormattedTransferSpeed(speedText))
                    ResetSpeedSmoothing();
                updateSpeedText(speedText);
            };
            _updatePortLabel = updatePortLabel;
            _updateCurrentPartitionText = updateCurrentPartitionText;
            _appendNativeLog = appendNativeLog;
            _setSendLoaderChecked = setSendLoaderChecked;
            var t = new Thread(PortDetectThread) { IsBackground = true };
            t.Start();
        }

        public bool IsStopRequested => Volatile.Read(ref _stopRequested) != 0;

        public void SetLoaderProfile(string? firehoseProfile, string? authType)
        {
            _requestedFirehoseProfile = NormalizeFirehoseProfile(firehoseProfile);
            _requestedAuthType = string.IsNullOrWhiteSpace(authType)
                ? "none"
                : authType.Trim().ToLowerInvariant();
        }

        private static string NormalizeFirehoseProfile(string? firehoseProfile)
        {
            string profile = string.IsNullOrWhiteSpace(firehoseProfile)
                ? "generic_direct"
                : firehoseProfile.Trim().ToLowerInvariant();
            return profile switch
            {
                "generic" => "generic_direct",
                "oplus_standard" => "oplus_vip_adaptive",
                "oplus_8g1_8g2" => "oplus_special_rw",
                "zte" => "zte_firehose",
                _ => profile
            };
        }

        public void BeginOperation()
        {
            Interlocked.Exchange(ref _stopRequested, 0);
        }

        public bool RequestStop()
        {
            return Interlocked.Exchange(ref _stopRequested, 1) == 0;
        }

        public void ThrowIfStopRequested()
        {
            if (IsStopRequested)
                throw new OperationCanceledException("用户已停止 EDL 操作");
        }

        public bool TryRebootToEdlViaFirehose(int timeoutMs, out bool sent)
        {
            sent = false;
            if (FirehoseServer == null || _currentPort == null || !_currentPort.IsOpen)
                return false;
            try
            {
                string[] xmls =
                {
                    "<?xml version=\"1.0\"?><data><power value=\"reset_to_edl\"/></data>",
                    "<?xml version=\"1.0\"?><data><power value=\"edl\"/></data>"
                };
                foreach (string xml in xmls)
                {
                    PurgePortBuffer();
                    byte[] bytes = Encoding.UTF8.GetBytes(xml);
                    TraceEdlTx(xml);
                    _currentPort.Write(bytes, 0, bytes.Length);
                    sent = true;
                    if (WaitForAck(timeoutMs))
                        return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public void ReleaseEdlPort()
        {
            DisposeCurrentSerialPort();
        }

        public void BeginWaitingForEdlPort()
        {
            DisposeCurrentSerialPort();
            Interlocked.Exchange(ref _currentPortName, null);
        }

        public void AdoptDetectedEdlPort(string portName)
        {
            string normalizedPort = NormalizeSerialPortName(portName);
            if (string.IsNullOrWhiteSpace(normalizedPort))
                return;

            string? previousPort = Interlocked.Exchange(ref _currentPortName, normalizedPort);
            _updatePortLabel(normalizedPort);
            if (!string.Equals(previousPort, normalizedPort, StringComparison.OrdinalIgnoreCase))
            {
                _log($"{normalizedPort}已连接", null);
                _setSendLoaderChecked(true);
            }
        }

        private void DisposeCurrentSerialPort()
        {
            _saharaServer = null;
            FirehoseServer = null;
            _knownPhysicalLuns = Array.Empty<int>();
            SerialPort? port = Interlocked.Exchange(ref _currentPort, null);
            if (port != null)
            {
                try
                {
                    if (port.IsOpen)
                        port.Close();
                }
                catch
                {
                }
                try
                {
                    port.Dispose();
                }
                catch
                {
                }
            }
            MarkFirehoseSessionDisconnected();
        }

        private static string FormatSpeed(double bytesPerSecond)
        {
            if (!(bytesPerSecond > 0))
                return "0.00 B/s";
            string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
            double v = bytesPerSecond;
            int i = 0;
            while (v >= 1024 && i < units.Length - 1)
            {
                v /= 1024;
                i++;
            }
            return $"{v:0.00} {units[i]}";
        }

        private static bool IsFormattedTransferSpeed(string value)
        {
            return value.EndsWith(" B/s", StringComparison.Ordinal)
                || value.EndsWith(" KB/s", StringComparison.Ordinal)
                || value.EndsWith(" MB/s", StringComparison.Ordinal)
                || value.EndsWith(" GB/s", StringComparison.Ordinal);
        }

        private void UpdateSmoothedTransferSpeed(double bytesPerSecond)
        {
            if (double.IsNaN(bytesPerSecond)
                || double.IsInfinity(bytesPerSecond)
                || bytesPerSecond < 0)
            {
                return;
            }

            double displaySpeed;
            long now = Stopwatch.GetTimestamp();
            lock (_speedSmoothingSync)
            {
                double elapsedSeconds = _lastSpeedSampleTick <= 0
                    ? 0
                    : (now - _lastSpeedSampleTick) / (double)Stopwatch.Frequency;
                if (!_speedSmoothingReady || elapsedSeconds <= 0 || elapsedSeconds >= 2.0)
                {
                    _smoothedSpeedBytesPerSecond = bytesPerSecond;
                    _speedSmoothingReady = true;
                }
                else
                {
                    const double timeConstantSeconds = 0.75;
                    double alpha = 1.0 - Math.Exp(-elapsedSeconds / timeConstantSeconds);
                    alpha = Math.Clamp(alpha, 0.08, 0.45);
                    _smoothedSpeedBytesPerSecond +=
                        alpha * (bytesPerSecond - _smoothedSpeedBytesPerSecond);
                }

                _lastSpeedSampleTick = now;
                displaySpeed = _smoothedSpeedBytesPerSecond;
            }

            _updateSpeedText(FormatSpeed(displaySpeed));
        }

        private void ResetSpeedSmoothing()
        {
            lock (_speedSmoothingSync)
            {
                _smoothedSpeedBytesPerSecond = 0;
                _speedSmoothingReady = false;
                _lastSpeedSampleTick = 0;
            }
        }

        private static string FormatByteSize(long bytes)
        {
            if (bytes < 0)
                bytes = 0;
            string[] units = { "B", "KB", "MB", "GB" };
            double value = bytes;
            int unitIndex = 0;
            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024;
                unitIndex++;
            }
            return $"{value:0.##} {units[unitIndex]}";
        }

        private static void ThrowIfNativePortOpenFailed(string output, string? portName)
        {
            if (MainWindow.LooksLikeNativePortOpenFailed(output))
                throw MainWindow.CreateEdlPortOpenFailedException(portName);
        }

        private static void ThrowLoaderSendTimeout()
        {
            throw MainWindow.CreateEdlLoaderSendTimeoutException(null);
        }

        private Stopwatch StartLoaderSendProgress(
            string programmerPath,
            long totalBytes,
            int timeoutMs,
            out System.Threading.Timer progressTimer,
            string? statusText = null)
        {
            totalBytes = Math.Max(1, totalBytes);
            string fileName = Path.GetFileName(programmerPath);
            double timeoutSeconds = Math.Max(0.5, timeoutMs / 1000.0);
            double estimatedSeconds = Math.Clamp(totalBytes / (6.0 * 1024 * 1024), 0.25, timeoutSeconds * 0.85);
            Stopwatch stopwatch = Stopwatch.StartNew();
            _updateCurrentPartitionText(
                string.IsNullOrWhiteSpace(statusText)
                    ? (string.IsNullOrWhiteSpace(fileName) ? "发送引导" : $"发送引导 {fileName}")
                    : statusText);
            _updateProgress(0);
            _updateSpeedText(null);

            progressTimer = new System.Threading.Timer(_ =>
            {
                double elapsedSeconds = Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
                double percent = Math.Clamp(elapsedSeconds / estimatedSeconds * 95.0, 0, 95);
                _updateProgress(percent);
            }, null, 120, 120);

            return stopwatch;
        }

        private void FinishLoaderSendProgress(System.Threading.Timer progressTimer, Stopwatch stopwatch, bool completed)
        {
            progressTimer.Dispose();
            stopwatch.Stop();
            if (!completed)
            {
                _updateProgress(0);
                _updateSpeedText(null);
                _updateCurrentPartitionText(null);
                return;
            }

            _updateProgress(100);
            Thread.Sleep(250);
            _updateProgress(0);
            _updateSpeedText(null);
            _updateCurrentPartitionText(null);
        }

        private static bool TryGetSparseExpandedBytes(string filePath, out long expandedBytes, out uint blockSize)
        {
            expandedBytes = 0;
            blockSize = 0;
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var br = new BinaryReader(fs);
                if (fs.Length < 28)
                    return false;
                uint magic = br.ReadUInt32();
                if (magic != 0xED26FF3A)
                    return false;
                ushort major = br.ReadUInt16();
                br.ReadUInt16();
                br.ReadUInt16();
                br.ReadUInt16();
                uint blkSz = br.ReadUInt32();
                uint totalBlks = br.ReadUInt32();
                if (major != 1 || blkSz == 0 || totalBlks == 0)
                    return false;
                blockSize = blkSz;
                expandedBytes = checked((long)blkSz * totalBlks);
                return expandedBytes > 0;
            }
            catch
            {
                return false;
            }
        }

        private long GuessBytesPerProgressUnit(string filePath, bool looksSparse, long totalUnits)
        {
            if (totalUnits <= 0)
                return 1;

            if (looksSparse && TryGetSparseExpandedBytes(filePath, out long expandedBytes, out uint sparseBlockSize))
            {
                double ratio = expandedBytes / (double)totalUnits;
                if (ratio >= 1 && ratio <= 1024 * 1024)
                {
                    double diff = Math.Abs(ratio - sparseBlockSize);
                    if (diff <= sparseBlockSize * 0.2)
                        return (long)sparseBlockSize;
                    return (long)Math.Round(ratio);
                }
            }

            try
            {
                long fileLen = new FileInfo(filePath).Length;
                double ratio = fileLen / (double)totalUnits;
                if (ratio >= 256 && ratio <= 8192)
                {
                    if (Math.Abs(ratio - 512) <= 64) return 512;
                    if (Math.Abs(ratio - 4096) <= 512) return 4096;
                    if (_firehoseSectorSize > 0) return _firehoseSectorSize;
                    return (long)Math.Round(ratio);
                }
            }
            catch
            {
            }

            return 1;
        }

        private string? WaitForPort(string portName = "")
        {
            string normalizedRequestedPort = NormalizeSerialPortName(portName);
            return MainWindow.EnumeratePresentQcedlPorts()
                .Select(item => item.PortName)
                .FirstOrDefault(candidate =>
                    string.IsNullOrWhiteSpace(normalizedRequestedPort)
                    || string.Equals(candidate, normalizedRequestedPort, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeSerialPortName(string portName)
        {
            Match match = Regex.Match(portName ?? "", @"COM\s*(\d+)", RegexOptions.IgnoreCase);
            return match.Success
                ? "COM" + match.Groups[1].Value
                : (portName ?? "").Replace(@"\\.\", "", StringComparison.OrdinalIgnoreCase).Trim();
        }

        private static bool IsSerialPortPresent(string portName)
        {
            string expectedPort = NormalizeSerialPortName(portName);
            if (string.IsNullOrWhiteSpace(expectedPort))
                return false;

            try
            {
                return SerialPort.GetPortNames().Any(
                    candidate => string.Equals(candidate, expectedPort, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        public bool IsEdlPortReadyForExternalAccess(string portName)
        {
            string normalizedPort = NormalizeSerialPortName(portName);
            if (string.IsNullOrWhiteSpace(normalizedPort))
                return false;

            try
            {
                using var probePort = new SerialPort(normalizedPort);
                probePort.Open();
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public bool IsCurrentEdlPortOpen(string portName)
        {
            string normalizedPort = NormalizeSerialPortName(portName);
            SerialPort? currentPort = _currentPort;
            return currentPort?.IsOpen == true
                && string.Equals(
                    NormalizeSerialPortName(currentPort.PortName),
                    normalizedPort,
                    StringComparison.OrdinalIgnoreCase);
        }

        private void PortDetectThread()
        {
            while (true)
            {
                if (_portDetectStop)
                    return;

                string? currentPortName = _currentPortName;
                if (!string.IsNullOrWhiteSpace(currentPortName))
                {
                    if (IsSerialPortPresent(currentPortName))
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    _updatePortLabel(null);
                    _log($"{currentPortName} 已断开", null);
                    _setSendLoaderChecked(true);
                    ReleaseEdlPort();
                    if (string.Equals(_currentPortName, currentPortName, StringComparison.OrdinalIgnoreCase))
                        _currentPortName = null;
                    Thread.Sleep(100);
                    continue;
                }

                string? newPortName = WaitForPort();
                if (!string.IsNullOrEmpty(newPortName))
                {
                    AdoptDetectedEdlPort(newPortName);
                }
                Thread.Sleep(300);
            }
        }

        private void PurgePortBuffer()
        {
            if (_currentPort == null)
                return;
            try
            {
                _currentPort.DiscardInBuffer();
                _currentPort.DiscardOutBuffer();
            }
            catch
            {
            }
        }

        private void TraceEdlTx(string xml)
        {
            if (!string.IsNullOrWhiteSpace(xml))
                _appendNativeLog("[TX] " + xml.Replace("\r", "").Replace("\n", ""));
        }

        private void TraceEdlRx(string xml)
        {
            if (!string.IsNullOrWhiteSpace(xml))
                _appendNativeLog("[RX] " + xml.Replace("\r", "").Replace("\n", ""));
        }

        private void LogPartitionWriteFailure(EdlPartitionInfo part, Exception ex)
        {
            bool breaksSession = IsSessionBreakingFailure(ex);
            string shortMessage = breaksSession
                ? BuildSessionFailureReason(ex)
                : BuildShortEdlErrorMessage(ex);
            _log("__EDL_FLASH_ERROR__", null);
            _log($"{part.Label} (LUN{part.Lun}) 写入失败: {shortMessage}", "error");
            _appendNativeLog($"[Error] {part.Label} (LUN{part.Lun}) 写入失败: {ex}");
            if (breaksSession)
            {
                MarkFirehoseSessionFaulted(shortMessage);
                throw new EdlReadFailureException(
                    $"{part.Label} 写入期间通信会话失效，已停止后续分区：{shortMessage}",
                    true,
                    ex);
            }
        }

        private void LogMissingPartitionImage(EdlPartitionInfo part)
        {
            _log($"{part.Label}分区写入失败，未选择写入文件.", "error");
            _appendNativeLog($"[Error] {part.Label} (LUN{part.Lun}) 写入失败，未选择写入文件。");
        }

        private static string BuildShortEdlErrorMessage(Exception ex)
        {
            string message = ex.Message ?? "";
            if (message.Contains("not get permission", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("not allowed", StringComparison.OrdinalIgnoreCase))
                return "设备拒绝写入，已跳过";
            if (message.Contains("label", StringComparison.OrdinalIgnoreCase) &&
                message.Contains("not exist", StringComparison.OrdinalIgnoreCase))
                return "设备未接受该分区标签，已跳过";
            if (message.Contains("NAK", StringComparison.OrdinalIgnoreCase))
                return "设备返回 NAK，已跳过";

            string firstLine = message
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Split('\n')
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
                ?.Trim() ?? "未知错误，已跳过";
            return firstLine.Length > 120 ? firstLine[..120] + "..." : firstLine;
        }

        private void TraceSharpEdlProgramIntent(EdlPartitionInfo part, string imagePath, bool sparse)
        {
            int sectorSize = _firehoseSectorSize > 0 ? _firehoseSectorSize : 4096;
            long fileBytes = 0;
            try
            {
                if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
                    fileBytes = new FileInfo(imagePath).Length;
            }
            catch
            {
            }

            long sectors = fileBytes <= 0 ? 0 : (fileBytes + sectorSize - 1) / sectorSize;
            string fileName = System.Security.SecurityElement.Escape(Path.GetFileName(imagePath)) ?? "";
            string label = System.Security.SecurityElement.Escape(part.Label) ?? "";
            string xml = string.Format(
                CultureInfo.InvariantCulture,
                "<?xml version=\"1.0\" ?><data><program SECTOR_SIZE_IN_BYTES=\"{0}\" filename=\"{1}\" label=\"{2}\" num_partition_sectors=\"{3}\" physical_partition_number=\"{4}\" sparse=\"{5}\" start_sector=\"{6}\" /></data>",
                sectorSize,
                fileName,
                label,
                sectors,
                part.Lun,
                sparse ? "true" : "false",
                part.StartSector);
            _appendNativeLog("[TX/SharpEDL] " + xml);
        }

        private XElement? ReadFirehoseResponse(int timeoutMs)
        {
            return ReadFirehoseResponse(_currentPort, timeoutMs);
        }

        private XElement? ReadFirehoseResponse(SerialPort? port, int timeoutMs)
        {
            return ReadFirehoseResponse(port, timeoutMs, out _);
        }

        private XElement? ReadFirehoseResponse(
            SerialPort? port,
            int timeoutMs,
            out string responseContext)
        {
            responseContext = string.Empty;
            if (port == null || !port.IsOpen)
                return null;
            StringBuilder sb = new StringBuilder();
            DateTime start = DateTime.Now;
            while ((DateTime.Now - start).TotalMilliseconds < timeoutMs)
            {
                int available = port.BytesToRead;
                if (available > 0)
                {
                    byte[] buffer = new byte[Math.Min(available, 65536)];
                    int read = port.Read(buffer, 0, buffer.Length);
                    if (read > 0)
                    {
                        string chunk = Encoding.UTF8.GetString(buffer, 0, read);
                        sb.Append(chunk);
                        string content = sb.ToString();
                        responseContext = content;
                        if (content.Contains("<log "))
                        {
                            var logMatches = Regex.Matches(content, @"<log value=""([^""]*)""\s*/>");
                            foreach (Match m in logMatches)
                            {
                                if (m.Groups.Count > 1)
                                    _appendNativeLog("[Device] " + m.Groups[1].Value);
                            }
                        }
                        int startIndex = content.IndexOf("<response", StringComparison.OrdinalIgnoreCase);
                        if (startIndex >= 0)
                        {
                            int endIndex = content.IndexOf("/>", startIndex, StringComparison.OrdinalIgnoreCase);
                            if (endIndex > startIndex)
                            {
                                string respXml = content.Substring(startIndex, endIndex - startIndex + 2);
                                TraceEdlRx(respXml);
                                return XElement.Parse(respXml);
                            }
                        }
                    }
                }
                else
                {
                    Thread.Sleep(2);
                }
            }
            return null;
        }

        private bool WaitForAck(int timeoutMs)
        {
            return WaitForAck(_currentPort, timeoutMs);
        }

        private bool WaitForAck(SerialPort? port, int timeoutMs)
        {
            XElement? resp = ReadFirehoseResponse(port, timeoutMs);
            if (resp == null)
                return false;
            string value = resp.Attribute("value")?.Value ?? "";
            return value.Equals("ACK", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private enum ProgramCommandStartStatus
        {
            Accepted,
            PermissionDenied,
            Rejected,
            Timeout,
            SessionLost
        }

        private readonly struct ProgramCommandStartResult
        {
            public ProgramCommandStartStatus Status { get; }
            public string Reason { get; }

            public ProgramCommandStartResult(ProgramCommandStartStatus status, string reason)
            {
                Status = status;
                Reason = reason;
            }
        }

        private static string GetProgramResponseReason(XElement? response, string responseContext)
        {
            MatchCollection logMatches = Regex.Matches(
                responseContext ?? string.Empty,
                @"<log value=""([^""]*)""\s*/>",
                RegexOptions.IgnoreCase);
            for (int i = logMatches.Count - 1; i >= 0; i--)
            {
                string value = System.Net.WebUtility.HtmlDecode(logMatches[i].Groups[1].Value).Trim();
                if (value.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("not allowed", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("not get permission", StringComparison.OrdinalIgnoreCase))
                {
                    return value.Length > 240 ? value[..240] + "..." : value;
                }
            }

            string responseValue = response?.Attribute("value")?.Value ?? "未返回响应";
            return $"Firehose response={responseValue}";
        }

        private ProgramCommandStartResult WaitForProgramRawMode(int timeoutMs)
        {
            SerialPort? port = _currentPort;
            if (port == null || !port.IsOpen)
            {
                return new ProgramCommandStartResult(
                    ProgramCommandStartStatus.SessionLost,
                    "串口未打开或会话已关闭");
            }

            XElement? response = ReadFirehoseResponse(port, timeoutMs, out string responseContext);
            if (response == null)
            {
                bool sessionLost = _currentPort == null
                    || !ReferenceEquals(_currentPort, port)
                    || !port.IsOpen;
                return new ProgramCommandStartResult(
                    sessionLost ? ProgramCommandStartStatus.SessionLost : ProgramCommandStartStatus.Timeout,
                    sessionLost ? "串口已关闭或会话已被替换" : "program 命令等待 rawmode ACK 超时");
            }

            string value = response.Attribute("value")?.Value ?? string.Empty;
            string rawMode = response.Attribute("rawmode")?.Value ?? string.Empty;
            bool accepted = (value.Equals("ACK", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("true", StringComparison.OrdinalIgnoreCase))
                && rawMode.Equals("true", StringComparison.OrdinalIgnoreCase);
            if (accepted)
            {
                return new ProgramCommandStartResult(
                    ProgramCommandStartStatus.Accepted,
                    string.Empty);
            }

            string reason = GetProgramResponseReason(response, responseContext);
            if (value.Equals("NAK", StringComparison.OrdinalIgnoreCase)
                && IsExactOplusWritePermissionDenied(responseContext))
            {
                return new ProgramCommandStartResult(
                    ProgramCommandStartStatus.PermissionDenied,
                    reason);
            }

            return new ProgramCommandStartResult(
                ProgramCommandStartStatus.Rejected,
                reason);
        }

        private bool WriteSectorsWithVip(
            int lun,
            long startSector,
            byte[] data,
            int length,
            bool useVipMode,
            string? partitionName,
            bool readBackVerify = true)
        {
            VipSpoofStrategy strategy = useVipMode
                ? GetDeterministicVipWriteStrategy(lun, startSector)
                : GetDirectWriteStrategy(lun, partitionName, null);
            bool written = WriteSectorsOnce(
                lun,
                startSector,
                data,
                length,
                strategy,
                readBackVerify,
                out string? permissionDeniedReason);
            if (!written && !string.IsNullOrWhiteSpace(permissionDeniedReason))
                throw new Exception(permissionDeniedReason);
            return written;
        }

        private static VipSpoofStrategy GetDeterministicVipWriteStrategy(int lun, long startSector)
        {
            return startSector <= 33
                ? new VipSpoofStrategy($"gpt_backup{lun}.bin", "BackupGPT", 0)
                : new VipSpoofStrategy("gpt_backup0.bin", "BackupGPT", 0);
        }

        private VipSpoofStrategy GetDirectWriteStrategy(
            int lun,
            string? partitionName,
            string? sourceImagePath)
        {
            string label = (partitionName ?? string.Empty).Trim();
            if (label.Equals("gpt", StringComparison.OrdinalIgnoreCase)
                || label.Equals("PrimaryGPT", StringComparison.OrdinalIgnoreCase))
            {
                return new VipSpoofStrategy($"gpt_main{lun}.bin", "PrimaryGPT", 0);
            }
            if (label.Equals("BackupGPT", StringComparison.OrdinalIgnoreCase))
                return new VipSpoofStrategy($"gpt_backup{lun}.bin", "BackupGPT", 0);

            if (string.IsNullOrWhiteSpace(label))
                label = "rawdata";

            string filename = string.IsNullOrWhiteSpace(sourceImagePath)
                ? string.Empty
                : Path.GetFileName(sourceImagePath);
            if (string.IsNullOrWhiteSpace(filename))
                filename = SanitizePartitionName(label) + ".bin";

            return new VipSpoofStrategy(filename, label, 0);
        }

        private bool WriteSectorsOnce(
            int lun,
            long startSector,
            byte[] data,
            int length,
            VipSpoofStrategy strategy,
            bool readBackVerify,
            out string? permissionDeniedReason)
        {
            permissionDeniedReason = null;
            SerialPort? port = _currentPort;
            if (port == null || !port.IsOpen)
                throw new EdlReadFailureException("串口未打开或会话已关闭", true);

            int sectorSize = _firehoseSectorSize > 0 ? _firehoseSectorSize : 4096;
            if (length <= 0 || (length % sectorSize) != 0)
                throw new Exception($"写入数据长度未对齐扇区大小: {length} (sectorSize={sectorSize})");
            int numSectors = length / sectorSize;
            string verifyAttribute = readBackVerify ? " read_back_verify=\"true\"" : "";
            string xml;
            if (string.IsNullOrEmpty(strategy.Label))
            {
                xml = string.Format(
                    CultureInfo.InvariantCulture,
                    "<?xml version=\"1.0\" ?><data>" +
                    "<program SECTOR_SIZE_IN_BYTES=\"{0}\" num_partition_sectors=\"{1}\" " +
                    "physical_partition_number=\"{2}\" start_sector=\"{3}\"{4} />" +
                    "</data>",
                    sectorSize, numSectors, lun, startSector, verifyAttribute);
            }
            else
            {
                string escapedFilename = System.Security.SecurityElement.Escape(strategy.Filename)
                    ?? string.Empty;
                string escapedLabel = System.Security.SecurityElement.Escape(strategy.Label)
                    ?? string.Empty;
                xml = string.Format(
                    CultureInfo.InvariantCulture,
                    "<?xml version=\"1.0\" ?><data>" +
                    "<program SECTOR_SIZE_IN_BYTES=\"{0}\" filename=\"{1}\" label=\"{2}\" " +
                    "num_partition_sectors=\"{3}\" physical_partition_number=\"{4}\" start_sector=\"{5}\"{6} />" +
                    "</data>",
                    sectorSize, escapedFilename, escapedLabel, numSectors, lun, startSector, verifyAttribute);
            }

            PurgePortBuffer();
            byte[] xmlBytes = Encoding.UTF8.GetBytes(xml);
            TraceEdlTx(xml);
            try
            {
                port.Write(xmlBytes, 0, xmlBytes.Length);
            }
            catch (Exception ex)
            {
                throw new EdlReadFailureException("发送 program 命令时串口写入失败", true, ex);
            }

            ProgramCommandStartResult startResult = WaitForProgramRawMode(15000);
            if (startResult.Status == ProgramCommandStartStatus.PermissionDenied)
            {
                permissionDeniedReason = startResult.Reason;
                return false;
            }
            if (startResult.Status == ProgramCommandStartStatus.Rejected)
                throw new Exception($"设备在接收数据前拒绝 program 命令: {startResult.Reason}");
            if (startResult.Status == ProgramCommandStartStatus.Timeout
                || startResult.Status == ProgramCommandStartStatus.SessionLost)
            {
                throw new EdlReadFailureException(startResult.Reason, true);
            }

            try
            {
                port.Write(data, 0, length);
            }
            catch (Exception ex)
            {
                throw new EdlReadFailureException("发送 program 数据时串口写入失败", true, ex);
            }

            XElement? finalResponse = ReadFirehoseResponse(port, 15000, out string finalContext);
            if (finalResponse == null)
            {
                throw new EdlReadFailureException(
                    "写入数据后等待 final ACK 超时，写入结果未知",
                    true);
            }

            string finalValue = finalResponse.Attribute("value")?.Value ?? string.Empty;
            if (finalValue.Equals("ACK", StringComparison.OrdinalIgnoreCase)
                || finalValue.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            throw new EdlReadFailureException(
                $"写入数据后设备未返回 final ACK，写入结果未知: " +
                GetProgramResponseReason(finalResponse, finalContext),
                true);
        }

        private bool WritePartitionSectors(
            EdlPartitionInfo part,
            long startSector,
            byte[] data,
            int length,
            string sourceImagePath,
            bool forceVipMode,
            bool readBackVerify)
        {
            EdlPartitionWriteStrategy learnedStrategy = GetPartitionWriteStrategy(part);
            if (learnedStrategy == EdlPartitionWriteStrategy.Unsupported)
                throw new Exception($"当前 Firehose 会话已确认不支持写入 {part.Label} (LUN{part.Lun})");

            bool useSpoof = learnedStrategy switch
            {
                EdlPartitionWriteStrategy.Direct => false,
                EdlPartitionWriteStrategy.SpoofRequired => true,
                _ => forceVipMode || ShouldUseProgramSpoofWrite(part)
            };
            VipSpoofStrategy strategy = useSpoof
                ? GetDeterministicVipWriteStrategy(part.Lun, startSector)
                : GetDirectWriteStrategy(part.Lun, part.Label, sourceImagePath);

            bool written;
            string? permissionDeniedReason;
            try
            {
                written = WriteSectorsOnce(
                    part.Lun,
                    startSector,
                    data,
                    length,
                    strategy,
                    readBackVerify,
                    out permissionDeniedReason);
            }
            catch (Exception ex) when (UsesAdaptiveOplusVipProfile && !IsSessionBreakingFailure(ex))
            {
                RememberPartitionWriteStrategy(part, EdlPartitionWriteStrategy.Unsupported);
                throw;
            }
            if (written)
            {
                if (UsesAdaptiveOplusVipProfile)
                {
                    RememberPartitionWriteStrategy(
                        part,
                        useSpoof
                            ? EdlPartitionWriteStrategy.SpoofRequired
                            : EdlPartitionWriteStrategy.Direct);
                }
                return true;
            }

            bool canNegotiateSpoof = !useSpoof
                && UsesAdaptiveOplusVipProfile
                && !string.IsNullOrWhiteSpace(permissionDeniedReason);
            if (!canNegotiateSpoof)
            {
                if (UsesAdaptiveOplusVipProfile)
                    RememberPartitionWriteStrategy(part, EdlPartitionWriteStrategy.Unsupported);
                throw new Exception(permissionDeniedReason ?? "设备拒绝 program 命令");
            }

            _appendNativeLog(
                $"[WriteStrategy] {part.Label} LUN{part.Lun}: direct command rejected before payload; " +
                "switching once to the OPLUS safe label.");
            VipSpoofStrategy safeStrategy = GetDeterministicVipWriteStrategy(part.Lun, startSector);
            bool spoofWritten;
            string? spoofPermissionDeniedReason;
            try
            {
                spoofWritten = WriteSectorsOnce(
                    part.Lun,
                    startSector,
                    data,
                    length,
                    safeStrategy,
                    readBackVerify,
                    out spoofPermissionDeniedReason);
            }
            catch (Exception ex) when (!IsSessionBreakingFailure(ex))
            {
                RememberPartitionWriteStrategy(part, EdlPartitionWriteStrategy.Unsupported);
                throw;
            }
            if (!spoofWritten)
            {
                RememberPartitionWriteStrategy(part, EdlPartitionWriteStrategy.Unsupported);
                throw new Exception(
                    spoofPermissionDeniedReason
                    ?? $"设备拒绝 {part.Label} 的 OPLUS 安全标签写入");
            }

            RememberPartitionWriteStrategy(part, EdlPartitionWriteStrategy.SpoofRequired);
            return true;
        }

        private void WriteRawImageViaProgram(
            string rawPath,
            EdlPartitionInfo part,
            double completedBytesBefore,
            double overallTotalBytes,
            long partTotalBytes,
            bool forceVipMode = false)
        {
            if (_currentPort == null)
                throw new Exception("Serial port is not open.");
            if (string.IsNullOrEmpty(rawPath) || !File.Exists(rawPath))
                throw new Exception("Image file does not exist.");
            if (!long.TryParse(part.StartSector, NumberStyles.Integer, CultureInfo.InvariantCulture, out long startSector))
                throw new Exception($"Invalid start sector: {part.StartSector}");

            int sectorSize = _firehoseSectorSize > 0 ? _firehoseSectorSize : 4096;
            int bytesPerChunk = Math.Max(sectorSize, _firehoseMaxPayloadSize > 0 ? _firehoseMaxPayloadSize : 1024 * 1024);
            bytesPerChunk = (bytesPerChunk / sectorSize) * sectorSize;
            if (bytesPerChunk <= 0)
                bytesPerChunk = sectorSize;

            byte[] buffer = new byte[bytesPerChunk];
            long totalBytes = new FileInfo(rawPath).Length;
            long sentBytes = 0;
            long currentSector = startSector;
            long lastBytes = 0;
            long lastTick = Stopwatch.GetTimestamp();
            long lastSpeedUiTick = 0;
            bool useVipMode = forceVipMode || ShouldUseProgramSpoofWrite(part);
            bool readBackVerify = !ShouldUseFastVipProgramWriteWithoutVerify(part, totalBytes, useVipMode);
            if (!readBackVerify)
            {
                _appendNativeLog($"[FastWrite] {part.Label} 使用快速VIP写入（关闭分段读回校验）");
            }

            void ReportProgress()
            {
                double fraction = totalBytes <= 0 ? 1 : Math.Clamp(sentBytes / (double)totalBytes, 0, 1);
                _updateProgress(Math.Clamp(fraction * 100.0, 0, 100));

                long now = Stopwatch.GetTimestamp();
                long dtTicks = now - lastTick;
                if (dtTicks > 0 && now - lastSpeedUiTick >= Stopwatch.Frequency / 5)
                {
                    double dt = dtTicks / (double)Stopwatch.Frequency;
                    long deltaBytes = sentBytes - lastBytes;
                    if (deltaBytes >= 0)
                    {
                        UpdateSmoothedTransferSpeed(deltaBytes / dt);
                        lastSpeedUiTick = now;
                    }
                    lastBytes = sentBytes;
                    lastTick = now;
                }
            }

            ReportProgress();
            using var fs = new FileStream(rawPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            while (sentBytes < totalBytes)
            {
                int toRead = (int)Math.Min(buffer.Length, totalBytes - sentBytes);
                int read = fs.Read(buffer, 0, toRead);
                if (read <= 0)
                    throw new Exception("Failed to read image data.");

                int padded = ((read + sectorSize - 1) / sectorSize) * sectorSize;
                if (padded > read)
                    Array.Clear(buffer, read, padded - read);

                bool written = WritePartitionSectors(
                    part,
                    currentSector,
                    buffer,
                    padded,
                    rawPath,
                    useVipMode,
                    readBackVerify);
                if (!written)
                    throw new Exception($"Write failed @ sector {currentSector}");

                currentSector += padded / sectorSize;
                sentBytes += read;
                ReportProgress();
            }
        }

        private void WriteSparseImageAsRawViaProgram(
            string sparsePath,
            EdlPartitionInfo part,
            double completedBytesBefore,
            double overallTotalBytes,
            long partTotalBytes,
            bool forceVipMode = false)
        {
            if (_currentPort == null)
                throw new Exception("串口未打开");
            if (string.IsNullOrEmpty(sparsePath) || !File.Exists(sparsePath))
                throw new Exception("镜像文件不存在");
            if (!long.TryParse(part.StartSector, NumberStyles.Integer, CultureInfo.InvariantCulture, out long startSector))
                throw new Exception($"无法解析 start sector: {part.StartSector}");

            int sectorSize = _firehoseSectorSize > 0 ? _firehoseSectorSize : 4096;
            int sectorsPerChunk = Math.Max(1, (_firehoseMaxPayloadSize > 0 ? _firehoseMaxPayloadSize : (16 * 1024 * 1024)) / sectorSize);
            int bytesPerChunk = sectorsPerChunk * sectorSize;
            byte[] outBuf = new byte[bytesPerChunk];

            long outputWritten = 0;
            long lastBytes = 0;
            long lastTick = Stopwatch.GetTimestamp();
            long lastSpeedUiTick = 0;
            long now;

            bool useVipMode = forceVipMode || ShouldUseProgramSpoofWrite(part);
            void ReportProgress(long current, long total)
            {
                double fraction = total <= 0 ? 0 : Math.Clamp(current / (double)total, 0, 1);
                _updateProgress(Math.Clamp(fraction * 100.0, 0, 100));
                now = Stopwatch.GetTimestamp();
                long dtTicks = now - lastTick;
                if (dtTicks > 0 && now - lastSpeedUiTick >= Stopwatch.Frequency / 5)
                {
                    double dt = dtTicks / (double)Stopwatch.Frequency;
                    long deltaBytes = current - lastBytes;
                    if (deltaBytes >= 0)
                    {
                        UpdateSmoothedTransferSpeed(deltaBytes / dt);
                        lastSpeedUiTick = now;
                    }
                    lastBytes = current;
                    lastTick = now;
                }
            }

            using var fs = new FileStream(sparsePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs);
            if (fs.Length < 28)
                throw new Exception("sparse 文件过小");
            uint magic = br.ReadUInt32();
            if (magic != 0xED26FF3A)
                throw new Exception("不是 sparse 镜像");
            ushort major = br.ReadUInt16();
            br.ReadUInt16();
            ushort fileHdrSz = br.ReadUInt16();
            ushort chunkHdrSz = br.ReadUInt16();
            uint blkSz = br.ReadUInt32();
            uint totalBlks = br.ReadUInt32();
            uint totalChunks = br.ReadUInt32();
            br.ReadUInt32();

            if (major != 1)
                throw new Exception($"sparse 版本不支持: {major}");
            if (fileHdrSz < 28 || chunkHdrSz < 12)
                throw new Exception("sparse 头大小异常");
            if (blkSz == 0)
                throw new Exception("sparse 块大小异常");

            if (fileHdrSz > 28)
                fs.Seek(fileHdrSz - 28, SeekOrigin.Current);

            long totalOutBytes = checked((long)totalBlks * blkSz);
            long currentSector = startSector;
            int outBufUsed = 0;
            bool readBackVerify = !ShouldUseFastVipProgramWriteWithoutVerify(part, totalOutBytes, useVipMode);
            if (!readBackVerify)
            {
                _appendNativeLog($"[FastWrite] {part.Label} 使用快速VIP写入（关闭分段读回校验）");
            }

            void FlushOutBuf()
            {
                if (outBufUsed <= 0)
                    return;
                int padded = ((outBufUsed + sectorSize - 1) / sectorSize) * sectorSize;
                if (padded > outBufUsed)
                    Array.Clear(outBuf, outBufUsed, padded - outBufUsed);
                bool written = WritePartitionSectors(
                    part,
                    currentSector,
                    outBuf,
                    padded,
                    sparsePath,
                    useVipMode,
                    readBackVerify);
                if (!written)
                    throw new Exception($"写入失败 @ sector {currentSector}");
                currentSector += padded / sectorSize;
                outputWritten += outBufUsed;
                outBufUsed = 0;
                ReportProgress(outputWritten, totalOutBytes);
            }

            void AppendBytesFromStream(long bytesToCopy)
            {
                while (bytesToCopy > 0)
                {
                    int space = outBuf.Length - outBufUsed;
                    if (space <= 0)
                    {
                        FlushOutBuf();
                        space = outBuf.Length - outBufUsed;
                    }
                    int toRead = (int)Math.Min(bytesToCopy, space);
                    int read = fs.Read(outBuf, outBufUsed, toRead);
                    if (read <= 0)
                        throw new Exception("读取 sparse RAW 数据失败");
                    outBufUsed += read;
                    bytesToCopy -= read;
                    if (outBufUsed == outBuf.Length)
                        FlushOutBuf();
                }
            }

            void AppendFillBytes(uint fillValue, long bytesToAppend)
            {
                Span<byte> fill = stackalloc byte[4];
                fill[0] = (byte)(fillValue & 0xFF);
                fill[1] = (byte)((fillValue >> 8) & 0xFF);
                fill[2] = (byte)((fillValue >> 16) & 0xFF);
                fill[3] = (byte)((fillValue >> 24) & 0xFF);
                int patternOffset = 0;
                while (bytesToAppend > 0)
                {
                    int space = outBuf.Length - outBufUsed;
                    if (space <= 0)
                    {
                        FlushOutBuf();
                        space = outBuf.Length - outBufUsed;
                    }
                    int toWrite = (int)Math.Min(bytesToAppend, space);
                    int fillOffset = outBufUsed;
                    for (int i = 0; i < toWrite; i++)
                        outBuf[fillOffset + i] = fill[(patternOffset + i) & 3];
                    patternOffset = (patternOffset + toWrite) & 3;
                    outBufUsed += toWrite;
                    bytesToAppend -= toWrite;
                    if (outBufUsed == outBuf.Length)
                        FlushOutBuf();
                }
            }

            void SkipDontCareBytes(long bytesToSkip)
            {
                if (bytesToSkip <= 0)
                    return;
                FlushOutBuf();
                if ((bytesToSkip % sectorSize) != 0)
                    throw new Exception("sparse DONT_CARE chunk 未按扇区对齐");
                currentSector += bytesToSkip / sectorSize;
                outputWritten += bytesToSkip;
                ReportProgress(outputWritten, totalOutBytes);
            }

            ReportProgress(0, totalOutBytes);

            for (uint i = 0; i < totalChunks; i++)
            {
                if (fs.Position + chunkHdrSz > fs.Length)
                    throw new Exception("sparse chunk 头越界");
                ushort chunkType = br.ReadUInt16();
                br.ReadUInt16();
                uint chunkSz = br.ReadUInt32();
                uint totalSz = br.ReadUInt32();
                if (chunkHdrSz > 12)
                    fs.Seek(chunkHdrSz - 12, SeekOrigin.Current);

                if (totalSz < chunkHdrSz)
                    throw new Exception("sparse chunk 大小异常");

                long dataBytes = (long)totalSz - chunkHdrSz;
                long outBytes = checked((long)chunkSz * blkSz);

                switch (chunkType)
                {
                    case 0xCAC1:
                        if (dataBytes != outBytes)
                            throw new Exception("RAW chunk 大小不匹配");
                        AppendBytesFromStream(outBytes);
                        break;
                    case 0xCAC2:
                        if (dataBytes != 4)
                            throw new Exception("FILL chunk 大小不匹配");
                        uint fillValue = br.ReadUInt32();
                        AppendFillBytes(fillValue, outBytes);
                        break;
                    case 0xCAC3:
                        if (dataBytes != 0)
                            throw new Exception("DONT_CARE chunk 大小不匹配");
                        SkipDontCareBytes(outBytes);
                        break;
                    case 0xCAC4:
                        if (chunkSz != 0 || dataBytes != 4)
                            throw new Exception("CRC32 chunk 大小不匹配");
                        br.ReadUInt32();
                        break;
                    default:
                        throw new Exception($"Invalid chunk type: 0x{chunkType:X4}");
                }
            }

            FlushOutBuf();
            if (outputWritten != totalOutBytes)
                throw new Exception($"sparse 展开大小不匹配: {outputWritten}/{totalOutBytes}");
            if (fs.Position != fs.Length)
                throw new Exception($"sparse 文件存在未解析尾部数据: {fs.Length - fs.Position} bytes");
            _updateSpeedText(null);
        }

        private static long GetPlannedFlashBytes(string filePath, bool looksSparse)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return 0;
            try
            {
                if (looksSparse && TryGetSparseExpandedBytes(filePath, out long expandedBytes, out _))
                    return expandedBytes;
            }
            catch
            {
            }
            try
            {
                return new FileInfo(filePath).Length;
            }
            catch
            {
                return 0;
            }
        }

        private bool ReceiveDataAfterRawMode(byte[] buffer, int timeoutMs)
        {
            if (_currentPort == null)
                return false;
            int totalBytes = buffer.Length;
            int received = 0;
            bool headerFound = false;
            byte[] probeBuf = new byte[16384];
            int probeIdx = 0;
            DateTime start = DateTime.Now;
            while ((DateTime.Now - start).TotalMilliseconds < timeoutMs)
            {
                if (!headerFound)
                {
                    int available = _currentPort.BytesToRead;
                    if (available > 0)
                    {
                        int read = _currentPort.Read(probeBuf, probeIdx, probeBuf.Length - probeIdx);
                        if (read <= 0)
                            continue;
                        probeIdx += read;
                        string content = Encoding.UTF8.GetString(probeBuf, 0, probeIdx);
                        int ackIndex = content.IndexOf("rawmode=\"true\"", StringComparison.OrdinalIgnoreCase);
                        if (ackIndex == -1)
                            ackIndex = content.IndexOf("rawmode='true'", StringComparison.OrdinalIgnoreCase);
                        if (ackIndex >= 0)
                        {
                            int xmlEndIndex = content.IndexOf("</data>", ackIndex, StringComparison.OrdinalIgnoreCase);
                            int responseStartForLog = content.LastIndexOf("<response", ackIndex, StringComparison.OrdinalIgnoreCase);
                            int responseEndForLog = responseStartForLog >= 0
                                ? content.IndexOf("/>", responseStartForLog, StringComparison.OrdinalIgnoreCase)
                                : -1;
                            if (responseStartForLog >= 0 && responseEndForLog > responseStartForLog)
                                TraceEdlRx(content.Substring(responseStartForLog, responseEndForLog - responseStartForLog + 2));
                            if (xmlEndIndex < 0 && responseEndForLog > responseStartForLog)
                                xmlEndIndex = responseEndForLog - 5;
                            if (xmlEndIndex >= 0)
                            {
                                headerFound = true;
                                int dataStart = responseEndForLog > responseStartForLog && content.IndexOf("</data>", ackIndex, StringComparison.OrdinalIgnoreCase) < 0
                                    ? responseEndForLog + 2
                                    : xmlEndIndex + 7;
                                while (dataStart < probeIdx && (probeBuf[dataStart] == '\n' || probeBuf[dataStart] == '\r'))
                                    dataStart++;
                                int leftover = probeIdx - dataStart;
                                if (leftover > 0)
                                {
                                    int copy = Math.Min(leftover, totalBytes);
                                    Array.Copy(probeBuf, dataStart, buffer, 0, copy);
                                    received = copy;
                                }
                            }
                        }
                        else if (content.Contains("NAK", StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                        if (probeIdx >= probeBuf.Length && !headerFound)
                            probeIdx = 0;
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }
                }
                else
                {
                    if (received >= totalBytes)
                        return true;
                    int available = _currentPort.BytesToRead;
                    if (available > 0)
                    {
                        int toRead = Math.Min(totalBytes - received, Math.Min(available, 1024 * 1024));
                        int read = _currentPort.Read(buffer, received, toRead);
                        if (read > 0)
                            received += read;
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }
                }
            }
            return received >= totalBytes;
        }

        private bool ReceiveReadData(
            SerialPort? port,
            byte[] buffer,
            int initialTimeoutMs,
            int finalAckTimeoutMs,
            bool acceptCompleteDataWithoutFinalAck = false)
        {
            if (port == null || !port.IsOpen)
            {
                SetLastReadFailure("串口未打开或会话已关闭", true);
                return false;
            }

            int totalBytes = buffer.Length;
            int received = 0;
            byte[] probeBuf = new byte[65536];
            int probeIdx = 0;
            bool ackFound = false;
            DateTime start = DateTime.Now;

            while ((DateTime.Now - start).TotalMilliseconds < initialTimeoutMs)
            {
                int available = port.BytesToRead;
                if (available <= 0)
                {
                    Thread.Sleep(1);
                    continue;
                }

                int read = port.Read(probeBuf, probeIdx, probeBuf.Length - probeIdx);
                if (read <= 0)
                    continue;
                probeIdx += read;

                string content = Encoding.UTF8.GetString(probeBuf, 0, probeIdx);
                int responseStart = content.IndexOf("<response", StringComparison.OrdinalIgnoreCase);
                if (responseStart < 0)
                {
                    if (content.Contains("NAK", StringComparison.OrdinalIgnoreCase))
                    {
                        SetLastReadFailure("设备在读取命令阶段返回 NAK", false);
                        return false;
                    }
                    if (probeIdx >= probeBuf.Length)
                    {
                        _appendNativeLog("[Readback] response buffer overflow before ACK");
                        SetLastReadFailure("读取响应超过缓冲区且未找到 ACK", true);
                        return false;
                    }
                    continue;
                }

                int responseEnd = content.IndexOf("/>", responseStart, StringComparison.OrdinalIgnoreCase);
                if (responseEnd <= responseStart)
                {
                    if (probeIdx >= probeBuf.Length)
                    {
                        _appendNativeLog("[Readback] incomplete ACK response");
                        SetLastReadFailure("设备返回了不完整的 ACK 响应", true);
                        return false;
                    }
                    continue;
                }

                string responseXml = content.Substring(responseStart, responseEnd - responseStart + 2);
                TraceEdlRx(responseXml);
                Match valueMatch = Regex.Match(responseXml, @"value\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
                string value = valueMatch.Success ? valueMatch.Groups[1].Value : "";
                if (!value.Equals("ACK", StringComparison.OrdinalIgnoreCase)
                    && !value.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    SetLastReadFailure(
                        string.IsNullOrWhiteSpace(value)
                            ? "设备返回了无法识别的读取响应"
                            : $"设备返回 {value}",
                        false);
                    return false;
                }

                int dataStart = responseEnd + 2;
                int dataWrapperEnd = content.IndexOf("</data>", responseEnd, StringComparison.OrdinalIgnoreCase);
                if (dataWrapperEnd >= 0)
                    dataStart = dataWrapperEnd + 7;
                while (dataStart < probeIdx && (probeBuf[dataStart] == '\r' || probeBuf[dataStart] == '\n'))
                    dataStart++;

                int leftover = probeIdx - dataStart;
                if (leftover > 0)
                {
                    int copy = Math.Min(leftover, totalBytes);
                    Array.Copy(probeBuf, dataStart, buffer, 0, copy);
                    received = copy;
                }
                ackFound = true;
                break;
            }

            if (!ackFound)
            {
                SetLastReadFailure($"读取命令在 {initialTimeoutMs}ms 内未收到初始 ACK", true);
                return false;
            }

            DateTime lastReadAt = DateTime.Now;
            while (received < totalBytes)
            {
                if ((DateTime.Now - lastReadAt).TotalMilliseconds >= initialTimeoutMs)
                {
                    SetLastReadFailure(
                        $"读取数据超时，已接收 {received}/{totalBytes} 字节，连续 {initialTimeoutMs}ms 无新数据",
                        true);
                    return false;
                }
                int available = port.BytesToRead;
                if (available <= 0)
                {
                    Thread.Sleep(1);
                    continue;
                }

                int toRead = Math.Min(totalBytes - received, Math.Min(available, 1024 * 1024));
                int read = port.Read(buffer, received, toRead);
                if (read > 0)
                {
                    received += read;
                    lastReadAt = DateTime.Now;
                }
            }

            if (WaitForAck(port, finalAckTimeoutMs))
                return true;

            if (!acceptCompleteDataWithoutFinalAck || received != totalBytes)
            {
                SetLastReadFailure(
                    $"数据已接收完整，但 {finalAckTimeoutMs}ms 内未收到 final ACK",
                    true);
                return false;
            }

            _appendNativeLog(
                "[Readback] Payload is complete; target omitted the final rawmode=false ACK.");
            ObserveMissingFinalReadAck();
            PurgePortBuffer();
            return true;
        }

        private void ConfigureFirehoseWithEdlStyle(int timeoutMs, int attempt = 0)
        {
            SerialPort? sessionPort = _currentPort;
            if (sessionPort == null || !sessionPort.IsOpen)
                throw new Exception("串口未打开");
            long sessionGeneration = CurrentSessionGeneration;
            object sessionServer = FirehoseServer
                ?? throw new EdlReadFailureException("Firehose 配置失败：会话不存在", true);
            string memoryName = (FirehoseServer?.MemoryName ?? "ufs").ToLowerInvariant();
            int requestedMaxPayload = _firehoseMaxPayloadSize;
            string xml = string.Format(
                "<?xml version=\"1.0\" ?><data><configure MemoryName=\"{0}\" Verbose=\"0\" AlwaysValidate=\"0\" " +
                "MaxPayloadSizeToTargetInBytes=\"{1}\" ZlpAwareHost=\"0\" SkipStorageInit=\"0\" CheckDevinfo=\"0\" EnableFlash=\"1\" /></data>",
                memoryName, _firehoseMaxPayloadSize);
            PurgePortBuffer();
            byte[] configBytes = Encoding.UTF8.GetBytes(xml);
            TraceEdlTx(xml);
            sessionPort.Write(configBytes, 0, configBytes.Length);
            XElement? resp = ReadFirehoseResponse(sessionPort, timeoutMs);
            if (resp == null)
                throw new TimeoutException("Firehose 配置超时");
            if (!IsCurrentSession(sessionGeneration, sessionServer))
                throw new EdlReadFailureException("Firehose 配置失败：会话在等待响应期间已被替换", true);
            string value = resp.Attribute("value")?.Value ?? "";
            if (!value.Equals("ACK", StringComparison.OrdinalIgnoreCase) && !value.Equals("NAK", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Firehose 配置返回异常: " + value);
            string detectedMemoryName = resp.Attribute("MemoryName")?.Value ?? memoryName;
            int? detectedSectorSize = null;
            string? sectorAttr = resp.Attribute("SectorSizeInBytes")?.Value;
            if (!string.IsNullOrEmpty(sectorAttr) && int.TryParse(sectorAttr, out int sectorSize))
            {
                _firehoseSectorSize = sectorSize;
                detectedSectorSize = sectorSize;
            }
            int? detectedMaxPayload = null;
            string? payloadAttr = resp.Attribute("MaxPayloadSizeToTargetInBytes")?.Value
                ?? resp.Attribute("MaxPayloadSizeToTargetInBytesSupported")?.Value;
            if (!string.IsNullOrEmpty(payloadAttr) && int.TryParse(payloadAttr, out int maxPayload))
            {
                _firehoseMaxPayloadSize = Math.Max(64 * 1024, Math.Min(maxPayload, 16 * 1024 * 1024));
                detectedMaxPayload = _firehoseMaxPayloadSize;
            }
            int? detectedMaxXml = null;
            string? maxXmlAttr = resp.Attribute("MaxXMLSizeInBytes")?.Value;
            if (!string.IsNullOrEmpty(maxXmlAttr) && int.TryParse(maxXmlAttr, out int maxXml))
                detectedMaxXml = Math.Max(0, maxXml);

            UpdateFirehoseCapabilities(
                detectedMemoryName,
                detectedSectorSize,
                detectedMaxPayload,
                detectedMaxXml);
            if (value.Equals("NAK", StringComparison.OrdinalIgnoreCase) && _firehoseMaxPayloadSize != requestedMaxPayload)
            {
                if (attempt >= 2)
                    throw new Exception("Firehose Payload 协商超过最大重试次数");
                ConfigureFirehoseWithEdlStyle(timeoutMs, attempt + 1);
                return;
            }
            if (value.Equals("NAK", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Firehose 拒绝当前配置参数");
            MarkFirehoseSessionReady();
        }

        private bool UsesZteFirehoseProfile =>
            _requestedFirehoseProfile.Equals("zte_firehose", StringComparison.OrdinalIgnoreCase);

        private bool UsesAdaptiveOplusVipProfile =>
            _requestedFirehoseProfile.Equals("oplus_vip_adaptive", StringComparison.OrdinalIgnoreCase)
            && _requestedAuthType.Equals("oplus_vip", StringComparison.OrdinalIgnoreCase);

        private void ConfigureZteFirehoseProfile(int timeoutMs, int attempt = 0)
        {
            SerialPort? sessionPort = _currentPort;
            if (sessionPort == null || !sessionPort.IsOpen)
                throw new EdlReadFailureException("ZTE Firehose 配置失败：端口已关闭", true);

            long sessionGeneration = CurrentSessionGeneration;
            FirehoseServer sessionServer = FirehoseServer
                ?? throw new EdlReadFailureException("ZTE Firehose 配置失败：会话不存在", true);
            int requestedMaxPayload = attempt == 0
                ? 1024 * 1024
                : Math.Max(64 * 1024, Math.Min(_firehoseMaxPayloadSize, 1024 * 1024));
            string xml = string.Format(
                CultureInfo.InvariantCulture,
                "<?xml version=\"1.0\" encoding=\"UTF-8\" ?><data><configure MemoryName=\"ufs\" " +
                "Verbose=\"0\" AlwaysValidate=\"0\" MaxDigestTableSizeInBytes=\"8192\" " +
                "MaxPayloadSizeToTargetInBytes=\"{0}\" ZlpAwareHost=\"1\" SkipStorageInit=\"0\" Oem=\"ZTE\"/></data>",
                requestedMaxPayload);

            PurgePortBuffer();
            byte[] bytes = Encoding.UTF8.GetBytes(xml);
            TraceEdlTx(xml);
            try
            {
                sessionPort.Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                throw new EdlReadFailureException("ZTE Firehose 配置失败：端口写入失败", true, ex);
            }

            XElement? resp;
            try
            {
                resp = ReadFirehoseResponse(sessionPort, timeoutMs);
            }
            catch (Exception ex)
            {
                throw new EdlReadFailureException("ZTE Firehose 配置失败：会话读取失败", true, ex);
            }

            if (resp == null)
            {
                throw new EdlReadFailureException(
                    "引导不匹配：设备未响应 ZTE Firehose 配置",
                    true);
            }
            if (!IsCurrentSession(sessionGeneration, sessionServer))
                throw new EdlReadFailureException("ZTE Firehose 配置失败：会话在等待响应期间已被替换", true);

            string value = resp.Attribute("value")?.Value ?? "";
            if (!value.Equals("ACK", StringComparison.OrdinalIgnoreCase)
                && !value.Equals("NAK", StringComparison.OrdinalIgnoreCase))
            {
                throw new EdlReadFailureException("ZTE Firehose 配置返回异常: " + value, true);
            }

            string detectedMemoryName = resp.Attribute("MemoryName")?.Value ?? "UFS";
            int detectedSectorSize = detectedMemoryName.Equals("eMMC", StringComparison.OrdinalIgnoreCase)
                ? 512
                : 4096;
            string? sectorAttr = resp.Attribute("SectorSizeInBytes")?.Value;
            if (!string.IsNullOrWhiteSpace(sectorAttr)
                && int.TryParse(sectorAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedSectorSize)
                && parsedSectorSize > 0)
            {
                detectedSectorSize = parsedSectorSize;
            }

            int detectedMaxPayload = requestedMaxPayload;
            string? payloadAttr = resp.Attribute("MaxPayloadSizeToTargetInBytesSupported")?.Value
                ?? resp.Attribute("MaxPayloadSizeToTargetInBytes")?.Value;
            if (!string.IsNullOrWhiteSpace(payloadAttr)
                && int.TryParse(payloadAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPayload)
                && parsedPayload > 0)
            {
                detectedMaxPayload = Math.Max(64 * 1024, Math.Min(parsedPayload, 16 * 1024 * 1024));
            }

            int detectedMaxXml = 0;
            string? maxXmlAttr = resp.Attribute("MaxXMLSizeInBytes")?.Value;
            if (!string.IsNullOrWhiteSpace(maxXmlAttr))
                int.TryParse(maxXmlAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out detectedMaxXml);

            sessionServer.MemoryName = detectedMemoryName;
            sessionServer.SectorSize = detectedSectorSize;
            sessionServer.MaxPayloadSizeToTarget = detectedMaxPayload;
            _firehoseSectorSize = detectedSectorSize;
            _firehoseMaxPayloadSize = detectedMaxPayload;
            UpdateFirehoseCapabilities(
                detectedMemoryName,
                detectedSectorSize,
                detectedMaxPayload,
                Math.Max(0, detectedMaxXml));

            if (value.Equals("NAK", StringComparison.OrdinalIgnoreCase))
            {
                if (attempt < 2 && detectedMaxPayload != requestedMaxPayload)
                {
                    ConfigureZteFirehoseProfile(timeoutMs, attempt + 1);
                    return;
                }

                throw new EdlReadFailureException(
                    "引导不匹配：设备拒绝 ZTE Firehose 配置参数",
                    true);
            }

            MarkFirehoseSessionReady();
        }

        private struct VipSpoofStrategy
        {
            public string Filename { get; }
            public string Label { get; }
            public int Priority { get; }

            public VipSpoofStrategy(string filename, string label, int priority)
            {
                Filename = filename;
                Label = label;
                Priority = priority;
            }
        }

        private List<VipSpoofStrategy> GetDynamicSpoofStrategies(int lun, long startSector, string? partitionName, bool isGptRead)
        {
            List<VipSpoofStrategy> strategies = new List<VipSpoofStrategy>();
            if (isGptRead || startSector <= 33)
            {
                strategies.Add(new VipSpoofStrategy($"gpt_backup{lun}.bin", "BackupGPT", 0));
                strategies.Add(new VipSpoofStrategy($"gpt_main{lun}.bin", "PrimaryGPT", 1));
            }
            strategies.Add(new VipSpoofStrategy("gpt_backup0.bin", "BackupGPT", 2));
            bool avoidRealPartitionName = IsProtectedEdlWritePartitionName(partitionName);
            if (!string.IsNullOrEmpty(partitionName) && !avoidRealPartitionName)
            {
                string safeName = SanitizePartitionName(partitionName);
                strategies.Add(new VipSpoofStrategy("gpt_backup0.bin", safeName, 3));
                strategies.Add(new VipSpoofStrategy(safeName + ".bin", safeName, 4));
            }
            strategies.Add(new VipSpoofStrategy("ssd", "ssd", 5));
            strategies.Add(new VipSpoofStrategy("gpt_main0.bin", "gpt_main0.bin", 6));
            strategies.Add(new VipSpoofStrategy("buffer.bin", "buffer", 8));
            if (!avoidRealPartitionName)
                strategies.Add(new VipSpoofStrategy("", "", 99));
            return strategies.OrderBy(s => s.Priority).ToList();
        }

        private string SanitizePartitionName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "rawdata";
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder();
            foreach (char c in name)
            {
                bool isValid = true;
                foreach (char inv in invalid)
                {
                    if (c == inv)
                    {
                        isValid = false;
                        break;
                    }
                }
                if (isValid)
                    sb.Append(c);
            }
            string safeName = sb.ToString().ToLowerInvariant();
            if (safeName.Length > 32)
                safeName = safeName.Substring(0, 32);
            return string.IsNullOrEmpty(safeName) ? "rawdata" : safeName;
        }

        private byte[]? ReadSectorsWithVip(
            int lun,
            long startSector,
            int numSectors,
            bool useVipMode,
            string? partitionName,
            int initialTimeoutMs = 15000,
            int finalAckTimeoutMs = 8000,
            bool acceptCompleteDataWithoutFinalAck = false)
        {
            if (_currentPort == null)
                return null;
            List<VipSpoofStrategy> strategies = useVipMode
                ? GetDynamicSpoofStrategies(lun, startSector, partitionName, startSector <= 33)
                : new List<VipSpoofStrategy> { new VipSpoofStrategy("", "", 0) };
            return ReadSectorsWithStrategies(
                lun,
                startSector,
                numSectors,
                strategies,
                initialTimeoutMs,
                finalAckTimeoutMs,
                acceptCompleteDataWithoutFinalAck);
        }

        private byte[]? ReadSectorsWithStrategies(
            int lun,
            long startSector,
            int numSectors,
            IEnumerable<VipSpoofStrategy> strategies,
            int initialTimeoutMs = 15000,
            int finalAckTimeoutMs = 8000,
            bool acceptCompleteDataWithoutFinalAck = false)
        {
            SerialPort? sessionPort = _currentPort;
            if (sessionPort == null || !sessionPort.IsOpen || FirehoseServer == null)
            {
                SetLastReadFailure("串口未打开或 Firehose 会话不存在", true);
                return null;
            }

            int sectorSize = _firehoseSectorSize > 0 ? _firehoseSectorSize : 4096;
            long sessionGeneration = CurrentSessionGeneration;
            object sessionServer = FirehoseServer;
            _lastSuccessfulReadStrategyFilename = null;
            _lastSuccessfulReadStrategyLabel = null;
            SetLastReadFailure("设备未返回有效读取结果", false);
            foreach (var strategy in strategies)
            {
                try
                {
                    if (!IsCurrentSession(sessionGeneration, sessionServer))
                        throw new EdlReadFailureException("读取过程中 Firehose 会话已失效", true);

                    PurgePortBuffer();
                    double sizeKB = (numSectors * sectorSize) / 1024.0;
                    string xml;
                    if (string.IsNullOrEmpty(strategy.Label))
                    {
                        xml = string.Format(
                            CultureInfo.InvariantCulture,
                            "<?xml version=\"1.0\" ?><data>\n" +
                            "<read SECTOR_SIZE_IN_BYTES=\"{0}\" num_partition_sectors=\"{1}\" " +
                            "physical_partition_number=\"{2}\" size_in_KB=\"{3:F1}\" start_sector=\"{4}\" />\n</data>\n",
                            sectorSize, numSectors, lun, sizeKB, startSector);
                    }
                    else if (string.IsNullOrEmpty(strategy.Filename))
                    {
                        xml = string.Format(
                            CultureInfo.InvariantCulture,
                            "<?xml version=\"1.0\" encoding=\"UTF-8\" ?><data>" +
                            "<read physical_partition_number=\"{0}\" label=\"{1}\" start_sector=\"{2}\" " +
                            "num_partition_sectors=\"{3}\" SECTOR_SIZE_IN_BYTES=\"{4}\" /></data>",
                            lun,
                            System.Security.SecurityElement.Escape(strategy.Label) ?? "",
                            startSector,
                            numSectors,
                            sectorSize);
                    }
                    else
                    {
                        xml = string.Format(
                            CultureInfo.InvariantCulture,
                            "<?xml version=\"1.0\" ?><data>\n" +
                            "<read SECTOR_SIZE_IN_BYTES=\"{0}\" filename=\"{1}\" label=\"{2}\" " +
                            "num_partition_sectors=\"{3}\" physical_partition_number=\"{4}\" " +
                            "size_in_KB=\"{5:F1}\" sparse=\"false\" start_sector=\"{6}\" />\n</data>\n",
                            sectorSize,
                            System.Security.SecurityElement.Escape(strategy.Filename) ?? "",
                            System.Security.SecurityElement.Escape(strategy.Label) ?? "",
                            numSectors,
                            lun,
                            sizeKB,
                            startSector);
                    }
                    byte[] readBytes = Encoding.UTF8.GetBytes(xml);
                    TraceEdlTx(xml);
                    sessionPort.Write(readBytes, 0, readBytes.Length);
                    byte[] buffer = new byte[numSectors * sectorSize];
                    if (ReceiveReadData(
                            sessionPort,
                            buffer,
                            initialTimeoutMs,
                            finalAckTimeoutMs,
                            acceptCompleteDataWithoutFinalAck))
                    {
                        if (!IsCurrentSession(sessionGeneration, sessionServer))
                            throw new EdlReadFailureException("读取完成时 Firehose 会话已被替换", true);
                        _lastSuccessfulReadStrategyFilename = strategy.Filename;
                        _lastSuccessfulReadStrategyLabel = strategy.Label;
                        return buffer;
                    }

                    _appendNativeLog(
                        $"[Readback] LUN{lun} sector={startSector} count={numSectors} " +
                        $"strategy={strategy.Filename}/{strategy.Label} failed: {_lastReadFailureReason}");
                    if (_lastReadFailureBreaksSession)
                        throw CreateLastReadFailureException(
                            $"读取 LUN{lun} sector={startSector} count={numSectors}");
                }
                catch (EdlReadFailureException)
                {
                    throw;
                }
                catch (Exception ex) when (
                    ex is IOException
                    || ex is InvalidOperationException
                    || ex is UnauthorizedAccessException
                    || ex is ObjectDisposedException)
                {
                    SetLastReadFailure($"串口通信异常：{ex.Message}", true);
                    throw new EdlReadFailureException(
                        $"读取 LUN{lun} sector={startSector} count={numSectors} 失败：{_lastReadFailureReason}",
                        true,
                        ex);
                }
            }
            return null;
        }

        private byte[]? ReadPrimaryGptPrefix(
            int lun,
            int requestedSectors = GptReadSectors,
            int finalAckTimeoutMs = 8000,
            bool acceptCompleteDataWithoutFinalAck = false)
        {
            if (!_useOplusReadWithoutSpoof)
            {
                return ReadSectorsWithVip(
                    lun,
                    0,
                    requestedSectors,
                    true,
                    "gpt",
                    15000,
                    finalAckTimeoutMs,
                    acceptCompleteDataWithoutFinalAck);
            }

            int sectorSize = _firehoseSectorSize > 0 ? _firehoseSectorSize : 4096;
            int exactGptSectors = sectorSize == 512 ? 34 : 6;
            var strategy = new VipSpoofStrategy($"gpt_main{lun}.bin", "PrimaryGPT", 0);
            return ReadSectorsWithStrategies(
                lun,
                0,
                exactGptSectors,
                new[] { strategy },
                15000,
                finalAckTimeoutMs,
                acceptCompleteDataWithoutFinalAck);
        }

        private byte[]? ProbeOptionalUfsLunGptPrefix(int lun)
        {
            int sectorSize = _firehoseSectorSize > 0 ? _firehoseSectorSize : 4096;
            int sectors = _useOplusReadWithoutSpoof
                ? (sectorSize == 512 ? 34 : 6)
                : GptReadSectors;
            VipSpoofStrategy strategy = _useOplusReadWithoutSpoof
                ? new VipSpoofStrategy($"gpt_main{lun}.bin", "PrimaryGPT", 0)
                : new VipSpoofStrategy($"gpt_backup{lun}.bin", "BackupGPT", 0);
            return ReadSectorsWithStrategies(
                lun,
                0,
                sectors,
                new[] { strategy },
                15000,
                1500,
                true);
        }

        private static bool IsOplusReadWithoutSpoofLoader(
            string? loaderPath,
            EdlOplusLoaderPackage? package)
        {
            if (package == null)
                return false;

            if (!string.IsNullOrWhiteSpace(package.FirehoseProfile))
            {
                return NormalizeFirehoseProfile(package.FirehoseProfile).Equals(
                    "oplus_special_rw",
                    StringComparison.OrdinalIgnoreCase);
            }

            string identity = string.Join(
                    " ",
                    package?.DisplayName,
                    package?.FirehosePath,
                    package?.WorkingDirectory,
                    loaderPath)
                .ToLowerInvariant();
            string normalized = Regex.Replace(identity, @"[^a-z0-9]+", "");

            return normalized.Contains("sm8450", StringComparison.Ordinal)
                || normalized.Contains("sm8475", StringComparison.Ordinal)
                || normalized.Contains("sm8550", StringComparison.Ordinal)
                || normalized.Contains("8gen1", StringComparison.Ordinal)
                || normalized.Contains("8plusgen1", StringComparison.Ordinal)
                || normalized.Contains("8gen1plus", StringComparison.Ordinal)
                || normalized.Contains("8gen2", StringComparison.Ordinal)
                || normalized.Contains("8g1", StringComparison.Ordinal)
                || normalized.Contains("8g2", StringComparison.Ordinal);
        }

        private static bool IsFastVipProgramWriteLoader(
            string? loaderPath,
            EdlOplusLoaderPackage? package)
        {
            if (package != null)
                return false;

            string identity = (loaderPath ?? string.Empty).ToLowerInvariant();
            string normalized = Regex.Replace(identity, @"[^a-z0-9]+", "");
            if (normalized.Contains("oneplus", StringComparison.Ordinal)
                || normalized.Contains("oppo", StringComparison.Ordinal)
                || normalized.Contains("oplus", StringComparison.Ordinal)
                || normalized.Contains("realme", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        private bool ShouldUseFastVipProgramWriteWithoutVerify(
            EdlPartitionInfo part,
            long totalBytes,
            bool useVipMode)
        {
            if (!useVipMode || !_fastVipProgramWrite)
                return false;
            if (IsGptLikeEdlPartition(part.Label))
                return false;
            return totalBytes > 0;
        }

        private static bool IsGptLikeEdlPartition(string? label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return false;
            string normalized = label.Trim();
            return normalized.Equals("PrimaryGPT", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("BackupGPT", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("gpt_main", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("gpt_backup", StringComparison.OrdinalIgnoreCase);
        }

        private int FindGptHeaderOffset(byte[] data)
        {
            int[] searchOffsets = { 4096, 512, 0, 4096 * 2, 512 * 2 };
            foreach (int offset in searchOffsets)
            {
                if (offset + 92 <= data.Length && MatchGptSignature(data, offset))
                    return offset;
            }
            for (int i = 0; i <= data.Length - 92; i += 512)
            {
                if (MatchGptSignature(data, i))
                    return i;
            }
            return -1;
        }

        private bool MatchGptSignature(byte[] data, int offset)
        {
            if (offset + 8 > data.Length)
                return false;
            return data[offset] == (byte)'E' &&
                   data[offset + 1] == (byte)'F' &&
                   data[offset + 2] == (byte)'I' &&
                   data[offset + 3] == (byte)' ' &&
                   data[offset + 4] == (byte)'P' &&
                   data[offset + 5] == (byte)'A' &&
                   data[offset + 6] == (byte)'R' &&
                   data[offset + 7] == (byte)'T';
        }

        private bool IsAllZero(byte[] data, int offset, int length)
        {
            if (offset + length > data.Length)
                return true;
            for (int i = 0; i < length; i++)
            {
                if (data[offset + i] != 0)
                    return false;
            }
            return true;
        }

        private List<EdlPartitionInfo> ParseGptPartitions(byte[] gptData, int lun, int defaultSectorSize, out int detectedSectorSize)
        {
            detectedSectorSize = defaultSectorSize;
            List<EdlPartitionInfo> partitions = new List<EdlPartitionInfo>();
            int headerOffset = FindGptHeaderOffset(gptData);
            if (headerOffset < 0)
                return partitions;
            if (headerOffset + 92 > gptData.Length)
                return partitions;
            ulong myLba = BitConverter.ToUInt64(gptData, headerOffset + 24);
            if (myLba > 0 && headerOffset > 0)
            {
                int sectorSize = (int)(headerOffset / (long)myLba);
                if (sectorSize == 512 || sectorSize == 4096)
                    detectedSectorSize = sectorSize;
            }
            ulong firstUsableLba = BitConverter.ToUInt64(gptData, headerOffset + 40);
            ulong lastUsableLba = BitConverter.ToUInt64(gptData, headerOffset + 48);
            bool hasUsableLbaBounds = firstUsableLba > 0 && lastUsableLba >= firstUsableLba;
            ulong entryLba = BitConverter.ToUInt64(gptData, headerOffset + 72);
            uint numberOfEntries = BitConverter.ToUInt32(gptData, headerOffset + 80);
            uint entrySize = BitConverter.ToUInt32(gptData, headerOffset + 84);
            if (numberOfEntries == 0 || entrySize < 128 || entrySize > 4096)
            {
                _appendNativeLog(
                    $"[GPT] LUN{lun} rejected invalid entry table: count={numberOfEntries}, size={entrySize}");
                return partitions;
            }

            if (entryLba > ulong.MaxValue / (ulong)detectedSectorSize)
            {
                _appendNativeLog(
                    $"[GPT] LUN{lun} rejected overflowing entry offset: LBA={entryLba}");
                return partitions;
            }

            ulong entryOffsetValue = entryLba * (ulong)detectedSectorSize;
            if (entryOffsetValue > int.MaxValue || entryOffsetValue >= (ulong)gptData.Length)
            {
                _appendNativeLog(
                    $"[GPT] LUN{lun} rejected invalid entry offset: LBA={entryLba}, bytes={entryOffsetValue}");
                return partitions;
            }

            int entryOffset = (int)entryOffsetValue;
            uint availableEntries = (uint)((gptData.Length - entryOffset) / entrySize);
            uint entriesToParse = Math.Min(numberOfEntries, availableEntries);
            for (uint i = 0; i < entriesToParse; i++)
            {
                long offsetValue = entryOffset + (long)i * entrySize;
                if (offsetValue < 0 || offsetValue > gptData.Length - (long)entrySize)
                    break;
                int offset = (int)offsetValue;
                if (IsAllZero(gptData, offset, 16))
                    continue;
                ulong startLba = BitConverter.ToUInt64(gptData, offset + 32);
                ulong endLba = BitConverter.ToUInt64(gptData, offset + 40);
                string name = Encoding.Unicode.GetString(gptData, offset + 56, 72).TrimEnd('\0');
                if (string.IsNullOrWhiteSpace(name))
                    name = $"part_{lun}_{i}";

                if (startLba == 0
                    || endLba == 0
                    || endLba < startLba
                    || startLba > long.MaxValue)
                {
                    _appendNativeLog(
                        $"[GPT] LUN{lun} skipped invalid entry {name}: start={startLba}, end={endLba}");
                    continue;
                }
                if (hasUsableLbaBounds
                    && (startLba < firstUsableLba || endLba > lastUsableLba))
                {
                    _appendNativeLog(
                        $"[GPT] LUN{lun} skipped out-of-range entry {name}: " +
                        $"start={startLba}, end={endLba}, usable={firstUsableLba}-{lastUsableLba}");
                    continue;
                }

                ulong sectorLenValue = endLba - startLba + 1;
                if (sectorLenValue == 0
                    || sectorLenValue > long.MaxValue
                    || sectorLenValue > (ulong)(long.MaxValue / detectedSectorSize))
                {
                    _appendNativeLog(
                        $"[GPT] LUN{lun} skipped oversized entry {name}: sectors={sectorLenValue}");
                    continue;
                }

                long sectorLen = (long)sectorLenValue;
                EdlPartitionInfo info = new EdlPartitionInfo
                {
                    Label = name,
                    Lun = lun,
                    StartSector = startLba.ToString(),
                    SectorLen = sectorLen,
                    BytesPerSector = detectedSectorSize
                };
                partitions.Add(info);
            }
            return partitions;
        }

        private List<EdlPartitionInfo> TryReadGptPartitionsViaFhLoader(int defaultSectorSize)
        {
            List<EdlPartitionInfo> partitions = new List<EdlPartitionInfo>();
            if (string.IsNullOrWhiteSpace(_currentPortName))
                return partitions;

            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fh_loader.exe");
            if (!File.Exists(exePath))
                return partitions;

            string workDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp", "edl_gpt_read_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(workDir);
            string portArg = NormalizeEdlPortArg(_currentPortName);
            string memoryName = (FirehoseServer?.MemoryName ?? "ufs").ToLowerInvariant();
            string configureArg = GetFhLoaderSkipConfigureArg(exePath);
            bool supportsSpecialRwMode = FhLoaderSupportsOption(exePath, "special_rw_mode");
            string[] modes = supportsSpecialRwMode
                ? new[] { "oplus_gptbackup", "oplus_gptmain", "" }
                : new[] { "" };

            CloseFirehosePortForExternalTool();
            try
            {
                foreach (string mode in modes)
                {
                    List<EdlPartitionInfo> modePartitions = new List<EdlPartitionInfo>();
                    int modeSectorSize = defaultSectorSize;
                    string specialModeArg = string.IsNullOrEmpty(mode) ? "" : $" --special_rw_mode={mode}";
                    _appendNativeLog(string.IsNullOrEmpty(mode)
                        ? "[Info] fh_loader GPT read fallback"
                        : $"[Info] fh_loader GPT read fallback: {mode}");

                    foreach (int lun in GetCandidatePhysicalLuns())
                    {
                        string outputName = $"gpt_lun{lun}.bin";
                        string outputPath = Path.Combine(workDir, outputName);
                        try
                        {
                            if (File.Exists(outputPath))
                                File.Delete(outputPath);

                            string xmlPath = Path.Combine(workDir, $"read_gpt_lun{lun}.xml");
                            string xml = string.Format(
                                CultureInfo.InvariantCulture,
                                "<?xml version=\"1.0\" ?><data><program SECTOR_SIZE_IN_BYTES=\"{0}\" filename=\"{1}\" physical_partition_number=\"{2}\" label=\"gpt\" start_sector=\"0\" num_partition_sectors=\"{3}\" /></data>",
                                modeSectorSize,
                                outputName,
                                lun,
                                GptReadSectors);
                            File.WriteAllText(xmlPath, xml, Encoding.UTF8);

                            string args =
                                $"--port={portArg} --memoryname={memoryName} --sendxml=\"{xmlPath}\" --convertprogram2read " +
                                $"--mainoutputdir=\"{workDir}\" {configureArg} --showpercentagecomplete{specialModeArg} --noprompt --loglevel=1";
                            var startInfo = new ProcessStartInfo(exePath, args)
                            {
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                CreateNoWindow = true,
                                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory
                            };
                            var result = RunProcessWithTimeout(startInfo, 30000);
                            ThrowIfNativePortOpenFailed(result.Output + "\n" + result.Error, _currentPortName);
                            if (result.TimedOut || result.ExitCode != 0 || !File.Exists(outputPath))
                                continue;

                            byte[] gptData = File.ReadAllBytes(outputPath);
                            if (gptData.Length < 512)
                                continue;

                            int detectedSectorSize;
                            List<EdlPartitionInfo> lunParts = ParseGptPartitions(gptData, lun, modeSectorSize, out detectedSectorSize);
                            if (lunParts.Count > 0)
                            {
                                modePartitions.AddRange(lunParts);
                                modeSectorSize = detectedSectorSize;
                            }
                        }
                        catch (Exception ex)
                        {
                            _appendNativeLog($"[Warn] fh_loader GPT read LUN{lun} failed: {ex.Message}");
                        }
                    }

                    if (modePartitions.Count > 0)
                    {
                        RememberPhysicalLuns(modePartitions.Select(partition => partition.Lun));
                        return modePartitions;
                    }
                }
            }
            finally
            {
                try
                {
                    ReopenFirehoseAfterExternalTool();
                }
                catch (Exception ex)
                {
                    _appendNativeLog("[Warn] reopen Firehose after fh_loader GPT read failed: " + ex.Message);
                    throw new EdlReadFailureException(
                        "fh_loader GPT 读取后无法恢复 Firehose 会话：" + ex.Message,
                        true,
                        ex);
                }
            }

            return partitions;
        }

        public List<EdlPartitionInfo> ReadGptPartitionsFromDevice(
            IEnumerable<int>? preferredPhysicalLuns = null)
        {
            if (_currentPort == null)
                throw new Exception("串口未打开");
            int defaultSectorSize = _firehoseSectorSize > 0 ? _firehoseSectorSize : 4096;
            List<EdlPartitionInfo> partitions = new List<EdlPartitionInfo>();
            var directDataLuns = new HashSet<int>();
            bool directReadAnyData = false;
            bool directReadHadException = false;
            int[] preferredLuns = NormalizePhysicalLuns(preferredPhysicalLuns);
            bool controlledUfsDiscovery = preferredLuns.Length == 0
                && _knownPhysicalLuns.Length == 0
                && !IsEmmcStorage();
            if (_useOplusReadWithoutSpoof)
                _log("启用 [8G1/8G1+/8G2] 特殊读写模式...", null);
            foreach (int lun in GetCandidatePhysicalLuns(preferredLuns))
            {
                ThrowIfStopRequested();
                _updateCurrentPartitionText($"LUN{lun} GPT");
                bool readOk = false;
                bool lunResponded = false;
                try
                {
                    byte[]? gptData = controlledUfsDiscovery && lun >= 6
                        ? ProbeOptionalUfsLunGptPrefix(lun)
                        : ReadPrimaryGptPrefix(lun);
                    if (gptData != null && gptData.Length >= 512)
                    {
                        lunResponded = true;
                        directReadAnyData = true;
                        directDataLuns.Add(lun);
                        int detectedSectorSize;
                        List<EdlPartitionInfo> lunParts = ParseGptPartitions(gptData, lun, defaultSectorSize, out detectedSectorSize);
                        if (lunParts.Count > 0)
                        {
                            partitions.AddRange(lunParts);
                            defaultSectorSize = detectedSectorSize;
                            _firehoseSectorSize = detectedSectorSize;
                            UpdateFirehoseCapabilities(sectorSize: detectedSectorSize);
                            readOk = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    directReadHadException = true;
                    _appendNativeLog($"[Warn] direct GPT read LUN{lun} failed: {ex.Message}");
                    if (IsSessionBreakingFailure(ex))
                    {
                        _log($"解析 LUN{lun} GPT...Failed", null);
                        throw;
                    }
                }
                if (controlledUfsDiscovery
                    && !lunResponded
                    && directDataLuns.Count > 0
                    && IsUnsupportedPhysicalLunReadFailure(_lastReadFailureReason))
                {
                    _log($"解析 LUN{lun} GPT...跳过", null);
                    _appendNativeLog(
                        $"[Capabilities] skipped unavailable UFS LUN{lun}: {_lastReadFailureReason}");
                    continue;
                }
                _log($"解析 LUN{lun} GPT...{(readOk ? "OK" : "Failed")}", null);
            }
            ThrowIfStopRequested();
            if (!_useOplusReadWithoutSpoof
                && partitions.Count == 0
                && (!directReadAnyData || directReadHadException))
            {
                _log("直连读取GPT失败，尝试 fh_loader 兼容读取...", "warn");
                partitions = TryReadGptPartitionsViaFhLoader(defaultSectorSize);
            }
            if (partitions.Count == 0 && (!directReadAnyData || directReadHadException))
            {
                string reason = string.IsNullOrWhiteSpace(_lastReadFailureReason)
                    ? "未读取到有效 GPT 数据"
                    : _lastReadFailureReason!;
                throw new Exception("读取分区表失败：" + reason);
            }
            if (directDataLuns.Count > 0)
                RememberPhysicalLuns(directDataLuns);
            else if (partitions.Count > 0)
                RememberPhysicalLuns(partitions.Select(partition => partition.Lun));
            ObserveDevicePartitions(partitions);
            return partitions;
        }

        private IReadOnlyList<int> GetCandidatePhysicalLuns(
            IEnumerable<int>? preferredPhysicalLuns = null)
        {
            int[] preferredLuns = NormalizePhysicalLuns(preferredPhysicalLuns);
            if (preferredLuns.Length > 0)
                return preferredLuns;
            if (_knownPhysicalLuns.Length > 0)
                return _knownPhysicalLuns;
            if (IsEmmcStorage())
                return new[] { 0 };

            return Enumerable.Range(0, MaxUfsPhysicalLunProbeCount).ToArray();
        }

        private static int[] NormalizePhysicalLuns(IEnumerable<int>? luns)
        {
            return luns?
                .Where(lun => lun >= 0)
                .Distinct()
                .OrderBy(lun => lun)
                .ToArray()
                ?? Array.Empty<int>();
        }

        private bool IsEmmcStorage()
        {
            string memoryName = GetDetectedMemoryName();
            if (string.IsNullOrWhiteSpace(memoryName)
                || memoryName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                memoryName = (FirehoseServer?.MemoryName ?? string.Empty).Trim();
            }
            return memoryName.Contains("emmc", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUnsupportedPhysicalLunReadFailure(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return false;

            return reason.Contains("NAK", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("unsupported", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("invalid lun", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("invalid physical", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("physical partition", StringComparison.OrdinalIgnoreCase);
        }

        private void RememberPhysicalLuns(IEnumerable<int> luns)
        {
            int[] parsedLuns = luns
                .Where(lun => lun >= 0)
                .Distinct()
                .OrderBy(lun => lun)
                .ToArray();
            if (parsedLuns.Length > 0)
            {
                _knownPhysicalLuns = parsedLuns;
                ObservePhysicalLuns(parsedLuns);
            }
        }

        public void ReadAndLogVersionInformation(IList<EdlPartitionInfo> partitions)
        {
            Debug.Assert(FirehoseServer != null);
            ThrowIfStopRequested();
            if (partitions == null || partitions.Count == 0)
                throw new Exception("未读取到设备分区表");

            _updateCurrentPartitionText("读取版本信息");
            _updateProgress(0);
            _log("开始读取设备版本信息...", null);

            try
            {
                var parser = new EdlBuildPropReader();
                var versionReadTimer = Stopwatch.StartNew();
                BuildPropResult result;
                EdlPartitionInfo? superPartition = partitions.FirstOrDefault(partition =>
                    partition.Label.Equals("super", StringComparison.OrdinalIgnoreCase))
                    ?? partitions.FirstOrDefault(partition =>
                        partition.Label.Equals("super_a", StringComparison.OrdinalIgnoreCase));

                if (superPartition != null)
                {
                    _log("解析 super 动态分区元数据...", null);
                    _updateSpeedText("读取 Super 元数据");
                    result = parser.ReadFromSuper(
                        CreatePartitionByteReader(superPartition, versionReadTimer),
                        status =>
                        {
                            _appendNativeLog("[VersionInfo] " + status);
                            _updateSpeedText(status);
                            _updateCurrentPartitionText(status);
                        },
                        (completed, total) =>
                        {
                            double percent = total <= 0
                                ? 10
                                : 10 + completed * 90.0 / total;
                            _updateProgress(percent);
                        });
                }
                else
                {
                    result = new BuildPropResult();
                    string[] priority =
                    {
                        "my_manifest", "odm", "my_product", "vendor",
                        "product", "system_ext", "system"
                    };

                    List<EdlPartitionInfo> versionPartitions = priority
                        .Select(target => partitions.FirstOrDefault(item =>
                            NormalizeVersionPartitionName(item.Label)
                                .Equals(target, StringComparison.OrdinalIgnoreCase)))
                        .Where(partition => partition != null)
                        .Cast<EdlPartitionInfo>()
                        .ToList();

                    for (int index = 0; index < versionPartitions.Count; index++)
                    {
                        ThrowIfStopRequested();
                        EdlPartitionInfo partition = versionPartitions[index];
                        _updateSpeedText($"解析 {partition.Label} 版本信息...");
                        _updateCurrentPartitionText($"解析 {partition.Label} 版本信息...");

                        parser.ReadFromPhysical(
                            result,
                            partition.Label,
                            CreatePartitionByteReader(partition, versionReadTimer),
                            status => _appendNativeLog("[VersionInfo] " + status));
                        _updateProgress((index + 1) * 100.0 / Math.Max(1, versionPartitions.Count));
                    }
                }

                ThrowIfStopRequested();
                if (result.Properties.Count == 0)
                    throw new Exception("未能从设备分区中读取到 build.prop");

                _updateProgress(100);

                string? brand = result.Get(
                    "ro.product.brand",
                    "ro.product.odm.brand",
                    "ro.product.vendor.brand");
                string? marketName = result.Get(
                    "ro.vendor.oplus.market.enname",
                    "ro.vendor.oplus.market.name",
                    "ro.config.marketing_name");
                string? model = result.Get(
                    "ro.product.model",
                    "ro.product.odm.model",
                    "ro.product.vendor.model");
                string? device = result.Get(
                    "ro.product.device",
                    "ro.product.odm.device",
                    "ro.vendor.product.device.oem");
                string? project = result.Get(
                    "ro.separate.soft",
                    "ro.vendor.product.oem");
                string? platform = result.Get(
                    "ro.board.platform",
                    "ro.product.oplus.cpuinfo");
                string? android = result.Get(
                    "ro.build.version.release",
                    "ro.product.build.version.release",
                    "ro.build.version.release_or_codename");
                string? systemVersion = result.Get(
                    "ro.build.display.id.show",
                    "ro.build.display.id",
                    "ro.build.version.oplusrom",
                    "ro.build.version.oplusrom.display");
                string? ota = result.Get(
                    "ro.build.version.ota",
                    "ro.build.display.ota");
                string? securityPatch = result.Get(
                    "ro.build.version.security_patch",
                    "ro.vendor.build.security_patch");
                string? region = result.Get(
                    "ro.vendor.oplus.regionmark",
                    "ro.oplus.image.system_ext.area");
                string? buildDate = result.Get(
                    "ro.build.date",
                    "ro.product.build.date",
                    "ro.system.build.date");
                string? fingerprint = result.Get(
                    "ro.build.fingerprint",
                    "ro.product.build.fingerprint",
                    "ro.system.build.fingerprint",
                    "ro.vendor.build.fingerprint");

                string deviceSummary = JoinVersionValues(" · ", marketName, model);
                _log($"__EDL_VERSION_HEADER__|{deviceSummary}", null);
                LogVersionSummaryRow("品牌", brand);
                LogVersionSummaryRow("代号", JoinVersionValues(
                    " · ",
                    device,
                    PrefixVersionValue("项目", project)));
                LogVersionSummaryRow("平台", platform);
                LogVersionSummaryRow("Android", JoinVersionValues(
                    " · ",
                    android,
                    result.FileSystems.Count > 0
                        ? string.Join(" / ", result.FileSystems.OrderBy(item => item))
                        : null));
                LogVersionSummaryRow("系统版本", systemVersion);
                LogVersionSummaryRow("OTA版本", ota);
                LogVersionSummaryRow("安全补丁", securityPatch);
                LogVersionSummaryRow("地区", region);
                LogVersionSummaryRow("编译日期", buildDate);
                LogVersionSummaryRow("系统指纹", fingerprint);
                _log("__EDL_VERSION_DONE__", "success");
            }
            catch (TimeoutException)
            {
                _appendNativeLog("[VersionInfo] Read timed out; releasing the Firehose session.");
                DisposeCurrentSerialPort();
                throw;
            }
            finally
            {
                _updateSpeedText(null);
                _updateCurrentPartitionText(null);
            }
        }

        private void LogVersionSummaryRow(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                _log($"__EDL_VERSION_ROW__|{name}|{value}", null);
        }

        private static string? PrefixVersionValue(string label, string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : $"{label} {value}";
        }

        private static string JoinVersionValues(string separator, params string?[] values)
        {
            return string.Join(
                separator,
                values.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private IByteReader CreatePartitionByteReader(
            EdlPartitionInfo partition,
            Stopwatch versionReadTimer)
        {
            int sectorSize = _firehoseSectorSize > 0 ? _firehoseSectorSize : 4096;
            return new CachedByteReader(
                (offset, length) => ReadPartitionByteRange(
                    partition,
                    offset,
                    length,
                    versionReadTimer),
                sectorSize);
        }

        private byte[]? ReadPartitionByteRange(
            EdlPartitionInfo partition,
            long byteOffset,
            int byteLength,
            Stopwatch versionReadTimer)
        {
            ThrowIfStopRequested();
            if (versionReadTimer.ElapsedMilliseconds >= VersionInfoTotalTimeoutMs)
                throw new TimeoutException("读取版本信息超时，已停止解析");
            if (byteOffset < 0 || byteLength < 0)
                return null;
            if (byteLength == 0)
                return Array.Empty<byte>();
            if (!long.TryParse(
                    partition.StartSector,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long partitionStartSector))
                throw new Exception($"无法解析 {partition.Label} 的起始扇区");

            int sectorSize = _firehoseSectorSize > 0 ? _firehoseSectorSize : 4096;
            long firstRelativeSector = byteOffset / sectorSize;
            long lastRelativeSector = checked((byteOffset + byteLength - 1) / sectorSize);
            int sectorCount = checked((int)(lastRelativeSector - firstRelativeSector + 1));
            var readInfo = new EdlPartitionInfo
            {
                Label = "BackupGPT",
                FileSectorOffset = 0,
                StartSector = checked(partitionStartSector + firstRelativeSector)
                    .ToString(CultureInfo.InvariantCulture),
                SectorLen = sectorCount,
                BytesPerSector = sectorSize,
                Sparse = false,
                Lun = partition.Lun
            };

            _appendNativeLog(
                $"[VersionInfo] Read {partition.Label} LUN{partition.Lun} " +
                $"sector={readInfo.StartSector} count={sectorCount} as BackupGPT");
            byte[]? sectorData = ReadSectorsWithStrategies(
                partition.Lun,
                partitionStartSector + firstRelativeSector,
                sectorCount,
                new[]
                {
                    new VipSpoofStrategy(
                        $"gpt_backup{partition.Lun}.bin",
                        "BackupGPT",
                        0)
                },
                VersionInfoReadDataTimeoutMs,
                VersionInfoReadFinalAckTimeoutMs,
                true);
            if (sectorData == null || sectorData.Length != sectorCount * sectorSize)
            {
                throw CreateLastReadFailureException(
                    $"读取 {partition.Label} 版本数据");
            }
            if (versionReadTimer.ElapsedMilliseconds >= VersionInfoTotalTimeoutMs)
                throw new TimeoutException("读取版本信息超时，已停止解析");

            int sourceOffset = (int)(byteOffset % sectorSize);
            if (sourceOffset + byteLength > sectorData.Length)
                return null;

            byte[] result = new byte[byteLength];
            Buffer.BlockCopy(sectorData, sourceOffset, result, 0, byteLength);
            return result;
        }

        private static string NormalizeVersionPartitionName(string name)
        {
            if (name.EndsWith("_a", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("_b", StringComparison.OrdinalIgnoreCase))
                return name[..^2];
            return name;
        }

        public (List<EdlPartitionInfo> Partitions, string? ProgrammerPath) ParseFlashPack(string programPath)
        {
            if (string.IsNullOrEmpty(programPath) || !Directory.Exists(programPath))
                throw new Exception("无效的刷机包路径");
            DirectoryInfo info = new DirectoryInfo(programPath);
            string? programmer = null;
            foreach (var item in info.GetFiles())
            {
                if (item.Name.StartsWith("prog", StringComparison.OrdinalIgnoreCase) &&
                    (item.Extension.Equals(".mbn", StringComparison.OrdinalIgnoreCase) ||
                     item.Extension.Equals(".elf", StringComparison.OrdinalIgnoreCase)))
                    programmer = item.FullName;
            }
            string[] programFiles = info.GetFiles("rawprogram*.xml")
                .Select(file => file.FullName)
                .Where(file => !MainWindow.IsExcludedEdlRawProgramFile(file))
                .ToArray();
            if (programFiles.Length == 0)
                throw new Exception("未在目录中找到 rawprogram*.xml");
            List<EdlPartitionInfo> partitions = ProgramFlasher.ParseProgramFiles(programPath, programFiles);
            return (partitions, programmer);
        }

        public (List<EdlPartitionInfo> Partitions, string? ProgrammerPath) ParseFlashPack(string programPath, string[] programFiles)
        {
            if (string.IsNullOrEmpty(programPath) || !Directory.Exists(programPath))
                throw new Exception("无效的刷机包路径");
            DirectoryInfo info = new DirectoryInfo(programPath);
            string? programmer = null;
            foreach (var item in info.GetFiles())
            {
                if (item.Name.StartsWith("prog", StringComparison.OrdinalIgnoreCase) &&
                    (item.Extension.Equals(".mbn", StringComparison.OrdinalIgnoreCase) ||
                     item.Extension.Equals(".elf", StringComparison.OrdinalIgnoreCase)))
                    programmer = item.FullName;
            }
            string[] files = programFiles
                .Where(File.Exists)
                .ToArray();
            if (files.Length == 0)
                throw new Exception("未选择 rawprogram*.xml");
            List<EdlPartitionInfo> partitions = ProgramFlasher.ParseProgramFiles(programPath, files);
            return (partitions, programmer);
        }

        private static bool IsEdlPermissionNak(Exception ex)
        {
            string message = ex.ToString();
            return message.Contains("Error from device NAK", StringComparison.OrdinalIgnoreCase)
                || message.Contains("not allowed on external network", StringComparison.OrdinalIgnoreCase)
                || message.Contains("not get permission", StringComparison.OrdinalIgnoreCase)
                || message.Contains("decrypt token", StringComparison.OrdinalIgnoreCase);
        }

        private void ReadPartitionWithSafeLabel(EdlPartitionInfo partition)
        {
            if (string.IsNullOrWhiteSpace(partition.FilePath))
                throw new Exception("回读文件路径为空");
            if (!long.TryParse(
                    partition.StartSector,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long startSector))
                throw new Exception($"无法解析 {partition.Label} 的起始扇区");

            int sectorSize = _firehoseSectorSize > 0 ? _firehoseSectorSize : 4096;
            long totalSectors = partition.SectorLen;
            if (totalSectors <= 0)
                throw new Exception($"{partition.Label} 分区大小无效");

            int maxPayload = _firehoseMaxPayloadSize > 0
                ? _firehoseMaxPayloadSize
                : 1024 * 1024;
            if (IsEdlSuperPartition(partition))
                maxPayload = Math.Min(maxPayload, 4 * 1024 * 1024);
            int sectorsPerChunk = Math.Max(1, maxPayload / sectorSize);
            long totalBytes = checked(totalSectors * sectorSize);
            long completedBytes = 0;
            long currentSector = startSector;
            long remainingSectors = totalSectors;
            long lastBytes = 0;
            long lastTick = Stopwatch.GetTimestamp();
            long lastSpeedUiTick = 0;
            bool superReadUsedBackupGptSpoof = false;

            using (var output = new FileStream(
                partition.FilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read))
            {
                while (remainingSectors > 0)
            {
                int sectorCount = (int)Math.Min(remainingSectors, sectorsPerChunk);
                byte[]? data = ReadSectorsForReadbackPartition(
                    partition.Lun,
                    currentSector,
                    sectorCount,
                    partition.Label);
                if (data == null || data.Length != sectorCount * sectorSize)
                {
                    throw CreateLastReadFailureException(
                        $"回读 {partition.Label} (LUN{partition.Lun}) sector={currentSector} count={sectorCount}");
                }

                if (completedBytes == 0 &&
                    IsEdlSuperPartition(partition) &&
                    _lastSuccessfulReadStrategyLabel != null &&
                    _lastSuccessfulReadStrategyLabel.Equals("BackupGPT", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(_lastSuccessfulReadStrategyFilename))
                {
                    superReadUsedBackupGptSpoof = true;
                }

                if (completedBytes == 0 && IsEdlSuperPartition(partition) && !LooksLikeSuperFirstChunk(data))
                    throw new Exception("super 回读首块未识别到 LP metadata，已停止保存，避免生成异常镜像");

                output.Write(data, 0, data.Length);
                currentSector += sectorCount;
                remainingSectors -= sectorCount;
                completedBytes += data.Length;
                _updateProgress(Math.Clamp(completedBytes * 100.0 / totalBytes, 0, 100));

                long now = Stopwatch.GetTimestamp();
                if (now - lastSpeedUiTick >= Stopwatch.Frequency / 5)
                {
                    long elapsedTicks = now - lastTick;
                    if (elapsedTicks > 0)
                    {
                        double elapsedSeconds = elapsedTicks / (double)Stopwatch.Frequency;
                        UpdateSmoothedTransferSpeed((completedBytes - lastBytes) / elapsedSeconds);
                        lastBytes = completedBytes;
                        lastTick = now;
                        lastSpeedUiTick = now;
                    }
                }
            }
            output.Flush();
            }

            string? normalizeReason = null;
            if (superReadUsedBackupGptSpoof &&
                TryNormalizeOplusSuperReadbackMetadata(partition.FilePath, out normalizeReason))
            {
                _appendNativeLog("[SuperReadback] normalized OPLUS LP metadata for flashable super image");
            }
            else if (superReadUsedBackupGptSpoof && !string.IsNullOrWhiteSpace(normalizeReason))
            {
                _appendNativeLog("[SuperReadback] skip LP metadata normalization: " + normalizeReason);
            }
        }

        private void ReadSuperPartitionWithFhLoader(EdlPartitionInfo partition)
        {
            if (string.IsNullOrWhiteSpace(_currentPortName))
                throw new Exception("no EDL port is connected");
            if (string.IsNullOrWhiteSpace(partition.FilePath))
                throw new Exception("super output path is empty");
            if (!long.TryParse(
                    partition.StartSector,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long startSector) ||
                startSector < 0)
                throw new Exception($"invalid super start sector: {partition.StartSector}");

            int sectorSize = partition.BytesPerSector > 0
                ? partition.BytesPerSector
                : (_firehoseSectorSize > 0 ? _firehoseSectorSize : 4096);
            long totalSectors = partition.SectorLen;
            if (totalSectors <= 0)
                throw new Exception("super partition size is invalid");
            long expectedBytes = checked(totalSectors * (long)sectorSize);

            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fh_loader.exe");
            if (!File.Exists(exePath))
                throw new Exception("fh_loader.exe not found");

            string outputDir = Path.GetDirectoryName(partition.FilePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            Directory.CreateDirectory(outputDir);
            string outputFileName = Path.GetFileName(partition.FilePath);
            string outputPath = Path.Combine(outputDir, outputFileName);
            string workDir = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "tmp",
                "edl_super_read_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(workDir);
            string xmlPath = Path.Combine(workDir, "super_read.xml");
            string legacyPortTracePath = Path.Combine(outputDir, "port_trace.txt");
            DeleteIfExists(legacyPortTracePath);

            string escapedFileName = System.Security.SecurityElement.Escape(outputFileName) ?? outputFileName;
            string escapedLabel = System.Security.SecurityElement.Escape(partition.Label) ?? partition.Label;
            string xml = string.Format(
                CultureInfo.InvariantCulture,
                "<?xml version=\"1.0\" ?><data>{0}" +
                "<program SECTOR_SIZE_IN_BYTES=\"{1}\" filename=\"{2}\" physical_partition_number=\"{3}\" label=\"{4}\" start_sector=\"{5}\" num_partition_sectors=\"{6}\" />{0}" +
                "</data>",
                Environment.NewLine,
                sectorSize,
                escapedFileName,
                partition.Lun,
                escapedLabel,
                startSector,
                totalSectors);
            File.WriteAllText(xmlPath, xml, Encoding.UTF8);

            string portArg = NormalizeEdlPortArg(_currentPortName);
            string memoryName = (FirehoseServer?.MemoryName ?? "ufs").ToLowerInvariant();
            string configureArg = GetFhLoaderSkipConfigureArg(exePath);
            bool supportsSpecialRwMode = FhLoaderSupportsOption(exePath, "special_rw_mode");
            string[] modes = supportsSpecialRwMode
                ? new[] { "oplus_gptbackup", "oplus_gptmain", "" }
                : new[] { "" };

            string lastError = "";
            CloseFirehosePortForExternalTool();
            try
            {
                foreach (string mode in modes)
                {
                    try
                    {
                        if (File.Exists(outputPath))
                            File.Delete(outputPath);
                    }
                    catch
                    {
                    }

                    string specialModeArg = string.IsNullOrEmpty(mode) ? "" : $" --special_rw_mode={mode}";
                    string args =
                        $"--port={portArg} --memoryname={memoryName} --sendxml=\"{xmlPath}\" --convertprogram2read " +
                        $"--mainoutputdir=\"{outputDir}\" --porttracename=\"NUL\" {configureArg} " +
                        $"--showpercentagecomplete{specialModeArg} --noprompt --loglevel=1";
                    _appendNativeLog(string.IsNullOrEmpty(mode)
                        ? "[SuperReadback] fh_loader read"
                        : $"[SuperReadback] fh_loader read: {mode}");

                    var startInfo = new ProcessStartInfo(exePath, args)
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory
                    };
                    var result = RunProcessWithTimeout(
                        startInfo,
                        60 * 60 * 1000,
                        outputPath,
                        expectedBytes);
                    ThrowIfNativePortOpenFailed(result.Output + "\n" + result.Error, _currentPortName);

                    if (!result.TimedOut && result.ExitCode == 0 && File.Exists(outputPath))
                    {
                        long actualBytes = new FileInfo(outputPath).Length;
                        if (actualBytes == expectedBytes)
                        {
                            _updateProgress(100);
                            return;
                        }

                        lastError = $"size mismatch, expected {expectedBytes}, actual {actualBytes}";
                        _appendNativeLog($"[Warn] fh_loader super read {mode} {lastError}");
                    }
                    else
                    {
                        lastError = result.TimedOut
                            ? "fh_loader super read timed out"
                            : $"fh_loader exit={result.ExitCode}: {result.Output}{Environment.NewLine}{result.Error}";
                        _appendNativeLog($"[Warn] fh_loader super read {mode} failed");
                    }
                }
            }
            finally
            {
                try
                {
                    ReopenFirehoseAfterExternalTool();
                }
                catch (Exception ex)
                {
                    _appendNativeLog("[Warn] reopen Firehose after super read failed: " + ex.Message);
                    DeleteIfExists(legacyPortTracePath);
                    throw new EdlReadFailureException(
                        "super 回读后无法恢复 Firehose 会话：" + ex.Message,
                        true,
                        ex);
                }

                DeleteIfExists(legacyPortTracePath);
            }

            try
            {
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
            }
            catch
            {
            }
            throw new Exception("fh_loader super read failed: " + lastError);
        }

        private static void DeleteIfExists(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static bool LooksLikeSuperFirstChunk(byte[] data)
        {
            static bool HasAscii(byte[] buffer, int offset, byte a, byte b, byte c, byte d)
            {
                return offset >= 0
                    && offset + 4 <= buffer.Length
                    && buffer[offset] == a
                    && buffer[offset + 1] == b
                    && buffer[offset + 2] == c
                    && buffer[offset + 3] == d;
            }

            bool hasGeometry = HasAscii(data, 4096, (byte)'g', (byte)'D', (byte)'l', (byte)'a')
                || HasAscii(data, 8192, (byte)'g', (byte)'D', (byte)'l', (byte)'a');
            bool hasHeader = HasAscii(data, 12288, (byte)'0', (byte)'P', (byte)'L', (byte)'A');
            return hasGeometry && hasHeader;
        }

        private sealed class RawLpPartitionEntry
        {
            public required byte[] Data { get; init; }
            public required string Name { get; init; }
            public required uint FirstExtentIndex { get; init; }
            public required uint NumExtents { get; init; }
            public required int OriginalIndex { get; init; }
        }

        private static bool TryNormalizeOplusSuperReadbackMetadata(string imagePath, out string? reason)
        {
            reason = null;
            try
            {
                if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                {
                    reason = "image file missing";
                    return false;
                }

                using var fs = new FileStream(imagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                if (fs.Length < 1024 * 1024)
                {
                    reason = "image too small";
                    return false;
                }

                byte[] head = new byte[1024 * 1024];
                int read = 0;
                while (read < head.Length)
                {
                    int n = fs.Read(head, read, head.Length - read);
                    if (n <= 0)
                        break;
                    read += n;
                }

                if (read < head.Length)
                {
                    reason = "cannot read metadata head";
                    return false;
                }

                if (!TryBuildNormalizedOplusSuperMetadataArea(head, out byte[]? normalized, out reason))
                    return false;

                if (normalized == null || normalized.Length == 0)
                {
                    reason = "no normalized metadata";
                    return false;
                }

                if (head.AsSpan(0, normalized.Length).SequenceEqual(normalized))
                {
                    reason = "metadata already normalized";
                    return false;
                }

                fs.Position = 0;
                fs.Write(normalized, 0, normalized.Length);
                fs.Flush(true);
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private static bool TryBuildNormalizedOplusSuperMetadataArea(
            byte[] head,
            out byte[]? normalized,
            out string? reason)
        {
            normalized = null;
            reason = null;

            const int reservedBytes = 4096;
            const int geometrySize = 4096;
            const int geometryOffset = 4096;
            const int backupGeometryOffset = 8192;
            const int metadataOffset = 12288;
            const int targetSlotCount = 2;
            const int targetHeaderSize = 128;
            const int lpSectorSize = 512;
            const uint geometryMagic = 0x616c4467;
            const uint headerMagic = 0x414c5030;

            if (head.Length < 1024 * 1024)
            {
                reason = "metadata head too small";
                return false;
            }

            if (ReadU32(head, geometryOffset) != geometryMagic ||
                ReadU32(head, backupGeometryOffset) != geometryMagic ||
                ReadU32(head, metadataOffset) != headerMagic)
            {
                reason = "LP metadata magic not found";
                return false;
            }

            uint geometryStructSize = ReadU32(head, geometryOffset + 4);
            uint metadataMaxSize = ReadU32(head, geometryOffset + 40);
            uint metadataSlotCount = ReadU32(head, geometryOffset + 44);
            uint logicalBlockSize = ReadU32(head, geometryOffset + 48);
            ushort majorVersion = ReadU16(head, metadataOffset + 4);
            ushort minorVersion = ReadU16(head, metadataOffset + 6);
            uint sourceHeaderSize = ReadU32(head, metadataOffset + 8);
            uint tablesSize = ReadU32(head, metadataOffset + 44);

            if (geometryStructSize < 52 ||
                geometryStructSize > geometrySize ||
                metadataMaxSize < targetHeaderSize ||
                metadataMaxSize > 1024 * 1024 ||
                metadataSlotCount <= targetSlotCount ||
                majorVersion != 10 ||
                minorVersion < 2 ||
                sourceHeaderSize < targetHeaderSize ||
                sourceHeaderSize > metadataMaxSize ||
                tablesSize == 0 ||
                tablesSize > metadataMaxSize - targetHeaderSize)
            {
                reason = "not an OPLUS expanded LP metadata layout";
                return false;
            }

            uint partOff = ReadU32(head, metadataOffset + 80);
            uint partNum = ReadU32(head, metadataOffset + 84);
            uint partEntrySize = ReadU32(head, metadataOffset + 88);
            uint extOff = ReadU32(head, metadataOffset + 92);
            uint extNum = ReadU32(head, metadataOffset + 96);
            uint extEntrySize = ReadU32(head, metadataOffset + 100);
            uint grpOff = ReadU32(head, metadataOffset + 104);
            uint grpNum = ReadU32(head, metadataOffset + 108);
            uint grpEntrySize = ReadU32(head, metadataOffset + 112);
            uint bdOff = ReadU32(head, metadataOffset + 116);
            uint bdNum = ReadU32(head, metadataOffset + 120);
            uint bdEntrySize = ReadU32(head, metadataOffset + 124);

            if (partEntrySize != 52 ||
                extEntrySize != 24 ||
                grpEntrySize != 48 ||
                bdEntrySize != 64 ||
                partNum == 0 ||
                bdNum == 0)
            {
                reason = "unsupported LP table descriptors";
                return false;
            }

            int sourceTablesBase = checked(metadataOffset + (int)sourceHeaderSize);
            if (sourceTablesBase + tablesSize > head.Length)
            {
                reason = "LP tables exceed read buffer";
                return false;
            }

            byte[] tables = new byte[tablesSize];
            Buffer.BlockCopy(head, sourceTablesBase, tables, 0, checked((int)tablesSize));

            if (partOff + checked(partNum * partEntrySize) > tablesSize ||
                extOff + checked(extNum * extEntrySize) > tablesSize ||
                grpOff + checked(grpNum * grpEntrySize) > tablesSize ||
                bdOff + checked(bdNum * bdEntrySize) > tablesSize)
            {
                reason = "LP table range invalid";
                return false;
            }

            var active = new List<RawLpPartitionEntry>();
            var inactive = new List<RawLpPartitionEntry>();
            for (int i = 0; i < partNum; i++)
            {
                int entryOffset = checked((int)partOff + i * (int)partEntrySize);
                byte[] entry = new byte[partEntrySize];
                Buffer.BlockCopy(tables, entryOffset, entry, 0, entry.Length);
                string name = ReadFixedAscii(entry, 0, 36);
                uint firstExtentIndex = ReadU32(entry, 40);
                uint numExtents = ReadU32(entry, 44);
                var parsed = new RawLpPartitionEntry
                {
                    Data = entry,
                    Name = name,
                    FirstExtentIndex = firstExtentIndex,
                    NumExtents = numExtents,
                    OriginalIndex = i
                };

                if (numExtents > 0)
                    active.Add(parsed);
                else
                    inactive.Add(parsed);
            }

            if (active.Count == 0 ||
                inactive.Count == 0 ||
                active.Count + inactive.Count != partNum ||
                !active.Any(entry => HasSlotSuffix(entry.Name)))
            {
                reason = "LP partition table does not look like an OPLUS slot-expanded table";
                return false;
            }

            active = active
                .OrderBy(entry => entry.FirstExtentIndex)
                .ThenBy(entry => entry.OriginalIndex)
                .ToList();
            inactive = inactive
                .OrderBy(entry => entry.OriginalIndex)
                .ToList();

            byte[] normalizedPartitions = new byte[checked((int)(partNum * partEntrySize))];
            int partitionWriteOffset = 0;
            foreach (var entry in active)
            {
                Buffer.BlockCopy(entry.Data, 0, normalizedPartitions, partitionWriteOffset, entry.Data.Length);
                partitionWriteOffset += entry.Data.Length;
            }
            foreach (var entry in inactive)
            {
                byte[] data = (byte[])entry.Data.Clone();
                WriteU32(data, 40, extNum);
                WriteU32(data, 44, 0);
                Buffer.BlockCopy(data, 0, normalizedPartitions, partitionWriteOffset, data.Length);
                partitionWriteOffset += data.Length;
            }

            Buffer.BlockCopy(
                normalizedPartitions,
                0,
                tables,
                checked((int)partOff),
                normalizedPartitions.Length);

            int blockDeviceOffset = checked((int)bdOff);
            ulong firstLogicalSector = ReadU64(tables, blockDeviceOffset);
            long firstPayloadOffset = checked((long)firstLogicalSector * lpSectorSize);
            long metadataTotalSize = checked(metadataOffset + targetSlotCount * 2L * metadataMaxSize);
            if (firstPayloadOffset < metadataTotalSize || firstPayloadOffset > 16L * 1024 * 1024)
                firstPayloadOffset = 1024 * 1024;
            if (firstPayloadOffset < metadataTotalSize || firstPayloadOffset > int.MaxValue)
            {
                reason = "invalid LP payload offset";
                return false;
            }

            byte[] geometry = BuildLpGeometry(
                geometryMagic,
                geometryStructSize,
                metadataMaxSize,
                targetSlotCount,
                logicalBlockSize);
            byte[] metadata = BuildLpMetadata(
                headerMagic,
                majorVersion,
                0,
                targetHeaderSize,
                tables,
                partOff,
                partNum,
                partEntrySize,
                extOff,
                extNum,
                extEntrySize,
                grpOff,
                grpNum,
                grpEntrySize,
                bdOff,
                bdNum,
                bdEntrySize);

            if (metadata.Length > metadataMaxSize)
            {
                reason = "normalized LP metadata exceeds max size";
                return false;
            }

            byte[] paddedMetadata = new byte[metadataMaxSize];
            Buffer.BlockCopy(metadata, 0, paddedMetadata, 0, metadata.Length);

            normalized = new byte[(int)firstPayloadOffset];
            Buffer.BlockCopy(geometry, 0, normalized, reservedBytes, geometry.Length);
            Buffer.BlockCopy(geometry, 0, normalized, reservedBytes + geometrySize, geometry.Length);

            int writeOffset = metadataOffset;
            for (int i = 0; i < targetSlotCount; i++)
            {
                Buffer.BlockCopy(paddedMetadata, 0, normalized, writeOffset, paddedMetadata.Length);
                writeOffset += paddedMetadata.Length;
            }
            for (int i = 0; i < targetSlotCount; i++)
            {
                Buffer.BlockCopy(paddedMetadata, 0, normalized, writeOffset, paddedMetadata.Length);
                writeOffset += paddedMetadata.Length;
            }

            return true;
        }

        private static bool HasSlotSuffix(string name)
        {
            return name.EndsWith("_a", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith("_b", StringComparison.OrdinalIgnoreCase);
        }

        private static byte[] BuildLpGeometry(
            uint magic,
            uint structSize,
            uint metadataMaxSize,
            uint metadataSlotCount,
            uint logicalBlockSize)
        {
            byte[] geometry = new byte[4096];
            WriteU32(geometry, 0, magic);
            WriteU32(geometry, 4, structSize);
            WriteU32(geometry, 40, metadataMaxSize);
            WriteU32(geometry, 44, metadataSlotCount);
            WriteU32(geometry, 48, logicalBlockSize);
            byte[] checksum = SHA256.HashData(geometry.AsSpan(0, checked((int)structSize)));
            checksum.CopyTo(geometry.AsSpan(8, 32));
            return geometry;
        }

        private static byte[] BuildLpMetadata(
            uint magic,
            ushort majorVersion,
            ushort minorVersion,
            int headerSize,
            byte[] tables,
            uint partOff,
            uint partNum,
            uint partEntrySize,
            uint extOff,
            uint extNum,
            uint extEntrySize,
            uint grpOff,
            uint grpNum,
            uint grpEntrySize,
            uint bdOff,
            uint bdNum,
            uint bdEntrySize)
        {
            byte[] header = new byte[headerSize];
            WriteU32(header, 0, magic);
            WriteU16(header, 4, majorVersion);
            WriteU16(header, 6, minorVersion);
            WriteU32(header, 8, (uint)headerSize);
            WriteU32(header, 44, (uint)tables.Length);
            SHA256.HashData(tables).CopyTo(header.AsSpan(48, 32));
            WriteTableDescriptor(header, 80, partOff, partNum, partEntrySize);
            WriteTableDescriptor(header, 92, extOff, extNum, extEntrySize);
            WriteTableDescriptor(header, 104, grpOff, grpNum, grpEntrySize);
            WriteTableDescriptor(header, 116, bdOff, bdNum, bdEntrySize);
            SHA256.HashData(header.AsSpan(0, headerSize)).CopyTo(header.AsSpan(12, 32));

            byte[] metadata = new byte[header.Length + tables.Length];
            Buffer.BlockCopy(header, 0, metadata, 0, header.Length);
            Buffer.BlockCopy(tables, 0, metadata, header.Length, tables.Length);
            return metadata;
        }

        private static void WriteTableDescriptor(byte[] data, int offset, uint tableOffset, uint numEntries, uint entrySize)
        {
            WriteU32(data, offset, tableOffset);
            WriteU32(data, offset + 4, numEntries);
            WriteU32(data, offset + 8, entrySize);
        }

        private static string ReadFixedAscii(byte[] data, int offset, int length)
        {
            int end = offset;
            int max = Math.Min(data.Length, offset + length);
            while (end < max && data[end] != 0)
                end++;
            return Encoding.ASCII.GetString(data, offset, end - offset);
        }

        private static ushort ReadU16(byte[] data, int offset)
        {
            return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
        }

        private static uint ReadU32(byte[] data, int offset)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
        }

        private static ulong ReadU64(byte[] data, int offset)
        {
            return BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset, 8));
        }

        private static void WriteU16(byte[] data, int offset, ushort value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), value);
        }

        private static void WriteU32(byte[] data, int offset, uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
        }

        private byte[]? ReadSectorsForReadbackPartition(int lun, long startSector, int numSectors, string? partitionName)
        {
            var strategies = new List<VipSpoofStrategy>();

            bool isSuperRead = !string.IsNullOrWhiteSpace(partitionName)
                && (partitionName.Equals("super", StringComparison.OrdinalIgnoreCase)
                    || partitionName.StartsWith("super_", StringComparison.OrdinalIgnoreCase));

            if (isSuperRead)
            {
                string safeName = SanitizePartitionName(partitionName!);
                strategies.Add(new VipSpoofStrategy("", safeName, 0));
                foreach (var strategy in GetDynamicSpoofStrategies(lun, startSector, partitionName, false))
                {
                    bool exists = strategies.Any(item =>
                        item.Label.Equals(strategy.Label, StringComparison.OrdinalIgnoreCase)
                        && item.Filename.Equals(strategy.Filename, StringComparison.OrdinalIgnoreCase));
                    if (!exists)
                        strategies.Add(strategy);
                }
                return ReadSectorsWithStrategies(lun, startSector, numSectors, strategies);
            }

            if (!string.IsNullOrWhiteSpace(partitionName))
            {
                string safeName = SanitizePartitionName(partitionName);
                strategies.Add(new VipSpoofStrategy($"{lun}_{safeName}.img", safeName, 0));
                strategies.Add(new VipSpoofStrategy(safeName + ".img", safeName, 1));
            }

            foreach (var strategy in GetDynamicSpoofStrategies(lun, startSector, partitionName, startSector <= 33))
            {
                bool exists = strategies.Any(item =>
                    item.Label.Equals(strategy.Label, StringComparison.OrdinalIgnoreCase)
                    && item.Filename.Equals(strategy.Filename, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                    strategies.Add(strategy);
            }

            if (!strategies.Any(item => string.IsNullOrEmpty(item.Label)))
                strategies.Add(new VipSpoofStrategy("", "", 99));

            return ReadSectorsWithStrategies(lun, startSector, numSectors, strategies);
        }

        private static string GenerateReadbackRawprogramXml(IEnumerable<EdlPartitionInfo> partitions)
        {
            string xml = ProgramFlasher.GenerateRawprogram(partitions.ToList());
            try
            {
                XDocument doc = XDocument.Parse(xml);
                foreach (XElement program in doc.Descendants("program"))
                {
                    string sectorSizeText = program.Attribute("SECTOR_SIZE_IN_BYTES")?.Value ?? "4096";
                    string startSectorText = program.Attribute("start_sector")?.Value ?? "0";
                    if (!TryParseLongAllowHex(sectorSizeText, out long sectorSize) || sectorSize <= 0)
                        sectorSize = 4096;
                    if (!TryParseLongAllowHex(startSectorText, out long startSector) || startSector < 0)
                        continue;

                    long startByte = checked(startSector * sectorSize);
                    program.SetAttributeValue(
                        "start_byte_hex",
                        "0x" + startByte.ToString("x", CultureInfo.InvariantCulture));
                }

                using var writer = new StringWriter(CultureInfo.InvariantCulture);
                doc.Save(writer);
                return writer.ToString();
            }
            catch
            {
                return xml;
            }
        }

        public void DoReadPartitions(string readPath, IList<EdlPartitionInfo> selected, bool generateProgram)
        {
            Debug.Assert(FirehoseServer != null);
            if (selected.Count == 0)
                throw new Exception("请先选择要读取的分区");
            Dictionary<int, List<EdlPartitionInfo>> partitions = new Dictionary<int, List<EdlPartitionInfo>>();
            int successCount = 0;
            for (int i = 0; i < selected.Count; i++)
            {
                ThrowIfStopRequested();
                EdlPartitionInfo partition = selected[i];
                _updateCurrentPartitionText(partition.Label);
                _updateProgress(0);
                _log($"__EDL_FLASH_BEGIN__|[回读]{partition.Label}...", null);
                long lastBytes = 0;
                long lastTick = Stopwatch.GetTimestamp();
                long lastSpeedUiTick = 0;
                EventHandler<(long, long)> progressHandler = (sender, e) =>
                {
                    long current = e.Item1;
                    long total = e.Item2;
                    long now = Stopwatch.GetTimestamp();
                    long dtTicks = now - lastTick;
                    if (dtTicks > 0)
                    {
                        double dt = dtTicks / (double)Stopwatch.Frequency;
                        long delta = current - lastBytes;
                        if (delta >= 0 && now - lastSpeedUiTick >= Stopwatch.Frequency / 5)
                        {
                            double speed = delta / dt;
                            lastSpeedUiTick = now;
                            UpdateSmoothedTransferSpeed(speed);
                        }
                    }
                    lastBytes = current;
                    lastTick = now;

                    double partPercent = total <= 0 ? 0 : Math.Clamp(current * 100.0 / total, 0, 100);
                    _updateProgress(partPercent);
                };
                FirehoseServer.ProgressChanged += progressHandler;
                if (partition.Label == "PrimaryGPT")
                    partition.FilePath = Path.Combine(readPath, $"gpt_main{partition.Lun}.bin");
                else if (partition.Label == "BackupGPT")
                    partition.FilePath = Path.Combine(readPath, $"gpt_backup{partition.Lun}.bin");
                else
                    partition.FilePath = Path.Combine(readPath, partition.Label + ".img");
                bool readSucceeded = false;
                try
                {
                    if (IsEdlSuperPartition(partition))
                    {
                        _appendNativeLog(
                            $"[Readback] {partition.Label} uses fh_loader convertprogram2read mode.");
                        ReadSuperPartitionWithFhLoader(partition);
                    }
                    else
                    {
                        ReadPartitionWithSafeLabel(partition);
                    }

                    readSucceeded = true;
                    successCount++;
                    _log("__EDL_FLASH_OK__", null);
                }
                catch (Exception ex)
                {
                    bool sessionBreaking = IsSessionBreakingFailure(ex);
                    _appendNativeLog(
                        $"[Readback] {partition.Label} LUN{partition.Lun} failed.{Environment.NewLine}{ex}");
                    _log("__EDL_FLASH_ERROR__", null);
                    _log($"{partition.Label} 回读失败: {BuildShortEdlErrorMessage(ex)}", "error");
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(partition.FilePath) && File.Exists(partition.FilePath))
                            File.Delete(partition.FilePath);
                    }
                    catch
                    {
                    }
                    if (sessionBreaking)
                        throw;
                }
                finally
                {
                    FirehoseServer.ProgressChanged -= progressHandler;
                }
                ThrowIfStopRequested();
                if (readSucceeded)
                {
                    if (partitions.ContainsKey(partition.Lun))
                        partitions[partition.Lun].Add(partition);
                    else
                        partitions[partition.Lun] = new List<EdlPartitionInfo> { partition };
                    _updateProgress(100);
                }
            }
            _updateSpeedText(null);
            _updateCurrentPartitionText(null);
            if (successCount == 0)
                throw new Exception("所选分区均回读失败");
            if (generateProgram)
            {
                ThrowIfStopRequested();
                _log("选中分区读取完成，开始生成XML.", null);
                foreach (var item in partitions)
                {
                    string rawprogramName = $"rawprogram{item.Key}.xml";
                    _log($"__EDL_FLASH_BEGIN__|生成rawprogram{item.Key}...", null);
                    string rawprogramPath = Path.Combine(readPath, rawprogramName);
                    File.WriteAllText(rawprogramPath, GenerateReadbackRawprogramXml(item.Value));
                    _log("__EDL_FLASH_OK__", null);
                }
                _log("生成XML完成.", null);
            }
            _log("[Done]资料保存路径:" + readPath, null);
        }

        public void DoBackupBasebandFingerprintPartitions(string readPath, IList<EdlPartitionInfo> availablePartitions)
        {
            Debug.Assert(FirehoseServer != null);
            if (string.IsNullOrWhiteSpace(readPath))
                throw new Exception("无效的基带指纹备份路径");
            if (availablePartitions == null || availablePartitions.Count == 0)
                throw new Exception("未读取到分区表");

            Directory.CreateDirectory(readPath);
            var selected = SelectBasebandFingerprintBackupPartitions(availablePartitions);
            if (selected.Count == 0)
                throw new Exception("未找到可备份的基带指纹分区");

            string[] requiredLabels = { "persist", "modemst1", "modemst2", "fsg", "fsc" };
            var normalizedLabels = new HashSet<string>(
                availablePartitions
                    .Where(p => !string.IsNullOrWhiteSpace(p.Label))
                    .Select(p => MainWindow.StripEdlSlotSuffix(p.Label.Trim())),
                StringComparer.OrdinalIgnoreCase);
            var missingRequired = requiredLabels
                .Where(label => !normalizedLabels.Contains(label))
                .ToList();
            if (missingRequired.Count > 0)
                _log("未找到分区: " + string.Join(", ", missingRequired), "warn");

            var successPartitions = new Dictionary<int, List<EdlPartitionInfo>>();
            int successCount = 0;
            int failedCount = 0;
            foreach (EdlPartitionInfo partition in selected)
            {
                ThrowIfStopRequested();
                _updateCurrentPartitionText(partition.Label);
                _updateProgress(0);
                _log($"__EDL_FLASH_BEGIN__|[回读]{partition.Label}...", null);
                long lastBytes = 0;
                long lastTick = Stopwatch.GetTimestamp();
                long lastSpeedUiTick = 0;
                EventHandler<(long, long)> progressHandler = (sender, e) =>
                {
                    long current = e.Item1;
                    long total = e.Item2;
                    long now = Stopwatch.GetTimestamp();
                    long dtTicks = now - lastTick;
                    if (dtTicks > 0)
                    {
                        double dt = dtTicks / (double)Stopwatch.Frequency;
                        long delta = current - lastBytes;
                        if (delta >= 0 && now - lastSpeedUiTick >= Stopwatch.Frequency / 5)
                        {
                            double speed = delta / dt;
                            lastSpeedUiTick = now;
                            UpdateSmoothedTransferSpeed(speed);
                        }
                    }
                    lastBytes = current;
                    lastTick = now;

                    double partPercent = total <= 0 ? 0 : Math.Clamp(current * 100.0 / total, 0, 100);
                    _updateProgress(partPercent);
                };

                FirehoseServer.ProgressChanged += progressHandler;
                partition.FilePath = Path.Combine(readPath, ToSafeBackupFileName(partition.Label) + ".img");
                try
                {
                    ReadPartitionWithSafeLabel(partition);
                    if (!successPartitions.TryGetValue(partition.Lun, out var list))
                    {
                        list = new List<EdlPartitionInfo>();
                        successPartitions[partition.Lun] = list;
                    }
                    list.Add(partition);
                    successCount++;
                    _updateProgress(100);
                    _log("__EDL_FLASH_OK__", null);
                }
                catch (Exception ex)
                {
                    failedCount++;
                    _log("__EDL_FLASH_ERROR__", null);
                    _log($"{partition.Label} 备份失败: {ex.Message}", "error");
                    if (IsSessionBreakingFailure(ex))
                        throw;
                }
                finally
                {
                    FirehoseServer.ProgressChanged -= progressHandler;
                }
                ThrowIfStopRequested();
            }

            _updateSpeedText(null);
            _updateCurrentPartitionText(null);

            if (successCount == 0)
                throw new Exception("未能备份任何基带指纹分区");

            foreach (var item in successPartitions.OrderBy(item => item.Key))
            {
                string rawprogramName = $"rawprogram{item.Key}.xml";
                _log($"__EDL_FLASH_BEGIN__|生成rawprogram{item.Key}...", null);
                string rawprogramPath = Path.Combine(readPath, rawprogramName);
                File.WriteAllText(rawprogramPath, GenerateReadbackRawprogramXml(item.Value), Encoding.UTF8);
                _log("__EDL_FLASH_OK__", null);
            }
            if (failedCount > 0)
                _log($"基带指纹备份流程结束，失败 {failedCount} 个分区，已跳过并继续。", "error");
            _log($"基带指纹备份完成: {successCount} 个分区", null);
            _log("[Done]资料保存路径:" + readPath, null);
        }

        private static List<EdlPartitionInfo> SelectBasebandFingerprintBackupPartitions(IEnumerable<EdlPartitionInfo> partitions)
        {
            var result = new List<EdlPartitionInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (EdlPartitionInfo partition in partitions)
            {
                if (!MainWindow.IsEdlBasebandFingerprintBackupPartitionLabel(partition.Label))
                    continue;
                string key = partition.Lun.ToString(CultureInfo.InvariantCulture) + ":" + partition.Label;
                if (seen.Add(key))
                    result.Add(partition);
            }
            return result;
        }

        private static string ToSafeBackupFileName(string? label)
        {
            string name = string.IsNullOrWhiteSpace(label) ? "partition" : label.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');
            return name;
        }

        public void DoForceEnableOemUnlock(EdlPartitionInfo frpPartition)
        {
            Debug.Assert(FirehoseServer != null);
            if (frpPartition == null)
                throw new Exception("未找到 frp 分区");

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string bakDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bak");
            string tmpDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp", "edl_oem_unlock_" + timestamp);
            Directory.CreateDirectory(bakDir);
            Directory.CreateDirectory(tmpDir);

            string backupPath = Path.Combine(bakDir, "frp_oem_backup_" + timestamp + ".img");
            string patchedPath = Path.Combine(tmpDir, "frp_patched.img");
            var frp = ClonePartitionForEdlOperation(frpPartition);

            _updateCurrentPartitionText("frp");
            _updateProgress(0);
            frp.FilePath = backupPath;
            _log("__EDL_FLASH_BEGIN__|回读frp...", null);
            EventHandler<(long, long)> readProgressHandler = (sender, e) =>
            {
                long current = e.Item1;
                long total = e.Item2;
                double percent = total <= 0 ? 0 : Math.Clamp(current * 100.0 / total, 0, 100);
                _updateProgress(percent);
            };
            FirehoseServer.ProgressChanged += readProgressHandler;
            try
            {
                ReadPartitionWithSafeLabel(frp);
                _updateProgress(100);
                _log("__EDL_FLASH_OK__", null);
            }
            catch
            {
                _log("__EDL_FLASH_ERROR__", null);
                throw;
            }
            finally
            {
                FirehoseServer.ProgressChanged -= readProgressHandler;
            }

            _updateProgress(0);
            _log("__EDL_FLASH_BEGIN__|修补frp...", null);
            try
            {
                PatchFrpOemUnlockFlag(backupPath, patchedPath);
                _updateProgress(100);
                _log("__EDL_FLASH_OK__", null);
            }
            catch
            {
                _log("__EDL_FLASH_ERROR__", null);
                throw;
            }

            _updateProgress(0);
            frp.FilePath = patchedPath;
            frp.Sparse = false;
            _log("__EDL_FLASH_BEGIN__|刷入frp...", null);
            EventHandler<(long, long)> writeProgressHandler = (sender, e) =>
            {
                long current = e.Item1;
                long total = e.Item2;
                double percent = total <= 0 ? 0 : Math.Clamp(current * 100.0 / total, 0, 100);
                _updateProgress(percent);
            };
            FirehoseServer.ProgressChanged += writeProgressHandler;
            try
            {
                try
                {
                    TraceSharpEdlProgramIntent(frp, patchedPath, false);
                    FirehoseServer.WriteUnsparseImage(frp).CheckAndThrow();
                }
                catch (Exception ex) when (IsEdlPermissionNak(ex))
                {
                    _appendNativeLog(
                        $"[ForceOEM] direct frp write was denied; retrying with a safe label.{Environment.NewLine}{ex}");
                    ConfigureFirehoseWithEdlStyle(8000);
                    WriteRawImageViaProgram(
                        patchedPath,
                        frp,
                        0,
                        new FileInfo(patchedPath).Length,
                        new FileInfo(patchedPath).Length);
                }

                _updateProgress(100);
                _log("__EDL_FLASH_OK__", null);
            }
            catch (Exception ex)
            {
                _appendNativeLog($"[ForceOEM] frp write failed.{Environment.NewLine}{ex}");
                _log("__EDL_FLASH_ERROR__", null);
                throw new Exception("frp 写入被设备拒绝，请确认当前引导具备写入权限");
            }
            finally
            {
                FirehoseServer.ProgressChanged -= writeProgressHandler;
                _updateSpeedText(null);
                _updateCurrentPartitionText(null);
                try { Directory.Delete(tmpDir, true); } catch { }
            }

            _log("强开OEM完成", null);
        }

        private static void PatchFrpOemUnlockFlag(string sourcePath, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new Exception("未找到 frp 备份文件");

            File.Copy(sourcePath, outputPath, true);
            using var fs = new FileStream(outputPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            if (fs.Length <= 0)
                throw new Exception("frp 文件为空");

            fs.Seek(-1, SeekOrigin.End);
            int original = fs.ReadByte();
            if (original != 0x00 && original != 0x01)
                throw new Exception("frp 文件末尾1字节16进制数值不是00或01");

            if (original == 0x01)
                return;

            fs.Seek(-1, SeekOrigin.End);
            fs.WriteByte(0x01);
            fs.Flush(true);
        }

        private static EdlPartitionInfo ClonePartitionForEdlOperation(EdlPartitionInfo partition)
        {
            return new EdlPartitionInfo
            {
                Label = partition.Label,
                Lun = partition.Lun,
                StartSector = partition.StartSector,
                SectorLen = partition.SectorLen,
                BytesPerSector = partition.BytesPerSector,
                Sparse = partition.Sparse,
                FilePath = partition.FilePath
            };
        }

        public void DoBackupGptMainFiles(string readPath, IList<int> luns)
        {
            Debug.Assert(FirehoseServer != null);
            if (string.IsNullOrWhiteSpace(readPath))
                throw new Exception("无效的GPT备份路径");

            Directory.CreateDirectory(readPath);
            int sectorSize = _firehoseSectorSize > 0 ? _firehoseSectorSize : 4096;
            var targetLuns = (luns ?? Array.Empty<int>())
                .Distinct()
                .OrderBy(lun => lun)
                .ToList();
            if (targetLuns.Count == 0)
                throw new Exception("未选择要备份的LUN");

            int successCount = 0;
            int failedCount = 0;
            foreach (int lun in targetLuns)
            {
                ThrowIfStopRequested();
                _updateCurrentPartitionText($"LUN{lun} GPT");
                _updateProgress(0);
                _log($"__EDL_FLASH_BEGIN__|备份LUN{lun} GPT...", null);
                try
                {
                    byte[]? gptData = ReadPrimaryGptPrefix(lun);
                    if (gptData == null || gptData.Length < 512)
                        throw CreateLastReadFailureException($"备份 LUN{lun} GPT");

                    if (!TryExtractPrimaryGptImage(gptData, sectorSize, out byte[] gptImage, out int detectedSectorSize, out int gptSectors, out string error))
                        throw new Exception(error);

                    string fileName = $"gpt_main{lun}.bin";
                    string imagePath = Path.Combine(readPath, fileName);
                    File.WriteAllBytes(imagePath, gptImage);

                    string rawprogramPath = Path.Combine(readPath, $"rawprogram{lun}.xml");
                    File.WriteAllText(
                        rawprogramPath,
                        BuildGptMainRawprogramXml(fileName, lun, detectedSectorSize, gptSectors),
                        Encoding.UTF8);

                    successCount++;
                    _updateProgress(100);
                    _log("__EDL_FLASH_OK__", null);
                }
                catch (Exception ex)
                {
                    failedCount++;
                    _log("__EDL_FLASH_ERROR__", null);
                    _log($"LUN{lun} GPT备份失败: {ex.Message}", "error");
                    if (IsSessionBreakingFailure(ex))
                        throw;
                }
                ThrowIfStopRequested();
            }

            _updateSpeedText(null);
            _updateCurrentPartitionText(null);
            if (failedCount > 0)
                _log($"备份GPT流程结束，失败 {failedCount} 个LUN，已跳过并继续。", "error");
            if (successCount == 0)
                throw new Exception("未能备份任何GPT");
            _log($"备份GPT完成: {successCount} 个LUN", null);
            _log("[Done]资料保存路径:" + readPath, null);
        }

        private sealed class EdlGptSlotImage
        {
            public int Lun { get; init; }
            public byte[] Image { get; init; } = Array.Empty<byte>();
            public int SectorSize { get; init; }
            public int SectorCount { get; init; }
            public string? DetectedSlot { get; init; }
            public bool HasSlotPairs { get; init; }
        }

        private readonly struct GptSlotPair
        {
            public int AAttributeOffset { get; }
            public int BAttributeOffset { get; }

            public GptSlotPair(int aAttributeOffset, int bAttributeOffset)
            {
                AAttributeOffset = aAttributeOffset;
                BAttributeOffset = bAttributeOffset;
            }

            public GptSlotPair WithA(int offset) => new GptSlotPair(offset, BAttributeOffset);

            public GptSlotPair WithB(int offset) => new GptSlotPair(AAttributeOffset, offset);

            public bool IsComplete => AAttributeOffset >= 0 && BAttributeOffset >= 0;
        }

        public string ReadCurrentSlot()
        {
            Debug.Assert(FirehoseServer != null);
            ReadSlotGptImages(out string currentSlot);
            _log($"当前槽位: {currentSlot.ToUpperInvariant()}", null);
            _updateProgress(100);
            _updateSpeedText(null);
            _updateCurrentPartitionText(null);
            return currentSlot;
        }

        private List<EdlGptSlotImage> ReadSlotGptImages(out string currentSlot)
        {
            var images = new List<EdlGptSlotImage>();
            var detectedSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _log("开始读取设备当前槽位...", null);
            currentSlot = "";
            IReadOnlyList<int> targetLuns = GetCandidatePhysicalLuns();
            bool controlledUfsDiscovery = _knownPhysicalLuns.Length == 0
                && !IsEmmcStorage();

            for (int index = 0; index < targetLuns.Count; index++)
            {
                ThrowIfStopRequested();
                int lun = targetLuns[index];
                _updateCurrentPartitionText($"LUN{lun} GPT");
                _updateProgress(index * 100.0 / Math.Max(1, targetLuns.Count));
                try
                {
                    byte[]? gptData = ReadPrimaryGptPrefix(lun);
                    if (gptData == null || gptData.Length < 512)
                        throw CreateLastReadFailureException($"读取 LUN{lun} GPT 槽位信息");

                    int defaultSectorSize = _firehoseSectorSize > 0 ? _firehoseSectorSize : 4096;
                    if (!TryExtractPrimaryGptImage(
                            gptData,
                            defaultSectorSize,
                            out byte[] gptImage,
                            out int detectedSectorSize,
                            out int gptSectors,
                            out string extractError))
                    {
                        throw new Exception(extractError);
                    }

                    if (!ValidatePrimaryGptCrc(gptImage, detectedSectorSize, out string crcError))
                        throw new Exception(crcError);

                    string? detectedSlot = DetectSlotFromPrimaryGpt(
                        gptImage,
                        detectedSectorSize,
                        out bool hasSlotPairs,
                        out bool slotConflict);
                    if (slotConflict)
                        throw new Exception("GPT 中的 A/B 槽位标记互相冲突");
                    if (!string.IsNullOrEmpty(detectedSlot))
                        detectedSlots.Add(detectedSlot);

                    images.Add(new EdlGptSlotImage
                    {
                        Lun = lun,
                        Image = gptImage,
                        SectorSize = detectedSectorSize,
                        SectorCount = gptSectors,
                        DetectedSlot = detectedSlot,
                        HasSlotPairs = hasSlotPairs
                    });
                    _log($"读取 LUN{lun} GPT...OK", null);
                }
                catch (Exception ex)
                {
                    string failureReason = string.IsNullOrWhiteSpace(_lastReadFailureReason)
                        ? ex.Message
                        : _lastReadFailureReason!;
                    if (controlledUfsDiscovery
                        && images.Count > 0
                        && IsUnsupportedPhysicalLunReadFailure(failureReason))
                    {
                        _log($"读取 LUN{lun} GPT...跳过", null);
                        _appendNativeLog(
                            $"[Capabilities] slot discovery skipped unavailable UFS LUN{lun}: {failureReason}");
                        continue;
                    }

                    _log($"读取 LUN{lun} GPT...Failed", "error");
                    throw;
                }
            }

            ThrowIfStopRequested();
            if (images.Count > 0)
                RememberPhysicalLuns(images.Select(image => image.Lun));
            if (detectedSlots.Count == 0)
                throw new Exception("未在 GPT 中识别到 A/B 槽位信息");
            if (detectedSlots.Count > 1)
                throw new Exception("各 LUN 的当前槽位不一致，已停止切换");

            currentSlot = detectedSlots.First().ToLowerInvariant();
            return images;
        }

        public void DoSwitchSlot(string targetSlot)
        {
            Debug.Assert(FirehoseServer != null);
            string normalizedTarget = (targetSlot ?? "").Trim().ToLowerInvariant();
            if (normalizedTarget != "a" && normalizedTarget != "b")
                throw new Exception("目标槽位只能是 A 或 B");

            List<EdlGptSlotImage> images = ReadSlotGptImages(out string currentSlot);
            _log($"当前槽位: {currentSlot.ToUpperInvariant()}", null);
            if (currentSlot == normalizedTarget)
            {
                _log($"当前已经是槽位 {normalizedTarget.ToUpperInvariant()}，无需切换", null);
                _updateProgress(100);
                _updateCurrentPartitionText(null);
                return;
            }

            _log($"准备切换到槽位: {normalizedTarget.ToUpperInvariant()}", null);
            foreach (var image in images)
            {
                ThrowIfStopRequested();
                try
                {
                    if (image.HasSlotPairs)
                    {
                        int changedPairs = SwitchPrimaryGptSlot(image.Image, image.SectorSize, normalizedTarget);
                        if (changedPairs <= 0)
                            throw new Exception("未修改任何 A/B 分区条目");

                        string? verifySlot = DetectSlotFromPrimaryGpt(
                            image.Image,
                            image.SectorSize,
                            out _,
                            out bool verifyConflict);
                        if (verifyConflict || !string.Equals(verifySlot, normalizedTarget, StringComparison.OrdinalIgnoreCase))
                            throw new Exception("修改后的 GPT 槽位校验失败");
                        if (!ValidatePrimaryGptCrc(image.Image, image.SectorSize, out string crcError))
                            throw new Exception(crcError);
                    }

                    _log($"修改 LUN{image.Lun} GPT...OK", null);
                }
                catch
                {
                    _log($"修改 LUN{image.Lun} GPT...Failed", "error");
                    throw;
                }
            }

            _log("写入修改后的 GPT...", null);
            long totalBytes = 0;
            for (int i = 0; i < images.Count; i++)
            {
                ThrowIfStopRequested();
                var image = images[i];
                _updateCurrentPartitionText($"LUN{image.Lun} GPT");
                _updateProgress(i * 100.0 / images.Count);
                _log(
                    $"__EDL_FLASH_BEGIN__|{FormatEdlPartitionWriteLine($"gpt_main{image.Lun}.img", $"LUN{image.Lun}", "PrimaryGPT")}",
                    null);
                try
                {
                    if (!WritePrimaryGptImage(image.Lun, image.Image))
                        throw new Exception($"LUN{image.Lun} 主 GPT 写入失败");
                    totalBytes += image.Image.Length;
                    _log("__EDL_FLASH_OK__", null);
                }
                catch
                {
                    _log("__EDL_FLASH_ERROR__", null);
                    throw;
                }
                ThrowIfStopRequested();
            }

            _log($"写入总大小 : {FormatByteSize(totalBytes)}", null);
            string memoryName = (FirehoseServer?.MemoryName ?? "").Trim();
            bool isUfs = memoryName.Equals("ufs", StringComparison.OrdinalIgnoreCase)
                || (string.IsNullOrEmpty(memoryName) && _firehoseSectorSize >= 4096);
            int bootValue = isUfs
                ? (normalizedTarget == "a" ? 1 : 2)
                : 0;

            ThrowIfStopRequested();
            if (!TrySetBootLun(bootValue, 8000))
            {
                _log($"设置启动槽位 ({currentSlot.ToUpperInvariant()} → {normalizedTarget.ToUpperInvariant()})... Failed", "error");
                throw new Exception($"设置启动槽位 value={bootValue} 失败");
            }
            _log($"设置启动槽位 ({currentSlot.ToUpperInvariant()} → {normalizedTarget.ToUpperInvariant()})... OK", null);
            _updateProgress(100);
            _updateSpeedText(null);
            _updateCurrentPartitionText(null);
        }

        private bool WritePrimaryGptImage(int lun, byte[] image)
        {
            VipSpoofStrategy strategy = new VipSpoofStrategy(
                $"gpt_main{lun}.bin",
                "PrimaryGPT",
                0);
            bool written = WriteSectorsOnce(
                lun,
                0,
                image,
                image.Length,
                strategy,
                true,
                out string? permissionDeniedReason);
            if (!written && !string.IsNullOrWhiteSpace(permissionDeniedReason))
                _appendNativeLog($"[WriteStrategy] PrimaryGPT LUN{lun} rejected: {permissionDeniedReason}");
            return written;
        }

        private string? DetectSlotFromPrimaryGpt(
            byte[] gptImage,
            int sectorSize,
            out bool hasSlotPairs,
            out bool conflict)
        {
            hasSlotPairs = false;
            conflict = false;
            if (!TryGetPrimaryGptSlotPairs(gptImage, sectorSize, out var pairs, out _, out _, out _))
                return null;

            hasSlotPairs = pairs.Count > 0;
            if (!hasSlotPairs)
                return null;

            if (pairs.TryGetValue("boot", out GptSlotPair bootPair) && bootPair.IsComplete)
            {
                int aPriority = GetGptSlotPriority(gptImage, bootPair.AAttributeOffset);
                int bPriority = GetGptSlotPriority(gptImage, bootPair.BAttributeOffset);
                if (aPriority != bPriority)
                    return aPriority > bPriority ? "a" : "b";
            }

            int aVotes = 0;
            int bVotes = 0;
            foreach (GptSlotPair pair in pairs.Values)
            {
                if (!pair.IsComplete)
                    continue;
                int aPriority = GetGptSlotPriority(gptImage, pair.AAttributeOffset);
                int bPriority = GetGptSlotPriority(gptImage, pair.BAttributeOffset);
                if (aPriority > bPriority)
                    aVotes++;
                else if (bPriority > aPriority)
                    bVotes++;
            }

            if (aVotes > 0 && bVotes > 0)
            {
                conflict = true;
                return null;
            }
            if (aVotes > 0)
                return "a";
            if (bVotes > 0)
                return "b";
            return null;
        }

        private int SwitchPrimaryGptSlot(byte[] gptImage, int sectorSize, string targetSlot)
        {
            if (!TryGetPrimaryGptSlotPairs(
                    gptImage,
                    sectorSize,
                    out var pairs,
                    out int headerOffset,
                    out int entryArrayOffset,
                    out int entryArrayLength))
            {
                throw new Exception("GPT 分区项结构异常");
            }

            int changedPairs = 0;
            foreach (var item in pairs)
            {
                GptSlotPair pair = item.Value;
                if (!pair.IsComplete)
                    continue;

                int aEntryOffset = pair.AAttributeOffset - 48;
                int bEntryOffset = pair.BAttributeOffset - 48;
                for (int i = 0; i < 16; i++)
                {
                    byte value = gptImage[aEntryOffset + i];
                    gptImage[aEntryOffset + i] = gptImage[bEntryOffset + i];
                    gptImage[bEntryOffset + i] = value;
                }

                bool isBootPair = item.Key.Equals("boot", StringComparison.OrdinalIgnoreCase);
                ulong targetAttributes = isBootPair
                    ? 0x103F000000000000UL
                    : 0x1004000000000000UL;
                ulong otherAttributes = isBootPair
                    ? 0x103A000000000000UL
                    : 0UL;

                if (targetSlot == "a")
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(gptImage.AsSpan(pair.AAttributeOffset, 8), targetAttributes);
                    BinaryPrimitives.WriteUInt64LittleEndian(gptImage.AsSpan(pair.BAttributeOffset, 8), otherAttributes);
                }
                else
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(gptImage.AsSpan(pair.AAttributeOffset, 8), otherAttributes);
                    BinaryPrimitives.WriteUInt64LittleEndian(gptImage.AsSpan(pair.BAttributeOffset, 8), targetAttributes);
                }
                changedPairs++;
            }

            if (changedPairs > 0)
                UpdatePrimaryGptCrc(gptImage, headerOffset, entryArrayOffset, entryArrayLength);
            return changedPairs;
        }

        private bool TryGetPrimaryGptSlotPairs(
            byte[] gptImage,
            int sectorSize,
            out Dictionary<string, GptSlotPair> pairs,
            out int headerOffset,
            out int entryArrayOffset,
            out int entryArrayLength)
        {
            pairs = new Dictionary<string, GptSlotPair>(StringComparer.OrdinalIgnoreCase);
            headerOffset = FindGptHeaderOffset(gptImage);
            entryArrayOffset = 0;
            entryArrayLength = 0;
            if (headerOffset < 0 || headerOffset + 92 > gptImage.Length)
                return false;

            uint numberOfEntries = BinaryPrimitives.ReadUInt32LittleEndian(gptImage.AsSpan(headerOffset + 80, 4));
            uint entrySize = BinaryPrimitives.ReadUInt32LittleEndian(gptImage.AsSpan(headerOffset + 84, 4));
            ulong entryLba = BinaryPrimitives.ReadUInt64LittleEndian(gptImage.AsSpan(headerOffset + 72, 8));
            if (numberOfEntries == 0 || entrySize < 128 || entryLba == 0)
                return false;

            ulong entryOffset64 = entryLba * (ulong)sectorSize;
            ulong entryLength64 = (ulong)numberOfEntries * entrySize;
            if (entryOffset64 > int.MaxValue
                || entryLength64 > int.MaxValue
                || entryOffset64 + entryLength64 > (ulong)gptImage.Length)
            {
                return false;
            }

            entryArrayOffset = (int)entryOffset64;
            entryArrayLength = (int)entryLength64;
            for (uint i = 0; i < numberOfEntries; i++)
            {
                int offset = entryArrayOffset + checked((int)(i * entrySize));
                if (offset + entrySize > gptImage.Length || IsAllZero(gptImage, offset, 16))
                    continue;

                string name = Encoding.Unicode.GetString(gptImage, offset + 56, 72).TrimEnd('\0');
                if (name.Length <= 2)
                    continue;

                bool isA = name.EndsWith("_a", StringComparison.OrdinalIgnoreCase);
                bool isB = name.EndsWith("_b", StringComparison.OrdinalIgnoreCase);
                if (!isA && !isB)
                    continue;

                string baseName = name.Substring(0, name.Length - 2);
                pairs.TryGetValue(baseName, out GptSlotPair pair);
                if (!pairs.ContainsKey(baseName))
                    pair = new GptSlotPair(-1, -1);
                int attributeOffset = offset + 48;
                pairs[baseName] = isA ? pair.WithA(attributeOffset) : pair.WithB(attributeOffset);
            }

            foreach (string incomplete in pairs.Where(item => !item.Value.IsComplete).Select(item => item.Key).ToList())
                pairs.Remove(incomplete);
            return true;
        }

        private static int GetGptSlotPriority(byte[] gptImage, int attributeOffset)
        {
            ulong attributes = BinaryPrimitives.ReadUInt64LittleEndian(gptImage.AsSpan(attributeOffset, 8));
            return (int)((attributes >> 48) & 0x0F);
        }

        private bool ValidatePrimaryGptCrc(byte[] gptImage, int sectorSize, out string error)
        {
            error = "";
            if (!TryGetPrimaryGptSlotPairs(
                    gptImage,
                    sectorSize,
                    out _,
                    out int headerOffset,
                    out int entryArrayOffset,
                    out int entryArrayLength))
            {
                error = "GPT 结构不完整";
                return false;
            }

            uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(gptImage.AsSpan(headerOffset + 12, 4));
            if (headerSize < 92 || headerSize > sectorSize || headerOffset + headerSize > gptImage.Length)
            {
                error = "GPT 头大小异常";
                return false;
            }

            uint storedEntryCrc = BinaryPrimitives.ReadUInt32LittleEndian(gptImage.AsSpan(headerOffset + 88, 4));
            uint actualEntryCrc = ComputeGptCrc32(gptImage.AsSpan(entryArrayOffset, entryArrayLength));
            if (storedEntryCrc != actualEntryCrc)
            {
                error = "GPT 分区项 CRC 校验失败";
                return false;
            }

            uint storedHeaderCrc = BinaryPrimitives.ReadUInt32LittleEndian(gptImage.AsSpan(headerOffset + 16, 4));
            byte[] header = gptImage.AsSpan(headerOffset, checked((int)headerSize)).ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16, 4), 0);
            uint actualHeaderCrc = ComputeGptCrc32(header);
            if (storedHeaderCrc != actualHeaderCrc)
            {
                error = "GPT 头 CRC 校验失败";
                return false;
            }
            return true;
        }

        private static void UpdatePrimaryGptCrc(
            byte[] gptImage,
            int headerOffset,
            int entryArrayOffset,
            int entryArrayLength)
        {
            uint entryCrc = ComputeGptCrc32(gptImage.AsSpan(entryArrayOffset, entryArrayLength));
            BinaryPrimitives.WriteUInt32LittleEndian(gptImage.AsSpan(headerOffset + 88, 4), entryCrc);

            uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(gptImage.AsSpan(headerOffset + 12, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(gptImage.AsSpan(headerOffset + 16, 4), 0);
            uint headerCrc = ComputeGptCrc32(gptImage.AsSpan(headerOffset, checked((int)headerSize)));
            BinaryPrimitives.WriteUInt32LittleEndian(gptImage.AsSpan(headerOffset + 16, 4), headerCrc);
        }

        private static uint ComputeGptCrc32(ReadOnlySpan<byte> data)
        {
            uint crc = 0xFFFFFFFFU;
            foreach (byte value in data)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc >> 1) ^ ((crc & 1U) != 0 ? 0xEDB88320U : 0U);
            }
            return ~crc;
        }

        private bool TryExtractPrimaryGptImage(byte[] gptData, int defaultSectorSize, out byte[] gptImage, out int detectedSectorSize, out int gptSectors, out string error)
        {
            gptImage = Array.Empty<byte>();
            detectedSectorSize = defaultSectorSize > 0 ? defaultSectorSize : 4096;
            gptSectors = 0;
            error = "";

            int headerOffset = FindGptHeaderOffset(gptData);
            if (headerOffset < 0)
            {
                error = "未识别到GPT头";
                return false;
            }
            if (headerOffset + 92 > gptData.Length)
            {
                error = "GPT头数据不完整";
                return false;
            }

            ulong myLba = BitConverter.ToUInt64(gptData, headerOffset + 24);
            if (myLba > 0 && headerOffset > 0)
            {
                int sectorSize = (int)(headerOffset / (long)myLba);
                if (sectorSize == 512 || sectorSize == 4096)
                    detectedSectorSize = sectorSize;
            }

            ulong entryLba = BitConverter.ToUInt64(gptData, headerOffset + 72);
            uint numberOfEntries = BitConverter.ToUInt32(gptData, headerOffset + 80);
            uint entrySize = BitConverter.ToUInt32(gptData, headerOffset + 84);
            if (entryLba == 0 || numberOfEntries == 0 || entrySize == 0)
            {
                error = "GPT分区表参数异常";
                return false;
            }

            ulong requiredBytes = entryLba * (ulong)detectedSectorSize + (ulong)numberOfEntries * entrySize;
            ulong alignedBytes = ((requiredBytes + (ulong)detectedSectorSize - 1) / (ulong)detectedSectorSize) * (ulong)detectedSectorSize;
            if (alignedBytes == 0 || alignedBytes > (ulong)gptData.Length || alignedBytes > int.MaxValue)
            {
                error = "GPT主表大小超出读取范围";
                return false;
            }

            gptImage = new byte[(int)alignedBytes];
            Array.Copy(gptData, 0, gptImage, 0, gptImage.Length);
            gptSectors = gptImage.Length / detectedSectorSize;
            if (gptSectors <= 0)
            {
                error = "GPT主表扇区数异常";
                return false;
            }
            return true;
        }

        private static string BuildGptMainRawprogramXml(string fileName, int lun, int sectorSize, int numSectors)
        {
            double sizeKb = numSectors * sectorSize / 1024.0;
            return string.Format(
                CultureInfo.InvariantCulture,
                "<?xml version=\"1.0\" ?><data>\n" +
                "<program SECTOR_SIZE_IN_BYTES=\"{0}\" filename=\"{1}\" label=\"PrimaryGPT\" " +
                "num_partition_sectors=\"{2}\" physical_partition_number=\"{3}\" size_in_KB=\"{4:F1}\" sparse=\"false\" start_sector=\"0\" />\n" +
                "</data>\n",
                sectorSize,
                fileName,
                numSectors,
                lun,
                sizeKb);
        }

        public void DoWritePartitions(string programPath, IList<(string Label, int Lun)> bypassPartitions, bool skipSafe, bool skipData)
        {
            Debug.Assert(FirehoseServer != null);
            if (string.IsNullOrEmpty(programPath))
                throw new Exception("请先加载软件包");
            _updateCurrentPartitionText("批量刷写");
            var userBypassPartitions = bypassPartitions.ToList();
            var effectiveFlashed = TryGetEffectiveXmlFlashPartitions(programPath, userBypassPartitions, skipSafe, skipData);
            if (effectiveFlashed.Count == 0)
                throw new Exception("No flashable partitions found after filters.");

            string[] rawprogramFiles = FindRawprogramFiles(programPath);
            DoWriteSelectedPartitionsFromProgram(
                programPath,
                rawprogramFiles,
                effectiveFlashed.Select(p => (p.Label, p.Lun)).ToList(),
                false,
                false);
        }

        public void DoWriteSelectedPartitionsFromProgram(
            string programPath,
            string[] programFiles,
            IList<(string Label, int Lun)> selectedPartitions,
            bool skipSafe,
            bool skipData,
            IReadOnlyDictionary<string, string>? selectedFileOverrides = null,
            bool activateBootLun = true,
            IReadOnlyDictionary<string, EdlPartitionInfo>? devicePartitionTargets = null,
            bool applyPackagePostActions = true)
        {
            Debug.Assert(FirehoseServer != null);
            if (string.IsNullOrEmpty(programPath))
                throw new Exception("请先加载软件包");
            if (programFiles == null || programFiles.Length == 0)
                throw new Exception("未选择 rawprogram*.xml");
            if (selectedPartitions == null || selectedPartitions.Count == 0)
                throw new Exception("请先勾选要刷写的分区");

            string[] files = programFiles
                .Where(File.Exists)
                .ToArray();
            if (files.Length == 0)
                throw new Exception("未选择 rawprogram*.xml");

            var selectedSet = new HashSet<string>(
                selectedPartitions.Select(p => $"{p.Label}|{p.Lun}"),
                StringComparer.OrdinalIgnoreCase);

            List<EdlPartitionInfo> partitions = ProgramFlasher.ParseProgramFiles(programPath, files);
            List<EdlPartitionInfo> selectedXmlPartitions = partitions
                .Where(p => selectedSet.Contains($"{p.Label}|{p.Lun}"))
                .Where(p => !ShouldSkipByCategory(p.Label, skipSafe, skipData))
                .ToList();
            if (devicePartitionTargets != null)
                ObserveDevicePartitions(devicePartitionTargets.Values);
            List<EdlPartitionInfo> toFlash = devicePartitionTargets == null
                ? selectedXmlPartitions
                : MapXmlPartitionsToDeviceTargets(selectedXmlPartitions, devicePartitionTargets);

            if (toFlash.Count == 0)
                throw new Exception("未找到可刷写的分区（可能被跳过选项过滤或XML不包含该分区）");

            string? dynamicSuperDefinition = DynamicSuperPlanner.FindDefinition(programPath);
            bool dynamicSuperAssigned = false;
            var plan = new List<(
                EdlPartitionInfo Part,
                string? Path,
                bool LooksSparse,
                long TotalBytes,
                DynamicSuperPlan? DynamicSuper)>(toFlash.Count);
            foreach (var p in toFlash)
            {
                string partitionKey = $"{p.Label}|{p.Lun}";
                string? selectedFilePath = null;
                bool hasSelectedFileOverride =
                    selectedFileOverrides != null &&
                    selectedFileOverrides.TryGetValue(partitionKey, out selectedFilePath) &&
                    !string.IsNullOrWhiteSpace(selectedFilePath);

                string? resolvedPath;
                if (hasSelectedFileOverride)
                {
                    resolvedPath = File.Exists(selectedFilePath)
                        ? Path.GetFullPath(selectedFilePath)
                        : null;
                    _appendNativeLog(
                        $"[FlashPlan] {p.Label} (LUN{p.Lun}) 使用界面选择文件: {selectedFilePath}");
                }
                else
                {
                    resolvedPath = ResolveExistingProgramFilePath(programPath, p.FilePath);
                }

                if (resolvedPath == null)
                {
                    if (!hasSelectedFileOverride &&
                        IsEdlSuperPartition(p) &&
                        dynamicSuperDefinition != null)
                    {
                        if (dynamicSuperAssigned)
                            throw new InvalidOperationException("刷写计划中包含多个空 super 条目，无法确定免合并写入目标");
                        DynamicSuperPlan dynamicPlan = CreateDynamicSuperPlan(programPath);
                        plan.Add((p, null, false, dynamicPlan.TotalWriteBytes, dynamicPlan));
                        dynamicSuperAssigned = true;
                    }
                    else
                    {
                        plan.Add((p, null, false, 0, null));
                    }
                    continue;
                }
                bool looksSparse = LooksLikeAndroidSparse(resolvedPath);
                long totalBytes = GetPlannedFlashBytes(resolvedPath, looksSparse);
                plan.Add((p, resolvedPath, looksSparse, totalBytes, null));
            }

            plan = plan
                .OrderBy(item => item.Part.Lun)
                .ToList();

            double totalBytesSum = plan.Sum(x => (double)Math.Max(0, x.TotalBytes));
            bool useCountFallback = totalBytesSum <= 0;
            double overallTotalBytes = useCountFallback ? plan.Count : totalBytesSum;

            double completedBytesBefore = 0;
            var succeeded = new List<EdlPartitionInfo>();
            int failedCount = 0;
            var remainingPlanItemsByLun = plan
                .GroupBy(item => item.Part.Lun)
                .ToDictionary(group => group.Key, group => group.Count());
            var completedGptLuns = new HashSet<int>();

            void CompletePlanItemForLun(int lun)
            {
                ThrowIfStopRequested();
                if (!remainingPlanItemsByLun.TryGetValue(lun, out int remaining))
                    return;

                remaining--;
                remainingPlanItemsByLun[lun] = remaining;
                if (!applyPackagePostActions || remaining > 0 || !completedGptLuns.Add(lun))
                    return;

                List<EdlPartitionInfo> succeededForLun = succeeded
                    .Where(part => part.Lun == lun && !IsRawprogramGptEntry(part.Label, part.FilePath ?? string.Empty))
                    .ToList();
                if (succeededForLun.Count == 0)
                    return;

                string[] gptSourceFiles = FindRawprogramFilesForSucceededPartitions(files, succeededForLun);
                failedCount += WriteRawprogramGptEntriesForTouchedLuns(
                    programPath,
                    gptSourceFiles,
                    new HashSet<int> { lun },
                    succeeded);
            }

            for (int i = 0; i < plan.Count; i++)
            {
                ThrowIfStopRequested();
                var (part, resolvedPath, looksSparse, partTotalBytesLong, dynamicSuperPlan) = plan[i];
                double completedBytesBeforeLocal = completedBytesBefore;
                long partTotalBytes = useCountFallback
                    ? Math.Max(1, partTotalBytesLong)
                    : Math.Max(0, partTotalBytesLong);
                _updateCurrentPartitionText(part.Label);
                _updateProgress(0);
                if (dynamicSuperPlan != null)
                {
                    try
                    {
                        WriteDynamicSuperPlan(part, dynamicSuperPlan);
                        succeeded.Add(part);
                    }
                    catch (OperationCanceledException) when (IsStopRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        LogPartitionWriteFailure(part, ex);
                    }

                    completedBytesBefore += partTotalBytes;
                    _updateProgress(100);
                    CompletePlanItemForLun(part.Lun);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(resolvedPath))
                {
                    failedCount++;
                    LogMissingPartitionImage(part);
                    completedBytesBefore += partTotalBytes;
                    _updateProgress(100);
                    CompletePlanItemForLun(part.Lun);
                    continue;
                }

                part.FilePath = resolvedPath;
                EventHandler<(long, long)> progressHandler = (sender, e) =>
                {
                    long current = e.Item1;
                    long total = e.Item2;
                    double fraction = total <= 0 ? 0 : Math.Clamp(current / (double)total, 0, 1);
                    _updateProgress(Math.Clamp(fraction * 100.0, 0, 100));
                };
                FirehoseServer.ProgressChanged += progressHandler;
                long lastUnits = 0;
                long lastTick = Stopwatch.GetTimestamp();
                long lastSpeedUiTick = 0;
                long bytesPerUnit = 1;
                bool bytesPerUnitKnown = false;
                EventHandler<(long, long)> speedHandler = (sender, e) =>
                {
                    long currentUnits = e.Item1;
                    long totalUnits = e.Item2;
                    if (!bytesPerUnitKnown && totalUnits > 0)
                    {
                        bytesPerUnit = GuessBytesPerProgressUnit(resolvedPath, looksSparse, totalUnits);
                        bytesPerUnitKnown = true;
                    }
                    long now = Stopwatch.GetTimestamp();
                    long dtTicks = now - lastTick;
                    if (dtTicks > 0)
                    {
                        double dt = dtTicks / (double)Stopwatch.Frequency;
                        long deltaUnits = currentUnits - lastUnits;
                        if (deltaUnits >= 0 && now - lastSpeedUiTick >= Stopwatch.Frequency / 5)
                        {
                            double bytesPerSecond = (deltaUnits * (double)bytesPerUnit) / dt;
                            UpdateSmoothedTransferSpeed(bytesPerSecond);
                            lastSpeedUiTick = now;
                        }
                    }
                    lastUnits = currentUnits;
                    lastTick = now;
                };
                FirehoseServer.ProgressChanged += speedHandler;
                try
                {
                    string flashLinePrefix = FormatEdlPartitionWriteLine(resolvedPath, $"LUN{part.Lun}", part.Label);
                    _log($"__EDL_FLASH_BEGIN__|{flashLinePrefix}", null);
                    if (looksSparse)
                    {
                        part.Sparse = true;
                        WriteSparsePartitionWithSessionStrategy(
                            resolvedPath,
                            part,
                            completedBytesBeforeLocal,
                            overallTotalBytes,
                            partTotalBytes);
                    }
                    else
                    {
                        part.Sparse = false;
                        WriteRawPartitionWithSessionStrategy(
                            resolvedPath,
                            part,
                            completedBytesBeforeLocal,
                            overallTotalBytes,
                            partTotalBytes);
                    }

                    _log("__EDL_FLASH_OK__", null);
                    succeeded.Add(part);
                }
                catch (Exception ex)
                {
                    failedCount++;
                    LogPartitionWriteFailure(part, ex);
                }
                finally
                {
                    FirehoseServer.ProgressChanged -= progressHandler;
                    FirehoseServer.ProgressChanged -= speedHandler;
                }

                completedBytesBefore += partTotalBytes;
                _updateProgress(100);
                CompletePlanItemForLun(part.Lun);
            }

            ThrowIfStopRequested();
            if (applyPackagePostActions && succeeded.Count > 0)
                TryApplyPatchAndActivateBootLunForXmlFlash(programPath, succeeded, activateBootLun);
            _log("写分区流程结束...", null);
            _updateSpeedText(null);
            _updateCurrentPartitionText(null);
        }

        private static List<EdlPartitionInfo> MapXmlPartitionsToDeviceTargets(
            IList<EdlPartitionInfo> xmlPartitions,
            IReadOnlyDictionary<string, EdlPartitionInfo> devicePartitionTargets)
        {
            var mapped = new List<EdlPartitionInfo>(xmlPartitions.Count);
            foreach (var group in xmlPartitions.GroupBy(
                         partition => $"{partition.Label}|{partition.Lun}",
                         StringComparer.OrdinalIgnoreCase))
            {
                if (!devicePartitionTargets.TryGetValue(group.Key, out EdlPartitionInfo? deviceTarget))
                    throw new Exception($"设备分区表中未找到目标分区: {group.Key}");
                if (!TryParseLongAllowHex(deviceTarget.StartSector, out long deviceStartSector))
                    throw new Exception($"无法解析设备分区 {deviceTarget.Label} 的起始扇区");
                if (deviceTarget.SectorLen <= 0)
                    throw new Exception($"设备分区 {deviceTarget.Label} 的大小无效");

                var entries = group.ToList();
                var xmlStartSectors = new List<long>(entries.Count);
                foreach (EdlPartitionInfo entry in entries)
                {
                    if (!TryParseLongAllowHex(entry.StartSector, out long xmlStartSector))
                        throw new Exception($"无法解析 XML 分区 {entry.Label} 的起始扇区");
                    xmlStartSectors.Add(xmlStartSector);
                }

                long xmlBaseSector = xmlStartSectors.Min();
                for (int i = 0; i < entries.Count; i++)
                {
                    EdlPartitionInfo entry = entries[i];
                    long relativeSector = checked(xmlStartSectors[i] - xmlBaseSector);
                    if (relativeSector < 0 || relativeSector >= deviceTarget.SectorLen)
                        throw new Exception($"XML 分区 {entry.Label} 的相对偏移超出设备分区范围");

                    long remainingSectors = checked(deviceTarget.SectorLen - relativeSector);
                    long xmlSectorLen = entry.SectorLen > 0 ? entry.SectorLen : remainingSectors;
                    if (xmlSectorLen > remainingSectors)
                    {
                        throw new Exception(
                            $"XML 分区 {entry.Label} 的写入范围超出设备分区边界: " +
                            $"{xmlSectorLen} > {remainingSectors} sectors");
                    }

                    mapped.Add(new EdlPartitionInfo
                    {
                        Label = deviceTarget.Label,
                        Lun = deviceTarget.Lun,
                        StartSector = checked(deviceStartSector + relativeSector)
                            .ToString(CultureInfo.InvariantCulture),
                        SectorLen = xmlSectorLen,
                        BytesPerSector = deviceTarget.BytesPerSector > 0
                            ? deviceTarget.BytesPerSector
                            : entry.BytesPerSector,
                        Sparse = entry.Sparse,
                        FilePath = entry.FilePath,
                        FileSectorOffset = entry.FileSectorOffset
                    });
                }
            }

            return mapped;
        }

        private int WriteRawprogramGptEntriesForTouchedLuns(string programPath, string[] rawprogramFiles, ISet<int> touchedLuns, IList<EdlPartitionInfo> succeeded)
        {
            if (touchedLuns == null || touchedLuns.Count == 0)
                return 0;

            var entries = TryParseRawprogramGptEntries(rawprogramFiles, touchedLuns);
            if (entries.Count == 0)
                return 0;

            int failedCount = 0;
            var writtenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in entries)
            {
                ThrowIfStopRequested();
                string key = $"{part.Label}|{part.Lun}|{part.FilePath}";
                if (!writtenKeys.Add(key))
                    continue;

                string? resolvedPath = ResolveExistingProgramFilePath(programPath, part.FilePath);
                string displayName = string.IsNullOrWhiteSpace(part.FilePath) ? part.Label : Path.GetFileName(part.FilePath);
                _updateCurrentPartitionText(part.Label);
                _updateProgress(0);
                _log($"__EDL_FLASH_BEGIN__|{FormatEdlPartitionWriteLine(displayName, $"LUN{part.Lun}", part.Label)}", null);

                try
                {
                    if (resolvedPath == null)
                        throw new Exception($"GPT image file does not exist: {part.FilePath}");

                    part.FilePath = resolvedPath;
                    part.Sparse = false;
                    NormalizeRawprogramGptStartSector(programPath, part, resolvedPath);

                    try
                    {
                        if (_useOplusReadWithoutSpoof)
                        {
                            WriteRawImageViaProgram(resolvedPath, part, 0, 1, new FileInfo(resolvedPath).Length);
                        }
                        else
                        {
                            TraceSharpEdlProgramIntent(part, resolvedPath, false);
                            FirehoseServer!.WriteUnsparseImage(part).CheckAndThrow();
                        }
                    }
                    catch (Exception ex) when (TryParseLongAllowHex(part.StartSector, out _))
                    {
                        _appendNativeLog($"[Warn] {part.Label} WriteUnsparseImage failed, retry raw sector write: {ex.Message}");
                        WriteRawImageViaProgram(resolvedPath, part, 0, 1, new FileInfo(resolvedPath).Length);
                    }

                    _log("__EDL_FLASH_OK__", null);
                    succeeded.Add(part);
                }
                catch (Exception ex)
                {
                    failedCount++;
                    LogPartitionWriteFailure(part, ex);
                }
                finally
                {
                    _updateProgress(100);
                }
                ThrowIfStopRequested();
            }

            return failedCount;
        }

        public void DoWriteGptEntriesFromRawprogramFiles(string programPath, string[] rawprogramFiles)
        {
            Debug.Assert(FirehoseServer != null);
            if (string.IsNullOrWhiteSpace(programPath) || !Directory.Exists(programPath))
                throw new Exception("无效的刷机包路径");
            if (rawprogramFiles == null || rawprogramFiles.Length == 0)
                throw new Exception("未选择 rawprogram*.xml");

            string[] files = rawprogramFiles.Where(File.Exists).ToArray();
            if (files.Length == 0)
                throw new Exception("未选择 rawprogram*.xml");

            var gptLuns = TryParseRawprogramGptLuns(files);
            if (gptLuns.Count == 0)
                throw new Exception("所选 rawprogram*.xml 中未找到 GPT 条目");

            var written = new List<EdlPartitionInfo>();
            int failedCount = WriteRawprogramGptEntriesForTouchedLuns(programPath, files, gptLuns, written);
            ThrowIfStopRequested();
            long totalBytes = written
                .Select(p => p.FilePath ?? "")
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Sum(p => new FileInfo(p).Length);

            if (failedCount > 0)
                _log($"写入GPT流程结束，失败 {failedCount} 个条目，已跳过并继续。", "error");
            _log($"写入总大小 : {FormatByteSize(totalBytes)}", null);
            _log("操作线程结束...", null);
            _updateSpeedText(null);
            _updateCurrentPartitionText(null);
        }

        private HashSet<int> TryParseRawprogramGptLuns(string[] rawprogramFiles)
        {
            var luns = new HashSet<int>();
            if (rawprogramFiles == null || rawprogramFiles.Length == 0)
                return luns;

            foreach (string rawprogramFile in rawprogramFiles.Where(File.Exists).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                XDocument doc;
                try
                {
                    doc = XDocument.Load(rawprogramFile);
                }
                catch (Exception ex)
                {
                    _appendNativeLog($"[Warn] rawprogram GPT LUN parse failed: {rawprogramFile} {ex.Message}");
                    continue;
                }

                foreach (var elem in doc.Descendants("program"))
                {
                    string label = elem.Attribute("label")?.Value?.Trim() ?? "";
                    string filename = elem.Attribute("filename")?.Value?.Trim() ?? "";
                    if (!IsRawprogramGptEntry(label, filename))
                        continue;

                    if (int.TryParse(elem.Attribute("physical_partition_number")?.Value ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out int lun))
                        luns.Add(lun);
                }
            }

            return luns;
        }

        private string[] FindRawprogramFilesForSucceededPartitions(string[] rawprogramFiles, IList<EdlPartitionInfo> succeeded)
        {
            if (rawprogramFiles == null || rawprogramFiles.Length == 0 || succeeded == null || succeeded.Count == 0)
                return Array.Empty<string>();

            var succeededKeys = new HashSet<string>(
                succeeded
                    .Where(p => !IsRawprogramGptEntry(p.Label, p.FilePath ?? ""))
                    .Select(p => $"{p.Label}|{p.Lun}"),
                StringComparer.OrdinalIgnoreCase);
            if (succeededKeys.Count == 0)
                return Array.Empty<string>();

            var sourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawprogramFile in rawprogramFiles.Where(File.Exists).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                XDocument doc;
                try
                {
                    doc = XDocument.Load(rawprogramFile);
                }
                catch (Exception ex)
                {
                    _appendNativeLog($"[Warn] rawprogram source parse failed: {rawprogramFile} {ex.Message}");
                    continue;
                }

                foreach (var elem in doc.Descendants("program"))
                {
                    string label = elem.Attribute("label")?.Value?.Trim() ?? "";
                    string filename = elem.Attribute("filename")?.Value?.Trim() ?? "";
                    if (IsRawprogramGptEntry(label, filename))
                        continue;

                    if (!int.TryParse(elem.Attribute("physical_partition_number")?.Value ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out int lun))
                        continue;

                    if (succeededKeys.Contains($"{label}|{lun}"))
                    {
                        sourceFiles.Add(rawprogramFile);
                        break;
                    }
                }
            }

            return sourceFiles
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private List<EdlPartitionInfo> TryParseRawprogramGptEntries(string[] rawprogramFiles, ISet<int> touchedLuns)
        {
            var entries = new List<EdlPartitionInfo>();
            if (rawprogramFiles == null || rawprogramFiles.Length == 0)
                return entries;

            foreach (string rawprogramFile in rawprogramFiles.Where(File.Exists).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                XDocument doc;
                try
                {
                    doc = XDocument.Load(rawprogramFile);
                }
                catch (Exception ex)
                {
                    _appendNativeLog($"[Warn] rawprogram GPT parse failed: {rawprogramFile} {ex.Message}");
                    continue;
                }

                foreach (var elem in doc.Descendants("program"))
                {
                    string label = elem.Attribute("label")?.Value?.Trim() ?? "";
                    string filename = elem.Attribute("filename")?.Value?.Trim() ?? "";
                    if (!IsRawprogramGptEntry(label, filename))
                        continue;

                    if (!int.TryParse(elem.Attribute("physical_partition_number")?.Value ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out int lun))
                        continue;
                    if (!touchedLuns.Contains(lun))
                        continue;

                    int sectorSize = 4096;
                    int.TryParse(elem.Attribute("SECTOR_SIZE_IN_BYTES")?.Value ?? "4096", NumberStyles.Integer, CultureInfo.InvariantCulture, out sectorSize);
                    if (sectorSize <= 0)
                        sectorSize = _firehoseSectorSize > 0 ? _firehoseSectorSize : 4096;

                    long sectorLen = 0;
                    TryParseLongAllowHex(elem.Attribute("num_partition_sectors")?.Value ?? "0", out sectorLen);

                    entries.Add(new EdlPartitionInfo
                    {
                        Label = string.IsNullOrWhiteSpace(label) ? Path.GetFileNameWithoutExtension(filename) : label,
                        Lun = lun,
                        StartSector = elem.Attribute("start_sector")?.Value?.Trim() ?? "0",
                        SectorLen = sectorLen,
                        BytesPerSector = sectorSize,
                        FilePath = filename,
                        Sparse = false
                    });
                }
            }

            return entries
                .OrderBy(p => p.Lun)
                .ThenBy(p => p.Label.Equals("PrimaryGPT", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ToList();
        }

        private static bool IsRawprogramGptEntry(string label, string filename)
        {
            string fileNameOnly = Path.GetFileName(filename ?? "");
            return label.Equals("PrimaryGPT", StringComparison.OrdinalIgnoreCase)
                || label.Equals("BackupGPT", StringComparison.OrdinalIgnoreCase)
                || fileNameOnly.StartsWith("gpt_main", StringComparison.OrdinalIgnoreCase)
                || fileNameOnly.StartsWith("gpt_backup", StringComparison.OrdinalIgnoreCase);
        }

        private void NormalizeRawprogramGptStartSector(string programPath, EdlPartitionInfo part, string resolvedPath)
        {
            if (TryParseLongAllowHex(part.StartSector, out _))
                return;

            string expression = part.StartSector ?? "";
            if (!expression.Contains("NUM_DISK_SECTORS", StringComparison.OrdinalIgnoreCase))
                return;

            int sectorSize = part.BytesPerSector > 0 ? part.BytesPerSector : (_firehoseSectorSize > 0 ? _firehoseSectorSize : 4096);
            if (!TryGetNumDiskSectorsForLun(programPath, resolvedPath, part.Lun, sectorSize, out long numDiskSectors))
            {
                _appendNativeLog($"[Warn] Unable to evaluate GPT start_sector expression: {expression}");
                return;
            }

            if (TryEvaluateNumDiskSectorsExpression(expression, numDiskSectors, out long startSector))
            {
                _appendNativeLog($"[Info] GPT start_sector {expression} => {startSector}");
                part.StartSector = startSector.ToString(CultureInfo.InvariantCulture);
            }
        }

        private bool TryGetNumDiskSectorsForLun(string programPath, string resolvedPath, int lun, int sectorSize, out long numDiskSectors)
        {
            numDiskSectors = 0;
            string? imageDir = Path.GetDirectoryName(resolvedPath);
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(imageDir))
                candidates.Add(Path.Combine(imageDir, $"gpt_main{lun}.bin"));
            try
            {
                candidates.AddRange(Directory.GetFiles(programPath, $"gpt_main{lun}.bin", SearchOption.AllDirectories));
            }
            catch
            {
            }

            foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (TryReadNumDiskSectorsFromGptFile(candidate, sectorSize, out numDiskSectors))
                    return true;
            }

            try
            {
                byte[]? gptData = ReadPrimaryGptPrefix(
                    lun,
                    8,
                    finalAckTimeoutMs: 1500,
                    acceptCompleteDataWithoutFinalAck: true);
                if (gptData != null && TryReadNumDiskSectorsFromGptBytes(gptData, sectorSize, out numDiskSectors))
                    return true;
            }
            catch (Exception ex)
            {
                _appendNativeLog($"[Warn] Unable to read LUN{lun} GPT for disk size: {ex.Message}");
                if (IsSessionBreakingFailure(ex))
                    throw;
            }

            return false;
        }

        private static bool TryEvaluateNumDiskSectorsExpression(string expression, long numDiskSectors, out long value)
        {
            value = 0;
            if (numDiskSectors <= 0)
                return false;

            string text = (expression ?? "").Trim().TrimEnd('.').Replace(" ", "");
            const string token = "NUM_DISK_SECTORS";
            if (!text.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                return false;

            value = numDiskSectors;
            string tail = text.Substring(token.Length);
            if (tail.Length == 0)
                return true;
            char sign = tail[0];
            if (sign != '+' && sign != '-')
                return false;
            if (!long.TryParse(tail.Substring(1).TrimEnd('.'), NumberStyles.Integer, CultureInfo.InvariantCulture, out long delta))
                return false;
            value = sign == '-' ? numDiskSectors - delta : numDiskSectors + delta;
            return value >= 0;
        }

        private static bool TryReadNumDiskSectorsFromGptFile(string filePath, int sectorSize, out long numDiskSectors)
        {
            numDiskSectors = 0;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return false;
            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                return TryReadNumDiskSectorsFromGptBytes(data, sectorSize, out numDiskSectors);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadNumDiskSectorsFromGptBytes(byte[] data, int sectorSize, out long numDiskSectors)
        {
            numDiskSectors = 0;
            if (data == null || data.Length < 92)
                return false;

            foreach (int offset in new[] { Math.Max(512, sectorSize), 512, 0 })
            {
                if (offset < 0 || offset + 92 > data.Length)
                    continue;
                if (data[offset] != (byte)'E' || data[offset + 1] != (byte)'F' || data[offset + 2] != (byte)'I' || data[offset + 3] != (byte)' ')
                    continue;
                ulong backupLba = BitConverter.ToUInt64(data, offset + 32);
                if (backupLba == 0 || backupLba > (ulong)long.MaxValue - 1UL)
                    continue;
                numDiskSectors = checked((long)backupLba + 1);
                return true;
            }

            return false;
        }

        private void CloseFirehosePortForExternalTool()
        {
            DisposeCurrentSerialPort();
            Thread.Sleep(300);
        }

        private void ReopenFirehoseAfterExternalTool()
        {
            if (string.IsNullOrWhiteSpace(_currentPortName))
                throw new Exception("fh_loader 写入后无法重新连接设备：端口为空");

            Exception? lastError = null;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    _currentPort = new SerialPort(_currentPortName);
                    _currentPort.Open();
                    _currentPort.DtrEnable = true;
                    _currentPort.RtsEnable = true;
                    BeginFirehoseSession("reopen after external tool");
                    _saharaServer = new SaharaServer(_currentPort);
                    FirehoseServer = new FirehoseServer(_currentPort);
                    Thread.Sleep(500);
                    ConfigureFirehoseWithEdlStyle(8000);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    DisposeCurrentSerialPort();
                    Thread.Sleep(200);
                }
            }

            throw new Exception("fh_loader 写入后重新连接 Firehose 失败: " + lastError?.Message);
        }

        private static bool FhLoaderSupportsOption(string exePath, string option)
        {
            try
            {
                string text = Encoding.ASCII.GetString(File.ReadAllBytes(exePath));
                return text.Contains(option, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsBypassed(EdlPartitionInfo part, IList<(string Label, int Lun)> bypass)
        {
            if (bypass == null || bypass.Count == 0)
                return false;
            for (int i = 0; i < bypass.Count; i++)
            {
                var item = bypass[i];
                if (!item.Label.Equals(part.Label, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (item.Lun == -1 || item.Lun == part.Lun)
                    return true;
            }
            return false;
        }

        private static string[] FindRawprogramFiles(string programPath)
        {
            try
            {
                return Directory.GetFiles(programPath, "rawprogram*.xml", SearchOption.AllDirectories)
                    .Where(MainWindow.IsSelectableEdlRawProgramFile)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static string[] FindPatchFiles(string programPath)
        {
            try
            {
                return Directory.GetFiles(programPath, "patch*.xml", SearchOption.AllDirectories)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static string FormatEdlPartitionWriteLine(string sourcePathOrName, string targetScope, string targetLabel)
        {
            string sourceName = Path.GetFileName(sourcePathOrName);
            if (string.IsNullOrWhiteSpace(sourceName))
                sourceName = sourcePathOrName;
            return $"{sourceName}->[{targetScope}]{targetLabel}...";
        }

        private static bool TryParseLongAllowHex(string text, out long value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private List<EdlPartitionInfo> TryGetEffectiveXmlFlashPartitions(string programPath, IList<(string Label, int Lun)> bypass, bool skipSafe, bool skipData)
        {
            string[] rawprogramFiles = FindRawprogramFiles(programPath);
            if (rawprogramFiles.Length == 0)
                return new List<EdlPartitionInfo>();
            List<EdlPartitionInfo> partitions = ProgramFlasher.ParseProgramFiles(programPath, rawprogramFiles);
            return partitions
                .Where(p => !ShouldSkipByCategory(p.Label, skipSafe, skipData))
                .Where(p => !IsBypassed(p, bypass))
                .ToList();
        }

        private int ApplyPatchXmlFromDirectory(string programPath, int timeoutMs)
        {
            string[] patchFiles = FindPatchFiles(programPath);
            if (patchFiles.Length == 0)
                return 0;

            int totalSuccess = 0;
            for (int i = 0; i < patchFiles.Length; i++)
            {
                ThrowIfStopRequested();
                string patchPath = patchFiles[i];
                if (!File.Exists(patchPath))
                    continue;
                string patchFileName = Path.GetFileName(patchPath);
                _log($"__EDL_FLASH_BEGIN__|应用 {patchFileName}...", null);
                try
                {
                    XDocument doc = XDocument.Load(patchPath);
                    XElement? root = doc.Root;
                    if (root == null)
                        throw new Exception($"{patchFileName} 缺少 XML 根节点");

                    foreach (var elem in root.Elements("patch"))
                    {
                        ThrowIfStopRequested();
                        string filename = elem.Attribute("filename")?.Value?.Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(filename) &&
                            !filename.Equals("DISK", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string value = elem.Attribute("value")?.Value ?? "";
                        if (string.IsNullOrWhiteSpace(value))
                            continue;

                        int lun = 0;
                        int.TryParse(elem.Attribute("physical_partition_number")?.Value ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out lun);

                        string startSector = elem.Attribute("start_sector")?.Value?.Trim() ?? "0";
                        if (string.IsNullOrWhiteSpace(startSector))
                            startSector = "0";

                        int byteOffset = 0;
                        int.TryParse(elem.Attribute("byte_offset")?.Value ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out byteOffset);

                        int sizeInBytes = 0;
                        int.TryParse(elem.Attribute("size_in_bytes")?.Value ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out sizeInBytes);

                        if (sizeInBytes <= 0)
                            continue;

                        if (!ApplyPatch(lun, startSector, byteOffset, sizeInBytes, value, timeoutMs))
                            throw new Exception($"{patchFileName} 中的 Patch 命令未返回 ACK");
                        totalSuccess++;
                    }

                    _log("__EDL_FLASH_OK__", null);
                }
                catch (OperationCanceledException) when (IsStopRequested)
                {
                    throw;
                }
                catch
                {
                    _log("__EDL_FLASH_ERROR__", null);
                    throw;
                }
            }

            return totalSuccess;
        }

        private bool ApplyPatch(int lun, string startSector, int byteOffset, int sizeInBytes, string value, int timeoutMs)
        {
            if (_currentPort == null || !_currentPort.IsOpen)
                throw new Exception("串口未打开");
            int sectorSize = _firehoseSectorSize > 0 ? _firehoseSectorSize : 4096;
            string safeStartSector = System.Security.SecurityElement.Escape(startSector) ?? "0";
            string safeValue = System.Security.SecurityElement.Escape(value) ?? "";
            string xml = string.Format(
                CultureInfo.InvariantCulture,
                "<?xml version=\"1.0\" ?><data><patch SECTOR_SIZE_IN_BYTES=\"{0}\" byte_offset=\"{1}\" filename=\"DISK\" physical_partition_number=\"{2}\" size_in_bytes=\"{3}\" start_sector=\"{4}\" value=\"{5}\" /></data>",
                sectorSize, byteOffset, lun, sizeInBytes, safeStartSector, safeValue);
            PurgePortBuffer();
            byte[] bytes = Encoding.UTF8.GetBytes(xml);
            TraceEdlTx(xml);
            _currentPort.Write(bytes, 0, bytes.Length);
            return WaitForAck(timeoutMs);
        }

        private bool TrySetBootLun(int lun, int timeoutMs)
        {
            if (_currentPort == null || !_currentPort.IsOpen)
                throw new Exception("串口未打开");
            string xml = string.Format(
                CultureInfo.InvariantCulture,
                "<?xml version=\"1.0\" ?><data><setbootablestoragedrive value=\"{0}\" /></data>",
                lun);
            PurgePortBuffer();
            byte[] bytes = Encoding.UTF8.GetBytes(xml);
            TraceEdlTx(xml);
            _currentPort.Write(bytes, 0, bytes.Length);
            return WaitForAck(timeoutMs);
        }

        private static int GuessBootLunFromPartitions(IList<EdlPartitionInfo> flashed)
        {
            if (flashed == null || flashed.Count == 0)
                return 0;
            int aCount = 0;
            int bCount = 0;
            for (int i = 0; i < flashed.Count; i++)
            {
                var p = flashed[i];
                if (p.Label.EndsWith("_a", StringComparison.OrdinalIgnoreCase)) aCount++;
                else if (p.Label.EndsWith("_b", StringComparison.OrdinalIgnoreCase)) bCount++;
            }
            if (aCount > bCount)
                return 1;
            if (bCount > aCount)
                return 2;
            return 0;
        }

        private void TryApplyPatchAndActivateBootLunForXmlFlash(
            string programPath,
            IList<EdlPartitionInfo> flashedPartitions,
            bool activateBootLun = true)
        {
            int patchTimeoutMs = 8000;
            int bootLunTimeoutMs = 8000;

            string[] patchFiles = FindPatchFiles(programPath);
            if (patchFiles.Length > 0)
                ApplyPatchXmlFromDirectory(programPath, patchTimeoutMs);

            if (!activateBootLun)
            {
                _appendNativeLog("[FlashPlan] 手动选择分区写入，跳过激活启动 LUN");
                return;
            }

            string memoryName = (FirehoseServer?.MemoryName ?? "").Trim();
            bool isUfs = memoryName.Equals("ufs", StringComparison.OrdinalIgnoreCase);
            if (!isUfs && string.IsNullOrEmpty(memoryName))
                isUfs = _firehoseSectorSize >= 4096;
            if (!isUfs)
                return;

            if (flashedPartitions == null || flashedPartitions.Count == 0)
                return;

            int bootLun = GuessBootLunFromPartitions(flashedPartitions);
            if (bootLun <= 0)
            {
                _appendNativeLog("[FlashPlan] A/B 槽位写入数量相同或没有槽位分区，跳过激活启动 LUN");
                return;
            }

            _log($"__EDL_FLASH_BEGIN__|设置启动存储单元...LUN{bootLun}", null);
            try
            {
                bool ok = TrySetBootLun(bootLun, bootLunTimeoutMs);
                _log(ok ? "__EDL_FLASH_OK__" : "__EDL_FLASH_ERROR__", null);
            }
            catch
            {
                _log("__EDL_FLASH_ERROR__", null);
                throw;
            }
        }

        private static string? ResolveExistingProgramFilePath(string programDir, string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;
            if (File.Exists(filePath))
                return Path.GetFullPath(filePath);
            if (!Path.IsPathRooted(filePath))
            {
                string combined = Path.GetFullPath(Path.Combine(programDir, filePath));
                if (File.Exists(combined))
                    return combined;
            }
            return null;
        }

        private static bool ShouldSkipByCategory(string label, bool skipSafe, bool skipData)
        {
            if (skipSafe && MainWindow.IsEdlBasebandPartitionLabel(label))
                return true;
            if (skipData)
            {
                if (label.Equals("userdata", StringComparison.OrdinalIgnoreCase)) return true;
                if (label.Equals("metadata", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool LooksLikeAndroidSparse(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (fs.Length < 4)
                    return false;
                Span<byte> buf = stackalloc byte[4];
                int read = fs.Read(buf);
                if (read != 4)
                    return false;
                uint magic = (uint)(buf[0] | (buf[1] << 8) | (buf[2] << 16) | (buf[3] << 24));
                return magic == 0xED26FF3A;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryValidateAndroidSparseImage(string filePath, out string? reason)
        {
            reason = null;
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var br = new BinaryReader(fs);
                if (fs.Length < 28)
                {
                    reason = "文件过小";
                    return false;
                }
                uint magic = br.ReadUInt32();
                if (magic != 0xED26FF3A)
                {
                    reason = "不是 sparse 镜像";
                    return false;
                }
                ushort major = br.ReadUInt16();
                br.ReadUInt16();
                ushort fileHdrSz = br.ReadUInt16();
                ushort chunkHdrSz = br.ReadUInt16();
                uint blkSz = br.ReadUInt32();
                uint totalBlks = br.ReadUInt32();
                uint totalChunks = br.ReadUInt32();
                br.ReadUInt32();

                if (major != 1)
                {
                    reason = $"sparse 版本不支持: {major}";
                    return false;
                }
                if (fileHdrSz < 28 || chunkHdrSz < 12)
                {
                    reason = "sparse 头大小异常";
                    return false;
                }
                if (blkSz == 0 || blkSz > 1024 * 1024)
                {
                    reason = "块大小异常";
                    return false;
                }
                if (totalChunks == 0 || totalChunks > 50_000_000)
                {
                    reason = "chunk 数异常";
                    return false;
                }

                if (fileHdrSz > 28)
                {
                    long skip = fileHdrSz - 28;
                    if (fs.Position + skip > fs.Length)
                    {
                        reason = "sparse 头越界";
                        return false;
                    }
                    fs.Seek(skip, SeekOrigin.Current);
                }

                ulong parsedBlocks = 0;
                for (uint i = 0; i < totalChunks; i++)
                {
                    if (fs.Position + chunkHdrSz > fs.Length)
                    {
                        reason = "chunk 头越界";
                        return false;
                    }
                    ushort chunkType = br.ReadUInt16();
                    br.ReadUInt16();
                    uint chunkSz = br.ReadUInt32();
                    uint totalSz = br.ReadUInt32();
                    if (chunkHdrSz > 12)
                        fs.Seek(chunkHdrSz - 12, SeekOrigin.Current);

                    if (totalSz < chunkHdrSz)
                    {
                        reason = "chunk 大小异常";
                        return false;
                    }

                    long dataBytes;
                    switch (chunkType)
                    {
                        case 0xCAC1:
                            dataBytes = (long)chunkSz * blkSz;
                            if (totalSz != chunkHdrSz + dataBytes)
                            {
                                reason = "RAW chunk 大小不匹配";
                                return false;
                            }
                            break;
                        case 0xCAC2:
                            dataBytes = 4;
                            if (totalSz != chunkHdrSz + dataBytes)
                            {
                                reason = "FILL chunk 大小不匹配";
                                return false;
                            }
                            break;
                        case 0xCAC3:
                            dataBytes = 0;
                            if (totalSz != chunkHdrSz)
                            {
                                reason = "DONT_CARE chunk 大小不匹配";
                                return false;
                            }
                            break;
                        case 0xCAC4:
                            dataBytes = 4;
                            if (chunkSz != 0)
                            {
                                reason = "CRC32 chunk 块数必须为 0";
                                return false;
                            }
                            if (totalSz != chunkHdrSz + dataBytes)
                            {
                                reason = "CRC32 chunk 大小不匹配";
                                return false;
                            }
                            break;
                        default:
                            reason = $"Invalid chunk type: 0x{chunkType:X4}";
                            return false;
                    }

                    if (chunkType != 0xCAC4)
                    {
                        parsedBlocks += chunkSz;
                        if (parsedBlocks > totalBlks)
                        {
                            reason = "chunk 展开块数超出 sparse 头声明";
                            return false;
                        }
                    }

                    if (fs.Position + dataBytes > fs.Length)
                    {
                        reason = "chunk 数据越界";
                        return false;
                    }
                    fs.Seek(dataBytes, SeekOrigin.Current);
                }

                if (parsedBlocks != totalBlks)
                {
                    reason = $"chunk 展开块数不匹配: {parsedBlocks}/{totalBlks}";
                    return false;
                }
                if (fs.Position != fs.Length)
                {
                    reason = $"sparse 文件存在未解析尾部数据: {fs.Length - fs.Position} bytes";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private static bool IsEdlUserdataPartition(EdlPartitionInfo part)
        {
            return part.Label.Equals("userdata", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEdlSuperPartition(EdlPartitionInfo part)
        {
            return part.Label.Equals("super", StringComparison.OrdinalIgnoreCase)
                || part.Label.StartsWith("super_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEdlPermissionProtectedPartition(EdlPartitionInfo part)
        {
            return IsProtectedEdlWritePartitionName(part.Label);
        }

        private bool ShouldUseProgramSpoofWrite(EdlPartitionInfo part)
        {
            if (_useOplusReadWithoutSpoof)
                return true;

            if (IsEdlSuperPartition(part))
                return false;

            EdlPartitionWriteStrategy learnedStrategy = GetPartitionWriteStrategy(part);
            return learnedStrategy == EdlPartitionWriteStrategy.SpoofRequired;
        }

        private static bool IsExactOplusWritePermissionDenied(string? details)
        {
            return !string.IsNullOrWhiteSpace(details)
                && (details.Contains("not allowed on external network", StringComparison.OrdinalIgnoreCase)
                    || details.Contains("not get permission", StringComparison.OrdinalIgnoreCase));
        }

        private void ValidatePartitionWriteTarget(
            EdlPartitionInfo part,
            string imagePath,
            bool sparse)
        {
            if (!HasKnownDevicePartitionTargets
                || IsRawprogramGptEntry(part.Label, part.FilePath ?? string.Empty))
            {
                return;
            }

            if (!TryGetKnownDevicePartitionTarget(part.Label, part.Lun, out EdlPartitionInfo? target)
                || target == null)
            {
                RememberPartitionWriteStrategy(part, EdlPartitionWriteStrategy.Unsupported);
                throw new Exception($"设备 GPT 中不存在目标分区 {part.Label} (LUN{part.Lun})");
            }

            if (!TryParseLongAllowHex(part.StartSector, out long writeStart)
                || !TryParseLongAllowHex(target.StartSector, out long targetStart)
                || target.SectorLen <= 0)
            {
                RememberPartitionWriteStrategy(part, EdlPartitionWriteStrategy.Unsupported);
                throw new Exception($"无法验证 {part.Label} (LUN{part.Lun}) 的写入范围");
            }

            int sectorSize = target.BytesPerSector > 0
                ? target.BytesPerSector
                : (_firehoseSectorSize > 0 ? _firehoseSectorSize : 4096);
            long plannedBytes = GetPlannedFlashBytes(imagePath, sparse);
            long imageSectors = plannedBytes <= 0
                ? 0
                : checked((plannedBytes + sectorSize - 1) / sectorSize);
            long declaredSectors = part.SectorLen > 0 ? part.SectorLen : imageSectors;
            long writeSectors = Math.Max(imageSectors, declaredSectors);
            long targetEnd = checked(targetStart + target.SectorLen);
            long writeEnd = checked(writeStart + writeSectors);
            if (writeStart < targetStart || writeEnd > targetEnd)
            {
                RememberPartitionWriteStrategy(part, EdlPartitionWriteStrategy.Unsupported);
                throw new Exception(
                    $"{part.Label} (LUN{part.Lun}) 写入范围超出设备 GPT 目标分区");
            }
        }

        private void WriteRawPartitionWithSessionStrategy(
            string rawPath,
            EdlPartitionInfo part,
            double completedBytesBefore,
            double overallTotalBytes,
            long partTotalBytes)
        {
            ValidatePartitionWriteTarget(part, rawPath, false);
            EdlPartitionWriteStrategy learnedStrategy = GetPartitionWriteStrategy(part);
            if (learnedStrategy == EdlPartitionWriteStrategy.Unsupported)
                throw new Exception($"当前 Firehose 会话已确认不支持写入 {part.Label} (LUN{part.Lun})");

            if (UsesAdaptiveOplusVipProfile || ShouldUseProgramSpoofWrite(part))
            {
                WriteRawImageViaProgram(
                    rawPath,
                    part,
                    completedBytesBefore,
                    overallTotalBytes,
                    partTotalBytes,
                    forceVipMode: ShouldUseProgramSpoofWrite(part));
                return;
            }

            part.FilePath = rawPath;
            part.Sparse = false;
            TraceSharpEdlProgramIntent(part, rawPath, false);
            FirehoseServer!.WriteUnsparseImage(part).CheckAndThrow();
        }

        private void WriteSparsePartitionWithSessionStrategy(
            string sparsePath,
            EdlPartitionInfo part,
            double completedBytesBefore,
            double overallTotalBytes,
            long partTotalBytes)
        {
            ValidatePartitionWriteTarget(part, sparsePath, true);
            if (!TryValidateAndroidSparseImage(sparsePath, out string? validationError))
            {
                throw new Exception(
                    $"分区 {part.Label} (LUN{part.Lun}) 的稀疏镜像无效: " +
                    $"{Path.GetFileName(sparsePath)} ({validationError})");
            }

            EdlPartitionWriteStrategy learnedStrategy = GetPartitionWriteStrategy(part);
            if (learnedStrategy == EdlPartitionWriteStrategy.Unsupported)
                throw new Exception($"当前 Firehose 会话已确认不支持写入 {part.Label} (LUN{part.Lun})");

            bool useSpoofWrite = ShouldUseProgramSpoofWrite(part);
            WriteSparseImageAsRawViaProgram(
                sparsePath,
                part,
                completedBytesBefore,
                overallTotalBytes,
                partTotalBytes,
                forceVipMode: useSpoofWrite);
        }

        private static bool IsProtectedEdlWritePartitionName(string? partitionName)
        {
            if (string.IsNullOrWhiteSpace(partitionName))
                return false;
            return partitionName.Equals("userdata", StringComparison.OrdinalIgnoreCase)
                || partitionName.Equals("frp", StringComparison.OrdinalIgnoreCase)
                || partitionName.Equals("super", StringComparison.OrdinalIgnoreCase)
                || partitionName.StartsWith("super_", StringComparison.OrdinalIgnoreCase)
                || partitionName.Equals("last_parti", StringComparison.OrdinalIgnoreCase)
                || partitionName.Equals("ocdt", StringComparison.OrdinalIgnoreCase)
                || partitionName.Equals("oplusreserve4", StringComparison.OrdinalIgnoreCase)
                || partitionName.Equals("oplusreserve5", StringComparison.OrdinalIgnoreCase)
                || partitionName.Equals("param", StringComparison.OrdinalIgnoreCase)
                || partitionName.Equals("preisp_otp", StringComparison.OrdinalIgnoreCase)
                || partitionName.Equals("preisp_dt", StringComparison.OrdinalIgnoreCase)
                || partitionName.Equals("preisp_dt_bk", StringComparison.OrdinalIgnoreCase);
        }

        public void FlashWithoutProgram(IList<EdlPartitionInfo> selected, string? overridePrefix = null, bool suppressDoneLog = false)
        {
            Debug.Assert(FirehoseServer != null);
            if (selected.Count == 0)
                throw new Exception("请先选择要刷写的分区");
            ObserveDevicePartitions(selected);

            var plan = new List<(EdlPartitionInfo Part, string? Path, bool LooksSparse, long TotalBytes)>(selected.Count);
            foreach (var item in selected)
            {
                string originalPath = item.FilePath ?? "";
                string? fullPath = null;
                if (!string.IsNullOrWhiteSpace(originalPath))
                {
                    try
                    {
                        string candidatePath = Path.GetFullPath(originalPath);
                        if (File.Exists(candidatePath))
                            fullPath = candidatePath;
                    }
                    catch
                    {
                    }
                }

                if (fullPath == null)
                {
                    plan.Add((item, null, false, 0));
                    continue;
                }

                bool looksSparse = LooksLikeAndroidSparse(fullPath);
                long totalBytes = GetPlannedFlashBytes(fullPath, looksSparse);
                plan.Add((item, fullPath, looksSparse, totalBytes));
            }

            double totalBytesSum = plan.Sum(x => (double)Math.Max(0, x.TotalBytes));
            bool useCountFallback = totalBytesSum <= 0;
            double overallTotalBytes = useCountFallback ? plan.Count : totalBytesSum;

            double completedBytesBefore = 0;
            int failedCount = 0;

            for (int i = 0; i < plan.Count; i++)
            {
                ThrowIfStopRequested();
                var (item, fullPath, looksSparse, partTotalBytesLong) = plan[i];
                double completedBytesBeforeLocal = completedBytesBefore;
                long partTotalBytes = useCountFallback
                    ? Math.Max(1, partTotalBytesLong)
                    : Math.Max(0, partTotalBytesLong);
                _updateCurrentPartitionText(item.Label);
                _updateProgress(0);
                if (string.IsNullOrWhiteSpace(fullPath))
                {
                    failedCount++;
                    LogMissingPartitionImage(item);
                    completedBytesBefore += partTotalBytes;
                    _updateProgress(100);
                    continue;
                }

                item.FilePath = fullPath;
                EventHandler<(long, long)> progressHandler = (sender, e) =>
                {
                    long current = e.Item1;
                    long total = e.Item2;
                    double fraction = total <= 0 ? 0 : Math.Clamp(current / (double)total, 0, 1);
                    _updateProgress(Math.Clamp(fraction * 100.0, 0, 100));
                };
                FirehoseServer.ProgressChanged += progressHandler;
                long lastUnits = 0;
                long lastTick = Stopwatch.GetTimestamp();
                long lastSpeedUiTick = 0;
                long bytesPerUnit = 1;
                bool bytesPerUnitKnown = false;
                EventHandler<(long, long)> speedHandler = (sender, e) =>
                {
                    long currentUnits = e.Item1;
                    long totalUnits = e.Item2;
                    if (!bytesPerUnitKnown && totalUnits > 0)
                    {
                        bytesPerUnit = GuessBytesPerProgressUnit(fullPath, looksSparse, totalUnits);
                        bytesPerUnitKnown = true;
                    }
                    long now = Stopwatch.GetTimestamp();
                    long dtTicks = now - lastTick;
                    if (dtTicks > 0)
                    {
                        double dt = dtTicks / (double)Stopwatch.Frequency;
                        long deltaUnits = currentUnits - lastUnits;
                        if (deltaUnits >= 0 && now - lastSpeedUiTick >= Stopwatch.Frequency / 5)
                        {
                            double bytesPerSecond = (deltaUnits * (double)bytesPerUnit) / dt;
                            UpdateSmoothedTransferSpeed(bytesPerSecond);
                            lastSpeedUiTick = now;
                        }
                    }
                    lastUnits = currentUnits;
                    lastTick = now;
                };
                FirehoseServer.ProgressChanged += speedHandler;
                try
                {
                    string flashLinePrefix = string.IsNullOrWhiteSpace(overridePrefix)
                        ? FormatEdlPartitionWriteLine(fullPath, $"LUN{item.Lun}", item.Label)
                        : overridePrefix;
                    _log($"__EDL_FLASH_BEGIN__|{flashLinePrefix}", null);
                    if (looksSparse)
                    {
                        item.Sparse = true;
                        WriteSparsePartitionWithSessionStrategy(
                            fullPath,
                            item,
                            completedBytesBeforeLocal,
                            overallTotalBytes,
                            partTotalBytes);
                    }
                    else
                    {
                        item.Sparse = false;
                        WriteRawPartitionWithSessionStrategy(
                            fullPath,
                            item,
                            completedBytesBeforeLocal,
                            overallTotalBytes,
                            partTotalBytes);
                    }

                    _log("__EDL_FLASH_OK__", null);
                }
                catch (Exception ex)
                {
                    failedCount++;
                    LogPartitionWriteFailure(item, ex);
                }
                finally
                {
                    FirehoseServer.ProgressChanged -= progressHandler;
                    FirehoseServer.ProgressChanged -= speedHandler;
                }

                completedBytesBefore += partTotalBytes;
                _updateProgress(100);
            }
            ThrowIfStopRequested();
            if (!suppressDoneLog)
                _log("写分区流程结束...", null);
            _updateSpeedText(null);
            _updateCurrentPartitionText(null);
        }

        private bool TryReadDiskSectorCountForLun(int lun, int sectorSize, out long numDiskSectors)
        {
            numDiskSectors = 0;
            try
            {
                byte[]? gptData = ReadPrimaryGptPrefix(
                    lun,
                    8,
                    finalAckTimeoutMs: 1500,
                    acceptCompleteDataWithoutFinalAck: true);
                return gptData != null && TryReadNumDiskSectorsFromGptBytes(gptData, sectorSize, out numDiskSectors) && numDiskSectors > 0;
            }
            catch (Exception ex)
            {
                _appendNativeLog($"[Warn] Unable to read LUN{lun} disk sectors before format: {ex.Message}");
                if (IsSessionBreakingFailure(ex))
                    throw;
                return false;
            }
        }

        private static long GetKnownLunEndSector(IEnumerable<EdlPartitionInfo> knownPartitions, int lun)
        {
            long endSector = 0;
            foreach (var part in knownPartitions.Where(p => p.Lun == lun))
            {
                if (!TryParseLongAllowHex(part.StartSector, out long startSector))
                    continue;
                long sectorLen = Math.Max(0, part.SectorLen);
                long partEnd = sectorLen > long.MaxValue - startSector ? long.MaxValue : startSector + sectorLen;
                if (partEnd > endSector)
                    endSector = partEnd;
            }
            return endSector;
        }

        private long WipeLunGptTable(int lun, long diskSectors, int sectorSize)
        {
            int primarySectors = (int)Math.Min(GptReadSectors, Math.Max(1, diskSectors));
            byte[] primaryZeros = new byte[primarySectors * sectorSize];
            bool useVipLabel = _useOplusReadWithoutSpoof || UsesAdaptiveOplusVipProfile;
            if (!WriteSectorsWithVip(lun, 0, primaryZeros, primaryZeros.Length, useVipLabel, "gpt"))
                throw new Exception($"LUN{lun} 主 GPT 清空失败");

            long wipedBytes = primaryZeros.Length;
            if (diskSectors <= primarySectors)
                return wipedBytes;

            int backupSectors = (int)Math.Min(GptReadSectors, diskSectors);
            long backupStart = Math.Max(0, diskSectors - backupSectors);
            byte[] backupZeros = new byte[backupSectors * sectorSize];
            if (!WriteSectorsWithVip(
                    lun,
                    backupStart,
                    backupZeros,
                    backupZeros.Length,
                    useVipLabel,
                    "BackupGPT"))
                throw new Exception($"LUN{lun} 备份 GPT 清空失败");

            wipedBytes += backupZeros.Length;
            return wipedBytes;
        }

        private const int EdlPartitionHeaderWipeBytes = 1024 * 1024;

        private static bool IsEdlGptPseudoPartition(EdlPartitionInfo partition)
        {
            string label = partition.Label ?? string.Empty;
            return label.Equals("PrimaryGPT", StringComparison.OrdinalIgnoreCase)
                || label.Equals("BackupGPT", StringComparison.OrdinalIgnoreCase)
                || label.StartsWith("gpt_main", StringComparison.OrdinalIgnoreCase)
                || label.StartsWith("gpt_backup", StringComparison.OrdinalIgnoreCase);
        }

        private long WipePartitionHeader(EdlPartitionInfo partition)
        {
            if (!long.TryParse(
                    partition.StartSector,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long startSector))
                throw new Exception($"无法解析 {partition.Label} 的起始扇区");

            int sectorSize = partition.BytesPerSector > 0
                ? partition.BytesPerSector
                : (_firehoseSectorSize > 0 ? _firehoseSectorSize : 4096);
            long partitionSectors = partition.SectorLen;
            if (partitionSectors <= 0)
                throw new Exception($"{partition.Label} 分区大小无效");

            int maxHeaderSectors = Math.Max(1, EdlPartitionHeaderWipeBytes / sectorSize);
            int sectorsToWipe = checked((int)Math.Min(partitionSectors, maxHeaderSectors));
            byte[] zeros = new byte[checked(sectorsToWipe * sectorSize)];
            _appendNativeLog(
                $"[EraseHeader] {partition.Label} LUN{partition.Lun} start={startSector} " +
                $"sectors={sectorsToWipe} bytes={zeros.Length}");
            bool useVipLabel = _useOplusReadWithoutSpoof || UsesAdaptiveOplusVipProfile;
            if (!WriteSectorsWithVip(
                    partition.Lun,
                    startSector,
                    zeros,
                    zeros.Length,
                    useVipLabel,
                    partition.Label))
                throw new Exception($"{partition.Label} 头部擦除失败 @ sector {startSector}");

            return zeros.Length;
        }

        public void DoFormatLuns(IList<EdlPartitionInfo> knownPartitions, IList<int> luns)
        {
            Debug.Assert(FirehoseServer != null);
            var targetLuns = (luns ?? Array.Empty<int>())
                .Distinct()
                .OrderBy(lun => lun)
                .ToList();
            if (targetLuns.Count == 0)
                throw new Exception("请先选择要格式化的 LUN");

            int sectorSize = _firehoseSectorSize > 0 ? _firehoseSectorSize : 4096;
            IList<EdlPartitionInfo> partitionSnapshot = knownPartitions ?? Array.Empty<EdlPartitionInfo>();

            foreach (int lun in targetLuns)
            {
                ThrowIfStopRequested();
                _updateCurrentPartitionText($"LUN{lun}");
                _updateProgress(0);

                bool hasDiskSectors = TryReadDiskSectorCountForLun(lun, sectorSize, out long diskSectors);
                if (!hasDiskSectors || diskSectors <= 0)
                    diskSectors = GetKnownLunEndSector(partitionSnapshot, lun);
                if (diskSectors <= 0)
                    throw new Exception($"无法确定 LUN{lun} 的擦除范围");

                List<EdlPartitionInfo> lunPartitions = partitionSnapshot
                    .Where(partition => partition.Lun == lun)
                    .Where(partition => !IsEdlGptPseudoPartition(partition))
                    .Where(partition => partition.SectorLen > 0)
                    .GroupBy(partition => new { partition.StartSector, partition.SectorLen })
                    .Select(group => group.First())
                    .OrderBy(partition =>
                        long.TryParse(
                            partition.StartSector,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out long startSector)
                            ? startSector
                            : long.MaxValue)
                    .ToList();

                int totalSteps = Math.Max(1, lunPartitions.Count + 1);
                int completedSteps = 0;
                if (lunPartitions.Count > 0)
                {
                    _log($"__EDL_FLASH_BEGIN__|擦除LUN{lun}分区头部...", null);
                    try
                    {
                        foreach (EdlPartitionInfo partition in lunPartitions)
                        {
                            ThrowIfStopRequested();
                            _updateCurrentPartitionText($"{partition.Label} (LUN{lun})");
                            WipePartitionHeader(partition);
                            completedSteps++;
                            _updateProgress(completedSteps * 100.0 / totalSteps);
                        }
                        _log("__EDL_FLASH_OK__", null);
                    }
                    catch (OperationCanceledException) when (IsStopRequested)
                    {
                        throw;
                    }
                    catch
                    {
                        _log("__EDL_FLASH_ERROR__", null);
                        throw;
                    }
                }
                else
                {
                    _appendNativeLog($"[EraseHeader] LUN{lun} has no parsed partitions; skipping partition headers");
                }

                _updateCurrentPartitionText($"LUN{lun} GPT");
                ThrowIfStopRequested();
                try
                {
                    WipeLunGptTable(lun, diskSectors, sectorSize);
                    _log($"擦除LUN{lun} GPT... Done", null);
                }
                catch
                {
                    _log($"擦除LUN{lun} GPT... Error", "error");
                    throw;
                }
                finally
                {
                    _updateProgress(100);
                }
                ThrowIfStopRequested();
            }

            _log("操作线程结束...", null);
            _updateSpeedText(null);
            _updateCurrentPartitionText(null);
        }

        public void DoErasePartitionHeaders(IList<EdlPartitionInfo> selected)
        {
            Debug.Assert(FirehoseServer != null);
            if (selected.Count == 0)
                throw new Exception("请先选择要擦除头部的分区");

            foreach (EdlPartitionInfo item in selected)
            {
                ThrowIfStopRequested();
                if (item.SectorLen <= 0)
                {
                    _appendNativeLog(
                        $"[EraseHeader] skip {item.Label} LUN{item.Lun}: invalid partition size {item.SectorLen}");
                    _log($"{item.Label} 分区大小无效，已跳过", "warn");
                    continue;
                }

                _updateCurrentPartitionText(item.Label);
                _updateProgress(0);
                _log($"__EDL_FLASH_BEGIN__|[擦除头部]{item.Label}...", null);
                try
                {
                    WipePartitionHeader(item);
                    _updateProgress(100);
                    _log("__EDL_FLASH_OK__", null);
                }
                catch
                {
                    _log("__EDL_FLASH_ERROR__", null);
                    throw;
                }
                ThrowIfStopRequested();
            }

            _log("[Done]选中分区头部擦除成功.", null);
            _updateSpeedText(null);
            _updateCurrentPartitionText(null);
        }
        public void DoEraseOperation(IList<EdlPartitionInfo> selected, bool silent = false)
        {
            Debug.Assert(FirehoseServer != null);
            if (selected.Count == 0)
                throw new Exception("请先选择要擦除的分区");
            for (int i = 0; i < selected.Count; i++)
            {
                ThrowIfStopRequested();
                var item = selected[i];
                _updateCurrentPartitionText(item.Label);
                _updateProgress(0);
                if (!silent)
                    _log($"__EDL_FLASH_BEGIN__|[擦除]{item.Label}...", null);
                try
                {
                    FirehoseServer.ErasePartition(item);
                    if (!silent)
                        _log("__EDL_FLASH_OK__", null);
                }
                catch
                {
                    if (!silent)
                        _log("__EDL_FLASH_ERROR__", null);
                    throw;
                }
                _updateProgress(100);
                ThrowIfStopRequested();
            }
            if (!silent)
                _log("[Done]选中分区擦除成功.", null);
            _updateCurrentPartitionText(null);
        }

        private void DoManualAuth()
        {
            throw new NotSupportedException("暂未实现手动 EDL Auth，请关闭该选项使用 Mi NoAuth。");
        }

        private void DoNoAuth()
        {
            Debug.Assert(FirehoseServer != null);
            _log("进行Mi NoAuth", null);
            MiAuth auth = new MiAuth(FirehoseServer);
            if (auth.BypassAuth())
                _log("Mi NoAuth成功", null);
            else
                throw new Exception("Mi NoAuth失败");
        }

        private (string Output, string Error, int ExitCode, bool TimedOut) RunProcessWithTimeout(
            ProcessStartInfo startInfo,
            int timeoutMs,
            string? progressFilePath = null,
            long progressTotalBytes = 0,
            Action<string>? outputLineReceived = null)
        {
            StringBuilder outputBuilder = new StringBuilder();
            StringBuilder errorBuilder = new StringBuilder();
            bool traceNative = MainWindow.IsEdlNativeProcess(startInfo.FileName);
            Stopwatch sw = Stopwatch.StartNew();
            if (traceNative)
            {
                _appendNativeLog($"[PROC] {Path.GetFileName(startInfo.FileName)} {startInfo.Arguments}");
                _appendNativeLog($"[PROC] cwd={startInfo.WorkingDirectory}");
                _appendNativeLog($"[PROC] timeout={timeoutMs}ms");
            }
            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        outputBuilder.AppendLine(e.Data);
                        if (traceNative)
                            _appendNativeLog("[OUT] " + e.Data);
                        outputLineReceived?.Invoke(e.Data);
                    }
                };
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        errorBuilder.AppendLine(e.Data);
                        if (traceNative)
                            _appendNativeLog("[ERR] " + e.Data);
                        outputLineReceived?.Invoke(e.Data);
                    }
                };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                long lastProgressBytes = -1;
                long lastSpeedBytes = 0;
                long lastSpeedTick = Stopwatch.GetTimestamp();
                long lastSpeedUiTick = 0;
                while (true)
                {
                    if (process.WaitForExit(250))
                    {
                        UpdateFileProgress(
                            progressFilePath,
                            progressTotalBytes,
                            ref lastProgressBytes,
                            ref lastSpeedBytes,
                            ref lastSpeedTick,
                            ref lastSpeedUiTick,
                            true);
                        break;
                    }

                    UpdateFileProgress(
                        progressFilePath,
                        progressTotalBytes,
                        ref lastProgressBytes,
                        ref lastSpeedBytes,
                        ref lastSpeedTick,
                        ref lastSpeedUiTick,
                        false);

                    if (sw.ElapsedMilliseconds >= timeoutMs)
                        break;
                }
                bool exited = process.HasExited;
                if (!exited)
                {
                    process.Kill(true);
                    process.WaitForExit();
                    sw.Stop();
                    if (traceNative)
                    {
                        _appendNativeLog($"[ERR] process timeout after {sw.ElapsedMilliseconds}ms");
                        string output = outputBuilder.ToString().Trim();
                        string error = errorBuilder.ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(output))
                            _appendNativeLog("[OUT/SNAPSHOT] " + output.Replace("\r", "").Replace("\n", "\\n"));
                        if (!string.IsNullOrWhiteSpace(error))
                            _appendNativeLog("[ERR/SNAPSHOT] " + error.Replace("\r", "").Replace("\n", "\\n"));
                    }
                    return (outputBuilder.ToString(), errorBuilder.ToString(), -1, true);
                }
                // WaitForExit(timeout) can return before redirected output callbacks finish.
                process.WaitForExit();
                sw.Stop();
                if (traceNative)
                    _appendNativeLog($"[PROC] exit={process.ExitCode}, elapsed={sw.ElapsedMilliseconds}ms");
                return (outputBuilder.ToString(), errorBuilder.ToString(), process.ExitCode, false);
            }
        }

        private void UpdateFileProgress(
            string? progressFilePath,
            long progressTotalBytes,
            ref long lastProgressBytes,
            ref long lastSpeedBytes,
            ref long lastSpeedTick,
            ref long lastSpeedUiTick,
            bool force)
        {
            if (string.IsNullOrWhiteSpace(progressFilePath) || progressTotalBytes <= 0)
                return;

            long currentBytes;
            try
            {
                currentBytes = File.Exists(progressFilePath)
                    ? new FileInfo(progressFilePath).Length
                    : 0;
            }
            catch
            {
                return;
            }

            currentBytes = Math.Clamp(currentBytes, 0, progressTotalBytes);
            if (force || currentBytes != lastProgressBytes)
            {
                _updateProgress(Math.Clamp(currentBytes * 100.0 / progressTotalBytes, 0, 100));
                lastProgressBytes = currentBytes;
            }

            long now = Stopwatch.GetTimestamp();
            if (!force && now - lastSpeedUiTick < Stopwatch.Frequency / 2)
                return;

            long elapsedTicks = now - lastSpeedTick;
            if (elapsedTicks <= 0)
                return;

            double seconds = elapsedTicks / (double)Stopwatch.Frequency;
            long deltaBytes = currentBytes - lastSpeedBytes;
            if (deltaBytes >= 0 && seconds > 0)
                UpdateSmoothedTransferSpeed(deltaBytes / seconds);

            lastSpeedBytes = currentBytes;
            lastSpeedTick = now;
            lastSpeedUiTick = now;
        }

        private enum EdlPortProtocolState
        {
            Unknown,
            Sahara,
            Firehose
        }

        private EdlPortProtocolState ProbeEdlPortProtocolState(string portName, int timeoutMs = 4000)
        {
            if (string.IsNullOrEmpty(portName))
                throw new Exception("没有设备连接");
            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "QSaharaServer.exe");
            if (!File.Exists(exePath))
                throw new Exception("未找到 QSaharaServer.exe，请确认程序目录");

            var probeWatch = Stopwatch.StartNew();
            for (int attempt = 0; attempt < 1; attempt++)
            {
                ThrowIfStopRequested();
                int remainingMs = timeoutMs - (int)probeWatch.ElapsedMilliseconds;
                if (remainingMs <= 0)
                {
                    _appendNativeLog("[PORT-PROBE] timed out; protocol state is unknown");
                    return EdlPortProtocolState.Unknown;
                }

                ProcessStartInfo startInfo = new ProcessStartInfo(
                    exePath,
                    $"-p {NormalizeEdlPortArg(portName)} -d")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory
                };
                var result = RunProcessWithTimeout(startInfo, remainingMs);
                string combinedOutput = result.Output + "\n" + result.Error;
                string? portStatus = TryExtractQsaharaTag(combinedOutput, "portstatus");
                if (string.Equals(portStatus, "sahara", StringComparison.OrdinalIgnoreCase))
                {
                    string? saharaVersion = TryExtractQsaharaTag(combinedOutput, "saharaversion");
                    string? serial = TryExtractQsaharaTag(combinedOutput, "serial");
                    _appendNativeLog(
                        $"[PORT-PROBE] state=sahara, version={saharaVersion ?? "unknown"}, "
                        + $"serial={serial ?? "unknown"}");
                    if (!string.IsNullOrWhiteSpace(saharaVersion))
                        _log($"Sahara版本：{saharaVersion}", null);
                    if (!string.IsNullOrWhiteSpace(serial))
                        _log($"设备序列号：{serial}", null);
                    return EdlPortProtocolState.Sahara;
                }
                if (string.Equals(portStatus, "firehose", StringComparison.OrdinalIgnoreCase)
                    || combinedOutput.Contains("FIREHOSE MODE DETECTED", StringComparison.OrdinalIgnoreCase)
                    || combinedOutput.Contains("不支持FIREHOSE模式下通讯", StringComparison.OrdinalIgnoreCase))
                {
                    _appendNativeLog("[PORT-PROBE] state=firehose");
                    return EdlPortProtocolState.Firehose;
                }

                ThrowIfNativePortOpenFailed(combinedOutput, portName);
                if (result.TimedOut)
                    _appendNativeLog("[PORT-PROBE] timed out; protocol state is unknown");
                else
                    _appendNativeLog($"[PORT-PROBE] state=unknown, exit={result.ExitCode}");
                return EdlPortProtocolState.Unknown;
            }

            return EdlPortProtocolState.Unknown;
        }

        private static string? TryExtractQsaharaTag(string output, string tag)
        {
            if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(tag))
                return null;
            var m = Regex.Match(
                output,
                @"\[" + Regex.Escape(tag) + @"\]\s*([^\r\n]+)",
                RegexOptions.IgnoreCase);
            if (!m.Success)
                return null;
            return m.Groups[1].Value.Trim();
        }

        private static bool ContainsAnyIgnoreCase(string text, params string[] patterns)
        {
            return patterns.Any(pattern =>
                text.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        private static bool LooksLikeDeviceRejectedLoader(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return false;

            return ContainsAnyIgnoreCase(
                output,
                "SAHARA_NAK_HASH_TABLE_AUTH_FAILURE",
                "SAHARA_NAK_HASH_VERIFICATION_FAILURE",
                "HASH_TABLE_AUTH_FAILURE",
                "hash table verification failed",
                "image authentication failed",
                "authentication failure",
                "secure boot verification failed",
                "Digitally Signed Image was rejected",
                "image was rejected by the target");
        }

        private static bool LooksLikeMismatchedLoader(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return false;

            return ContainsAnyIgnoreCase(
                output,
                "SAHARA_NAK_INVALID_IMAGE_TYPE",
                "SAHARA_NAK_INVALID_DEST_ADDR",
                "SAHARA_NAK_UNSUPPORTED_CMD",
                "SAHARA_NAK_INVALID_ELF_HDR",
                "SAHARA_NAK_INVALID_ELF_HEADER",
                "SAHARA_NAK_INVALID_IMAGE_HEADER_SIZE",
                "SAHARA_NAK_INVALID_IMAGE_DATA_SIZE",
                "SAHARA_NAK_HASH_TABLE_NOT_FOUND",
                "SAHARA_NAK_UNEXPECTED_IMAGE_ID",
                "unsupported sahara version",
                "protocol mismatch",
                "target type mismatch",
                "no mapping for image",
                "invalid ELF",
                "invalid programmer image",
                "failed to load image",
                "requested image id was not found");
        }

        private static bool LooksLikeMissingSaharaHello(string output)
        {
            if (string.IsNullOrWhiteSpace(output)
                || !output.Contains("SAHARA_WAIT_HELLO", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // This QSahara build can emit a transient read-timeout before the
            // target sends HELLO. Do not let that early warning override later
            // protocol progress or a completed transfer.
            if (ContainsAnyIgnoreCase(
                    output,
                    "SAHARA_HELLO_RESPONSE",
                    "SAHARA_READ_DATA",
                    "Requested ID",
                    "File transferred successfully",
                    "Successfully uploaded all images"))
            {
                return false;
            }

            return ContainsAnyIgnoreCase(
                output,
                "[portstatus]error",
                "从读取端口超时",
                "Unable to read packet header",
                "Only read 0 bytes",
                "Sahara protocol error");
        }

        private Exception CreateLoaderSendFailure(string output, int exitCode, string stage)
        {
            if (LooksLikeDeviceRejectedLoader(output))
            {
                _appendNativeLog($"[SAHARA] classification=device-rejected, stage={stage}, exit={exitCode}");
                return new Exception("设备拒绝了引导文件，请检查引导文件是否与设备匹配.");
            }

            if (LooksLikeMismatchedLoader(output))
            {
                _appendNativeLog($"[SAHARA] classification=loader-mismatch, stage={stage}, exit={exitCode}");
                return new Exception("引导不匹配：当前 Firehose 文件与设备平台或 Image ID 不匹配");
            }

            _appendNativeLog($"[SAHARA] classification=transport-or-unknown, stage={stage}, exit={exitCode}");
            return new Exception($"QSaharaServer {stage}失败，退出码: {exitCode}");
        }

        private bool RunFhLoaderSendProgrammer(string portName, string programmerPath)
        {
            return RunQSaharaSendProgrammer(portName, programmerPath);
        }

        private bool RunQSaharaSendProgrammer(string portName, string programmerPath)
        {
            if (string.IsNullOrEmpty(portName))
                throw new Exception("没有设备连接");
            if (string.IsNullOrEmpty(programmerPath) || !File.Exists(programmerPath))
                throw new Exception("引导文件不存在");
            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "QSaharaServer.exe");
            if (!File.Exists(exePath))
                throw new Exception("未找到 QSaharaServer.exe，请确认程序目录");
            Match portMatch = Regex.Match(portName, @"COM(\d+)", RegexOptions.IgnoreCase);
            if (!portMatch.Success)
                throw new Exception("无法解析端口号: " + portName);
            string portNumber = portMatch.Groups[1].Value;

            IReadOnlyList<SaharaProgrammerImage>? manifestImages = null;
            string saharaArguments;
            long loaderBytes;
            int transferTimeoutMs;
            if (SaharaProgrammerManifest.IsManifest(programmerPath))
            {
                manifestImages = SaharaProgrammerManifest.Load(programmerPath);
                saharaArguments = SaharaProgrammerManifest.BuildArguments(manifestImages);
                loaderBytes = manifestImages.Aggregate(
                    0L,
                    (total, image) => checked(total + new FileInfo(image.FilePath).Length));
                transferTimeoutMs = 45000;
                _appendNativeLog($"[SAHARA] multi-image manifest: {programmerPath}");
                foreach (SaharaProgrammerImage image in manifestImages)
                {
                    _appendNativeLog(
                        $"[SAHARA] image_id={image.ImageId}, file={image.FilePath}");
                }
            }
            else
            {
                saharaArguments = $"-s 13:\"{programmerPath}\"";
                loaderBytes = new FileInfo(programmerPath).Length;
                transferTimeoutMs = 15000;
            }

            object manifestProgressSync = new object();
            SaharaProgrammerImage? activeManifestImage = null;
            HashSet<int> confirmedManifestImageIds = new HashSet<int>();

            void ConfirmActiveManifestImage()
            {
                if (activeManifestImage == null
                    || !confirmedManifestImageIds.Add(activeManifestImage.ImageId))
                {
                    return;
                }

                _log(
                    $"发送引导文件：{Path.GetFileName(activeManifestImage.FilePath)} "
                    + $"(Image ID {activeManifestImage.ImageId})...Done",
                    null);
            }

            void HandleSaharaTransferOutput(string line)
            {
                if (manifestImages == null || string.IsNullOrWhiteSpace(line))
                    return;

                lock (manifestProgressSync)
                {
                    Match requestMatch = Regex.Match(
                        line,
                        "Requested\\s+ID\\s+(\\d+)\\s*,\\s*file\\s*:\\s*\"([^\"]+)\"",
                        RegexOptions.IgnoreCase);
                    if (requestMatch.Success
                        && int.TryParse(requestMatch.Groups[1].Value, out int imageId))
                    {
                        activeManifestImage = manifestImages.FirstOrDefault(image => image.ImageId == imageId);
                    }

                    if (line.Contains("Still More images to be uploaded", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("Successfully uploaded all images", StringComparison.OrdinalIgnoreCase))
                    {
                        ConfirmActiveManifestImage();
                    }
                }
            }

            (string Output, string Error, int ExitCode, bool TimedOut) RunTransfer(string arguments)
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(exePath, arguments)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory
                };
                return RunProcessWithTimeout(
                    startInfo,
                    transferTimeoutMs,
                    outputLineReceived: HandleSaharaTransferOutput);
            }

            static bool IsFirehoseOutput(string output)
            {
                return output.Contains("[portstatus]firehose", StringComparison.OrdinalIgnoreCase)
                    || output.Contains("FIREHOSE MODE DETECTED", StringComparison.OrdinalIgnoreCase)
                    || output.Contains("不支持FIREHOSE模式下通讯", StringComparison.OrdinalIgnoreCase);
            }

            static bool IsTransferSuccessful(
                (string Output, string Error, int ExitCode, bool TimedOut) result,
                bool requireAllImages = false)
            {
                string combinedOutput = result.Output + "\n" + result.Error;
                if (result.TimedOut || LooksLikeDeviceRejectedLoader(combinedOutput))
                    return false;
                if (requireAllImages)
                {
                    return combinedOutput.Contains(
                        "Successfully uploaded all images",
                        StringComparison.OrdinalIgnoreCase);
                }

                return result.ExitCode == 0
                    || combinedOutput.Contains("File transferred successfully", StringComparison.OrdinalIgnoreCase);
            }

            Stopwatch transferWatch = StartLoaderSendProgress(
                programmerPath,
                loaderBytes,
                transferTimeoutMs,
                out System.Threading.Timer progressTimer,
                manifestImages == null ? null : $"发送多文件引导（{manifestImages.Count}个文件）");
            bool progressCompleted = false;
            try
            {
                if (!WaitForAvailableEdlPort(portName, 1500))
                    throw MainWindow.CreateEdlPortOpenFailedException(portName);
                bool isMultiImage = manifestImages != null;
                string primaryArgs = isMultiImage
                    ? $"-p {NormalizeEdlPortArg(portName)} {saharaArguments}"
                    : $"-u {portNumber} {saharaArguments}";
                var result = RunTransfer(primaryArgs);
                string combined = result.Output + "\n" + result.Error;
                if (IsFirehoseOutput(combined))
                {
                    _log("端口已处于FireHose模式，跳过发送引导", "success");
                    return false;
                }
                if (result.TimedOut)
                {
                    // Some QSahara builds keep running after the programmer has already
                    // switched the target to Firehose. Let the caller probe Firehose
                    // instead of starting a second Sahara process on the same port.
                    _appendNativeLog("[SAHARA] transfer process timed out; probing Firehose before retrying Sahara");
                    return false;
                }

                if (LooksLikeMissingSaharaHello(combined))
                {
                    throw new EdlReadFailureException(
                        "Sahara 会话已失效：发送引导时未收到设备 HELLO",
                        true);
                }

                if (LooksLikeDeviceRejectedLoader(combined)
                    || LooksLikeMismatchedLoader(combined))
                {
                    throw CreateLoaderSendFailure(combined, result.ExitCode, "发送引导");
                }

                if (!isMultiImage && !IsTransferSuccessful(result))
                {
                    _appendNativeLog(
                        $"[SAHARA] USB transport failed (exit={result.ExitCode}, timeout={result.TimedOut}); "
                        + "waiting for the 9008 port before serial transport retry");
                    if (!WaitForAvailableEdlPort(portName, 3000))
                        throw MainWindow.CreateEdlPortOpenFailedException(portName);
                    Thread.Sleep(150);

                    string serialArgs = $"-p {NormalizeEdlPortArg(portName)} {saharaArguments}";
                    result = RunTransfer(serialArgs);
                    combined = result.Output + "\n" + result.Error;
                    if (IsFirehoseOutput(combined))
                    {
                        _log("端口已处于FireHose模式，跳过发送引导", "success");
                        return false;
                    }
                    if (LooksLikeDeviceRejectedLoader(combined)
                        || LooksLikeMismatchedLoader(combined))
                    {
                        throw CreateLoaderSendFailure(combined, result.ExitCode, "发送引导");
                    }
                }

                if (!IsTransferSuccessful(result, isMultiImage))
                {
                    ThrowIfNativePortOpenFailed(combined, portName);
                    if (result.TimedOut)
                        ThrowLoaderSendTimeout();
                    throw CreateLoaderSendFailure(combined, result.ExitCode, "发送引导");
                }

                FinishLoaderSendProgress(progressTimer, transferWatch, true);
                progressCompleted = true;
                if (manifestImages == null)
                {
                    _log("发送引导...Done", null);
                }
                else if (confirmedManifestImageIds.Count != manifestImages.Count)
                {
                    _log($"发送多文件引导（{manifestImages.Count}个文件）...Done", null);
                }
                return true;
            }
            finally
            {
                if (!progressCompleted)
                    FinishLoaderSendProgress(progressTimer, transferWatch, false);
            }
        }

        private bool WaitForAvailableEdlPort(string portName, int timeoutMs)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (true)
            {
                ThrowIfStopRequested();
                if (IsEdlPortReadyForExternalAccess(portName))
                    return true;

                if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                    return false;

                int delayMs = Math.Min(150, Math.Max(1, timeoutMs - (int)stopwatch.ElapsedMilliseconds));
                Thread.Sleep(delayMs);
            }
        }

        private bool WaitForStableEdlPortAfterProgrammer(string portName, int timeoutMs)
        {
            const int pollIntervalMs = 100;
            const int stableReadyMs = 400;
            Stopwatch stopwatch = Stopwatch.StartNew();
            long readySinceMs = -1;

            _appendNativeLog($"[OPLUS] programmer sent; waiting for stable port {portName}");
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                ThrowIfStopRequested();
                if (IsEdlPortReadyForExternalAccess(portName))
                {
                    if (readySinceMs < 0)
                        readySinceMs = stopwatch.ElapsedMilliseconds;
                    if (stopwatch.ElapsedMilliseconds - readySinceMs >= stableReadyMs)
                    {
                        _appendNativeLog(
                            $"[OPLUS] port {portName} stable after programmer, elapsed={stopwatch.ElapsedMilliseconds}ms");
                        Thread.Sleep(pollIntervalMs);
                        return true;
                    }
                }
                else
                {
                    readySinceMs = -1;
                }

                Thread.Sleep(Math.Min(
                    pollIntervalMs,
                    Math.Max(1, timeoutMs - (int)stopwatch.ElapsedMilliseconds)));
            }

            _appendNativeLog(
                $"[OPLUS] port {portName} did not become stable after programmer within {timeoutMs}ms");
            return false;
        }

        private static bool IsRetryableQsaharaStartFailure(
            (string Output, string Error, int ExitCode, bool TimedOut) result)
        {
            if (result.TimedOut || result.ExitCode == 0)
                return false;

            string output = result.Output + "\n" + result.Error;
            return MainWindow.LooksLikeNativePortOpenFailed(output)
                || output.Contains("Unable to read packet header", StringComparison.OrdinalIgnoreCase)
                || output.Contains("Only read 0 bytes", StringComparison.OrdinalIgnoreCase)
                || output.Contains("从读取端口超时", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeEdlPortArg(string portName)
        {
            return portName.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase)
                ? portName
                : @"\\.\" + portName;
        }

        private bool RunOplusQsaharaSendProgrammer(string portName, string programmerPath)
        {
            if (string.IsNullOrEmpty(portName))
                throw new Exception("没有设备连接");
            if (string.IsNullOrEmpty(programmerPath) || !File.Exists(programmerPath))
                throw new Exception("OPLUS firehose 文件不存在");

            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "QSaharaServer.exe");
            if (!File.Exists(exePath))
                throw new Exception("未找到 QSaharaServer.exe，请确认程序目录");

            string args = $"-p {NormalizeEdlPortArg(portName)} -s 13:\"{programmerPath}\"";
            var fileInfo = new FileInfo(programmerPath);
            _appendNativeLog($"[OPLUS] send firehose port={NormalizeEdlPortArg(portName)}");
            _appendNativeLog($"[OPLUS] firehose={programmerPath}");
            _appendNativeLog($"[OPLUS] firehose_size={fileInfo.Length} bytes");
            ProcessStartInfo CreateStartInfo() => new ProcessStartInfo(exePath, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory
            };
            long loaderBytes = fileInfo.Length;
            const int timeoutMs = 15000;
            Stopwatch transferWatch = StartLoaderSendProgress(programmerPath, loaderBytes, timeoutMs, out System.Threading.Timer progressTimer);
            bool progressCompleted = false;
            try
            {
                var sendWatch = Stopwatch.StartNew();
                var result = RunProcessWithTimeout(CreateStartInfo(), timeoutMs);
                string combined = result.Output + "\n" + result.Error;
                if (LooksLikeMissingSaharaHello(combined))
                {
                    throw new EdlReadFailureException(
                        "Sahara 会话已失效：发送引导时未收到设备 HELLO",
                        true);
                }
                if (IsRetryableQsaharaStartFailure(result))
                {
                    int remainingMs = timeoutMs - (int)sendWatch.ElapsedMilliseconds;
                    if (remainingMs > 150)
                    {
                        WaitForAvailableEdlPort(portName, Math.Min(500, remainingMs - 150));
                        remainingMs = timeoutMs - (int)sendWatch.ElapsedMilliseconds;
                        if (remainingMs > 150)
                        {
                            _appendNativeLog(
                                $"[OPLUS] transient Sahara start failure (exit={result.ExitCode}); retrying after 150ms");
                            Thread.Sleep(150);
                            remainingMs = timeoutMs - (int)sendWatch.ElapsedMilliseconds;
                            if (remainingMs > 0)
                            {
                                result = RunProcessWithTimeout(CreateStartInfo(), remainingMs);
                                combined = result.Output + "\n" + result.Error;
                            }
                        }
                    }
                }

                ThrowIfNativePortOpenFailed(combined, portName);
                if (result.TimedOut)
                {
                    ThrowLoaderSendTimeout();
                }
                if (combined.Contains("[portstatus]firehose", StringComparison.OrdinalIgnoreCase)
                    || combined.Contains("FIREHOSE MODE DETECTED", StringComparison.OrdinalIgnoreCase)
                    || combined.Contains("不支持FIREHOSE模式下通讯", StringComparison.OrdinalIgnoreCase))
                {
                    _log("端口已处于FireHose模式，跳过发送引导", "success");
                    return false;
                }
                if (LooksLikeDeviceRejectedLoader(combined)
                    || LooksLikeMismatchedLoader(combined))
                {
                    throw CreateLoaderSendFailure(combined, result.ExitCode, "发送 OPLUS firehose");
                }
                if (result.ExitCode != 0)
                    throw CreateLoaderSendFailure(combined, result.ExitCode, "发送 OPLUS firehose");
                FinishLoaderSendProgress(progressTimer, transferWatch, true);
                progressCompleted = true;
                return true;
            }
            finally
            {
                if (!progressCompleted)
                    FinishLoaderSendProgress(progressTimer, transferWatch, false);
            }
        }

        private (string Output, string Error, int ExitCode, bool TimedOut) RunOplusFhLoaderOnce(
            string args,
            string workingDirectory,
            int timeoutMs = 20000)
        {
            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fh_loader.exe");
            if (!File.Exists(exePath))
                throw new Exception("未找到 fh_loader.exe，请确认程序目录");
            Directory.CreateDirectory(workingDirectory);

            ProcessStartInfo startInfo = new ProcessStartInfo(exePath, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            };
            return RunProcessWithTimeout(startInfo, timeoutMs);
        }

        private void RunOplusFhLoader(string args, string errorPrefix, string workingDirectory, int timeoutMs = 20000)
        {
            var result = RunOplusFhLoaderOnce(args, workingDirectory, timeoutMs);
            ThrowIfNativePortOpenFailed(result.Output + "\n" + result.Error, _currentPortName);
            if (!result.TimedOut && result.ExitCode != 0)
            {
                Thread.Sleep(600);
                result = RunOplusFhLoaderOnce(args, workingDirectory, timeoutMs);
                ThrowIfNativePortOpenFailed(result.Output + "\n" + result.Error, _currentPortName);
            }
            if (result.TimedOut)
                throw new Exception(errorPrefix + "超时");
            if (result.ExitCode != 0)
                throw new Exception($"{errorPrefix}失败，退出码: {result.ExitCode}: {result.Output}{Environment.NewLine}{result.Error}");
        }

        private static string GetFhLoaderSkipConfigureArg(string exePath)
        {
            if (FhLoaderSupportsOption(exePath, "skip_configure"))
                return "--skip_configure";
            if (FhLoaderSupportsOption(exePath, "noautoconfigure"))
                return "--noautoconfigure";
            return "";
        }

        private void RunOplusSignedDigest(string portName, string filePath, string logText)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                throw new Exception(logText + "文件不存在");

            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fh_loader.exe");
            string configureArg = GetFhLoaderSkipConfigureArg(exePath);
            string portArg = NormalizeEdlPortArg(portName);
            string outputDir = Path.GetDirectoryName(filePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            Directory.CreateDirectory(outputDir);
            string args = $"--port={portArg} --signeddigests=\"{Path.GetFileName(filePath)}\" --testvipimpact --noprompt {configureArg} --mainoutputdir=.\\";
            var result = RunOplusFhLoaderOnce(args, outputDir);
            string combined = result.Output + "\n" + result.Error;
            ThrowIfNativePortOpenFailed(combined, portName);

            bool targetFinalSuccess =
                combined.Contains("verify passed.", StringComparison.OrdinalIgnoreCase)
                || combined.Contains("verify signature ok", StringComparison.OrdinalIgnoreCase)
                || combined.Contains("{All Finished Successfully}", StringComparison.OrdinalIgnoreCase);
            bool targetRejectedData =
                !targetFinalSuccess
                && (combined.Contains("verify signature failed", StringComparison.OrdinalIgnoreCase)
                    || combined.Contains("Digitally Signed Digest Table was rejected", StringComparison.OrdinalIgnoreCase)
                    || combined.Contains("verify failed.", StringComparison.OrdinalIgnoreCase));
            bool targetAcceptedData =
                targetFinalSuccess
                || combined.Contains("VIP is enabled, receiving the partition info", StringComparison.OrdinalIgnoreCase)
                || combined.Contains("devprg_svip_check_permission ok", StringComparison.OrdinalIgnoreCase);
            if (result.TimedOut)
                throw new Exception(logText + "超时");
            if (targetRejectedData)
                throw new Exception("设备拒绝了引导文件，请检查引导文件是否与设备匹配.");
            if (result.ExitCode != 0 && !targetAcceptedData)
                throw new Exception($"{logText}失败，退出码: {result.ExitCode}: {result.Output}{Environment.NewLine}{result.Error}");

            if (result.ExitCode != 0)
                _appendNativeLog($"[OPLUS] {logText} host exit={result.ExitCode}, target already accepted data; skip retry");
            _log(logText, null);
        }

        private void RunOplusSendXml(string portName, string xmlPath, string xmlContent, string logText)
        {
            string dir = Path.GetDirectoryName(xmlPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            Directory.CreateDirectory(dir);
            File.WriteAllText(xmlPath, xmlContent, Encoding.UTF8);

            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fh_loader.exe");
            string configureArg = GetFhLoaderSkipConfigureArg(exePath);
            string portArg = NormalizeEdlPortArg(portName);
            string args = $"--port={portArg} --sendxml=\"{Path.GetFileName(xmlPath)}\" --noprompt {configureArg} --mainoutputdir=.\\";
            RunOplusFhLoader(args, logText, dir);
            _log(logText, null);
        }

        private void RunOplusConfigure(string portName, string workDir)
        {
            Directory.CreateDirectory(workDir);
            string xmlPath = Path.Combine(workDir, "configure.xml");
            string xmlContent = "<?xml version=\"1.0\" ?><data><configure MemoryName=\"ufs\" Verbose=\"0\" AlwaysValidate=\"0\" MaxDigestTableSizeInBytes=\"8192\" MaxPayloadSizeToTargetInBytes=\"1048576\" ZlpAwareHost=\"1\" SkipStorageInit=\"0\" /></data>";
            File.WriteAllText(xmlPath, xmlContent, Encoding.UTF8);

            string portArg = NormalizeEdlPortArg(portName);
            string args = $"--port={portArg} --memoryname=ufs --configure=\"{Path.GetFileName(xmlPath)}\" --search_path=.\\ --mainoutputdir=.\\ --noprompt";
            RunOplusFhLoader(args, "配置 OPLUS Firehose", workDir);
        }

        private bool RunOplusSignedFirehoseSequence(string portName, EdlOplusLoaderPackage package)
        {
            if (package == null)
                throw new Exception("OPLUS 引导包为空");

            string workDir = string.IsNullOrWhiteSpace(package.WorkingDirectory)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp")
                : package.WorkingDirectory;
            Directory.CreateDirectory(workDir);

            if (!RunOplusQsaharaSendProgrammer(portName, package.FirehosePath))
                return false;
            _log("发送引导...Done", null);
            if (!WaitForStableEdlPortAfterProgrammer(portName, 5000))
            {
                throw new EdlReadFailureException(
                    "发送引导后端口未重新就绪，已停止发送 Digest",
                    true);
            }
            RunOplusSignedDigest(
                portName,
                package.DigestPath,
                "发送Digest...Done");
            RunOplusSendXml(
                portName,
                Path.Combine(workDir, "oplus_verify.xml"),
                "<?xml version=\"1.0\"?><data><verify value=\"ping\" EnableVip=\"1\"/></data>",
                "VIP验证...Done");
            RunOplusSignedDigest(portName, package.SignPath, "发送Sign...Done");
            RunOplusSendXml(
                portName,
                Path.Combine(workDir, "oplus_sha256init.xml"),
                "<?xml version=\"1.0\"?><data><sha256init Verbose=\"1\"/></data>",
                "SHA256初始化...OK");
            RunOplusConfigure(portName, workDir);
            _log("进入FireHose...Done", null);
            _log("配置设备...Done", null);
            _log("准备读取分区表...", null);
            return true;
        }

        private QCResponse GetDeviceConfigWithTimeout(int timeoutMs, int retryCount)
        {
            Debug.Assert(FirehoseServer != null);
            for (int attempt = 0; attempt <= retryCount; attempt++)
            {
                FirehoseServer sessionServer = FirehoseServer
                    ?? throw new EdlReadFailureException("获取设备配置失败：Firehose 会话不存在", true);
                long sessionGeneration = CurrentSessionGeneration;
                Task<QCResponse> task = Task.Run(() => sessionServer.GetDeviceConfig());
                Task completedTask = Task.WhenAny(task, Task.Delay(timeoutMs)).GetAwaiter().GetResult();
                if (ReferenceEquals(completedTask, task))
                {
                    QCResponse result;
                    try
                    {
                        result = task.GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        throw new EdlReadFailureException(
                            $"获取设备配置失败：{ex.Message}",
                            true,
                            ex);
                    }

                    if (!IsCurrentSession(sessionGeneration, sessionServer))
                    {
                        throw new EdlReadFailureException(
                            $"获取设备配置失败：会话 {sessionGeneration} 已被替换，已丢弃旧结果",
                            true);
                    }
                    return result;
                }

                _ = task.ContinueWith(
                    completed => _ = completed.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                string timeoutReason =
                    $"获取设备配置超时：会话 {sessionGeneration} 在 {timeoutMs}ms 内未返回，旧会话已关闭";
                MarkFirehoseSessionFaulted(timeoutReason);
                if (attempt < retryCount)
                {
                    _log("获取设备配置超时，尝试重新打开端口", "warn");
                    string portName = _currentPortName
                        ?? throw new EdlReadFailureException("重新打开端口失败：端口名称为空", true);
                    _currentPort = new SerialPort(portName);
                    _currentPort.Open();
                    _currentPort.DtrEnable = true;
                    _currentPort.RtsEnable = true;
                    BeginFirehoseSession($"device config retry {attempt + 1}");
                    _saharaServer = new SaharaServer(_currentPort);
                    FirehoseServer = new FirehoseServer(_currentPort);
                    Thread.Sleep(300);
                }
            }
            throw new TimeoutException(
                $"获取设备配置超时：{timeoutMs}ms 内未收到有效响应，当前 Firehose 会话已作废");
        }

        public void PrepareForOperation(string loaderPath, bool sendLoader, bool manualAuth, EdlOplusLoaderPackage? oplusLoaderPackage = null)
        {
            try
            {
                ThrowIfStopRequested();
                if (string.IsNullOrEmpty(_currentPortName))
                    throw new Exception("没有设备连接");
                if (oplusLoaderPackage != null)
                    SetLoaderProfile(oplusLoaderPackage.FirehoseProfile, oplusLoaderPackage.AuthType);
                _deferLoaderSendToFirehoseProbe = false;
                _log($"__EDL_PORT_CONNECT_BEGIN__|{_currentPortName}", null);
                bool reuseOpenFirehose = HasOpenFirehoseSession;
                EdlPortProtocolState protocolState = EdlPortProtocolState.Unknown;

                // A checked "send loader" option must be verified against the device,
                // even when an old in-process Firehose session still appears open.
                if (sendLoader && reuseOpenFirehose)
                {
                    DisposeCurrentSerialPort();
                    reuseOpenFirehose = false;
                }
                if (oplusLoaderPackage != null || !reuseOpenFirehose)
                {
                    _useOplusReadWithoutSpoof = IsOplusReadWithoutSpoofLoader(loaderPath, oplusLoaderPackage);
                    _fastVipProgramWrite = IsFastVipProgramWriteLoader(loaderPath, oplusLoaderPackage);
                }

                if (reuseOpenFirehose)
                {
                    _log("__EDL_PORT_CONNECT_RESULT__|Firehose", null);
                    _log("端口已处于FireHose模式，跳过发送引导", "success");
                }
                else if (_currentPort != null)
                {
                    DisposeCurrentSerialPort();
                }

                if (!reuseOpenFirehose)
                {
                    if (!WaitForAvailableEdlPort(_currentPortName, 1500))
                        throw MainWindow.CreateEdlPortOpenFailedException(_currentPortName);
                    ThrowIfStopRequested();
                    protocolState = ProbeEdlPortProtocolState(_currentPortName);
                    _log(
                        $"__EDL_PORT_CONNECT_RESULT__|{protocolState switch
                        {
                            EdlPortProtocolState.Firehose => "Firehose",
                            EdlPortProtocolState.Sahara => "Sahara",
                            _ => "Unknown"
                        }}",
                        null);
                    if (protocolState == EdlPortProtocolState.Firehose)
                    {
                        _log("端口已处于FireHose模式，跳过发送引导", "success");
                        sendLoader = false;
                    }
                }
                if (string.IsNullOrEmpty(loaderPath) && sendLoader && oplusLoaderPackage == null)
                    throw new Exception("未选择引导");

                void OpenFirehosePort()
                {
                    Exception? openEx = null;
                    for (int attempt = 0; attempt < 10; attempt++)
                    {
                        ThrowIfStopRequested();
                        try
                        {
                            _currentPort = new SerialPort(_currentPortName);
                            _currentPort.Open();
                            openEx = null;
                            break;
                        }
                        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
                        {
                            openEx = ex;
                            DisposeCurrentSerialPort();
                            Thread.Sleep(150);
                        }
                    }
                    if (openEx != null)
                        throw openEx;
                    _currentPort!.DtrEnable = true;
                    _currentPort!.RtsEnable = true;

                    BeginFirehoseSession("operation port open");
                    _saharaServer = new SaharaServer(_currentPort!);
                    FirehoseServer = new FirehoseServer(_currentPort!);

                    Thread.Sleep(800);
                }

                void TrySendLoaderIfNeeded()
                {
                    ThrowIfStopRequested();
                    _setSendLoaderChecked(true);
                    try
                    {
                        if (oplusLoaderPackage != null)
                        {
                            if (protocolState != EdlPortProtocolState.Sahara)
                                _log("设备状态未知，尝试发送引导...", "warn");
                            if (!RunOplusSignedFirehoseSequence(_currentPortName, oplusLoaderPackage))
                                sendLoader = false;
                            return;
                        }

                        if (protocolState != EdlPortProtocolState.Sahara)
                            _log("设备状态未知，尝试发送引导...", "warn");
                        bool sent = RunFhLoaderSendProgrammer(_currentPortName, loaderPath);
                        if (!sent)
                            sendLoader = false;
                    }
                    catch (Exception ex)
                    {
                        if (ex.Message.Contains("FIREHOSE MODE DETECTED")
                            || ex.Message.Contains("不支持FIREHOSE模式下通讯"))
                        {
                            _log("__EDL_PORT_CONNECT_RESULT__|Firehose", null);
                            _log("端口已处于FireHose模式，跳过发送引导", "success");
                            sendLoader = false;
                        }
                        else
                        {
                            throw;
                        }
                    }
                    finally
                    {
                        _setSendLoaderChecked(false);
                    }
                    ThrowIfStopRequested();
                }

                if (!reuseOpenFirehose
                    && protocolState == EdlPortProtocolState.Unknown)
                {
                    // Running the same Sahara command again cannot help when the
                    // device never sent HELLO. Probe the already-active Firehose
                    // session instead; only report failure when both protocols
                    // are unresponsive.
                    _deferLoaderSendToFirehoseProbe = true;
                    sendLoader = false;
                    _setSendLoaderChecked(false);
                    _log("未收到 Sahara HELLO，尝试连接Firehose...", "warn");
                }
                else if (sendLoader)
                {
                    TrySendLoaderIfNeeded();
                }
                else if (!reuseOpenFirehose)
                {
                    if (protocolState == EdlPortProtocolState.Sahara)
                    {
                        sendLoader = true;
                        TrySendLoaderIfNeeded();
                    }
                    else if (protocolState == EdlPortProtocolState.Unknown)
                    {
                        _log("设备状态未知，尝试连接Firehose...", "warn");
                    }
                }

                if (!reuseOpenFirehose)
                    OpenFirehosePort();

                ThrowIfStopRequested();
                if (UsesZteFirehoseProfile)
                {
                    _log("配置 ZTE Firehose...", null);
                    try
                    {
                        ConfigureZteFirehoseProfile(_deferLoaderSendToFirehoseProbe ? 2500 : 8000);
                    }
                    catch (Exception ex) when (_deferLoaderSendToFirehoseProbe)
                    {
                        throw new EdlReadFailureException(
                            "无法识别设备状态，请重新进入端口后再试.",
                            true,
                            ex);
                    }
                    _log("配置 ZTE Firehose...OK", "success");
                }
                else
                {
                    _log("获取设备配置", null);
                    QCResponse response;
                    try
                    {
                        int configTimeoutMs = _deferLoaderSendToFirehoseProbe ? 2500 : 8000;
                        int configRetryCount = _deferLoaderSendToFirehoseProbe ? 0 : 1;
                        response = GetDeviceConfigWithTimeout(configTimeoutMs, configRetryCount);
                    }
                    catch (Exception ex) when (
                        _deferLoaderSendToFirehoseProbe
                        && ex.Message.Contains("获取设备配置超时", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new EdlReadFailureException(
                            "无法识别设备状态，请重新进入端口后再试.",
                            true,
                            ex);
                    }
                    catch (Exception ex) when (!sendLoader && ex.Message.Contains("获取设备配置超时", StringComparison.OrdinalIgnoreCase))
                    {
                        DisposeCurrentSerialPort();

                        sendLoader = true;
                        TrySendLoaderIfNeeded();

                        OpenFirehosePort();
                        _log("获取设备配置", null);
                        response = GetDeviceConfigWithTimeout(8000, 1);
                    }
                    if (response.Response != "ACK")
                    {
                        if (string.Join("", response.Logs).Contains("before authentication"))
                        {
                            if (manualAuth)
                                DoManualAuth();
                            else
                                DoNoAuth();
                            response = GetDeviceConfigWithTimeout(8000, 1);
                            if (response.Response != "ACK")
                                throw new Exception("设备认证后仍无法获取有效配置");
                        }
                        else
                        {
                            throw new Exception("获取设备配置失败，设备未返回 ACK");
                        }
                    }
                    ThrowIfStopRequested();
                    try
                    {
                        ConfigureFirehoseWithEdlStyle(8000);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Firehose 配置失败，已停止当前操作: " + ex.Message, ex);
                    }
                }
                ThrowIfStopRequested();
                string storage = GetDetectedMemoryName();
                if (string.IsNullOrWhiteSpace(storage)
                    || storage.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                {
                    storage = (FirehoseServer.MemoryName ?? "").ToUpperInvariant();
                }
                _log($"存储类型：{(string.IsNullOrWhiteSpace(storage) ? "Unknown" : storage)}", null);
            }
            catch (OperationCanceledException) when (IsStopRequested)
            {
                throw;
            }
            catch (Exception)
            {
                _log("__EDL_PORT_CONNECT_RESULT__|Error", null);
                DisposeCurrentSerialPort();
                throw;
            }
        }
    }
}
