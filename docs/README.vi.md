**Ngôn ngữ / Languages:** [简体中文](../README.md) · [繁體中文](README.zh-TW.md) · [English](README.en.md) · [日本語](README.ja.md) · [हिन्दी](README.hi.md) · [Tiếng Việt](README.vi.md) · [한국어](README.ko.md)

# Hộp công cụ Violet（VioletToolBox）

<img src="../VioletToolBox/logo2.ico" width="112" alt="VioletToolBox Logo">

Bộ công cụ đa năng cho phát triển, bảo trì và flash thiết bị Android. Tên chương trình phát hành là `VioletToolBox`, tên giao diện là “紫罗兰工具箱”. Công cụ có khoảng 110 chức năng, bao gồm phần lớn quy trình ADB / Fastboot phổ biến.

[![Release](https://img.shields.io/github/v/tag/Smart-Paocai/VioletToolBox?label=Release&color=7c3aed)](https://github.com/Smart-Paocai/VioletToolBox/releases) [![Telegram](https://img.shields.io/badge/Telegram-%40violettoolbox-26A5E4?logo=telegram&logoColor=white)](https://t.me/violettoolbox) [![Website](https://img.shields.io/badge/Website-violettool.top-7C3AED?logo=googlechrome&logoColor=white)](https://violettool.top/) [![License](https://img.shields.io/github/license/Smart-Paocai/VioletToolBox?label=License)](../LICENSE)

> [!WARNING]
> Flash, mở khóa, đọc/ghi phân vùng, EDL và chức năng Root có thể làm mất dữ liệu, brick thiết bị hoặc ảnh hưởng bảo hành. Hãy kiểm tra đúng mẫu máy, firmware, phân vùng và chế độ; sao lưu dữ liệu; tự chịu trách nhiệm khi sử dụng.

## Chức năng

1. **Trang chủ và kết nối** — trạng thái thiết bị, khởi động lại, gỡ lỗi không dây, đổi slot, CMD/Device Manager.
2. **Trình chiếu màn hình** — dựa trên scrcpy: phím ảo, tự động chiếu, kích thước, FPS, chất lượng và tiêu đề.
3. **Flash cơ bản** — `boot`/`init_boot`, mở/khóa lại, Xiaomi flash, format, FRP, ADB, DDR, OCDT.
4. **Flash trực quan** — bảng phân vùng ADB/Fastboot, đọc/ghi/xóa, GPT, `.bin`, XML, script Xiaomi, Slot A.
5. **Flash OPPO/OnePlus** — trích xuất URL, chế độ thường/ép/AB/FastbootD, ARB, sửa Super, Payload, FB/FBD/tự cứu máy.
6. **Flash EDL** — Qualcomm 9008 Sahara/Firehose, phân vùng, GPT, LUN, sao lưu, OEM, slot và thông tin phần mềm.
7. **Hỗ trợ hạ cấp** — OPPO/OnePlus OTA, sao chép liên kết và phân tích phiên bản.
8. **Khu vực mô-đun** — cài mô-đun hàng loạt từ PC, nhận diện trình quản lý Root và ẩn Root.
9. **Tải tiếp tục** — tải đa luồng OTA OPLUS ColorOS 16, hỗ trợ tiếp tục với liên kết mới.
10. **Truyền tệp** — truyền ADB hàng loạt, cài APK, bộ nhớ trong và thư mục Root.
11. **Vá ngoại tuyến** — `boot`/`init_boot`, OnePlus Root, GKI, Lenovo AVB, phân tích `vbmeta`.
12. **Quản lý ứng dụng** — danh sách, đóng băng/mở băng, trích APK, xóa dữ liệu và gỡ cài đặt.
13. **Công cụ Android chung** — OPLUS Super/OFP, OFP/OPS, script flash, chuyển Payload, TWRP và OCDT.
14. **Payload** — xem và trích xuất ZIP cục bộ hoặc từ URL.
15. **Sao lưu** — ảnh, video và danh bạ.
16. **Tài nguyên flash** — tải nhanh tệp và tài nguyên thường dùng.
17. **ROM Center** — OTA, gói Fastboot và ảnh Boot; hãy sử dụng API cho nhà phát triển một cách có trách nhiệm.

## Xây dựng

Cần Windows 10/11 và .NET 8 SDK trở lên.

```powershell
dotnet restore VioletToolBox/SmartTool.csproj
dotnet build VioletToolBox/SmartTool.csproj -c Debug
dotnet publish VioletToolBox/SmartTool.csproj -c Release -r win-x64 --self-contained false
```

## Tải xuống và chạy

Tải gói hoàn chỉnh từ [Releases](../../releases), giải nén rồi chạy `VioletToolBox.exe`. Không xóa DLL, thư mục `exe`, `avbtool`, PEM hoặc phụ thuộc khác. Bản phát hành xây dựng từ mã nguồn cần phụ thuộc từ `VioletToolBox-win-x64.zip` trong thư mục Release.

## Giấy phép

Phát hành theo GNU General Public License v3.0 hoặc mới hơn. Xem [LICENSE](../LICENSE).
