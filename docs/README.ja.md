**言語 / Languages：** [简体中文](../README.md) · [繁體中文](README.zh-TW.md) · [English](README.en.md) · [日本語](README.ja.md) · [हिन्दी](README.hi.md) · [Tiếng Việt](README.vi.md) · [한국어](README.ko.md)

# 紫羅蘭ツールボックス（VioletToolBox）

<img src="../VioletToolBox/logo2.ico" width="112" alt="VioletToolBox Logo">

Android 端末の開発・保守・フラッシュ向け多機能ツールボックスです。配布プログラム名は `VioletToolBox`、UI 名は「紫罗兰工具箱」です。主要な ADB / Fastboot 作業を含む約 110 の機能を提供します。

[![Release](https://img.shields.io/github/v/tag/Smart-Paocai/VioletToolBox?label=Release&color=7c3aed)](https://github.com/Smart-Paocai/VioletToolBox/releases) [![Telegram](https://img.shields.io/badge/Telegram-%40violettoolbox-26A5E4?logo=telegram&logoColor=white)](https://t.me/violettoolbox) [![Website](https://img.shields.io/badge/Website-violettool.top-7C3AED?logo=googlechrome&logoColor=white)](https://violettool.top/) [![License](https://img.shields.io/github/license/Smart-Paocai/VioletToolBox?label=License)](../LICENSE)

> [!WARNING]
> フラッシュ、アンロック、パーティション操作、EDL、Root 関連機能は、データ消失、端末の起動不能、保証への影響を招く可能性があります。端末モデル、ファームウェア、パーティション、モードを確認し、必ずバックアップを取ったうえで自己責任で使用してください。

## 機能

1. **ホームと接続**：端末状態、再起動、ワイヤレスデバッグ、スロット切替、CMD / デバイスマネージャー。
2. **画面ミラーリング**：scrcpy ベース。仮想キー、自動ミラーリング、サイズ、FPS、画質、タイトルの設定。
3. **基本フラッシュ**：`boot` / `init_boot`、アンロック / 再ロック、Xiaomi フラッシュ、フォーマット、FRP、ADB 修復、DDR、ベースバンドデバッグ、OCDT。
4. **ビジュアルフラッシュ**：ADB / Fastboot のパーティション表、読み書き消去、GPT、`.bin`、XML、Xiaomi スクリプト、Slot A。
5. **OPPO / OnePlus フラッシュ**：クラウド URL 抽出、通常 / 強制 / AB / FastbootD、ARB、Super 修復、Payload、FB / FBD / 自動救済。
6. **EDL フラッシュ**：Qualcomm 9008 Sahara / Firehose、パーティション、GPT、LUN、バックアップ、OEM、スロット、ソフトウェア情報。
7. **ダウングレード支援**：OPPO / OnePlus OTA、リンクコピー、バージョン解析。
8. **モジュール**：PC からの一括導入、Root マネージャー検出、Root 非表示。
9. **レジュームダウンロード**：OPLUS ColorOS 16 OTA のマルチスレッド継続ダウンロード。
10. **ファイル転送**：ADB 一括転送、APK 導入、内部ストレージ、Root ディレクトリ。
11. **オフラインパッチ**：`boot` / `init_boot`、OnePlus Root、GKI、Lenovo AVB、`vbmeta` 分析。
12. **アプリ管理**：一覧、凍結 / 解除、APK 抽出、更新凍結、データ消去、アンインストール。
13. **Android 共通ツール**：OPLUS Super / OFP、OFP / OPS 展開、フラッシュスクリプト、Payload 変換、TWRP、OCDT。
14. **Payload**：ローカルまたは URL の ZIP 情報表示と抽出。
15. **バックアップ**：写真、動画、連絡先。
16. **フラッシュリソース**：よく使うファイルとリソースのダウンロード。
17. **ROM センター**：各社 OTA、Fastboot パッケージ、Boot イメージ。API は開発者に公開されています。適切に利用してください。

## ビルド

Windows 10/11 と .NET 8 SDK 以降が必要です。

```powershell
dotnet restore VioletToolBox/SmartTool.csproj
dotnet build VioletToolBox/SmartTool.csproj -c Debug
dotnet publish VioletToolBox/SmartTool.csproj -c Release -r win-x64 --self-contained false
```

## ダウンロードと実行

[Releases](../../releases) から完全なパッケージを展開して `VioletToolBox.exe` を実行してください。DLL、`exe`、`avbtool`、PEM などの依存ファイルは削除しないでください。ソースから発行した場合は `VioletToolBox-win-x64.zip` の依存ファイルを Release ディレクトリへ配置します。

## ライセンス

GNU General Public License v3.0 以降で提供されます。[LICENSE](../LICENSE) を参照してください。
