# Teams Language Assistant (TLA) – .NET 参考实现

## 项目概述
TLA 参考实现基于 .NET 7 Minimal API 与 SQLite，支撑 Microsoft Teams 消息扩展的跨语翻译、术语优先与合规留痕能力。功能设计遵循《BOBTLA 需求说明书》对 MVP 阶段的 Must/Should 要求，包括多模型路由、PII 拦截、预算控制与 Adaptive Card 回复体验。【F:docs/BOBTLA需求说明书.txt†L40-L207】

## 核心架构
| 模块 | 路径 | 说明 |
| --- | --- | --- |
| Web 宿主 | `src/TlaPlugin/Program.cs` | Minimal API 启动翻译与离线草稿接口，注入配置、术语库与模型工厂。 |
| 配置与模型 | `src/TlaPlugin/Configuration/PluginOptions.cs`、`src/TlaPlugin/Providers/*` | 以 `PluginOptions` 映射区域策略与模型参数；`MockModelProvider` 模拟多提供方与回退。 |
| 服务层 | `src/TlaPlugin/Services/*` | 覆盖语言检测、术语合并、预算守卫、合规网关、审计日志、SQLite 草稿仓库及翻译路由。 |
| 使用统计 | `src/TlaPlugin/Services/UsageMetricsService.cs` | 聚合租户维度的调用成本、延迟与模型占比，为前端仪表盘提供实时数据。 |
| 缓存与限流 | `src/TlaPlugin/Services/TranslationCache.cs`、`src/TlaPlugin/Services/TranslationThrottle.cs` | `TranslationCache` 依据租户与参数缓存译文，`TranslationThrottle` 控制并发与分钟速率。 |
| 密钥与令牌 | `src/TlaPlugin/Services/KeyVaultSecretResolver.cs`、`src/TlaPlugin/Services/TokenBroker.cs` | `KeyVaultSecretResolver` 模拟 Key Vault 缓存密钥，`TokenBroker` 生成 OBO 访问令牌供模型调用使用。 |
| Teams 适配 | `src/TlaPlugin/Teams/MessageExtensionHandler.cs` | 输出 Adaptive Card、处理预算/合规异常、保存离线草稿。 |
| 界面本地化 | `src/TlaPlugin/Services/LocalizationCatalogService.cs` | 暴露日文默认 UI 文案并提供中文覆盖，供消息卡片与错误提示统一取值。 |
| 进度路标 | `src/TlaPlugin/Services/DevelopmentRoadmapService.cs` | 汇总阶段目标、交付物与测试摘要，通过 `/api/roadmap` 供前端展示。 |
| 测试 | `tests/TlaPlugin.Tests/*` | 使用 xUnit 验证合规网关、路由回退、离线草稿持久化与消息扩展错误处理。 |

### 关键流程
1. `MessageExtensionHandler` 接收翻译命令后调用 `TranslationPipeline`，先执行术语替换与语言检测，命中 `TranslationCache` 时直接返回缓存；未命中时通过 `TranslationThrottle` 获取配额后委派 `TranslationRouter` 调用模型并聚合多语言结果。【F:src/TlaPlugin/Teams/MessageExtensionHandler.cs†L22-L64】【F:src/TlaPlugin/Services/TranslationPipeline.cs†L33-L76】【F:src/TlaPlugin/Services/TranslationCache.cs†L34-L78】【F:src/TlaPlugin/Services/TranslationThrottle.cs†L27-L78】
2. `TranslationRouter` 在调用模型前通过 `TokenBroker` 执行 OBO 令牌交换，再依次评估合规策略、预算额度与可用性，对失败的提供方自动回退并写入审计日志与令牌受众信息。【F:src/TlaPlugin/Services/TokenBroker.cs†L1-L63】【F:src/TlaPlugin/Services/TranslationRouter.cs†L30-L112】
3. `ComplianceGateway` 在翻译前检查区域、认证、禁译词及 PII，违反策略时阻断调用；`BudgetGuard` 跟踪租户当日花费避免超支。【F:src/TlaPlugin/Services/ComplianceGateway.cs†L17-L69】【F:src/TlaPlugin/Services/BudgetGuard.cs†L8-L27】
4. `OfflineDraftStore` 通过 SQLite 持久化草稿，支持断线场景下的恢复与清理。【F:src/TlaPlugin/Services/OfflineDraftStore.cs†L14-L82】

## 开发阶段
| 阶段 | 目标 | 进度 | 成果 |
| --- | --- | --- | --- |
| 阶段 1：平台基线 | 吸收需求、搭建 Minimal API、完成消息扩展骨架 | ✅ 完成 | 建立 .NET + SQLite 架构，交付 Adaptive Card 主流程并沉淀术语/语气服务。 |
| 阶段 2：安全与合规 | 打通合规网关、预算守卫与密钥/OBO 管理 | ✅ 完成 | `ComplianceGateway` 覆盖区域/禁译，`BudgetGuard`、`TokenBroker` 与 `KeyVaultSecretResolver` 协同生效。 |
| 阶段 3：性能与可观测 | 提升缓存、速率与多模型互联能力，沉淀指标 | ✅ 完成 | `TranslationCache`、`TranslationThrottle`、`UsageMetricsService` 形成性能护栏并输出仪表盘数据。 |
| 阶段 4：前端体验 | 聚合状态/路标 API，交付设置页仪表盘与本地化 | ✅ 完成 | `/api/status`、`/api/roadmap`、`/api/localization` 已对接新建 `src/webapp` 仪表盘，界面体验封板。 |
| 阶段 5：上线准备 | 串联真实模型、联调并封板 | 🚧 进行中 | 对接真实依赖、巩固密钥治理并完成发布前冒烟。 |

## 当前状态
项目处于 **阶段 5：上线准备**，前三个阶段已完成平台、合规与性能底座，阶段 4 的前端体验亦已封板，整体完成度 80%，前端完成度 80%，但 Stage 5 集成尚待打通。【F:src/TlaPlugin/Services/ProjectStatusService.cs†L12-L102】我们将 `/api/status`、`/api/roadmap`、`/api/localization/*` 聚合到全新的 `src/webapp` 仪表盘，页面通过回退样例自动回显进度、阶段成果与本地化语言，确保前端可离线预览。【F:src/webapp/index.html†L1-L44】【F:src/webapp/app.js†L1-L161】【F:src/webapp/styles.css†L1-L108】路标数据与测试清单已按五大阶段重新梳理，活跃阶段聚焦上线准备并延伸到发布前的端到端验证。【F:src/TlaPlugin/Services/DevelopmentRoadmapService.cs†L12-L102】

阶段 5 的三项关键后续动作如下，均可在《阶段 5 联调 Runbook》中找到详细步骤：

1. **密钥映射**：按照 Key Vault 映射与 `Stage5SmokeTests` 验证流程固化密钥分发策略，确保 `KeyVaultSecretResolver` 能解析真实机密。【F:docs/stage5-integration-runbook.md†L1-L55】
2. **Graph/OBO 冒烟**：依据 Graph 权限开通与 `reply` 命令流程完成 OBO 链路冒烟，验证 Teams 回帖所需的令牌与网络依赖。【F:docs/stage5-integration-runbook.md†L57-L140】
3. **真实模型切换**：在成本可控场景下启用 `--use-live-model` 等模式切换到真实模型 Provider，并结合远程 API 校验发布前冒烟结果。【F:docs/stage5-integration-runbook.md†L141-L210】

## 下一步规划
1. **完善设置页组件并补全校验**：继续扩展 `src/webapp` 仪表盘的上传、搜索与校验体验，保障前端交互质量。【F:src/webapp/app.js†L89-L149】
2. **串联实时数据与刷新机制**：将前端与 `/api/status`、`/api/roadmap`、`/api/localization/*` 建立轮询或订阅，确保阶段进度实时更新。【F:src/webapp/app.js†L1-L161】
3. **安排联调与上线验收**：替换模拟模型、连通真实 Key Vault/OBO，并规划 CI 与回滚预案，为阶段 5 发布做准备。【F:src/TlaPlugin/Services/ProjectStatusService.cs†L12-L42】【F:src/TlaPlugin/Services/DevelopmentRoadmapService.cs†L12-L102】

## 阶段成果与测试
- **多模型路由与语气模板**：`TranslationRouter` 在首选模型失败后自动回退，利用 `ToneTemplateService` 统一敬体/商务/技术风格。【F:src/TlaPlugin/Services/TranslationRouter.cs†L18-L112】【F:src/TlaPlugin/Services/ToneTemplateService.cs†L5-L34】
- **预算与审计留痕**：`BudgetGuard` 以租户+日期统计消耗，`AuditLogger` 保存哈希指纹与模型元数据满足审计需求。【F:src/TlaPlugin/Services/BudgetGuard.cs†L8-L27】【F:src/TlaPlugin/Services/AuditLogger.cs†L15-L43】
- **SQLite 草稿支持**：`OfflineDraftStore` 在断线时保留草稿并支持定期清理，xUnit 覆盖持久化流程。【F:src/TlaPlugin/Services/OfflineDraftStore.cs†L14-L82】【F:tests/TlaPlugin.Tests/OfflineDraftStoreTests.cs†L1-L30】
- **合规网关**：`ComplianceGateway` 综合地区、认证、禁译与 PII 正则，测试验证禁译词阻断与认证放行。【F:src/TlaPlugin/Services/ComplianceGateway.cs†L17-L69】【F:tests/TlaPlugin.Tests/ComplianceGatewayTests.cs†L1-L33】
- **消息扩展体验**：`MessageExtensionHandler` 输出以日文为默认文案的 Adaptive Card，并在预算或速率超限时返回提示卡片；单测验证卡片内容。【F:src/TlaPlugin/Teams/MessageExtensionHandler.cs†L21-L94】【F:tests/TlaPlugin.Tests/MessageExtensionHandlerTests.cs†L17-L118】
- **缓存去重与限流**：`TranslationCache` 以租户维度缓存译文，`TranslationThrottle` 限制速率与并发；单测覆盖缓存复用与限流提示。【F:src/TlaPlugin/Services/TranslationCache.cs†L15-L88】【F:src/TlaPlugin/Services/TranslationThrottle.cs†L13-L116】【F:tests/TlaPlugin.Tests/TranslationPipelineTests.cs†L1-L110】【F:tests/TlaPlugin.Tests/MessageExtensionHandlerTests.cs†L17-L179】
- **密钥托管与 OBO**：`KeyVaultSecretResolver` 以 TTL 缓存密钥，`TokenBroker` 使用 HMAC 生成令牌并在到期前缓存，路由在执行翻译前要求有效令牌；单测覆盖缓存刷新、异常与缺失用户的情境。【F:src/TlaPlugin/Services/KeyVaultSecretResolver.cs†L1-L63】【F:src/TlaPlugin/Services/TokenBroker.cs†L1-L63】【F:src/TlaPlugin/Services/TranslationRouter.cs†L30-L135】【F:tests/TlaPlugin.Tests/TokenBrokerTests.cs†L1-L39】【F:tests/TlaPlugin.Tests/TranslationRouterTests.cs†L1-L214】【F:tests/TlaPlugin.Tests/KeyVaultSecretResolverTests.cs†L1-L67】
- **多语广播体验**：`TranslationRouter` 根据附加语言调整预算、逐一重写译文并将结果写入审计，Adaptive Card 与消息扩展渲染出"额外翻译"分节；单测覆盖卡片内容与审计记录。【F:src/TlaPlugin/Services/TranslationRouter.cs†L71-L169】【F:src/TlaPlugin/Services/AuditLogger.cs†L15-L43】【F:src/TlaPlugin/Teams/MessageExtensionHandler.cs†L21-L78】【F:tests/TlaPlugin.Tests/TranslationRouterTests.cs†L17-L210】【F:tests/TlaPlugin.Tests/MessageExtensionHandlerTests.cs†L17-L116】
- **多模型接入与一键插入**：`ConfigurableChatModelProvider` 统一封装 OpenAI/Claude/Groq/OpenWebUI/Ollama 调用并在 KeyVault 中解析密钥，Adaptive Card 行动按钮支持将主译文及额外语种一键写回聊天对话框；测试验证工厂类型选择与 Teams 按钮 payload。【F:src/TlaPlugin/Providers/ConfigurableChatModelProvider.cs†L1-L209】【F:src/TlaPlugin/Services/ModelProviderFactory.cs†L9-L52】【F:src/TlaPlugin/Services/TranslationRouter.cs†L119-L169】【F:tests/TlaPlugin.Tests/ModelProviderFactoryTests.cs†L1-L62】【F:tests/TlaPlugin.Tests/MessageExtensionHandlerTests.cs†L17-L116】
- **前端数据平面**：`ConfigurationSummaryService` 汇总租户限额、支持语言、语气模板与模型提供方；`ProjectStatusService` 暴露阶段进度、整体完成度与前端准备度；Minimal API 公开配置、用语、审计与状态端点供前端即时读取。【F:src/TlaPlugin/Services/ConfigurationSummaryService.cs†L1-L48】【F:src/TlaPlugin/Services/ProjectStatusService.cs†L8-L62】【F:src/TlaPlugin/Program.cs†L56-L85】【F:tests/TlaPlugin.Tests/ConfigurationSummaryServiceTests.cs†L1-L53】【F:tests/TlaPlugin.Tests/ProjectStatusServiceTests.cs†L1-L34】
- **使用统计看板**：`UsageMetricsService` 聚合租户维度的调用量、成本与延迟，并记录"合规拒绝""预算不足""模型错误""认证失败"等失败原因分布，`/api/metrics` 为前端仪表盘提供实时 JSON；单元测试覆盖服务聚合、失败统计与路由集成后的多租户指标。【F:src/TlaPlugin/Services/UsageMetricsService.cs†L1-L123】【F:src/TlaPlugin/Program.cs†L86-L98】【F:tests/TlaPlugin.Tests/UsageMetricsServiceTests.cs†L1-L56】【F:tests/TlaPlugin.Tests/TranslationRouterTests.cs†L148-L287】
- **界面本地化能力**：`LocalizationCatalogService` 以日文为默认界面语言提供卡片标题、动作按钮与错误提示的 i18n 字典，并支持中文覆盖与可枚举的可用语言列表；消息扩展、路由与新建的前端仪表盘共用同一份本地化数据，实现 API 与 UI 的一致性。【F:src/TlaPlugin/Services/LocalizationCatalogService.cs†L1-L122】【F:src/TlaPlugin/Teams/MessageExtensionHandler.cs†L14-L69】【F:src/TlaPlugin/Services/TranslationRouter.cs†L18-L206】【F:src/TlaPlugin/Program.cs†L86-L118】【F:src/webapp/app.js†L1-L161】【F:src/webapp/viewModel.js†L1-L62】【F:tests/TlaPlugin.Tests/LocalizationCatalogServiceTests.cs†L1-L66】【F:tests/TlaPlugin.Tests/TranslationPipelineTests.cs†L13-L102】【F:tests/TlaPlugin.Tests/MessageExtensionHandlerTests.cs†L13-L125】
- **阶段路标与仪表盘体验**：`DevelopmentRoadmapService` 将九个子阶段合并为五大阶段，活跃阶段聚焦前端体验并新增仪表盘测试项；`src/webapp` 仪表盘按阶段渲染进度、交付物与测试卡片，`dashboardViewModel.test.js` 验证聚合逻辑保持与服务同步。【F:src/TlaPlugin/Services/DevelopmentRoadmapService.cs†L12-L102】【F:src/TlaPlugin/Program.cs†L104-L118】【F:src/webapp/index.html†L1-L44】【F:src/webapp/app.js†L1-L161】【F:src/webapp/viewModel.js†L1-L62】【F:tests/TlaPlugin.Tests/DevelopmentRoadmapServiceTests.cs†L1-L18】【F:tests/dashboardViewModel.test.js†L1-L35】

### 测试与运行
1. `dotnet restore` – 还原 NuGet 依赖。
2. `dotnet test` – 执行 xUnit 测试套件，覆盖合规、路由、草稿、缓存限流、OBO 令牌与消息扩展场景。【F:tests/TlaPlugin.Tests/TranslationRouterTests.cs†L1-L205】【F:tests/TlaPlugin.Tests/TokenBrokerTests.cs†L1-L39】【F:tests/TlaPlugin.Tests/MessageExtensionHandlerTests.cs†L17-L179】
3. `dotnet run --project src/TlaPlugin/TlaPlugin.csproj` – 启动本地 API，`POST /api/translate` 接受 `TranslationRequest` 负载返回 Adaptive Card。
4. `npm test` – 使用 Node 测试仪表盘视图模型与消息扩展逻辑，覆盖阶段聚合、本地化排序与 Teams 体验。【F:tests/dashboardViewModel.test.js†L1-L35】【F:tests/messageExtension.test.js†L1-L80】
5. `dotnet run --project scripts/SmokeTests/Stage5SmokeTests` – 提供 `secrets`、`reply` 与 `metrics` 子命令，分别校验 Key Vault 密钥映射、模拟 OBO+Teams 回帖并拉取 `/api/metrics`/`/api/audit` 观测数据，详见 Stage 5 Runbook。【F:scripts/SmokeTests/Stage5SmokeTests/Program.cs†L82-L414】【F:docs/stage5-integration-runbook.md†L1-L210】

> 代码注释统一改写为日文，界面默认文案保持日文并提供中文覆盖，避免混用多种语言，符合多语言治理规范。【F:src/TlaPlugin/Services/TranslationRouter.cs†L18-L176】【F:src/TlaPlugin/Teams/MessageExtensionHandler.cs†L9-L94】【F:src/TlaPlugin/Services/LocalizationCatalogService.cs†L1-L122】

### 请求示例

消息扩展和 Compose 插件现在可以显式携带 RAG 开关与上下文提示，后端会在 `ExtensionData` 中解析 `useRag` 与 `contextHints` 字段：

```http
POST /api/translate
Content-Type: application/json

{
  "text": "Need a formal Japanese reply",
  "sourceLanguage": "en",
  "targetLanguage": "ja",
  "tenantId": "contoso",
  "userId": "alex",
  "channelId": "general",
  "useRag": true,
  "contextHints": [
    "budget review",
    "contract draft"
  ],
  "metadata": {
    "origin": "messageExtension",
    "modelId": "model-a",
    "tone": "formal",
    "useTerminology": true
  }
}
```

关闭 RAG 时只需省略提示或保持数组为空，后端会退回传统翻译流程。
