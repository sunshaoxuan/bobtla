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

3. **配置访问策略或托管身份** – 为运行 Stage 服务的托管身份或应用注册授予目标 Key Vault 的 `get`/`list` Secret 权限。可通过 Azure Portal、`az keyvault set-policy` 或 Terraform 完成，确保 `Stage5SmokeTests` 的 `secrets` 命令能够直接读取远程机密。未授予权限时脚本会输出「无法访问远程 Key Vault」的提示，请根据错误信息补齐访问策略。

4. **将 Key Vault 引用映射进配置** – 在 Stage 配置中引用 `src/TlaPlugin/appsettings.Stage.json` 模板，按租户替换其中的 `KeyVaultUri`、`ClientId`、`ClientSecretName` 占位符，并确认 `GraphScopes` 使用 `https://graph.microsoft.com/.default` 或 `https://graph.microsoft.com/<Permission>` 的资源限定格式，且 `UseHmacFallback=false` 已覆盖 OBO 场景。作用域值需与 Azure AD 管理员已授权的范围一致，否则 OBO 将返回 `invalid_scope`。部署命令或冒烟脚本可通过 `--override appsettings.Stage.json` 注入该文件。若不同租户使用独立 Vault，可在 `Plugin.Security.TenantOverrides["<tenant>"].KeyVaultUri` 指向各自的 Key Vault。对真实 Key Vault，可使用 [Azure App Service Key Vault 引用](https://learn.microsoft.com/azure/app-service/app-service-key-vault-references) 或下方示例直接注入机密值：

   ```bash
   # 使用环境变量覆盖 SeedSecrets
   export TLA_Plugin__Security__SeedSecrets__tla-client-secret="<client-secret>"
   export TLA_Plugin__Security__SeedSecrets__openai-api-key="<openai-key>"
   export TLA_Plugin__Security__SeedSecrets__enterprise-graph-secret="<enterprise-secret>"
   ```

5. **运行密钥解析冒烟** – 利用新增的 `Stage5SmokeTests` 工具检查所有密钥是否可被 `KeyVaultSecretResolver` 获取，确认映射指向 Key Vault 中的真实条目：

   ```bash
   dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- secrets --appsettings src/TlaPlugin/appsettings.json --override appsettings.Stage.json
   ```

   输出中的 ✔ 表示成功解析；如遇 ✘ 项目，按错误提示检查 Key Vault 引用或环境变量是否配置正确。Stage 模板默认启用 `Plugin.Security.FailOnSeedFallback=true`，因此脚本会在缺失机密时立即报错提醒补齐 Key Vault 映射。脚本会同步打印 `GraphScopes` 列表并标记是否符合资源限定格式，提醒现场工程师确认作用域与 Azure AD 授权一致，避免因无效 scope 造成 OBO 失败。【F:scripts/SmokeTests/Stage5SmokeTests/Program.cs†L1-L308】【F:src/TlaPlugin/appsettings.Stage.json†L1-L23】

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

   > 提示：`TokenBroker` 在默认配置下继续使用 HMAC 令牌便于单元测试。若要打通真实 Graph OBO，请在 `Plugin.Security` 中将 `UseHmacFallback` 设置为 `false`，填充所需的 `GraphScopes`（推荐 `https://graph.microsoft.com/.default` 加上必要的精细化权限），并按租户覆盖 `ClientId`/`ClientSecretName`。冒烟脚本会记录成功调用时的 Authorization 头部，并输出作用域检查结果，便于比对 AAD 返回的访问令牌。

   `reply` 命令新增 `--assertion` 用于传入用户断言 (JWT)。在默认的 HMAC 回退模式下可以省略，脚本会生成带有 `aud/tid/sub` 字段的模拟 JWT 触发后续流程；若需要对比实际 OBO 行为，则必须提供真实的用户令牌。

   **HMAC 回退（默认）** – 可直接运行以下命令，脚本会在控制台提示已生成模拟断言：

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

   若希望显式传入模拟值，可将脚本输出的断言保存后重复使用，例如 `--assertion $(cat ./artifacts/hmac-user.jwt)`。

   **真实 Graph 调用** – 当 Stage 环境具备 AAD 访问令牌与网络出口时，需通过 `--assertion` 提供实际用户 JWT，并追加 `--use-live-graph` 触发真实 Graph 请求：

   ```bash
   export USER_ASSERTION=$(az account get-access-token --resource api://tla-plugin --query accessToken -o tsv)
   dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- reply \
     --tenant contoso.onmicrosoft.com \
     --user stage-user \
     --thread 19:stage-thread@thread.tacv2 \
     --channel 19:stage-channel \
     --language ja \
     --tone business \
     --text "Stage 5 手动联调验证" \
     --use-live-graph \
     --assertion "$USER_ASSERTION"
   ```

   **真实模型 Provider** – 在成本预算可接受的场景下，可追加 `--use-live-model` 以跳过 Stub 模型并复用配置中的真实 Provider 列表。该模式会使用 `ModelProviderFactory.CreateProviders()` 解析 Key Vault API Key、按顺序触发多模型回退，并保留预算、审计与失败统计逻辑，用于验证密钥接入与容灾链路：

   ```bash
   dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- reply \
     --tenant contoso.onmicrosoft.com \
     --user stage-user \
     --thread 19:stage-thread@thread.tacv2 \
     --channel 19:stage-channel \
     --language ja \
     --tone business \
     --text "Stage 5 手动联调验证" \
     --use-live-model
   ```

   成功运行后，控制台会打印一次 Graph 请求与指标快照，可用于变更记录留痕：

   ```text
   提示：未提供用户断言，已生成模拟 JWT 以驱动 HMAC 回退流程。
   [TeamsReplyClient] 调用成功:
     MessageId: smoke-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
     Status:    Created
     Language:  ja
   Graph 调用诊断:
     Mode:        stub
     CallCount:   1
     LastPath:    /teams/.../messages
     Authorization: Bearer eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0...
     Body:
   {...Graph 请求负载...}
   使用指标摘要:
   {
     "overall": { "translations": 1, "failures": 0 },
     "tenants": { "contoso.onmicrosoft.com": { "translations": 1 } }
   }
   审计记录样例:
   {
     "tenantId": "contoso.onmicrosoft.com",
     "status": "Success"
   }
   ```

   > 提示：启用真实模型时会按配置调用外部推理 API，请先确认 Key Vault 中的 `ApiKeySecretName` 已填充真实密钥，并评估当次调用可能产生的费用；如需同时验证 Graph，可同时追加 `--use-live-graph`，确保回帖链路、模型回退与审计记录均覆盖真实依赖。

   模式无论真假都会打印 Graph 请求路径、Authorization 头与负载；在真实模式下还会追加 `StatusCode` 与响应 JSON，便于现场工程师对照 Graph 诊断信息定位权限或配额问题。若命令返回非零退出码，请根据控制台中输出的 Graph 错误消息与错误代码排查 Token、权限或配置缺失。【F:scripts/SmokeTests/Stage5SmokeTests/Program.cs†L322-L382】【F:scripts/SmokeTests/Stage5SmokeTests/Program.cs†L563-L653】

3. **网络与凭据准备** – 对接真实 Graph 前需确保 Stage 服务的反向代理或网络安全组允许访问 Graph 端点，并将 `SeedSecrets` 替换为 Key Vault 引用。若当前环境仍使用模拟 Token，可在 Stage 实现中扩展 `TokenBroker` 以获取 AAD 访问令牌，再复用上述命令验证 `ReplyService` 行为。

## 3. 指标与审计观测

1. **Metrics API** – Stage 环境部署后，可直接访问 `GET /api/metrics` 获取与冒烟输出一致的 `UsageMetricsReport`，字段包含 `overall` 汇总、各租户 `translations/cost/failures` 细分。可将结果接入 Grafana/Workbook 进行可视化。【F:src/TlaPlugin/Program.cs†L454-L474】【F:src/TlaPlugin/Services/UsageMetricsService.cs†L1-L120】

   ```bash
   dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- metrics \
     --baseUrl https://stage5.contoso.net \
     --output ./artifacts/stage5-metrics.json
   ```

   命令会打印 `/api/metrics` 与 `/api/audit` 响应，并在 `--output` 指定路径落盘留痕，便于将成本与失败原因导入仪表盘或变更记录。【F:scripts/SmokeTests/Stage5SmokeTests/Program.cs†L570-L656】

2. **失败原因映射** – `UsageMetricsService` 会将预算、认证、模型、鉴权失败分别记录为「预算不足」「合规拒绝」「模型错误」「认证失败」，确保仪表盘上可直接洞察失败原因比例。【F:src/TlaPlugin/Services/UsageMetricsService.cs†L7-L74】

3. **审计导出** – `/api/audit` 返回由 `AuditLogger` 生成的 JSON 列表，包含租户、模型、成本及消息哈希。冒烟脚本输出的审计快照与线上格式一致，可作为调试模板或 SOX 留档。【F:src/TlaPlugin/Services/AuditLogger.cs†L1-L44】【F:scripts/SmokeTests/Stage5SmokeTests/Program.cs†L520-L548】

4. **Runbook 纳入集成计划** – 将本 Runbook 及命令示例纳入阶段 5 联调计划，执行顺序建议：

   1. 密钥映射并通过 `secrets` 检查；
   2. 完成 Graph 权限与管理员同意；
   3. 运行 `reply` 冒烟，保存指标/审计快照；
   4. 运行 `metrics` 命令或直接 `curl /api/metrics`，确认仪表盘展示的成本、失败原因与冒烟快照一致；
   5. 将脚本与命令记录在变更工单或自动化流水线中，便于回归与成本复核。

通过上述步骤，可在 Stage 环境保证 Key Vault、Graph OBO 与观测指标三项能力全部打通，为后续正式上线提供可重复的联调手册。
