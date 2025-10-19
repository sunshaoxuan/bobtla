# TLA 实现进度评估（基于当前工作分支）

> 注：仓库未配置 `origin` 远程，执行 `git fetch origin` 失败，因此以下评估基于本地 `work` 分支的最新提交。若需与上游 `main` 对齐，请先添加远程后再同步。

## 完成度概览

| 分类 | 权重 | 完成度 | 说明 |
| --- | --- | --- | --- |
| Must (R1, R2, R3, R5, R7, R9, R10) | 60% | 0.54 | 自动检测/翻译、回帖、术语、预算守卫、审计链路均已落地；新增预算、RAG 与回帖链路的结构化日志覆盖 OBO、Graph 回帖与失败诊断，Teams Compose/设置页仍待 Stage 验证，合规侧缺少真实密钥托管演练，因此按 ~90% 计入。 |
| Should (R4, R6, R8, R11) | 25% | 0.22 | 多模型路由、RAG 上下文、MCP 工具链均完备；仪表盘在缓存基础上新增“最近同步/路线同步”标签，`resolveDataFromCache` 会标记网络或缓存来源并通过测试锁定逻辑，折算 ~88%。 【F:src/webapp/app.js†L39-L214】【F:src/webapp/index.html†L15-L53】【F:tests/dashboard.freshness.test.js†L1-L78】 |
| Could (R12, R13, R14) | 15% | 0.09 | 离线草稿/分片与术语上传管理实现度高；多语广播目前以单卡片附带附加译文方式呈现，未拆分多条公告，折算 60%。 |
| **总体** | **100%** | **85%** | 结合权重折算后的当前完成度约 **85%**。 |

## 需求映射详情

### Must 级需求

- **R1 自动检测并翻译** — `TranslationPipeline` 在翻译入口调用 `LanguageDetector`，若置信度不足会返回候选语言提示，同时兼顾长文本排队流程。 【F:src/TlaPlugin/Services/TranslationPipeline.cs†L47-L120】【F:src/TlaPlugin/Services/LanguageDetector.cs†L122-L256】
- **R2 可编辑回帖** — `ReplyService` 支持语气改写、附加多语言译文并最终通过 Teams 客户端回复，新增的结构化日志覆盖 OBO 令牌、附加语种与 Graph 状态，便于 Stage 诊断；`MessageExtensionHandler` 也会在译后触发回帖。 【F:src/TlaPlugin/Services/ReplyService.cs†L24-L334】【F:src/TlaPlugin/Services/TeamsReplyClient.cs†L1-L214】【F:src/TlaPlugin/Teams/MessageExtensionHandler.cs†L185-L266】
- **R3 术语库合并与冲突提示** — `GlossaryService` 处理多层级合并、冲突预览，消息扩展在冲突时返回自适应卡片供用户决策。 【F:src/TlaPlugin/Services/GlossaryService.cs†L13-L196】【F:src/TlaPlugin/Teams/MessageExtensionHandler.cs†L37-L118】
- **R5 成本/延迟控制** — `BudgetGuard`、`TranslationThrottle` 与 `UsageMetricsService` 组合提供预算守卫、速率限制与性能监控，并新增结构化日志记录预算拒绝与 RAG 获取耗时以支撑告警。 【F:src/TlaPlugin/Services/BudgetGuard.cs†L1-L90】【F:src/TlaPlugin/Services/TranslationThrottle.cs†L13-L88】【F:src/TlaPlugin/Services/UsageMetricsService.cs†L10-L109】【F:src/TlaPlugin/Services/ContextRetrievalService.cs†L1-L225】
- **R7 审计追溯** — `AuditLogger` 记录租户、模型、成本等字段并哈希原文，接口 `/api/audit` 支持导出。 【F:src/TlaPlugin/Services/AuditLogger.cs†L13-L52】【F:src/TlaPlugin/Program.cs†L482-L520】
- **R9 Teams 集成** — `MessageExtensionHandler` 覆盖翻译、改写、离线草稿与回帖流程；`wwwroot` 下提供 Tab/仪表盘静态资源，待 Stage 环境验证。 【F:src/TlaPlugin/Teams/MessageExtensionHandler.cs†L19-L336】【F:src/TlaPlugin/wwwroot/webapp/app.js†L1-L116】
- **R10 安全合规** — `ComplianceGateway` 按区域、认证、PII 检测拦截；`ConfigurableChatModelProvider` 支持 Key Vault 密钥解析及回退日志。实际密钥拉通尚未验证。 【F:src/TlaPlugin/Services/ComplianceGateway.cs†L8-L66】【F:src/TlaPlugin/Providers/ConfigurableChatModelProvider.cs†L20-L116】

### Should 级需求

- **R4 多模型路由** — `ModelProviderFactory` 从配置构建多家模型，`TranslationRouter` 在合规/预算校验后按优先级回退。 【F:src/TlaPlugin/Services/ModelProviderFactory.cs†L12-L61】【F:src/TlaPlugin/Services/TranslationRouter.cs†L17-L139】
- **R6 RAG 上下文增强** — `ContextRetrievalService` 通过 OBO 交换令牌并缓存频道上下文，`TranslationPipeline` 在翻译前调用。 【F:src/TlaPlugin/Services/ContextRetrievalService.cs†L16-L126】【F:src/TlaPlugin/Services/TranslationPipeline.cs†L90-L118】
- **R8 多语言与语气** — `PluginOptions.SupportedLanguages` 预置超过 100 种语言；`ToneTemplateService` 提供多语气提示，但 `LocalizationCatalogService` 目前仅含日/中 UI 资源。 【F:src/TlaPlugin/Configuration/PluginOptions.cs†L10-L143】【F:src/TlaPlugin/Services/ToneTemplateService.cs†L5-L44】【F:src/TlaPlugin/Services/LocalizationCatalogService.cs†L13-L95】
- **R11 MCP 工具** — `McpServer` 和 `McpToolRegistry` 暴露翻译、检测、术语与回帖工具，并执行参数校验。 【F:src/TlaPlugin/Services/McpServer.cs†L13-L123】

### Could 级需求

- **R12 离线草稿/长文本分片** — `OfflineDraftStore` 基于 SQLite 存储草稿，`TranslationPipeline` 将超长文本拆分入队并返回排队卡片。 【F:src/TlaPlugin/Services/OfflineDraftStore.cs†L13-L132】【F:src/TlaPlugin/Services/TranslationPipeline.cs†L108-L182】
- **R13 多语广播** — 回帖时可生成附加译文并在卡片中展示，但仍是单条消息，没有按成员语言拆分多帖。 【F:src/TlaPlugin/Services/ReplyService.cs†L182-L320】
- **R14 术语上传与管理** — Minimal API 支持 multipart 上传 CSV/TSV 并返回冲突/错误，前端设置页调用 `fetchJson` 渲染结果。 【F:src/TlaPlugin/Program.cs†L482-L520】【F:src/webapp/settingsTab.js†L1-L120】

## 下一步建议

1. **补齐合规演练**：在 Stage 环境实际配置 Key Vault、Graph 权限并运行 `Stage5SmokeTests` 新增的 readiness 命令，闭环 R10 缺口。 【F:scripts/SmokeTests/Stage5SmokeTests/Program.cs†L40-L214】
2. **扩展前端国际化**：补充 `LocalizationCatalogService` 及静态资源的多语言文案，与 `PluginOptions.SupportedLanguages` 对齐，提升 R8 完成度。
3. **多语广播策略**：为 R13 增加“按目标语言拆分多条回帖”开关或批量发送逻辑，兼容现有 `AdditionalTargetLanguages` 数据结构。
4. **Stage 验证记录**：将仪表盘/Compose 插件在 Stage 环境的真实调用、失败路径截图或日志沉淀至 `stage5_task_plan.md`，便于 go-live 评审。

