# Teams Language Assistant (TLA) 插件实现摘要

## 项目总览
Teams Language Assistant (TLA) 旨在为 Microsoft Teams 提供自动语言检测、术语优先和一键回贴体验，以满足跨语种团队的实时沟通需求。产品目标、指标及阶段性路线图均依据《BOBTLA 需求说明书》所述的 MVP→Beta→GA 演进策略设计。[【F:docs/BOBTLA需求说明书.txt†L1-L134】](docs/BOBTLA需求说明书.txt)

## 架构与代码结构
本仓库提供可在自托管环境运行的消息扩展参考实现，核心模块如下：

| 模块 | 目录 | 说明 |
| --- | --- | --- |
| 配置中心 | `src/config.js` | 记录区域策略、模型候选、预算与安全基线。 |
| 模型抽象 | `src/models/modelProvider.js` | 抽象多模型提供方并提供 Mock Provider 以便单元测试验证回退逻辑。 |
| 服务层 | `src/services/` | 包含语言检测、术语库合并、预算守卫、审计日志、离线草稿与翻译路由等组件。 |
| Teams 适配 | `src/teams/messageExtension.js` | 封装消息扩展命令处理、异常反馈与卡片生成。 |
| 参考服务 | `src/server.js` | 构建具备默认依赖的 HTTP 端点，演示在 Teams 回调中的编排流程。 |
| 测试 | `tests/` | 采用 Node.js 原生测试框架覆盖路由回退、预算、术语库、离线草稿及错误处理。 |

服务层遵循需求中的 Must-have 项：自动检测源语言、术语库三层级合并、多模型回退、预算控制与审计追溯，并生成 Adaptive Card 以避免用户跳出对话上下文。[【F:docs/BOBTLA需求说明书.txt†L40-L115】【F:src/services/translationRouter.js†L1-L107】](docs/BOBTLA需求说明书.txt)

## 开发阶段规划
| 阶段 | 目标 | 进度 | 结果摘要 |
| --- | --- | --- | --- |
| 阶段 0：需求吸收 | 解析需求说明书、整理 Must/MVP 功能、识别测试维度 | ✅ 完成 | 提炼 KPI、术语库策略、多模型回退及安全要求，形成配置基线。 |
| 阶段 1：核心编排 | 实现路由器、预算守卫、审计、离线草稿与消息扩展适配层 | ✅ 完成 | `TranslationRouter` 支持回退与术语覆盖；`MessageExtensionHandler` 输出 Adaptive Card 并处理错误。 |
| 阶段 2：测试与文档 | 编写 Node 原生单测、生成开发摘要、整理下一步计划 | ✅ 完成 | 7 个核心场景全部通过；README 汇总阶段成果、测试结果与后续计划。 |

## 现行阶段
当前处于 **阶段 2：测试与文档封版**。核心功能均以 Mock Provider 完成自洽闭环，可用于与真实模型 SDK 集成前的接口验证。

## 下一步计划
1. **集成真实模型 SDK**：替换 `MockModelProvider`，根据租户配置动态加载 Azure OpenAI、Anthropic 等提供方；补充网络调用重试与遥测指标。
2. **引入合规网关**：实现 PII/PHI 检测与禁译库校验，联动 Azure Key Vault 完成密钥托管与 OBO 授权流。[【F:docs/BOBTLA需求说明书.txt†L115-L189】](docs/BOBTLA需求说明书.txt)
3. **完善前端体验**：实现群组多语广播、术语冲突提示与人工复核模式 UI，覆盖桌面与移动端兼容性测试。
4. **自动化流水线**：补充 lint、覆盖率与合规扫描，构建灰度发布与回滚脚本以支持 Beta→GA 迁移。

## 阶段成果与测试记录
- **翻译路由与术语库**：多模型回退逻辑在首选模型失败时自动切换备用模型，并按租户层级应用术语替换。[【F:src/services/translationRouter.js†L1-L107】【F:tests/translationRouter.test.js†L1-L54】](src/services/translationRouter.js)
- **预算与审计合规**：每日预算耗尽时立即阻断请求，审计日志以指纹存储原文，满足不可逆留痕要求。[【F:src/services/budgetGuard.js†L1-L23】【F:src/services/auditLogger.js†L1-L41】](src/services/budgetGuard.js)
- **离线草稿与卡片输出**：保存离线草稿并在恢复后重试翻译；Adaptive Card 模板遵循消息扩展不跳转原则。[【F:src/services/offlineDraftStore.js†L1-L37】【F:src/services/translationPipeline.js†L1-L60】](src/services/offlineDraftStore.js)
- **消息扩展错误处理**：针对预算超限与翻译异常分别生成提示卡片，便于用户快速决策。[【F:src/teams/messageExtension.js†L1-L63】【F:tests/messageExtension.test.js†L1-L60】](src/teams/messageExtension.js)

### 测试情况
| 测试集 | 说明 | 结果 |
| --- | --- | --- |
| `npm test` | Node.js 原生单元测试，覆盖路由回退、预算、术语库、离线草稿与错误处理 | ✅ 通过 |

最新测试命令与输出详见文末“Testing”章节。
