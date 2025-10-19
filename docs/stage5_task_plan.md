# Stage 5 Completion Synchronous Workstreams

Following the 86% completion assessment, the remaining scope targets Stage 5 readiness and production hardening. The tasks below are structured as parallel workstreams so multiple owners can progress simultaneously toward 100% completion.

## Progress Assessment (as of current review)

| Workstream | Status | Evidence & Notes |
| --- | --- | --- |
| Secrets & Compliance Readiness | 🟡 部分完成 | `Stage5SmokeTests` 新增 `--verify-readiness` 与 `ready` 命令，可在 HMAC/Graph 检查后探测 Stage 就绪文件并写入时间戳，为 StageFiveDiagnostics 提供真实信号。 【F:scripts/SmokeTests/Stage5SmokeTests/Program.cs†L40-L214】【F:scripts/SmokeTests/Stage5SmokeTests/Program.cs†L430-L520】 |
| Live Model Provider Enablement | 🟡 部分完成 | `ConfigurableChatModelProvider` 现记录模型调用起止、密钥解析与回退原因，`ModelProviderFactory` 注入 ILogger 以支撑 live 模式诊断。 【F:src/TlaPlugin/Providers/ConfigurableChatModelProvider.cs†L22-L208】【F:src/TlaPlugin/Services/ModelProviderFactory.cs†L1-L56】 |
| Frontend Telemetry Dashboard Integration | 🟡 部分完成 | 在重试/告警与缓存的基础上，`resolveDataFromCache` 记录数据来源与时间戳，新增 `updateFreshnessIndicator` 统一驱动“最近同步/路线同步/最近更新”标签并携带来源提示，Node 测试覆盖 metrics 标签的来源切换。 【F:src/webapp/app.js†L39-L214】【F:src/webapp/app.js†L912-L1056】【F:tests/dashboard.freshness.test.js†L1-L120】 |
| Reply Service & Teams Integration Hardening | 🟡 部分完成 | `ReplyService` 与 `TeamsReplyClient` 增加 OBO 交换、附加语种与 Graph 调用的结构化日志，可追踪消息 ID、状态码与预算/权限异常，为 Stage 回帖冒烟提供可观测性。 【F:src/TlaPlugin/Services/ReplyService.cs†L24-L334】【F:src/TlaPlugin/Services/TeamsReplyClient.cs†L1-L214】 |
| Observability & Rollout Operations | 🟡 部分完成 | `BudgetGuard`、`ContextRetrievalService`、`ReplyService` 与 `TeamsReplyClient` 输出结构化日志，记录预算拒绝、RAG 抓取耗时与 Graph 回复状态，为后续 Application Insights 查询奠定数据基础。 【F:src/TlaPlugin/Services/BudgetGuard.cs†L1-L90】【F:src/TlaPlugin/Services/ContextRetrievalService.cs†L1-L225】【F:src/TlaPlugin/Services/ReplyService.cs†L24-L334】【F:src/TlaPlugin/Services/TeamsReplyClient.cs†L1-L214】 |
| Documentation & Stakeholder Alignment | 🟡 部分完成 | 当前文档已列出工作流与负责人框架，但尚缺 burndown、风险与会议纪要等动态内容。 【F:docs/stage5_task_plan.md†L1-L32】 |

## 下一步并行任务拆解

1. **Secrets & Compliance Readiness**
   - 利用 `Stage5SmokeTests -- secrets --verify-readiness` 持续探测共享卷权限，并在联调完成后执行 `-- ready` 写入冒烟时间戳，驱动 `StageFiveDiagnostics` 更新。
   - 完成密钥回退策略清理：更新服务器配置关闭 HMAC 回退，提交变更记录，并在 `StageFiveDiagnostics` 中同步状态标记。
   - 执行 Graph 权限验证脚本：编写/运行自动化脚本校验所需作用域，输出结果至 Runbook。
   - 准备 `Stage5SmokeTests` 流水线：在 CI/CD 中植入 secrets/reply/metrics 冒烟脚本并记录最新运行结果。
   - 2025-10-19 变更记录：`appsettings.Stage.json` 与部署 override 已指向 `tla-stage-kv`、`contoso-stage-kv`、`enterprise-stage-kv`，并统一 Graph 作用域/模型 Provider，确保 `ConfigurableChatModelProvider` 读取真实 Key Vault 凭据。【F:src/TlaPlugin/appsettings.Stage.json†L1-L49】【F:deploy/stage.appsettings.override.json†L1-L41】【F:src/TlaPlugin/appsettings.json†L241-L266】
   - 冒烟命令在容器内因缺少 .NET SDK 未执行：`dotnet` 不存在导致 `secrets`/`reply`/`metrics` 三条命令返回 `command not found`，需在具备 SDK 的环境（或 CI 阶段）重试并落盘输出。【dd1477†L1-L4】【94bc32†L1-L3】【a2f815†L1-L3】
   - 告警预案：准备下列 KQL 作为 Application Insights 告警规则，监测模型提供方回退/失败峰值并提醒密钥或 Graph 链路异常：

     ```kusto
     let window = ago(15m);
     AppTraces
     | where TimeGenerated >= window
     | where Properties["Category"] == "TlaPlugin.Providers.ConfigurableChatModelProvider"
     | where Message has "Provider" and (Message has "使用回退模型" or SeverityLevel >= 3)
     | summarize FallbackCount = count() by ProviderId = tostring(Properties["ProviderId"]), bin(TimeGenerated, 5m)
     | where FallbackCount > 0
     ```
   - 下一步：在具备 .NET SDK 的 Stage 自动化流水线重跑冒烟，并将 `metrics` 输出与 Stage 就绪文件时间戳附加到变更票据；完成后启用上述查询的阈值告警（例如连续两窗出现回退或错误）。

2. **Live Model Provider Enablement**
   - 在基础设施仓库中登记 Key Vault secrets，编写校验脚本检查密钥有效期并告警。
   - 基于新日志完善 `--use-live-model` 集成测试，断言密钥解析、HTTP 成功/失败与回退路径均有记录。
   - 结合日志输出定义 Application Insights 查询与告警，捕获密钥缺失、请求超时等异常。

3. **Frontend Telemetry Dashboard Integration**
   - 验证缓存策略：在 Stage 环境监控 `/api/status`、`/api/roadmap` 等接口的真实响应，确认本地存储缓存能在短暂失败时复用最新数据，再逐步删除冗余常量。
   - 新增端到端 UI 测试：使用 Playwright/Teams WebView 模拟请求失败与成功路径，验证 toast、最新同步标签与图表渲染。

4. **Reply Service & Teams Integration Hardening**
   - 同步最新 Teams 消息扩展 schema，更新 DTO、验证器与映射表。
   - 在 Stage 环境跑通多轮对话，复核新增日志中记录的 token、语言与 Graph 状态，针对 budget guard 与审计差异开 Issue 跟踪。

5. **Observability & Rollout Operations**
   - 丰富日志：为预算守卫、RAG 检索、模型回退增加结构化字段，并在 Application Insights/Splunk 中建立查询。（预算与 RAG 日志已落地，需继续覆盖模型回退与指标管道。）
   - 建设 Stage 仪表盘与告警：定义延迟、错误率、令牌使用基线，配置阈值与通知渠道。
   - 草拟回滚手册：涵盖配置开关、模型切换和 Teams manifest 回退流程。

6. **Documentation & Stakeholder Alignment**
   - 建立周度 burndown 与风险登记表，将任务、负责人与阻塞项同步到共享文档。
   - 规划 Stage 5 go/no-go 会议，拟定议程、成功标准与待决议事项。

---

以下为最初规划的任务清单，保留以追踪完整需求：

## 1. Secrets & Compliance Readiness
- Finalize HMAC fallback removal and update configuration to rely exclusively on signed callbacks.
- Expand Microsoft Graph scopes per the Stage 5 runbook; validate delegated and application permissions in integration tenants.
- Execute `Stage5SmokeTests` (`secrets`, `reply`, `metrics`) end-to-end with production-like secrets.
- Document operational validation results and open issues in the deployment runbook.

## 2. Live Model Provider Enablement
- Provision Key Vault secrets for each production model provider and map them in the deployment configuration.
- Exercise `ConfigurableChatModelProvider` with `--use-live-model` to validate key retrieval, fallback, and telemetry reporting.
- Implement alerting for missing/expired secrets and add automated validation in CI to block stale credentials.

## 3. Frontend Telemetry Dashboard Integration
- Replace local fallback data in the dashboard with live API calls to the telemetry service.
- Harden `fetchJson` error handling with retry, toast notification, and instrumentation for failed requests.
- Build integration tests that assert chart rendering with live responses and mocked failure scenarios.

## 4. Reply Service & Teams Integration Hardening
- Align ReplyService contracts with the latest Teams message extension schema; update DTOs and validation.
- Perform Stage environment reply loops to confirm multi-turn routing, budget guards, and audit logging.
- Capture diagnostics and resolve discrepancies surfaced by `StageFiveDiagnostics`.

## 5. Observability & Rollout Operations
- Complete log enrichment for budget/guardrail decisions and RAG retrieval metrics.
- Configure stage dashboards and alerts for latency, error rate, and token usage baselines.
- Prepare rollback playbook covering config flags, model provider fallback, and Teams app manifest reversion.

## 6. Documentation & Stakeholder Alignment
- Update requirements/design traceability matrix to reflect remaining gaps and coverage.
- Publish a weekly burndown with ownership per task, risks, and decision blockers.
- Schedule Stage 5 go/no-go review with stakeholders once smoke tests and telemetry integration are complete.

Each workstream can be staffed independently, enabling parallel progress while maintaining shared visibility through the updated documentation and instrumentation.
