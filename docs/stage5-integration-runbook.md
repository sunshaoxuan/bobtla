# 阶段 5 联调 Runbook

本 Runbook 面向 Stage 5 环境，记录密钥映射、Graph 权限开通与回帖冒烟验证，以及指标与审计的观测方法。所有步骤均基于 `src/TlaPlugin` 项目现有实现，避免与生产数据混用时可复制执行。

## 0. Stage 监控仪表盘与告警入口

- **实时指标总览**（Grafana）：<https://grafana.stage5.contoso.net/d/tla-stage5-alerts/telemetry-ops?orgId=1&var-env=Stage&var-service=TLAPlugin>
  - 面板涵盖翻译量、平均延迟、错误率、成本使用，已设置 5 分钟自动刷新。
  - 「Failure Breakdown」面板会在 `Stage5SmokeTests -- metrics` 输出含失败条目时触发红色高亮。
- **Azure Monitor 告警规则**：`Stage5-Metrics-Ingestion-Gap`（>10 分钟无数据），`Stage5-Reply-ErrorRate`（错误率 >5% 持续 15 分钟）。
- **最新冒烟尝试（2025-10-23 09:23 UTC）**：`Stage5SmokeTests -- secrets --verify-readiness`、`-- reply --use-live-graph`、`-- metrics` 与 `-- ready` 均因容器缺少 .NET SDK 返回 `command not found`，未产生新的日志或 Stage 就绪文件更新。请在具备 SDK 的 Stage 节点重跑并归档输出。【aaac2f†L1-L3】【89980a†L1-L2】【1d1e51†L1-L2】【80756a†L1-L3】
- **当前告警状态（2025-10-23 09:23 UTC）**：Stage 容器暂缺 .NET SDK，`Stage5SmokeTests` 无法在本地复跑，导致 `Stage5-Reply-ErrorRate` 告警仍保持待恢复状态且未生成新的 `/api/metrics` 采集记录。待 Stage 节点补齐 SDK 后重跑 `--use-live-graph` 与 `-- metrics`，并在 Grafana Dashboard 校验恢复情况。【aaac2f†L1-L3】【89980a†L1-L2】【1d1e51†L1-L2】

### 冒烟日志与可视化归档

- **日志汇总目录**：`artifacts/logs/2024-05-19/`（由 CI 任务 `stage5-smoke` 自动上传）。
  - `secrets-smoke-20240519.log` — 对应 `Stage5SmokeTests -- secrets` 输出，包含密钥解析详情与 Graph scope 校验结果。
  - `reply-obo-20240519.log` — 真实 OBO 冒烟日志，记录 403 失败栈以供权限排查。
  - `metrics-summary-20240519.json` — 指标摘要快照，可用于周报与 go/no-go 判据核查。
- **Grafana 关键截图**：<https://contoso.sharepoint.com/sites/stage5/Shared%20Documents/grafana/stage5-telemetry-20240519.png>
  - 截图更新时间：2024-05-19 18:45 UTC，由 @nora.zhu 提供。
  - 包含延迟、错误率双坐标及告警条幅，需在周报模板中引用。
- **Burndown/指标附件**：参照 `artifacts/burndown/stage5-burndown-20240520.csv` 与 `docs/stage5_task_plan.md` 中的 Mermaid 图表。

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

### Stage 环境变量配置

在加载 Stage 覆盖文件前，先让应用以 Stage 环境启动，确保 `appsettings.Stage.json` 中的 `UseHmacFallback=false` 等安全覆盖生效。可在本地或部署脚本中执行：

```bash
export DOTNET_ENVIRONMENT=Stage
dotnet run --project src/TlaPlugin --configuration Release
```

若通过部署管道运行，也可在发布命令追加 `--environment Stage`，或设置 `ASPNETCORE_ENVIRONMENT=Stage` 等等效变量。若未显式设置这些环境变量，.NET 会继续读取基础 `appsettings.json`，默认的 `UseHmacFallback=true` 会保持启用。

> 📦 **发布包检查** – Stage 配置文件需要随产物一起发布才能覆盖远端实例。执行一次发布并确认 `appsettings.Stage.json` 出现在输出目录中：

```bash
dotnet publish src/TlaPlugin/TlaPlugin.csproj -c Release -o ./artifacts/stage-publish
test -f ./artifacts/stage-publish/appsettings.Stage.json && echo "✔ Stage 覆盖文件已打包"
```

如未看到 ✔，请检查 `TlaPlugin.csproj` 中的 `<Content Include="appsettings.Stage.json">` 片段是否被保留，或在 CI/CD 中显式复制该文件。

4. **将 Key Vault 引用映射进配置** – 在 Stage 配置中引用 `src/TlaPlugin/appsettings.Stage.json` 模板，按租户替换其中的 `KeyVaultUri`、`ClientId`、`ClientSecretName` 占位符，并确认 `GraphScopes` 使用 `https://graph.microsoft.com/.default` 或 `https://graph.microsoft.com/<Permission>` 的资源限定格式，且 `UseHmacFallback=false` 已覆盖 OBO 场景。Stage 模板已内建 `.default/Chat.ReadWrite/ChatMessage.Send/ChannelMessage.Send/Group.ReadWrite.All/Team.ReadBasic.All` 作用域，可根据租户授权情况裁剪或扩展。【F:src/TlaPlugin/appsettings.Stage.json†L7-L21】【F:deploy/stage.appsettings.override.json†L5-L19】 作用域值需与 Azure AD 管理员已授权的范围一致，否则 OBO 将返回 `invalid_scope`。2025-10-23 已复核上述文件，确认 `UseHmacFallback=false` 与 GraphScopes 列表在 Stage 与部署 override 中保持一致。部署命令或冒烟脚本可通过 `--override appsettings.Stage.json` 注入该文件。若不同租户使用独立 Vault，可在 `Plugin.Security.TenantOverrides["<tenant>"].KeyVaultUri` 指向各自的 Key Vault。对真实 Key Vault，可使用 [Azure App Service Key Vault 引用](https://learn.microsoft.com/azure/app-service/app-service-key-vault-references) 或下方示例直接注入机密值：

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

   输出中的 ✔ 表示成功解析；如遇 ✘ 项目，按错误提示检查 Key Vault 引用或环境变量是否配置正确。Stage 模板默认启用 `Plugin.Security.FailOnSeedFallback=true`，因此脚本会在缺失机密时立即报错提醒补齐 Key Vault 映射。脚本会同步打印 `GraphScopes` 列表并标记是否符合资源限定格式，提醒现场工程师确认作用域与 Azure AD 授权一致，避免因无效 scope 造成 OBO 失败。建议将命令输出保存在联调记录中，作为 Stage 凭据映射已完成的佐证。【F:scripts/SmokeTests/Stage5SmokeTests/Program.cs†L82-L147】【F:src/TlaPlugin/appsettings.Stage.json†L1-L23】

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

   > Compose 插件中新增“多帖广播附加译文”开关：关闭时会沿用单帖附带模式，将附加语种拼接在同一消息与自适应卡片中；开启后会为每个附加语种额外发送一条 Graph 回复，并在主贴卡片中列出语言、模型与成本。Stage 联调时建议同时验证两种模式，确认 `Dispatches` 返回数组与 `/api/audit` 中的 `translations` 元素数量一致。

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

   **远程 API 模式** – 当 Stage 配置禁用 HMAC 回退 (`Plugin.Security.UseHmacFallback=false`) 或命令行提供 `--baseUrl` 时，脚本会自动直接访问已部署服务的 `/api/translate`、`/api/reply` 与 `/api/metrics`。可继续使用 `--use-remote-api` 在本地配置下手动触发，或通过 `--use-local-stub` 在 Stage 配置下强制回退到离线 Stub。示例命令：

   ```bash
   export USER_ASSERTION=$(az account get-access-token --resource api://tla-plugin --query accessToken -o tsv)
   dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- reply \
     --tenant contoso.onmicrosoft.com \
     --user stage-user \
     --thread 19:stage-thread@thread.tacv2 \
     --language ja \
     --text "Stage 5 远程 API 冒烟" \
     --baseUrl https://stage5.contoso.net \
     --assertion "$USER_ASSERTION"
   ```

   远程模式运行成功时会输出远端返回的翻译摘要、回帖结果、`/api/metrics` 与 `/api/audit` JSON 片段；如遇 401/403/429 等状态，脚本会打印 `21/22/23` 等退出码帮助定位鉴权或配额问题。与离线模式不同，此时不再显示本地 Graph 诊断信息，而是复用远程响应作为调试依据。若需要短暂关闭自动远程（例如在 Stage 配置下测试 Stub），可在命令末尾追加 `--use-local-stub`，脚本会提示已忽略自动触发条件。

   ```text
   [ModeDecider] 检测到 --use-remote-api 参数
   [Remote] /api/translate 调用成功:
     ModelId:   gpt4-stage
     Language:  ja-JP
     Latency:   123 ms
     CostUsd:   0.1500
     Response:  こちらは Stage5 の远程调用示例。

   [Remote] /api/reply 调用成功:
     MessageId: 19:stage-thread@thread.tacv2;messageid
     Status:    Created
     Language:  ja-JP
     Tone:      business

   使用指标摘要:
   {
     "overall": {
       "translations": 42,
       "totalCostUsd": 6.3,
       "averageLatencyMs": 310,
       "failures": []
     },
     "tenants": [
       {
         "tenantId": "contoso.onmicrosoft.com",
         "translations": 5,
         "totalCostUsd": 0.75,
         "averageLatencyMs": 280,
         "lastUpdated": "2024-03-12T02:11:34.123Z",
         "models": [
           { "modelId": "gpt4-stage", "translations": 5, "totalCostUsd": 0.75 }
         ],
         "failures": []
       }
     ]
   }

   审计记录样例:
  [
    {
      "tenantId": "contoso.onmicrosoft.com",
      "status": "Success",
      "language": "ja-JP",
      "toneApplied": "business"
    }
  ]
  ```

## 附录 B：Stage 5 go/no-go 判据（更新于 2024-05-20）

| 序号 | 判据 | 验收方式 | 最新状态 |
| --- | --- | --- | --- |
| G1 | Graph OBO 回帖在 Stage 环境成功执行 3 次，错误率 <5% | 查看 `reply-obo-*.log` 并在 Grafana `Failure Breakdown` 面板确认 | 阻塞：ISSUE-4821 未完成管理员同意，最近一次（2024-05-19）仍为 403。 |
| G2 | `openai-api-key` 新密钥在 Stage 与生产 Key Vault 中生效，成本监控与配额告警正常 | 对比 `secrets-smoke-*.log` 中的密钥有效期，与 Azure Monitor 告警仪表 | Stage Vault 已验证，通过生产审批后复测。 |
| G3 | Metrics API 与仪表盘刷新延迟 <5 分钟，并留存截图 | 运行 `Stage5SmokeTests -- metrics`，比对 Grafana 截图与 `metrics-summary` | 2024-05-19 截图确认达标，持续跟踪下一次冒烟。 |
| G4 | `stage-ready.json` 时间戳与 `StageFiveDiagnostics` 显示一致，Runbook/周报留存证据 | 在 `Stage5SmokeTests -- ready` 后比对 `artifacts/logs` 与诊断页面 | 初版流程已就绪，等待 CI 校验任务完成。 |

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

   冒烟显示 `Status: Created` 后，请立即调用一次 Metrics API 并核对 Stage 就绪文件，确保 `StageReadinessFilePath` 覆盖已经生效：

   ```bash
   curl -H "Authorization: Bearer <token>" https://stage5.contoso.net/api/metrics | jq '.'
   dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- metrics \
     --appsettings src/TlaPlugin/appsettings.json \
     --override src/TlaPlugin/appsettings.Stage.json \
     --baseUrl https://stage5.contoso.net
   ```

   第一条命令返回的 `tenants[].lastUpdated` 应接近当前时间，`metrics` 命令会在远程输出后追加「Stage 就绪文件检查」段落：当共享卷内存在 ISO-8601 时间戳时显示 `✔ 最近成功时间`，否则标记缺失或权限异常，便于排查 `FileStageReadinessStore` 是否将成功时间写入共享文件。若仍需人工复核，可继续执行 `tail -n 1 <shared-path>/stage-readiness.txt` 观察原始内容。【F:scripts/SmokeTests/Stage5SmokeTests/Program.cs†L375-L468】【F:src/TlaPlugin/Services/UsageMetricsService.cs†L22-L88】【F:src/TlaPlugin/Services/FileStageReadinessStore.cs†L12-L88】

   > 提示：启用真实模型时会按配置调用外部推理 API，请先确认 Key Vault 中的 `ApiKeySecretName` 已填充真实密钥，并评估当次调用可能产生的费用；如需同时验证 Graph，可同时追加 `--use-live-graph`，确保回帖链路、模型回退与审计记录均覆盖真实依赖。

   加上 `--use-live-model` 后，冒烟命令会通过 `LiveModelSmokeHarness` 逐个触发真实 Provider：缺失 Endpoint 或 API Key 时会打印 `⚠ 已触发回退 Provider`，成功调用的 Provider 会输出耗时、模型 ID 及裁剪后的译文预览，方便在不进入遥测平台的前提下确认成功与回退日志均已生成。【F:scripts/SmokeTests/Stage5SmokeTests/Program.cs†L318-L367】【F:scripts/SmokeTests/Stage5SmokeTests/LiveModelSmokeHarness.cs†L1-L167】

   模式无论真假都会打印 Graph 请求路径、Authorization 头与负载；在真实模式下还会追加 `StatusCode` 与响应 JSON，便于现场工程师对照 Graph 诊断信息定位权限或配额问题。若命令返回非零退出码，请根据控制台中输出的 Graph 错误消息与错误代码排查 Token、权限或配置缺失。【F:scripts/SmokeTests/Stage5SmokeTests/Program.cs†L261-L330】【F:scripts/SmokeTests/Stage5SmokeTests/Program.cs†L543-L565】

3. **网络与凭据准备** – 对接真实 Graph 前需确保 Stage 服务的反向代理或网络安全组允许访问 Graph 端点，并将 `SeedSecrets` 替换为 Key Vault 引用。若当前环境仍使用模拟 Token，可在 Stage 实现中扩展 `TokenBroker` 以获取 AAD 访问令牌，再复用上述命令验证 `ReplyService` 行为。

## 3. 指标与审计观测

1. **Metrics API** – Stage 环境部署后，可直接访问 `GET /api/metrics` 获取与冒烟输出一致的 `UsageMetricsReport`，字段包含 `overall` 汇总、各租户 `translations/cost/failures` 细分。可将结果接入 Grafana/Workbook 进行可视化。【F:src/TlaPlugin/Program.cs†L454-L474】【F:src/TlaPlugin/Services/UsageMetricsService.cs†L1-L120】

   ```bash
   dotnet run --project scripts/SmokeTests/Stage5SmokeTests -- metrics \
     --baseUrl https://stage5.contoso.net \
     --output ./artifacts/stage5-metrics.json
   ```

   命令会打印 `/api/metrics` 与 `/api/audit` 响应，并在 `--output` 指定路径落盘留痕，便于将成本与失败原因导入仪表盘或变更记录。【F:scripts/SmokeTests/Stage5SmokeTests/Program.cs†L332-L414】

2. **失败原因映射** – `UsageMetricsService` 会将预算、认证、模型、鉴权失败分别记录为「预算不足」「合规拒绝」「模型错误」「认证失败」，确保仪表盘上可直接洞察失败原因比例。【F:src/TlaPlugin/Services/UsageMetricsService.cs†L7-L74】

3. **审计导出** – `/api/audit` 返回由 `AuditLogger` 生成的 JSON 列表，包含租户、模型、成本及消息哈希。冒烟脚本输出的审计快照与线上格式一致，可作为调试模板或 SOX 留档。【F:src/TlaPlugin/Services/AuditLogger.cs†L1-L44】【F:scripts/SmokeTests/Stage5SmokeTests/Program.cs†L315-L327】

4. **Runbook 纳入集成计划** – 将本 Runbook 及命令示例纳入阶段 5 联调计划，执行顺序建议：

   1. 密钥映射并通过 `secrets` 检查；
   2. 完成 Graph 权限与管理员同意；
   3. 运行 `reply` 冒烟，保存指标/审计快照；
   4. 运行 `metrics` 命令或直接 `curl /api/metrics`，确认仪表盘展示的成本、失败原因与冒烟快照一致；
   5. 将脚本与命令记录在变更工单或自动化流水线中，便于回归与成本复核。

通过上述步骤，可在 Stage 环境保证 Key Vault、Graph OBO 与观测指标三项能力全部打通，为后续正式上线提供可重复的联调手册。

## 4. CI 密钥校验与告警

1. **CI 密钥有效期守护** – 流水线新增 `npm run ci:validate-secrets` 步骤，会执行 `scripts/ci/validate-secrets.sh` 调用 `Stage5SmokeTests -- secrets`。脚本会读取 `deploy/stage.appsettings.override.json`，逐一解析 `ConfigurableChatModelProvider.ApiKeySecretName` 对应的 Key Vault 机密：
   - 未解析到值或值为空直接失败；
   - Key Vault 返回的 `ExpiresOn` 在 7 天内（含已过期）同样判定失败；
   - 未配置到期时间将返回 ⚠️，提示后续在 Key Vault 中补齐。任何失败都会导致脚本以 `41` 退出码中止流水线，需轮换密钥后再触发部署。【F:scripts/SmokeTests/Stage5SmokeTests/Program.cs†L266-L343】【F:scripts/ci/validate-secrets.sh†L1-L15】【F:package.json†L11-L17】

   **响应策略**：CI 失败后，请在 Key Vault 中续期或新建密钥，更新 `ApiKeySecretName` 映射并记录到变更工单，随后重新执行 `npm run ci:validate-secrets` 直至返回 0，最后补充 Runbook 与 Stage 凭据台账中的过期时间。为降低误差，可提前 7 天安排轮换计划并在成功后更新 Grafana/AI 告警的到期阈值。

2. **应用日志告警模板** – `ConfigurableChatModelProvider` 统一输出 `Provider {ProviderId}`、`Operation`、`Duration`、`HasHttpClient` 等字段，可在 Application Insights 中使用以下 Kusto 查询建立告警规则：

   ```kusto
   traces
   | where timestamp > ago(5m)
   | where customDimensions["Category"] == "TlaPlugin.Providers.ConfigurableChatModelProvider"
   | extend provider = tostring(customDimensions["ProviderId"]), operation = tostring(customDimensions["Operation"])
   | summarize errors = countif(severityLevel >= 3), fallbacks = countif(message has "回退模型"), slowCalls = countif(todouble(customDimensions["Duration"]) > 15000)
       by provider
   | where errors > 0 or fallbacks > 3 or slowCalls > 0
   ```

   Grafana 可通过 Azure Monitor Data Source 复用同一查询，分别设置「错误次数 > 0」、「回退次数 > 3」、「耗时 > 15s」阈值，并将告警指向运行手册。若告警触发，应立即核对上一步的 CI 脚本与密钥到期时间，必要时临时切换到备用模型并在 Runbook 中记录处理过程。【F:src/TlaPlugin/Providers/ConfigurableChatModelProvider.cs†L71-L156】

## 5. Stage 就绪文件持久化

1. **替换配置占位符** – 在 `src/TlaPlugin/appsettings.Stage.json` 中，`Plugin.StageReadinessFilePath` 默认使用 `<shared-path>/stage-readiness.txt` 占位符。将 `<shared-path>` 更新为实际挂载到容器或 App Service 的共享卷路径，例如 Azure Files：

   ```bash
   sed -i 's#<shared-path>#/mnt/stage/shared#g' src/TlaPlugin/appsettings.Stage.json
   ```

   若通过环境变量覆盖，可继续使用 `TLA_Plugin__StageReadinessFilePath`，但建议与配置文件保持一致，便于审计。

2. **验证写入权限** – 使用部署身份在目标实例上执行一次读写探测，确认 `FileStageReadinessStore` 能够创建目录并写入 ISO-8601 时间戳。亦可在冒烟后通过 `Stage5SmokeTests -- metrics` 的「Stage 就绪文件检查」输出确认：

   ```bash
   readiness_file="/mnt/stage/shared/stage-readiness.txt"
   mkdir -p "$(dirname "$readiness_file")"
   echo "$(date -Iseconds)" | tee "$readiness_file"
   cat "$readiness_file"
   ```

   以上命令应成功输出时间戳，即表示路径可写且可读。请确保该卷在多个实例间共享，以便 Stage Ready 状态在横向扩展时保持一致。

3. **保留默认回退** – 如果未配置该项，插件会继续使用 `App_Data/stage-readiness.txt` 默认路径，可用于单实例或开发环境。Stage 环境推荐显式指向持久化卷，以避免 Pod/实例重启后阶段状态丢失。【F:src/TlaPlugin/Program.cs†L136-L147】【F:src/TlaPlugin/Services/FileStageReadinessStore.cs†L10-L69】
