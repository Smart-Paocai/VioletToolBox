using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfImage = System.Windows.Controls.Image;
using WpfBorder = System.Windows.Controls.Border;
using WpfGrid = System.Windows.Controls.Grid;
using WpfWrapPanel = System.Windows.Controls.WrapPanel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinFormsFolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;
using WinFormsSaveFileDialog = System.Windows.Forms.SaveFileDialog;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfMessageBoxResult = System.Windows.MessageBoxResult;

namespace WpfApp1
{
    public partial class MainWindow : Window
    {
        private const string PhonePhotoPath = "/sdcard/DCIM/Camera/";
        private static readonly string[] PhoneVideoPaths = new[]
        {
            "/sdcard/DCIM/Camera/",
            "/sdcard/Movies/",
            "/sdcard/Videos/",
            "/sdcard/Pictures/Screenshots/",
            "/sdcard/Movies/ScreenRecord/",
            "/sdcard/Download/"
        };
        
        private List<string> _phonePhotoFiles = new List<string>();
        private List<string> _phoneVideoFiles = new List<string>();
        private Dictionary<string, BitmapImage> _imageCache = new Dictionary<string, BitmapImage>();
        private bool _isPreloaded = false;
        private HashSet<string> _selectedImages = new HashSet<string>();

        private string GetPreferredAdbPath()
        {
            string adbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform-tools", "adb.exe");
            return File.Exists(adbPath) ? adbPath : "adb";
        }

        private void BackupAssistantButton_Click(object sender, RoutedEventArgs e)
        {
            HideAllViews();
            var backupAssistantView = this.FindName("BackupAssistantView") as WpfGrid;
            if (backupAssistantView != null) backupAssistantView.Visibility = Visibility.Visible;

            try
            {
                var method = this.GetType().GetMethod("UpdateButtonStates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                method?.Invoke(this, new object[] { "BackupAssistant" });
                
                var field = this.GetType().GetField("currentView", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                field?.SetValue(this, "BackupAssistant");
            }
            catch { }
            
            _ = LoadPhonePhotoListAsync();
        }

        private void HideAllViews()
        {
            var homeView = this.FindName("HomeView") as WpfGrid;
            var screenMirrorView = this.FindName("ScreenMirrorView") as WpfGrid;
            var basicFlashView = this.FindName("BasicFlashView") as WpfGrid;
            var fastbootVisualizationView = this.FindName("FastbootVisualizationView") as WpfGrid;
            var hiddenEnvironmentView = this.FindName("HiddenEnvironmentView") as WpfGrid;
            var downloadView = this.FindName("DownloadView") as WpfGrid;
            var aboutToolView = this.FindName("AboutToolView") as WpfGrid;
            var systemZoneView = this.FindName("SystemZoneView") as WpfGrid;
            var oujiaFlashView = this.FindName("OujiaFlashView") as WpfGrid;
            var autorootView = this.FindName("AutorootView") as WpfGrid;
            var appManagementView = this.FindName("AppManagementView") as WpfGrid;
            var androidGeneralView = this.FindName("AndroidGeneralView") as WpfGrid;
            var payloadView = this.FindName("PayloadView") as WpfGrid;
            var romDownloadView = this.FindName("RomDownloadview") as WpfGrid;
            var edlFlashView = this.FindName("EdlFlashView") as WpfGrid;
            var colorOSAssistantView = this.FindName("ColorOSAssistantView") as WpfGrid;
            var backupAssistantView = this.FindName("BackupAssistantView") as WpfGrid;
            var violetDownloadView = this.FindName("VioletDownloadView") as WpfGrid;

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
            if (violetDownloadView != null) violetDownloadView.Visibility = Visibility.Collapsed;
        }

        private void BtnLoadImages_Click(object sender, RoutedEventArgs e)
        {
            var photoGallery = this.FindName("photoGallery") as WpfWrapPanel;
            
            if (_currentMode == "photos")
            {
                if (_isPreloaded)
                {
                    _imageCache.Clear();
                    _selectedImages.Clear();
                    _isPreloaded = false;
                    photoGallery?.Children.Clear();
                }
                
                _ = PreloadAllImagesAsync();
            }
            else if (_currentMode == "videos")
            {
                _selectedImages.Clear();
                DisplayVideoGallery();
            }
            else if (_currentMode == "contacts")
            {
                _ = ExportContactsAsync();
            }
        }

        private async Task LoadPhonePhotoListAsync()
        {
            var lblStatus = this.FindName("lblStatus") as WpfTextBlock;
            var btnLoadImages = this.FindName("btnLoadImages") as WpfButton;
            
            if (lblStatus != null) lblStatus.Text = "正在检测ADB设备...";

            var deviceId = await Task.Run(() => GetConnectedDeviceId());
            if (string.IsNullOrEmpty(deviceId))
            {
                if (lblStatus != null) lblStatus.Text = "错误：未检测到安卓设备！";
                WpfMessageBox.Show("请确认：\n1. 手机已开启USB调试\n2. 已授权电脑访问\n3. ADB能正常识别设备",
                    "设备检测失败", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                return;
            }

            if (lblStatus != null) lblStatus.Text = "正在加载手机图片列表...";
            _phonePhotoFiles = await Task.Run(() => GetPhonePhotoFiles());
            if (_phonePhotoFiles.Count == 0)
            {
                if (lblStatus != null) lblStatus.Text = "未找到图片（路径：/sdcard/DCIM/Camera）";
                return;
            }

            if (lblStatus != null) lblStatus.Text = $"检测到 {_phonePhotoFiles.Count} 张图片，点击「加载图片」按钮开始预加载";
            if (btnLoadImages != null) btnLoadImages.IsEnabled = true;
        }

        private string GetConnectedDeviceId()
        {
            try
            {
                string adbPath = GetPreferredAdbPath();
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = "devices",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var lines = output.Trim().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Contains("\tdevice") && !line.StartsWith("List"))
                    {
                        return line.Split('\t')[0].Trim();
                    }
                }
                return "";
            }
            catch
            {
                return "";
            }
        }

        private List<string> GetPhonePhotoFiles()
        {
            var files = new List<string>();
            try
            {
                string adbPath = GetPreferredAdbPath();
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = $"shell ls {PhonePhotoPath}",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                var lines = output.Trim().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var fileName = line.Trim();
                    if (!string.IsNullOrEmpty(fileName) &&
                        (fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                         fileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                         fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) &&
                        !fileName.Equals("Raw", StringComparison.OrdinalIgnoreCase))
                    {
                        files.Add(fileName);
                    }
                }
                return files;
            }
            catch
            {
                return files;
            }
        }

        private async Task PreloadAllImagesAsync()
        {
            if (_phonePhotoFiles.Count == 0) return;

            var lblStatus = this.FindName("lblStatus") as WpfTextBlock;
            var progressBar = this.FindName("progressBar") as WpfProgressBar;
            var btnLoadImages = this.FindName("btnLoadImages") as WpfButton;
            var cbThreadCount = this.FindName("cbThreadCount") as WpfComboBox;
            var btnToggleSelectAll = this.FindName("btnToggleSelectAll") as WpfButton;

            int threadCount = 32;
            if (cbThreadCount?.SelectedItem is WpfComboBoxItem selectedItem)
            {
                if (int.TryParse(selectedItem.Content.ToString(), out int count))
                {
                    threadCount = count;
                }
            }

            if (lblStatus != null) lblStatus.Text = $"开始预加载所有图片到内存（并发数：{threadCount}）...";
            if (progressBar != null)
            {
                progressBar.Visibility = Visibility.Visible;
                progressBar.Maximum = _phonePhotoFiles.Count;
                progressBar.Value = 0;
            }

            if (btnLoadImages != null) btnLoadImages.IsEnabled = false;
            if (cbThreadCount != null) cbThreadCount.IsEnabled = false;

            InitializeEmptyGallery();

            int successCount = 0;
            int failCount = 0;
            var lockObj = new object();
            var startTime = DateTime.Now;

            await Task.Run(() =>
            {
                Parallel.ForEach(_phonePhotoFiles, new ParallelOptions { MaxDegreeOfParallelism = threadCount }, (fileName) =>
                {
                    try
                    {
                        var image = LoadImageFromPhone(fileName);
                        if (image != null)
                        {
                            lock (_imageCache)
                            {
                                _imageCache[fileName] = image;
                            }
                            lock (lockObj)
                            {
                                successCount++;
                            }

                            Dispatcher.Invoke(() => UpdateGalleryImage(fileName, image));
                        }
                        else
                        {
                            lock (lockObj)
                            {
                                failCount++;
                            }
                        }
                    }
                    catch
                    {
                        lock (lockObj)
                        {
                            failCount++;
                        }
                    }

                    Dispatcher.Invoke(() =>
                    {
                        if (progressBar != null) progressBar.Value++;
                        var total = successCount + failCount;
                        var elapsed = (DateTime.Now - startTime).TotalSeconds;
                        var speed = elapsed > 0 ? total / elapsed : 0;
                        if (lblStatus != null) lblStatus.Text = $"预加载中：{total}/{_phonePhotoFiles.Count} (成功:{successCount} 失败:{failCount}) 速度:{speed:F1}张/秒";
                    });
                });
            });

            var totalTime = (DateTime.Now - startTime).TotalSeconds;
            if (progressBar != null) progressBar.Visibility = Visibility.Collapsed;
            _isPreloaded = true;

            if (btnLoadImages != null)
            {
                btnLoadImages.IsEnabled = true;
                btnLoadImages.Content = "🔄 重新加载";
            }
            if (cbThreadCount != null) cbThreadCount.IsEnabled = true;
            if (btnToggleSelectAll != null) btnToggleSelectAll.IsEnabled = true;

            if (lblStatus != null)
            {
                if (failCount == 0)
                {
                    lblStatus.Text = $"预加载完成！共{successCount}张图片已缓存到内存，耗时 {totalTime:F1} 秒";
                }
                else
                {
                    lblStatus.Text = $"预加载完成：成功{successCount}张，失败{failCount}张，耗时 {totalTime:F1} 秒";
                }
            }
        }

        private void InitializeEmptyGallery()
        {
            var photoGallery = this.FindName("photoGallery") as WpfWrapPanel;
            if (photoGallery == null) return;

            photoGallery.Children.Clear();

            foreach (var fileName in _phonePhotoFiles)
            {
                var border = new WpfBorder
                {
                    Width = 175,
                    Height = 175,
                    Margin = new Thickness(1),
                    BorderBrush = System.Windows.Media.Brushes.LightGray,
                    BorderThickness = new Thickness(2),
                    Background = System.Windows.Media.Brushes.WhiteSmoke,
                    Tag = fileName
                };

                var grid = new WpfGrid();

                var loadingText = new WpfTextBlock
                {
                    Text = "加载中...",
                    FontSize = 14,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = "loading"
                };

                var label = new WpfTextBlock
                {
                    Text = fileName.Length > 20 ? fileName.Substring(0, 17) + "..." : fileName,
                    FontSize = 10,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(150, 240, 240, 240)),
                    Padding = new Thickness(5, 2, 5, 2),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    TextAlignment = TextAlignment.Center
                };

                grid.Children.Add(loadingText);
                grid.Children.Add(label);
                border.Child = grid;

                photoGallery.Children.Add(border);
            }
        }

        private void UpdateGalleryImage(string fileName, BitmapImage image)
        {
            var photoGallery = this.FindName("photoGallery") as WpfWrapPanel;
            if (photoGallery == null) return;

            WpfBorder? targetBorder = null;
            foreach (var child in photoGallery.Children)
            {
                if (child is WpfBorder border && border.Tag?.ToString() == fileName)
                {
                    targetBorder = border;
                    break;
                }
            }

            if (targetBorder == null) return;

            var grid = targetBorder.Child as WpfGrid;
            if (grid == null) return;

            var loadingText = grid.Children.OfType<WpfTextBlock>().FirstOrDefault(t => t.Tag?.ToString() == "loading");
            if (loadingText != null)
            {
                grid.Children.Remove(loadingText);
            }

            var img = new WpfImage
            {
                Source = image,
                Stretch = Stretch.UniformToFill,
                Width = 171,
                Height = 171
            };

            var checkMark = new WpfTextBlock
            {
                Text = "✓",
                FontSize = 40,
                Foreground = System.Windows.Media.Brushes.White,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 0, 120, 215)),
                Width = 60,
                Height = 60,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new Thickness(0, 5, 5, 0),
                Visibility = Visibility.Collapsed,
                Tag = "checkmark"
            };

            var label = grid.Children.OfType<WpfTextBlock>().FirstOrDefault(t => t.Tag?.ToString() != "checkmark");
            if (label != null)
            {
                label.Foreground = System.Windows.Media.Brushes.White;
                label.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(150, 0, 0, 0));
            }

            grid.Children.Insert(0, img);
            grid.Children.Add(checkMark);

            targetBorder.Background = System.Windows.Media.Brushes.White;
            targetBorder.BorderBrush = System.Windows.Media.Brushes.Gray;

            targetBorder.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 1)
                {
                    ToggleImageSelection(fileName, targetBorder);
                    e.Handled = true;
                }
            };
        }

        private BitmapImage? LoadImageFromPhone(string fileName)
        {
            try
            {
                string adbPath = GetPreferredAdbPath();
                var phoneFullPath = $"{PhonePhotoPath}{fileName}";
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = $"exec-out cat {phoneFullPath}",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                using (var ms = new MemoryStream())
                {
                    process.StandardOutput.BaseStream.CopyTo(ms);
                    process.WaitForExit();

                    if (ms.Length == 0) return null;

                    ms.Position = 0;
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch
            {
                return null;
            }
        }

        private void ToggleImageSelection(string fileName, WpfBorder border)
        {
            var btnBackup = this.FindName("btnBackup") as WpfButton;
            var lblStatus = this.FindName("lblStatus") as WpfTextBlock;
            
            var grid = border.Child as WpfGrid;
            if (grid == null) return;

            var checkMark = grid.Children.OfType<WpfTextBlock>().FirstOrDefault(t => t.Tag?.ToString() == "checkmark");
            if (checkMark == null) return;

            if (_selectedImages.Contains(fileName))
            {
                _selectedImages.Remove(fileName);
                checkMark.Visibility = Visibility.Collapsed;
                border.BorderBrush = System.Windows.Media.Brushes.Gray;
            }
            else
            {
                _selectedImages.Add(fileName);
                checkMark.Visibility = Visibility.Visible;
                border.BorderBrush = System.Windows.Media.Brushes.DodgerBlue;
            }

            if (btnBackup != null) btnBackup.IsEnabled = _selectedImages.Count > 0;
            if (lblStatus != null) lblStatus.Text = $"已选中 {_selectedImages.Count} 张图片";
            UpdateToggleSelectAllButton();
        }

        private void BtnToggleSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedImages.Count == _phonePhotoFiles.Count)
            {
                DeselectAll();
            }
            else
            {
                SelectAll();
            }
        }

        private void SelectAll()
        {
            var photoGallery = this.FindName("photoGallery") as WpfWrapPanel;
            var btnBackup = this.FindName("btnBackup") as WpfButton;
            var lblStatus = this.FindName("lblStatus") as WpfTextBlock;
            
            _selectedImages.Clear();
            
            if (_currentMode == "photos")
            {
                foreach (var fileName in _phonePhotoFiles)
                {
                    _selectedImages.Add(fileName);
                }
            }
            else if (_currentMode == "videos")
            {
                foreach (var videoPath in _phoneVideoFiles)
                {
                    _selectedImages.Add(videoPath);
                }
            }

            if (photoGallery != null)
            {
                foreach (WpfBorder border in photoGallery.Children)
                {
                    var grid = border.Child as WpfGrid;
                    if (grid == null) continue;

                    var checkMark = grid.Children.OfType<WpfTextBlock>().FirstOrDefault(t => t.Tag?.ToString() == "checkmark");
                    if (checkMark != null)
                    {
                        checkMark.Visibility = Visibility.Visible;
                        border.BorderBrush = System.Windows.Media.Brushes.DodgerBlue;
                    }
                }
            }

            if (btnBackup != null) btnBackup.IsEnabled = true;
            
            var itemType = _currentMode == "photos" ? "图片" : "视频";
            if (lblStatus != null) lblStatus.Text = $"已全选 {_selectedImages.Count} 个{itemType}";
            UpdateToggleSelectAllButton();
        }

        private void DeselectAll()
        {
            var photoGallery = this.FindName("photoGallery") as WpfWrapPanel;
            var btnBackup = this.FindName("btnBackup") as WpfButton;
            var lblStatus = this.FindName("lblStatus") as WpfTextBlock;
            
            _selectedImages.Clear();

            if (photoGallery != null)
            {
                foreach (WpfBorder border in photoGallery.Children)
                {
                    var grid = border.Child as WpfGrid;
                    if (grid == null) continue;

                    var checkMark = grid.Children.OfType<WpfTextBlock>().FirstOrDefault(t => t.Tag?.ToString() == "checkmark");
                    if (checkMark != null)
                    {
                        checkMark.Visibility = Visibility.Collapsed;
                        border.BorderBrush = System.Windows.Media.Brushes.Gray;
                    }
                }
            }

            if (btnBackup != null) btnBackup.IsEnabled = false;
            if (lblStatus != null) lblStatus.Text = "已取消全选";
            UpdateToggleSelectAllButton();
        }

        private void UpdateToggleSelectAllButton()
        {
            var btnToggleSelectAll = this.FindName("btnToggleSelectAll") as WpfButton;
            if (btnToggleSelectAll == null) return;

            int totalCount = _currentMode == "photos" ? _phonePhotoFiles.Count : _phoneVideoFiles.Count;

            if (_selectedImages.Count == totalCount && totalCount > 0)
            {
                btnToggleSelectAll.Content = "✗ 取消全选";
            }
            else
            {
                btnToggleSelectAll.Content = "✓ 全选";
            }
        }

        private async void BtnBackup_Click(object sender, RoutedEventArgs e)
        {
            var selectedFiles = _selectedImages.ToList();

            if (selectedFiles.Count == 0)
            {
                var itemType = _currentMode == "photos" ? "图片" : "视频";
                WpfMessageBox.Show($"请先选择要备份的{itemType}", "提示", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
                return;
            }

            string? savePath = null;
            using (var fbd = new WinFormsFolderBrowserDialog())
            {
                var itemType = _currentMode == "photos" ? "图片" : "视频";
                fbd.Description = $"请选择{itemType}保存位置";
                fbd.ShowNewFolderButton = true;
                fbd.SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                if (fbd.ShowDialog() == WinFormsDialogResult.OK)
                {
                    savePath = fbd.SelectedPath;
                }
                else
                {
                    return;
                }
            }

            if (string.IsNullOrEmpty(savePath) || !Directory.Exists(savePath))
            {
                WpfMessageBox.Show("保存路径无效！", "错误", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                return;
            }

            var btnBackup = this.FindName("btnBackup") as WpfButton;
            var progressBar = this.FindName("progressBar") as WpfProgressBar;
            var lblStatus = this.FindName("lblStatus") as WpfTextBlock;

            if (btnBackup != null) btnBackup.IsEnabled = false;
            if (progressBar != null)
            {
                progressBar.Visibility = Visibility.Visible;
                progressBar.Maximum = selectedFiles.Count;
                progressBar.Value = 0;
            }

            int successCount = 0;
            var failFiles = new List<string>();

            await Task.Run(() =>
            {
                string adbPath = GetPreferredAdbPath();
                foreach (var file in selectedFiles)
                {
                    try
                    {
                        string phonePath;
                        string fileName;
                        
                        if (_currentMode == "photos")
                        {
                            phonePath = $"{PhonePhotoPath}{file}";
                            fileName = file;
                        }
                        else
                        {
                            phonePath = file;
                            fileName = Path.GetFileName(file);
                        }
                        
                        var localPath = Path.Combine(savePath, fileName);

                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = adbPath,
                                Arguments = $"pull \"{phonePath}\" \"{localPath}\"",
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        process.Start();
                        process.WaitForExit();

                        if (process.ExitCode == 0)
                        {
                            successCount++;
                        }
                        else
                        {
                            failFiles.Add(fileName);
                        }

                        Dispatcher.Invoke(() =>
                        {
                            if (progressBar != null) progressBar.Value++;
                            if (lblStatus != null) lblStatus.Text = $"备份中：{fileName} ({progressBar.Value}/{selectedFiles.Count})";
                        });
                    }
                    catch
                    {
                        failFiles.Add(_currentMode == "photos" ? file : Path.GetFileName(file));
                        Dispatcher.Invoke(() =>
                        {
                            if (progressBar != null) progressBar.Value++;
                        });
                    }
                }
            });

            if (progressBar != null) progressBar.Visibility = Visibility.Collapsed;
            if (btnBackup != null) btnBackup.IsEnabled = true;

            var itemTypeName = _currentMode == "photos" ? "图片" : "视频";
            
            if (failFiles.Count == 0)
            {
                WpfMessageBox.Show($"全部{successCount}个{itemTypeName}备份成功！\n保存路径：{savePath}",
                    "备份完成", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
            }
            else
            {
                var failMsg = $"成功：{successCount} 失败：{failFiles.Count}\n失败文件：\n{string.Join("\n", failFiles.Take(10))}";
                if (failFiles.Count > 10)
                {
                    failMsg += $"\n... 还有 {failFiles.Count - 10} 个失败";
                }
                WpfMessageBox.Show(failMsg, "备份完成（部分失败）", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
            }
            
            if (lblStatus != null) lblStatus.Text = $"备份完成：成功{successCount}，失败{failFiles.Count}";
        }

        #region 右侧导航栏
        private string _currentMode = "photos";

        // 导航到图片
        private void BtnNavPhotos_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMode == "photos") return;
            
            var btnNavPhotos = this.FindName("btnNavPhotos") as WpfButton;
            UpdateNavigationButtonStyle(btnNavPhotos);
            _currentMode = "photos";
            
            SwitchToPhotosMode();
        }

        // 导航到视频
        private async void BtnNavVideos_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMode == "videos") return;
            
            var btnNavVideos = this.FindName("btnNavVideos") as WpfButton;
            UpdateNavigationButtonStyle(btnNavVideos);
            _currentMode = "videos";
            
            await SwitchToVideosMode();
        }

        // 导航到通讯录
        private async void BtnNavContacts_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMode == "contacts") return;
            
            var btnNavContacts = this.FindName("btnNavContacts") as WpfButton;
            UpdateNavigationButtonStyle(btnNavContacts);
            _currentMode = "contacts";
            
            await SwitchToContactsMode();
        }

        // 切换到图片模式
        private void SwitchToPhotosMode()
        {
            var lblStatus = this.FindName("lblStatus") as WpfTextBlock;
            var btnLoadImages = this.FindName("btnLoadImages") as WpfButton;
            var btnBackup = this.FindName("btnBackup") as WpfButton;
            var btnRestoreContacts = this.FindName("btnRestoreContacts") as WpfButton;
            var photoGallery = this.FindName("photoGallery") as WpfWrapPanel;

            if (lblStatus != null) lblStatus.Text = "图片模式";
            if (btnLoadImages != null) btnLoadImages.Content = "🔄 加载图片";
            if (btnBackup != null)
            {
                btnBackup.Content = "💾 备份选中图片";
                btnBackup.Visibility = Visibility.Visible;
            }
            
            if (btnRestoreContacts != null) btnRestoreContacts.Visibility = Visibility.Collapsed;
            
            if (photoGallery != null)
            {
                photoGallery.Children.Clear();
                if (_imageCache.Count > 0)
                {
                    DisplayPhotoGallery();
                }
            }
        }

        // 切换到视频模式
        private async Task SwitchToVideosMode()
        {
            var lblStatus = this.FindName("lblStatus") as WpfTextBlock;
            var btnLoadImages = this.FindName("btnLoadImages") as WpfButton;
            var btnBackup = this.FindName("btnBackup") as WpfButton;
            var btnToggleSelectAll = this.FindName("btnToggleSelectAll") as WpfButton;
            var btnRestoreContacts = this.FindName("btnRestoreContacts") as WpfButton;
            var photoGallery = this.FindName("photoGallery") as WpfWrapPanel;

            if (lblStatus != null) lblStatus.Text = "正在检测视频文件...";
            if (btnLoadImages != null)
            {
                btnLoadImages.Content = "🔄 加载视频";
                btnLoadImages.IsEnabled = false;
            }
            if (btnBackup != null)
            {
                btnBackup.Content = "💾 备份选中视频";
                btnBackup.Visibility = Visibility.Visible;
            }
            if (btnToggleSelectAll != null) btnToggleSelectAll.IsEnabled = false;
            if (btnRestoreContacts != null) btnRestoreContacts.Visibility = Visibility.Collapsed;
            
            if (photoGallery != null) photoGallery.Children.Clear();
            
            await LoadPhoneVideoListAsync();
        }

        // 切换到通讯录模式
        private async Task SwitchToContactsMode()
        {
            var lblStatus = this.FindName("lblStatus") as WpfTextBlock;
            var btnLoadImages = this.FindName("btnLoadImages") as WpfButton;
            var btnBackup = this.FindName("btnBackup") as WpfButton;
            var btnToggleSelectAll = this.FindName("btnToggleSelectAll") as WpfButton;
            var btnRestoreContacts = this.FindName("btnRestoreContacts") as WpfButton;
            var photoGallery = this.FindName("photoGallery") as WpfWrapPanel;

            if (lblStatus != null) lblStatus.Text = "通讯录模式";
            if (btnLoadImages != null)
            {
                btnLoadImages.Content = "📥 导出通讯录";
                btnLoadImages.IsEnabled = true;
            }
            if (btnBackup != null) btnBackup.Visibility = Visibility.Collapsed;
            if (btnToggleSelectAll != null) btnToggleSelectAll.IsEnabled = false;
            
            if (btnRestoreContacts != null)
            {
                btnRestoreContacts.Visibility = Visibility.Visible;
                btnRestoreContacts.IsEnabled = true;
            }
            
            if (photoGallery != null) photoGallery.Children.Clear();
            
            await Task.CompletedTask;
        }

        // 更新导航按钮样式
        private void UpdateNavigationButtonStyle(WpfButton activeButton)
        {
            var btnNavPhotos = this.FindName("btnNavPhotos") as WpfButton;
            var btnNavVideos = this.FindName("btnNavVideos") as WpfButton;
            var btnNavContacts = this.FindName("btnNavContacts") as WpfButton;

            if (btnNavPhotos != null)
            {
                btnNavPhotos.Background = System.Windows.Media.Brushes.White;
                btnNavPhotos.Foreground = System.Windows.Media.Brushes.Black;
            }
            if (btnNavVideos != null)
            {
                btnNavVideos.Background = System.Windows.Media.Brushes.White;
                btnNavVideos.Foreground = System.Windows.Media.Brushes.Black;
            }
            if (btnNavContacts != null)
            {
                btnNavContacts.Background = System.Windows.Media.Brushes.White;
                btnNavContacts.Foreground = System.Windows.Media.Brushes.Black;
            }

            if (activeButton != null)
            {
                activeButton.Background = System.Windows.Media.Brushes.DodgerBlue;
                activeButton.Foreground = System.Windows.Media.Brushes.White;
            }
        }

        // 恢复通讯录按钮点击事件
        private async void BtnRestoreContacts_Click(object sender, RoutedEventArgs e)
        {
            var lblStatus = this.FindName("lblStatus") as WpfTextBlock;
            var btnRestoreContacts = this.FindName("btnRestoreContacts") as WpfButton;
            var progressBar = this.FindName("progressBar") as WpfProgressBar;

            // 检查ADB设备
            if (lblStatus != null) lblStatus.Text = "正在检测ADB设备...";
            var deviceId = await Task.Run(() => GetConnectedDeviceId());
            if (string.IsNullOrEmpty(deviceId))
            {
                if (lblStatus != null) lblStatus.Text = "错误：未检测到安卓设备！";
                WpfMessageBox.Show("请确认：\n1. 手机已开启USB调试\n2. 已授权电脑访问\n3. ADB能正常识别设备",
                    "设备检测失败", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                return;
            }

            // 打开文件选择对话框
            string? vcfFilePath = null;
            using (var ofd = new System.Windows.Forms.OpenFileDialog())
            {
                ofd.Title = "选择要恢复的通讯录文件";
                ofd.Filter = "VCF 文件 (*.vcf)|*.vcf|所有文件 (*.*)|*.*";
                ofd.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads";

                if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    vcfFilePath = ofd.FileName;
                }
                else
                {
                    if (lblStatus != null) lblStatus.Text = "已取消恢复";
                    return;
                }
            }

            if (string.IsNullOrEmpty(vcfFilePath) || !File.Exists(vcfFilePath))
            {
                if (lblStatus != null) lblStatus.Text = "文件不存在";
                return;
            }

            if (btnRestoreContacts != null) btnRestoreContacts.IsEnabled = false;
            if (progressBar != null)
            {
                progressBar.Visibility = Visibility.Visible;
                progressBar.IsIndeterminate = true;
            }

            try
            {
                if (lblStatus != null) lblStatus.Text = "正在上传通讯录文件到手机...";

                // 上传VCF文件到手机
                var uploadResult = await Task.Run(() => UploadVcfToPhone(vcfFilePath));
                if (!uploadResult)
                {
                    if (lblStatus != null) lblStatus.Text = "上传文件失败";
                    WpfMessageBox.Show("无法将通讯录文件上传到手机", "错误",
                        WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                    return;
                }

                if (lblStatus != null) lblStatus.Text = $"通讯录文件已成功上传到手机 /sdcard/ 目录";
            }
            catch (Exception ex)
            {
                if (lblStatus != null) lblStatus.Text = $"恢复失败：{ex.Message}";
                WpfMessageBox.Show($"恢复通讯录时出错：{ex.Message}", "错误",
                    WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
            }
            finally
            {
                if (progressBar != null)
                {
                    progressBar.IsIndeterminate = false;
                    progressBar.Visibility = Visibility.Collapsed;
                }
                if (btnRestoreContacts != null) btnRestoreContacts.IsEnabled = true;
            }
        }

        // 上传VCF文件到手机
        private bool UploadVcfToPhone(string localVcfPath)
        {
            try
            {
                string adbPath = GetPreferredAdbPath();
                var fileName = Path.GetFileName(localVcfPath);
                var phoneDestPath = $"/sdcard/{fileName}";

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = $"push \"{localVcfPath}\" \"{phoneDestPath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                // 检查是否上传成功
                if (process.ExitCode == 0 && !error.Contains("error") && !error.Contains("failed"))
                {
                    return true;
                }

                System.Diagnostics.Debug.WriteLine($"上传失败：{error}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"上传VCF失败：{ex.Message}");
                return false;
            }
        }

        // 显示相册网格
        private void DisplayPhotoGallery()
        {
            var photoGallery = this.FindName("photoGallery") as WpfWrapPanel;
            var btnToggleSelectAll = this.FindName("btnToggleSelectAll") as WpfButton;
            var lblStatus = this.FindName("lblStatus") as WpfTextBlock;
            
            if (photoGallery == null) return;
            
            photoGallery.Children.Clear();

            foreach (var fileName in _phonePhotoFiles)
            {
                if (!_imageCache.ContainsKey(fileName)) continue;

                var border = new WpfBorder
                {
                    Width = 175,
                    Height = 175,
                    Margin = new Thickness(1),
                    BorderBrush = System.Windows.Media.Brushes.Gray,
                    BorderThickness = new Thickness(2),
                    Background = System.Windows.Media.Brushes.White,
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                var grid = new WpfGrid();

                var img = new WpfImage
                {
                    Source = _imageCache[fileName],
                    Stretch = Stretch.UniformToFill,
                    Width = 171,
                    Height = 171
                };

                var checkMark = new WpfTextBlock
                {
                    Text = "✓",
                    FontSize = 40,
                    Foreground = System.Windows.Media.Brushes.White,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 0, 120, 215)),
                    Width = 60,
                    Height = 60,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    Margin = new Thickness(0, 5, 5, 0),
                    Visibility = Visibility.Collapsed,
                    Tag = "checkmark"
                };

                var label = new WpfTextBlock
                {
                    Text = fileName.Length > 20 ? fileName.Substring(0, 17) + "..." : fileName,
                    FontSize = 10,
                    Foreground = System.Windows.Media.Brushes.White,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(150, 0, 0, 0)),
                    Padding = new Thickness(5, 2, 5, 2),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    TextAlignment = TextAlignment.Center
                };

                grid.Children.Add(img);
                grid.Children.Add(checkMark);
                grid.Children.Add(label);
                border.Child = grid;

                border.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.ClickCount == 1)
                    {
                        ToggleImageSelection(fileName, border);
                        e.Handled = true;
                    }
                };

                photoGallery.Children.Add(border);
            }

            if (btnToggleSelectAll != null) btnToggleSelectAll.IsEnabled = true;
            if (lblStatus != null) lblStatus.Text = $"相册加载完成，共{_phonePhotoFiles.Count}张图片。单击选中";
        }

        #region 视频功能
        // 加载手机视频列表
        private async Task LoadPhoneVideoListAsync()
        {
            var lblStatus = this.FindName("lblStatus") as WpfTextBlock;
            var btnLoadImages = this.FindName("btnLoadImages") as WpfButton;
            
            if (lblStatus != null) lblStatus.Text = "正在检测ADB设备...";

            var deviceId = await Task.Run(() => GetConnectedDeviceId());
            if (string.IsNullOrEmpty(deviceId))
            {
                if (lblStatus != null) lblStatus.Text = "错误：未检测到安卓设备！";
                WpfMessageBox.Show("请确认：\n1. 手机已开启USB调试\n2. 已授权电脑访问\n3. ADB能正常识别设备",
                    "设备检测失败", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                return;
            }

            if (lblStatus != null) lblStatus.Text = "正在扫描视频文件...";
            _phoneVideoFiles = await Task.Run(() => GetPhoneVideoFiles());

            if (_phoneVideoFiles.Count == 0)
            {
                if (lblStatus != null) lblStatus.Text = "未找到视频文件";
                WpfMessageBox.Show("未在以下路径找到视频文件：\n" +
                    "• /sdcard/DCIM/Camera/\n" +
                    "• /sdcard/Movies/\n" +
                    "• /sdcard/Videos/\n" +
                    "• /sdcard/Download/",
                    "提示", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
                return;
            }

            if (lblStatus != null) lblStatus.Text = $"检测到 {_phoneVideoFiles.Count} 个视频文件，点击「加载视频」按钮开始加载";
            if (btnLoadImages != null) btnLoadImages.IsEnabled = true;
        }

        // 获取手机视频文件列表
        private List<string> GetPhoneVideoFiles()
        {
            var allVideos = new List<string>();
            string adbPath = GetPreferredAdbPath();

            foreach (var path in PhoneVideoPaths)
            {
                try
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = adbPath,
                            Arguments = $"shell ls {path}",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode != 0 || output.Contains("No such file")) continue;

                    var lines = output.Trim().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var fileName = line.Trim();
                        if (!string.IsNullOrEmpty(fileName) &&
                            (fileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                             fileName.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ||
                             fileName.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ||
                             fileName.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                             fileName.EndsWith(".3gp", StringComparison.OrdinalIgnoreCase) ||
                             fileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)))
                        {
                            allVideos.Add($"{path}{fileName}");
                        }
                    }
                }
                catch { }
            }

            return allVideos;
        }

        // 显示视频网格
        private void DisplayVideoGallery()
        {
            var photoGallery = this.FindName("photoGallery") as WpfWrapPanel;
            var btnToggleSelectAll = this.FindName("btnToggleSelectAll") as WpfButton;
            var lblStatus = this.FindName("lblStatus") as WpfTextBlock;
            
            if (photoGallery == null) return;
            
            photoGallery.Children.Clear();

            foreach (var videoPath in _phoneVideoFiles)
            {
                var fileName = Path.GetFileName(videoPath);

                var border = new WpfBorder
                {
                    Width = 175,
                    Height = 175,
                    Margin = new Thickness(1),
                    BorderBrush = System.Windows.Media.Brushes.Gray,
                    BorderThickness = new Thickness(2),
                    Background = System.Windows.Media.Brushes.White,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = videoPath
                };

                var grid = new WpfGrid();

                var videoIcon = new WpfTextBlock
                {
                    Text = "🎬",
                    FontSize = 60,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var checkMark = new WpfTextBlock
                {
                    Text = "✓",
                    FontSize = 40,
                    Foreground = System.Windows.Media.Brushes.White,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 0, 120, 215)),
                    Width = 60,
                    Height = 60,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    Margin = new Thickness(0, 5, 5, 0),
                    Visibility = Visibility.Collapsed,
                    Tag = "checkmark"
                };

                var label = new WpfTextBlock
                {
                    Text = fileName.Length > 20 ? fileName.Substring(0, 17) + "..." : fileName,
                    FontSize = 10,
                    Foreground = System.Windows.Media.Brushes.White,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(150, 0, 0, 0)),
                    Padding = new Thickness(5, 2, 5, 2),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    TextAlignment = TextAlignment.Center
                };

                grid.Children.Add(videoIcon);
                grid.Children.Add(checkMark);
                grid.Children.Add(label);
                border.Child = grid;

                border.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.ClickCount == 1)
                    {
                        ToggleVideoSelection(videoPath, border);
                        e.Handled = true;
                    }
                };

                photoGallery.Children.Add(border);
            }

            if (btnToggleSelectAll != null) btnToggleSelectAll.IsEnabled = true;
            if (lblStatus != null) lblStatus.Text = $"找到 {_phoneVideoFiles.Count} 个视频文件。单击选中，点击备份按钮保存";
        }

        // 切换视频选中状态
        private void ToggleVideoSelection(string videoPath, WpfBorder border)
        {
            var btnBackup = this.FindName("btnBackup") as WpfButton;
            var lblStatus = this.FindName("lblStatus") as WpfTextBlock;

            var grid = border.Child as WpfGrid;
            if (grid == null) return;

            var checkMark = grid.Children.OfType<WpfTextBlock>().FirstOrDefault(t => t.Tag?.ToString() == "checkmark");
            if (checkMark == null) return;

            if (_selectedImages.Contains(videoPath))
            {
                _selectedImages.Remove(videoPath);
                checkMark.Visibility = Visibility.Collapsed;
                border.BorderBrush = System.Windows.Media.Brushes.Gray;
            }
            else
            {
                _selectedImages.Add(videoPath);
                checkMark.Visibility = Visibility.Visible;
                border.BorderBrush = System.Windows.Media.Brushes.DodgerBlue;
            }

            if (btnBackup != null) btnBackup.IsEnabled = _selectedImages.Count > 0;
            if (lblStatus != null) lblStatus.Text = $"已选中 {_selectedImages.Count} 个视频";
            UpdateToggleSelectAllButton();
        }
        #endregion

        #region 通讯录功能
        // 联系人类
        private class Contact
        {
            public string Name { get; set; } = "";
            public string Phone { get; set; } = "";
        }

        private List<Contact> _contacts = new List<Contact>();

        // 导出通讯录
        private async Task ExportContactsAsync()
        {
            var lblStatus = this.FindName("lblStatus") as WpfTextBlock;
            var btnLoadImages = this.FindName("btnLoadImages") as WpfButton;
            var progressBar = this.FindName("progressBar") as WpfProgressBar;

            if (lblStatus != null) lblStatus.Text = "正在检测ADB设备...";
            if (btnLoadImages != null) btnLoadImages.IsEnabled = false;

            var deviceId = await Task.Run(() => GetConnectedDeviceId());
            if (string.IsNullOrEmpty(deviceId))
            {
                if (lblStatus != null) lblStatus.Text = "错误：未检测到安卓设备！";
                WpfMessageBox.Show("请确认：\n1. 手机已开启USB调试\n2. 已授权电脑访问\n3. ADB能正常识别设备",
                    "设备检测失败", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                if (btnLoadImages != null) btnLoadImages.IsEnabled = true;
                return;
            }

            string? savePath = null;
            using (var sfd = new System.Windows.Forms.SaveFileDialog())
            {
                sfd.Title = "选择通讯录保存位置";
                sfd.Filter = "VCF 文件 (*.vcf)|*.vcf";
                sfd.FileName = "contacts.vcf";
                sfd.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads";

                if (sfd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    savePath = sfd.FileName;
                }
                else
                {
                    if (lblStatus != null) lblStatus.Text = "已取消导出";
                    if (btnLoadImages != null) btnLoadImages.IsEnabled = true;
                    return;
                }
            }

            if (progressBar != null)
            {
                progressBar.Visibility = Visibility.Visible;
                progressBar.IsIndeterminate = true;
            }

            try
            {
                if (lblStatus != null) lblStatus.Text = "正在从手机读取联系人数据...";

                var generateResult = await Task.Run(() => GenerateVcfOnPhone());
                if (!generateResult)
                {
                    if (lblStatus != null) lblStatus.Text = "生成联系人文件失败";
                    WpfMessageBox.Show("无法在手机上生成联系人文件，请检查手机权限", "错误",
                        WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                    return;
                }

                if (lblStatus != null) lblStatus.Text = "正在下载联系人文件...";

                var pullResult = await Task.Run(() => PullVcfFromPhone(savePath));
                if (!pullResult)
                {
                    if (lblStatus != null) lblStatus.Text = "下载联系人文件失败";
                    WpfMessageBox.Show("无法从手机下载联系人文件", "错误",
                        WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
                    return;
                }

                if (lblStatus != null) lblStatus.Text = "正在解析联系人数据...";
                _contacts = await Task.Run(() => ParseVcfFile(savePath));

                await Task.Run(() => DeleteVcfOnPhone());

                DisplayContacts();

                if (lblStatus != null) lblStatus.Text = $"通讯录导出成功！共 {_contacts.Count} 个联系人，已保存到：{savePath}";
            }
            catch (Exception ex)
            {
                if (lblStatus != null) lblStatus.Text = $"导出失败：{ex.Message}";
                WpfMessageBox.Show($"导出通讯录时出错：{ex.Message}", "错误",
                    WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
            }
            finally
            {
                if (progressBar != null)
                {
                    progressBar.IsIndeterminate = false;
                    progressBar.Visibility = Visibility.Collapsed;
                }
                if (btnLoadImages != null) btnLoadImages.IsEnabled = true;
            }
        }

        // 在手机上生成 VCF 文件
        private bool GenerateVcfOnPhone()
        {
            try
            {
                string adbPath = GetPreferredAdbPath();
                // 先删除旧文件
                var deleteProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = "shell rm -f /sdcard/Download/contacts.vcf",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                deleteProcess.Start();
                deleteProcess.WaitForExit();

                // 使用更简单的方法：直接查询联系人数据并在本地处理
                // 先获取联系人数据
                var queryProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = "shell content query --uri content://com.android.contacts/data/phones --projection display_name:data1",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8
                    }
                };
                queryProcess.Start();
                var queryOutput = queryProcess.StandardOutput.ReadToEnd();
                queryProcess.WaitForExit();

                if (string.IsNullOrEmpty(queryOutput))
                {
                    return false;
                }

                // 解析查询结果并生成VCF内容
                var vcfContent = new System.Text.StringBuilder();
                var lines = queryOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var line in lines)
                {
                    if (line.Contains("display_name=") && line.Contains("data1="))
                    {
                        // 提取姓名
                        var nameStart = line.IndexOf("display_name=") + 13;
                        var nameEnd = line.IndexOf(",", nameStart);
                        if (nameEnd == -1) nameEnd = line.Length;
                        var name = line.Substring(nameStart, nameEnd - nameStart).Trim();

                        // 提取电话
                        var phoneStart = line.IndexOf("data1=") + 6;
                        var phoneEnd = line.IndexOf(",", phoneStart);
                        if (phoneEnd == -1) phoneEnd = line.Length;
                        var phone = line.Substring(phoneStart, phoneEnd - phoneStart).Trim();

                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(phone))
                        {
                            vcfContent.AppendLine("BEGIN:VCARD");
                            vcfContent.AppendLine("VERSION:3.0");
                            vcfContent.AppendLine($"FN:{name}");
                            vcfContent.AppendLine($"TEL:{phone}");
                            vcfContent.AppendLine("END:VCARD");
                        }
                    }
                }

                // 将VCF内容写入手机（使用UTF-8编码避免中文乱码）
                var tempFile = Path.GetTempFileName();
                File.WriteAllText(tempFile, vcfContent.ToString(), System.Text.Encoding.UTF8);

                var pushProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = $"push \"{tempFile}\" /sdcard/Download/contacts.vcf",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                pushProcess.Start();
                pushProcess.WaitForExit();

                // 删除临时文件
                try { File.Delete(tempFile); } catch { }

                // 检查文件是否生成
                var checkProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = "shell ls -l /sdcard/Download/contacts.vcf",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                checkProcess.Start();
                var output = checkProcess.StandardOutput.ReadToEnd();
                checkProcess.WaitForExit();

                return output.Contains("contacts.vcf") && !output.Contains("No such file");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"生成VCF失败：{ex.Message}");
                return false;
            }
        }

        // 从手机拉取 VCF 文件
        private bool PullVcfFromPhone(string localPath)
        {
            try
            {
                string adbPath = GetPreferredAdbPath();
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = $"pull /sdcard/Download/contacts.vcf \"{localPath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit(10000);

                return File.Exists(localPath);
            }
            catch
            {
                return false;
            }
        }

        // 删除手机上的临时 VCF 文件
        private void DeleteVcfOnPhone()
        {
            try
            {
                string adbPath = GetPreferredAdbPath();
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = adbPath,
                        Arguments = "shell rm /sdcard/Download/contacts.vcf",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit();
            }
            catch { }
        }

        // 解析 VCF 文件
        private List<Contact> ParseVcfFile(string vcfPath)
        {
            var contacts = new List<Contact>();

            try
            {
                var lines = File.ReadAllLines(vcfPath);
                Contact? currentContact = null;

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();

                    if (trimmedLine == "BEGIN:VCARD")
                    {
                        currentContact = new Contact();
                    }
                    else if (trimmedLine.StartsWith("FN:"))
                    {
                        if (currentContact != null)
                        {
                            currentContact.Name = trimmedLine.Substring(3).Trim();
                        }
                    }
                    else if (trimmedLine.StartsWith("TEL:"))
                    {
                        if (currentContact != null)
                        {
                            currentContact.Phone = trimmedLine.Substring(4).Trim();
                        }
                    }
                    else if (trimmedLine == "END:VCARD")
                    {
                        if (currentContact != null && !string.IsNullOrEmpty(currentContact.Name) && !string.IsNullOrEmpty(currentContact.Phone))
                        {
                            contacts.Add(currentContact);
                        }
                        currentContact = null;
                    }
                }
            }
            catch { }

            return contacts;
        }

        // 显示联系人列表
        private void DisplayContacts()
        {
            var photoGallery = this.FindName("photoGallery") as WpfWrapPanel;
            if (photoGallery == null) return;

            photoGallery.Children.Clear();

            if (_contacts.Count == 0)
            {
                var emptyText = new WpfTextBlock
                {
                    Text = "未找到联系人",
                    FontSize = 16,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                photoGallery.Children.Add(emptyText);
                return;
            }

            foreach (var contact in _contacts)
            {
                var border = new WpfBorder
                {
                    Width = 350,
                    Height = 80,
                    Margin = new Thickness(5),
                    BorderBrush = System.Windows.Media.Brushes.LightGray,
                    BorderThickness = new Thickness(1),
                    Background = System.Windows.Media.Brushes.White,
                    Padding = new Thickness(15, 10, 15, 10)
                };

                var grid = new WpfGrid();
                grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var nameText = new WpfTextBlock
                {
                    Text = contact.Name,
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                System.Windows.Controls.Grid.SetRow(nameText, 0);

                var phoneText = new WpfTextBlock
                {
                    Text = contact.Phone,
                    FontSize = 14,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    VerticalAlignment = VerticalAlignment.Center
                };
                System.Windows.Controls.Grid.SetRow(phoneText, 1);

                grid.Children.Add(nameText);
                grid.Children.Add(phoneText);
                border.Child = grid;

                photoGallery.Children.Add(border);
            }
        }
        #endregion
        #endregion
    }
}
