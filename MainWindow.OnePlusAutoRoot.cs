using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Payload_Dumper_C_.Core;

namespace WpfApp1
{
    public partial class MainWindow
    {
        private const string Alpha28103DownloadUrl = "https://violettool.top/autoroot/Alpha28103.apk";
        private const string KernelSuManagerDownloadUrl = "https://violettool.top/autoroot/KernelSU_v3.3.0.apk";
        private const string SukiSuManagerDownloadUrl = "https://violettool.top/autoroot/SukiSU_v4.2.0.apk";
        private const string ReSukiSuManagerDownloadUrl = "https://violettool.top/autoroot/ReSukiSU.apk";
        private const string APatchManagerDownloadUrl = "https://violettool.top/autoroot/APatch.apk";
        private const string FolkPatchManagerDownloadUrl = "https://violettool.top/autoroot/FolkPatch.apk";
        private const string LegacyAutorootTrafficUsageFile = @"C:\Used.txt";
        private const string AutoRootCloudFileHint = "无需选择自动下载云端版本";
        private const string OfflineManagerFileHint = "留空则自动下载云端版本";
        private const string OfflineBootFileHint = "请选择initboot或boot文件";
        private const string KernelPatchBootFileHint = "请选择boot文件";
        private static readonly object AutorootTrafficUsageLock = new object();
        private static string? _resolvedAutorootTrafficUsageFile;
        private static readonly HttpClient OnePlusAutoRootHttpClient = new HttpClient();
        private CancellationTokenSource? _onePlusAutoRootCancellation;
        private bool _onePlusAutoRootCriticalFlash;
        private bool _autoRootModeUiActive;
        private string _offlinePatchMagiskPath = string.Empty;
        private string _offlinePatchBootPath = string.Empty;

        private async Task RunOnePlusAutoRootAsync()
        {
            if (_onePlusAutoRootCancellation != null)
            {
                return;
            }

            var startButton = btnAutoRoot;
            var cancelButton = this.FindName("btnCancelAutoRoot") as System.Windows.Controls.Button;
            if (startButton != null) startButton.IsEnabled = false;
            if (cancelButton != null) cancelButton.IsEnabled = true;
            if (OnePlusAutoRootModeRadioButton != null) OnePlusAutoRootModeRadioButton.IsEnabled = false;
            if (OfflinePatchModeRadioButton != null) OfflinePatchModeRadioButton.IsEnabled = false;
            SetPatchSchemeSelectionEnabled(false);

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string violetTmpRoot = Path.Combine(desktop, "VioletTmp");
            bool createdTmpRoot = !Directory.Exists(violetTmpRoot);
            string workDirectory = Path.Combine(violetTmpRoot, "AutoRoot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            string originalTempDirectory = tempDir;

            var cancellation = new CancellationTokenSource();
            _onePlusAutoRootCancellation = cancellation;
            bool homeDeviceDetectionPaused = false;
            try
            {
                Directory.CreateDirectory(workDirectory);
                UpdateAutorootProgress(0, "准备中");
                AppendAutorootLog("开始一加全自动ROOT...", "yellow");

                string adbPath = GetAdbPath();
                string fastbootPath = GetFastbootPath();

                AppendAutorootStage("01  设备识别");
                UpdateAutorootProgress(5, "检测ADB");
                string adbSerial = await GetSingleAdbSystemDeviceAsync(adbPath, cancellation.Token);
                AppendAutorootLog($"设备已连接【系统】: {adbSerial}", "green");

                string brand = await GetAdbPropertyAsync(adbPath, adbSerial, "ro.product.brand", cancellation.Token);
                if (!brand.Contains("OnePlus", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"当前设备品牌为 {brand}，不是 OnePlus 设备。");
                }
                AppendAutorootLog($"设备品牌: {brand}", "green");

                UpdateAutorootProgress(10, "识别机型");
                string deviceCode = await GetAdbPropertyAsync(adbPath, adbSerial, "ro.product.model", cancellation.Token);
                if (!C16DeviceCodeNameMap.TryGetValue(deviceCode, out string? deviceName) ||
                    string.IsNullOrWhiteSpace(deviceName) ||
                    !deviceName.Contains("一加", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"未在ROM设备字典中找到一加机型代号: {deviceCode}。");
                }
                AppendAutorootLog($"设备代号: {deviceCode}");
                AppendAutorootLog($"识别机型: {deviceName}", "green");

                string systemVersion = await GetAdbPropertyAsync(adbPath, adbSerial, "ro.build.display.id", cancellation.Token);
                if (string.IsNullOrWhiteSpace(systemVersion))
                {
                    throw new InvalidOperationException("无法读取当前系统版本。");
                }
                AppendAutorootLog($"系统版本: {systemVersion}", "green");

                UpdateAutorootProgress(15, "检查内核");
                string kernelVersion = await RunAdbShellTextAsync(adbPath, adbSerial, "uname -r", cancellation.Token);
                var kernel = ParseAutoRootKernel(kernelVersion);
                AppendAutorootLog($"内核版本: {kernelVersion}");

                string patchScheme = GetSelectedAutoRootPatchScheme();
                if (kernel.OnlyAlphaSupported && IsLkmPatchScheme(patchScheme))
                {
                    MagiskAlphaPatchRadioButton.IsChecked = true;
                    patchScheme = "Alpha";
                    AppendAutorootLog("内核版本小于等于 5.4，仅支持 Alpha，已自动切换。", "yellow");
                }
                bool isKernelPatch = IsKernelPatchScheme(patchScheme);
                string partitionName = isKernelPatch ? "boot" : kernel.PartitionName;
                AppendAutorootLog($"需要提取并修补: {partitionName}.img", "green");

                string? lkmKmi = IsLkmPatchScheme(patchScheme)
                    ? ParseAutoRootKmi(kernelVersion)
                    : null;
                if (lkmKmi != null)
                {
                    AppendAutorootLog($"LKM KMI: {lkmKmi}", "green");
                }

                AppendAutorootStage("02  云提取Rom");
                UpdateAutorootProgress(20, "查询ROM");
                var rom = await FindOnePlusRomAsync(deviceCode, deviceName, systemVersion, cancellation.Token);
                AppendAutorootLog($"已匹配ROM: {rom.Series} / {rom.Device} / {rom.Version}", "green");
                string resolveRouteDisplayName = GetRomResolveRouteDisplayName(rom.ResolveRoute);
                if (!string.IsNullOrWhiteSpace(resolveRouteDisplayName))
                {
                    AppendAutorootLog($"ROM链接解析: {resolveRouteDisplayName}", "green");
                }

                string imagesDirectory = Path.Combine(workDirectory, "images");
                Directory.CreateDirectory(imagesDirectory);
                string sourceImage = await ExtractAutoRootPartitionAsync(
                    rom.DownloadUrl,
                    rom.RequestHeaders,
                    partitionName,
                    imagesDirectory,
                    cancellation.Token);
                if (!File.Exists(sourceImage))
                {
                    throw new FileNotFoundException($"未生成 {partitionName}.img。", sourceImage);
                }
                SetAutoRootGeneratedPaths(sourceImage, null);
                AppendAutorootLog($"分区提取完成: {sourceImage}", "green");

                AppendAutorootStage("03  镜像修补", $"{patchScheme} · {partitionName}.img");
                var manager = GetAutoRootManagerInfo(patchScheme);
                UpdateAutorootProgress(60, $"下载{manager.DisplayName}");
                string managerApk = Path.Combine(workDirectory, manager.FileName);
                await DownloadAutoRootFileAsync(manager.DownloadUrl, managerApk, 60, 70, cancellation.Token);
                SetAutoRootGeneratedPaths(null, managerApk);
                AppendAutorootLog($"已下载 {manager.DisplayName}", "green");

                UpdateAutorootProgress(72, "脱机修补");
                string patchedImage = Path.Combine(workDirectory, partitionName + "_patched.img");
                if (string.Equals(patchScheme, "Alpha", StringComparison.OrdinalIgnoreCase))
                {
                    magiskbootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "magiskboot.exe");
                    if (!File.Exists(magiskbootPath))
                    {
                        throw new FileNotFoundException("未找到脱机修补所需的 magiskboot.exe。", magiskbootPath);
                    }
                    tempDir = workDirectory;
                    if (!await ExecuteMagiskPatchAsync(sourceImage, patchedImage, managerApk))
                    {
                        throw new InvalidOperationException("Alpha 脱机修补失败。");
                    }
                }
                else if (isKernelPatch)
                {
                    await ExecuteKernelPatchAsync(
                        sourceImage,
                        patchedImage,
                        patchScheme,
                        cancellation.Token);
                }
                else
                {
                    await ExecuteLkmPatchAsync(
                        sourceImage,
                        patchedImage,
                        patchScheme,
                        lkmKmi!,
                        cancellation.Token);
                }
                if (!File.Exists(patchedImage))
                {
                    throw new FileNotFoundException("修补流程未生成目标镜像。", patchedImage);
                }
                cancellation.Token.ThrowIfCancellationRequested();

                AppendAutorootStage("04  写入文件");
                if (_isDeviceDetectionEnabled)
                {
                    AppendAutorootLog("委托主页停止异步设备检测...");
                    HandleStopDeviceDetection(terminateRunningTools: false);
                    homeDeviceDetectionPaused = true;
                }
                cancellation.Token.ThrowIfCancellationRequested();

                UpdateAutorootProgress(80, "重启到刷写模式");
                bool useFastbootD = chkFastbootD?.IsChecked == true;
                string rebootTarget = useFastbootD ? "fastboot" : "bootloader";
                await EnsureAdbDeviceReadyForRebootAsync(adbPath, adbSerial, cancellation.Token);
                var rebootResult = await RunAutoRootToolAsync(
                    adbPath,
                    $"-s \"{adbSerial}\" reboot {rebootTarget}",
                    cancellation.Token);
                if (rebootResult.ExitCode != 0)
                {
                    throw new InvalidOperationException($"重启到{(useFastbootD ? "FastbootD" : "Fastboot")}失败: {rebootResult.ErrorText}");
                }

                string fastbootSerial = await WaitForSingleFastbootDeviceAsync(fastbootPath, TimeSpan.FromSeconds(60), cancellation.Token);
                AppendAutorootLog($"已连接【{(useFastbootD ? "FastbootD" : "Fastboot")}】: {fastbootSerial}", "green");

                UpdateAutorootProgress(84, "刷写前检查");
                await EnsureBootloaderUnlockedAsync(fastbootPath, fastbootSerial, cancellation.Token);
                AppendAutorootLog("分区校验通过", "green");
                AppendAutorootLog("等待3秒，确保设备连接稳定...", "yellow");
                await Task.Delay(TimeSpan.FromSeconds(3), cancellation.Token);

                UpdateAutorootProgress(88, "刷入镜像");
                _onePlusAutoRootCriticalFlash = true;
                if (cancelButton != null) cancelButton.IsEnabled = false;
                AutoRootCommandResult flashResult = await RunAutoRootToolAsync(
                    fastbootPath,
                    $"-s \"{fastbootSerial}\" flash {partitionName} \"{patchedImage}\"",
                    cancellation.Token);
                if (flashResult.ExitCode != 0)
                {
                    throw new InvalidOperationException($"刷入 {partitionName} 失败: {flashResult.ErrorText}");
                }
                AppendAutorootLog($"Flashing {partitionName}...OK", "green");

                UpdateAutorootProgress(92, "重启手机");
                var fastbootReboot = await RunAutoRootToolAsync(
                    fastbootPath,
                    $"-s \"{fastbootSerial}\" reboot",
                    cancellation.Token);
                _onePlusAutoRootCriticalFlash = false;
                if (cancelButton != null && !cancellation.IsCancellationRequested)
                {
                    cancelButton.IsEnabled = true;
                }
                if (fastbootReboot.ExitCode != 0)
                {
                    throw new InvalidOperationException($"重启手机失败: {fastbootReboot.ErrorText}");
                }

                AppendAutorootStage("05  安装ROOT管理器");
                UpdateAutorootProgress(95, "等待开机");
                await WaitForAndroidBootCompletedAsync(adbPath, adbSerial, TimeSpan.FromMinutes(8), cancellation.Token);
                AppendAutorootLog("设备已开机", "green");
                if (homeDeviceDetectionPaused)
                {
                    AppendAutorootLog("委托主页开始异步设备检测...");
                    HandleStartDeviceDetection();
                    homeDeviceDetectionPaused = false;
                    await RefreshDeviceStatusImmediatelyAsync(terminateRunningTools: true);
                }

                UpdateAutorootProgress(98, $"安装{manager.DisplayName}");
                string? installFailure = null;
                try
                {
                    var installResult = await RunAutoRootToolAsync(
                        adbPath,
                        $"-s \"{adbSerial}\" install -r \"{managerApk}\"",
                        cancellation.Token);
                    if (installResult.ExitCode != 0 ||
                        (!installResult.StandardOutput.Contains("Success", StringComparison.OrdinalIgnoreCase) &&
                         !installResult.StandardError.Contains("Success", StringComparison.OrdinalIgnoreCase)))
                    {
                        installFailure = installResult.ErrorText;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    installFailure = ex.Message;
                }

                if (string.IsNullOrWhiteSpace(installFailure))
                {
                    AppendAutorootLog($"{manager.DisplayName} 安装完成", "green");
                }
                else
                {
                    string? manualApkPath = TryPrepareManualManagerInstallation(managerApk, manager.FileName);
                    AppendAutorootLog(
                        $"{manager.DisplayName} 自动安装未完成，不影响ROOT结果: {installFailure}",
                        "yellow");
                    if (!string.IsNullOrWhiteSpace(manualApkPath))
                    {
                        AppendAutorootLog($"请手动安装管理器: {manualApkPath}", "yellow");
                        TryShowFileInExplorer(manualApkPath);
                    }
                    System.Windows.MessageBox.Show(
                        !string.IsNullOrWhiteSpace(manualApkPath)
                            ? $"ROOT镜像已刷入并成功开机。\n\n由于手机未开启USB安装、存在锁屏密码或系统限制，{manager.DisplayName} 未能自动安装。\n安装包已保存到：\n{manualApkPath}\n\n请手动安装即可。"
                            : $"ROOT镜像已刷入并成功开机。\n\n{manager.DisplayName} 未能自动安装，请手动下载安装管理器。",
                        "ROOT成功，管理器需要手动安装",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                UpdateAutorootProgress(100, "任务完成");
                AppendAutorootLog("一加全自动ROOT完成！", "green");
            }
            catch (OperationCanceledException)
            {
                UpdateAutorootProgress(0, "任务取消");
                AppendAutorootLog("一加全自动ROOT已取消。", "yellow");
            }
            catch (AutoRootRomUnavailableException)
            {
                UpdateAutorootProgress(0, "无可用ROM");
                const string message = "云端无可用Rom版本，请使用常规脱机修补模式";
                AppendAutorootLog(message, "yellow");
                System.Windows.MessageBox.Show(message, "一加全自动ROOT", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UpdateAutorootProgress(0, "任务失败");
                AppendAutorootLog($"一加全自动ROOT失败: {ex.Message}", "red");
                System.Windows.MessageBox.Show(ex.Message, "一加全自动ROOT", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (homeDeviceDetectionPaused)
                {
                    AppendAutorootLog("委托主页开始异步设备检测...");
                    HandleStartDeviceDetection();
                    homeDeviceDetectionPaused = false;
                }
                tempDir = originalTempDirectory;
                TryCleanupAutoRootDirectory(workDirectory, violetTmpRoot, createdTmpRoot);
                if (_autoRootModeUiActive)
                {
                    if (txtBootPath != null) txtBootPath.Text = AutoRootCloudFileHint;
                    if (txtMagiskPath != null) txtMagiskPath.Text = AutoRootCloudFileHint;
                }
                _onePlusAutoRootCriticalFlash = false;
                if (ReferenceEquals(_onePlusAutoRootCancellation, cancellation))
                {
                    _onePlusAutoRootCancellation = null;
                }
                cancellation.Dispose();
                if (cancelButton != null) cancelButton.IsEnabled = false;
                if (OnePlusAutoRootModeRadioButton != null) OnePlusAutoRootModeRadioButton.IsEnabled = true;
                if (OfflinePatchModeRadioButton != null) OfflinePatchModeRadioButton.IsEnabled = true;
                SetPatchSchemeSelectionEnabled(true);
                if (startButton != null) startButton.IsEnabled = true;
            }
        }

        private async Task RunOfflinePatchAutoFlashAsync()
        {
            if (_onePlusAutoRootCancellation != null) return;

            string sourceImage = txtBootPath?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sourceImage) || sourceImage == OfflineBootFileHint || !File.Exists(sourceImage))
            {
                System.Windows.MessageBox.Show("请先选择有效的 init_boot.img 或 boot.img。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string patchScheme = GetSelectedAutoRootPatchScheme();
            bool isKernelPatch = IsKernelPatchScheme(patchScheme);
            try
            {
                EnsureKernelPatchBootImage(sourceImage, patchScheme);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, patchScheme, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            const long initBootSize = 8L * 1024L * 1024L;
            long sourceSize = new FileInfo(sourceImage).Length;
            string partitionName = isKernelPatch
                ? "boot"
                : sourceSize == initBootSize ? "init_boot" : "boot";
            var manager = GetAutoRootManagerInfo(patchScheme);
            string selectedManagerApk = txtMagiskPath?.Text?.Trim() ?? string.Empty;
            bool downloadManager = string.IsNullOrWhiteSpace(selectedManagerApk) ||
                                   selectedManagerApk == OfflineManagerFileHint;
            if (!downloadManager && !File.Exists(selectedManagerApk))
            {
                System.Windows.MessageBox.Show("所选管理器安装包不存在。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var startButton = btnAutoRoot;
            var cancelButton = this.FindName("btnCancelAutoRoot") as System.Windows.Controls.Button;
            if (startButton != null) startButton.IsEnabled = false;
            if (cancelButton != null) cancelButton.IsEnabled = true;
            if (OnePlusAutoRootModeRadioButton != null) OnePlusAutoRootModeRadioButton.IsEnabled = false;
            if (OfflinePatchModeRadioButton != null) OfflinePatchModeRadioButton.IsEnabled = false;
            if (AutoFlashAndInstallCheckBox != null) AutoFlashAndInstallCheckBox.IsEnabled = false;
            if (btnSelectMagisk != null) btnSelectMagisk.IsEnabled = false;
            if (btnSelectBoot != null) btnSelectBoot.IsEnabled = false;
            SetPatchSchemeSelectionEnabled(false);

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string violetTmpRoot = Path.Combine(desktop, "VioletTmp");
            bool createdTmpRoot = !Directory.Exists(violetTmpRoot);
            string workDirectory = Path.Combine(violetTmpRoot, "OfflineAutoFlash_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            string originalTempDirectory = tempDir;
            var cancellation = new CancellationTokenSource();
            _onePlusAutoRootCancellation = cancellation;
            bool homeDeviceDetectionPaused = false;

            try
            {
                Directory.CreateDirectory(workDirectory);
                UpdateAutorootProgress(0, "准备中");
                AppendAutorootLog("开始常规修补、自动刷入并安装APK...", "yellow");
                AppendAutorootLog(
                    isKernelPatch
                        ? $"{patchScheme} 固定修补并刷写 boot 分区"
                        : $"根据镜像大小 {sourceSize / 1024d / 1024d:0.##}MB，识别刷写分区为 {partitionName}",
                    "green");

                string adbPath = GetAdbPath();
                string fastbootPath = GetFastbootPath();
                UpdateAutorootProgress(5, "检测ADB");
                string adbSerial = await GetSingleAdbSystemDeviceAsync(adbPath, cancellation.Token);
                AppendAutorootLog($"设备已连接【系统】: {adbSerial}", "green");

                AppendAutorootStage("01  管理器与镜像修补", $"{patchScheme} · {partitionName}.img");
                string managerApk = selectedManagerApk;
                if (downloadManager)
                {
                    UpdateAutorootProgress(10, $"下载{manager.DisplayName}");
                    managerApk = Path.Combine(workDirectory, manager.FileName);
                    await DownloadAutoRootFileAsync(manager.DownloadUrl, managerApk, 10, 25, cancellation.Token);
                    AppendAutorootLog($"已下载 {manager.DisplayName}", "green");
                }

                UpdateAutorootProgress(30, "脱机修补");
                string patchedImage = Path.Combine(workDirectory, partitionName + "_patched.img");
                if (string.Equals(patchScheme, "Alpha", StringComparison.OrdinalIgnoreCase))
                {
                    InitializeAutorootPaths();
                    tempDir = workDirectory;
                    if (!await ExecuteMagiskPatchAsync(sourceImage, patchedImage, managerApk))
                    {
                        throw new InvalidOperationException("Alpha 脱机修补失败。");
                    }
                }
                else if (isKernelPatch)
                {
                    await ExecuteKernelPatchAsync(
                        sourceImage,
                        patchedImage,
                        patchScheme,
                        cancellation.Token);
                }
                else
                {
                    string kmi = await ResolveOfflineLkmKmiAsync(
                        cancellation.Token,
                        adbPath,
                        adbSerial);
                    await ExecuteLkmPatchAsync(sourceImage, patchedImage, patchScheme, kmi, cancellation.Token);
                }
                if (!File.Exists(patchedImage) || new FileInfo(patchedImage).Length <= 0)
                {
                    throw new FileNotFoundException("修补流程未生成目标镜像。", patchedImage);
                }
                AppendAutorootLog($"修补完成: {patchedImage}", "green");
                cancellation.Token.ThrowIfCancellationRequested();

                AppendAutorootStage("02  设备刷写", "模式切换 · 解锁校验 · 安全刷入");
                if (_isDeviceDetectionEnabled)
                {
                    AppendAutorootLog("委托主页停止异步设备检测...");
                    HandleStopDeviceDetection(terminateRunningTools: false);
                    homeDeviceDetectionPaused = true;
                }
                cancellation.Token.ThrowIfCancellationRequested();

                UpdateAutorootProgress(60, "重启到刷写模式");
                bool useFastbootD = chkFastbootD?.IsChecked == true;
                string rebootTarget = useFastbootD ? "fastboot" : "bootloader";
                await EnsureAdbDeviceReadyForRebootAsync(adbPath, adbSerial, cancellation.Token);
                var rebootResult = await RunAutoRootToolAsync(
                    adbPath,
                    $"-s \"{adbSerial}\" reboot {rebootTarget}",
                    cancellation.Token);
                if (rebootResult.ExitCode != 0)
                {
                    throw new InvalidOperationException($"重启到{(useFastbootD ? "FastbootD" : "Fastboot")}失败: {rebootResult.ErrorText}");
                }

                string fastbootSerial = await WaitForSingleFastbootDeviceAsync(fastbootPath, TimeSpan.FromSeconds(60), cancellation.Token);
                AppendAutorootLog($"已连接【{(useFastbootD ? "FastbootD" : "Fastboot")}】: {fastbootSerial}", "green");
                UpdateAutorootProgress(70, "刷写前检查");
                await EnsureBootloaderUnlockedAsync(fastbootPath, fastbootSerial, cancellation.Token);
                AppendAutorootLog("等待3秒，确保设备连接稳定...", "yellow");
                await Task.Delay(TimeSpan.FromSeconds(3), cancellation.Token);

                UpdateAutorootProgress(78, $"刷入{partitionName}");
                _onePlusAutoRootCriticalFlash = true;
                if (cancelButton != null) cancelButton.IsEnabled = false;
                var flashResult = await RunAutoRootToolAsync(
                    fastbootPath,
                    $"-s \"{fastbootSerial}\" flash {partitionName} \"{patchedImage}\"",
                    cancellation.Token);
                if (flashResult.ExitCode != 0)
                {
                    throw new InvalidOperationException($"刷入 {partitionName} 失败: {flashResult.ErrorText}");
                }
                AppendAutorootLog($"Flashing {partitionName}...OK", "green");

                UpdateAutorootProgress(86, "重启手机");
                var fastbootReboot = await RunAutoRootToolAsync(
                    fastbootPath,
                    $"-s \"{fastbootSerial}\" reboot",
                    cancellation.Token);
                _onePlusAutoRootCriticalFlash = false;
                if (cancelButton != null && !cancellation.IsCancellationRequested) cancelButton.IsEnabled = true;
                if (fastbootReboot.ExitCode != 0)
                {
                    throw new InvalidOperationException($"重启手机失败: {fastbootReboot.ErrorText}");
                }

                AppendAutorootStage("03  收尾与安装", "等待开机 · 安装 ROOT 管理器");
                UpdateAutorootProgress(92, "等待开机");
                await WaitForAndroidBootCompletedAsync(adbPath, adbSerial, TimeSpan.FromMinutes(8), cancellation.Token);
                AppendAutorootLog("设备已开机", "green");
                if (homeDeviceDetectionPaused)
                {
                    AppendAutorootLog("委托主页开始异步设备检测...");
                    HandleStartDeviceDetection();
                    homeDeviceDetectionPaused = false;
                    await RefreshDeviceStatusImmediatelyAsync(terminateRunningTools: true);
                }

                UpdateAutorootProgress(97, $"安装{manager.DisplayName}");
                string? installFailure = null;
                try
                {
                    var installResult = await RunAutoRootToolAsync(
                        adbPath,
                        $"-s \"{adbSerial}\" install -r \"{managerApk}\"",
                        cancellation.Token);
                    if (installResult.ExitCode != 0 ||
                        (!installResult.StandardOutput.Contains("Success", StringComparison.OrdinalIgnoreCase) &&
                         !installResult.StandardError.Contains("Success", StringComparison.OrdinalIgnoreCase)))
                    {
                        installFailure = installResult.ErrorText;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    installFailure = ex.Message;
                }

                if (string.IsNullOrWhiteSpace(installFailure))
                {
                    AppendAutorootLog($"{manager.DisplayName} 安装完成", "green");
                }
                else
                {
                    string fallbackName = Path.GetFileName(managerApk);
                    string? manualApkPath = TryPrepareManualManagerInstallation(managerApk, fallbackName);
                    AppendAutorootLog($"{manager.DisplayName} 自动安装未完成，不影响ROOT结果: {installFailure}", "yellow");
                    if (!string.IsNullOrWhiteSpace(manualApkPath))
                    {
                        AppendAutorootLog($"请手动安装管理器: {manualApkPath}", "yellow");
                        TryShowFileInExplorer(manualApkPath);
                    }
                    System.Windows.MessageBox.Show(
                        !string.IsNullOrWhiteSpace(manualApkPath)
                            ? $"ROOT镜像已刷入并成功开机。\n\n管理器未能自动安装，安装包已保存到：\n{manualApkPath}\n\n请手动安装即可。"
                            : "ROOT镜像已刷入并成功开机。\n\n管理器未能自动安装，请手动安装。",
                        "ROOT成功，管理器需要手动安装",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                UpdateAutorootProgress(100, "任务完成");
                AppendAutorootLog("常规脱机修补、自动刷入及APK安装流程完成！", "green");
            }
            catch (OperationCanceledException)
            {
                UpdateAutorootProgress(0, "任务取消");
                AppendAutorootLog("自动刷入任务已取消。", "yellow");
            }
            catch (Exception ex)
            {
                UpdateAutorootProgress(0, "任务失败");
                AppendAutorootLog($"自动刷入任务失败: {ex.Message}", "red");
                System.Windows.MessageBox.Show(ex.Message, "自动刷入并安装APK", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (homeDeviceDetectionPaused)
                {
                    AppendAutorootLog("委托主页开始异步设备检测...");
                    HandleStartDeviceDetection();
                    homeDeviceDetectionPaused = false;
                }
                tempDir = originalTempDirectory;
                TryCleanupAutoRootDirectory(workDirectory, violetTmpRoot, createdTmpRoot);
                _onePlusAutoRootCriticalFlash = false;
                if (ReferenceEquals(_onePlusAutoRootCancellation, cancellation)) _onePlusAutoRootCancellation = null;
                cancellation.Dispose();
                if (downloadManager && txtMagiskPath != null) txtMagiskPath.Text = OfflineManagerFileHint;
                if (cancelButton != null) cancelButton.IsEnabled = false;
                if (OnePlusAutoRootModeRadioButton != null) OnePlusAutoRootModeRadioButton.IsEnabled = true;
                if (OfflinePatchModeRadioButton != null) OfflinePatchModeRadioButton.IsEnabled = true;
                if (AutoFlashAndInstallCheckBox != null) AutoFlashAndInstallCheckBox.IsEnabled = true;
                if (btnSelectMagisk != null) btnSelectMagisk.IsEnabled = true;
                if (btnSelectBoot != null) btnSelectBoot.IsEnabled = true;
                SetPatchSchemeSelectionEnabled(true);
                if (startButton != null) startButton.IsEnabled = true;
            }
        }

        private void PatchOperationMode_Checked(object sender, RoutedEventArgs e)
        {
            if (txtMagiskPath == null || txtBootPath == null ||
                btnSelectMagisk == null || btnSelectBoot == null)
            {
                return;
            }

            bool useAutoRoot = OnePlusAutoRootModeRadioButton?.IsChecked == true;
            if (useAutoRoot && !_autoRootModeUiActive)
            {
                _offlinePatchMagiskPath = txtMagiskPath.Text == OfflineManagerFileHint
                    ? string.Empty
                    : txtMagiskPath.Text;
                _offlinePatchBootPath = txtBootPath.Text == OfflineBootFileHint ||
                                        txtBootPath.Text == KernelPatchBootFileHint
                    ? string.Empty
                    : txtBootPath.Text;
                txtMagiskPath.Text = AutoRootCloudFileHint;
                txtBootPath.Text = AutoRootCloudFileHint;
            }
            else if (!useAutoRoot && _autoRootModeUiActive)
            {
                txtMagiskPath.Text = string.IsNullOrWhiteSpace(_offlinePatchMagiskPath)
                    ? OfflineManagerFileHint
                    : _offlinePatchMagiskPath;
                txtBootPath.Text = string.IsNullOrWhiteSpace(_offlinePatchBootPath)
                    ? GetOfflineBootFileHint()
                    : _offlinePatchBootPath;
            }

            _autoRootModeUiActive = useAutoRoot;
            UpdatePatchSchemeUiState();
        }

        private void InitializeAutoRootModeUiState()
        {
            _autoRootModeUiActive = OnePlusAutoRootModeRadioButton?.IsChecked == true;
            if (_autoRootModeUiActive)
            {
                if (txtMagiskPath != null) txtMagiskPath.Text = AutoRootCloudFileHint;
                if (txtBootPath != null) txtBootPath.Text = AutoRootCloudFileHint;
            }
            else
            {
                if (txtMagiskPath != null) txtMagiskPath.Text = OfflineManagerFileHint;
                if (txtBootPath != null) txtBootPath.Text = GetOfflineBootFileHint();
            }
            UpdatePatchSchemeUiState();
        }

        private void PatchScheme_Checked(object sender, RoutedEventArgs e)
        {
            UpdatePatchSchemeUiState();
        }

        private void UpdatePatchSchemeUiState()
        {
            string patchScheme = GetSelectedAutoRootPatchScheme();
            bool isLkm = IsLkmPatchScheme(patchScheme);
            bool isOfflineMode = OfflinePatchModeRadioButton?.IsChecked == true;

            if (LkmKmiPanel != null)
            {
                LkmKmiPanel.Visibility = isLkm
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                LkmKmiPanel.IsEnabled = isLkm && isOfflineMode;
            }
            if (AutoDetectKmiCheckBox != null)
            {
                AutoDetectKmiCheckBox.Visibility = isLkm && isOfflineMode
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                AutoDetectKmiCheckBox.IsHitTestVisible = isLkm && isOfflineMode;
                AutoDetectKmiCheckBox.Opacity = 1;
                if (!isLkm) AutoDetectKmiCheckBox.IsChecked = false;
            }

            if (!_autoRootModeUiActive && txtBootPath != null &&
                (txtBootPath.Text == OfflineBootFileHint || txtBootPath.Text == KernelPatchBootFileHint))
            {
                txtBootPath.Text = GetOfflineBootFileHint();
            }
        }

        private string GetOfflineBootFileHint()
        {
            return IsKernelPatchScheme(GetSelectedAutoRootPatchScheme())
                ? KernelPatchBootFileHint
                : OfflineBootFileHint;
        }

        private void SetPatchSchemeSelectionEnabled(bool enabled)
        {
            if (MagiskAlphaPatchRadioButton != null) MagiskAlphaPatchRadioButton.IsEnabled = enabled;
            if (KernelSuLkmPatchRadioButton != null) KernelSuLkmPatchRadioButton.IsEnabled = enabled;
            if (SukiSuLkmPatchRadioButton != null) SukiSuLkmPatchRadioButton.IsEnabled = enabled;
            if (ReSukiSuLkmPatchRadioButton != null) ReSukiSuLkmPatchRadioButton.IsEnabled = enabled;
            if (APatchPatchRadioButton != null) APatchPatchRadioButton.IsEnabled = enabled;
            if (FolkPatchPatchRadioButton != null) FolkPatchPatchRadioButton.IsEnabled = enabled;
        }

        private void BtnCancelAutoRoot_Click(object sender, RoutedEventArgs e)
        {
            if (_onePlusAutoRootCriticalFlash)
            {
                System.Windows.MessageBox.Show(
                    "正在写入分区，此阶段不能取消任务。",
                    "取消任务",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var cancellation = _onePlusAutoRootCancellation;
            if (cancellation == null || cancellation.IsCancellationRequested) return;

            var cancelButton = this.FindName("btnCancelAutoRoot") as System.Windows.Controls.Button;
            if (cancelButton != null) cancelButton.IsEnabled = false;
            UpdateAutorootProgress(AutorootProgressBar?.Value ?? 0, "正在取消");
            AppendAutorootLog("用户正在取消一加全自动ROOT任务...", "yellow");
            cancellation.Cancel();
        }

        private void SetAutoRootGeneratedPaths(string? imagePath, string? managerPath)
        {
            if (!string.IsNullOrWhiteSpace(imagePath) && txtBootPath != null)
            {
                txtBootPath.Text = imagePath;
            }
            if (!string.IsNullOrWhiteSpace(managerPath) && txtMagiskPath != null)
            {
                txtMagiskPath.Text = managerPath;
            }
        }

        private static string? TryPrepareManualManagerInstallation(string sourceApk, string fileName)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string target = Path.Combine(desktop, fileName);
                if (File.Exists(target))
                {
                    target = Path.Combine(
                        desktop,
                        Path.GetFileNameWithoutExtension(fileName) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + Path.GetExtension(fileName));
                }
                File.Copy(sourceApk, target, overwrite: false);
                return target;
            }
            catch
            {
                return null;
            }
        }

        private static void TryShowFileInExplorer(string filePath)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }

        private void EnsureAndRefreshAutorootTrafficUsage()
        {
            try
            {
                long bytes;
                lock (AutorootTrafficUsageLock)
                {
                    string usageFile = ResolveAutorootTrafficUsageFileUnsafe();
                    bytes = ReadAutorootTrafficUsageUnsafe(usageFile);
                }
                UpdateAutorootTrafficUsageText(bytes);
            }
            catch (Exception ex)
            {
                UpdateAutorootTrafficUsageText(0);
                AppendAutorootLog($"无法创建或读取流量记录文件: {ex.Message}", "yellow");
            }
        }

        private void AddAutorootTrafficUsage(long downloadedBytes)
        {
            if (downloadedBytes <= 0) return;
            try
            {
                long totalBytes;
                lock (AutorootTrafficUsageLock)
                {
                    string usageFile = ResolveAutorootTrafficUsageFileUnsafe();
                    long current = File.Exists(usageFile)
                        ? ReadAutorootTrafficUsageUnsafe(usageFile)
                        : 0;
                    totalBytes = current > long.MaxValue - downloadedBytes
                        ? long.MaxValue
                        : current + downloadedBytes;
                    WriteAutorootTrafficUsageUnsafe(usageFile, totalBytes);
                }
                UpdateAutorootTrafficUsageText(totalBytes);
            }
            catch (Exception ex)
            {
                AppendAutorootLog($"累计服务器流量失败: {ex.Message}", "yellow");
            }
        }

        private static string ResolveAutorootTrafficUsageFileUnsafe()
        {
            if (!string.IsNullOrWhiteSpace(_resolvedAutorootTrafficUsageFile))
            {
                try
                {
                    EnsureAutorootTrafficUsageFile(_resolvedAutorootTrafficUsageFile);
                    return _resolvedAutorootTrafficUsageFile;
                }
                catch
                {
                    // 路径权限或用户配置发生变化时，重新选择当前用户可写目录。
                    _resolvedAutorootTrafficUsageFile = null;
                }
            }

            string[] candidateRoots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            };
            Exception? lastError = null;
            foreach (string root in candidateRoots
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    string directory = Path.Combine(root, "VioletTool");
                    Directory.CreateDirectory(directory);
                    TrySetHiddenAttribute(directory);
                    string usageFile = Path.Combine(directory, "Used.txt");
                    InitializeAutorootTrafficUsageFileWithMigration(usageFile);
                    TrySetHiddenAttribute(usageFile);
                    _resolvedAutorootTrafficUsageFile = usageFile;
                    return usageFile;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            // 最后兜底到程序目录。若程序安装在 Program Files，此路径仍可能不可写，
            // 因此只在两个当前用户应用数据目录均不可用时尝试。
            try
            {
                string applicationDirectory = AppContext.BaseDirectory;
                if (string.IsNullOrWhiteSpace(applicationDirectory))
                {
                    throw new DirectoryNotFoundException("无法获取程序目录。");
                }

                string applicationUsageFile = Path.Combine(applicationDirectory, "Used.txt");
                InitializeAutorootTrafficUsageFileWithMigration(applicationUsageFile);
                TrySetHiddenAttribute(applicationUsageFile);
                _resolvedAutorootTrafficUsageFile = applicationUsageFile;
                return applicationUsageFile;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            throw new IOException("无法在用户应用数据目录或程序目录创建流量记录文件。", lastError);
        }

        private static void InitializeAutorootTrafficUsageFileWithMigration(string targetFile)
        {
            if (!File.Exists(targetFile))
            {
                long initialBytes = 0;
                string programDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                string programDataFile = string.IsNullOrWhiteSpace(programDataRoot)
                    ? string.Empty
                    : Path.Combine(programDataRoot, "VioletTool", "Used.txt");
                string publicDocumentsRoot = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
                string publicDocumentsFile = string.IsNullOrWhiteSpace(publicDocumentsRoot)
                    ? string.Empty
                    : Path.Combine(publicDocumentsRoot, "VioletTool", "Used.txt");
                string applicationDirectory = AppContext.BaseDirectory;
                string applicationFile = string.IsNullOrWhiteSpace(applicationDirectory)
                    ? string.Empty
                    : Path.Combine(applicationDirectory, "Used.txt");
                string[] legacyFiles =
                {
                    LegacyAutorootTrafficUsageFile,
                    programDataFile,
                    publicDocumentsFile,
                    applicationFile
                };

                foreach (string legacyFile in legacyFiles
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (!string.Equals(legacyFile, targetFile, StringComparison.OrdinalIgnoreCase) &&
                            File.Exists(legacyFile))
                        {
                            initialBytes = Math.Max(
                                initialBytes,
                                ReadAutorootTrafficUsageUnsafe(legacyFile));
                        }
                    }
                    catch
                    {
                        // 旧位置可能需要管理员权限；迁移失败不影响新记录文件使用。
                    }
                }

                WriteAutorootTrafficUsageUnsafe(targetFile, initialBytes);
            }
            EnsureAutorootTrafficUsageFile(targetFile);
        }

        private static void TrySetHiddenAttribute(string path)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.Hidden) == 0)
                {
                    File.SetAttributes(path, attributes | FileAttributes.Hidden);
                }
            }
            catch
            {
            }
        }

        private static void EnsureAutorootTrafficUsageFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                WriteAutorootTrafficUsageUnsafe(filePath, 0);
                return;
            }

            // 同时验证现有文件是否可读写，避免只通过 File.Exists 后仍在累计时失败。
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        }

        private static void WriteAutorootTrafficUsageUnsafe(string filePath, long bytes)
        {
            if (File.Exists(filePath))
            {
                FileAttributes attributes = File.GetAttributes(filePath);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
                }
            }

            byte[] content = System.Text.Encoding.UTF8.GetBytes(
                Math.Max(0, bytes).ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var stream = new FileStream(
                filePath,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.Read);
            stream.SetLength(0);
            stream.Write(content, 0, content.Length);
            stream.Flush(true);
        }

        private static long ReadAutorootTrafficUsageUnsafe(string usageFile)
        {
            string content = File.ReadAllText(usageFile).Trim();
            return long.TryParse(
                content,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out long bytes) && bytes >= 0
                ? bytes
                : 0;
        }

        private void UpdateAutorootTrafficUsageText(long bytes)
        {
            void Update()
            {
                var usageRun = this.FindName("AutorootTrafficUsedRun") as System.Windows.Documents.Run;
                if (usageRun == null) return;
                double megabytes = Math.Max(0, bytes) / 1024d / 1024d;
                usageRun.Text = megabytes.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "MB流量.";
                usageRun.Foreground = megabytes >= 500d
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69))
                    : megabytes >= 100d
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 126, 34))
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69));
            }

            if (Dispatcher.CheckAccess()) Update();
            else Dispatcher.Invoke(Update);
        }

        private void AutorootDonateLink_RequestNavigate(
            object sender,
            System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppendAutorootLog($"打开赞赏页面失败: {ex.Message}", "yellow");
            }
            e.Handled = true;
        }

        private async Task<string> GetSingleAdbSystemDeviceAsync(string adbPath, CancellationToken cancellationToken)
        {
            var result = await RunAutoRootToolAsync(adbPath, "devices", cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"adb devices 执行失败: {result.ErrorText}");
            }

            var devices = result.StandardOutput
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => Regex.Match(line.Trim(), @"^(\S+)\s+device$"))
                .Where(match => match.Success)
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (devices.Count == 0)
            {
                throw new InvalidOperationException("未检测到处于系统且已授权USB调试的设备。");
            }
            if (devices.Count > 1)
            {
                throw new InvalidOperationException("检测到多台ADB设备，请仅保留一台设备连接。");
            }
            return devices[0];
        }

        private async Task EnsureAdbDeviceReadyForRebootAsync(
            string adbPath,
            string serial,
            CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            bool waitingLogged = false;
            string lastError = string.Empty;

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AutoRootCommandResult result = await RunAutoRootToolAsync(
                    adbPath,
                    $"-s \"{serial}\" get-state",
                    cancellationToken);

                if (result.ExitCode == 0 &&
                    string.Equals(result.StandardOutput.Trim(), "device", StringComparison.OrdinalIgnoreCase))
                {
                    if (waitingLogged)
                    {
                        AppendAutorootLog("ADB设备连接已恢复", "green");
                    }
                    return;
                }

                lastError = string.Join(" ", new[] { result.ErrorText, result.StandardOutput }
                    .Where(text => !string.IsNullOrWhiteSpace(text)))
                    .Trim();
                if (!waitingLogged)
                {
                    AppendAutorootLog("等待ADB设备连接稳定...", "yellow");
                    waitingLogged = true;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
            }

            throw new InvalidOperationException(
                $"ADB设备未就绪，无法重启到刷写模式: {lastError}");
        }

        private async Task<string> GetAdbPropertyAsync(
            string adbPath,
            string serial,
            string property,
            CancellationToken cancellationToken)
        {
            string value = await RunAdbShellTextAsync(adbPath, serial, $"getprop {property}", cancellationToken);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"设备属性 {property} 返回为空。");
            }
            return value;
        }

        private async Task<string> RunAdbShellTextAsync(
            string adbPath,
            string serial,
            string shellCommand,
            CancellationToken cancellationToken)
        {
            var result = await RunAutoRootToolAsync(
                adbPath,
                $"-s \"{serial}\" shell {shellCommand}",
                cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"ADB命令失败: {result.ErrorText}");
            }
            return result.StandardOutput.Trim();
        }

        private static AutoRootKernelInfo ParseAutoRootKernel(string kernelVersion)
        {
            var match = Regex.Match(kernelVersion ?? string.Empty, @"(?<major>\d+)\.(?<minor>\d+)");
            if (!match.Success ||
                !int.TryParse(match.Groups["major"].Value, out int major) ||
                !int.TryParse(match.Groups["minor"].Value, out int minor))
            {
                throw new InvalidOperationException($"无法解析内核版本: {kernelVersion}。");
            }

            bool atMostFiveFour = major < 5 || (major == 5 && minor <= 4);
            string partition;
            if (major < 5 || (major == 5 && minor <= 10))
            {
                partition = "boot";
            }
            else if (major > 5 || (major == 5 && minor >= 15))
            {
                partition = "init_boot";
            }
            else
            {
                throw new NotSupportedException($"内核 {major}.{minor} 介于 5.10 与 5.15 之间，无法安全判断应修补的分区。");
            }

            return new AutoRootKernelInfo(major, minor, partition, atMostFiveFour);
        }

        private string GetSelectedAutoRootPatchScheme()
        {
            if (APatchPatchRadioButton?.IsChecked == true) return "APatch";
            if (FolkPatchPatchRadioButton?.IsChecked == true) return "FolkPatch";
            if (KernelSuLkmPatchRadioButton?.IsChecked == true) return "KernelSU LKM";
            if (SukiSuLkmPatchRadioButton?.IsChecked == true) return "SukiSU LKM";
            if (ReSukiSuLkmPatchRadioButton?.IsChecked == true) return "ReSukiSU LKM";
            return "Alpha";
        }

        private static bool IsKernelPatchScheme(string patchScheme)
        {
            return string.Equals(patchScheme, "APatch", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(patchScheme, "FolkPatch", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLkmPatchScheme(string patchScheme)
        {
            return string.Equals(patchScheme, "KernelSU LKM", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(patchScheme, "SukiSU LKM", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(patchScheme, "ReSukiSU LKM", StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureKernelPatchBootImage(string sourceImage, string patchScheme)
        {
            if (!IsKernelPatchScheme(patchScheme)) return;

            string fileName = Path.GetFileName(sourceImage);
            if (fileName.Contains("init_boot", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"{patchScheme} 只能修补 boot.img，不能使用 init_boot.img。");
            }
        }

        private string GetSelectedOfflineLkmKmi()
        {
            if (LkmKmiComboBox?.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
                item.Content is string value &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
            throw new InvalidOperationException("请选择 LKM KMI 版本。");
        }

        private async Task<string> ResolveOfflineLkmKmiAsync(
            CancellationToken cancellationToken,
            string? adbPath = null,
            string? adbSerial = null)
        {
            if (AutoDetectKmiCheckBox?.IsChecked != true)
            {
                return GetSelectedOfflineLkmKmi();
            }

            adbPath ??= GetAdbPath();
            adbSerial ??= await GetSingleAdbSystemDeviceAsync(adbPath, cancellationToken);
            string kernelVersion = await RunAdbShellTextAsync(
                adbPath,
                adbSerial,
                "uname -r",
                cancellationToken);
            string kmi = ParseAutoRootKmi(kernelVersion);

            bool supported = false;
            Dispatcher.Invoke(() =>
            {
                if (LkmKmiComboBox == null) return;

                foreach (object entry in LkmKmiComboBox.Items)
                {
                    if (entry is System.Windows.Controls.ComboBoxItem item &&
                        string.Equals(item.Content?.ToString(), kmi, StringComparison.OrdinalIgnoreCase))
                    {
                        LkmKmiComboBox.SelectedItem = item;
                        supported = true;
                        break;
                    }
                }
            });

            if (!supported)
            {
                throw new NotSupportedException($"自动识别到 LKM KMI {kmi}，但当前版本暂无对应修补模块。");
            }

            AppendAutorootLog($"自动识别 LKM KMI: {kmi}", "green");
            return kmi;
        }

        private static string ParseAutoRootKmi(string kernelVersion)
        {
            Match version = Regex.Match(kernelVersion ?? string.Empty, @"(?<major>\d+)\.(?<minor>\d+)");
            Match android = Regex.Match(kernelVersion ?? string.Empty, @"android(?<version>\d+)", RegexOptions.IgnoreCase);
            if (!version.Success || !android.Success)
            {
                throw new InvalidOperationException($"无法从内核版本识别 LKM KMI: {kernelVersion}。");
            }
            return $"android{android.Groups["version"].Value}-{version.Groups["major"].Value}.{version.Groups["minor"].Value}";
        }

        private static AutoRootManagerInfo GetAutoRootManagerInfo(string patchScheme)
        {
            if (string.Equals(patchScheme, "KernelSU LKM", StringComparison.OrdinalIgnoreCase))
            {
                return new AutoRootManagerInfo(
                    "KernelSU Manager 3.3.0",
                    "KernelSU_v3.3.0.apk",
                    KernelSuManagerDownloadUrl);
            }
            if (string.Equals(patchScheme, "SukiSU LKM", StringComparison.OrdinalIgnoreCase))
            {
                return new AutoRootManagerInfo(
                    "SukiSU Ultra 4.2.0",
                    "SukiSU_v4.2.0.apk",
                    SukiSuManagerDownloadUrl);
            }
            if (string.Equals(patchScheme, "ReSukiSU LKM", StringComparison.OrdinalIgnoreCase))
            {
                return new AutoRootManagerInfo(
                    "ReSukiSU Manager 4.1.0 (35045)",
                    "ReSukiSU.apk",
                    ReSukiSuManagerDownloadUrl);
            }
            if (string.Equals(patchScheme, "APatch", StringComparison.OrdinalIgnoreCase))
            {
                return new AutoRootManagerInfo(
                    "APatch 11219",
                    "APatch.apk",
                    APatchManagerDownloadUrl);
            }
            if (string.Equals(patchScheme, "FolkPatch", StringComparison.OrdinalIgnoreCase))
            {
                return new AutoRootManagerInfo(
                    "FolkPatch 5.0",
                    "FolkPatch.apk",
                    FolkPatchManagerDownloadUrl);
            }
            return new AutoRootManagerInfo(
                "Magisk Alpha 28103",
                "Alpha28103.apk",
                Alpha28103DownloadUrl);
        }

        private async Task ExecuteKernelPatchAsync(
            string sourceImage,
            string outputImage,
            string patchScheme,
            CancellationToken cancellationToken)
        {
            EnsureKernelPatchBootImage(sourceImage, patchScheme);
            if (!File.Exists(sourceImage))
            {
                throw new FileNotFoundException("未找到需要修补的 boot.img。", sourceImage);
            }
            string resourceRoot = ResolveKernelPatchRoot();
            string kptoolsPath = Path.Combine(resourceRoot, "kptools.exe");
            string kpimgName = string.Equals(patchScheme, "FolkPatch", StringComparison.OrdinalIgnoreCase)
                ? "fkpimg"
                : "akpimg";
            string kpimgPath = Path.Combine(resourceRoot, kpimgName);
            if (!File.Exists(kptoolsPath))
            {
                throw new FileNotFoundException("未找到 KernelPatch 修补器 kptools.exe。", kptoolsPath);
            }
            if (!File.Exists(kpimgPath))
            {
                throw new FileNotFoundException($"未找到 {patchScheme} 内核补丁文件。", kpimgPath);
            }

            string outputDirectory = Path.GetDirectoryName(outputImage) ??
                                     throw new InvalidOperationException("无法确定修补镜像输出目录。");
            Directory.CreateDirectory(outputDirectory);

            string tempRoot = Path.Combine(Path.GetTempPath(), "VioletTool", "KernelPatch");
            string workDirectory = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDirectory);
            string workBoot = Path.Combine(workDirectory, "boot.img");
            string workKpimg = Path.Combine(workDirectory, "kpimg");
            string kernelPath = Path.Combine(workDirectory, "kernel");
            string originalKernelPath = Path.Combine(workDirectory, "kernel_original");
            string patchedBootPath = Path.Combine(workDirectory, "new-boot.img");

            try
            {
                File.Copy(sourceImage, workBoot, true);
                File.Copy(kpimgPath, workKpimg, true);
                AppendAutorootLog($"开始 {patchScheme} 内核修补...", "yellow");

                AutoRootCommandResult unpackResult = await RunAutoRootToolAsync(
                    kptoolsPath,
                    "unpack \"boot.img\"",
                    cancellationToken,
                    workDirectory);
                if (unpackResult.ExitCode != 0 || !File.Exists(kernelPath))
                {
                    throw new InvalidOperationException(
                        $"boot.img 解包失败或镜像中不存在可修补内核: {unpackResult.ErrorText}");
                }

                AutoRootCommandResult flagsResult = await RunAutoRootToolAsync(
                    kptoolsPath,
                    "-i \"kernel\" -f",
                    cancellationToken,
                    workDirectory);
                string kernelFlags = flagsResult.StandardOutput + Environment.NewLine + flagsResult.StandardError;
                if (flagsResult.ExitCode != 0 ||
                    !kernelFlags.Contains("CONFIG_KALLSYMS=y", StringComparison.OrdinalIgnoreCase))
                {
                    throw new NotSupportedException(
                        $"当前 boot 内核未启用 CONFIG_KALLSYMS=y，无法使用 {patchScheme}。");
                }
                if (!kernelFlags.Contains("CONFIG_KALLSYMS_ALL=y", StringComparison.OrdinalIgnoreCase))
                {
                    AppendAutorootLog("内核未启用 CONFIG_KALLSYMS_ALL=y，将按官方初步兼容模式继续。", "yellow");
                }
                AppendAutorootLog("内核兼容性检查通过", "green");

                AutoRootCommandResult listResult = await RunAutoRootToolAsync(
                    kptoolsPath,
                    "-i \"kernel\" -l",
                    cancellationToken,
                    workDirectory);
                string patchInfo = listResult.StandardOutput + Environment.NewLine + listResult.StandardError;
                if (listResult.ExitCode != 0)
                {
                    throw new InvalidOperationException($"无法读取内核修补状态: {listResult.ErrorText}");
                }
                if (patchInfo.Contains("patched=true", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "所选 boot.img 已包含 KernelPatch，请使用对应系统版本的原厂 boot.img。");
                }

                File.Move(kernelPath, originalKernelPath, true);
                // APatch 官方无密钥模式不传 -S；FolkPatch 官方管理器在未启用
                // 自定义 SuperKey 时会传入兼容占位值 "su"。
                string patchArguments = string.Equals(patchScheme, "FolkPatch", StringComparison.OrdinalIgnoreCase)
                    ? "-p -i \"kernel_original\" -S \"su\" -k \"kpimg\" -o \"kernel\""
                    : "-p -i \"kernel_original\" -k \"kpimg\" -o \"kernel\"";
                AutoRootCommandResult patchResult = await RunAutoRootToolAsync(
                    kptoolsPath,
                    patchArguments,
                    cancellationToken,
                    workDirectory);
                if (patchResult.ExitCode != 0 || !File.Exists(kernelPath))
                {
                    throw new InvalidOperationException($"{patchScheme} 内核修补失败: {patchResult.ErrorText}");
                }

                AutoRootCommandResult verifyResult = await RunAutoRootToolAsync(
                    kptoolsPath,
                    "-i \"kernel\" -l",
                    cancellationToken,
                    workDirectory);
                string verifyInfo = verifyResult.StandardOutput + Environment.NewLine + verifyResult.StandardError;
                if (verifyResult.ExitCode != 0 ||
                    !verifyInfo.Contains("patched=true", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"{patchScheme} 内核修补结果校验失败。");
                }

                AutoRootCommandResult repackResult = await RunAutoRootToolAsync(
                    kptoolsPath,
                    "repack \"boot.img\"",
                    cancellationToken,
                    workDirectory);
                if (repackResult.ExitCode != 0 ||
                    !File.Exists(patchedBootPath) ||
                    new FileInfo(patchedBootPath).Length <= 0)
                {
                    throw new InvalidOperationException($"boot.img 重新打包失败: {repackResult.ErrorText}");
                }

                File.Copy(patchedBootPath, outputImage, true);
                AppendAutorootLog($"{patchScheme} 修补完成: {outputImage}", "green");
            }
            finally
            {
                try
                {
                    string fullWork = Path.GetFullPath(workDirectory);
                    string fullRoot = Path.GetFullPath(tempRoot).TrimEnd(Path.DirectorySeparatorChar) +
                                      Path.DirectorySeparatorChar;
                    if (fullWork.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) &&
                        Directory.Exists(fullWork))
                    {
                        Directory.Delete(fullWork, true);
                    }
                }
                catch
                {
                }
            }
        }

        private static string ResolveKernelPatchRoot()
        {
            string resourceRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KernelPatch");
            return Directory.Exists(resourceRoot)
                ? resourceRoot
                : throw new DirectoryNotFoundException(
                    "未找到 KernelPatch 资源目录，请重新安装完整版本的紫罗兰工具箱。");
        }

        private async Task ExecuteLkmPatchAsync(
            string sourceImage,
            string outputImage,
            string patchScheme,
            string kmi,
            CancellationToken cancellationToken)
        {
            if (!Regex.IsMatch(kmi ?? string.Empty, @"^android\d+-\d+\.\d+$", RegexOptions.IgnoreCase))
            {
                throw new InvalidOperationException($"无效的 LKM KMI: {kmi}。");
            }

            string lkmRoot = ResolveLkmPatchRoot();
            string ksudPath = Path.Combine(lkmRoot, "ksud.exe");
            string moduleFolder = string.Equals(patchScheme, "ReSukiSU LKM", StringComparison.OrdinalIgnoreCase)
                ? "ReSukiSU"
                : string.Equals(patchScheme, "SukiSU LKM", StringComparison.OrdinalIgnoreCase)
                    ? "SukiSU_Ultra"
                    : "KernelSU";
            string modulePath = Path.Combine(lkmRoot, moduleFolder, kmi + "_kernelsu.ko");
            if (!File.Exists(ksudPath))
            {
                throw new FileNotFoundException("未找到 LKM 修补器 ksud.exe。", ksudPath);
            }
            if (!File.Exists(modulePath))
            {
                throw new FileNotFoundException($"{patchScheme} 不支持当前 KMI {kmi}。", modulePath);
            }

            string outputDirectory = Path.GetDirectoryName(outputImage) ??
                                     throw new InvalidOperationException("无法确定 LKM 输出目录。");
            Directory.CreateDirectory(outputDirectory);
            if (File.Exists(outputImage)) File.Delete(outputImage);

            AppendAutorootLog($"开始 {patchScheme} 修补: {kmi}", "yellow");
            var result = await RunAutoRootToolAsync(
                ksudPath,
                $"boot-patch -b \"{sourceImage}\" -m \"{modulePath}\" --kmi \"{kmi}\" --out \"{outputDirectory}\" --out-name \"{Path.GetFileName(outputImage)}\"",
                cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"{patchScheme} 修补失败: {result.ErrorText}");
            }
            if (!File.Exists(outputImage) || new FileInfo(outputImage).Length <= 0)
            {
                throw new FileNotFoundException($"{patchScheme} 未生成修补镜像。", outputImage);
            }
            AppendAutorootLog($"{patchScheme} 修补完成: {outputImage}", "green");
        }

        private static string ResolveLkmPatchRoot()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDirectory, "LKM_Patch"),
                Path.GetFullPath(Path.Combine(
                    baseDirectory,
                    "..",
                    "..",
                    "Release",
                    "net8.0-windows",
                    "win-x64",
                    "LKM_Patch"))
            };
            string? found = candidates.FirstOrDefault(path => File.Exists(Path.Combine(path, "ksud.exe")));
            return found ?? throw new DirectoryNotFoundException(
                "未找到 LKM_Patch 资源目录，请确认 ksud.exe 和 KMI 模块已随程序发布。");
        }

        private async Task<AutoRootRomMatch> FindOnePlusRomAsync(
            string deviceCode,
            string mappedDeviceName,
            string systemVersion,
            CancellationToken cancellationToken)
        {
            AutoRootRomVersionCandidate? nearestCandidate = null;
            var seriesNames = await FetchRomApiSeriesAsync(
                RomApiPackageTypeFull,
                "OnePlus",
                cancellationToken);

            foreach (string series in seriesNames)
            {
                List<string> devices;
                try
                {
                    devices = await FetchRomApiDevicesAsync(
                        RomApiPackageTypeFull,
                        "OnePlus",
                        series,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    AppendAutorootLog($"跳过ROM系列 {series}: {ex.Message}", "yellow");
                    continue;
                }

                var matchedDevices = devices
                    .Where(device => AutoRootDeviceNameMatches(device, mappedDeviceName, deviceCode))
                    .OrderByDescending(device => device.Contains("C16", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (string device in matchedDevices)
                {
                    List<string> versions;
                    try
                    {
                        versions = await FetchRomApiVersionsAsync(
                            RomApiPackageTypeFull,
                            "OnePlus",
                            series,
                            device,
                            cancellationToken);
                    }
                    catch
                    {
                        continue;
                    }

                    string systemBuildId = ExtractAutoRootBuildId(systemVersion);
                    string? version = versions.FirstOrDefault(item =>
                        string.Equals(
                            ExtractAutoRootBuildId(item),
                            systemBuildId,
                            StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(version))
                    {
                        return await CreateAutoRootRomMatchAsync(
                            series,
                            device,
                            version,
                            cancellationToken);
                    }

                    foreach (string cloudVersion in versions)
                    {
                        if (!TryGetSafeAutoRootVersionDistance(systemVersion, cloudVersion, out int distance)) continue;
                        if (nearestCandidate == null || distance < nearestCandidate.Distance)
                        {
                            nearestCandidate = new AutoRootRomVersionCandidate(
                                series,
                                device,
                                cloudVersion,
                                distance);
                        }
                    }
                }
            }

            if (nearestCandidate != null)
            {
                AppendAutorootLog(
                    $"云端无精确版本，使用安全波动范围内ROM: {nearestCandidate.Version}（末级版本差 {nearestCandidate.Distance}）",
                    "yellow");
                return await CreateAutoRootRomMatchAsync(
                    nearestCandidate.Series,
                    nearestCandidate.Device,
                    nearestCandidate.Version,
                    cancellationToken);
            }

            throw new AutoRootRomUnavailableException();
        }

        private async Task<AutoRootRomMatch> CreateAutoRootRomMatchAsync(
            string series,
            string device,
            string version,
            CancellationToken cancellationToken)
        {
            var response = await FetchRomApiDownloadLinksAsync(
                RomApiPackageTypeFull,
                "OnePlus",
                series,
                device,
                version,
                cancellationToken);
            var links = PreferRomArchiveLinks(RomApiPackageTypeFull, "OnePlus", response.Links);
            string? downloadUrl = links.FirstOrDefault(link => !string.IsNullOrWhiteSpace(link));
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                throw new AutoRootRomUnavailableException();
            }

            return new AutoRootRomMatch(
                series,
                device,
                version,
                downloadUrl,
                response.RequestHeaders ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                response.ResolveRoute ?? string.Empty);
        }

        private static bool TryGetSafeAutoRootVersionDistance(
            string currentVersion,
            string cloudVersion,
            out int distance)
        {
            distance = int.MaxValue;
            const string pattern = @"^(?<device>[A-Z0-9]+)_(?<track>\d+(?:\.\d+)*?)\.(?<build>\d+)(?<region>\([A-Z0-9]+\))$";
            Match current = Regex.Match(ExtractAutoRootBuildId(currentVersion), pattern, RegexOptions.IgnoreCase);
            Match cloud = Regex.Match(ExtractAutoRootBuildId(cloudVersion), pattern, RegexOptions.IgnoreCase);
            if (!current.Success || !cloud.Success) return false;
            if (!string.Equals(current.Groups["device"].Value, cloud.Groups["device"].Value, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(current.Groups["track"].Value, cloud.Groups["track"].Value, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(current.Groups["region"].Value, cloud.Groups["region"].Value, StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(current.Groups["build"].Value, out int currentBuild) ||
                !int.TryParse(cloud.Groups["build"].Value, out int cloudBuild))
            {
                return false;
            }

            distance = Math.Abs(currentBuild - cloudBuild);
            return distance is > 0 and <= 3;
        }

        private static string ExtractAutoRootBuildId(string value)
        {
            string cleaned = CleanLink(value);
            Match match = Regex.Match(
                cleaned,
                @"[A-Z0-9]+_\d+(?:\.\d+)+\([A-Z0-9]+\)",
                RegexOptions.IgnoreCase);
            return match.Success ? match.Value : cleaned;
        }

        private static bool AutoRootDeviceNameMatches(string apiDeviceName, string mappedDeviceName, string deviceCode)
        {
            if (apiDeviceName.Contains(deviceCode, StringComparison.OrdinalIgnoreCase)) return true;
            string api = NormalizeAutoRootDeviceName(apiDeviceName);
            string mapped = NormalizeAutoRootDeviceName(mappedDeviceName);
            return !string.IsNullOrWhiteSpace(api) &&
                   !string.IsNullOrWhiteSpace(mapped) &&
                   (string.Equals(api, mapped, StringComparison.OrdinalIgnoreCase) ||
                    api.EndsWith(mapped, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeAutoRootDeviceName(string value)
        {
            string normalized = Regex.Replace(value ?? string.Empty, @"^\[[^\]]+\]", string.Empty);
            normalized = normalized
                .Replace("OnePlus", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("一加", string.Empty, StringComparison.OrdinalIgnoreCase);
            return new string(normalized
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray());
        }

        private async Task<string> ExtractAutoRootPartitionAsync(
            string url,
            IReadOnlyDictionary<string, string> requestHeaders,
            string partitionName,
            string outputDirectory,
            CancellationToken cancellationToken)
        {
            var headers = requestHeaders.Count > 0
                ? new Dictionary<string, string>(requestHeaders, StringComparer.OrdinalIgnoreCase)
                : null;
            var startedAt = DateTime.Now;

            UpdateAutorootProgress(25, "连接ROM");
            await using var sourceReader = await PayloadProcessing.OpenSourceAsync(
                url,
                cancellationToken,
                headers).ConfigureAwait(true);

            if (sourceReader is HttpRangeReader)
            {
                try
                {
                    var entries = await ZipStoredEntryLocator.ListEntriesAsync(sourceReader, cancellationToken).ConfigureAwait(true);
                    bool hasPayload = entries.Any(entry =>
                        !string.IsNullOrWhiteSpace(entry.Name) &&
                        entry.Name.EndsWith("payload.bin", StringComparison.OrdinalIgnoreCase));
                    if (!hasPayload)
                    {
                        if (!TryFindGenericZipPartitionEntry(entries, partitionName, out var entry))
                        {
                            throw new InvalidOperationException(
                                $"内核推测应修补 {partitionName}.img，但ROM包中不存在该分区，已停止刷写。");
                        }
                        AppendAutorootLog($"ROM分区校验通过：存在 {partitionName}.img", "green");

                        string outputPath = Path.Combine(outputDirectory, partitionName + ".img");
                        var progress = new Progress<long>(doneBytes =>
                        {
                            long total = Math.Max(1, entry.CompressedSize);
                            double percent = Math.Min(100, doneBytes * 100d / total);
                            UpdateAutorootProgress(25 + percent * 0.35, $"提取{partitionName}");
                        });
                        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
                        await ZipStoredEntryLocator.ExtractEntryAsync(sourceReader, entry, output, cancellationToken, progress).ConfigureAwait(true);
                        return outputPath;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch
                {
                    // 无法直接枚举ZIP时继续尝试payload流程。
                }
            }

            var unzipProgress = new Progress<double>(percent =>
                UpdateAutorootProgress(25 + Math.Clamp(percent, 0, 100) * 0.20, "解压payload"));
            var extractProgress = new Progress<(long DoneOps, long TotalOps)>(progress =>
            {
                if (progress.TotalOps <= 0) return;
                double percent = progress.DoneOps * 100d / progress.TotalOps;
                UpdateAutorootProgress(40 + Math.Clamp(percent, 0, 100) * 0.20, $"提取{partitionName}");
            });

            var (payloadReader, _) = await PayloadProcessing.OpenPayloadReaderAsync(
                sourceReader,
                cancellationToken,
                log: message =>
                {
                    if (!string.IsNullOrWhiteSpace(message)) AppendAutorootLog(message);
                },
                extractProgress: unzipProgress,
                eagerExtract: false).ConfigureAwait(true);

            await using IRandomAccessReader? payloadReaderScope = ReferenceEquals(payloadReader, sourceReader)
                ? null
                : payloadReader;
            var context = await PayloadProcessing.ReadManifestAsync(payloadReader, cancellationToken).ConfigureAwait(true);
            bool romContainsPartition = context.Manifest.Partitions.Any(partition =>
                string.Equals(partition.PartitionName, partitionName, StringComparison.OrdinalIgnoreCase));
            if (!romContainsPartition)
            {
                throw new InvalidOperationException(
                    $"内核推测应修补 {partitionName}.img，但ROM payload中不存在该分区，已停止刷写。");
            }
            AppendAutorootLog($"ROM payload分区校验通过：存在 {partitionName}", "green");
            if (context.Reader is ZipPayloadLazyReader lazy && !lazy.IsExtracted)
            {
                await lazy.EnsureExtractedAsync(unzipProgress, cancellationToken).ConfigureAwait(true);
            }

            await PayloadProcessing.ExtractPartitionsAsync(
                context,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { partitionName },
                outputDirectory,
                Environment.ProcessorCount,
                log: message =>
                {
                    if (!string.IsNullOrWhiteSpace(message)) AppendAutorootLog(message);
                },
                progress: extractProgress,
                cancellationToken: cancellationToken).ConfigureAwait(true);

            string? extracted = TryFindExtractedImage(outputDirectory, partitionName, startedAt);
            return extracted ?? throw new FileNotFoundException($"提取完成后未找到 {partitionName}.img。");
        }

        private async Task DownloadAutoRootFileAsync(
            string url,
            string outputPath,
            double progressStart,
            double progressEnd,
            CancellationToken cancellationToken)
        {
            using var response = await OnePlusAutoRootHttpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? 0;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true);
            var buffer = new byte[1 << 20];
            long done = 0;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                while (true)
                {
                    int read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (read <= 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    done += read;

                    double percent = total > 0 ? Math.Min(100, done * 100d / total) : 0;
                    double speed = done / Math.Max(0.1, stopwatch.Elapsed.TotalSeconds) / 1024d / 1024d;
                    UpdateAutorootProgress(
                        progressStart + (progressEnd - progressStart) * percent / 100d,
                        $"{speed:F1}MB/s");
                }
            }
            finally
            {
                if (done > 0) AddAutorootTrafficUsage(done);
            }
        }

        private async Task<string> WaitForSingleFastbootDeviceAsync(
            string fastbootPath,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + timeout;
            BeginAutorootFastbootWaitCountdown(Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds)));
            try
            {
                while (DateTime.UtcNow < deadline)
                {
                    int remainingSeconds = Math.Max(0, (int)Math.Ceiling((deadline - DateTime.UtcNow).TotalSeconds));
                    UpdateAutorootFastbootWaitCountdown(remainingSeconds);

                    var result = await RunAutoRootToolAsync(fastbootPath, "devices", cancellationToken);
                    var devices = (result.StandardOutput + Environment.NewLine + result.StandardError)
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(line => Regex.Match(line.Trim(), @"^(\S+)\s+fastboot$", RegexOptions.IgnoreCase))
                        .Where(match => match.Success)
                        .Select(match => match.Groups[1].Value)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (devices.Count == 1) return devices[0];
                    if (devices.Count > 1)
                    {
                        throw new InvalidOperationException("检测到多台Fastboot设备，请仅保留一台设备连接。");
                    }
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }

                throw new TimeoutException("等待Fastboot设备连接超时，请检查数据线或驱动。");
            }
            finally
            {
                EndAutorootFastbootWaitCountdown();
            }
        }

        private async Task EnsureBootloaderUnlockedAsync(
            string fastbootPath,
            string serial,
            CancellationToken cancellationToken)
        {
            var getVarResult = await RunAutoRootToolAsync(
                fastbootPath,
                $"-s \"{serial}\" getvar unlocked",
                cancellationToken);
            string output = getVarResult.StandardOutput + Environment.NewLine + getVarResult.StandardError;
            Match match = Regex.Match(
                output,
                @"(?:device\s+)?unlocked\s*:\s*(yes|no|true|false|1|0)",
                RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                var deviceInfoResult = await RunAutoRootToolAsync(
                    fastbootPath,
                    $"-s \"{serial}\" oem device-info",
                    cancellationToken);
                output = deviceInfoResult.StandardOutput + Environment.NewLine + deviceInfoResult.StandardError;
                match = Regex.Match(
                    output,
                    @"(?:device\s+)?unlocked\s*:\s*(yes|no|true|false|1|0)",
                    RegexOptions.IgnoreCase);
            }

            if (!match.Success)
            {
                throw new InvalidOperationException("无法确认 Bootloader 解锁状态，为防止误刷已停止任务。");
            }

            string value = match.Groups[1].Value;
            bool unlocked = value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                            value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                            value == "1";
            if (!unlocked)
            {
                throw new InvalidOperationException("Bootloader 尚未解锁，无法刷写修补镜像。");
            }

            AppendAutorootLog("解锁状态：Bootloader 已解锁", "green");
        }

        private async Task WaitForAndroidBootCompletedAsync(
            string adbPath,
            string serial,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var result = await RunAutoRootToolAsync(
                        adbPath,
                        $"-s \"{serial}\" shell getprop sys.boot_completed",
                        cancellationToken);
                    if (result.ExitCode == 0 && string.Equals(result.StandardOutput.Trim(), "1", StringComparison.Ordinal))
                    {
                        return;
                    }
                }
                catch
                {
                }
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            throw new TimeoutException("等待手机开机超时。");
        }

        private static async Task<AutoRootCommandResult> RunAutoRootToolAsync(
            string fileName,
            string arguments,
            CancellationToken cancellationToken,
            string? workingDirectory = null)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                    ? string.Empty
                    : workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo) ??
                                throw new InvalidOperationException($"无法启动 {Path.GetFileName(fileName)}。");
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
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
                throw;
            }
            await Task.WhenAll(outputTask, errorTask);
            return new AutoRootCommandResult(process.ExitCode, outputTask.Result, errorTask.Result);
        }

        private void UpdateAutorootProgress(double value, string status)
        {
            void Update()
            {
                if (AutorootProgressBar == null) return;
                AutorootProgressBar.Value = Math.Clamp(value, 0, 100);
                AutorootProgressBar.Tag = status;
            }

            if (Dispatcher.CheckAccess()) Update();
            else Dispatcher.Invoke(Update);
        }

        private void TryCleanupAutoRootDirectory(string workDirectory, string rootDirectory, bool createdRoot)
        {
            try
            {
                string fullWork = Path.GetFullPath(workDirectory);
                string fullRoot = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (fullWork.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullWork))
                {
                    Directory.Delete(fullWork, true);
                }

                if (createdRoot && Directory.Exists(rootDirectory) && !Directory.EnumerateFileSystemEntries(rootDirectory).Any())
                {
                    Directory.Delete(rootDirectory);
                }
                AppendAutorootLog("临时文件已清理。");
            }
            catch (Exception ex)
            {
                AppendAutorootLog($"清理 VioletTmp 失败: {ex.Message}", "yellow");
            }
        }

        private sealed record AutoRootKernelInfo(
            int Major,
            int Minor,
            string PartitionName,
            bool OnlyAlphaSupported);

        private sealed record AutoRootRomMatch(
            string Series,
            string Device,
            string Version,
            string DownloadUrl,
            IReadOnlyDictionary<string, string> RequestHeaders,
            string ResolveRoute);

        private sealed record AutoRootRomVersionCandidate(
            string Series,
            string Device,
            string Version,
            int Distance);

        private sealed record AutoRootManagerInfo(
            string DisplayName,
            string FileName,
            string DownloadUrl);

        private sealed class AutoRootRomUnavailableException : Exception
        {
        }

        private sealed record AutoRootCommandResult(
            int ExitCode,
            string StandardOutput,
            string StandardError)
        {
            public string ErrorText => string.IsNullOrWhiteSpace(StandardError)
                ? StandardOutput.Trim()
                : StandardError.Trim();
        }
    }
}
