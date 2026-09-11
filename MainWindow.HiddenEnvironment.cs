using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace WpfApp1
{
    // 模块文件项数据模型
    public class ModuleFileItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _fileName;
        private string _fullPath;

        public string FileName
        {
            get => _fileName;
            set
            {
                _fileName = value;
                OnPropertyChanged(nameof(FileName));
            }
        }

        public string FullPath
        {
            get => _fullPath;
            set
            {
                _fullPath = value;
                OnPropertyChanged(nameof(FullPath));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class MainWindow
    {
        private void HiddenEnvironmentButton_Click(object sender, RoutedEventArgs e)
        {
            // 显示隐藏环境视图，隐藏其他视图
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
            if (hiddenEnvironmentView != null) hiddenEnvironmentView.Visibility = Visibility.Visible;
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
            UpdateButtonStates("HiddenEnvironment");

            // 更新当前界面状态
            currentView = "HiddenEnvironment";

        }

        private void SelectHiddenRootZipButton_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择隐藏环境资源包7z文件",
                Filter = "7z压缩包 (*.7z)|*.7z|所有文件 (*.*)|*.*",
                FilterIndex = 1
            };

            if (openFileDialog.ShowDialog() == true)
            {
                HiddenRootZipPathTextBox.Text = openFileDialog.FileName;
                HiddenRootZipPathTextBox.Foreground = System.Windows.Media.Brushes.Black;
            }
        }

        // 隐藏ROOT方案复选框互斥逻辑
        private void HiddenRootScheme_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox checkedBox)
            {
                // 取消其他复选框的勾选
                if (checkedBox != MagiskAlpha28104CheckBox)
                    MagiskAlpha28104CheckBox.IsChecked = false;
                
                if (checkedBox != MagiskAlpha29000CheckBox)
                    MagiskAlpha29000CheckBox.IsChecked = false;
                
                if (checkedBox != SUkiSULKMCheckBox)
                    SUkiSULKMCheckBox.IsChecked = false;
                
                if (checkedBox != SUkiSUGKICheckBox)
                    SUkiSUGKICheckBox.IsChecked = false;
            }
        }

        // 下载隐藏环境资源包按钮点击事件
        private void DownloadHiddenPackageButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = "https://wwauy.lanzouv.com/izhBC42c9y7e";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"无法打开浏览器: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 安装检测软件按钮点击事件
        private async void InstallDetectAppsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // APK文件名和显示名称映射
                var apkMapping = new Dictionary<System.Windows.Controls.CheckBox, (string fileName, string displayName)>
                {
                    { DetectApp1CheckBox, ("1.apk", "momo") },
                    { DetectApp2CheckBox, ("2.apk", "ruru") },
                    { DetectApp3CheckBox, ("3.apk", "Hunter") },
                    { DetectApp4CheckBox, ("4.apk", "Luna") },
                    { DetectApp5CheckBox, ("5.apk", "紫色放大镜") },
                    { DetectApp6CheckBox, ("6.apk", "密钥认证") },
                    { DetectApp7CheckBox, ("7.apk", "应用列表检测器") },
                    { DetectApp8CheckBox, ("8.apk", "春秋检测3.8") },
                    { DetectApp9CheckBox, ("9.apk", "MT管理器") }
                };

                // 获取选中的APK
                var selectedApks = apkMapping.Where(kvp => kvp.Key?.IsChecked == true).Select(kvp => kvp.Value).ToList();

                if (selectedApks.Count == 0)
                {
                    AddHiddenRootLog("错误", "请至少选择一个检测软件");
                    return;
                }

                // 检查是否选择了资源包
                string zipPath = HiddenRootZipPathTextBox?.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(zipPath)
                    || zipPath == "请选择隐藏环境资源包7z文件"
                    || !File.Exists(zipPath)
                    || !Path.GetExtension(zipPath).Equals(".7z", StringComparison.OrdinalIgnoreCase))
                {
                    AddHiddenRootLog("错误", "请先选择有效的隐藏环境资源包7z文件");
                    return;
                }

                AddHiddenRootLog("系统", "开始安装检测软件");

                // 检查设备连接
                AddHiddenRootLog("信息", "设备连接状态...");
                string devicesOutput = await ExecuteAdbCommandWithOutput("devices");
                if (!devicesOutput.Contains("\tdevice"))
                {
                    AppendHiddenRootLogStatus("错误", "未连接");
                    return;
                }
                AppendHiddenRootLogStatus("成功", "已连接");

                // 检查ROOT权限
                AddHiddenRootLog("信息", "检查ROOT权限...");
                string rootCheckResult = await ExecuteAdbCommandWithOutput("shell \"su -c 'echo root_check'\"");
                bool hasRoot = rootCheckResult.Contains("root_check");
                
                if (hasRoot)
                {
                    AppendHiddenRootLogStatus("成功", "已授予");
                }
                else
                {
                    AppendHiddenRootLogStatus("提示", "未授予，将使用普通安装");
                }

                // 解压资源包
                string programDir = AppDomain.CurrentDomain.BaseDirectory;
                string sevenZipPath = Path.Combine(programDir, "bin", "7z.exe");

                if (!File.Exists(sevenZipPath))
                {
                    AddHiddenRootLog("错误", $"未找到 7z.exe: {sevenZipPath}");
                    return;
                }

                // 获取7z文件所在目录
                string zipDirectory = Path.GetDirectoryName(zipPath);
                string newZipName = "violettoolbox.7z";
                string newZipPath = Path.Combine(zipDirectory, newZipName);

                // 如果目标文件已存在，先删除
                if (File.Exists(newZipPath) && newZipPath != zipPath)
                {
                    File.Delete(newZipPath);
                }

                // 重命名7z文件（如果不是同一个文件）
                if (newZipPath != zipPath)
                {
                    AddHiddenRootLog("信息", "重命名资源包...");
                    File.Copy(zipPath, newZipPath, true);
                    AppendHiddenRootLogStatus("成功", "Done");
                }

                // 解压到同目录下的violettoolbox文件夹
                string extractPath = Path.Combine(zipDirectory, "violettoolbox");

                // 如果目录已存在，先删除
                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, true);
                }
                Directory.CreateDirectory(extractPath);

                AddHiddenRootLog("信息", "解压资源包...");

                // 解压ZIP
                var extractProcess = new ProcessStartInfo
                {
                    FileName = sevenZipPath,
                    Arguments = $"x \"{newZipPath}\" -o\"{extractPath}\" -y",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(extractProcess))
                {
                    if (process != null)
                    {
                        await process.WaitForExitAsync();
                        if (process.ExitCode != 0)
                        {
                            AddHiddenRootLog("错误", "解压资源包失败");
                            return;
                        }
                    }
                }

                AppendHiddenRootLogStatus("成功", "Done");

                int successCount = 0;
                int failCount = 0;

                // 在解压目录中查找APK文件（忽略大小写，一次性扫描，避免foreach内重复IO
                string[] allApkFiles = Directory.GetFiles(extractPath, "*.apk", SearchOption.AllDirectories);

                foreach (var apkInfo in selectedApks)
                {
                    string apkFileName = apkInfo.fileName;
                    string apkDisplayName = apkInfo.displayName;

                    // 尝试找到匹配的APK文件（忽略大小写）
                    string matchedFile = allApkFiles.FirstOrDefault(f => 
                        Path.GetFileName(f).Equals(apkFileName, StringComparison.OrdinalIgnoreCase));

                    // 如果精确匹配失败，尝试模糊匹配
                    if (string.IsNullOrEmpty(matchedFile))
                    {
                        string baseNameWithoutExt = Path.GetFileNameWithoutExtension(apkFileName);
                        // 去掉末尾的数字
                        string baseNameWithoutNumber = System.Text.RegularExpressions.Regex.Replace(baseNameWithoutExt, @"\d+$", "");
                        
                        matchedFile = allApkFiles.FirstOrDefault(f => 
                            Path.GetFileNameWithoutExtension(f).Contains(baseNameWithoutNumber, StringComparison.OrdinalIgnoreCase));
                    }

                    if (string.IsNullOrEmpty(matchedFile))
                    {
                        AddHiddenRootLog("警告", $"未找到安装包: {apkDisplayName}");
                        failCount++;
                        continue;
                    }

                    string apkPath = matchedFile;
                    AddHiddenRootLog("信息", $"安装 {apkDisplayName}...");

                    if (hasRoot)
                    {
                        // 静默安装（需要ROOT）
                        await ExecuteAdbCommand($"push \"{apkPath}\" /data/local/tmp/app.apk");
                        string installResult = await ExecuteAdbCommandWithOutput("shell \"su -c 'pm install -r -g /data/local/tmp/app.apk'\"");
                        await ExecuteAdbCommand("shell \"su -c 'rm /data/local/tmp/app.apk'\"");

                        if (installResult.Contains("Success"))
                        {
                            AppendHiddenRootLogStatus("成功", "OK");
                            successCount++;
                        }
                        else
                        {
                            AppendHiddenRootLogStatus("错误", "失败");
                            AddHiddenRootLog("错误", $"{apkDisplayName} 安装失败: {installResult}");
                            failCount++;
                        }
                    }
                    else
                    {
                        // 正常安装（无ROOT）
                        string installResult = await ExecuteAdbCommandWithOutput($"install -r \"{apkPath}\"");

                        if (installResult.Contains("Success"))
                        {
                            AppendHiddenRootLogStatus("成功", "OK");
                            successCount++;
                        }
                        else
                        {
                            AppendHiddenRootLogStatus("错误", "失败");
                            AddHiddenRootLog("错误", $"{apkDisplayName} 安装失败: {installResult}");
                            failCount++;
                        }
                    }

                    await Task.Delay(500);
                }

                // 清理临时解压目录
                AddHiddenRootLog("信息", "清理临时文件...");
                try
                {
                    if (Directory.Exists(extractPath))
                    {
                        Directory.Delete(extractPath, true);
                    }
                }
                catch
                {
                    AddHiddenRootLog("警告", "清理临时目录失败");
                }

                // 清理重命名产生的violettoolbox.7z残留
                try
                {
                    if (File.Exists(newZipPath) && newZipPath != zipPath)
                    {
                        File.Delete(newZipPath);
                    }
                }
                catch
                {
                    AddHiddenRootLog("警告", "清理临时压缩包失败");
                }

                AddHiddenRootLog(failCount == 0 ? "成功" : "警告", $"检测软件安装完成：成功 {successCount}，失败 {failCount}");
            }
            catch (Exception ex)
            {
                AddHiddenRootLog("错误", $"安装过程中发生错误: {ex.Message}");
            }
        }

        private static bool IsModuleInstallSuccessful(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return false;
            }

            string normalized = output.ToLowerInvariant();
            return normalized.Contains("success")
                || normalized.Contains("done")
                || normalized.Contains("installed")
                || output.Contains("安装成功", StringComparison.Ordinal)
                || normalized.Contains("inflating:")
                || normalized.Contains("extracting")
                || normalized.Contains("created:")
                || normalized.Contains("creating:");
        }

        private static bool IsHiddenRootShellCommandFailure(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return false;
            }

            string normalized = output.ToLowerInvariant();
            return normalized.Contains("permission denied")
                || normalized.Contains("not found")
                || normalized.Contains("no such file")
                || normalized.Contains("invalid")
                || normalized.Contains("failed")
                || normalized.Contains("error:");
        }

        // SukiSU LKM/GKI 方案: 启用并持久化 selinux_hide 内核特性。
        // 任何一步失败都只记录日志，不中断主流程；启用失败时使用资源包配置兜底。
        private async Task<bool> ConfigureSukiSuSelinuxHideAsync(string extractPath)
        {
            try
            {
                AddHiddenRootLog("信息", "启用 SukiSU selinux_hide 内核特性...");
                string setResult = await ExecuteAdbCommandWithOutput(
                    "shell \"su -c 'ksud feature set selinux_hide 1'\"");
                if (IsHiddenRootShellCommandFailure(setResult))
                {
                    AppendHiddenRootLogStatus("警告", "Error");
                    AddHiddenRootLog("警告", $"启用 selinux_hide 失败(不影响后续): {setResult}");
                    await TryReplaceSukiSuFeatureConfigAsync(extractPath);
                }
                else
                {
                    AppendHiddenRootLogStatus("成功", "OK");
                }

                AddHiddenRootLog("信息", "检查 SukiSU feature 状态...");
                string listResult = await ExecuteAdbCommandWithOutput(
                    "shell \"su -c 'ksud feature list'\"");
                if (IsHiddenRootShellCommandFailure(listResult))
                {
                    AppendHiddenRootLogStatus("警告", "Error");
                    AddHiddenRootLog("警告", $"读取 SukiSU feature 状态失败(不影响后续): {listResult}");
                }
                else
                {
                    AppendHiddenRootLogStatus("成功", "OK");
                }

                AddHiddenRootLog("信息", "持久化 SukiSU feature 状态...");
                string saveResult = await ExecuteAdbCommandWithOutput(
                    "shell \"su -c 'ksud feature save'\"");
                if (IsHiddenRootShellCommandFailure(saveResult))
                {
                    AppendHiddenRootLogStatus("警告", "Error");
                    AddHiddenRootLog("警告", $"保存 SukiSU feature 状态失败(不影响后续): {saveResult}");
                }
                else
                {
                    AppendHiddenRootLogStatus("成功", "OK");
                }
            }
            catch (Exception ex)
            {
                AddHiddenRootLog("警告", $"SukiSU selinux_hide 配置过程异常(不影响后续): {ex.Message}");
            }

            return true;
        }

        // ksud feature 加载失败时，使用资源包中的配置替换设备配置。
        private async Task TryReplaceSukiSuFeatureConfigAsync(string extractPath)
        {
            try
            {
                string localFeatureConfig = Path.Combine(extractPath, ".feature_config");
                if (!File.Exists(localFeatureConfig))
                {
                    AddHiddenRootLog("提示", "资源包中未找到 .feature_config, 跳过替换");
                    return;
                }

                AddHiddenRootLog("信息", "检查设备 /data/adb/ksu/ 目录 ...");
                string checkResult = await ExecuteAdbCommandWithOutput(
                    "shell \"su -c 'test -d /data/adb/ksu && echo ksu_dir_exists'\"");
                if (!checkResult.Contains("ksu_dir_exists", StringComparison.Ordinal))
                {
                    AddHiddenRootLog("提示", "设备无 /data/adb/ksu/ 目录, 跳过替换");
                    return;
                }

                AddHiddenRootLog("信息", "推送 .feature_config 到设备...");
                string pushResult = await ExecuteAdbCommandWithOutput(
                    $"push \"{localFeatureConfig}\" /data/local/tmp/.feature_config");
                if (IsHiddenRootShellCommandFailure(pushResult))
                {
                    AppendHiddenRootLogStatus("警告", "Error");
                    AddHiddenRootLog("警告", $"推送 .feature_config 失败(不影响后续): {pushResult}");
                    return;
                }
                AppendHiddenRootLogStatus("成功", "Done");

                AddHiddenRootLog("信息", "替换设备 .feature_config ...");
                string replaceResult = await ExecuteAdbCommandWithOutput(
                    "shell \"su -c 'mkdir -p /data/adb/ksu && " +
                    "chmod 700 /data/adb/ksu && " +
                    "(chattr -i /data/adb/ksu/.feature_config 2>/dev/null || true) && " +
                    "rm -f /data/adb/ksu/.feature_config.tmp && " +
                    "cp /data/local/tmp/.feature_config /data/adb/ksu/.feature_config.tmp && " +
                    "chown 0:0 /data/adb/ksu/.feature_config.tmp && " +
                    "chmod 600 /data/adb/ksu/.feature_config.tmp && " +
                    "mv -f /data/adb/ksu/.feature_config.tmp /data/adb/ksu/.feature_config && " +
                    "test -s /data/adb/ksu/.feature_config && echo feature_config_replace_ok'\"");

                await ExecuteAdbCommand("shell \"su -c 'rm -f /data/local/tmp/.feature_config'\"");

                if (replaceResult.Contains("feature_config_replace_ok", StringComparison.Ordinal))
                {
                    AppendHiddenRootLogStatus("成功", "OK");
                }
                else
                {
                    AppendHiddenRootLogStatus("警告", "Error");
                    AddHiddenRootLog("警告", $"替换 .feature_config 失败(不影响后续): {replaceResult}");
                }
            }
            catch (Exception ex)
            {
                AddHiddenRootLog("警告", $"替换 .feature_config 过程异常(不影响后续): {ex.Message}");
            }
        }

        private async Task<bool> InstallHiddenRootModuleAsync(
            string moduleName,
            string suCommand,
            string moduleInstallCommand)
        {
            AddHiddenRootLog("信息", $"安装模块 {moduleName}...");
            string result = await ExecuteAdbCommandWithOutput(
                $"{suCommand} \"{moduleInstallCommand} /storage/emulated/0/violet/{moduleName}\"");

            if (IsModuleInstallSuccessful(result))
            {
                AppendHiddenRootLogStatus("成功", "OK");
                return true;
            }

            AppendHiddenRootLogStatus("错误", "Error");
            AddHiddenRootLog("错误", $"模块 {moduleName} 安装失败: {result}");
            return false;
        }

        private async Task<bool> ReplaceHiddenRootKeyFileAsync()
        {
            AddHiddenRootLog("信息", "替换密钥文件...");

            // su -s selects a shell executable on Magisk and must not be used as
            // the command-execution switch. Use the same verified su -c form as
            // the ROOT permission probe, then replace the protected file safely.
            string result = await ExecuteAdbCommandWithOutput(
                "shell \"su -c 'mkdir -p /data/adb/tricky_store && " +
                "chmod 700 /data/adb/tricky_store && " +
                "(chmod u+w /data/adb/tricky_store/target.txt 2>/dev/null || true) && " +
                "(chattr -i /data/adb/tricky_store/target.txt 2>/dev/null || true) && " +
                "rm -f /data/adb/tricky_store/target.txt /data/adb/tricky_store/target.txt.tmp && " +
                "cp /storage/emulated/0/violet/target.txt /data/adb/tricky_store/target.txt.tmp && " +
                "chown 0:0 /data/adb/tricky_store/target.txt.tmp && " +
                "chmod 600 /data/adb/tricky_store/target.txt.tmp && " +
                "mv -f /data/adb/tricky_store/target.txt.tmp /data/adb/tricky_store/target.txt && " +
                "test -s /data/adb/tricky_store/target.txt && echo key_replace_ok'\"");

            if (result.Contains("key_replace_ok", StringComparison.Ordinal))
            {
                AppendHiddenRootLogStatus("成功", "OK");
                return true;
            }

            AppendHiddenRootLogStatus("错误", "Error");
            AddHiddenRootLog("错误", $"替换密钥文件失败: {result}");
            return false;
        }

        // 模块安装按钮点击事件
        private async void StartModuleInstallButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. 检查是否有选中的模块
                var selectedModules = ModuleListBox.Items.Cast<ModuleFileItem>()
                    .Where(m => m.IsSelected)
                    .ToList();

                if (selectedModules.Count == 0)
                {
                    AddHiddenRootLog("警告", "请先拖入需要安装的模块文件");
                    return;
                }

                AddHiddenRootLog("系统", "开始安装模块");

                // 2. 检查设备连接
                AddHiddenRootLog("信息", "设备连接状态...");
                string devicesOutput = await ExecuteAdbCommandWithOutput("devices");
                if (!devicesOutput.Contains("\tdevice"))
                {
                    AppendHiddenRootLogStatus("错误", "未连接");
                    return;
                }
                AppendHiddenRootLogStatus("成功", "已连接");

                // 3. 检查ROOT权限
                AddHiddenRootLog("信息", "检查ROOT权限...");
                string rootCheckResult = await ExecuteAdbCommandWithOutput("shell \"su -c 'echo root_check'\"");
                if (!rootCheckResult.Contains("root_check"))
                {
                    AppendHiddenRootLogStatus("错误", "未授予");
                    AddHiddenRootLog("错误", "请先向【Shell】授予 ROOT 权限");
                    return;
                }

                AppendHiddenRootLogStatus("成功", "已授予");

                // 4. 检测ROOT管理器类型
                AddHiddenRootLog("信息", "查询 ROOT 管理器类型...");
                
                string checkKsud = await ExecuteAdbCommandWithOutput("shell \"su -c 'ls /data/adb/ksud 2>/dev/null'\"");
                string checkApd = await ExecuteAdbCommandWithOutput("shell \"su -c 'ls /data/adb/apd 2>/dev/null'\"");
                string checkMagiskDb = await ExecuteAdbCommandWithOutput("shell \"su -c 'ls /data/adb/magisk.db 2>/dev/null'\"");

                bool hasKsud = checkKsud.Contains("/data/adb/ksud");
                bool hasApd = checkApd.Contains("/data/adb/apd");
                bool hasMagiskDb = checkMagiskDb.Contains("/data/adb/magisk.db");

                int detectedCount = (hasKsud ? 1 : 0) + (hasApd ? 1 : 0) + (hasMagiskDb ? 1 : 0);

                string rootManagerType = "";
                string installCommand = "";

                if (detectedCount == 0)
                {
                    AppendHiddenRootLogStatus("错误", "未检测到");
                    AddHiddenRootLog("错误", "未检测到 ROOT 管理器");
                    return;
                }
                else if (detectedCount > 1)
                {
                    // 检测到多个ROOT管理器，让用户选择
                    AppendHiddenRootLogStatus("提示", "检测到多个");
                    
                    var choices = new List<string>();
                    if (hasKsud) choices.Add("KernelSU/SukiSU Ultra");
                    if (hasApd) choices.Add("APatch");
                    if (hasMagiskDb) choices.Add("Magisk");

                    // 创建选择对话框
                    rootManagerType = await ShowRootManagerSelectionDialog(choices);
                    
                    if (string.IsNullOrEmpty(rootManagerType))
                    {
                        AddHiddenRootLog("提示", "用户取消操作");
                        return;
                    }
                }
                else
                {
                    // 只检测到一个ROOT管理器
                    if (hasKsud) rootManagerType = "KernelSU/SukiSU Ultra";
                    else if (hasApd) rootManagerType = "APatch";
                    else if (hasMagiskDb) rootManagerType = "Magisk";
                }

                AppendHiddenRootLogStatus("成功", rootManagerType);

                // 5. 根据ROOT管理器类型设置安装命令
                if (rootManagerType.Contains("KernelSU") || rootManagerType.Contains("SukiSU"))
                {
                    installCommand = "ksud module install";
                }
                else if (rootManagerType.Contains("APatch"))
                {
                    installCommand = "apd module install";
                }
                else if (rootManagerType.Contains("Magisk"))
                {
                    // Magisk需要尝试不同的su命令
                    installCommand = "magisk --install-module";
                }

                // 6. 开始安装模块
                int successCount = 0;
                int failCount = 0;

                foreach (var module in selectedModules)
                {
                    AddHiddenRootLog("信息", $"安装模块 {module.FileName}...");

                    try
                    {
                        // 推送模块到设备
                        string tempPath = $"/data/local/tmp/{module.FileName}";
                        await ExecuteAdbCommand($"push \"{module.FullPath}\" {tempPath}");

                        string installResult = "";

                        if (rootManagerType.Contains("Magisk"))
                        {
                            // Magisk尝试两种命令
                            installResult = await ExecuteAdbCommandWithOutput($"shell \"su -c '{installCommand} {tempPath}'\"");
                            
                            if (!installResult.Contains("Success") && !installResult.ToLower().Contains("done"))
                            {
                                // 尝试su -s
                                installResult = await ExecuteAdbCommandWithOutput($"shell \"su -s '{installCommand} {tempPath}'\"");
                            }
                        }
                        else
                        {
                            // KernelSU/APatch使用统一命令
                            installResult = await ExecuteAdbCommandWithOutput($"shell \"su -c '{installCommand} {tempPath}'\"");
                        }

                        // 清理临时文件
                        await ExecuteAdbCommand($"shell \"su -c 'rm {tempPath}'\"");

                        // 判断安装结果
                        if (IsModuleInstallSuccessful(installResult))
                        {
                            AppendHiddenRootLogStatus("成功", "OK");
                            successCount++;
                        }
                        else
                        {
                            AppendHiddenRootLogStatus("错误", "失败");
                            AddHiddenRootLog("错误", $"{module.FileName} 安装失败: {installResult}");
                            failCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendHiddenRootLogStatus("错误", "失败");
                        AddHiddenRootLog("错误", $"{module.FileName} 安装异常: {ex.Message}");
                        failCount++;
                    }

                    await Task.Delay(500);
                }

                AddHiddenRootLog(failCount == 0 ? "成功" : "警告", $"模块安装完成：成功 {successCount}，失败 {failCount}");
                
                if (successCount > 0)
                {
                    AddHiddenRootLog("提示", "请重启设备以使模块生效");
                }
            }
            catch (Exception ex)
            {
                AddHiddenRootLog("错误", $"模块安装过程中发生错误: {ex.Message}");
            }
        }

        // 显示ROOT管理器选择对话框
        private Task<string> ShowRootManagerSelectionDialog(List<string> choices)
        {
            var tcs = new TaskCompletionSource<string>();

            var dialog = new Window
            {
                Title = "选择ROOT管理器",
                Width = 400,
                Height = 250,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var mainGrid = new Grid { Margin = new Thickness(20) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 标题文本
            var titleText = new TextBlock
            {
                Text = "检测到多个ROOT管理器，请选择当前使用的管理器：",
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 15),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(titleText, 0);
            mainGrid.Children.Add(titleText);

            // CheckBox容器
            var checkBoxPanel = new StackPanel 
            { 
                Margin = new Thickness(0, 0, 0, 15),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
            };
            Grid.SetRow(checkBoxPanel, 1);

            var radioButtons = new List<System.Windows.Controls.RadioButton>();
            foreach (var choice in choices)
            {
                var radioButton = new System.Windows.Controls.RadioButton
                {
                    Content = choice,
                    FontSize = 13,
                    Margin = new Thickness(20, 8, 20, 8),
                    GroupName = "RootManager",
                    VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left
                };
                
                if (radioButtons.Count == 0)
                {
                    radioButton.IsChecked = true; // 默认选中第一个
                }
                
                radioButtons.Add(radioButton);
                checkBoxPanel.Children.Add(radioButton);
            }
            mainGrid.Children.Add(checkBoxPanel);

            // 按钮容器
            var buttonPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            Grid.SetRow(buttonPanel, 2);

            var confirmButton = new System.Windows.Controls.Button
            {
                Content = "确定",
                Width = 80,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0)
            };
            confirmButton.Click += (s, e) =>
            {
                var selected = radioButtons.FirstOrDefault(rb => rb.IsChecked == true);
                tcs.SetResult(selected?.Content?.ToString() ?? "");
                dialog.Close();
            };

            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "取消",
                Width = 80,
                Height = 30
            };
            cancelButton.Click += (s, e) =>
            {
                tcs.SetResult("");
                dialog.Close();
            };

            buttonPanel.Children.Add(confirmButton);
            buttonPanel.Children.Add(cancelButton);
            mainGrid.Children.Add(buttonPanel);

            dialog.Content = mainGrid;
            dialog.ShowDialog();

            return tcs.Task;
        }

        // 模块ListBox拖放事件 - DragEnter
        private void ModuleListBox_DragEnter(object sender, System.Windows.DragEventArgs e)
        {
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

        // 模块ListBox拖放事件 - DragOver
        private void ModuleListBox_DragOver(object sender, System.Windows.DragEventArgs e)
        {
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

        // 模块ListBox拖放事件 - Drop
        private void ModuleListBox_Drop(object sender, System.Windows.DragEventArgs e)
        {
            try
            {
                if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                {
                    string[] files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                    
                    // 只过滤.zip文件
                    var zipFiles = files.Where(f => Path.GetExtension(f).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                                       .ToList();

                    if (zipFiles.Count > 0)
                    {
                        // 获取已存在的文件路径（用于去重）
                        var existingPaths = ModuleListBox.Items.Cast<ModuleFileItem>()
                            .Select(m => m.FullPath)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        int addedCount = 0;
                        int duplicateCount = 0;

                        // 累计添加新的ZIP文件到ListBox，默认全部勾选
                        foreach (var zipFile in zipFiles)
                        {
                            // 检查是否已存在（去重）
                            if (existingPaths.Contains(zipFile))
                            {
                                duplicateCount++;
                                continue;
                            }

                            var moduleItem = new ModuleFileItem
                            {
                                FileName = Path.GetFileName(zipFile),
                                FullPath = zipFile,
                                IsSelected = true  // 默认勾选
                            };
                            ModuleListBox.Items.Add(moduleItem);
                            addedCount++;
                        }

                        // 对整个列表按文件名排序
                        var allItems = ModuleListBox.Items.Cast<ModuleFileItem>()
                            .OrderBy(m => m.FileName)
                            .ToList();
                        
                        ModuleListBox.Items.Clear();
                        foreach (var item in allItems)
                        {
                            ModuleListBox.Items.Add(item);
                        }

                        if (addedCount > 0)
                        {
                            AddHiddenRootLog("成功", $"已添加 {addedCount} 个模块文件");
                        }
                        
                        if (duplicateCount > 0)
                        {
                            AddHiddenRootLog("提示", $"跳过 {duplicateCount} 个重复文件");
                        }
                    }
                    else
                    {
                        AddHiddenRootLog("警告", "未找到ZIP格式的模块文件");
                    }
                }
            }
            catch (Exception ex)
            {
                AddHiddenRootLog("错误", $"拖放文件失败: {ex.Message}");
            }
            e.Handled = true;
        }

        // 手动选择模块按钮点击事件
        private void SelectModuleButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddHiddenRootLog("信息", "打开文件选择对话框...");
                
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择模块文件",
                    Filter = "ZIP文件 (*.zip)|*.zip|所有文件 (*.*)|*.*",
                    Multiselect = true, // 允许多选
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) // 默认打开桌面
                };

                bool? result = openFileDialog.ShowDialog(this);
                
                if (result == true)
                {
                    string[] selectedFiles = openFileDialog.FileNames;
                    AddHiddenRootLog("信息", $"用户选择了 {selectedFiles.Length} 个文件");
                    
                    // 获取已存在的文件路径（用于去重）
                    var existingPaths = ModuleListBox.Items.Cast<ModuleFileItem>()
                        .Select(m => m.FullPath)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    int addedCount = 0;
                    int duplicateCount = 0;

                    // 添加选中的文件
                    foreach (var filePath in selectedFiles)
                    {
                        // 检查是否已存在（去重）
                        if (existingPaths.Contains(filePath))
                        {
                            duplicateCount++;
                            continue;
                        }

                        var moduleItem = new ModuleFileItem
                        {
                            FileName = Path.GetFileName(filePath),
                            FullPath = filePath,
                            IsSelected = true  // 默认勾选
                        };
                        ModuleListBox.Items.Add(moduleItem);
                        addedCount++;

                    }

                    // 对整个列表按文件名排序
                    var allItems = ModuleListBox.Items.Cast<ModuleFileItem>()
                        .OrderBy(m => m.FileName)
                        .ToList();
                    
                    ModuleListBox.Items.Clear();
                    foreach (var item in allItems)
                    {
                        ModuleListBox.Items.Add(item);
                    }

                    if (addedCount > 0)
                    {
                        AddHiddenRootLog("成功", $"已添加 {addedCount} 个模块文件");
                    }
                    
                    if (duplicateCount > 0)
                    {
                        AddHiddenRootLog("提示", $"跳过 {duplicateCount} 个重复文件");
                    }
                }
                else
                {
                    AddHiddenRootLog("提示", "用户取消了文件选择");
                }
            }
            catch (Exception ex)
            {
                AddHiddenRootLog("错误", $"选择文件失败: {ex.Message}");
                System.Windows.MessageBox.Show($"选择文件失败: {ex.Message}\n\n{ex.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // 清空模块列表按钮点击事件
        private void ClearModuleListButton_Click(object sender, RoutedEventArgs e)
        {
            if (ModuleListBox.Items.Count > 0)
            {
                int count = ModuleListBox.Items.Count;
                ModuleListBox.Items.Clear();
                AddHiddenRootLog("提示", $"已清空 {count} 个模块文件");
            }
            else
            {
                AddHiddenRootLog("提示", "列表已经是空的");
            }
        }

        private async void StartHiddenRootButton_Click(object sender, RoutedEventArgs e)
                {
                    UpdateHiddenRootProgress(0, "正在校验运行条件");
                    StartHiddenRootButton.IsEnabled = false;

                    try
                    {
                        // 检查是否勾选了方案
                        bool is28104Version = MagiskAlpha28104CheckBox?.IsChecked == true;
                        bool is29000Version = MagiskAlpha29000CheckBox?.IsChecked == true;
                        bool isSUkiSULKM = SUkiSULKMCheckBox?.IsChecked == true;
                        bool isSUkiSUGKI = SUkiSUGKICheckBox?.IsChecked == true;

                        int selectedCount = (is28104Version ? 1 : 0) + (is29000Version ? 1 : 0) + (isSUkiSULKM ? 1 : 0) + (isSUkiSUGKI ? 1 : 0);

                        if (selectedCount == 0)
                        {
                            AddHiddenRootLog("错误", "请先勾选一个隐藏方案");
                            FailHiddenRootProgress("请选择隐藏方案");
                            return;
                        }

                        if (selectedCount > 1)
                        {
                            AddHiddenRootLog("错误", "只能选择一个隐藏方案");
                            FailHiddenRootProgress("只能选择一个隐藏方案");
                            return;
                        }

                        // 根据版本确定使用的命令前缀和模块安装命令
                        string suCommand;
                        string moduleInstallCommand;
                        
                        if (is29000Version)
                        {
                            suCommand = "shell su -s";
                            moduleInstallCommand = "magisk --install-module";
                        }
                        else if (isSUkiSULKM || isSUkiSUGKI)
                        {
                            suCommand = "shell su -c";
                            moduleInstallCommand = "ksud module install";
                        }
                        else // is28104Version
                        {
                            suCommand = "shell su -c";
                            moduleInstallCommand = "magisk --install-module";
                        }

                        // 检查是否选择了7z文件
                        string zipPath = HiddenRootZipPathTextBox?.Text?.Trim() ?? "";
                        if (string.IsNullOrEmpty(zipPath)
                            || zipPath == "请选择隐藏环境资源包7z文件"
                            || !File.Exists(zipPath)
                            || !Path.GetExtension(zipPath).Equals(".7z", StringComparison.OrdinalIgnoreCase))
                        {
                            AddHiddenRootLog("错误", "请先选择有效的隐藏环境资源包7z文件");
                            FailHiddenRootProgress("资源包无效");
                            return;
                        }

                        // 显示警告信息
                        if (isSUkiSUGKI)
                        {
                            AddHiddenRootLog("警告", "仅支持安卓 12 及以上设备；建议线刷后使用，避免产生过多 ROOT 痕迹。GKI 模式完成后需自行伪装内核版本，并提前备份原厂内核信息。本功能仅供学习交流，请勿用于非法用途。");
                        }
                        else
                        {
                            AddHiddenRootLog("警告", "仅支持安卓 12 及以上设备；建议线刷后使用，避免产生过多 ROOT 痕迹。本功能仅供学习交流，请勿用于非法用途。");
                        }
                        await Task.Delay(1000);

                        AddHiddenRootLog("系统", "开始隐藏 ROOT 流程");
                        UpdateHiddenRootProgress(2, "正在准备资源包");

                        // 1. 重命名并解压7z文件
                        string programDir = AppDomain.CurrentDomain.BaseDirectory;
                        string sevenZipPath = Path.Combine(programDir, "bin", "7z.exe");

                        if (!File.Exists(sevenZipPath))
                        {
                            AddHiddenRootLog("错误", $"未找到7z.exe: {sevenZipPath}");
                            FailHiddenRootProgress("缺少7z.exe");
                            return;
                        }

                        // 获取7z文件所在目录
                        string zipDirectory = Path.GetDirectoryName(zipPath);
                        string newZipName = "violettoolbox.7z";
                        string newZipPath = Path.Combine(zipDirectory, newZipName);

                        // 如果目标文件已存在，先删除
                        if (File.Exists(newZipPath) && newZipPath != zipPath)
                        {
                            File.Delete(newZipPath);
                        }

                        // 重命名7z文件（如果不是同一个文件）
                        if (newZipPath != zipPath)
                        {
                            AddHiddenRootLog("信息", "重命名资源包...");
                            File.Copy(zipPath, newZipPath, true);
                            AppendHiddenRootLogStatus("成功", "Done");
                        }

                        UpdateHiddenRootProgress(5, "正在解压资源包");

                        // 解压到同目录下的violettoolbox文件夹
                        string extractPath = Path.Combine(zipDirectory, "violettoolbox");

                        // 如果目录已存在，先删除
                        if (Directory.Exists(extractPath))
                        {
                            Directory.Delete(extractPath, true);
                        }
                        Directory.CreateDirectory(extractPath);

                        AddHiddenRootLog("信息", "解压隐藏环境资源包...");

                        var extractProcess = new ProcessStartInfo
                        {
                            FileName = sevenZipPath,
                            Arguments = $"x \"{newZipPath}\" -o\"{extractPath}\" -y",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };

                        using (var process = Process.Start(extractProcess))
                        {
                            if (process != null)
                            {
                                await process.WaitForExitAsync();
                                if (process.ExitCode != 0)
                                {
                                    AddHiddenRootLog("错误", "解压失败");
                                    FailHiddenRootProgress("资源包解压失败");
                                    return;
                                }
                            }
                        }

                        AppendHiddenRootLogStatus("成功", "Done");
                        UpdateHiddenRootProgress(15, "正在检测设备连接");

                        // 2. 检查ADB设备连接
                        AddHiddenRootLog("信息", "设备连接状态...");
                        string devicesOutput = await ExecuteAdbCommandWithOutput("devices");

                        if (!devicesOutput.Contains("\tdevice"))
                        {
                            AppendHiddenRootLogStatus("错误", "未连接");
                            FailHiddenRootProgress("设备未连接");
                            return;
                        }

                        AppendHiddenRootLogStatus("成功", "已连接");
                        UpdateHiddenRootProgress(20, "设备已连接，等待ROOT授权");

                        // 停止5秒
                        await Task.Delay(5000);

                        // 3. 检查Root权限
                        if (isSUkiSULKM || isSUkiSUGKI)
                        {
                            AddHiddenRootLog("提示", "请提前打开ROOT管理器程序给予【Shell】ROOT权限.");
                        }
                        else
                        {
                            AddHiddenRootLog("提示", "请注意手机上的ROOT弹窗，请点击【允许】");
                        }
                        AddHiddenRootLog("信息", "检查ROOT权限...");

                        string rootCheckResult = await ExecuteAdbCommandWithOutput("shell \"su -c 'echo root_check'\"");

                        if (!rootCheckResult.Contains("root_check"))
                        {
                            AppendHiddenRootLogStatus("错误", "未授予");
                            FailHiddenRootProgress("ROOT权限未授予");
                            return;
                        }

                        AppendHiddenRootLogStatus("成功", "已授予");
                        UpdateHiddenRootProgress(27, "正在读取设备信息");
                        await Task.Delay(2000);

                        // SukiSU LKM/GKI 方案需要启用并持久化 selinux_hide。
                        if (isSUkiSULKM || isSUkiSUGKI)
                        {
                            UpdateHiddenRootProgress(29, "正在配置 SukiSU selinux_hide");
                            await ConfigureSukiSuSelinuxHideAsync(extractPath);
                        }

                        // 查询内核版本 (如 5.15.167-android13-8-00014-... 可同时得到内核5.15和安卓13)
                        AddHiddenRootLog("信息", "查询内核版本...");
                        string kernelVersion = await ExecuteAdbCommandWithOutput("shell uname -r");
                        AppendHiddenRootLogStatus("信息", kernelVersion);

                        // 查询安卓版本（兜底：内核字符串不带androidNN标记时使用）
                        AddHiddenRootLog("信息", "查询安卓版本...");
                        string androidVersion = await ExecuteAdbCommandWithOutput("shell getprop ro.build.version.release");
                        AppendHiddenRootLogStatus("信息", androidVersion);

                        // 查询设备厂商/型号信息 (Xiaomi/Redmi/OnePlus等)
                        AddHiddenRootLog("信息", "查询设备厂商...");
                        string deviceInfo = await ExecuteAdbCommandWithOutput("shell \"getprop ro.product.manufacturer; getprop ro.product.brand; getprop ro.product.model; getprop ro.product.device; getprop ro.product.marketname\"");
                        AppendHiddenRootLogStatus("信息", deviceInfo);
                        UpdateHiddenRootProgress(34, "正在匹配设备环境");

                        // 小米/红米设备: 直接跳过, 命中后不再检查处理器/分区表
                        bool isXiaomiRedmi = deviceInfo.IndexOf("xiaomi", StringComparison.OrdinalIgnoreCase) >= 0
                            || deviceInfo.IndexOf("redmi", StringComparison.OrdinalIgnoreCase) >= 0
                            || deviceInfo.Contains("小米")
                            || deviceInfo.Contains("红米");

                        string? selectedPathmaskModule = null;
                        if (isXiaomiRedmi)
                        {
                            AddHiddenRootLog("提示", "跳过 PathMask 模块刷入: 小米/红米设备");
                        }
                        else
                        {
                            // 查询分区表 (通过bootloader分区特征判断平台, 无需root)
                            AddHiddenRootLog("信息", "查询分区表...");
                            string partInfo = await ExecuteAdbCommandWithOutput("shell ls -al /dev/block/by-name/");
                            AppendHiddenRootLogStatus("成功", "OK");

                            // 存在 lk / lk_a / lk_b 分区 -> 不刷入 (小米系高通bootloader特征)
                            bool hasLK = System.Text.RegularExpressions.Regex.IsMatch(partInfo, @"\blk(?:_a|_b)?\s*->");
                            // 天玑(MTK)平台特征分区 (preloader/proinfo/nvram/md1img) -> 不刷入
                            bool isMTK = System.Text.RegularExpressions.Regex.IsMatch(partInfo, @"\b(preloader|proinfo|nvram|md1img)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                            if (hasLK || isMTK)
                            {
                                List<string> skipReasons = new List<string>();
                                if (hasLK) skipReasons.Add("存在 lk/lk_a/lk_b 分区");
                                if (isMTK) skipReasons.Add("天玑(MTK)平台分区特征");
                                AddHiddenRootLog("提示", $"跳过 PathMask 模块刷入: {string.Join(" + ", skipReasons)}");
                            }
                            else
                            {
                                // 根据内核版本 + 安卓版本选择对应的 PathMask 模块
                                selectedPathmaskModule = SelectPathmaskModule(kernelVersion, androidVersion, extractPath);
                                if (selectedPathmaskModule != null)
                                {
                                    AddHiddenRootLog("信息", $"匹配到 PathMask 模块: {Path.GetFileName(selectedPathmaskModule)}");
                                }
                                else
                                {
                                    AddHiddenRootLog("警告", "未匹配到对应内核的 PathMask 模块，将跳过安装");
                                }
                            }
                        }

                        UpdateHiddenRootProgress(38, "正在创建手机目录");

                        // 4. 在手机创建violet文件夹
                        AddHiddenRootLog("信息", "创建手机目录...");
                        await ExecuteAdbCommand($"{suCommand} \"rm -rf /storage/emulated/0/violet\"");
                        await ExecuteAdbCommand($"{suCommand} \"mkdir -p /storage/emulated/0/violet\"");
                        await ExecuteAdbCommand($"{suCommand} \"chmod 777 /storage/emulated/0/violet\"");
                        AppendHiddenRootLogStatus("成功", "Done");
                        UpdateHiddenRootProgress(40, "正在准备ADB推送");

                        // 5. 推送文件到手机
                        AddHiddenRootLog("信息", "推送文件到手机...");

                        // 获取所有文件（包括子目录中的文件），但排除.apk文件和pathmask内核模块目录
                        string pathmaskDir = Path.Combine(extractPath, "pathmask");
                        string[] allFiles = Directory.GetFiles(extractPath, "*.*", SearchOption.AllDirectories)
                            .Where(f => !f.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                            .Where(f => !f.StartsWith(pathmaskDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            .ToArray();

                        if (allFiles.Length == 0)
                        {
                            AddHiddenRootLog("错误", "解压后未找到任何文件");
                            FailHiddenRootProgress("资源包中没有可推送文件");
                            return;
                        }

                        int totalPushFileCount = allFiles.Length + (selectedPathmaskModule != null ? 1 : 0);
                        int currentPushFileIndex = 0;
                        long totalPushBytes = allFiles.Sum(file => new FileInfo(file).Length)
                            + (selectedPathmaskModule != null ? new FileInfo(selectedPathmaskModule).Length : 0);
                        long completedPushBytes = 0;

                        foreach (string file in allFiles)
                        {
                            string fileName = Path.GetFileName(file);
                            long fileSize = new FileInfo(file).Length;
                            long fileStartBytes = completedPushBytes;
                            currentPushFileIndex++;

                            (bool Success, string Output) pushResult = await ExecuteAdbPushWithProgressAsync(
                                file,
                                $"/storage/emulated/0/violet/{fileName}",
                                filePercent =>
                                {
                                    double transferredBytes = fileStartBytes + (fileSize * filePercent / 100d);
                                    double transferPercent = totalPushBytes > 0
                                        ? transferredBytes * 100d / totalPushBytes
                                        : currentPushFileIndex * 100d / totalPushFileCount;
                                    UpdateHiddenRootProgress(
                                        40 + transferPercent * 0.22,
                                        $"ADB推送 {currentPushFileIndex}/{totalPushFileCount}: {fileName} ({filePercent:0}%)");
                                });

                            if (!pushResult.Success)
                            {
                                AddHiddenRootLog("错误", $"推送 {fileName} 失败: {pushResult.Output}");
                                FailHiddenRootProgress($"ADB推送失败: {fileName}");
                                return;
                            }

                            completedPushBytes += fileSize;
                        }

                        AppendHiddenRootLogStatus("成功", "Done");

                        // 推送匹配内核版本的 PathMask 模块
                        if (selectedPathmaskModule != null)
                        {
                            string pmFileName = Path.GetFileName(selectedPathmaskModule);
                            long fileSize = new FileInfo(selectedPathmaskModule).Length;
                            long fileStartBytes = completedPushBytes;
                            currentPushFileIndex++;
                            AddHiddenRootLog("信息", $"推送 PathMask 模块 {pmFileName}...");

                            (bool Success, string Output) pmPushResult = await ExecuteAdbPushWithProgressAsync(
                                selectedPathmaskModule,
                                $"/storage/emulated/0/violet/{pmFileName}",
                                filePercent =>
                                {
                                    double transferredBytes = fileStartBytes + (fileSize * filePercent / 100d);
                                    double transferPercent = totalPushBytes > 0
                                        ? transferredBytes * 100d / totalPushBytes
                                        : 100d;
                                    UpdateHiddenRootProgress(
                                        40 + transferPercent * 0.22,
                                        $"ADB推送 {currentPushFileIndex}/{totalPushFileCount}: {pmFileName} ({filePercent:0}%)");
                                });

                            if (!pmPushResult.Success)
                            {
                                AddHiddenRootLog("错误", $"PathMask 模块推送失败: {pmPushResult.Output}");
                                FailHiddenRootProgress($"ADB推送失败: {pmFileName}");
                                return;
                            }

                            completedPushBytes += fileSize;
                            AppendHiddenRootLogStatus("成功", "Done");
                        }

                        UpdateHiddenRootProgress(62, "ADB推送完成，正在设置权限");

                        // 设置文件权限
                        AddHiddenRootLog("信息", "设置文件权限...");
                        await ExecuteAdbCommand($"{suCommand} \"chmod -R 777 /storage/emulated/0/violet/*\"");

                        AppendHiddenRootLogStatus("成功", "OK");
                        UpdateHiddenRootProgress(65, "正在验证文件完整性");

                        // 验证文件是否推送成功
                        AddHiddenRootLog("信息", "验证文件完整性...");

                        // 检查必需的模块文件是否存在
                        var requiredModules = new List<string>
                        {
                            "1.zip",
                            "2.zip",
                            "3.zip",
                            "5.zip",
                            "target.txt"
                        };
                        if (isSUkiSUGKI) requiredModules.Add("8.zip");
                        if (AddRescueModuleCheckBox?.IsChecked == true) requiredModules.Add("9.zip");
                        if (InstallZip6CheckBox?.IsChecked == true) requiredModules.Add("6.zip");
                   
                        bool allFilesExist = true;

                        foreach (string module in requiredModules)
                        {
                            string checkResult = await ExecuteAdbCommandWithOutput($"shell ls /storage/emulated/0/violet/{module} 2>&1");
                            if (checkResult.Contains("No such file") || checkResult.Contains("does not exist"))
                            {
                                allFilesExist = false;
                                break;
                            }
                        }

                        if (!allFilesExist)
                        {
                            AppendHiddenRootLogStatus("错误", "推送不完整");
                            FailHiddenRootProgress("文件完整性校验失败");
                            return;
                        }

                        AppendHiddenRootLogStatus("成功", "Done");
                        UpdateHiddenRootProgress(68, "正在安装模块");

                        // 6. 安装模块 (1.zip, 2.zip, 3.zip, 5.zip)
                        string[] firstBatchModules = { "1.zip", "2.zip", "3.zip", "5.zip" };
                        int totalModuleCount = firstBatchModules.Length
                            + (selectedPathmaskModule != null ? 1 : 0)
                            + (isSUkiSUGKI ? 1 : 0)
                            + (AddRescueModuleCheckBox?.IsChecked == true ? 1 : 0)
                            + (InstallZip6CheckBox?.IsChecked == true ? 1 : 0);
                        int installedModuleCount = 0;

                        foreach (string module in firstBatchModules)
                        {
                            if (!await InstallHiddenRootModuleAsync(module, suCommand, moduleInstallCommand))
                            {
                                FailHiddenRootProgress($"模块安装失败: {module}");
                                return;
                            }

                            installedModuleCount++;
                            UpdateHiddenRootProgress(
                                68 + installedModuleCount * 12d / totalModuleCount,
                                $"正在安装模块 {installedModuleCount}/{totalModuleCount}");
                            await Task.Delay(1000);
                        }

                        // 6.0 安装内核匹配的 PathMask 模块
                        if (selectedPathmaskModule != null)
                        {
                            string pmFileName = Path.GetFileName(selectedPathmaskModule);
                            if (!await InstallHiddenRootModuleAsync(pmFileName, suCommand, moduleInstallCommand))
                            {
                                FailHiddenRootProgress($"模块安装失败: {pmFileName}");
                                return;
                            }

                            installedModuleCount++;
                            UpdateHiddenRootProgress(
                                68 + installedModuleCount * 12d / totalModuleCount,
                                $"正在安装模块 {installedModuleCount}/{totalModuleCount}");
                            await Task.Delay(1000);
                        }

                        // 6.1 GKI模式额外安装8.zip
                        if (isSUkiSUGKI)
                        {
                            if (!await InstallHiddenRootModuleAsync("8.zip", suCommand, moduleInstallCommand))
                            {
                                FailHiddenRootProgress("模块安装失败: 8.zip");
                                return;
                            }

                            installedModuleCount++;
                            UpdateHiddenRootProgress(
                                68 + installedModuleCount * 12d / totalModuleCount,
                                $"正在安装模块 {installedModuleCount}/{totalModuleCount}");
                            await Task.Delay(1000);
                        }

                        // 6.2 如果勾选了添加救砖模块，安装9.zip
                        if (AddRescueModuleCheckBox?.IsChecked == true)
                        {
                            if (!await InstallHiddenRootModuleAsync("9.zip", suCommand, moduleInstallCommand))
                            {
                                FailHiddenRootProgress("模块安装失败: 9.zip");
                                return;
                            }

                            installedModuleCount++;
                            UpdateHiddenRootProgress(
                                68 + installedModuleCount * 12d / totalModuleCount,
                                $"正在安装模块 {installedModuleCount}/{totalModuleCount}");
                            await Task.Delay(1000);
                        }

                        // 6.3 可选安装 LSP 模块 6.zip。
                        if (InstallZip6CheckBox?.IsChecked == true)
                        {
                            if (!await InstallHiddenRootModuleAsync("6.zip", suCommand, moduleInstallCommand))
                            {
                                FailHiddenRootProgress("模块安装失败: 6.zip");
                                return;
                            }

                            installedModuleCount++;
                            UpdateHiddenRootProgress(
                                68 + installedModuleCount * 12d / totalModuleCount,
                                $"正在安装模块 {installedModuleCount}/{totalModuleCount}");
                            await Task.Delay(1000);
                        }

                        // 7. 首次替换target.txt文件
                        if (!await ReplaceHiddenRootKeyFileAsync())
                        {
                            FailHiddenRootProgress("替换密钥文件失败");
                            return;
                        }

                        UpdateHiddenRootProgress(82, "正在重启设备");

                        // 8. 重启设备
                        AddHiddenRootLog("信息", "重启设备...");
                        await ExecuteAdbCommand("reboot");
                        AppendHiddenRootLogStatus("成功", "OK");
                        UpdateHiddenRootProgress(85, "等待设备重新连接");

                        // 12. 监听设备重启
                        AddHiddenRootLog("信息", "等待设备重新连接...");
                        bool hasDetectedTargetDevice = false;
                        int maxWaitTime = 100;
                        int waitCount = 0;
                        bool storageWaitingShown = false;

                        while (!hasDetectedTargetDevice && waitCount < maxWaitTime)
                        {
                            await Task.Delay(3000);
                            waitCount++;
                            UpdateHiddenRootProgress(
                                85 + Math.Min(waitCount, maxWaitTime) * 7d / maxWaitTime,
                                $"等待设备重新连接 ({waitCount}/{maxWaitTime})");

                            try
                            {
                                string devicesCheck = await ExecuteAdbCommandWithOutput("devices");
                                if (devicesCheck.Contains("\tdevice"))
                                {
                                    // 设备已连接，检查是否能访问violet文件夹中的文件
                                    string checkFile = await ExecuteAdbCommandWithOutput("shell ls /storage/emulated/0/violet/target.txt 2>&1");
                            //因为变更核心文件这里的探针也改成了配置文件
                                    if (!checkFile.Contains("No such file") && !checkFile.Contains("does not exist") && !checkFile.Contains("ls:"))
                                    {
                                        hasDetectedTargetDevice = true;
                                        AppendHiddenRootLogStatus("成功", "OK");
                                        UpdateHiddenRootProgress(92, "设备已恢复，正在扫描风险文件");
                                    }
                                    else
                                    {
                                        if (!storageWaitingShown)
                                        {
                                            AppendHiddenRootLogStatus("成功", "已连接");
                                            AddHiddenRootLog("信息", "等待设备存储挂载...");
                                            storageWaitingShown = true;
                                        }
                                    }
                                }
                            }
                            catch { }
                        }

                        if (!hasDetectedTargetDevice)
                        {
                            AppendHiddenRootLogStatus("错误", "超时");
                            FailHiddenRootProgress("等待设备重启超时");
                            return;
                        }

                        await Task.Delay(5000);

                        // 9. 扫描并移除风险文件
                        UpdateHiddenRootProgress(94, "正在扫描风险文件");
                        AddHiddenRootLog("信息", "扫描风险文件...");

                        List<string> riskFiles = new List<string>();

                        // 扫描风险文件
                        string[] scanDirs = { "/storage/emulated/0/", "/storage/emulated/0/Download/" };
                        foreach (string dir in scanDirs)
                        {
                            // 扫描.img文件
                            string imgFiles = await ExecuteAdbCommandWithOutput($"shell find {dir} -maxdepth 1 -name '*.img' 2>/dev/null");
                            if (!string.IsNullOrEmpty(imgFiles))
                            {
                                string[] imgFileList = imgFiles.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                                foreach (string imgFile in imgFileList)
                                {
                                    string trimmedFile = imgFile.Trim();
                                    if (!string.IsNullOrEmpty(trimmedFile))
                                    {
                                        riskFiles.Add(trimmedFile);
                                    }
                                }
                            }

                            // 扫描.json文件
                            string jsonFiles = await ExecuteAdbCommandWithOutput($"shell find {dir} -maxdepth 1 -name '*.json' 2>/dev/null");
                            if (!string.IsNullOrEmpty(jsonFiles))
                            {
                                string[] jsonFileList = jsonFiles.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                                foreach (string jsonFile in jsonFileList)
                                {
                                    string trimmedFile = jsonFile.Trim();
                                    if (!string.IsNullOrEmpty(trimmedFile))
                                    {
                                        riskFiles.Add(trimmedFile);
                                    }
                                }
                            }

                            // 扫描.xml文件
                            string xmlFiles = await ExecuteAdbCommandWithOutput($"shell find {dir} -maxdepth 1 -name '*.xml' 2>/dev/null");
                            if (!string.IsNullOrEmpty(xmlFiles))
                            {
                                string[] xmlFileList = xmlFiles.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                                foreach (string xmlFile in xmlFileList)
                                {
                                    string trimmedFile = xmlFile.Trim();
                                    if (!string.IsNullOrEmpty(trimmedFile))
                                    {
                                        riskFiles.Add(trimmedFile);
                                    }
                                }
                            }

                            // 扫描.zip文件
                            string zipFiles = await ExecuteAdbCommandWithOutput($"shell find {dir} -maxdepth 1 -name '*.zip' 2>/dev/null");
                            if (!string.IsNullOrEmpty(zipFiles))
                            {
                                string[] zipFileList = zipFiles.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                                foreach (string zipFile in zipFileList)
                                {
                                    string trimmedFile = zipFile.Trim();
                                    if (!string.IsNullOrEmpty(trimmedFile))
                                    {
                                        riskFiles.Add(trimmedFile);
                                    }
                                }
                            }

                            // 扫描target.txt文件
                            string targetFile = await ExecuteAdbCommandWithOutput($"shell find {dir} -maxdepth 1 -name 'target.txt' 2>/dev/null");
                            if (!string.IsNullOrEmpty(targetFile))
                            {
                                string[] targetFileList = targetFile.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                                foreach (string target in targetFileList)
                                {
                                    string trimmedFile = target.Trim();
                                    if (!string.IsNullOrEmpty(trimmedFile))
                                    {
                                        riskFiles.Add(trimmedFile);
                                    }
                                }
                            }
                        }

                        // 检查MT2文件夹
                        string mt2Check = await ExecuteAdbCommandWithOutput("shell ls /storage/emulated/0/MT2/ 2>&1");
                        if (!mt2Check.Contains("No such file") && !mt2Check.Contains("does not exist"))
                        {
                            riskFiles.Add("/storage/emulated/0/MT2/");
                        }

                        AppendHiddenRootLogStatus(riskFiles.Count == 0 ? "成功" : "警告", $"{riskFiles.Count} 项");
                        UpdateHiddenRootProgress(96, "正在移除风险文件");

                        // 移除风险文件
                        AddHiddenRootLog("信息", "移除风险文件...");
                        foreach (string riskFile in riskFiles)
                        {
                            if (riskFile.EndsWith("/"))
                            {
                                await ExecuteAdbCommand($"shell rm -rf \"{riskFile}\"");
                            }
                            else
                            {
                                await ExecuteAdbCommand($"shell rm -f \"{riskFile}\"");
                            }
                        }
                        AppendHiddenRootLogStatus("成功", "Done");
                        UpdateHiddenRootProgress(97, "正在启动 HMA-OSS");


                        // 11. 打开隐藏应用列表
                        AddHiddenRootLog("信息", "启动 HMA-OSS...");
                        await ExecuteAdbCommand("shell monkey -p org.frknkrc44.hma_oss 1");
                        AppendHiddenRootLogStatus("成功", "OK");
                        UpdateHiddenRootProgress(98, "正在再次替换密钥文件");

                        // HMA-OSS启动后再次替换，确保重启后的目标文件生效
                        if (!await ReplaceHiddenRootKeyFileAsync())
                        {
                            FailHiddenRootProgress("再次替换密钥文件失败");
                            return;
                        }

                        UpdateHiddenRootProgress(99, "正在清理临时文件");
                        await Task.Delay(5000);

                        // 12. 删除violet目录
                        AddHiddenRootLog("信息", "清理临时文件...");
                        await ExecuteAdbCommand("shell rm -rf /storage/emulated/0/violet/");
                        AppendHiddenRootLogStatus("成功", "OK");
                        UpdateHiddenRootProgress(100, "隐藏 ROOT 流程完成");

                // 13. 完成
                AddHiddenRootLog("成功", "隐藏 ROOT 流程完成");

                //这里的提示是因为真没招了，magisk默认申请就弹窗，导致只能在这里提示一下

                // 14. Magisk Alpha 额外提示
                if (is28104Version || is29000Version)
                {
                    AddHiddenRootLog("提示", "检测到 Magisk：请在超级用户列表中向 WsuWebUI 授予 ROOT 权限，并在 HMA-OSS 中授予应用列表权限后重启设备");


                }
            }
                    catch (Exception ex)
                    {
                        FailHiddenRootProgress(ex.Message);
                        AddHiddenRootLog("错误", $"操作过程中发生错误: {ex.Message}");
                        System.Windows.MessageBox.Show($"操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        StartHiddenRootButton.IsEnabled = true;
                    }
                }

        // 根据内核版本和安卓版本，从解压目录的pathmask文件夹中选择匹配的模块
        // 文件名格式: a<安卓大版本>-<内核主.次版本>.zip (如 a15-6.6.zip)
        // 匹配优先级: 内核+安卓都匹配 > 仅内核匹配(选安卓最接近的)
        private string? SelectPathmaskModule(string kernelVersion, string androidVersion, string extractPath)
        {
            try
            {
                string pathmaskDir = Path.Combine(extractPath, "pathmask");
                if (!Directory.Exists(pathmaskDir)) return null;

                // 解析内核主版本号 (如 "6.6.127-4k-g46a034eca005-dirty" -> "6.6")
                var kernelMatch = System.Text.RegularExpressions.Regex.Match(kernelVersion.Trim(), @"^(\d+)\.(\d+)");
                if (!kernelMatch.Success) return null;
                string kernelPrefix = $"{kernelMatch.Groups[1].Value}.{kernelMatch.Groups[2].Value}";

                // 解析安卓大版本: 优先从内核字符串提取 androidNN 标记
                // (如 5.15.167-android13-8-00014-... -> 13, 一次性同时得到内核版本和安卓版本)
                int androidMajor = 0;
                var kernelAndroidMatch = System.Text.RegularExpressions.Regex.Match(kernelVersion, @"android(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (kernelAndroidMatch.Success)

                {
                    int.TryParse(kernelAndroidMatch.Groups[1].Value, out androidMajor);
                }

                // 兜底: getprop ro.build.version.release (如 "15" / "14" / "13.0", 用于自编译内核等无androidNN标记的情况)
                if (androidMajor == 0)
                {
                    var androidMatch = System.Text.RegularExpressions.Regex.Match(androidVersion.Trim(), @"^(\d+)");
                    if (androidMatch.Success) int.TryParse(androidMatch.Groups[1].Value, out androidMajor);
                }

                // 扫描 pathmask 目录, 动态收集候选 (新增版本zip无需改代码)
                var candidates = new List<(string FilePath, int Android, string Kernel)>();
                foreach (string file in Directory.GetFiles(pathmaskDir, "a*.zip", SearchOption.TopDirectoryOnly))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(Path.GetFileNameWithoutExtension(file), @"^a(\d+)-(\d+\.\d+)$");
                    if (!m.Success) continue;
                    if (!int.TryParse(m.Groups[1].Value, out int android)) continue;
                    candidates.Add((file, android, m.Groups[2].Value));
                }
                if (candidates.Count == 0) return null;

                // 第一优先级: 内核 + 安卓都匹配
                var exact = candidates.FirstOrDefault(c => c.Kernel == kernelPrefix && c.Android == androidMajor);
                if (!string.IsNullOrEmpty(exact.FilePath)) return exact.FilePath;

                // 第二优先级: 仅内核匹配, 选安卓版本最接近的 (如 5.10 内核同时有 a12/a13)
                var kernelOnly = candidates.Where(c => c.Kernel == kernelPrefix)
                    .OrderBy(c => Math.Abs(c.Android - androidMajor))
                    .ToList();
                if (kernelOnly.Count > 0) return kernelOnly[0].FilePath;

                return null;
            }
            catch (Exception ex)
            {
                AddHiddenRootLog("警告", $"选择PathMask模块失败: {ex.Message}");
                return null;
            }
        }

        private void UpdateHiddenRootProgress(double value, string status)
        {
            void Update()
            {
                HiddenRootProgressBar.Value = Math.Clamp(value, 0, 100);
                HiddenRootProgressBar.Tag = status;
            }

            if (Dispatcher.CheckAccess())
            {
                Update();
            }
            else
            {
                Dispatcher.Invoke(Update);
            }
        }

        private void FailHiddenRootProgress(string status)
        {
            UpdateHiddenRootProgress(HiddenRootProgressBar.Value, $"失败: {status}");
        }

        private async Task<(bool Success, string Output)> ExecuteAdbPushWithProgressAsync(
            string localPath,
            string remotePath,
            Action<double> onProgress)
        {
            try
            {
                string adbPath = GetToolPath("adb.exe");
                string selectedSerial = GetSelectedDeviceSerial();
                var startInfo = new ProcessStartInfo
                {
                    FileName = adbPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = new UTF8Encoding(false),
                    StandardErrorEncoding = new UTF8Encoding(false),
                    WorkingDirectory = File.Exists(adbPath)
                        ? (Path.GetDirectoryName(adbPath) ?? AppDomain.CurrentDomain.BaseDirectory)
                        : AppDomain.CurrentDomain.BaseDirectory
                };

                if (!string.IsNullOrWhiteSpace(selectedSerial))
                {
                    startInfo.ArgumentList.Add("-s");
                    startInfo.ArgumentList.Add(selectedSerial);
                }

                startInfo.ArgumentList.Add("push");
                startInfo.ArgumentList.Add(localPath);
                startInfo.ArgumentList.Add(remotePath);

                using Process? process = Process.Start(startInfo);
                if (process == null)
                {
                    return (false, "ADB进程无法启动");
                }

                var standardOutput = new StringBuilder();
                var standardError = new StringBuilder();
                int lastReportedPercent = -1;
                object progressLock = new object();

                void ReportProgress(int percent)
                {
                    percent = Math.Clamp(percent, 0, 100);
                    lock (progressLock)
                    {
                        if (percent <= lastReportedPercent)
                        {
                            return;
                        }

                        lastReportedPercent = percent;
                    }

                    onProgress(percent);
                }

                async Task ReadProgressStreamAsync(StreamReader reader, StringBuilder output)
                {
                    char[] buffer = new char[256];
                    string tail = string.Empty;

                    while (true)
                    {
                        int read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }

                        string chunk = new string(buffer, 0, read);
                        output.Append(chunk);

                        string progressSource = tail + chunk;
                        foreach (System.Text.RegularExpressions.Match match in
                                 System.Text.RegularExpressions.Regex.Matches(progressSource, @"(?<!\d)(\d{1,3})%"))
                        {
                            if (int.TryParse(match.Groups[1].Value, out int percent))
                            {
                                ReportProgress(percent);
                            }
                        }

                        tail = progressSource.Length > 64 ? progressSource[^64..] : progressSource;
                    }
                }

                Task outputTask = ReadProgressStreamAsync(process.StandardOutput, standardOutput);
                Task errorTask = ReadProgressStreamAsync(process.StandardError, standardError);
                await Task.WhenAll(process.WaitForExitAsync(), outputTask, errorTask).ConfigureAwait(false);

                string outputText = string.Join(Environment.NewLine, new[]
                {
                    standardOutput.ToString().Trim(),
                    standardError.ToString().Trim()
                }.Where(text => !string.IsNullOrWhiteSpace(text)));

                if (process.ExitCode == 0)
                {
                    ReportProgress(100);
                    return (true, outputText);
                }

                return (false, string.IsNullOrWhiteSpace(outputText) ? $"adb push退出码: {process.ExitCode}" : outputText);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        // 执行ADB命令并返回标准输出与错误输出。
        private async Task<string> ExecuteAdbCommandWithOutput(
            string command,
            CancellationToken cancellationToken = default)
        {
            Process? process = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                string adbPath = GetToolPath("adb.exe");
                string selectedSerial = GetSelectedDeviceSerial();
                string arguments = string.IsNullOrWhiteSpace(selectedSerial)
                    ? command
                    : $"-s {selectedSerial} {command}";

                var startInfo = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = File.Exists(adbPath)
                        ? (Path.GetDirectoryName(adbPath) ?? AppDomain.CurrentDomain.BaseDirectory)
                        : AppDomain.CurrentDomain.BaseDirectory
                };

                process = Process.Start(startInfo);
                if (process == null)
                {
                    return "Error: Process could not be started.";
                }

                Task<string> outputTask;
                Task<string> errorTask;
                using (var outputReader = new StreamReader(
                           process.StandardOutput.BaseStream,
                           new UTF8Encoding(false),
                           true))
                using (var errorReader = new StreamReader(
                           process.StandardError.BaseStream,
                           new UTF8Encoding(false),
                           true))
                {
                    outputTask = outputReader.ReadToEndAsync();
                    errorTask = errorReader.ReadToEndAsync();

                    await process.WaitForExitAsync(cancellationToken);
                    await Task.WhenAll(outputTask, errorTask);
                }

                string output = await outputTask;
                string error = await errorTask;
                return string.Join(
                    Environment.NewLine,
                    new[] { output.Trim(), error.Trim() }
                        .Where(text => !string.IsNullOrWhiteSpace(text)));
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
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
            finally
            {
                process?.Dispose();
            }
        }

        private void FlashLogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void XiaomiFlashScriptPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void WipeAndLockBLCheckBox_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void CompleteWipeCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox checkBox && checkBox.IsChecked == true)
            {
                var keepDataCheckBox = this.FindName("KeepDataCheckBox") as System.Windows.Controls.CheckBox;
                var wipeAndLockBLCheckBox = this.FindName("WipeAndLockBLCheckBox") as System.Windows.Controls.CheckBox;
                
                if (keepDataCheckBox != null) keepDataCheckBox.IsChecked = false;
                if (wipeAndLockBLCheckBox != null) wipeAndLockBLCheckBox.IsChecked = false;
            }
        }

        private void KeepDataCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox checkBox && checkBox.IsChecked == true)
            {
                var completeWipeCheckBox = this.FindName("CompleteWipeCheckBox") as System.Windows.Controls.CheckBox;
                var wipeAndLockBLCheckBox = this.FindName("WipeAndLockBLCheckBox") as System.Windows.Controls.CheckBox;
                
                if (completeWipeCheckBox != null) completeWipeCheckBox.IsChecked = false;
                if (wipeAndLockBLCheckBox != null) wipeAndLockBLCheckBox.IsChecked = false;
            }
        }

        private void WipeAndLockBLCheckBox_Checked_1(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.CheckBox checkBox && checkBox.IsChecked == true)
            {
                var completeWipeCheckBox = this.FindName("CompleteWipeCheckBox") as System.Windows.Controls.CheckBox;
                var keepDataCheckBox = this.FindName("KeepDataCheckBox") as System.Windows.Controls.CheckBox;
                
                if (completeWipeCheckBox != null) completeWipeCheckBox.IsChecked = false;
                if (keepDataCheckBox != null) keepDataCheckBox.IsChecked = false;
            }
        }

        private void FlashLogTextBox_TextChanged_1(object sender, TextChangedEventArgs e)
        {

        }

        private void AutoRebootCheckBox_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void FlashLogTextBox_TextChanged_2(object sender, TextChangedEventArgs e)
        {
            // 自动滚动到底部
            if (sender is System.Windows.Controls.TextBox textBox)
            {
                // 确保外层ScrollViewer也滚动到底部
                var scrollViewer = FindParent<ScrollViewer>(textBox);
                if (scrollViewer != null)
                {
                    scrollViewer.ScrollToBottom();
                }
            }
        }

        private void GridSplitter_DragDelta_1(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {

        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void CheckBox_Checked_1(object sender, RoutedEventArgs e)
        {

        }

        // 比特率滑块值变化事件
        private void BitrateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var bitrateValueText = this.FindName("BitrateValueText") as TextBlock;
            if (bitrateValueText != null)
            {
                bitrateValueText.Text = ((int)e.NewValue).ToString();
            }
        }

        // 最大帧率滑块值变化事件
        private void MaxFpsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var maxFpsValueText = this.FindName("MaxFpsValueText") as TextBlock;
            if (maxFpsValueText != null)
            {
                maxFpsValueText.Text = ((int)e.NewValue).ToString();
            }
        }

        // 登录缓存滑块值变化事件
        private void LoginCacheSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var loginCacheValueText = this.FindName("LoginCacheValueText") as TextBlock;
            if (loginCacheValueText != null)
            {
                loginCacheValueText.Text = ((int)e.NewValue).ToString();
            }
        }

        private void CheckBox_Checked_2(object sender, RoutedEventArgs e)
        {

        }

        private void ScreenMirrorLogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 日志由批量刷新器统一滚动，避免每次 TextChanged 重复布局。
        }

        // 启动scrcpy进程并捕获输出日志
        private void StartScrcpyWithLogging()
        {
            try
            {
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
                    
                    int maxSize = GetWindowMaxSize();
                    string selectedSerial = GetSelectedDeviceSerial();
                    
                    // 构建scrcpy命令参数
                    string arguments = $"--video-bit-rate {bitrate}M --max-fps {maxFps} --max-size {maxSize}";
                    if (!string.IsNullOrEmpty(selectedSerial))
                    {
                        arguments = $"-s {selectedSerial} " + arguments;
                    }
                    
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
                    }
                    else
                    {
                        AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] ERROR: 启动scrcpy失败！\n");
                    }
                }
                else
                {
                    AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] ERROR: 未找到scrcpy.exe文件，请检查platform-tools目录！\n");
                }
            }
            catch (Exception ex)
            {
                AppendToLogTextBox($"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n");
            }
        }

        // 向日志文本框添加内容的辅助方法
        private void AppendToLogTextBox(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            screenMirrorLogQueue.Enqueue(message);
            if (Interlocked.CompareExchange(ref screenMirrorLogPumpActive, 1, 0) != 0)
            {
                return;
            }

            void StartLogPump()
            {
                if (screenMirrorLogFlushTimer == null)
                {
                    screenMirrorLogFlushTimer = new DispatcherTimer(DispatcherPriority.Background)
                    {
                        Interval = TimeSpan.FromMilliseconds(100)
                    };
                    screenMirrorLogFlushTimer.Tick += (s, e) => FlushScreenMirrorLogQueue();
                }

                screenMirrorLogFlushTimer.Start();
            }

            if (Dispatcher.CheckAccess())
            {
                StartLogPump();
            }
            else
            {
                try
                {
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(StartLogPump));
                }
                catch
                {
                    Interlocked.Exchange(ref screenMirrorLogPumpActive, 0);
                }
            }
        }

        private void FlushScreenMirrorLogQueue()
        {
            var batch = new StringBuilder();
            while (batch.Length < ScreenMirrorLogMaxBatchCharacters &&
                   screenMirrorLogQueue.TryDequeue(out string? message))
            {
                batch.Append(message);
            }

            var logTextBox = this.FindName("ScreenMirrorLogTextBox") as System.Windows.Controls.TextBox;
            if (logTextBox != null && batch.Length > 0)
            {
                string batchText = batch.ToString();
                if (logTextBox.Text.Length + batchText.Length <= ScreenMirrorLogMaxCharacters)
                {
                    logTextBox.AppendText(batchText);
                }
                else
                {
                    string combined = logTextBox.Text + batchText;
                    int startIndex = Math.Max(0, combined.Length - ScreenMirrorLogMaxCharacters);
                    int firstLineBreak = combined.IndexOf('\n', startIndex);
                    if (firstLineBreak >= startIndex && firstLineBreak < combined.Length - 1)
                    {
                        startIndex = firstLineBreak + 1;
                    }

                    logTextBox.Text = combined.Substring(startIndex);
                    logTextBox.CaretIndex = logTextBox.Text.Length;
                }

                logTextBox.ScrollToEnd();
            }

            if (!screenMirrorLogQueue.IsEmpty)
            {
                return;
            }

            screenMirrorLogFlushTimer?.Stop();
            Interlocked.Exchange(ref screenMirrorLogPumpActive, 0);

            // 处理停止计时器和释放标志之间刚好到达的新日志。
            if (!screenMirrorLogQueue.IsEmpty &&
                Interlocked.CompareExchange(ref screenMirrorLogPumpActive, 1, 0) == 0)
            {
                screenMirrorLogFlushTimer?.Start();
            }
        }

        private void PartitionTableDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 查找ListBox控件
                var listBox = this.FindName("FileListTextBox") as System.Windows.Controls.ListBox;
                if (listBox == null)
                {
                    // 如果没有找到命名的ListBox，尝试查找所有ListBox
                    listBox = FindVisualChild<System.Windows.Controls.ListBox>(this);
                }

                if (listBox != null)
                {
                    listBox.Items.Clear();
                    listBox.Items.Add(new FileItem { Name = "正在加载根目录文件...", IsFile = false });
                }

                // 获取程序目录中的adb.exe路径
                string programDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string adbPath = Path.Combine(programDirectory, "platform-tools", "adb.exe");

                // 更新上次加载的目录路径和当前路径
                lastLoadedDirectory = "/sdcard/";
                currentPath = "/sdcard/";
                
                // 更新路径显示
                var pathTextBox = this.FindName("CurrentPathTextBox") as System.Windows.Controls.TextBox;
                if (pathTextBox != null)
                {
                    pathTextBox.Text = currentPath;
                }
                
                // 创建进程启动信息
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = "shell ls /sdcard/",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
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
                        
                        string combinedMessage = $"{error}\n{output}".Trim();

                        if (IsAdbDeviceDisconnectedError(combinedMessage))
                        {
                            listBox.Items.Add(new FileItem { Name = "设备未连接", IsFile = false });
                        }
                        else if (!string.IsNullOrEmpty(error))
                        {
                            listBox.Items.Add(new FileItem { Name = $"错误: {error}", IsFile = false });
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
                        }
                        else
                        {
                            listBox.Items.Add(new FileItem { Name = "未找到文件或目录为空", IsFile = false });
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
                    string errorMessage = IsAdbDeviceDisconnectedError(ex.Message)
                        ? "设备未连接"
                        : $"执行命令时发生错误: {ex.Message}";
                    listBox.Items.Add(new FileItem { Name = errorMessage, IsFile = false });
                }
            }
        }

        private static SolidColorBrush GetHiddenRootLogBrush(string color)
        {
            return color switch
            {
                "Red" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38)),
                "Amber" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(217, 119, 6)),
                "Green" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 163, 74)),
                "Blue" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235)),
                "Purple" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(124, 58, 237)),
                "Muted" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139)),
                "Timestamp" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184)),
                _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85))
            };
        }

        private static string NormalizeHiddenRootLogText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "--";
            }

            string normalized = string.Join(" / ", text
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Distinct(StringComparer.OrdinalIgnoreCase));

            if (normalized.EndsWith("ing...", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(0, normalized.Length - 6).TrimEnd() + " ...";
            }
            else if (normalized.EndsWith("...", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(0, normalized.Length - 3).TrimEnd() + " ...";
            }

            return normalized;
        }

        private static string GetHiddenRootLogTag(string level, string message)
        {
            if (level == "错误") return "[Error]";
            if (level == "警告") return "[Warning]";
            if (level == "提示") return "[Notice]";
            if (level == "成功") return "[Success]";
            if (level == "系统") return "[System]";

            if (message.Contains("文件选择", StringComparison.Ordinal) || message.Contains("用户选择", StringComparison.Ordinal)) return "[Selection]";
            if (message.Contains("设备连接", StringComparison.Ordinal) || message.Contains("检查", StringComparison.Ordinal) || message.Contains("验证", StringComparison.Ordinal)) return "[Checking]";
            if (message.Contains("查询", StringComparison.Ordinal) || message.Contains("ROOT管理器类型", StringComparison.Ordinal)) return "[Reading]";
            if (message.Contains("匹配", StringComparison.Ordinal)) return "[Matching]";
            if (message.Contains("重命名", StringComparison.Ordinal) || message.Contains("解压", StringComparison.Ordinal) || message.Contains("创建", StringComparison.Ordinal)) return "[Preparing]";
            if (message.Contains("推送", StringComparison.Ordinal)) return "[Pushing]";
            if (message.Contains("安装", StringComparison.Ordinal) || message.StartsWith("Install", StringComparison.OrdinalIgnoreCase)) return "[Installing]";
            if (message.Contains("重启设备", StringComparison.Ordinal)) return "[Rebooting]";
            if (message.Contains("等待", StringComparison.Ordinal) || message.Contains("监控", StringComparison.Ordinal)) return "[Waiting]";
            if (message.Contains("扫描", StringComparison.Ordinal) || message.Contains("找到", StringComparison.Ordinal)) return "[Scanning]";
            if (message.Contains("移除", StringComparison.Ordinal) || message.Contains("清理", StringComparison.Ordinal)) return "[Cleaning]";
            if (message.Contains("打开", StringComparison.Ordinal) || message.Contains("启动", StringComparison.Ordinal)) return "[Launching]";
            if (message.Contains("替换", StringComparison.Ordinal) || message.Contains("设置", StringComparison.Ordinal)) return "[Configuring]";
            return "[Info]";
        }

        private static SolidColorBrush GetHiddenRootTagBrush(string level)
        {
            return level switch
            {
                "错误" => GetHiddenRootLogBrush("Red"),
                "警告" => GetHiddenRootLogBrush("Amber"),
                "提示" => GetHiddenRootLogBrush("Blue"),
                "成功" => GetHiddenRootLogBrush("Green"),
                _ => GetHiddenRootLogBrush("Purple")
            };
        }

        private static SolidColorBrush GetHiddenRootBodyBrush(string level)
        {
            return level switch
            {
                "错误" => GetHiddenRootLogBrush("Red"),
                "警告" => GetHiddenRootLogBrush("Amber"),
                "提示" => GetHiddenRootLogBrush("Muted"),
                "成功" => GetHiddenRootLogBrush("Green"),
                _ => GetHiddenRootLogBrush("Body")
            };
        }

        private void AddHiddenRootLog(string level, string message)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (HiddenEnvironmentLogTextBox == null) return;

                    var paragraph = new Paragraph
                    {
                        Margin = new Thickness(0, 0.5, 0, 0.5),
                        LineHeight = 19
                    };

                    paragraph.Inlines.Add(new Run($"[{DateTime.Now:HH:mm:ss}] ")
                    {
                        Foreground = GetHiddenRootLogBrush("Timestamp")
                    });

                    paragraph.Inlines.Add(new Run(GetHiddenRootLogTag(level, message) + " ")
                    {
                        Foreground = GetHiddenRootTagBrush(level),
                        FontWeight = FontWeights.SemiBold
                    });

                    paragraph.Inlines.Add(new Run(NormalizeHiddenRootLogText(message))
                    {
                        Foreground = GetHiddenRootBodyBrush(level),
                        FontWeight = level is "成功" or "错误" ? FontWeights.SemiBold : FontWeights.Normal
                    });

                    HiddenEnvironmentLogTextBox.Document.PagePadding = new Thickness(0);
                    HiddenEnvironmentLogTextBox.Document.Blocks.Add(paragraph);
                    HiddenEnvironmentLogTextBox.ScrollToEnd();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"日志输出失败: {ex.Message}");
                }
            });
        }

        // 在同一行追加日志状态（用于显示OK/Done等状态）
        private void AppendHiddenRootLogStatus(string level, string status)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    if (HiddenEnvironmentLogTextBox == null || HiddenEnvironmentLogTextBox.Document.Blocks.Count == 0) return;

                    if (HiddenEnvironmentLogTextBox.Document.Blocks.LastBlock is Paragraph lastParagraph && lastParagraph.Inlines.Count > 0)
                    {
                        string normalizedStatus = NormalizeHiddenRootLogText(status);
                        if (level == "成功" && normalizedStatus.Equals("Done", StringComparison.OrdinalIgnoreCase))
                        {
                            normalizedStatus = "OK";
                        }

                        var statusRun = new Run(" " + normalizedStatus)
                        {
                            Foreground = level switch
                            {
                                "成功" => GetHiddenRootLogBrush("Green"),
                                "错误" => GetHiddenRootLogBrush("Red"),
                                "警告" => GetHiddenRootLogBrush("Amber"),
                                "提示" or "信息" => GetHiddenRootLogBrush("Blue"),
                                _ => GetHiddenRootLogBrush("Body")
                            },
                            FontWeight = level is "成功" or "错误" ? FontWeights.Bold : FontWeights.SemiBold
                        };

                        lastParagraph.Inlines.Add(statusRun);
                        HiddenEnvironmentLogTextBox.ScrollToEnd();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"日志状态追加失败: {ex.Message}");
                }
            });
        }

        private void DetectApp2CheckBox_Checked()
        {

        }

        private void DetectApp5CheckBox_Checked(object sender, RoutedEventArgs e)
        {

        }

    }

}
