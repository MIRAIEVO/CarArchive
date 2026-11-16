# CarArchive 协作约定

本项目由 MIRAIEVO、Codex（OpenAI）、GitHub Copilot（Microsoft）共同协作。

## 每次任务开始

1. 阅读 README、CHANGELOG 和 docs/collaboration/HANDOFF.md，检查 git status 与当前文件负责人。
2. 用户最新指令优先。先用适用的已有 skill、plugin 和本机工具；实际困难再安装必要环境。
3. 未读到另一方确认前，不宣称已沟通或已完成交接。
4. 修改前登记文件范围；不得覆盖另一方未提交的改动。共用文件的写入必须串行。

## 安全与验证

- 原照片、已分类目录及现有 Data 不作测试材料的破坏性操作。测试使用隔离副本。
- 凭据只在内存中读取，禁止写入日志、聊天或 Git；命名指南不发送给识别 API。
- 复制组产生独立照片副本；回收操作禁止永久删除回退；已归档文件不属于清理组记录的目标。
- 记录实际验证命令和限制；不要以模拟测试代替真实 API 效果声明。
- 只有 MIRAIEVO 授权的任务可以发布。每次交付生成便携包，根目录一个启动 EXE，其他文件放入子目录。
- 完成后更新交接记录和 CHANGELOG，回复简练。旧版本 Data、备份和原始照片不自动清理。

## 当前任务

当前任务、文件归属和互通状态见 docs/collaboration/HANDOFF.md。
