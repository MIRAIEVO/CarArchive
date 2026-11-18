# CarArchive 1.3

共同作者：MIRAIEVO、Codex、GitHub Copilot。

模型列表包含 GPT-6.2 Astra／Sol。左上角提供 Minimal、Low、Medium、High、Xhigh、Max、Ultra 七档，默认 Low。可在设置修改新分组默认档位和当前厂商的默认联网状态。各档只返回车型身份字段；OpenAI／Claude 使用各自的工具上限，Gemini／DeepSeek 的查询数为建议值。

OpenAI 的 Medium 及更高档采用流式接收并提高思考与输出预算。仅在首轮预算耗尽或明确未返回最终文本时降档恢复一次；取消、限流及连接中断不自动重发。保留了切组过渡、预览稳定性与右键操作的 1.2.5 修复。

在「设置 → 识别与费用」选择厂商、输入 API Key、选择模型并决定是否联网。「模型清单与提示设置」可读取账号的模型 ID，或填写补充提示词。关闭联网时不启用搜索工具；开启后只显示接口实际提供的来源。

新增接口：[OpenAI Responses 与联网搜索](https://developers.openai.com/api/docs/guides/tools-web-search)、[Claude Messages 联网搜索](https://platform.claude.com/docs/en/agents-and-tools/tool-use/web-search-tool)、[DeepSeek 图片输入](https://api-docs.deepseek.com/guides/vision/)与[联网工具](https://api-docs.deepseek.com/guides/web_tools/)。DeepSeek 图片识别请选择 `deepseek-flash`。

解压到新目录，运行根目录的 CarArchive.exe。可在首次启动时导入旧版 Data 中的设置和加密 Key；换 Windows 用户或机器须重新输入密钥。测试包不含个人数据，不自动覆盖旧安装。
