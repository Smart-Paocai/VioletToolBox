**語言 / Languages：** [简体中文](README.md) · [繁體中文](README.zh-TW.md) · [English](README.en.md) · [日本語](README.ja.md) · [हिन्दी](README.hi.md) · [Tiếng Việt](README.vi.md) · [한국어](README.ko.md)

# 紫羅蘭工具箱（VioletToolBox）

<img src="logo2.ico" width="112" alt="VioletToolBox Logo">

面向 Android 裝置開發與維護的多功能玩機工具箱。發行程式名稱為 `VioletToolBox`，介面名稱為「紫羅蘭工具箱」。工具箱提供約 110 項功能，涵蓋多數品牌裝置的常用 ADB 與 Fastboot 工作流程。

[![Release](https://img.shields.io/github/v/tag/Smart-Paocai/VioletToolBox?label=Release&color=7c3aed)](../../releases) [![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D6)](#建置) [![.NET](https://img.shields.io/badge/.NET-8.0%2B-512BD4)](#建置) [![License](https://img.shields.io/github/license/Smart-Paocai/VioletToolBox?label=License)](LICENSE)

> [!WARNING]
> 刷機、解鎖、分割區讀寫、EDL 與 Root 功能可能造成資料遺失、裝置變磚或影響保固。請確認裝置型號、韌體、分割區與操作模式，事先備份重要資料，並自行承擔操作風險。

## 功能一覽

1. **首頁與裝置連線**：裝置狀態識別、快捷重啟、無線偵錯、槽位切換，以及 CMD / Windows 裝置管理員捷徑。
2. **投屏**：基於 scrcpy，支援虛擬按鍵、全自動投屏、視窗大小、幀率、清晰度與標題自訂。
3. **基本刷入**：`boot` / `init_boot` 等鏡像刷入、解鎖 / 回鎖、小米線刷、格式化、FRP 擦除、ADB 修復、DDR 讀取、基帶偵錯與 OCDT 分析。
4. **可視刷寫**：ADB / Fastboot 分割區表讀取與可視化讀、寫、擦；支援 GPT 回讀、`.bin`、XML 腳本、小米腳本與 Slot A 精簡刷寫。
5. **歐加線刷**：雲端 / 本地 URL 提取鏡像；全量包的普通、強力、AB、FastbootD 刷寫與修復；售後散包的 FB、FBD、全自動救磚。
6. **EDL 刷寫**：高通 9008 Sahara / Firehose 通訊、分割區讀寫擦、GPT、LUN、備份、OEM、槽位與軟體資訊操作。
7. **降級助手**：OPPO / 一加 OTA 降級、手動複製連結與版本資訊解析。
8. **模組專區**：PC 批次安裝模組、Root 管理器識別與自動隱藏 Root。
9. **斷點續傳**：多執行緒下載 OPLUS ColorOS 16 動態連結 OTA，支援換連結後續傳。
10. **檔案傳輸**：ADB 批次傳入 / 傳出、批次安裝 APK、內部儲存與 Root 根目錄存取。
11. **離線修補**：`boot` / `init_boot` 修補、一加全自動 Root、GKI、聯想 AVB 與 `vbmeta` 簽名分析。
12. **應用程式管理**：應用程式清單、凍結 / 解凍、提取 APK、凍結小米更新、清除資料與解除安裝。
13. **Android 通用工具**：OPLUS Super / OFP 分段合併、OFP / OPS 解包、刷機腳本、Payload 轉線刷、TWRP 分割區操作與 OCDT 生成。
14. **Payload**：可視化讀取本地或雲端 URL 的全量包 ZIP 並提取內容。
15. **備份助手**：備份圖片、影片與通訊錄。
16. **刷機資源**：快捷下載常用刷機檔案與資源。
17. **ROM 專區**：OPPO、OPLUS、realme、魅族、聯想、小米、Redmi 的 OTA、線刷包與 Boot 鏡像；API 對開發者開放，請勿濫用。

## 建置

需求：Windows 10/11、.NET 8 SDK 或更高版本。

```powershell
dotnet restore SmartTool.csproj
dotnet build SmartTool.csproj -c Debug
dotnet publish SmartTool.csproj -c Release -r win-x64 --self-contained false
```

## 下載與執行

請從 [Releases](../../releases) 下載完整發行包並解壓縮後執行 `VioletToolBox.exe`。請勿刪除 DLL、`exe` 目錄、`avbtool`、PEM 或其他執行相依檔案；原始碼發行時，需將 `VioletToolBox-win-x64.zip` 的相依檔案放入 Release 目錄。

## 授權條款

採用 GNU General Public License v3.0 或更高版本，詳見 [LICENSE](LICENSE)。
