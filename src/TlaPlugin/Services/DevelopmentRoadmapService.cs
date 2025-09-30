using System.Collections.Generic;
using System.Linq;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 段階別の成果とテスト計画を公開するサービス。
/// </summary>
public class DevelopmentRoadmapService
{
    private static readonly IReadOnlyList<RoadmapStage> StagePlan = new List<RoadmapStage>
    {
        new(
            "phase1",
            "阶段 1：平台基线",
            "完成需求吸收、Minimal API 与消息扩展骨架。",
            true,
            new[]
            {
                "梳理 BOBTLA 需求说明书",
                "搭建 .NET Minimal API + SQLite 基础设施",
                "交付消息扩展与 Adaptive Card 主流程"
            },
            new[]
            {
                "需求对齐评审",
                "消息扩展冒烟测试"
            }),
        new(
            "phase2",
            "阶段 2：安全与合规",
            "上线合规网关、预算守卫与密钥/OBO 管理。",
            true,
            new[]
            {
                "ComplianceGateway 区域与禁译策略",
                "BudgetGuard 成本追踪",
                "KeyVaultSecretResolver 与 TokenBroker 一体化"
            },
            new[]
            {
                "禁译词阻断测试",
                "令牌刷新测试"
            }),
        new(
            "phase3",
            "阶段 3：性能与可观测",
            "优化缓存、速率与多模型互联并沉淀指标。",
            true,
            new[]
            {
                "TranslationCache 去重",
                "TranslationThrottle 并发与速率限制",
                "UsageMetricsService 聚合监控"
            },
            new[]
            {
                "缓存命中测试",
                "指标聚合测试"
            }),
        new(
            "phase4",
            "阶段 4：前端体验",
            "构建 Teams 设置页与前端仪表盘，统一本地化。",
            false,
            new[]
            {
                "LocalizationCatalogService 推送日文默认 UI",
                "/api/status 与 /api/roadmap 汇总阶段数据",
                "新增 src/webapp 仪表盘页面与视图模型"
            },
            new[]
            {
                "本地化单测",
                "前端视图模型测试"
            }),
        new(
            "phase5",
            "阶段 5：上线准备",
            "串联真实模型、端到端联调并准备发布验收。",
            false,
            new[]
            {
                "接入生产模型与监控",
                "前后端联调脚本",
                "发布清单与回滚预案"
            },
            new[]
            {
                "联调回归测试",
                "性能基准测试"
            })
    };

    private static readonly IReadOnlyList<RoadmapTest> TestCatalog = new List<RoadmapTest>
    {
        new("compliance", "ComplianceGatewayTests", "验证禁译词与区域白名单逻辑", true),
        new("router", "TranslationRouterTests", "覆盖多模型回退与多语广播", true),
        new("messageExtension", "MessageExtensionHandlerTests", "断言 Adaptive Card 与错误提示", true),
        new("roadmap", "DevelopmentRoadmapServiceTests", "验证阶段与测试摘要保持同步", true),
        new("dashboard", "dashboardViewModel.test.js", "验证仪表盘阶段聚合与进度计算", true)
    };

    /// <summary>
    /// ロードマップ全体を返却する。
    /// </summary>
    public DevelopmentRoadmap GetRoadmap()
    {
        var activeStage = StagePlan.FirstOrDefault(stage => !stage.Completed)?.Id ?? StagePlan.Last().Id;
        return new DevelopmentRoadmap(StagePlan, TestCatalog, activeStage);
    }
}
