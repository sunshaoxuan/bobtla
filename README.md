# Teams Language Assistant (TLA) – .NET 参考实现

## 项目概述
TLA 参考实现基于 .NET 7 Minimal API 与 SQLite，支撑 Microsoft Teams 消息扩展的跨语翻译、术语优先与合规留痕能力。功能设计遵循《BOBTLA 需求说明书》对 MVP 阶段的 Must/Should 要求，包括多模型路由、PII 拦截、预算控制与 Adaptive Card 回复体验。【F:docs/BOBTLA需求说明书.txt†L40-L207】

## 核心架构
| 模块 | 路径 | 说明 |
| --- | --- | --- |
| Web 宿主 | `src/TlaPlugin/Program.cs` | Minimal API 启动翻译与离线草稿接口，注入配置、术语库与模型工厂。 |
| 配置与模型 | `src/TlaPlugin/Configuration/PluginOptions.cs`、`src/TlaPlugin/Providers/*` | 以 `PluginOptions` 映射区域策略与模型参数；`MockModelProvider` 模拟多提供方与回退。 |
| 服务层 | `src/TlaPlugin/Services/*` | 覆盖语言检测、术语合并、预算守卫、合规网关、审计日志、SQLite 草稿仓库及翻译路由。 |
| 缓存与限流 | `src/TlaPlugin/Services/TranslationCache.cs`、`src/TlaPlugin/Services/TranslationThrottle.cs` | `TranslationCache` 依据租户与参数缓存译文，`TranslationThrottle` 控制并发与分钟速率。 |
| 密钥与令牌 | `src/TlaPlugin/Services/KeyVaultSecretResolver.cs`、`src/TlaPlugin/Services/TokenBroker.cs` | `KeyVaultSecretResolver` 模拟 Key Vault 缓存密钥，`TokenBroker` 生成 OBO 访问令牌供模型调用使用。 |
| Teams 适配 | `src/TlaPlugin/Teams/MessageExtensionHandler.cs` | 输出 Adaptive Card、处理预算/合规异常、保存离线草稿。 |
| 测试 | `tests/TlaPlugin.Tests/*` | 使用 xUnit 验证合规网关、路由回退、离线草稿持久化与消息扩展错误处理。 |

### 关键流程
1. `MessageExtensionHandler` 接收翻译命令后调用 `TranslationPipeline`，先执行术语替换与语言检测，命中 `TranslationCache` 时直接返回缓存；未命中时通过 `TranslationThrottle` 获取配额后委派 `TranslationRouter` 调用模型并聚合多语言结果。【F:src/TlaPlugin/Teams/MessageExtensionHandler.cs†L22-L64】【F:src/TlaPlugin/Services/TranslationPipeline.cs†L33-L76】【F:src/TlaPlugin/Services/TranslationCache.cs†L34-L78】【F:src/TlaPlugin/Services/TranslationThrottle.cs†L27-L78】
2. `TranslationRouter` 在调用模型前通过 `TokenBroker` 执行 OBO 令牌交换，再依次评估合规策略、预算额度与可用性，对失败的提供方自动回退并写入审计日志与令牌受众信息。【F:src/TlaPlugin/Services/TokenBroker.cs†L1-L63】【F:src/TlaPlugin/Services/TranslationRouter.cs†L30-L112】
3. `ComplianceGateway` 在翻译前检查区域、认证、禁译词及 PII，违反策略时阻断调用；`BudgetGuard` 跟踪租户当日花费避免超支。【F:src/TlaPlugin/Services/ComplianceGateway.cs†L17-L69】【F:src/TlaPlugin/Services/BudgetGuard.cs†L8-L27】
4. `OfflineDraftStore` 通过 SQLite 持久化草稿，支持断线场景下的恢复与清理。【F:src/TlaPlugin/Services/OfflineDraftStore.cs†L14-L82】

## 开发阶段
| 阶段 | 目标 | 进度 | 成果 |
| --- | --- | --- | --- |
| 阶段 0：需求吸收 | 解析说明书并确定 .NET 技术栈、SQLite 本地存储策略 | ✅ 完成 | 明确 MVP 功能、地区策略与测试范围。 |
| 阶段 1：服务编排 | 实现模型工厂、翻译路由、合规/预算/术语服务 | ✅ 完成 | 支持多模型回退、语气模板与审计追踪。 |
| 阶段 2：Teams 适配 | 构建消息扩展处理器与 Adaptive Card 响应 | ✅ 完成 | 返回日文 UI 文案的卡片，整合多语言结果。 |
| 阶段 3：持久化与测试 | 集成 SQLite 草稿仓库，使用 xUnit 覆盖关键路径 | ✅ 完成 | 草稿持久化、合规守卫、预算超限等单测通过。 |
| 阶段 4：合规加固 | 提供地区/认证校验与 PII 正则库，文档化阶段成果 | ✅ 完成 | `ComplianceGateway` 支持禁译词与区域白名单。 |
| 阶段 5：性能护栏 | 引入缓存去重与速率/并发限制 | ✅ 完成 | `TranslationCache` 降低重复调用成本，`TranslationThrottle` 保证租户速率受控。 |
| 阶段 6：密钥与 OBO | 集成 Key Vault 密钥缓存与 OBO 令牌代理 | ✅ 完成 | `KeyVaultSecretResolver` 缓存密钥，`TokenBroker` 缓存/刷新访问令牌并在路由前强制认证。 |

## 当前状态
项目处于 **阶段 6：密钥与 OBO**，在性能护栏基础上增加 Key Vault 密钥缓存与 On-behalf-of 令牌代理，要求每次翻译均先获取有效令牌后才允许调用模型，配合审计日志记录令牌受众以满足后续安全稽核。【F:src/TlaPlugin/Services/KeyVaultSecretResolver.cs†L1-L63】【F:src/TlaPlugin/Services/TokenBroker.cs†L1-L63】【F:src/TlaPlugin/Services/TranslationRouter.cs†L30-L112】

## 下一步规划
1. **对接真实模型 SDK**：替换 Mock 提供方，引入 Azure OpenAI/Anthropic 并使用并发/延迟策略。
2. **接入真实 Key Vault/OBO**：将模拟密钥存储替换为 Azure Key Vault SDK，调用 Microsoft Graph 获取用户断言，完成端到端 OBO。【F:docs/BOBTLA需求说明书.txt†L207-L270】
3. **多语广播与 Tab 设置页**：扩展 `TranslationRequest.AdditionalTargetLanguages` 支撑群组广播，新增 Tab 管理术语库上传与租户统计。
4. **CI 合规流水线**：接入 Roslyn 分析、机密扫描与 SQL 压测，自动生成审计报表。

## 阶段成果与测试
- **多模型路由与语气模板**：`TranslationRouter` 在首选模型失败后自动回退，利用 `ToneTemplateService` 统一敬体/商务/技术风格。【F:src/TlaPlugin/Services/TranslationRouter.cs†L18-L103】【F:src/TlaPlugin/Services/ToneTemplateService.cs†L5-L34】
- **预算与审计留痕**：`BudgetGuard` 以租户+日期统计消耗，`AuditLogger` 保存哈希指纹与模型元数据满足审计需求。【F:src/TlaPlugin/Services/BudgetGuard.cs†L8-L27】【F:src/TlaPlugin/Services/AuditLogger.cs†L9-L35】
- **SQLite 草稿支持**：`OfflineDraftStore` 在断线时保留草稿并支持定期清理，xUnit 覆盖持久化流程。【F:src/TlaPlugin/Services/OfflineDraftStore.cs†L14-L82】【F:tests/TlaPlugin.Tests/OfflineDraftStoreTests.cs†L1-L30】
- **合规网关**：`ComplianceGateway` 综合地区、认证、禁译与 PII 正则，测试验证禁译词阻断与认证放行。【F:src/TlaPlugin/Services/ComplianceGateway.cs†L17-L69】【F:tests/TlaPlugin.Tests/ComplianceGatewayTests.cs†L1-L33】
- **消息扩展体验**：`MessageExtensionHandler` 输出日文 Adaptive Card，并在预算或速率超限时返回提示卡片；单测验证卡片内容。【F:src/TlaPlugin/Teams/MessageExtensionHandler.cs†L22-L112】【F:tests/TlaPlugin.Tests/MessageExtensionHandlerTests.cs†L1-L142】
- **缓存去重与限流**：`TranslationCache` 以租户维度缓存译文，`TranslationThrottle` 限制速率与并发；单测覆盖缓存复用与限流提示。【F:src/TlaPlugin/Services/TranslationCache.cs†L15-L88】【F:src/TlaPlugin/Services/TranslationThrottle.cs†L13-L116】【F:tests/TlaPlugin.Tests/TranslationPipelineTests.cs†L1-L110】【F:tests/TlaPlugin.Tests/MessageExtensionHandlerTests.cs†L1-L142】
- **密钥托管与 OBO**：`KeyVaultSecretResolver` 以 TTL 缓存密钥，`TokenBroker` 使用 HMAC 生成令牌并在到期前缓存，路由在执行翻译前要求有效令牌；单测覆盖缓存刷新、异常与缺失用户的情境。【F:src/TlaPlugin/Services/KeyVaultSecretResolver.cs†L1-L63】【F:src/TlaPlugin/Services/TokenBroker.cs†L1-L63】【F:src/TlaPlugin/Services/TranslationRouter.cs†L30-L112】【F:tests/TlaPlugin.Tests/TokenBrokerTests.cs†L1-L39】【F:tests/TlaPlugin.Tests/TranslationRouterTests.cs†L1-L120】【F:tests/TlaPlugin.Tests/KeyVaultSecretResolverTests.cs†L1-L67】

### 测试与运行
1. `dotnet restore` – 还原 NuGet 依赖。 
2. `dotnet test` – 执行 xUnit 测试套件，覆盖合规、路由、草稿、缓存限流、OBO 令牌与消息扩展场景。【F:tests/TlaPlugin.Tests/TranslationRouterTests.cs†L1-L120】【F:tests/TlaPlugin.Tests/TokenBrokerTests.cs†L1-L39】【F:tests/TlaPlugin.Tests/MessageExtensionHandlerTests.cs†L1-L142】
3. `dotnet run --project src/TlaPlugin/TlaPlugin.csproj` – 启动本地 API，`POST /api/translate` 接受 `TranslationRequest` 负载返回 Adaptive Card。

> 代码注释以日文撰写，界面返回文案默认使用日文，符合需求文档“代码注释为日文、界面默认日文”的约束。【F:src/TlaPlugin/Services/TranslationRouter.cs†L13-L103】【F:src/TlaPlugin/Teams/MessageExtensionHandler.cs†L10-L112】
