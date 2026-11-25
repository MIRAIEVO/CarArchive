# CarArchive 1.4 · 车影归档

Windows 汽车照片分类工具：导入照片、调整分组、AI识别／网页辅助识别、复核命名后归档。采用 C#、.NET 10、WinUI 3、SQLite。

- [下载 CarArchive 1.4 Windows x64 便携版](https://github.com/MIRAIEVO/CarArchive/releases/download/v1.4/CarArchive-1.4-windows-x64.zip)
- [使用说明](CarArchive/README.md) · [升级记录](CHANGELOG.md) · [版本说明](docs/releases/README.md)
- [协作交接](docs/collaboration/HANDOFF.md) · [共同作者](AUTHORS.md)

完整解压 ZIP 后运行根目录 `CarArchive.exe`，保留 `Program`、`Data`、`Documentation`。包内不含用户照片、数据库或 API Key。

## 工作流程

1. 导入一组或按时间批量分组；小图在后台生成，高清图按点击加载。
2. 点击当前预览图，按需框选；统一切换单图或多图上传。复制框选图片并生成提示词，粘贴至 Gemini 网页人工验证。
3. 修改完整车型名称，或从现存车型／品牌目录的模糊建议中选择目标。
4. 确认并移动原图。文件经过校验，不静默覆盖，支持中断恢复及归档撤销。

组间任务独立，默认两个识别任务和独立文件队列并行。复制组会创建独立原图副本；移除记录默认保留照片，回收源照片是单独的操作，且不用于已归档照片。

## 1.3

Gemini、OpenAI、Claude 与 DeepSeek 均可用于 AI识别。七档强度支持设置默认值，OpenAI 高档采用流式接收。详见 [1.3 更新说明](docs/releases/release-notes-v1.3.md)。

## 1.2.4

紧凑居中的固定识别按钮、加深红色与变浅的悬停反馈，以及遵循系统设置的轻快交互动效。详见 [更新说明](docs/releases/release-notes-v1.2.4.md)。

## 1.2.3

设置实时保存、行内合并、当前组撤回移动、识别取消切换，以及右上角 JSON 窗口与粘贴即确定。详见 [体验版说明](docs/releases/release-notes-v1.2.3.md)。

## 1.2.2

二次导入修复、缩略图提速、全局上传开关、自动 JSON 回填、浮动合并、右键菜单及固定设置子页、首次配置限定导入。详见 [本版说明](docs/releases/release-notes-v1.2.2.md)。

## 1.2

- 各组独立保存识别强度和上传方式，默认精简字段。单张使用当前照片；多张仅上传所有框选区域，无框选回退当前整图。
- 查询期间允许复制组、拆出未上传图片；合并保留靠前组的信息。归档与浏览并行，手动切组后不再自动跳转。
- 分组／组内编辑撤回与重做，独立副本与回收照片的恢复；撤销归档仅删除本次创建的空目录。
- 导入直接索引原文件，后台优先缩略图；DNG 使用 Windows RAW 解码器，视频支持播放、拖动及当前帧识别，原视频归档。滚轮缩放不改变上传裁剪范围。
- 删除已归档记录后清理对应缓存和撤回内容，属于不可撤回的记录清理。

- 车型与品牌共用原生左侧建议弹层，空间不足时回退；统一圆角、动画、焦点和键盘操作。
- 可配置快捷键与按操作记忆的确认提示；输入框和建议列表保留自己的编辑／导航键。
- 独立照片副本、仅回收站删除、逐项操作记录，以及旧共享引用的隔离迁移。
- Codex 与 Copilot 使用共享历史和文件分工协作；文档、发布脚本和开发工具整理。
- 识别请求的 Token 优化以兼容和效果回归为前提，具体启用策略与验证结果见本版说明。

命名指南随程序内置，综合品牌、车系、具体车型及补充要求；不依赖原归档目录在线。当前目录是否存在仅影响目录补全，既有目录不自动改名，指南及历史名称不发送给 API。

## 开发与验证

需要 Windows、.NET 10 SDK、Windows App SDK／WinUI C# 工具链及 Windows SDK。

```powershell
dotnet run --project .\CarArchive.Tests\CarArchive.Tests.csproj
dotnet run --project .\CarArchive.PhotoTests\CarArchive.PhotoTests.csproj
dotnet run --project .\CarArchive.Tools\CarArchive.Tools.csproj -- self-test-readonly
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package-Portable.ps1 -Version 1.2.2
```

发布包位于 `artifacts`。原生验证只使用隔离的 `CARARCHIVE_DATA_DIR`；日常 Data、旧发布目录和备份不作为破坏性测试目标。

共同作者：**MIRAIEVO、Codex（OpenAI）、GitHub Copilot（Microsoft）**。
