# VioletToolBox

一款面向 Android 设备开发与维护的多功能工具箱。界面名称为“紫罗兰工具箱”。

## 功能范围

- ADB / Fastboot 设备操作与日志
- 高通 9008（Sahara / Firehose）相关工具
- ROM 下载、分区与动态 Super 镜像处理
- AVB、OFP / OPS 等固件辅助处理能力

## 构建

运行环境：Windows 10/11，.NET 8 SDK 或更高版本。

```powershell
dotnet build SmartTool.csproj -c Debug
```

发布 x64 桌面版本：

```powershell
dotnet publish SmartTool.csproj -c Release -r win-x64 --self-contained false
```

## 许可证

本项目采用 GNU General Public License v3.0（GPL-3.0）或更高版本授权。详见 [LICENSE](LICENSE)。
