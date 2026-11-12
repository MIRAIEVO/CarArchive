# CarArchive 1.1 · 车影归档

Windows 汽车照片分类工具：导入、调整分组、识别查证、人工确认后归档。采用 C#、.NET 10、WinUI 3、SQLite。

- [使用说明](CarArchive/README.md)
- [昨天与今天的升级记录](CHANGELOG.md)
- [作者与协作署名](AUTHORS.md)
- [下载 Windows x64 便携版](artifacts/CarArchive-1.1-windows-x64.zip)

完整解压 ZIP 后运行根目录 `CarArchive.exe`，保留 `Program`、`Data`、`Documentation`。发布包不含用户照片、数据库或 API Key。

## 功能

- 分阶段导入、共享图片缓存、按点击加载高清预览；完整比例缩略图。
- 分组独立编辑、两个 Gemini 任务及独立归档队列并行，安全拆分／合并／复制引用。
- 精简、速度、费用、准确四档；深入查证以具体车型特征对照照片，修正建议手动采用。
- 完整车型名称由本地指南和可审查的命名学习生成；现有车型与品牌目录模糊匹配。
- 每次归档人工确认，原图校验、中断恢复、共享原图及单组撤销。
- 各版本使用根目录 `Data`，首次启动可导入旧数据，旧数据保持不变。

## 开发

需要 Windows、.NET 10 SDK、Windows App SDK／WinUI C# 工具链和 Windows SDK。

```powershell
dotnet run --project .\CarArchive.Tests\CarArchive.Tests.csproj
dotnet run --project .\CarArchive.PhotoTests\CarArchive.PhotoTests.csproj
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package-Portable.ps1 -Version 1.1
```

发布包位于 `artifacts`。原生验证必须使用隔离的 `CARARCHIVE_DATA_DIR`，不要指向日常数据。

作者：**MIRAIEVO、Codex（OpenAI）、GitHub Copilot（Microsoft）**。
