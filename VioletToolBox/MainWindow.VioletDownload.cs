using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SmartTool;

namespace WpfApp1
{
    public partial class MainWindow
    {
        private static readonly Regex VioletDownloadPercentRegex = new(@"\((?<value>\d{1,3})%\)", RegexOptions.Compiled);
        private static readonly Regex VioletDownloadSpeedRegex = new(@"DL:(?<value>[^\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex VioletDownloadEtaRegex = new(@"ETA:(?<value>[^\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private Process? _violetDownloadAria2Process;
        private bool _violetDownloadIsStoppingProcess;
        private bool _violetDownloadIsPaused;
        private bool _violetDownloadInitialized;
        private string? _violetDownloadActiveUrl;
        private string _violetDownloadCurrentSpeed = "";
        private string _violetDownloadCurrentEta = "";

        private void InitializeVioletDownload()
        {
            if (_violetDownloadInitialized)
            {
                return;
            }

            _violetDownloadInitialized = true;
            VioletDownloadOutputPathTextBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) + Path.DirectorySeparatorChar;
            VioletDownloadProgressBar.Tag = "等待开始下载...";
            VioletDownloadProgressTextBlock.Text = "等待开始下载...";
            UpdateVioletDownloadUiState(isRunning: false);
            Closing += VioletDownloadWindow_Closing;
        }

        private async void VioletDownloadWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (_violetDownloadAria2Process is null || _violetDownloadAria2Process.HasExited)
            {
                return;
            }

            e.Cancel = true;
            Closing -= VioletDownloadWindow_Closing;
            await StopVioletDownloadCurrentProcessAsync("正在关闭下载进程...");
            Close();
        }

        private async void VioletDownloadStartButton_Click(object sender, RoutedEventArgs e)
        {
            var url = VioletDownloadUrlTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                System.Windows.MessageBox.Show("请先输入初始下载链接。", "缺少链接", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!EnsureVioletDownloadOutputPath())
            {
                return;
            }

            VioletDownloadReplacementUrlTextBox.Clear();
            await StartVioletDownloadAsync(url, "开始下载");
        }

        private async void VioletDownloadResumeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_violetDownloadAria2Process is not null && !_violetDownloadAria2Process.HasExited)
            {
                AppendVioletDownloadLog("下载进程仍在运行，无需重复继续。");
                return;
            }

            var url = !string.IsNullOrWhiteSpace(VioletDownloadReplacementUrlTextBox.Text)
                ? VioletDownloadReplacementUrlTextBox.Text.Trim()
                : !string.IsNullOrWhiteSpace(_violetDownloadActiveUrl)
                    ? _violetDownloadActiveUrl
                    : VioletDownloadUrlTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(url))
            {
                System.Windows.MessageBox.Show("没有可用于继续下载的链接。", "缺少链接", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!EnsureVioletDownloadOutputPath())
            {
                return;
            }

            await StartVioletDownloadAsync(url, _violetDownloadIsPaused ? "继续下载" : "重新尝试下载");
        }

        private async void VioletDownloadReplaceUrlButton_Click(object sender, RoutedEventArgs e)
        {
            var replacementUrl = VioletDownloadReplacementUrlTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(replacementUrl))
            {
                System.Windows.MessageBox.Show("请先粘贴新的动态链接。", "缺少新链接", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!EnsureVioletDownloadOutputPath())
            {
                return;
            }

            await StopVioletDownloadCurrentProcessAsync("准备切换到新链接...");
            await StartVioletDownloadAsync(replacementUrl, "使用新链接继续");
        }

        private async void VioletDownloadPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_violetDownloadAria2Process is null || _violetDownloadAria2Process.HasExited)
            {
                AppendVioletDownloadLog("当前没有运行中的下载任务。");
                return;
            }

            _violetDownloadIsPaused = true;
            await StopVioletDownloadCurrentProcessAsync("正在暂停下载...");
            AppendVioletDownloadLog("下载已暂停，可直接点击“继续”，或粘贴新链接后点击“使用新链接继续”。");
        }

        private void VioletDownloadBrowseOutputButton_Click(object sender, RoutedEventArgs e)
        {
            var currentPath = VioletDownloadOutputPathTextBox.Text.Trim();
            var directory = Directory.Exists(currentPath) ? currentPath : Path.GetDirectoryName(currentPath);

            var dialog = new OpenFolderDialog
            {
                Title = "选择保存目录",
                InitialDirectory = GetVioletDownloadExistingDirectory(directory)
            };

            if (dialog.ShowDialog(this) == true)
            {
                VioletDownloadOutputPathTextBox.Text = dialog.FolderName + Path.DirectorySeparatorChar;
            }
        }

        private void VioletDownloadOpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var outputPath = VioletDownloadOutputPathTextBox.Text.Trim();
            var folderPath = Directory.Exists(outputPath)
                ? outputPath
                : Path.GetDirectoryName(outputPath);

            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                System.Windows.MessageBox.Show("保存目录不存在。", "无法打开目录", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folderPath}\"",
                UseShellExecute = true
            });
        }

        private async Task StartVioletDownloadAsync(string url, string reason)
        {
            try
            {
                if (_violetDownloadAria2Process is not null && !_violetDownloadAria2Process.HasExited)
                {
                    await StopVioletDownloadCurrentProcessAsync("正在重启下载任务...");
                }

                var aria2Path = ResolveVioletDownloadAria2ExecutablePath();
                if (aria2Path is null)
                {
                    System.Windows.MessageBox.Show("未找到 aria2c.exe，请确认它已被复制到程序输出目录。", "缺少 aria2c", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var outputPath = VioletDownloadOutputPathTextBox.Text.Trim();
                var fullPath = NormalizeVioletDownloadOutputPath(outputPath);
                var isDirectory = fullPath.EndsWith(Path.DirectorySeparatorChar)
                    || fullPath.EndsWith(Path.AltDirectorySeparatorChar)
                    || Directory.Exists(fullPath);

                string outputDirectory;
                string outputFileName;

                if (isDirectory)
                {
                    outputDirectory = fullPath;
                    outputFileName = "";
                }
                else
                {
                    outputDirectory = Path.GetDirectoryName(fullPath) ?? fullPath;
                    outputFileName = Path.GetFileName(fullPath);
                }

                Directory.CreateDirectory(outputDirectory);

                _violetDownloadActiveUrl = url;
                _violetDownloadIsPaused = false;
                _violetDownloadCurrentSpeed = "";
                _violetDownloadCurrentEta = "";
                VioletDownloadProgressBar.BeginAnimation(System.Windows.Controls.ProgressBar.ValueProperty, null);
                VioletDownloadProgressBar.Value = 0;
                VioletDownloadProgressBar.Tag = "等待进度...";
                VioletDownloadProgressTextBlock.Text = "aria2c 已启动，正在等待下载进度...";
                UpdateVioletDownloadUiState(isRunning: true);

                AppendVioletDownloadLog($"{reason}：{url}");
                AppendVioletDownloadLog($"输出文件：{NormalizeVioletDownloadOutputPath(outputPath)}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = aria2Path,
                    Arguments = BuildVioletDownloadAria2Arguments(url, outputDirectory, outputFileName),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = outputDirectory
                };

                var process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };

                process.OutputDataReceived += (_, args) => HandleVioletDownloadProcessOutput(args.Data);
                process.ErrorDataReceived += (_, args) => HandleVioletDownloadProcessOutput(args.Data);
                process.Exited += (_, _) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        var exitCode = process.ExitCode;
                        _violetDownloadAria2Process = null;
                        UpdateVioletDownloadUiState(isRunning: false);

                        if (_violetDownloadIsStoppingProcess)
                        {
                            _violetDownloadIsStoppingProcess = false;
                            return;
                        }

                        if (exitCode == 0)
                        {
                            VioletDownloadProgressBar.BeginAnimation(System.Windows.Controls.ProgressBar.ValueProperty, null);
                            VioletDownloadProgressBar.Value = 100;
                            VioletDownloadProgressBar.Tag = "下载完成";
                            VioletDownloadProgressTextBlock.Text = "下载完成。";
                            AppendVioletDownloadLog("aria2c 退出：下载完成。");
                        }
                        else
                        {
                            VioletDownloadProgressBar.Tag = "等待新链接";
                            VioletDownloadProgressTextBlock.Text = "链接可能已失效。请获取新的动态链接后点击“使用新链接继续”。";
                            AppendVioletDownloadLog($"aria2c 异常退出，退出码：{exitCode}");
                        }
                    });

                    process.Dispose();
                };

                if (!process.Start())
                {
                    System.Windows.MessageBox.Show("aria2c 启动失败。", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateVioletDownloadUiState(isRunning: false);
                    return;
                }

                _violetDownloadAria2Process = process;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                UpdateVioletDownloadUiState(isRunning: false);
                AppendVioletDownloadLog($"启动下载失败：{ex.Message}");
                System.Windows.MessageBox.Show(ex.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task StopVioletDownloadCurrentProcessAsync(string reason)
        {
            var process = _violetDownloadAria2Process;
            if (process is null || process.HasExited)
            {
                return;
            }

            _violetDownloadIsStoppingProcess = true;
            AppendVioletDownloadLog(reason);

            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            catch (InvalidOperationException)
            {
            }
            catch (Exception ex)
            {
                AppendVioletDownloadLog($"停止 aria2c 失败：{ex.Message}");
            }
            finally
            {
                _violetDownloadAria2Process = null;
                UpdateVioletDownloadUiState(isRunning: false);
            }
        }

        private string BuildVioletDownloadAria2Arguments(string url, string outputDirectory, string outputFileName)
        {
            var split = ParseVioletDownloadSplitCount();
            var headers = ParseVioletDownloadHeaders();
            return Aria2DownloadService.BuildArguments(
                url,
                outputDirectory,
                outputFileName,
                split,
                headers);
        }

        private int ParseVioletDownloadSplitCount()
        {
            if (VioletDownloadConnectionsComboBox.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Content?.ToString(), out var selectedValue))
            {
                return Math.Clamp(selectedValue, 1, 32);
            }

            return 16;
        }

        private List<string> ParseVioletDownloadHeaders()
        {
            return VioletDownloadHeadersTextBox.Text
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
        }

        private void HandleVioletDownloadProcessOutput(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                AppendVioletDownloadLog(line);
                UpdateVioletDownloadProgressFromLog(line);
            });
        }

        private void UpdateVioletDownloadProgressFromLog(string line)
        {
            var percentMatch = VioletDownloadPercentRegex.Match(line);
            if (percentMatch.Success &&
                double.TryParse(percentMatch.Groups["value"].Value, out var percentValue))
            {
                VioletDownloadProgressBar.BeginAnimation(System.Windows.Controls.ProgressBar.ValueProperty, null);
                VioletDownloadProgressBar.Value = Math.Clamp(percentValue, 0, 100);
            }

            var speedMatch = VioletDownloadSpeedRegex.Match(line);
            if (speedMatch.Success)
            {
                _violetDownloadCurrentSpeed = speedMatch.Groups["value"].Value;
            }

            var etaMatch = VioletDownloadEtaRegex.Match(line);
            if (etaMatch.Success)
            {
                _violetDownloadCurrentEta = etaMatch.Groups["value"].Value;
            }

            if (percentMatch.Success || speedMatch.Success || etaMatch.Success)
            {
                UpdateVioletDownloadProgressText();
            }
        }

        private void VioletDownloadProgressBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateVioletDownloadProgressText();
        }

        private void UpdateVioletDownloadProgressText()
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(_violetDownloadCurrentSpeed))
            {
                parts.Add(_violetDownloadCurrentSpeed);
            }

            if (!string.IsNullOrEmpty(_violetDownloadCurrentEta))
            {
                parts.Add(_violetDownloadCurrentEta);
            }

            if (parts.Count > 0)
            {
                string text = string.Join("    ", parts);
                VioletDownloadProgressBar.Tag = text;
                VioletDownloadProgressTextBlock.Text = text;
            }
            else if (VioletDownloadProgressBar.Value <= 0)
            {
                VioletDownloadProgressBar.Tag = "等待开始下载...";
                VioletDownloadProgressTextBlock.Text = "等待开始下载...";
            }
        }

        private bool EnsureVioletDownloadOutputPath()
        {
            var outputPath = VioletDownloadOutputPathTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                System.Windows.MessageBox.Show("请先选择保存文件路径。", "缺少保存路径", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            try
            {
                var fullPath = NormalizeVioletDownloadOutputPath(outputPath);
                var directory = Path.GetDirectoryName(fullPath);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    System.Windows.MessageBox.Show("保存路径无效。", "路径错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                Directory.CreateDirectory(directory);
                VioletDownloadOutputPathTextBox.Text = fullPath;
                return true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "路径错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        private string NormalizeVioletDownloadOutputPath(string outputPath)
        {
            var fullPath = Path.GetFullPath(outputPath);
            var endsWithSeparator = outputPath.EndsWith(Path.DirectorySeparatorChar) || outputPath.EndsWith(Path.AltDirectorySeparatorChar);

            if (endsWithSeparator || Directory.Exists(fullPath))
            {
                var guessedName = GuessVioletDownloadFileNameFromUrl();
                if (!string.IsNullOrEmpty(guessedName))
                {
                    return Path.Combine(fullPath, guessedName);
                }

                return fullPath;
            }

            return fullPath;
        }

        private string GuessVioletDownloadFileNameFromUrl()
        {
            var candidateUrl = !string.IsNullOrWhiteSpace(VioletDownloadReplacementUrlTextBox.Text)
                ? VioletDownloadReplacementUrlTextBox.Text.Trim()
                : !string.IsNullOrWhiteSpace(_violetDownloadActiveUrl)
                    ? _violetDownloadActiveUrl
                    : VioletDownloadUrlTextBox.Text.Trim();

            if (Uri.TryCreate(candidateUrl, UriKind.Absolute, out var uri))
            {
                var name = Path.GetFileName(uri.AbsolutePath);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }

            return "";
        }

        private string? ResolveVioletDownloadAria2ExecutablePath()
        {
            return Aria2DownloadService.ResolveExecutablePath();
        }

        private static string GetVioletDownloadExistingDirectory(string? directory)
        {
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                return directory;
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        private void UpdateVioletDownloadUiState(bool isRunning)
        {
            VioletDownloadStartButton.IsEnabled = !isRunning;
            VioletDownloadPauseButton.IsEnabled = isRunning;
            VioletDownloadResumeButton.IsEnabled = !isRunning;
            VioletDownloadReplaceUrlButton.IsEnabled = true;
        }

        private void AppendVioletDownloadLog(string message)
        {
            VioletDownloadLogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            VioletDownloadLogTextBox.ScrollToEnd();
        }
    }
}
