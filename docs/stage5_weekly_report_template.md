# Stage 5 周报模版（最后更新：2024-05-21，由 @matt.hu 维护）

> 使用方式：复制本模版至当周周报文档，填充对应信息并在周会前 24 小时完成。所有链接需指向最新冒烟日志、Grafana 截图与 go/no-go 判据；可参考 `docs/stage5-integration-runbook.md` 中的归档表与验证清单。

## 1. 本周摘要
- **整体完成度**：88%（参考 `docs/stage5_task_plan.md` Burndown / 进度图表）
- **关键信息**：
  -
  -

## 2. 冒烟与验证结果（参见 Runbook 归档表）

| 冒烟脚本 / 工具 | 最新日志日期 | 链接 / 附件 | 状态 / 备注 | 责任人 |
| --- | --- | --- | --- | --- |
| `Stage5SmokeTests -- secrets --verify-readiness` | 2024-05-19 → __填写最新日期__ | `artifacts/logs/YYYY-MM-DD/secrets-smoke-YYYYMMDD.log` | 填写密钥解析结果、是否存在缺失机密或回退 | @matt.hu |
| `Stage5SmokeTests -- reply --use-live-graph` | 2024-05-19 → __填写最新日期__ | `artifacts/logs/YYYY-MM-DD/reply-obo-YYYYMMDD.log` | 填写成功次数、403/429 等错误与处理情况 | @liang.chen |
| `Stage5SmokeTests -- metrics` | 2024-05-19 → __填写最新日期__ | `artifacts/logs/YYYY-MM-DD/metrics-summary-YYYYMMDD.json` | 填写刷新耗时、异常指标、Grafana 对应面板 | @nora.zhu |
| `Stage5SmokeTests -- ready` / `StageFiveDiagnostics` | 2024-05-19 → __填写最新日期__ | `artifacts/logs/YYYY-MM-DD/stage-ready.json`、诊断截图 | 填写时间戳是否一致、若不一致需列举差异与修复计划 | @matt.hu |

## 3. Grafana / 监控截图
- 截图链接：<https://contoso.sharepoint.com/sites/stage5/Shared%20Documents/grafana/stage5-telemetry-YYYYMMDD.png>（本周默认参考 20240521）
- 截图更新时间：
- 截图提供人：
- 关键观察（请结合 `Stage5SmokeTests -- metrics` 输出说明）：
  - 延迟：
  - 错误率：
  - 告警：

## 4. go/no-go 判据对齐
| 判据 ID | 判据内容 | 验收证据 | 状态（示例） | 责任人 |
| --- | --- | --- | --- | --- |
| G1 | Graph OBO 回帖 3 次成功、错误率 <5% | `artifacts/logs/YYYY-MM-DD/reply-obo-*.log`、Grafana Trace 链接 | ⏳ 阻塞 — 待 STAGE5-SDK-INSTALL + `--use-live-graph` 重跑 | @liang.chen |
| G2 | `openai-api-key` 新密钥双环境生效 | Key Vault 版本截图、`Stage5SmokeTests -- secrets` 日志、REQ-9937 纪要 | ⏳ 待审 — 等待安全评审通过后更新 | @ariel.wang |
| G3 | Metrics 刷新 <5 分钟并留存截图 | `metrics-summary-YYYYMMDD.json`、Grafana 20240521 截图 | ⏳ 阻塞 — 待指标冒烟重跑 | @nora.zhu |
| G4 | `stage-ready.json` 与 Diagnostics 一致 | `stage-ready.json`、`StageFiveDiagnostics` 截图 | ⏳ 阻塞 — 待 `-- ready` 重跑及 CI 校验 | @matt.hu |

> 若新增判据，请在 `docs/stage5-integration-runbook.md` 同步更新 go/no-go 表，并补充验证方式。

## 5. 风险与阻塞更新
- **风险表更新时间**：2024-05-21 → __填写最新日期__（参考 `docs/stage5_task_plan.md`）
- 新增/关闭风险：
  -
- 高优先级阻塞项（请同步到 Runbook go/no-go 表）：
  -

## 6. 负责人完成度
| 负责人 | 关键任务 | 本周完成度 | 本周进展 | 下周计划 / 阻塞 |
| --- | --- | --- | --- | --- |
| @liang.chen | Graph 权限、OBO 冒烟 | 78% → __填写最新值__ | 填写管理员同意、SDK 安装进展与日志情况 | 重跑 `--use-live-graph`、上传 go/no-go 证据 |
| @ariel.wang | 密钥轮换、成本监控 | 70% → __填写最新值__ | 填写 Key Vault 切换、成本阈值设置 | 等待安全审核、更新 `stage-ready.json` |
| @nora.zhu | 仪表盘与缓存验证 | 84% → __填写最新值__ | 填写 Grafana 截图、缓存验证 | 跟进 `-- metrics` 重跑与 Failure Breakdown 高亮 |
| @matt.hu | 文档、go/no-go 对齐 | 76% → __填写最新值__ | 填写文档更新、日志归档情况 | 补充 `-- ready` 输出、推动周报发送 |

## 7. 下周里程碑与会议
- **Stakeholder 评审日期**：2024-05-23（议程与待决议事项见 `docs/stage5_task_plan.md` D1-D4）
- **预期交付**：
  - 
- **需协调的外部团队 / 依赖**：
  - 

## 8. 附录
- Burndown 数据：`artifacts/burndown/stage5-burndown-YYYYMMDD.csv`
- Workstream 进度图：`artifacts/progress/stage5-workstream-YYYYMMDD.json` / <https://contoso.sharepoint.com/sites/stage5/workstream-progress>
- 相关工单：ISSUE-4821、REQ-9937 等
- 历史周报存档：<https://contoso.sharepoint.com/sites/stage5/Shared%20Documents/weekly>
