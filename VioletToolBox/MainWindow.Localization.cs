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
            English,
            Japanese
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
                ["春秋检测"] = "Chunqiu Checker",
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
                ["下载隐藏环境资源包V7.0"] = "Download Environment Resources V7.0",
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
                ["修复Super真死"] = "Fix Super",

                // Runtime status strings produced by code rather than by the XAML.
                ["等待设备连接..."] = "Waiting for device...",
                ["等待设备连接稳定..."] = "Waiting for a stable connection...",
                ["检测失败"] = "Detection failed"
            };

        // Japanese UI texts. The keys are the Simplified Chinese source strings, so this table
        // stays in sync with EnglishUiTexts and can be extended the same way.
        private static readonly IReadOnlyDictionary<string, string> JapaneseUiTexts =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // Shell and navigation
                ["紫罗兰工具箱"] = "紫羅蘭ツールボックス",
                ["主页"] = "ホーム",
                ["投屏"] = "画面ミラーリング",
                ["刷写功能"] = "フラッシュ",
                ["基本刷入"] = "基本フラッシュ",
                ["可视刷写"] = "ビジュアルフラッシュ",
                ["欧加线刷"] = "OPPO / OnePlus",
                ["EDL刷写"] = "EDL フラッシュ",
                ["降级助手"] = "ダウングレード支援",
                ["实用功能"] = "ツール",
                ["模块专区"] = "モジュール",
                ["断点续传"] = "レジュームダウンロード",
                ["文件传输"] = "ファイル転送",
                ["脱机修补"] = "オフラインパッチ",
                ["应用管理"] = "アプリ管理",
                ["安卓通用"] = "Android 共通ツール",
                ["备份助手"] = "バックアップ",
                ["刷机资源"] = "フラッシュリソース",
                ["下载专区"] = "ダウンロード",
                ["Rom专区"] = "ROM センター",
                ["关于"] = "このアプリについて",
                ["CMD命令行"] = "CMD",
                ["设备管理器"] = "デバイスマネージャー",
                ["正在初始化程序环境..."] = "初期化中...",

                // Home and device information
                ["设备信息"] = "端末情報",
                ["设备状态:"] = "状態:",
                ["A/B分区:"] = "A/B スロット:",
                ["连接类型:"] = "接続:",
                ["CPU厂家:"] = "CPU ベンダー:",
                ["设备序列号:"] = "シリアル:",
                ["CPU代号:"] = "CPU コード:",
                ["设备名称:"] = "端末名:",
                ["CPU名称:"] = "CPU:",
                ["设备代号:"] = "コードネーム:",
                ["操作系统:"] = "OS:",
                ["安卓版本:"] = "Android:",
                ["解锁状态:"] = "ロック状態:",
                ["内核版本:"] = "カーネル:",
                ["构建日期:"] = "ビルド日時:",
                ["电池状态"] = "バッテリー",
                ["存储与内存"] = "ストレージとメモリ",
                ["内部存储"] = "内部ストレージ",
                ["运行内存"] = "メモリ",
                ["快捷重启"] = "クイック再起動",
                ["快捷工具"] = "クイックツール",
                ["无线调试"] = "ワイヤレスデバッグ",
                ["切换槽位"] = "スロット切替",
                ["刷新设备"] = "更新",
                ["更多重启指令"] = "その他の再起動",
                ["设备检测"] = "自動検出",
                ["工具自检"] = "セルフチェック",
                ["多设备"] = "マルチデバイス",
                ["保存设备信息"] = "情報を保存",
                ["全功能使用教程"] = "使い方ガイド",
                ["连接指南"] = "接続ガイド",
                ["选择设备"] = "デバイス",
                ["已连接"] = "接続済み",
                ["未连接"] = "未接続",
                ["正在检测中"] = "検出中...",
                ["未检测到设备"] = "デバイスが見つかりません",
                ["错误"] = "エラー",
                ["A槽位"] = "スロット A",
                ["B槽位"] = "スロット B",
                ["已解锁"] = "解除済み",
                ["未解锁"] = "ロック中",
                ["严格模式"] = "Enforcing",
                ["宽容模式"] = "Permissive",
                ["已关闭"] = "無効",
                ["单击复制"] = "クリックでコピー",
                ["开启后持续检测 ADB 与 Fastboot 设备"] = "ADB と Fastboot デバイスを常時検出します",
                ["广告位招租"] = "広告掲載募集中",
                ["点击打开链接"] = "クリックでリンクを開く",
                ["版本信息"] = "ビルド情報",
                ["电池温度"] = "バッテリー温度",
                ["电量"] = "バッテリー残量",
                ["存储空间"] = "ストレージ",

                // Screen mirroring
                ["屏幕操作"] = "画面操作",
                ["强制竖屏"] = "縦画面",
                ["强制横屏"] = "横画面",
                ["自动旋转"] = "自動回転",
                ["截屏"] = "スクリーンショット",
                ["按键模拟"] = "キー操作",
                ["返回键"] = "戻る",
                ["主页键"] = "ホーム",
                ["多任务"] = "タスク",
                ["锁屏"] = "ロック",
                ["音量调节"] = "音量",
                ["音量+"] = "音量+",
                ["音量-"] = "音量-",
                ["静音"] = "ミュート",
                ["扩展选项"] = "オプション",
                ["同步剪切板"] = "クリップボード同期",
                ["窗口置顶"] = "最前面",
                ["屏幕常亮"] = "常時点灯",
                ["全屏启动"] = "全画面で起動",
                ["原始角度"] = "元の角度",
                ["投屏窗口大小"] = "ウィンドウサイズ",
                ["自定义大小"] = "カスタムサイズ",
                ["开始投屏"] = "開始",
                ["结束投屏"] = "停止",
                ["全自动投屏"] = "自動ミラーリング",
                ["虚拟按键"] = "ナビゲーションバー",
                ["投屏参数调节"] = "ミラーリング設定",
                ["屏幕清晰度"] = "ビットレート",
                ["投屏帧率："] = "最大 FPS:",
                ["投屏标题调节"] = "ウィンドウタイトル",
                ["启动槽位"] = "起動スロット",
                ["投屏日志"] = "ミラーリングログ",
                ["投屏实时输出..."] = "ミラーリング出力...",

                // Flashing and partition operations
                ["常规镜像刷入"] = "イメージ書き込み",
                ["请选择需要刷入的文件..."] = "イメージを選択...",
                ["自动重启"] = "自動再起動",
                ["等待FB设备"] = "FB 待機",
                ["开始刷入"] = "書き込み",
                ["Bootloader 操作"] = "Bootloader",
                ["解锁/回锁指令"] = "ロック解除 / 再ロック",
                ["执行指令"] = "実行",
                ["小米官方线刷"] = "Xiaomi 公式フラッシュ",
                ["刷机包目录"] = "ROM フォルダ",
                ["清除数据刷机"] = "データ消去してフラッシュ",
                ["保留数据刷机"] = "データ保持してフラッシュ",
                ["清除数据并回锁BL"] = "消去 + 再ロック",
                ["开始线刷"] = "書き込み",
                ["扩展功能"] = "ユーティリティ",
                ["格式化设备【双清】"] = "初期化",
                ["擦除谷歌锁"] = "Google ロック解除",
                ["修复ADB异常"] = "ADB 修復",
                ["强开小米USB安全设置"] = "USB セキュリティ",
                ["ADB读一加DDR版本"] = "DDR 読み取り",
                ["ADB强开基带调试端口"] = "Diag ポート",
                ["ADB切换文件传输"] = "ファイルモード",
                ["欧加真OCDT分析"] = "OCDT 解析",
                ["分区表"] = "パーティション表",
                ["请输入分区名称..."] = "パーティション検索...",
                ["生成XML"] = "XML 生成",
                ["保护分区"] = "保護",
                ["名称"] = "名前",
                ["大小"] = "サイズ",
                ["文件路径"] = "ファイルパス",
                ["执行日志"] = "ログ",
                ["刷写文件"] = "書き込みファイル",
                ["操作"] = "操作",
                ["刷A/B槽"] = "A/B 両スロット",
                ["清除数据"] = "データ消去",
                ["保留数据"] = "データ保持",
                ["跳过CRC"] = "CRC スキップ",
                ["禁用DM校验"] = "DM 検証を無効化",
                ["读分区表[开机]"] = "読取 (ADB)",
                ["读分区表[FB]"] = "読取 (FB)",
                ["写入分区"] = "書き込み",
                ["擦除分区"] = "消去",
                ["擦除指定分区"] = "選択を消去",
                ["备份字库"] = "全パーティションをバックアップ",
                ["回读分区"] = "読み戻し",
                ["备份GPT"] = "GPT バックアップ",
                ["停止操作"] = "停止",
                ["等待当前分区完成后停止后续操作"] = "現在のパーティション完了後に停止します",
                ["分区名称"] = "パーティション",
                ["分区大小"] = "サイズ",
                ["槽位管理"] = "スロット",
                ["读分区表"] = "パーティション表を読取",
                ["读版本信息"] = "バージョン読取",
                ["机型无误，继续刷入"] = "機種は一致しています。続行します",

                // Files, apps, payload and downloads
                ["读取应用列表"] = "アプリ一覧",
                ["仅看系统应用"] = "システムアプリ",
                ["仅显示用户应用"] = "ユーザーアプリ",
                ["包名搜索"] = "パッケージ検索",
                ["复制包名"] = "パッケージ名をコピー",
                ["查看冻结应用"] = "凍結アプリ",
                ["冻结"] = "凍結",
                ["解冻"] = "凍結解除",
                ["提取 APK 安装包"] = "APK 抽出",
                ["清除应用数据"] = "データ消去",
                ["卸载选中应用"] = "アンインストール",
                ["运行日志"] = "実行ログ",
                ["来源/URL"] = "取得元 / URL",
                ["路径/URL"] = "パス / URL",
                ["输出路径"] = "出力先",
                ["保存目录"] = "保存先",
                ["日志"] = "ログ",
                ["解包Payload"] = "Payload 展開",
                ["提取镜像"] = "イメージ抽出",
                ["选择目录"] = "フォルダ選択",
                ["打开目录"] = "フォルダを開く",
                ["全选"] = "すべて選択",
                ["✓ 全选"] = "✓ すべて選択",
                ["清空日志"] = "ログを消去",
                ["清空列表"] = "リストを消去",
                ["并发数:"] = "スレッド数:",
                ["图片"] = "写真",
                ["视频"] = "動画",
                ["通讯录"] = "連絡先",
                ["🔄 加载图片"] = "🔄 写真を読み込む",
                ["💾 备份选中图片"] = "💾 選択をバックアップ",
                ["📥 恢复通讯录"] = "📥 連絡先を復元",
                ["镜像下载"] = "イメージ取得",
                ["ROM获取"] = "ROM 取得",
                ["联想查包"] = "Lenovo パッケージ検索",
                ["复制下载链接"] = "リンクをコピー",
                ["修复网络异常"] = "ネットワーク修復",
                ["快捷云提取"] = "クラウド抽出",

                // About and common actions
                ["项目作者"] = "作者",
                ["支持开发者"] = "開発を支援",
                ["感谢支持"] = "ありがとうございます",
                ["社交媒体"] = "SNS",
                ["技术栈与开源致谢"] = "技術スタックと OSS クレジット",
                ["核心开发环境"] = "開発環境",
                ["赞助与支持"] = "スポンサー",
                ["品牌："] = "ブランド:",
                ["系列："] = "シリーズ:",
                ["机型："] = "機種:",
                ["版本："] = "バージョン:",
                ["分区："] = "パーティション:",
                ["路径："] = "パス:",
                ["选择"] = "参照",
                ["开始"] = "開始",
                ["停止"] = "停止",
                ["关闭"] = "閉じる",
                ["返回"] = "戻る",
                ["返回上级"] = "上の階層へ",
                ["返回主页"] = "ホーム",
                ["继续"] = "続行",
                ["查询"] = "検索",
                ["搜索"] = "検索",
                ["刷新"] = "更新",
                ["读取信息"] = "情報を読み取る",
                ["复制链接"] = "リンクをコピー",
                ["加载目录"] = "フォルダを読み込む",
                ["开始安装"] = "インストール",
                ["开始查询"] = "検索",
                ["开始打包"] = "作成",
                ["开始合并"] = "結合",
                ["开始检测设备"] = "デバイス検出",
                ["开始解包"] = "展開",
                ["开始签名"] = "署名",
                ["开始生成"] = "生成",
                ["开始下载"] = "ダウンロード",
                ["开始修复"] = "修復",
                ["开始制作"] = "作成",
                ["安装APK"] = "APK をインストール",
                ["高级选项"] = "詳細設定",
                ["参数调节"] = "設定",
                ["风险提示："] = "リスクについて:",
                ["检测中..."] = "検出中...",
                ["解析中..."] = "解析中...",
                ["等待开始下载..."] = "ダウンロード待機中...",
                ["等待用户读取分区表..."] = "パーティション情報の読み取りを待機中...",

                // Additional labels used by the specialist tool pages
                ["开发"] = "開発",
                ["（按加入时间排序）"] = "（参加順）",
                ["（查询的刷机包均为最新版）"] = "（最新版のみ）",
                ["（可选）"] = "（任意）",
                ["（默认当前用户桌面）"] = "（既定ではデスクトップ）",
                ["（默认即可）"] = "（既定のままで可）",
                ["【第一步】下载降级助手 APK"] = "手順 1: ダウングレード支援 APK を取得",
                ["【第二步】查询降级包"] = "手順 2: ダウングレードパッケージを検索",
                ["【第三步】下载并推送"] = "手順 3: 取得して転送",
                ["安装到 Recovery"] = "Recovery へインストール",
                ["安卓版本"] = "Android バージョン",
                ["安卓通用卡刷转线刷"] = "Android OTA から Fastboot ROM へ",
                ["安卓通用AK3"] = "Android AK3",
                ["版本"] = "バージョン",
                ["版本信息:"] = "バージョン:",
                ["包名"] = "パッケージ名",
                ["保护基带分区和数据分区不被写入"] = "モデムとデータのパーティションを保護します",
                ["保护基带指纹"] = "LUN5 を保護",
                ["保护数据"] = "データを保護",
                ["保留强制加密"] = "強制暗号化を維持",
                ["备份基带指纹"] = "NV バックアップ",
                ["本地降级包"] = "ローカルパッケージ",
                ["常规脱机修补模式"] = "通常のオフラインパッチ",
                ["初始下载链接"] = "元のダウンロードリンク",
                ["传出文件到电脑"] = "PC へファイルを取得",
                ["传入文件到"] = "ファイル転送先：",
                ["串口"] = "COM ポート",
                ["春秋检测"] = "春秋检测",
                ["存储器"] = "ストレージデバイス",
                ["搭配KernelSU"] = "KernelSU と併用",
                ["冻结更新"] = "更新を凍結",
                ["额外安装LSP"] = "LSP もインストール",
                ["发送引导"] = "Firehose を送信",
                ["引导文件"] = "Firehose",
                ["云端引导"] = "クラウド",
                ["自动格式化"] = "自動フォーマット",
                ["EDL重启"] = "EDL 再起動",
                ["非链式"] = "非チェーン",
                ["分段文件："] = "分割ファイル:",
                ["分区表校验（实验性功能）："] = "GPT 検証 (ベータ):",
                ["分析"] = "解析",
                ["分析 vbmeta"] = "vbmeta を解析",
                ["高通"] = "Qualcomm",
                ["高通工具箱"] = "Qualcomm ツール",
                ["高通修复FastbootD关键分区"] = "Qualcomm FastbootD 修復",
                ["格式化LUN"] = "LUN フォーマット",
                ["根目录"] = "ルートディレクトリ",
                ["官方下载器"] = "公式ダウンローダー",
                ["管理器安装包"] = "マネージャー APK",
                ["管理器并安装"] = "マネージャーをインストール",
                ["过滤多余xml"] = "不要な XML を除外",
                ["合并散包super"] = "イメージから super を作成",
                ["合并OFP分段Super"] = "分割 OFP super を結合",
                ["恢复出厂设置"] = "初期化",
                ["恢复更新"] = "更新を再開",
                ["机型"] = "機種",
                ["加载位置："] = "読み込み元:",
                ["加载云端引导..."] = "クラウドイメージを読み込み中...",
                ["架构"] = "アーキテクチャ",
                ["建议线刷后"] = "書き込み後に推奨",
                ["降级包保存目录"] = "ダウンロード先",
                ["降级助手 APK"] = "ダウングレード支援 APK",
                ["解包"] = "展開",
                ["解包后的文件夹"] = "展開先フォルダ",
                ["解包OFP"] = "OFP を展開",
                ["解包OPS"] = "OPS を展開",
                ["解决安卓15无法安装应用"] = "Android 15 のアプリ導入問題を修復",
                ["解锁状态"] = "ロック状態",
                ["仅在开机模式下可用（需ROOT）"] = "起動中のみ（ROOT 必須）",
                ["仅FBD"] = "FBD のみ",
                ["镜像"] = "イメージ",
                ["镜像目录："] = "イメージフォルダ:",
                ["开机状态下读取（需ROOT）"] = "起動中に読み取り（ROOT 必須）",
                ["开启"] = "有効",
                ["开始隐藏"] = "隠す",
                ["可用降级版本"] = "利用可能なダウングレード版",
                ["控制与恢复"] = "制御と復元",
                ["块"] = "ブロック",
                ["快捷安装"] = "クイックインストール",
                ["快捷操作"] = "クイック操作",
                ["类型："] = "種類:",
                ["联发科"] = "MediaTek",
                ["联发科修复FastbootD关键分区"] = "MediaTek FastbootD 修復",
                ["联想"] = "Lenovo",
                ["链式"] = "チェーン",
                ["路径"] = "パス",
                ["魅族"] = "Meizu",
                ["密钥认证"] = "鍵認証",
                ["鸣谢"] = "クレジット",
                ["鸣谢以下成员对代码的贡献"] = "コード貢献者",
                ["模块安装"] = "モジュールのインストール",
                ["模式"] = "モード",
                ["目标分区"] = "対象パーティション",
                ["欧加通用AK3"] = "OPlus AK3",
                ["欧加真"] = "OPlus / Realme",
                ["排除绑定指纹和基带的关键分区"] = "指紋とモデムのパーティションを除外します",
                ["排除敏感文件"] = "機密ファイルを除外",
                ["排除无关多余"] = "不要なファイルを除外",
                ["品牌"] = "ブランド",
                ["平台:"] = "プラットフォーム:",
                ["启动 NDM 下载器"] = "NDM ダウンローダーを起動",
                ["启动NEKO下载器"] = "NEKO ダウンローダーを起動",
                ["起始扇区"] = "開始セクター",
                ["前后端开发 & 核心架构"] = "開発 & アーキテクチャ",
                ["强开基带调试端口"] = "Diag ポート",
                ["强开OEM"] = "OEM を有効化",
                ["AB通刷"] = "A/B 両スロット",
                ["强力线刷"] = "強制書き込み",
                ["强制 rootfs"] = "rootfs を強制",
                ["切换"] = "切替",
                ["切换A槽"] = "スロット A に切替",
                ["清理残留文件"] = "残存ファイルを削除",
                ["请选择快捷提取方案 ↓"] = "抽出方法を選択 ↓",
                ["请选择一个解压好的刷机包文件..."] = "展開済みのファームウェアを選択...",
                ["请选择隐藏环境资源包7z文件"] = "環境リソースの 7z ファイルを選択",
                ["区域"] = "地域",
                ["取消"] = "キャンセル",
                ["全量包"] = "フルパッケージ",
                ["全量包模式"] = "フル ROM",
                ["软件名称"] = "ソフト名",
                ["散包路径："] = "イメージフォルダ:",
                ["删除"] = "削除",
                ["设备"] = "端末",
                ["设备报告"] = "デバイスレポート",
                ["设备代号"] = "コードネーム",
                ["设备名称"] = "端末名",
                ["设备序列号"] = "端末シリアル番号",
                ["生成 OCDT"] = "OCDT を生成",
                ["生成TXT线刷脚本"] = "TXT フラッシュスクリプトを生成",
                ["实用软件"] = "便利なソフト",
                ["使用"] = "使用",
                ["使用建议"] = "推奨事項",
                ["使用新链接继续"] = "新しいリンクで続行",
                ["手动选择模块文件"] = "モジュールを選択",
                ["售后包"] = "サービスパッケージ",
                ["售后包模式"] = "サービス ROM",
                ["刷机驱动"] = "USB ドライバー",
                ["刷写模式"] = "フラッシュモード",
                ["刷写模式："] = "フラッシュモード:",
                ["刷写设置"] = "フラッシュ設定",
                ["刷新目录"] = "フォルダを更新",
                ["双击选择存放路径"] = "ダブルクリックで選択",
                ["搜索分区名称"] = "パーティション検索",
                ["锁定BL"] = "ロック",
                ["特别鸣谢开源项目（名称按A~Z排序）"] = "OSS クレジット (A-Z)",
                ["提取分区"] = "パーティション抽出",
                ["提取或写入"] = "抽出または書き込み",
                ["添加救砖模块"] = "復旧モジュールを追加",
                ["停止当前页面正在执行的操作"] = "停止",
                ["停止检测设备"] = "検出を停止",
                ["推送卡刷包"] = "OTA パッケージを転送",
                ["脱机修补 & 全自动ROOT"] = "オフラインパッチ & 自動 ROOT",
                ["网络调试端口："] = "ワイヤレスデバッグポート:",
                ["微信"] = "WeChat",
                ["文件"] = "ファイル",
                ["文件存放"] = "ファイル保存先",
                ["文件大小"] = "ファイルサイズ",
                ["文件选择"] = "ファイル選択",
                ["系列"] = "シリーズ",
                ["系统"] = "システム",
                ["下载错误"] = "ダウンロードエラー",
                ["下载配置"] = "ダウンロード設定",
                ["下载隐藏环境资源包V7.0"] = "環境リソース V7.0 を取得",
                ["显示"] = "表示",
                ["线程数"] = "スレッド数",
                ["线刷"] = "Fastboot フラッシュ",
                ["相册目录"] = "写真フォルダ",
                ["项目号"] = "プロジェクト ID",
                ["项目号填入到此处点击生成."] = "プロジェクト ID を入力して生成をクリックしてください。",
                ["写入GPT"] = "GPT を書き込み",
                ["新版本AOSP签名"] = "新しい AOSP 署名",
                ["新的动态链接"] = "新しい動的リンク",
                ["新建文件"] = "新規ファイル",
                ["新建文件夹"] = "新規フォルダ",
                ["信息"] = "情報",
                ["修补"] = "パッチ",
                ["修补 vbmeta 标志"] = "vbmeta フラグをパッチ",
                ["修补方案"] = "パッチ方式",
                ["修补模式"] = "パッチモード",
                ["修补日志"] = "パッチログ",
                ["修复FastbootD"] = "FBD 修復",
                ["修复Super真死"] = "Super 修復",

                // Runtime status strings produced by code rather than by the XAML.
                ["等待设备连接..."] = "デバイス接続を待機中...",
                ["等待设备连接稳定..."] = "デバイス接続の安定を待機中...",
                ["检测失败"] = "検出に失敗しました",

                [" 📷\n图片"] = " 📷\n写真",
                [" 🎬\n视频"] = " 🎬\n動画",
                ["  📞\n通讯录"] = "  📞\n連絡先",
                ["哔哩哔哩：泡菜学不会"] = "bilibili：泡菜学不会",
                ["酷安：泡菜学不会"] = "Coolapk：泡菜学不会",
                ["抖音：泡菜学不会"] = "Douyin：泡菜学不会",
                // Remaining captions, instructions and safety messages in MainWindow.xaml.
                ["USB3.0补丁"] = "USB 3.0 パッチ",
                ["以管理员权限运行 USB 3.0 兼容性补丁"] = "管理者権限で USB 3.0 互換性パッチを実行",
                ["一加全自动ROOT模式"] = "OnePlus 自動 Root モード",
                ["自动识别KMI"] = "KMI を自動検出",
                ["自动识别KMI并选择"] = "KMI を自動検出して選択",
                ["自动刷入并安装APK"] = "自動フラッシュと APK インストール",
                ["保留 AVB2.0 / dm-verity"] = "AVB 2.0 / dm-verity を維持",
                ["使用本功能将消耗服务器流量"] = "サーバー通信量の目安：",
                ["≈10MB/次"] = "1 回あたり約 10 MB",
                ["，您当前已消耗"] = "、使用済み通信量：",
                ["0MB流量."] = "0 MB",
                ["制作"] = "作成",
                ["boot 路径"] = "boot のパス",
                ["ak3 路径"] = "AK3 のパス",
                ["AVB 签名"] = "AVB 署名",
                ["不知道自己当前版本使用新旧方案的先分析一下"] = "新旧どちらの方式か不明な場合は、先に解析してください",
                ["勾选后点击开始刷入将等待60秒检测Fastboot设备"] = "有効にすると、フラッシュ開始時に Fastboot 端末の接続を最大 60 秒待機します",
                ["前两个为解锁指令 后两个为回锁指令"] = "最初の 2 つはアンロック、後の 2 つは再ロックのコマンドです",
                ["【需ROOT】解决投屏报错adb push"] = "【Root 必須】ミラーリング時の adb push エラーを修復",
                ["需ROOT"] = "Root 必須",
                ["需要ROOT"] = "Root 必須",
                ["用于分析OCDT中的项目号是否为空"] = "OCDT のプロジェクト ID が空かどうかを解析します",
                ["flash_all*.bat 或 rawprogram*.xml"] = "flash_all*.bat または rawprogram*.xml",
                ["回读完成后，生成 rawprogram*.xml文件"] = "読み戻し後に rawprogram*.xml を生成",
                ["已选 0/0 · 0 B"] = "選択済み 0/0 · 0 B",
                ["该分区已受保护，当前禁止写入"] = "このパーティションは保護されているため、書き込みできません",
                ["对小米线刷BAT，或Fastboot模式下选择的RawProgram生效"] = "Xiaomi のフラッシュ用 BAT または Fastboot モードで選択した RawProgram に適用",
                ["仅对已选择的小米线刷 BAT 脚本生效"] = "選択した Xiaomi のフラッシュ用 BAT スクリプトにのみ適用",
                ["跳过 crclist 和 sparsecrclist 分区，仅对小米线刷 BAT 脚本生效"] = "crclist と sparsecrclist をスキップします。Xiaomi のフラッシュ用 BAT スクリプトにのみ適用",
                ["刷写 vbmeta 时添加 --disable-verity 和 --disable-verification"] = "vbmeta のフラッシュ時に --disable-verity と --disable-verification を追加",
                ["Fastboot模式下读取分区表"] = "Fastboot モードでパーティション表を読み取る",
                ["隐藏ROOT"] = "Root を非表示にする",
                ["资料目录："] = "リソースフォルダ：",
                ["隐藏方案"] = "Root 非表示の方式",
                ["Magisk Alpha方案  支持版本：28101~28104"] = "Magisk Alpha：対応バージョン 28101～28104",
                ["Magisk Alpha方案  支持版本：29000~最新"] = "Magisk Alpha：対応バージョン 29000 以降",
                ["SUkiSU LKM方案  支持版本：≥4.1 （KernelSU适用≥3.2）【新】"] = "SukiSU LKM：4.1 以降（KernelSU は 3.2 以降）【新】",
                ["SUkiSU GKI/Built-in方案  支持版本：≥3.18"] = "SukiSU GKI/Built-in：3.18 以降",
                ["本功能建议线刷后搭配KernelSU使用，本界面特别鸣谢@王听 @月饼 开发"] = "フラッシュ後に KernelSU と併用することを推奨します。この画面の開発協力：@王听 @月饼",
                ["特别"] = "特別",
                ["应用检测"] = "アプリ検出",
                ["拖动一个或多个模块到窗口，自动识别"] = "モジュールをウィンドウにドラッグすると自動認識します",
                ["支持自动过滤模块文件，"] = "モジュールファイルを自動で判別できます。",
                ["VioletDownload 多线程下载器"] = "VioletDownload マルチスレッドダウンローダー",
                ["适用于普通文件多线程下载、欧加真普通刷机包链接以及 ColorOS16 动态链接下载，支持断点续传和新旧链接无缝衔接。"] = "通常のファイル、OPPO / OnePlus / realme のファームウェア、ColorOS 16 の動的リンクに対応。中断したダウンロードの再開やリンクの差し替えが可能です。",
                ["下载前请确保保存目录有足够的磁盘空间。若空间不足导致下载错误，请清理磁盘后继续下载。"] = "ダウンロード前に保存先の空き容量を確認してください。容量不足で失敗した場合は、空き容量を確保してから再開できます。",
                ["普通下载建议先用 8 或 16 线程；如果链接不稳定，可先降低线程数后再尝试继续下载。"] = "通常は 8 または 16 スレッドを推奨します。接続が不安定な場合はスレッド数を減らして再開してください。",
                ["暂停"] = "一時停止",
                ["自定义请求头（可选）"] = "カスタムリクエストヘッダー（任意）",
                ["每行一个请求头，例如：Authorization: Bearer xxx 或 Cookie: a=b"] = "1 行に 1 つのヘッダーを入力（例：Authorization: Bearer xxx、Cookie: a=b）",
                ["当前路径，点击复制"] = "現在のパス（クリックでコピー）",
                ["重命名"] = "名前を変更",
                ["正在读取目录..."] = "フォルダを読み込み中...",
                ["无需打开文件传输模式，仅充电模式下可进行正常文件传输，支持直接从电脑端批量拖动文件到窗口传输文件，加载根目录文件需要提前在设备上授予SHELL ROOT权限。"] = "USB の充電モードでもファイルを転送できます。PC から複数のファイルをドラッグして転送可能です。ルートディレクトリを読み込むには、端末で SHELL に Root 権限を付与してください。",
                ["ROOT管理器"] = "Root マネージャー",
                ["用户上传"] = "ユーザー提供",
                ["4系内核AK3"] = "4.x カーネル用 AK3",
                ["NDM下载器"] = "NDM ダウンローダー",
                ["一加专用AK3"] = "OnePlus 用 AK3",
                ["您可以双击打开文件夹目录或双击运行程序，支持访问官网"] = "ダブルクリックでフォルダを開くかプログラムを実行できます。公式サイトにもアクセスできます",
                ["来上传您的自己的文件到服务器."] = "からファイルをサーバーにアップロードできます。",
                ["支持本地/云端全量包_欧加散包_无需解压直接提取"] = "ローカル／クラウドのフルパッケージや OPLUS の個別イメージに対応。事前展開せずに抽出できます",
                ["请选择解包后文件的保存路径文件夹..."] = "展開したファイルの保存先を選択...",
                ["Rom获取"] = "ROM 取得",
                ["C16动态解析"] = "C16 動的リンク解析",
                ["预设机型："] = "機種プリセット：",
                ["请输入 8 位 SN 或 10 位 MTM"] = "8 桁の SN または 10 桁の MTM を入力",
                ["支持 8 位 SN、10 位 MTM，或直接选择预设机型自动输出刷机包。"] = "8 桁の SN、10 桁の MTM、または機種プリセットからファームウェアを検索できます。",
                ["BUG反馈"] = "不具合を報告",
                ["该分区已受基带指纹保护，当前禁止写入"] = "モデム／指紋関連パーティションの保護が有効なため、書き込みできません",
                ["操作正在进行中，请勿断开数据线..."] = "処理中です。USB ケーブルを抜かないでください...",
                ["重启到系统"] = "システムを再起動",
                ["重启到EDL"] = "EDL に再起動",
                ["重启到Recovery"] = "Recovery に再起動",
                ["重启到FastbootD"] = "FastbootD に再起動",
                ["（可选，查询前自动安装）"] = "（任意、検索前に自動インストール）",
                ["若查询不到降级包，请先将设备联网并手动检查系统更新，然后重新查询。"] = "パッケージが見つからない場合は、端末をインターネットに接続してシステム更新を手動確認し、再検索してください。",
                ["必须下载安装到需降级的设备"] = "ダウングレードする端末にダウンロードしてインストールしてください",
                ["单击任意一行选择版本并解析云端信息，右侧可复制原始下载链接。"] = "行をクリックするとバージョンを選択し、クラウド情報を取得します。右側から元のダウンロードリンクをコピーできます。",
                ["ColorOS 版本"] = "ColorOS バージョン",
                ["OTA 版本名"] = "OTA バージョン名",
                ["本功能仅适用于已经熔断的 OPPO 和一加设备进行大安卓版本降级；未熔断设备使用可能导致不可逆熔断。"] = "この機能は、ARB のヒューズがすでに切られた OPPO / OnePlus 端末で Android のメジャーバージョンを下げる場合にのみ使用してください。未処理の端末では、不可逆なヒューズ切断が発生する可能性があります。",
                ["降级会清空数据，请提前备份重要数据。本界面下载的刷机包可用于官方 OTA 安装或 Fastboot 卡刷转线刷模式安装。"] = "ダウングレードするとデータが消去されます。事前にバックアップしてください。ここで取得したパッケージは、公式 OTA または OTA から Fastboot への変換モードでインストールできます。",
                ["本功能适用于 OPPO 和一加设备，不适用于真我设备。特别鸣谢：酷安 @皓皓的小月。"] = "OPPO / OnePlus 端末向けの機能です。realme には対応していません。開発協力：酷安 @皓皓的小月。",
                ["准备就绪"] = "準備完了",
                ["正在检测设备..."] = "端末を検出中...",
                ["该项目由哔哩哔哩UP主”泡菜学不会“开发，开发此项目希望能为安卓玩机生态贡献自己的一份力量。"] = "bilibili の「泡菜学不会」が開発しています。Android 端末のカスタマイズを楽しむコミュニティへの貢献を目指しています。",
                ["支付宝"] = "Alipay",
                ["如果您喜欢本程序，欢迎赞赏支持，金额不限。您的支持是本程序持续免费维护和改进的动力！"] = "このアプリを気に入っていただけたら、ご支援をお願いします。金額は問いません。皆さまの支援が、無料での保守と改善を続ける力になります。",
                ["虚位以待..."] = "参加者募集中...",
                ["感谢以下用户的慷慨捐赠，你们的支持是工具持续免费维护的动力！"] = "ご寄付いただいた皆さまに感謝します。皆さまの支援が無料での保守を支えています！",
                ["以及众多未留名的爱心网友，致以最诚挚的感谢！"] = "お名前を公表されていない支援者の皆さまにも、心より感謝します！",
                ["应用列表"] = "アプリ一覧",
                ["仅"] = "のみ",
                ["用户应用"] = "ユーザーアプリ",
                ["支持多选应用后进行卸载、冻结、提取 APK 等批量操作。"] = "複数のアプリを選択して、アンインストール、凍結、APK 抽出などを一括実行できます。",
                ["目前仅支持小米"] = "現在は Xiaomi のみ対応",
                ["OFP文件："] = "OFP ファイル：",
                ["OPS文件："] = "OPS ファイル：",
                ["未解包请先切换到欧加线刷区域解包"] = "未展開の場合は、先に OPPO / OnePlus フラッシュ画面で展開してください",
                ["的分区："] = "のパーティション：",
                ["OCDT生成"] = "OCDT 生成",
                ["例如：23803"] = "例：23803",
                ["变体:"] = "バリアント:",
                ["选择对应的处理器和项目号后点击生成。项目号可在解压后的售后线刷包 version_info.txt 中查看 project= 字段。"] = "プロセッサーとプロジェクト ID を選び、「生成」をクリックしてください。ID は展開済みサービスパッケージの version_info.txt 内の project= で確認できます。",
                ["分区表校验仅适用于全量包模式；通过比对刷机包中的部分底层分区与设备分区表内容，判断当前刷机包是否与设备匹配，预防刷错包导致黑砖"] = "パーティション表の検証はフルパッケージモード専用です。一部の低レベルパーティションを端末の表と照合し、不適合なパッケージによる起動不能を防ぎます。",
                ["全量包链接 or Payload.bin路径..."] = "フルパッケージの URL または payload.bin のパス...",
                ["云解包方案-从云端提取线刷文件"] = "クラウド展開：フラッシュ用ファイルを抽出",
                ["请选择Payload.bin文件或全量包Zip文件..."] = "payload.bin またはフルパッケージの ZIP を選択...",
                ["请选择解包好的文件夹或解包输出路径..."] = "展開済みフォルダまたは展開先を選択...",
                ["勾选此项将同时刷入A和B两个分区,并且可以安全的切换设备槽位"] = "A/B 両スロットにフラッシュして、安全にスロットを切り替えられるようにします",
                ["部分大分区刷不进去时可选择此模式修复super，默认刷A槽位"] = "大きなパーティションを書き込めない場合に super を修復します。既定ではスロット A にフラッシュします",
                ["没有Fastboot的机型勾选这个，天玑处理器设备请勿勾选"] = "Fastboot に対応しない機種で有効にしてください。Dimensity 搭載端末では有効にしないでください",
                ["ARB熔断检测"] = "ARB ヒューズ状態を確認",
                ["选择一个散包文件夹或将散包拖动到此自动选择..."] = "個別イメージのフォルダを選択するか、ここにドラッグ...",
                ["FBD模式"] = "FBD モード",
                ["FB模式"] = "FB モード",
                ["自动救砖模式"] = "自動復旧モード",
                ["解包Payload仅在全量包模式下可用"] = "Payload 展開はフルパッケージモード専用です",
                ["切换至 English"] = "English に切り替え",
                ["Y700二代"] = "Y700 第 2 世代",
                ["Y700三代"] = "Y700 第 3 世代",
                ["Y700四代"] = "Y700 第 4 世代",
                ["Y700五代"] = "Y700 第 5 世代",
                ["选择 boot 镜像"] = "boot イメージを選択",
                ["选择 boot/init_boot 镜像"] = "boot/init_boot イメージを選択",
                ["选择 vbmeta 镜像"] = "vbmeta イメージを選択",
                ["选择 recovery 镜像"] = "recovery イメージを選択",
                ["选择 vbmeta_system 镜像"] = "vbmeta_system イメージを選択",
                ["温馨提示：全自动ROOT功能和脱机修补功能所使用的KMI补丁文件均来自ROOT管理器官方，修补方式也和官方一致，ROOT有风险，由于部分机子版本原因，若使用过程中出现无限重启卡第一屏的情况，请长按电源和音量下键尝试进入Fastboot模式，并刷入与当前设备系统版本一致的boot.img或init_boot.img文件后即可正常开机，本程序只提供云端下载与批处理服务，正确使用时变砖概率趋近与0，一切后果与本程序无关，看不懂上面的话的请勿使用本功能！！！"] = "注意：自動 Root とオフラインパッチでは、Root マネージャー公式の KMI パッチと同じ方式を使用します。Root 化にはリスクがあります。再起動を繰り返す、または起動画面で止まる場合は、電源キーと音量下キーを長押しして Fastboot モードに入り、現在のシステムと同じバージョンの boot.img または init_boot.img をフラッシュしてください。本アプリはクラウドからの取得と一括処理を提供します。自己責任で使用し、手順を理解できない場合は実行しないでください。"
            };

        private readonly Dictionary<(DependencyObject Owner, DependencyProperty Property), object?>
            _localizedOriginalValues = new();

        private UiLanguage _currentUiLanguage = UiLanguage.SimplifiedChinese;

        // Keep local values (including UnsetValue) so Chinese resumes its original styles.
        private readonly Dictionary<(DependencyObject Owner, DependencyProperty Property), object>
            _translatedLayoutOriginalValues = new();

        private static readonly DataTemplate CompactTranslatedButtonTemplate = (DataTemplate)XamlReader.Parse(
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

        // Keep command/API identifiers separate from their translated ComboBox captions.
        private static readonly DependencyProperty LocalizedContentSourceProperty =
            DependencyProperty.RegisterAttached("LocalizedContentSource", typeof(string), typeof(MainWindow));

        private static string? GetRawLocalizedContent(ContentControl? control)
        {
            string? current = control?.Content?.ToString();
            if (control?.GetValue(LocalizedContentSourceProperty) is string raw &&
                ((EnglishUiTexts.TryGetValue(raw, out string? english) && current == english) ||
                 (JapaneseUiTexts.TryGetValue(raw, out string? japanese) && current == japanese)))
            {
                return raw;
            }
            return current;
        }

        private static string LanguagePreferencePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VioletTool",
                "ui-language.txt");

        // Maps a UI language to the culture used for culture-sensitive formatting.
        private static string CultureNameFor(UiLanguage language) => language switch
        {
            UiLanguage.English => "en-US",
            UiLanguage.Japanese => "ja-JP",
            _ => "zh-CN"
        };

        // Maps a culture name back to a UI language; used when reading the saved preference.
        private static bool TryParseCultureName(string cultureName, out UiLanguage language)
        {
            switch (cultureName.Trim().ToLowerInvariant())
            {
                case "zh":
                case "zh-cn":
                    language = UiLanguage.SimplifiedChinese;
                    return true;
                case "en":
                case "en-us":
                    language = UiLanguage.English;
                    return true;
                case "ja":
                case "ja-jp":
                    language = UiLanguage.Japanese;
                    return true;
                default:
                    language = UiLanguage.SimplifiedChinese;
                    return false;
            }
        }

        // Returns the translation table for the language, or null when the Chinese source
        // strings are already the text that should be displayed.
        private static IReadOnlyDictionary<string, string>? TextsFor(UiLanguage language) =>
            language switch
            {
                UiLanguage.English => EnglishUiTexts,
                UiLanguage.Japanese => JapaneseUiTexts,
                _ => null
            };

        // Chinese is the source language: the XAML and code already contain those strings,
        // so the UI is restored to its original values instead of being translated.
        private bool NeedsTranslation => _currentUiLanguage != UiLanguage.SimplifiedChinese;

        // The window is authored with Microsoft YaHei UI, a Simplified Chinese face. Under it
        // Japanese kana fall back to whatever font Windows substitutes, which looks wrong to a
        // Japanese reader, so the Japanese UI is switched to a Japanese face that still covers the
        // Chinese characters used for device names and other pass-through text.
        //
        // Note: the sidebar column width deliberately stays at its authored 130px. Widening it
        // for longer translated labels shifts and narrows the whole content area (the right-hand
        // cards lose ~26px and the inner columns ~46px), which visibly unsettles the layout.
        private static string? UiFontFamilyFor(UiLanguage language) => language switch
        {
            UiLanguage.Japanese => "Yu Gothic UI, Microsoft YaHei UI",
            _ => null
        };

        // The original font family of every element this class restyles, so Chinese can be
        // restored exactly as authored.
        private readonly Dictionary<DependencyObject, object> _translatedFontOriginalValues = new();

        // Cycles 简体中文 -> English -> 日本語 -> 简体中文.
        private static UiLanguage NextLanguage(UiLanguage current) => current switch
        {
            UiLanguage.SimplifiedChinese => UiLanguage.English,
            UiLanguage.English => UiLanguage.Japanese,
            _ => UiLanguage.SimplifiedChinese
        };

        private void InitializeLanguageUi()
        {
            _currentUiLanguage = LoadLanguagePreference();
            ApplyUiLanguage(_currentUiLanguage, persist: false);
            ContentRendered += (_, _) =>
            {
                if (NeedsTranslation)
                {
                    TranslateUiTree(this);
                    RefreshDynamicLocalizedUi();
                    ApplyTranslatedLayout(this);
                }
            };
        }

        private void LanguageSwitchButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyUiLanguage(NextLanguage(_currentUiLanguage), persist: true);
        }

        private void ApplyUiLanguage(UiLanguage language, bool persist)
        {
            RestoreLocalizedValues();

            _currentUiLanguage = language;
            string cultureName = CultureNameFor(language);
            CultureInfo uiCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
            Thread.CurrentThread.CurrentUICulture = uiCulture;
            Language = XmlLanguage.GetLanguage(cultureName);

            if (NeedsTranslation)
            {
                TranslateUiTree(this);
            }

            RefreshDynamicLocalizedUi();
            UpdateLanguageSwitchButton();

            if (NeedsTranslation)
            {
                ApplyTranslatedLayout(this);
            }

            if (persist)
            {
                SaveLanguagePreference(language);
            }
        }

        // Applies the font adjustment that translated texts need. Runs after the tree walk so
        // that every element has already been visited.
        private void ApplyTranslatedLayout(DependencyObject root)
        {
            ApplyUiFont(root);
        }

        private void ApplyUiFont(DependencyObject root)
        {
            string? fontFamily = UiFontFamilyFor(_currentUiLanguage);
            if (fontFamily == null)
            {
                return;
            }

            var font = new System.Windows.Media.FontFamily(fontFamily);
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

                if (current is TextBlock or System.Windows.Controls.TextBox or Run
                    or System.Windows.Controls.Control &&
                    !BindingOperations.IsDataBound(current, TextElement.FontFamilyProperty))
                {
                    if (!_translatedFontOriginalValues.ContainsKey(current))
                    {
                        _translatedFontOriginalValues.Add(current, current.ReadLocalValue(TextElement.FontFamilyProperty));
                    }

                    current.SetCurrentValue(TextElement.FontFamilyProperty, font);
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

        private void TranslateUiTree(DependencyObject root)
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

                TranslateObject(current);

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

        private void TranslateObject(DependencyObject target)
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

            IReadOnlyDictionary<string, string>? texts = TextsFor(_currentUiLanguage);
            if (texts == null)
            {
                return;
            }

            object originalValue = owner.GetValue(property);
            if (originalValue is not string sourceText ||
                !texts.TryGetValue(sourceText, out string? translatedText))
            {
                return;
            }

            var key = (owner, property);
            if (!_localizedOriginalValues.ContainsKey(key))
            {
                _localizedOriginalValues.Add(key, originalValue);
            }

            if (property == ContentControl.ContentProperty)
            {
                owner.SetValue(LocalizedContentSourceProperty, sourceText);
            }
            owner.SetCurrentValue(property, translatedText);

            // Latin and Japanese labels can be wider than the Chinese originals they replace,
            // so shrink them to fit instead of letting them overflow their button or label.
            if (property == ContentControl.ContentProperty &&
                owner is System.Windows.Controls.Primitives.ButtonBase button &&
                button.ContentTemplate == null && button.ContentTemplateSelector == null &&
                !BindingOperations.IsDataBound(button, ContentControl.ContentTemplateProperty))
            {
                SetTranslatedLayoutValue(button, ContentControl.ContentTemplateProperty,
                    CompactTranslatedButtonTemplate);
                SetTranslatedLayoutValue(button, UIElement.ClipToBoundsProperty, true);
            }

            if (property == TextBlock.TextProperty && owner is TextBlock label &&
                label.TextWrapping == TextWrapping.NoWrap)
            {
                SetTranslatedLayoutValue(label, TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
                SetTranslatedLayoutValue(label, UIElement.ClipToBoundsProperty, true);
            }
        }

        private void SetTranslatedLayoutValue(DependencyObject owner, DependencyProperty property, object value)
        {
            if (BindingOperations.IsDataBound(owner, property)) return;
            var key = (owner, property);
            if (!_translatedLayoutOriginalValues.ContainsKey(key))
                _translatedLayoutOriginalValues.Add(key, owner.ReadLocalValue(property));
            owner.SetCurrentValue(property, value);
        }

        private string LocalizeUiText(string sourceText)
        {
            IReadOnlyDictionary<string, string>? texts = TextsFor(_currentUiLanguage);
            if (texts == null)
            {
                return sourceText;
            }

            if (texts.TryGetValue(sourceText, out string? translatedText))
            {
                return translatedText;
            }

            const string detectionFailurePrefix = "检测失败:";
            if (sourceText.StartsWith(detectionFailurePrefix, StringComparison.Ordinal))
            {
                return LocalizeUiText("检测失败") + sourceText[(detectionFailurePrefix.Length - 1)..];
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
            foreach (var item in _translatedFontOriginalValues)
            {
                if (item.Value == DependencyProperty.UnsetValue)
                    item.Key.ClearValue(TextElement.FontFamilyProperty);
                else
                    item.Key.SetCurrentValue(TextElement.FontFamilyProperty, item.Value);
            }
            _translatedFontOriginalValues.Clear();

            foreach (var item in _translatedLayoutOriginalValues)
            {
                if (item.Value == DependencyProperty.UnsetValue)
                    item.Key.Owner.ClearValue(item.Key.Property);
                else
                    item.Key.Owner.SetCurrentValue(item.Key.Property, item.Value);
            }
            _translatedLayoutOriginalValues.Clear();

            foreach (KeyValuePair<(DependencyObject Owner, DependencyProperty Property), object?> item
                     in _localizedOriginalValues)
            {
                if (item.Key.Property == ComboBox.TextProperty &&
                    item.Key.Owner is ComboBox { SelectedIndex: >= 0 })
                    continue;
                item.Key.Owner.SetCurrentValue(item.Key.Property, item.Value);
                if (item.Key.Property == ContentControl.ContentProperty)
                    item.Key.Owner.ClearValue(LocalizedContentSourceProperty);
            }

            _localizedOriginalValues.Clear();
        }

        private void UpdateLanguageSwitchButton()
        {
            // The label shows the language that is currently active, so it never reads as the
            // wrong language. The tooltip names the language the next click will switch to.
            (string label, string currentName, string nextName) = _currentUiLanguage switch
            {
                UiLanguage.SimplifiedChinese => ("中", "简体中文", "English"),
                UiLanguage.English => ("EN", "English", "日本語"),
                _ => ("日", "日本語", "简体中文")
            };

            LanguageSwitchLabel.Text = label;
            string description = _currentUiLanguage switch
            {
                UiLanguage.SimplifiedChinese => $"当前：{currentName}，点击切换至 {nextName}",
                UiLanguage.Japanese => $"現在の言語：{currentName}。クリックで {nextName} に切り替え",
                _ => $"Current: {currentName} — click for {nextName}"
            };
            LanguageSwitchButton.ToolTip = description;
            LanguageSwitchButton.SetValue(
                AutomationProperties.NameProperty,
                description);
        }

        private static UiLanguage LoadLanguagePreference()
        {
            try
            {
                if (File.Exists(LanguagePreferencePath))
                {
                    string savedValue = File.ReadAllText(LanguagePreferencePath).Trim();
                    if (TryParseCultureName(savedValue, out UiLanguage savedLanguage))
                    {
                        return savedLanguage;
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

                File.WriteAllText(LanguagePreferencePath, CultureNameFor(language));
            }
            catch
            {
                // The language still changes for this session if the preference cannot be saved.
            }
        }
    }
}
