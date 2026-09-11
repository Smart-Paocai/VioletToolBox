# VioletToolBox (Purple Violet Toolbox)

A feature-rich Android device development and maintenance toolkit. The release binary is named `VioletToolBox`; its user interface is named “紫罗兰工具箱”. It provides about 110 functions for most Android brands, covering common ADB and Fastboot workflows.

**Languages:** [简体中文](README.md) · [繁體中文](README.zh-TW.md) · [English](README.en.md) · [日本語](README.ja.md) · [हिन्दी](README.hi.md) · [Tiếng Việt](README.vi.md) · [한국어](README.ko.md)

> [!WARNING]
> Flashing, unlocking, partition access, EDL operations, and Root-related features can erase data, brick a device, or affect warranty coverage. Verify the device model, firmware, partition, and mode; back up important data; and use the tool at your own risk.

## Features

### 1. Home and device connection

Device-state detection, quick reboot, wireless debugging, slot switching, and shortcuts to Command Prompt and Windows Device Manager.

### 2. Screen mirroring

Built on scrcpy, with virtual keys, automatic mirroring, adjustable window size / frame rate / quality, and custom window titles.

### 3. Basic flashing

Flash common images such as `boot` and `init_boot`; reboot; unlock or relock; use Xiaomi fastboot flashing; format devices; erase FRP; repair ADB; configure Xiaomi USB security; inspect OnePlus DDR; enable baseband debug ports; switch file-transfer mode; and analyze OPLUS OCDT.

### 4. Visual flashing

Read partition tables in ADB or Fastboot mode and visually read, write, or erase partitions. Supports partition / GPT backup, programmer-ready `.bin` files, `.img` or `.bin` selection, generated XML scripts, Xiaomi flash scripts, and Slot A simplified flashing.

### 5. OPPO / OnePlus flashing

Extract images from cloud or local URLs. Full-package mode supports normal / force / AB flashing, FastbootD-only flashing, FastbootD repair, ARB checks, Super repair, and Payload unpacking. After-sales-package mode supports FB, FBD, and automated unbrick workflows.

### 6. EDL flashing

For Qualcomm 9008 mode with Sahara / Firehose communication: partition-table read, partition read/write/erase, mode reboot, factory reset, GPT writing, LUN formatting, baseband-fingerprint and GPT backup, OEM enablement, slot management, and software-information readout.

### 7. Downgrade assistant

OPPO / OnePlus OTA downgrade support, manual package-link copying, and automatic package-version parsing.

### 8. Module center

Batch-install modules from a PC, detect Root managers, and automate Root hiding.

### 9. Resumable downloads

Multi-threaded download of dynamic-link OPLUS ColorOS 16 full OTA packages; replace an expired link and continue downloading.

### 10. File transfer

ADB batch file import/export, batch APK installation, internal-storage access, and Root-only device-root access.

### 11. Offline patching

Patch `boot` / `init_boot` without a Root manager; automated OnePlus Root; GKI image creation; Lenovo AVB signing; chained, non-chained, and newer Lenovo AOSP signatures; and `vbmeta` signature analysis.

### 12. App management

Read installed / frozen app lists; freeze, unfreeze, extract APKs, freeze Xiaomi system updates, clear app data, and uninstall apps.

### 13. General Android tools

Merge OPLUS split Super packages; unpack OFP / OPS; merge segmented OFP Super files; generate TXT flash scripts; convert Payload OTA packages for flashing; perform TWRP partition-table, read, write, and erase operations; and generate OPLUS OCDT files online.

### 14. Payload

Visually inspect and extract data from full-package ZIP files stored locally or at cloud URLs.

### 15. Backup assistant

Back up photos, videos, and contacts from a connected device.

### 16. Flashing resources

Quick access to frequently used flashing files and resources.

### 17. ROM center

Download or copy OTA ZIPs, fastboot packages, and Boot images for OPPO, OPLUS, realme, Meizu, Lenovo, Xiaomi, and Redmi devices. The ROM-center API is available to developers; please use it responsibly.

## Build

Requirements: Windows 10/11 and .NET 8 SDK or later.

```powershell
dotnet restore SmartTool.csproj
dotnet build SmartTool.csproj -c Debug
dotnet publish SmartTool.csproj -c Release -r win-x64 --self-contained false
```

## Download and run

Download and extract the complete package from [Releases](../../releases), then run `VioletToolBox.exe`. Do not remove bundled DLLs, the `exe` directory, `avbtool`, PEM files, or other runtime dependencies. To run a source-built release, place the runtime dependencies from `VioletToolBox-win-x64.zip` in the Release directory.

## License

Licensed under GNU General Public License v3.0 or later. See [LICENSE](LICENSE).
