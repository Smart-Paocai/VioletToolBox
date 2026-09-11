using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPanel = System.Windows.Controls.Panel;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfRadioButton = System.Windows.Controls.RadioButton;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace WpfApp1
{
    public partial class MainWindow
    {
        private const string EdlFeedbackFileMarker = "=== VioletTool EDL BUG Feedback ===";
        private const string EdlFeedbackUiLogMarker = "=== EDL 界面日志 ===";
        private const string EdlFeedbackNativeLogMarker = "=== EDL 底层通信日志 ===";

        private void EdlBugFeedbackButton_Click(object sender, RoutedEventArgs e)
        {
            ShowEdlBugFeedbackDialog();
        }

        private void ShowEdlBugFeedbackDialog()
        {
            var dialog = new Window
            {
                Title = "BUG反馈",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Width = 520,
                Height = 680,
                Background = WpfBrushes.White
            };

            var root = new Grid
            {
                Margin = new Thickness(22)
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel();
            header.Children.Add(new TextBlock
            {
                Text = "提交 EDL BUG 工单",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(45, 45, 45))
            });
            header.Children.Add(new TextBlock
            {
                Text = "提交时会自动附带当前界面日志和底层通信日志。",
                Margin = new Thickness(0, 5, 0, 0),
                FontSize = 12,
                Foreground = WpfBrushes.DimGray
            });
            root.Children.Add(header);

            var form = new Grid();
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(form, 2);
            root.Children.Add(form);

            var modelTextBox = CreateEdlFeedbackTextBox();
            var processorTextBox = CreateEdlFeedbackTextBox();
            var systemVersionTextBox = CreateEdlFeedbackTextBox();
            var feedbackTextBox = CreateEdlFeedbackTextBox();
            feedbackTextBox.AcceptsReturn = true;
            feedbackTextBox.TextWrapping = TextWrapping.Wrap;
            feedbackTextBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            feedbackTextBox.VerticalContentAlignment = VerticalAlignment.Top;
            feedbackTextBox.MinHeight = 150;
            feedbackTextBox.MaxLength = 12000;

            AddEdlFeedbackField(form, 0, "机型", modelTextBox, true);
            AddEdlFeedbackField(form, 2, "处理器", processorTextBox, true);
            AddEdlFeedbackField(form, 4, "系统版本", systemVersionTextBox, false);
            
            var testingLabel = new TextBlock
            {
                Text = "参与测试",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                FontSize = 13,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(55, 55, 55))
            };
            Grid.SetRow(testingLabel, 6);
            Grid.SetColumn(testingLabel, 0);
            form.Children.Add(testingLabel);

            var testingOptions = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(testingOptions, 6);
            Grid.SetColumn(testingOptions, 1);
            form.Children.Add(testingOptions);

            var willingToTestRadioButton = new WpfRadioButton
            {
                Content = "愿意测试",
                GroupName = "EdlFeedbackTesting",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 24, 0)
            };
            var unwillingToTestRadioButton = new WpfRadioButton
            {
                Content = "我不愿意测试",
                GroupName = "EdlFeedbackTesting",
                IsChecked = true,
                VerticalAlignment = VerticalAlignment.Center
            };
            testingOptions.Children.Add(willingToTestRadioButton);
            testingOptions.Children.Add(unwillingToTestRadioButton);

            var contactBorder = new Border
            {
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 0, 0, 0),
                Padding = new Thickness(10, 8, 10, 8),
                Background = new SolidColorBrush(WpfColor.FromRgb(249, 243, 253)),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(222, 190, 239)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = new TextBlock
                {
                    Text = "请加作者QQ：1227363342",
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(128, 0, 128))
                }
            };
            Grid.SetRow(contactBorder, 8);
            Grid.SetColumn(contactBorder, 1);
            form.Children.Add(contactBorder);

            willingToTestRadioButton.Checked += (_, _) =>
                contactBorder.Visibility = Visibility.Visible;
            unwillingToTestRadioButton.Checked += (_, _) =>
                contactBorder.Visibility = Visibility.Collapsed;

            AddEdlFeedbackField(form, 10, "反馈内容", feedbackTextBox, true);
            Grid.SetRowSpan(feedbackTextBox, 2);

            var statusText = new TextBlock
            {
                Text = "请尽量描述复现步骤、预期结果和实际结果。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = WpfBrushes.DimGray,
                FontSize = 12
            };
            Grid.SetRow(statusText, 4);
            root.Children.Add(statusText);

            var progress = new WpfProgressBar
            {
                Height = 3,
                IsIndeterminate = true,
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(progress, 5);
            root.Children.Add(progress);

            var buttons = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = WpfHorizontalAlignment.Right
            };
            Grid.SetRow(buttons, 6);
            root.Children.Add(buttons);

            var cancelButton = new WpfButton
            {
                Content = "取消",
                Width = 86,
                Height = 34,
                IsCancel = true,
                Margin = new Thickness(0, 0, 10, 0)
            };
            var submitButton = new WpfButton
            {
                Content = "提交工单",
                Width = 104,
                Height = 34,
                Background = new SolidColorBrush(WpfColor.FromRgb(180, 112, 221)),
                Foreground = WpfBrushes.White,
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(180, 112, 221))
            };
            buttons.Children.Add(cancelButton);
            buttons.Children.Add(submitButton);

            bool isSubmitting = false;

            void SetSubmitting(bool value)
            {
                isSubmitting = value;
                modelTextBox.IsEnabled = !value;
                processorTextBox.IsEnabled = !value;
                systemVersionTextBox.IsEnabled = !value;
                feedbackTextBox.IsEnabled = !value;
                willingToTestRadioButton.IsEnabled = !value;
                unwillingToTestRadioButton.IsEnabled = !value;
                submitButton.IsEnabled = !value;
                cancelButton.IsEnabled = !value;
                progress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            }

            cancelButton.Click += (_, _) => dialog.Close();
            dialog.Closing += (_, args) =>
            {
                if (isSubmitting)
                    args.Cancel = true;
            };
            dialog.Loaded += (_, _) => modelTextBox.Focus();
            submitButton.Click += async (_, _) =>
            {
                string model = modelTextBox.Text.Trim();
                string processor = processorTextBox.Text.Trim();
                string systemVersion = systemVersionTextBox.Text.Trim();
                string feedback = feedbackTextBox.Text.Trim();
                bool willingToTest = willingToTestRadioButton.IsChecked == true;

                if (string.IsNullOrWhiteSpace(model) ||
                    string.IsNullOrWhiteSpace(processor) ||
                    string.IsNullOrWhiteSpace(feedback))
                {
                    statusText.Text = "请填写机型、处理器和反馈内容。";
                    statusText.Foreground = new SolidColorBrush(WpfColor.FromRgb(210, 40, 40));
                    return;
                }

                SetSubmitting(true);
                statusText.Text = "正在整理日志并提交工单...";
                statusText.Foreground = WpfBrushes.DimGray;
                LogEdlMessage("正在提交BUG反馈...", null);

                string? feedbackLogPath = null;
                try
                {
                    string uiLog = CaptureEdlUiLogText();
                    feedbackLogPath = await Task.Run(() =>
                        CreateEdlFeedbackLogSnapshot(
                            model,
                            processor,
                            systemVersion,
                            feedback,
                            willingToTest,
                            uiLog));
                    await UploadEdlFeedbackLogAsync(feedbackLogPath);

                    statusText.Text = "工单提交成功，日志已上传。";
                    statusText.Foreground = new SolidColorBrush(WpfColor.FromRgb(0, 128, 0));
                    submitButton.Content = "已提交";
                    cancelButton.Content = "关闭";
                    LogEdlMessage("BUG反馈提交成功", null);
                }
                catch (Exception ex)
                {
                    statusText.Text = "提交失败：" + ex.Message;
                    statusText.Foreground = new SolidColorBrush(WpfColor.FromRgb(210, 40, 40));
                    LogEdlMessage("BUG反馈提交失败: " + ex.Message, "error");
                }
                finally
                {
                    DeleteEdlFeedbackLogSnapshot(feedbackLogPath);
                    SetSubmitting(false);
                }
            };

            dialog.Content = root;
            dialog.ShowDialog();
        }

        private static WpfTextBox CreateEdlFeedbackTextBox()
        {
            return new WpfTextBox
            {
                Height = 34,
                Padding = new Thickness(9, 5, 9, 5),
                FontSize = 13,
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(218, 218, 218)),
                BorderThickness = new Thickness(1),
                VerticalContentAlignment = VerticalAlignment.Center,
                MaxLength = 160
            };
        }

        private static void AddEdlFeedbackField(
            Grid form,
            int row,
            string label,
            WpfTextBox textBox,
            bool required)
        {
            var labelBlock = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 8, 0),
                FontSize = 13,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(55, 55, 55))
            };
            labelBlock.Inlines.Add(new Run(label));
            if (required)
            {
                labelBlock.Inlines.Add(new Run(" *")
                {
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(210, 40, 40))
                });
            }
            else
            {
                labelBlock.Inlines.Add(new Run("（选填）")
                {
                    FontSize = 11,
                    Foreground = WpfBrushes.Gray
                });
            }

            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);
            Grid.SetRow(textBox, row);
            Grid.SetColumn(textBox, 1);
            form.Children.Add(labelBlock);
            form.Children.Add(textBox);
        }

        private string CaptureEdlUiLogText()
        {
            if (EdlLogTextBox?.Document == null)
                return string.Empty;

            var lines = new List<string>();
            foreach (Block block in EdlLogTextBox.Document.Blocks)
                AppendEdlFlowBlockText(block, lines);
            return string.Join(Environment.NewLine, lines).Trim();
        }

        private static void AppendEdlFlowBlockText(Block block, ICollection<string> lines)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    AddEdlFeedbackLine(
                        lines,
                        new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text);
                    break;
                case BlockUIContainer container when container.Child != null:
                    AppendEdlUiElementText(container.Child, lines);
                    break;
                case Section section:
                    foreach (Block child in section.Blocks)
                        AppendEdlFlowBlockText(child, lines);
                    break;
                default:
                    AddEdlFeedbackLine(
                        lines,
                        new TextRange(block.ContentStart, block.ContentEnd).Text);
                    break;
            }
        }

        private static void AppendEdlUiElementText(UIElement element, ICollection<string> lines)
        {
            switch (element)
            {
                case TextBlock textBlock:
                    AddEdlFeedbackLine(lines, textBlock.Text);
                    return;
                case WpfTextBox textBox:
                    AddEdlFeedbackLine(lines, textBox.Text);
                    return;
                case Border border when border.Child != null:
                    AppendEdlUiElementText(border.Child, lines);
                    return;
                case WpfPanel panel:
                    foreach (UIElement child in panel.Children)
                        AppendEdlUiElementText(child, lines);
                    return;
                case ContentControl contentControl when contentControl.Content is UIElement child:
                    AppendEdlUiElementText(child, lines);
                    return;
                case ContentControl contentControl when contentControl.Content != null:
                    AddEdlFeedbackLine(lines, contentControl.Content.ToString());
                    return;
            }
        }

        private static void AddEdlFeedbackLine(ICollection<string> lines, string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
            foreach (string line in normalized.Split('\n'))
            {
                string trimmed = line.TrimEnd();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    lines.Add(trimmed);
            }
        }

        private string CreateEdlFeedbackLogSnapshot(
            string model,
            string processor,
            string systemVersion,
            string feedback,
            bool willingToTest,
            string uiLog)
        {
            string? sourcePath;
            lock (_edlNativeLogLock)
            {
                sourcePath = _edlNativeLogPath;
                if (string.IsNullOrWhiteSpace(sourcePath))
                    throw new InvalidOperationException("当前会话日志尚未初始化");
            }

            string nativeLog = File.Exists(sourcePath)
                ? ReadEdlFeedbackSourceLog(sourcePath)
                : string.Empty;

            int nativeMarkerIndex = nativeLog.IndexOf(
                EdlFeedbackNativeLogMarker,
                StringComparison.Ordinal);
            if (nativeLog.StartsWith(EdlFeedbackFileMarker, StringComparison.Ordinal) &&
                nativeMarkerIndex >= 0)
            {
                nativeLog = nativeLog[
                    (nativeMarkerIndex + EdlFeedbackNativeLogMarker.Length)..]
                    .TrimStart('\r', '\n');
            }

            var result = new StringBuilder();
            result.AppendLine(EdlFeedbackFileMarker);
            result.AppendLine("提交时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            result.AppendLine("机型: " + model);
            result.AppendLine("处理器: " + processor);
            result.AppendLine("系统版本: " +
                (string.IsNullOrWhiteSpace(systemVersion) ? "未填写" : systemVersion));
            result.AppendLine("是否愿意参与测试: " +
                (willingToTest ? "愿意测试" : "我不愿意测试"));
            if (willingToTest)
                result.AppendLine("测试联系方式: 请加作者QQ：1227363342");
            result.AppendLine();
            result.AppendLine("反馈内容:");
            result.AppendLine(feedback);
            result.AppendLine();
            result.AppendLine(EdlFeedbackUiLogMarker);
            result.AppendLine(string.IsNullOrWhiteSpace(uiLog) ? "无界面日志" : uiLog);
            result.AppendLine();
            result.AppendLine(EdlFeedbackNativeLogMarker);
            result.Append(string.IsNullOrWhiteSpace(nativeLog) ? "无底层通信日志" : nativeLog.TrimEnd());
            result.AppendLine();

            string finalText = result.ToString();
            int byteCount = Encoding.UTF8.GetByteCount(finalText);
            if (byteCount > EdlBugFeedbackMaxBytes)
                throw new InvalidOperationException(
                    $"工单日志超过 {EdlBugFeedbackMaxBytes / 1024 / 1024} MB，无法上传");

            string snapshotDirectory = Path.Combine(
                Path.GetTempPath(),
                "VioletTool",
                "edl-feedback",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(snapshotDirectory);
            string snapshotPath = Path.Combine(snapshotDirectory, Path.GetFileName(sourcePath));
            File.WriteAllText(snapshotPath, finalText, new UTF8Encoding(false));
            return snapshotPath;
        }

        private static string ReadEdlFeedbackSourceLog(string path)
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.SequentialScan);
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 64 * 1024,
                leaveOpen: false);
            return reader.ReadToEnd();
        }

        private static void DeleteEdlFeedbackLogSnapshot(string? snapshotPath)
        {
            if (string.IsNullOrWhiteSpace(snapshotPath))
                return;

            try
            {
                if (File.Exists(snapshotPath))
                    File.Delete(snapshotPath);

                string? snapshotDirectory = Path.GetDirectoryName(snapshotPath);
                if (!string.IsNullOrWhiteSpace(snapshotDirectory) && Directory.Exists(snapshotDirectory))
                    Directory.Delete(snapshotDirectory, false);
            }
            catch
            {
                // A failed cleanup must not turn a successful feedback upload into an error.
            }
        }

        private static async Task UploadEdlFeedbackLogAsync(string logPath)
        {
            string fileName = Path.GetFileName(logPath);
            if (!System.Text.RegularExpressions.Regex.IsMatch(
                    fileName,
                    @"^VioletToolEdlLog\d{8}_\d{6}\.txt$",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            {
                throw new InvalidOperationException("日志文件名格式不正确");
            }

            byte[] logBytes = await File.ReadAllBytesAsync(logPath).ConfigureAwait(false);
            if (logBytes.Length == 0 || logBytes.Length > EdlBugFeedbackMaxBytes)
                throw new InvalidOperationException("日志文件为空或超过上传限制");

            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(90)
            };
            string[] uploadUrls =
            {
                EdlBugFeedbackUploadUrl,
                EdlBugFeedbackFallbackUploadUrl
            };
            HttpResponseMessage? response = null;
            string responseText = string.Empty;
            try
            {
                foreach (string candidate in uploadUrls)
                {
                    response?.Dispose();
                    using var content = new ByteArrayContent(logBytes);
                    content.Headers.ContentType = new MediaTypeHeaderValue("text/plain")
                    {
                        CharSet = "utf-8"
                    };
                    content.Headers.Add("X-Violet-Log-Name", fileName);

                    string uploadUrl = ApplyViolettoolSignIfNeeded(candidate);
                    response = await client
                        .PostAsync(uploadUrl, content)
                        .ConfigureAwait(false);
                    responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (response.IsSuccessStatusCode ||
                        response.StatusCode != System.Net.HttpStatusCode.NotFound)
                    {
                        break;
                    }
                }

                if (response == null)
                    throw new InvalidOperationException("未能连接服务器上传接口");
                if (!response.IsSuccessStatusCode)
                {
                    string message = TryReadEdlFeedbackServerMessage(responseText);
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        message = "服务器上传接口尚未部署";
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(message)
                            ? $"服务器返回 {(int)response.StatusCode}"
                            : message);
                }

                if (!string.IsNullOrWhiteSpace(responseText))
                {
                    try
                    {
                        using JsonDocument json = JsonDocument.Parse(responseText);
                        if (json.RootElement.TryGetProperty("ok", out JsonElement ok) &&
                            ok.ValueKind == JsonValueKind.False)
                        {
                            throw new InvalidOperationException(
                                TryReadEdlFeedbackServerMessage(responseText) ?? "服务器拒绝了工单");
                        }
                    }
                    catch (JsonException)
                    {
                        throw new InvalidOperationException("服务器返回了无法识别的结果");
                    }
                }
            }
            finally
            {
                response?.Dispose();
            }
        }

        private static string? TryReadEdlFeedbackServerMessage(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
                return null;

            try
            {
                using JsonDocument json = JsonDocument.Parse(responseText);
                if (json.RootElement.TryGetProperty("message", out JsonElement message) &&
                    message.ValueKind == JsonValueKind.String)
                {
                    return message.GetString();
                }
            }
            catch (JsonException)
            {
            }

            return null;
        }
    }
}
