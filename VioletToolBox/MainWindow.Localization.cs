using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using ComboBox = System.Windows.Controls.ComboBox;

namespace WpfApp1
{
    public partial class MainWindow
    {
        private enum UiLanguage
        {
            SimplifiedChinese,
            English
        }

        private static readonly IReadOnlyDictionary<string, string> EnglishUiTexts =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // Shell and navigation
                ["紫罗兰工具箱"] = "Violet Toolbox",
                ["主页"] = "Home",
                ["投屏"] = "Screen",
                ["刷写功能"] = "Flashing",
                ["基本刷入"] = "Basic Flash",
                ["可视刷写"] = "Visual Flash",
                ["欧加线刷"] = "OPlus Flash",
                ["EDL刷写"] = "EDL Flash",
                ["降级助手"] = "Downgrade",
                ["实用功能"] = "Tools",
                ["模块专区"] = "Modules",
                ["断点续传"] = "Downloader",
                ["文件传输"] = "Files",
                ["脱机修补"] = "Offline Patch",
                ["应用管理"] = "Apps",
                ["安卓通用"] = "Android",
                ["备份助手"] = "Backup",
                ["刷机资源"] = "Resources",
                ["下载专区"] = "Downloads",
                ["Rom专区"] = "ROMs",
                ["关于"] = "About",
                ["CMD命令行"] = "CMD",
                ["设备管理器"] = "Device Mgr",
                ["正在初始化程序环境..."] = "Initializing...",

                // Home and device information
                ["设备信息"] = "Device Information",
                ["设备状态:"] = "Status:",
                ["A/B分区:"] = "A/B slot:",
                ["连接类型:"] = "Connection:",
                ["CPU厂家:"] = "CPU vendor:",
                ["设备序列号:"] = "Serial:",
                ["CPU代号:"] = "CPU code:",
                ["设备名称:"] = "Device:",
                ["CPU名称:"] = "CPU:",
                ["设备代号:"] = "Codename:",
                ["操作系统:"] = "OS:",
                ["安卓版本:"] = "Android:",
                ["解锁状态:"] = "Unlock:",
                ["内核版本:"] = "Kernel:",
                ["构建日期:"] = "Build:",
                ["电池状态"] = "Battery",
                ["存储与内存"] = "Storage & Memory",
                ["内部存储"] = "Internal Storage",
                ["运行内存"] = "Memory",
                ["快捷重启"] = "Quick Reboot",
                ["快捷工具"] = "Quick Tools",
                ["无线调试"] = "Wireless",
                ["切换槽位"] = "Switch Slot",
                ["刷新设备"] = "Refresh",
                ["更多重启指令"] = "More Reboots",
                ["设备检测"] = "Auto Detect",
                ["工具自检"] = "Self-check",
                ["多设备"] = "Multi-device",
                ["保存设备信息"] = "Save Info",
                ["全功能使用教程"] = "Full Guide",
                ["连接指南"] = "Connection Guide",
                ["选择设备"] = "Device",
                ["已连接"] = "Connected",
                ["未连接"] = "Disconnected",
                ["正在检测中"] = "Detecting...",
                ["未检测到设备"] = "No device detected",
                ["错误"] = "Error",
                ["A槽位"] = "Slot A",
                ["B槽位"] = "Slot B",
                ["已解锁"] = "Unlocked",
                ["未解锁"] = "Locked",
                ["严格模式"] = "Enforcing",
                ["宽容模式"] = "Permissive",
                ["已关闭"] = "Disabled",
                ["单击复制"] = "Click to copy",
                ["开启后持续检测 ADB 与 Fastboot 设备"] = "Continuously detect ADB and Fastboot devices",
                ["广告位招租"] = "Advertise Here",
                ["点击打开链接"] = "Click to open link",
                ["版本信息"] = "Build Info",
                ["电池温度"] = "Battery Temp",
                ["电量"] = "Battery Level",
                ["存储空间"] = "Storage",

                // Screen mirroring
                ["屏幕操作"] = "Screen Controls",
                ["强制竖屏"] = "Portrait",
                ["强制横屏"] = "Landscape",
                ["自动旋转"] = "Auto Rotate",
                ["截屏"] = "Screenshot",
                ["按键模拟"] = "Keys",
                ["返回键"] = "Back",
                ["主页键"] = "Home",
                ["多任务"] = "Recents",
                ["锁屏"] = "Lock",
                ["音量调节"] = "Volume",
                ["音量+"] = "Volume+",
                ["音量-"] = "Volume-",
                ["静音"] = "Mute",
                ["扩展选项"] = "Options",
                ["同步剪切板"] = "Clipboard",
                ["窗口置顶"] = "On Top",
                ["屏幕常亮"] = "Stay Awake",
                ["全屏启动"] = "Fullscreen",
                ["原始角度"] = "Original",
                ["投屏窗口大小"] = "Window Size",
                ["自定义大小"] = "Custom Size",
                ["开始投屏"] = "Start",
                ["结束投屏"] = "Stop",
                ["全自动投屏"] = "Auto Mirror",
                ["虚拟按键"] = "Navigation Bar",
                ["投屏参数调节"] = "Stream Settings",
                ["屏幕清晰度"] = "Bitrate",
                ["投屏帧率："] = "Max FPS:",
                ["投屏标题调节"] = "Window Title",
                ["启动槽位"] = "Active Slot",
                ["投屏日志"] = "Mirror Log",
                ["投屏实时输出..."] = "Mirror output...",

                // Flashing and partition operations
                ["常规镜像刷入"] = "Image Flashing",
                ["请选择需要刷入的文件..."] = "Select an image...",
                ["自动重启"] = "Reboot",
                ["等待FB设备"] = "Wait FB",
                ["开始刷入"] = "Flash",
                ["Bootloader 操作"] = "Bootloader",
                ["解锁/回锁指令"] = "Unlock / Lock",
                ["执行指令"] = "Execute",
                ["小米官方线刷"] = "Xiaomi ROM",
                ["刷机包目录"] = "ROM Folder",
                ["清除数据刷机"] = "Clean All",
                ["保留数据刷机"] = "Keep Data",
                ["清除数据并回锁BL"] = "Wipe + Lock",
                ["开始线刷"] = "Flash",
                ["扩展功能"] = "Utilities",
                ["格式化设备【双清】"] = "Factory Reset",
                ["擦除谷歌锁"] = "Clear Google Lock",
                ["修复ADB异常"] = "Fix ADB",
                ["强开小米USB安全设置"] = "USB Security",
                ["ADB读一加DDR版本"] = "Read DDR",
                ["ADB强开基带调试端口"] = "Diag Port",
                ["ADB切换文件传输"] = "File Mode",
                ["欧加真OCDT分析"] = "Analyze OCDT",
                ["分区表"] = "Partitions",
                ["请输入分区名称..."] = "Search partitions...",
                ["生成XML"] = "XML",
                ["保护分区"] = "Protect",
                ["名称"] = "Name",
                ["大小"] = "Size",
                ["文件路径"] = "File",
                ["执行日志"] = "Log",
                ["刷写文件"] = "Flash File",
                ["操作"] = "Actions",
                ["刷A/B槽"] = "AB",
                ["清除数据"] = "WipeData",
                ["保留数据"] = "Keep Data",
                ["跳过CRC"] = "Skip CRC",
                ["禁用DM校验"] = "Disable DM",
                ["读分区表[开机]"] = "Read (ADB)",
                ["读分区表[FB]"] = "Read (FB)",
                ["写入分区"] = "Flash",
                ["擦除分区"] = "Erase",
                ["擦除指定分区"] = "Erase Selected",
                ["备份字库"] = "Backup NV",
                ["回读分区"] = "Read Back",
                ["备份GPT"] = "Backup GPT",
                ["停止操作"] = "Stop",
                ["等待当前分区完成后停止后续操作"] = "Stop after the current partition completes",
                ["分区名称"] = "Partition",
                ["分区大小"] = "Size",
                ["槽位管理"] = "Slots",
                ["读分区表"] = "Read Table",
                ["读版本信息"] = "Read Version",
                ["机型无误，继续刷入"] = "The model is correct; continue",

                // Files, apps, payload and downloads
                ["读取应用列表"] = "Load Apps",
                ["仅看系统应用"] = "System Apps",
                ["仅显示用户应用"] = "User Apps",
                ["包名搜索"] = "Package Search",
                ["复制包名"] = "Copy Package",
                ["查看冻结应用"] = "Frozen Apps",
                ["冻结"] = "Freeze",
                ["解冻"] = "Unfreeze",
                ["提取 APK 安装包"] = "Extract APK",
                ["清除应用数据"] = "Clear Data",
                ["卸载选中应用"] = "Uninstall",
                ["运行日志"] = "Runtime Log",
                ["来源/URL"] = "Source / URL",
                ["路径/URL"] = "Path / URL",
                ["输出路径"] = "Output Folder",
                ["保存目录"] = "Save Folder",
                ["日志"] = "Log",
                ["解包Payload"] = "Extract Payload",
                ["提取镜像"] = "Extract Images",
                ["选择目录"] = "Select Folder",
                ["打开目录"] = "Open Folder",
                ["全选"] = "Select All",
                ["✓ 全选"] = "✓ Select All",
                ["清空日志"] = "Clear Log",
                ["清空列表"] = "Clear List",
                ["并发数:"] = "Threads:",
                ["图片"] = "Photos",
                ["视频"] = "Videos",
                ["通讯录"] = "Contacts",
                ["🔄 加载图片"] = "🔄 Load Photos",
                ["💾 备份选中图片"] = "💾 Back Up Selected",
                ["📥 恢复通讯录"] = "📥 Restore Contacts",
                ["镜像下载"] = "Image Download",
                ["ROM获取"] = "ROM Download",
                ["联想查包"] = "Lenovo Package Lookup",
                ["复制下载链接"] = "Copy Download Link",
                ["修复网络异常"] = "Repair Network",
                ["快捷云提取"] = "Cloud Extraction",

                // About and common actions
                ["项目作者"] = "Author",
                ["支持开发者"] = "Support Development",
                ["感谢支持"] = "Thank You",
                ["社交媒体"] = "Social Media",
                ["技术栈与开源致谢"] = "Technology & Open-source Credits",
                ["核心开发环境"] = "Core Development Stack",
                ["赞助与支持"] = "Sponsors",
                ["品牌："] = "Brand:",
                ["系列："] = "Series:",
                ["机型："] = "Model:",
                ["版本："] = "Version:",
                ["分区："] = "Partition:",
                ["路径："] = "Path:",
                ["选择"] = "Browse",
                ["开始"] = "Start",
                ["停止"] = "Stop",
                ["关闭"] = "Close",
                ["返回"] = "Back",
                ["返回上级"] = "Back",
                ["返回主页"] = "Home",
                ["继续"] = "Continue",
                ["查询"] = "Search",
                ["搜索"] = "Search",
                ["刷新"] = "Refresh",
                ["读取信息"] = "Read Info",
                ["复制链接"] = "Copy Link",
                ["加载目录"] = "Load Folder",
                ["开始安装"] = "Install",
                ["开始查询"] = "Search",
                ["开始打包"] = "Build",
                ["开始合并"] = "Merge",
                ["开始检测设备"] = "Detect Device",
                ["开始解包"] = "Extract",
                ["开始签名"] = "Sign",
                ["开始生成"] = "Generate",
                ["开始下载"] = "Download",
                ["开始修复"] = "Repair",
                ["开始制作"] = "Create",
                ["安装APK"] = "Install APK",
                ["高级选项"] = "Options",
                ["参数调节"] = "Settings",
                ["风险提示："] = "Warning:",
                ["检测中..."] = "Detecting...",
                ["解析中..."] = "Parsing...",
                ["等待开始下载..."] = "Waiting to download...",
                ["等待用户读取分区表..."] = "Waiting for partition data...",

                // Additional labels used by the specialist tool pages
                ["开发"] = "Development",
                ["（按加入时间排序）"] = "(by join date)",
                ["（查询的刷机包均为最新版）"] = "(latest packages only)",
                ["（可选）"] = "(optional)",
                ["（默认当前用户桌面）"] = "(desktop by default)",
                ["（默认即可）"] = "(default recommended)",
                ["【第一步】下载降级助手 APK"] = "Step 1: Download Assistant APK",
                ["【第二步】查询降级包"] = "Step 2: Find Downgrade Package",
                ["【第三步】下载并推送"] = "Step 3: Download and Push",
                ["安装到 Recovery"] = "Install to Recovery",
                ["安卓版本"] = "Android Version",
                ["安卓通用卡刷转线刷"] = "Android OTA to Fastboot ROM",
                ["安卓通用AK3"] = "Android AK3",
                ["版本"] = "Version",
                ["版本信息:"] = "Version:",
                ["包名"] = "Package Name",
                ["保护基带分区和数据分区不被写入"] = "Protect modem and data partitions",
                ["保护基带指纹"] = "Protect LUN5",
                ["保护数据"] = "Protect Data",
                ["保留强制加密"] = "Keep Forced Encryption",
                ["备份基带指纹"] = "Backup NV",
                ["本地降级包"] = "Local Downgrade Package",
                ["常规脱机修补模式"] = "Standard Offline Patching",
                ["初始下载链接"] = "Original Download Link",
                ["传出文件到电脑"] = "Pull File to PC",
                ["传入文件到"] = "Push File to",
                ["串口"] = "COM Port",
                ["春秋检测"] = "Anti-rollback Check",
                ["存储器"] = "Storage Device",
                ["搭配KernelSU"] = "Use with KernelSU",
                ["冻结更新"] = "Freeze Updates",
                ["额外安装LSP"] = "Install LSP Too",
                ["发送引导"] = "Send Firehose",
                ["引导文件"] = "Firehose",
                ["云端引导"] = "Cloud",
                ["自动格式化"] = "WipeData",
                ["EDL重启"] = "EDL Reboot",
                ["非链式"] = "Non-chained",
                ["分段文件："] = "Split File:",
                ["分区表校验（实验性功能）："] = "Check GPT (Beta):",
                ["分析"] = "Analyze",
                ["分析 vbmeta"] = "Analyze vbmeta",
                ["高通"] = "Qualcomm",
                ["高通工具箱"] = "Qualcomm Tools",
                ["高通修复FastbootD关键分区"] = "Qualcomm FastbootD Repair",
                ["格式化LUN"] = "Format LUN",
                ["根目录"] = "Root Directory",
                ["官方下载器"] = "Official Downloader",
                ["管理器安装包"] = "Manager APK",
                ["管理器并安装"] = "Manager and Install",
                ["过滤多余xml"] = "Filter Extra XML",
                ["合并散包super"] = "Build super from Images",
                ["合并OFP分段Super"] = "Merge Split OFP super",
                ["恢复出厂设置"] = "Factory Reset",
                ["恢复更新"] = "Restore Updates",
                ["机型"] = "Model",
                ["加载位置："] = "Loaded From:",
                ["加载云端引导..."] = "Loading Cloud Boot Image...",
                ["架构"] = "Architecture",
                ["建议线刷后"] = "Recommended after Flashing",
                ["降级包保存目录"] = "Download Folder",
                ["降级助手 APK"] = "Downgrade Assistant APK",
                ["解包"] = "Extract",
                ["解包后的文件夹"] = "Extracted Folder",
                ["解包OFP"] = "Extract OFP",
                ["解包OPS"] = "Extract OPS",
                ["解决安卓15无法安装应用"] = "Fix Android 15 App Installation",
                ["解锁状态"] = "Unlock Status",
                ["仅在开机模式下可用（需ROOT）"] = "Booted Mode Only (ROOT Required)",
                ["仅FBD"] = "FBD",
                ["镜像"] = "Image",
                ["镜像目录："] = "Image Folder:",
                ["开机状态下读取（需ROOT）"] = "Read While Booted (ROOT Required)",
                ["开启"] = "Enable",
                ["开始隐藏"] = "Hide",
                ["可用降级版本"] = "Available Downgrade Versions",
                ["控制与恢复"] = "Control and Restore",
                ["块"] = "Blocks",
                ["快捷安装"] = "Quick Install",
                ["快捷操作"] = "Quick Actions",
                ["类型："] = "Type:",
                ["联发科"] = "MediaTek",
                ["联发科修复FastbootD关键分区"] = "MediaTek FastbootD Repair",
                ["联想"] = "Lenovo",
                ["链式"] = "Chained",
                ["路径"] = "Path",
                ["魅族"] = "Meizu",
                ["密钥认证"] = "Key Authentication",
                ["鸣谢"] = "Credits",
                ["鸣谢以下成员对代码的贡献"] = "Code Contributors",
                ["模块安装"] = "Module Installation",
                ["模式"] = "Mode",
                ["目标分区"] = "Target Partition",
                ["欧加通用AK3"] = "OPlus AK3",
                ["欧加真"] = "OPlus/Realme",
                ["排除绑定指纹和基带的关键分区"] = "Exclude fingerprint/modem partitions",
                ["排除敏感文件"] = "Exclude Sensitive Files",
                ["排除无关多余"] = "Exclude Unrelated Files",
                ["品牌"] = "Brand",
                ["平台:"] = "Platform:",
                ["启动 NDM 下载器"] = "Open NDM Downloader",
                ["启动NEKO下载器"] = "Open NEKO Downloader",
                ["起始扇区"] = "Start Sector",
                ["前后端开发 & 核心架构"] = "Full-stack Development & Architecture",
                ["强开基带调试端口"] = "Diag Port",
                ["强开OEM"] = "Open OEM",
                ["AB通刷"] = "AB",
                ["强力线刷"] = "ForceFlash",
                ["强制 rootfs"] = "Force rootfs",
                ["切换"] = "Switch",
                ["切换A槽"] = "Switch SlotA",
                ["清理残留文件"] = "Clean Residual Files",
                ["请选择快捷提取方案 ↓"] = "Choose an Extraction Method ↓",
                ["请选择一个解压好的刷机包文件..."] = "Select an extracted firmware file...",
                ["请选择隐藏环境资源包7z文件"] = "Select the environment resource 7z file",
                ["区域"] = "Region",
                ["取消"] = "Cancel",
                ["全量包"] = "Full Package",
                ["全量包模式"] = "Full ROM",
                ["软件名称"] = "Software Name",
                ["散包路径："] = "Image Folder:",
                ["删除"] = "Delete",
                ["设备"] = "Device",
                ["设备报告"] = "Device Report",
                ["设备代号"] = "Codename",
                ["设备名称"] = "Device Name",
                ["设备序列号"] = "Serial Number",
                ["生成 OCDT"] = "Generate OCDT",
                ["生成TXT线刷脚本"] = "Generate TXT Flash Script",
                ["实用软件"] = "Useful Software",
                ["使用"] = "Use",
                ["使用建议"] = "Recommendations",
                ["使用新链接继续"] = "Continue with New Link",
                ["手动选择模块文件"] = "Select Module",
                ["售后包"] = "Service Package",
                ["售后包模式"] = "Service ROM",
                ["刷机驱动"] = "USB Drivers",
                ["刷写模式"] = "Flash Mode",
                ["刷写模式："] = "Flash Mode:",
                ["刷写设置"] = "Flash Settings",
                ["刷新目录"] = "Refresh Folder",
                ["双击选择存放路径"] = "Double-click to browse",
                ["搜索分区名称"] = "Search Partitions",
                ["锁定BL"] = "Lock",
                ["特别鸣谢开源项目（名称按A~Z排序）"] = "Open-source Credits (A-Z)",
                ["提取分区"] = "Extract Partition",
                ["提取或写入"] = "Extract or Flash",
                ["添加救砖模块"] = "Add Recovery Module",
                ["停止当前页面正在执行的操作"] = "Stop",
                ["停止检测设备"] = "Stop Detection",
                ["推送卡刷包"] = "Push OTA Package",
                ["脱机修补 & 全自动ROOT"] = "Offline Patching & Automatic ROOT",
                ["网络调试端口："] = "Wireless Debugging Port:",
                ["微信"] = "WeChat",
                ["文件"] = "File",
                ["文件存放"] = "File Storage",
                ["文件大小"] = "File Size",
                ["文件选择"] = "File Selection",
                ["系列"] = "Series",
                ["系统"] = "System",
                ["下载错误"] = "Download Error",
                ["下载配置"] = "Download Settings",
                ["下载隐藏环境资源包V6.0"] = "Download Environment Resources V6.0",
                ["显示"] = "Show",
                ["线程数"] = "Threads",
                ["线刷"] = "Fastboot Flash",
                ["相册目录"] = "Photo Folder",
                ["项目号"] = "Project ID",
                ["项目号填入到此处点击生成."] = "Enter the project ID, then click Generate.",
                ["写入GPT"] = "Write GPT",
                ["新版本AOSP签名"] = "New AOSP Signature",
                ["新的动态链接"] = "New Dynamic Link",
                ["新建文件"] = "New File",
                ["新建文件夹"] = "New Folder",
                ["信息"] = "Information",
                ["修补"] = "Patch",
                ["修补 vbmeta 标志"] = "Patch vbmeta Flags",
                ["修补方案"] = "Patch Method",
                ["修补模式"] = "Patch Mode",
                ["修补日志"] = "Patch Log",
                ["修复FastbootD"] = "Fix FBD",
                ["修复Super真死"] = "Fix Super"
            };

        private readonly Dictionary<(DependencyObject Owner, DependencyProperty Property), object?>
            _localizedOriginalValues = new();

        private UiLanguage _currentUiLanguage = UiLanguage.SimplifiedChinese;

        // Keep local values (including UnsetValue) so Chinese resumes its original styles.
        private readonly Dictionary<(DependencyObject Owner, DependencyProperty Property), object>
            _englishLayoutOriginalValues = new();

        private static readonly DataTemplate CompactEnglishButtonTemplate = (DataTemplate)XamlReader.Parse(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
            "<Viewbox Stretch='Uniform' StretchDirection='DownOnly'>" +
            "<TextBlock Text='{Binding}' TextWrapping='NoWrap'/></Viewbox></DataTemplate>");

        private sealed class LocalizedTextState
        {
            public LocalizedTextState(string rawText)
            {
                RawText = rawText;
            }

            public string RawText { get; }
        }

        private static string LanguagePreferencePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VioletTool",
                "ui-language.txt");

        private void InitializeLanguageUi()
        {
            _currentUiLanguage = LoadLanguagePreference();
            ApplyUiLanguage(_currentUiLanguage, persist: false);
            ContentRendered += (_, _) =>
            {
                if (_currentUiLanguage == UiLanguage.English)
                {
                    TranslateTreeToEnglish(this);
                    RefreshDynamicLocalizedUi();
                }
            };
        }

        private void LanguageSwitchButton_Click(object sender, RoutedEventArgs e)
        {
            UiLanguage nextLanguage = _currentUiLanguage == UiLanguage.SimplifiedChinese
                ? UiLanguage.English
                : UiLanguage.SimplifiedChinese;

            ApplyUiLanguage(nextLanguage, persist: true);
        }

        private void ApplyUiLanguage(UiLanguage language, bool persist)
        {
            RestoreLocalizedValues();

            _currentUiLanguage = language;
            string cultureName = language == UiLanguage.English ? "en-US" : "zh-CN";
            CultureInfo uiCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
            Thread.CurrentThread.CurrentUICulture = uiCulture;
            Language = XmlLanguage.GetLanguage(cultureName);

            if (language == UiLanguage.English)
            {
                TranslateTreeToEnglish(this);
            }

            RefreshDynamicLocalizedUi();
            UpdateLanguageSwitchButton();

            if (persist)
            {
                SaveLanguagePreference(language);
            }
        }

        private void TranslateTreeToEnglish(DependencyObject root)
        {
            var pending = new Stack<DependencyObject>();
            var visited = new HashSet<DependencyObject>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                DependencyObject current = pending.Pop();
                if (!visited.Add(current))
                {
                    continue;
                }

                TranslateObjectToEnglish(current);

                // HandyControl items and rich headers are not reliably logical/visual
                // children before their templates have been materialized at startup.
                if (current is HandyControl.Controls.SimpleItemsControl simpleItems)
                {
                    foreach (object item in simpleItems.Items)
                    {
                        if (item is DependencyObject dependencyItem)
                            pending.Push(dependencyItem);
                    }
                }
                if (current is HandyControl.Controls.HeaderedSimpleItemsControl headered &&
                    headered.Header is DependencyObject header)
                {
                    pending.Push(header);
                }

                foreach (object child in LogicalTreeHelper.GetChildren(current))
                {
                    if (child is DependencyObject dependencyChild)
                    {
                        pending.Push(dependencyChild);
                    }
                }

                if (current is Visual or System.Windows.Media.Media3D.Visual3D)
                {
                    int visualChildCount = VisualTreeHelper.GetChildrenCount(current);
                    for (int index = 0; index < visualChildCount; index++)
                    {
                        pending.Push(VisualTreeHelper.GetChild(current, index));
                    }
                }
            }
        }

        private void TranslateObjectToEnglish(DependencyObject target)
        {
            if (target is ComboBox { Name: "EdlResetComboBox", SelectedIndex: -1 } resetCombo)
            {
                TranslateProperty(resetCombo, ComboBox.TextProperty);
            }

            if (target is Window)
            {
                TranslateProperty(target, Window.TitleProperty);
            }

            if (target is TextBlock textBlock)
            {
                if (textBlock.Tag is LocalizedTextState)
                {
                    RefreshDynamicTextBlock(textBlock);
                }
                else
                {
                    TranslateProperty(target, TextBlock.TextProperty);
                }
            }

            if (target is Run)
            {
                TranslateProperty(target, Run.TextProperty);
            }

            if (target is ContentControl)
            {
                TranslateProperty(target, ContentControl.ContentProperty);
            }

            if (target is HeaderedContentControl)
            {
                TranslateProperty(target, HeaderedContentControl.HeaderProperty);
            }

            if (target is HeaderedItemsControl)
            {
                TranslateProperty(target, HeaderedItemsControl.HeaderProperty);
            }

            if (target is HandyControl.Controls.HeaderedSimpleItemsControl)
            {
                TranslateProperty(
                    target,
                    HandyControl.Controls.HeaderedSimpleItemsControl.HeaderProperty);
            }

            if (target is FrameworkElement)
            {
                TranslateProperty(target, ToolTipService.ToolTipProperty);
            }
        }

        private void TranslateProperty(DependencyObject owner, DependencyProperty property)
        {
            if (BindingOperations.IsDataBound(owner, property))
            {
                return;
            }

            object originalValue = owner.GetValue(property);
            if (originalValue is not string sourceText ||
                !EnglishUiTexts.TryGetValue(sourceText, out string? translatedText))
            {
                return;
            }

            var key = (owner, property);
            if (!_localizedOriginalValues.ContainsKey(key))
            {
                _localizedOriginalValues.Add(key, originalValue);
            }

            owner.SetCurrentValue(property, translatedText);

            if (property == ContentControl.ContentProperty &&
                owner is System.Windows.Controls.Primitives.ButtonBase button &&
                button.ContentTemplate == null && button.ContentTemplateSelector == null &&
                !BindingOperations.IsDataBound(button, ContentControl.ContentTemplateProperty))
            {
                SetEnglishLayoutValue(button, ContentControl.ContentTemplateProperty,
                    CompactEnglishButtonTemplate);
                SetEnglishLayoutValue(button, UIElement.ClipToBoundsProperty, true);
            }

            if (property == TextBlock.TextProperty && owner is TextBlock label &&
                label.TextWrapping == TextWrapping.NoWrap)
            {
                SetEnglishLayoutValue(label, TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
                SetEnglishLayoutValue(label, UIElement.ClipToBoundsProperty, true);
            }
        }

        private void SetEnglishLayoutValue(DependencyObject owner, DependencyProperty property, object value)
        {
            if (BindingOperations.IsDataBound(owner, property)) return;
            var key = (owner, property);
            if (!_englishLayoutOriginalValues.ContainsKey(key))
                _englishLayoutOriginalValues.Add(key, owner.ReadLocalValue(property));
            owner.SetCurrentValue(property, value);
        }

        private string LocalizeUiText(string sourceText)
        {
            if (_currentUiLanguage != UiLanguage.English)
            {
                return sourceText;
            }

            if (EnglishUiTexts.TryGetValue(sourceText, out string? translatedText))
            {
                return translatedText;
            }

            const string detectionFailurePrefix = "检测失败:";
            if (sourceText.StartsWith(detectionFailurePrefix, StringComparison.Ordinal))
            {
                return "Detection failed:" + sourceText[detectionFailurePrefix.Length..];
            }

            return sourceText;
        }

        private void SetLocalizedText(TextBlock textBlock, string rawText)
        {
            textBlock.Tag = new LocalizedTextState(rawText);
            textBlock.Text = LocalizeUiText(rawText);
        }

        private static string GetRawLocalizedText(TextBlock textBlock)
        {
            return textBlock.Tag is LocalizedTextState state
                ? state.RawText
                : textBlock.Text;
        }

        private void RefreshDynamicLocalizedUi()
        {
            RefreshDynamicTextBlock(DeviceStatusText);
            RefreshDynamicTextBlock(ConnectionTypeText);
            RefreshDynamicTextBlock(UnlockStatusText);
            RefreshDynamicTextBlock(ABPartitionText);
            RefreshDynamicTextBlock(SelinuxStatusText);
            RefreshDynamicTextBlock(BottomConnectionTypeText);
            RefreshDynamicTextBlock(BroadcastNoticeTextBlock);

            if (_activeBroadcastNotice != null)
            {
                string localizedNotice = LocalizeUiText(_activeBroadcastNotice.Text);
                BroadcastNoticeHost.ToolTip = _activeBroadcastNotice.Url == null
                    ? localizedNotice
                    : $"{localizedNotice}\n{LocalizeUiText("点击打开链接")}";
            }
        }

        private void RefreshDynamicTextBlock(TextBlock? textBlock)
        {
            if (textBlock?.Tag is LocalizedTextState state)
            {
                textBlock.Text = LocalizeUiText(state.RawText);
            }
        }

        private void RestoreLocalizedValues()
        {
            foreach (var item in _englishLayoutOriginalValues)
            {
                if (item.Value == DependencyProperty.UnsetValue)
                    item.Key.Owner.ClearValue(item.Key.Property);
                else
                    item.Key.Owner.SetCurrentValue(item.Key.Property, item.Value);
            }
            _englishLayoutOriginalValues.Clear();

            foreach (KeyValuePair<(DependencyObject Owner, DependencyProperty Property), object?> item
                     in _localizedOriginalValues)
            {
                if (item.Key.Property == ComboBox.TextProperty &&
                    item.Key.Owner is ComboBox { SelectedIndex: >= 0 })
                    continue;
                item.Key.Owner.SetCurrentValue(item.Key.Property, item.Value);
            }

            _localizedOriginalValues.Clear();
        }

        private void UpdateLanguageSwitchButton()
        {
            bool isEnglish = _currentUiLanguage == UiLanguage.English;
            LanguageSwitchLabel.Text = isEnglish ? "中" : "EN";
            LanguageSwitchButton.ToolTip = isEnglish
                ? "Switch to 简体中文"
                : "切换至 English";
            LanguageSwitchButton.SetValue(
                AutomationProperties.NameProperty,
                isEnglish ? "Switch to Simplified Chinese" : "切换至英文");
        }

        private static UiLanguage LoadLanguagePreference()
        {
            try
            {
                if (File.Exists(LanguagePreferencePath))
                {
                    string savedValue = File.ReadAllText(LanguagePreferencePath).Trim();
                    if (string.Equals(savedValue, "en-US", StringComparison.OrdinalIgnoreCase))
                    {
                        return UiLanguage.English;
                    }
                }
            }
            catch
            {
                // Preference read failures should never prevent the main window from opening.
            }

            return UiLanguage.SimplifiedChinese;
        }

        private static void SaveLanguagePreference(UiLanguage language)
        {
            try
            {
                string? directory = Path.GetDirectoryName(LanguagePreferencePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(
                    LanguagePreferencePath,
                    language == UiLanguage.English ? "en-US" : "zh-CN");
            }
            catch
            {
                // The language still changes for this session if the preference cannot be saved.
            }
        }
    }
}
