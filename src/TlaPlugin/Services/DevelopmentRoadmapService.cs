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
            "stage0",
            "阶段 0：需求吸收",
            "解析需求说明书并锁定 .NET 技术栈与 SQLite 缓存策略。",
            true,
            new[]
            {
                "完成需求说明书要点整理",
                "确定 .NET 7 Minimal API 与 SQLite 架构",
                "梳理 Must/Should/Could 范围"
            },
            new[]
            {
                "文档校验",
                "需求覆盖性评审"
            }),
        new(
            "stage1",
            "阶段 1：服务编排",
            "实现翻译路由、术语合并、预算守卫与模型工厂。",
            true,
            new[]
            {
                "完成 TranslationRouter 与多模型回退",
                "实现 ToneTemplateService 与术语合并",
                "提供 AuditLogger 记录调用元数据"
            },
            new[]
            {
                "路由回退单测",
                "术语替换单测"
            }),
        new(
            "stage2",
            "阶段 2：Teams 适配",
            "为消息扩展提供 Adaptive Card 与错误提示卡片。",
            true,
            new[]
            {
                "完成 MessageExtensionHandler",
                "封装一键翻译并回复按钮",
                "实现多语额外翻译分节"
            },
            new[]
            {
                "Adaptive Card 断言",
                "错误卡片渲染测试"
            }),
        new(
            "stage3",
            "阶段 3：持久化与测试",
            "引入 SQLite 离线草稿存储与 xUnit 基线。",
            true,
            new[]
            {
                "OfflineDraftStore 支持 TTL 清理",
                "TokenBroker 与 KeyVaultSecretResolver 单测",
                "TranslationThrottle 并发控制"
            },
            new[]
            {
                "SQLite 持久化验证",
                "速率限制单测"
            }),
        new(
            "stage4",
            "阶段 4：合规加固",
            "覆盖区域、认证、禁译词与 PII 检测。",
            true,
            new[]
            {
                "ComplianceGateway 区域白名单",
                "认证凭据校验",
                "禁译词与 PII 正则扫描"
            },
            new[]
            {
                "禁译词阻断单测",
                "认证放行单测"
            }),
        new(
            "stage5",
            "阶段 5：性能护栏",
            "通过缓存与速率限制降低延迟与成本。",
            true,
            new[]
            {
                "TranslationCache 去重",
                "TranslationThrottle 并发配额",
                "BudgetGuard 成本追踪"
            },
            new[]
            {
                "缓存命中测试",
                "预算超限提示测试"
            }),
        new(
            "stage6",
            "阶段 6：密钥与 OBO",
            "提供 Key Vault 密钥缓存与 OBO 令牌交换。",
            true,
            new[]
            {
                "KeyVaultSecretResolver TTL 缓存",
                "TokenBroker 刷新策略",
                "模型调用前强制令牌校验"
            },
            new[]
            {
                "密钥缓存单测",
                "令牌刷新单测"
            }),
        new(
            "stage7",
            "阶段 7：多语广播",
            "一次请求内广播多种语言并汇总卡片。",
            true,
            new[]
            {
                "多语预算评估",
                "Adaptive Card 附加额外译文",
                "AuditLogger 记录多语条目"
            },
            new[]
            {
                "多语卡片测试",
                "多租户审计测试"
            }),
        new(
            "stage8",
            "阶段 8：多模型互联",
            "整合 OpenAI、Claude、Groq、OpenWebUI、Ollama。",
            true,
            new[]
            {
                "ConfigurableChatModelProvider 重试回退",
                "ModelProviderFactory 选择逻辑",
                "外部模型密钥解析"
            },
            new[]
            {
                "模型选择单测",
                "密钥解析测试"
            }),
        new(
            "stage9",
            "阶段 9：前端体验筹备",
            "完善前端数据平面并规划 Tab/联调。",
            false,
            new[]
            {
                "LocalizationCatalogService 替换界面文案",
                "ProjectStatusService 汇报阶段进度",
                "新增 /api/roadmap 提供阶段成果与测试摘要"
            },
            new[]
            {
                "本地化单测",
                "Roadmap 服务单测"
            })
    };

    private static readonly IReadOnlyList<RoadmapTest> TestCatalog = new List<RoadmapTest>
    {
        new("compliance", "ComplianceGatewayTests", "验证禁译词与区域白名单逻辑", true),
        new("router", "TranslationRouterTests", "覆盖多模型回退与多语广播", true),
        new("messageExtension", "MessageExtensionHandlerTests", "断言 Adaptive Card 与错误提示", true),
        new("roadmap", "DevelopmentRoadmapServiceTests", "验证阶段与测试摘要保持同步", true)
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
