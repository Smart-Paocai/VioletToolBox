using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WpfCursors = System.Windows.Input.Cursors;
using WpfSize = System.Windows.Size;

namespace WpfApp1
{
    public partial class MainWindow
    {
        // 将 notice.json 上传到 violettool.top 的网站根目录即可更新公告。
        private const string BroadcastNoticeFeedUrl = "https://violettool.top/notice.json";
        private static readonly HttpClient BroadcastNoticeHttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        private readonly List<BroadcastNoticeItem> _broadcastNotices = new();
        private DispatcherTimer? _broadcastNoticeRotationTimer;
        private int _activeBroadcastNoticeIndex;
        private BroadcastNoticeItem? _activeBroadcastNotice;

        private sealed class BroadcastNoticeItem
        {
            public BroadcastNoticeItem(string text, string? url)
            {
                Text = text;
                Url = url;
            }

            public string Text { get; }
            public string? Url { get; }
        }

        private async Task InitializeBroadcastNoticesAsync()
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, BroadcastNoticeFeedUrl);
                request.Headers.Add("Cache-Control", "no-cache");

                using HttpResponseMessage response = await BroadcastNoticeHttpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    return;
                }

                string json = await response.Content.ReadAsStringAsync();
                List<BroadcastNoticeItem> notices = ParseBroadcastNotices(json);
                if (notices.Count == 0)
                {
                    return;
                }

                _broadcastNotices.Clear();
                _broadcastNotices.AddRange(notices);
                _activeBroadcastNoticeIndex = 0;
                BroadcastNoticeHost.Visibility = Visibility.Visible;

                _broadcastNoticeRotationTimer ??= new DispatcherTimer();
                _broadcastNoticeRotationTimer.Tick -= BroadcastNoticeRotationTimer_Tick;
                _broadcastNoticeRotationTimer.Tick += BroadcastNoticeRotationTimer_Tick;

                ShowBroadcastNotice(_activeBroadcastNoticeIndex);
            }
            catch
            {
                // 公告加载失败不打断工具箱启动，也不占用底部栏空间。
            }
        }

        private static List<BroadcastNoticeItem> ParseBroadcastNotices(string json)
        {
            var notices = new List<BroadcastNoticeItem>();

            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                JsonElement items;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    items = root;
                }
                else if (root.ValueKind == JsonValueKind.Object &&
                         root.TryGetProperty("items", out JsonElement itemCollection) &&
                         itemCollection.ValueKind == JsonValueKind.Array)
                {
                    items = itemCollection;
                }
                else
                {
                    return notices;
                }

                foreach (JsonElement item in items.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object ||
                        !item.TryGetProperty("text", out JsonElement textElement) ||
                        textElement.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    string text = textElement.GetString()?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    string? url = null;
                    if (item.TryGetProperty("url", out JsonElement urlElement) &&
                        urlElement.ValueKind == JsonValueKind.String)
                    {
                        url = GetSafeBroadcastUrl(urlElement.GetString());
                    }

                    notices.Add(new BroadcastNoticeItem(text, url));
                }
            }
            catch (JsonException)
            {
                // 格式错误时保持公告区域隐藏。
            }

            return notices;
        }

        private static string? GetSafeBroadcastUrl(string? value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }

            return uri.AbsoluteUri;
        }

        private void ShowBroadcastNotice(int index)
        {
            if (_broadcastNotices.Count == 0)
            {
                return;
            }

            _activeBroadcastNoticeIndex = (index + _broadcastNotices.Count) % _broadcastNotices.Count;
            _activeBroadcastNotice = _broadcastNotices[_activeBroadcastNoticeIndex];
            string localizedNotice = LocalizeUiText(_activeBroadcastNotice.Text);
            SetLocalizedText(BroadcastNoticeTextBlock, _activeBroadcastNotice.Text);
            BroadcastNoticeHost.Cursor = _activeBroadcastNotice.Url == null ? WpfCursors.Arrow : WpfCursors.Hand;
            BroadcastNoticeHost.ToolTip = _activeBroadcastNotice.Url == null
                ? localizedNotice
                : $"{localizedNotice}\n{LocalizeUiText("点击打开链接")}";

            RestartBroadcastNoticeAnimation();
        }

        private void RestartBroadcastNoticeAnimation()
        {
            if (_activeBroadcastNotice == null)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_activeBroadcastNotice == null || BroadcastNoticeHost.ActualWidth <= 0)
                {
                    return;
                }

                var translateTransform = (TranslateTransform)BroadcastNoticeTextBlock.RenderTransform;
                translateTransform.BeginAnimation(TranslateTransform.XProperty, null);

                BroadcastNoticeTextBlock.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
                double textWidth = BroadcastNoticeTextBlock.DesiredSize.Width;
                double viewportWidth = Math.Max(0, BroadcastNoticeHost.ActualWidth -
                    BroadcastNoticeHost.Padding.Left - BroadcastNoticeHost.Padding.Right);
                double rightAlignedPosition = Math.Max(0, viewportWidth - textWidth);

                double displaySeconds = 6;
                if (textWidth > viewportWidth && viewportWidth > 0)
                {
                    displaySeconds = Math.Clamp((textWidth + viewportWidth) / 90d, 5d, 12d);
                    translateTransform.X = -textWidth;
                    var animation = new DoubleAnimation
                    {
                        From = -textWidth,
                        To = viewportWidth,
                        Duration = TimeSpan.FromSeconds(displaySeconds),
                        BeginTime = TimeSpan.Zero,
                        RepeatBehavior = _broadcastNotices.Count == 1
                            ? RepeatBehavior.Forever
                            : new RepeatBehavior(1),
                        FillBehavior = FillBehavior.HoldEnd
                    };
                    Timeline.SetDesiredFrameRate(animation, 60);
                    translateTransform.BeginAnimation(TranslateTransform.XProperty, animation);
                }
                else
                {
                    translateTransform.X = rightAlignedPosition;
                }

                if (_broadcastNoticeRotationTimer == null)
                {
                    return;
                }

                _broadcastNoticeRotationTimer.Stop();
                if (_broadcastNotices.Count > 1)
                {
                    _broadcastNoticeRotationTimer.Interval = TimeSpan.FromSeconds(displaySeconds + 0.15d);
                    _broadcastNoticeRotationTimer.Start();
                }
            }), DispatcherPriority.Render);
        }

        private void BroadcastNoticeRotationTimer_Tick(object? sender, EventArgs e)
        {
            ShowBroadcastNotice(_activeBroadcastNoticeIndex + 1);
        }

        private void BroadcastNoticeHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RestartBroadcastNoticeAnimation();
        }

        private void BroadcastNoticeHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_activeBroadcastNotice?.Url))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(_activeBroadcastNotice.Url)
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                // 浏览器无法启动时不影响主窗口操作。
            }
        }
    }
}
