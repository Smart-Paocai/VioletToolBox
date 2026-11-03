using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;

namespace WpfApp1;

// Supply only the UI fields used by the production translator. No device I/O is started.
public partial class MainWindow : Window
{
    private readonly TextBlock DeviceStatusText = new();
    private readonly TextBlock ConnectionTypeText = new();
    private readonly TextBlock UnlockStatusText = new();
    private readonly TextBlock ABPartitionText = new();
    private readonly TextBlock SelinuxStatusText = new();
    private readonly TextBlock BottomConnectionTypeText = new();
    private readonly TextBlock BroadcastNoticeTextBlock = new();
    private readonly TextBlock LanguageSwitchLabel = new();
    private readonly Button LanguageSwitchButton = new();
    private readonly Border BroadcastNoticeHost = new();
    private Notice? _activeBroadcastNotice = null;
    private sealed record Notice(string Text, string? Url);

    public static void RunChecks()
    {
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        Check(EnglishUiTexts.Keys.All(JapaneseUiTexts.ContainsKey), "A source key is missing in Japanese.");
        var unchangedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "紫色放大镜", "泡菜", "昔日", "皓皓的小月", "仁济", "王听", "月饼", "白纸", "波仔",
            "老板来十斤爱情回家喂猪", "林夕", "落魄鱼", "萌混果宫", "匿名网友子旋", "王小桃", "小洺", "云", "曜日"
        };
        var xaml = XDocument.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "MainWindow.xaml"));
        var captions = new HashSet<string> { "Text", "Content", "Header", "ToolTip", "Title" };
        var missing = xaml.Descendants().Attributes()
            .Where(a => captions.Contains(a.Name.LocalName) && !a.Value.StartsWith('{'))
            .Select(a => a.Value)
            .Where(v => v.Any(c => c >= '\u4e00' && c <= '\u9fff'))
            .Where(v => !JapaneseUiTexts.ContainsKey(v) && !unchangedNames.Contains(v)).Distinct().ToArray();
        Check(missing.Length == 0, "Untranslated XAML captions: " + string.Join(" | ", missing));
        Check(TryParseCultureName("ja-JP", out var japanese) && japanese == UiLanguage.Japanese, "Japanese preference failed.");
        Check(TryParseCultureName("zh-CN", out var chinese) && chinese == UiLanguage.SimplifiedChinese, "Chinese preference failed.");
        Check(!TryParseCultureName("unknown", out var fallback) && fallback == UiLanguage.SimplifiedChinese, "Invalid preference fallback failed.");
        Check(NextLanguage(UiLanguage.SimplifiedChinese) == UiLanguage.English &&
              NextLanguage(UiLanguage.English) == UiLanguage.Japanese &&
              NextLanguage(UiLanguage.Japanese) == UiLanguage.SimplifiedChinese, "Language cycle failed.");

        var window = new MainWindow();
        var panel = new StackPanel();
        var inherited = new TextBlock { Text = "设备信息" };
        var explicitFont = new TextBlock { Text = "设备信息", FontFamily = new FontFamily("Consolas") };
        var combo = new ComboBox();
        var commands = new[] { "联发科", "重启到系统", "重启到EDL", "重启到Recovery", "重启到FastbootD", "云解包方案-从云端提取线刷文件", "联想" }
            .Select(text => new ComboBoxItem { Content = text }).ToArray();
        foreach (var command in commands) combo.Items.Add(command);
        panel.Children.Add(inherited);
        panel.Children.Add(explicitFont);
        panel.Children.Add(combo);
        window.Content = panel;
        window.SetLocalizedText(window.DeviceStatusText, "已连接");
        for (int cycle = 0; cycle < 2; cycle++)
        {
            window.ApplyUiLanguage(UiLanguage.English, persist: false);
            window.ApplyUiLanguage(UiLanguage.Japanese, persist: false);
            Check(inherited.Text == "端末情報", "Japanese caption not displayed.");
            Check(window.DeviceStatusText.Text == "接続済み", "Dynamic status not translated.");
            Check(window.LocalizeUiText("检测失败: example") == "検出に失敗しました: example", "Error punctuation was lost.");
            Check(window.LanguageSwitchButton.ToolTip.ToString()!.StartsWith("現在の言語"), "Language tooltip not Japanese.");
            foreach (var command in commands)
            {
                string raw = GetRawLocalizedContent(command)!;
                Check(command.Content.ToString() == JapaneseUiTexts[raw], "ComboBox caption not translated.");
            }
            Check(GetRawLocalizedContent(commands[0]) == "联发科", "OCDT processor identifier changed.");
            Check(GetRawLocalizedContent(commands[^1]) == "联想", "ROM API brand identifier changed.");
            window.ApplyUiLanguage(UiLanguage.SimplifiedChinese, persist: false);
            Check(inherited.Text == "设备信息", "Chinese caption was not restored.");
            Check(inherited.ReadLocalValue(TextBlock.FontFamilyProperty) == DependencyProperty.UnsetValue, "Font inheritance was not restored.");
            Check(explicitFont.FontFamily.Source == "Consolas", "Explicit font was not restored.");
        }
        window.ApplyUiLanguage(UiLanguage.Japanese, persist: false);
        commands[0].Content = "new dynamic value";
        Check(GetRawLocalizedContent(commands[0]) == "new dynamic value", "Raw content helper returned a stale value.");
        window.Close();
        Console.WriteLine($"PASS: {JapaneseUiTexts.Count} Japanese entries; XAML coverage, language cycling, identifiers, preferences and fonts.");
    }
}

internal static class Program
{
    [STAThread]
    private static void Main() => MainWindow.RunChecks();
}
