**语言 / Languages：** [简体中文](README.md) · [繁體中文](docs/README.zh-TW.md) · [English](docs/README.en.md) · [日本語](docs/README.ja.md) · [हिन्दी](docs/README.hi.md) · [Tiếng Việt](docs/README.vi.md) · [한국어](docs/README.ko.md)

# 紫罗兰工具箱（VioletToolBox）

<img src="VioletToolBox/logo2.ico" width="112" alt="VioletToolBox Logo">

一款面向 Android 设备开发的多功能搞机工具箱，总功能约110项，是国内目前功能最多的免费搞机工具箱，支持大部分品牌的安卓设备,几乎涵盖了所有常用的ADB和Fastboot功能,搞机刷机学习，这一个工具箱就够了！

[![Release](https://img.shields.io/github/v/tag/Smart-Paocai/VioletToolBox?label=Release&color=7c3aed)](https://github.com/Smart-Paocai/VioletToolBox/releases) [![Telegram](https://img.shields.io/badge/Telegram-%40violettoolbox-26A5E4?logo=telegram&logoColor=white)](https://t.me/violettoolbox) [![Website](https://img.shields.io/badge/Website-violettool.top-7C3AED?logo=googlechrome&logoColor=white)](https://violettool.top/) [![License](https://img.shields.io/github/license/Smart-Paocai/VioletToolBox?label=License)](LICENSE)

## 功能一览

### 1. 主页与设备连接

- 识别设备状态，提供快捷重启、无线调试与槽位切换。
- 底部状态栏可快捷打开 CMD 命令行和 Windows 设备管理器。

### 2. 投屏

基于 scrcpy 的二次开发，支持：

- 虚拟按键操作与全自动投屏。
- 自由调整投屏窗口大小、帧率与清晰度。
- 自定义投屏窗口标题。

### 3. 基本刷入

- 常规镜像刷入：支持 `boot`、`init_boot` 等常用镜像，并可快捷重启。
- 解锁 / 回锁指令、小米官方线刷、设备格式化与 FRP 擦除。
- 修复 ADB 异常、强开小米 USB 安全设置。
- ADB 读取一加 DDR 版本、强开基带调试端口、切换文件传输模式。
- OPLUS OCDT 分析。

### 4. 可视刷写

- 在 ADB（开机）与 Fastboot 模式下读取设备分区表，并进行可视化读、写、擦操作。
- ADB 模式支持回读分区与 GPT，可生成供编程器刷写的 `.bin` 文件。
- 支持 `.img` 与 `.bin` 格式选择；回读后可自动生成 XML 刷写脚本。
- 支持小米线刷脚本的可视化刷写，以及 Slot A 精简模式刷写。

### 5. 欧加线刷

- 支持从云端或本地 URL 快捷提取镜像。
- **全量包模式**：普通线刷、强力线刷、AB 通刷、仅 FastbootD 模式刷写、修复 FastbootD、ARB 熔断检测、修复 Super 真死、Payload 解包。
- **售后包模式**：售后散包刷写，提供 FB 模式、FBD 模式与全自动救砖模式。

### 6. EDL 刷写

面向高通设备的 9008（EDL）模式，支持 Sahara / Firehose 通信与：

- 分区表读取、分区读 / 写 / 擦、重启到指定模式。
- 恢复出厂设置、写入 GPT、格式化指定 LUN、备份基带指纹与 GPT。
- 强开 OEM、槽位管理、读取软件信息等。

### 7. 降级助手

支持 OPPO / 一加设备 OTA 降级，兼顾官方工具效果与更高自由度：可手动复制降级包链接，并自动解析降级包版本信息。

### 8. 模块专区

- 批量从 PC 安装模块到移动设备。
- 自动识别 Root 管理器，并支持自动隐藏 Root。

### 9. 断点续传

支持多线程下载 OPLUS ColorOS 16 动态链接的 OTA 全量包。链接过期后可替换新链接继续下载。

### 10. 文件传输

- 通过 ADB 批量传入 / 传出文件。
- 批量从电脑安装 APK。
- 读取内部存储；读取设备根目录需要 Root 权限。

### 11. 脱机修补

- 不依赖 Root 管理器修补 `boot` / `init_boot`。
- OnePlus 设备全自动 Root。
- 制作 GKI 镜像、联想 AVB 签名。
- 支持非链式、链式与联想新版 AOSP 签名；可自动分析 `vbmeta`，识别设备所使用的签名类型。

### 12. 应用管理

- 读取应用列表、查看冻结应用、冻结 / 解冻指定应用。
- 提取设备中指定 APK 到 PC。
- 冻结小米系统更新、清除指定应用数据、卸载指定应用。

### 13. Android 通用工具

- 合并 OPLUS 散包 Super、解包 OFP / OPS 文件、合并 OFP 分段文件为完整 Super。
- 生成 TXT 线刷脚本，将 Android Payload 卡刷包转换为线刷模式。
- 在 TWRP Recovery 模式下读取分区表、回读 / 写入 / 擦除分区。
- 为 OPLUS 设备在线生成 OCDT 文件。

### 14. Payload

可视化读取本地文件或云端 URL 中全量包 ZIP 的信息，并按需提取内容。

### 15. 备份助手

在线备份设备中的图片、视频与通讯录。

### 16. 刷机资源

快捷下载常用刷机文件与资源。

### 17. ROM 专区

快捷下载或复制 OPPO、OPLUS、realme、魅族、联想、小米、Redmi 等设备的 OTA 卡刷包、线刷包与 Boot 镜像。

ROM 专区 API 面向开发者开放，请合理使用，勿滥用服务资源。

## 构建

开发环境：Windows 10/11，.NET 8 SDK 或更高版本。

```powershell
dotnet restore VioletToolBox/SmartTool.csproj
dotnet build VioletToolBox/SmartTool.csproj -c Debug
```

发布 x64 桌面版本：

```powershell
dotnet publish VioletToolBox/SmartTool.csproj -c Release -r win-x64 --self-contained false
```

## 下载与运行

请从 [Releases](../../releases) 下载完整发布包并解压后运行 `VioletToolBox.exe`。不要单独删除发布包内的 DLL、`exe`、`avbtool`、PEM 或其他运行依赖，源码需要将`VioletToolBox-win-x64.zip`依赖文件移动到Release目录才能正常使用.

## 许可证

本项目采用 GNU General Public License v3.0（GPL-3.0）或更高版本授权，详见 [LICENSE](LICENSE)。
