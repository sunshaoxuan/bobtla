# 阶段 5 联调 Runbook

本 Runbook 面向 Stage 5 环境，记录密钥映射、Graph 权限开通与回帖冒烟验证，以及指标与审计的观测方法。所有步骤均基于 `src/TlaPlugin` 项目现有实现，避免与生产数据混用时可复制执行。

## 1. Key Vault 密钥映射与验证

1. **确认需要的机密名称** – `appsettings.json` 中 `Plugin.Security` 与各模型提供方引用的密钥如下：

   | 配置项 | Key Vault Secret Name | 说明 |
   | --- | --- | --- |
   | `Plugin.Security.ClientSecretName` | `tla-client-secret` | 用于 OBO Client 凭据，租户覆盖项可复用。 |
   | `Plugin.Providers[0].ApiKeySecretName` | `openai-api-key` | OpenAI 主模型访问密钥。 |
   | `Plugin.Security.TenantOverrides["enterprise.onmicrosoft.com"].ClientSecretName` | `enterprise-graph-secret` | Enterprise 租户专属 Graph 客户端机密。 |

2. **在部署租户 Key Vault 中创建/更新机密**（示例使用 Azure CLI，替换 `<vault>` 与 `<secret>` 值）：

   ```bash
   az keyvault secret set --vault-name <vault> --name tla-client-secret --value <client-secret>
   az keyvault secret set --vault-name <vault> --name openai-api-key --value <openai-key>
   az keyvault secret set --vault-name <vault> --name enterprise-graph-secret --value <enterprise-secret>
   ```

3. **将 Key Vault 引用映射进配置** – 在 Stage 配置（例如 `appsettings.Stage.json`）或部署环境变量中，设置以下键值对，让 `KeyVaultSecretResolver` 能够解析到实际密钥。对真实 Key Vault，可使用 [Azure App Service Key Vault 引用](https://learn.microsoft.com/azure/app-service/app-service-key-vault-references) 或下方示例直接注入机密值：

   ```bash
   # 使用环境变量覆盖 SeedSecrets
   export TLA_Plugin__Security__SeedSecrets__tla-client-secret="<client-secret>"
   export TLA_Plugin__Security__SeedSecrets__openai-api-key="<openai-key>"
   export TLA_Plugin__Security__SeedSecrets__enterprise-graph-secret="<enterprise-secret>"
   ```

4. **运行密钥解析冒烟** – 利用新增的 `Stage5SmokeTests` 工具检查所有密钥是否可被 `KeyVaultSecretResolver` 获取，确认映射指向 Key Vault 中的真实条目：

   ```bash
   dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- secrets --appsettings src/TlaPlugin/appsettings.json --override appsettings.Stage.json
   ```

   输出中的 ✔ 表示成功解析；如遇 ✘ 项目，按错误提示检查 Key Vault 引用或环境变量是否配置正确。【F:scripts/SmokeTests/Stage5SmokeTests/Program.cs†L1-L212】

## 2. Graph 权限与 ReplyService 冒烟

1. **为目标租户开通 Graph 权限** – 确保 Azure AD 应用已获以下委派权限并完成管理员同意：`Chat.ReadWrite`, `ChatMessage.Send`, `ChannelMessage.Send`, `Group.ReadWrite.All`。可通过 Azure Portal 或 CLI：

   ```bash
   az ad app permission add --id <appId> --api 00000003-0000-0000-c000-000000000000 --api-permissions Chat.ReadWrite delegated
   az ad app permission grant --id <appId> --api 00000003-0000-0000-c000-000000000000
   ```

2. **执行 OBO + Teams 回帖冒烟** – 该工具会：

   - 读取配置并调用 `TokenBroker.ExchangeOnBehalfOfAsync`；
   - 使用内置 `TranslationRouter` 生成译文，触发审计与成本指标；
   - 通过 `TeamsReplyClient.SendReplyAsync` 发起 Graph 请求（可模拟或直连）；
   - 输出 `/api/metrics` 中同源的数据结构与审计日志样例。

   **默认（模拟 Graph）** – 适合验证 Token 链路与审计指标而不触发真实 Graph 调用：

   ```bash
   dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- reply \
     --tenant contoso.onmicrosoft.com \
     --user stage-user \
     --thread 19:stage-thread@thread.tacv2 \
     --channel 19:stage-channel \
     --language ja \
     --tone business \
     --text "Stage 5 手动联调验证"
   ```

   **真实 Graph 调用** – 当 Stage 环境已具备可用的 AAD Token 与网络出口时，追加 `--use-live-graph`。脚本将读取 `Plugin.Security.GraphBaseUrl`/`GraphTimeout`/`GraphProxy` 配置实例化 `HttpClient`，并在失败时按 2 秒间隔重试最多三次：

   ```bash
   dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- reply \
     --tenant contoso.onmicrosoft.com \
     --user stage-user \
     --thread 19:stage-thread@thread.tacv2 \
     --channel 19:stage-channel \
     --language ja \
     --tone business \
     --text "Stage 5 手动联调验证" \
     --use-live-graph
   ```

   模式无论真假都会打印 Graph 请求路径、Authorization 头与负载；在真实模式下还会追加 `StatusCode` 与响应 JSON，便于现场工程师对照 Graph 诊断信息定位权限或配额问题。若命令返回非零退出码，请根据控制台中输出的 Graph 错误消息与错误代码排查 Token、权限或配置缺失。【F:scripts/SmokeTests/Stage5SmokeTests/Program.cs†L322-L382】【F:scripts/SmokeTests/Stage5SmokeTests/Program.cs†L563-L653】

3. **网络与凭据准备** – 对接真实 Graph 前需确保 Stage 服务的反向代理或网络安全组允许访问 Graph 端点，并将 `SeedSecrets` 替换为 Key Vault 引用。若当前环境仍使用模拟 Token，可在 Stage 实现中扩展 `TokenBroker` 以获取 AAD 访问令牌，再复用上述命令验证 `ReplyService` 行为。

## 3. 指标与审计观测

1. **Metrics API** – Stage 环境部署后，可直接访问 `GET /api/metrics` 获取与冒烟输出一致的 `UsageMetricsReport`，字段包含 `overall` 汇总、各租户 `translations/cost/failures` 细分。可将结果接入 Grafana/Workbook 进行可视化。【F:src/TlaPlugin/Program.cs†L454-L474】【F:src/TlaPlugin/Services/UsageMetricsService.cs†L1-L120】

   ```bash
   dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- metrics \
     --baseUrl https://stage5.contoso.net \
     --output ./artifacts/stage5-metrics.json
   ```

   命令会打印 `/api/metrics` 与 `/api/audit` 响应，并在 `--output` 指定路径落盘留痕，便于将成本与失败原因导入仪表盘或变更记录。【F:scripts/SmokeTests/Stage5SmokeTests/Program.cs†L36-L278】

2. **失败原因映射** – `UsageMetricsService` 会将预算、认证、模型、鉴权失败分别记录为「预算不足」「合规拒绝」「模型错误」「认证失败」，确保仪表盘上可直接洞察失败原因比例。【F:src/TlaPlugin/Services/UsageMetricsService.cs†L7-L74】

3. **审计导出** – `/api/audit` 返回由 `AuditLogger` 生成的 JSON 列表，包含租户、模型、成本及消息哈希。冒烟脚本输出的审计快照与线上格式一致，可作为调试模板或 SOX 留档。【F:src/TlaPlugin/Services/AuditLogger.cs†L1-L44】【F:scripts/SmokeTests/Stage5SmokeTests/Program.cs†L314-L332】

4. **Runbook 纳入集成计划** – 将本 Runbook 及命令示例纳入阶段 5 联调计划，执行顺序建议：

   1. 密钥映射并通过 `secrets` 检查；
   2. 完成 Graph 权限与管理员同意；
   3. 运行 `reply` 冒烟，保存指标/审计快照；
   4. 运行 `metrics` 命令或直接 `curl /api/metrics`，确认仪表盘展示的成本、失败原因与冒烟快照一致；
   5. 将脚本与命令记录在变更工单或自动化流水线中，便于回归与成本复核。

通过上述步骤，可在 Stage 环境保证 Key Vault、Graph OBO 与观测指标三项能力全部打通，为后续正式上线提供可重复的联调手册。
