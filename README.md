# CarArchive

车影归档是一款 Windows 桌面工具，用于整理汽车照片、识别车型信息，并按确认后的目录结构归档原图。

- 使用说明：[CarArchive/README.md](./CarArchive/README.md)
- Windows x64 一体化压缩包：解压后运行根目录的「启动车影归档」快捷方式。
- 发布版本：1.0

## 开发

需要 Windows、.NET 10 SDK 和 Windows App SDK / WinUI 工具链。

```powershell
dotnet run --project .\CarArchive.Tests\CarArchive.Tests.csproj
dotnet run --project .\CarArchive.PhotoTests\CarArchive.PhotoTests.csproj
.\scripts\Package-Windows.ps1
```

发布包位于 `artifacts\CarArchive-1.0-windows-x64.zip`。应用将数据库、缓存、日志和当前 Windows 用户加密的 Gemini API Key 保存在 `%LOCALAPPDATA%\CarArchive`，不会将这些数据打入发布包。
