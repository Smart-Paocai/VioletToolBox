# 바이올렛 툴박스（VioletToolBox）

Android 기기 개발·유지보수·플래싱을 위한 다기능 도구 상자입니다. 배포 프로그램 이름은 `VioletToolBox`, UI 이름은 “紫罗兰工具箱”이며, 주요 ADB / Fastboot 작업을 포함한 약 110개 기능을 제공합니다.

**언어 / Languages:** [简体中文](README.md) · [繁體中文](README.zh-TW.md) · [English](README.en.md) · [日本語](README.ja.md) · [हिन्दी](README.hi.md) · [Tiếng Việt](README.vi.md) · [한국어](README.ko.md)

> [!WARNING]
> 플래싱, 잠금 해제, 파티션 읽기/쓰기, EDL, Root 기능은 데이터 손실·벽돌 상태·보증 영향의 위험이 있습니다. 기기 모델, 펌웨어, 파티션, 모드를 확인하고 백업 후 본인 책임으로 사용하세요.

## 기능

1. **홈/연결** — 기기 상태, 빠른 재부팅, 무선 디버깅, 슬롯 전환, CMD/장치 관리자.
2. **화면 미러링** — scrcpy 기반 가상 키, 자동 미러링, 창 크기, FPS, 화질, 제목 설정.
3. **기본 플래싱** — `boot`/`init_boot`, 잠금 해제/재잠금, Xiaomi 플래싱, 포맷, FRP, ADB, DDR, OCDT.
4. **시각 플래싱** — ADB/Fastboot 파티션 표, 읽기/쓰기/지우기, GPT, `.bin`, XML, Xiaomi 스크립트, Slot A.
5. **OPPO/OnePlus 플래싱** — URL 추출, 일반/강제/AB/FastbootD, ARB, Super 복구, Payload, FB/FBD/자동 복구.
6. **EDL 플래싱** — Qualcomm 9008 Sahara/Firehose, 파티션, GPT, LUN, 백업, OEM, 슬롯, 소프트웨어 정보.
7. **다운그레이드** — OPPO/OnePlus OTA, 링크 복사, 버전 분석.
8. **모듈** — PC 일괄 설치, Root 관리자 감지, Root 숨김.
9. **이어받기 다운로드** — OPLUS ColorOS 16 OTA 멀티스레드 다운로드.
10. **파일 전송** — ADB 일괄 전송, APK 설치, 내부 저장소/Root 디렉터리.
11. **오프라인 패치** — `boot`/`init_boot`, OnePlus Root, GKI, Lenovo AVB, `vbmeta` 분석.
12. **앱 관리** — 목록, 동결/해제, APK 추출, 업데이트 동결, 데이터 삭제, 제거.
13. **Android 공용 도구** — OPLUS Super/OFP, OFP/OPS, 스크립트, Payload 변환, TWRP, OCDT.
14. **Payload** — 로컬 또는 URL ZIP 정보 확인 및 추출.
15. **백업** — 사진, 비디오, 연락처.
16. **플래싱 리소스** — 자주 쓰는 파일과 리소스 다운로드.
17. **ROM 센터** — OTA, Fastboot 패키지, Boot 이미지. 개발자 API를 책임 있게 사용하세요.

## 빌드

Windows 10/11 및 .NET 8 SDK 이상이 필요합니다.

```powershell
dotnet restore SmartTool.csproj
dotnet build SmartTool.csproj -c Debug
dotnet publish SmartTool.csproj -c Release -r win-x64 --self-contained false
```

## 다운로드 및 실행

[Releases](../../releases)에서 전체 패키지를 내려받아 압축을 풀고 `VioletToolBox.exe`를 실행하세요. DLL, `exe`, `avbtool`, PEM 등 의존 파일을 삭제하지 마세요. 소스 빌드 배포본은 `VioletToolBox-win-x64.zip`의 의존 파일을 Release 디렉터리에 넣어야 합니다.

## 라이선스

GNU General Public License v3.0 이상입니다. [LICENSE](LICENSE)를 참조하세요.
