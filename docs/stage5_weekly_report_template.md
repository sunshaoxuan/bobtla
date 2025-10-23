# Stage 5 周报模版（最后更新：2024-05-20，维护人：@matt.hu）

> 使用方式：复制本模版至当周周报文档，填充对应信息并在周会前 24 小时完成。所有链接需指向最新冒烟日志、Grafana 截图与 go/no-go 判据。

## 1. 本周摘要
- **整体完成度**：86%（参考 `docs/stage5_task_plan.md` Burndown 图表）
- **关键信息**：
  - 
  - 

## 2. 冒烟与验证结果
- `Stage5SmokeTests -- secrets`：日志链接（例如 `artifacts/logs/YYYY-MM-DD/secrets-smoke-YYYYMMDD.log`）
- `Stage5SmokeTests -- reply --use-live-graph`：日志链接（记录 403/成功次数、耗时）
- `Stage5SmokeTests -- metrics`：指标摘要 JSON 链接（例如 `metrics-summary-YYYYMMDD.json`）
- `Stage5SmokeTests -- ready`：`stage-ready.json` 时间戳与 `StageFiveDiagnostics` 截图/链接

## 3. Grafana / 监控截图
- 截图链接：<https://contoso.sharepoint.com/sites/stage5/Shared%20Documents/grafana/stage5-telemetry-YYYYMMDD.png>
- 截图更新时间：
- 截图提供人：
- 关键观察：
  - 延迟：
  - 错误率：
  - 告警：

## 4. go/no-go 判据对齐
| 判据 ID | 判据内容 | 验收证据 | 状态 | 责任人 |
| --- | --- | --- | --- | --- |
| G1 | Graph OBO 回帖 3 次成功、错误率 <5% |  |  |  |
| G2 | `openai-api-key` 新密钥双环境生效 |  |  |  |
| G3 | Metrics 刷新 <5 分钟并留存截图 |  |  |  |
| G4 | `stage-ready.json` 与 Diagnostics 一致 |  |  |  |

> 若新增判据，请在 `docs/stage5-integration-runbook.md` 同步更新附录 B，并补充验证方式。

## 5. 风险与阻塞更新
- **风险表更新时间**：YYYY-MM-DD
- 新增/关闭风险：
  - 
- 高优先级阻塞项：
  - 

## 6. 负责人完成度
| 负责人 | 关键任务 | 本周完成度 | 本周进展 | 下周计划 / 阻塞 |
| --- | --- | --- | --- | --- |
| @liang.chen | Graph 权限、OBO 冒烟 |  |  |  |
| @ariel.wang | 密钥轮换、成本监控 |  |  |  |
| @nora.zhu | 仪表盘与缓存验证 |  |  |  |
| @matt.hu | 文档、go/no-go 对齐 |  |  |  |

## 7. 下周里程碑与会议
- **Stakeholder 评审日期**：2024-05-23（议程见 `docs/stage5_task_plan.md`）
- **预期交付**：
  - 
- **需协调的外部团队 / 依赖**：
  - 

## 8. 附录
- Burndown 数据：`artifacts/burndown/stage5-burndown-YYYYMMDD.csv`
- 相关工单：ISSUE-4821、REQ-9937 等
- 历史周报存档：<https://contoso.sharepoint.com/sites/stage5/Shared%20Documents/weekly>
