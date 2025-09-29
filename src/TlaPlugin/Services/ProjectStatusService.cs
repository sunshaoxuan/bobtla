using System;
using System.Collections.Generic;
using System.Linq;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 提供开发进度快照以加速前端规划的服务。
/// </summary>
public class ProjectStatusService
{
    private static readonly IReadOnlyList<StageStatus> Stages = new List<StageStatus>
    {
        new("stage0", "阶段 0：需求吸收", "完成需求说明书解读与技术选型。", true),
        new("stage1", "阶段 1：服务编排", "交付多模型路由、预算守卫与术语融合能力。", true),
        new("stage2", "阶段 2：Teams 适配", "实现消息扩展与 Adaptive Card 响应。", true),
        new("stage3", "阶段 3：持久化与测试", "落地 SQLite 草稿存储并建立 xUnit 覆盖。", true),
        new("stage4", "阶段 4：合规加固", "引入区域、认证与禁译语校验。", true),
        new("stage5", "阶段 5：性能护栏", "提供翻译缓存与速率控制。", true),
        new("stage6", "阶段 6：密钥与 OBO", "完成 Key Vault 机密缓存与 OBO 代理。", true),
        new("stage7", "阶段 7：多语广播", "交付额外语言批量翻译与卡片呈现。", true),
        new("stage8", "阶段 8：多模型互联", "通过 ConfigurableChatModelProvider 统一外部 API。", true),
        new("stage9", "阶段 9：前端体验", "构建 Teams Tab、设置界面与联调测试。", false)
    };

    private static readonly IReadOnlyList<string> NextSteps = new List<string>
    {
        "完成真实模型联通与延迟监控",
        "替换 Key Vault/OBO 模拟为生产 SDK",
        "实现 Teams Tab 设置页与术语管理前端",
        "启动前后端联调与端到端测试"
    };

    /// <summary>
    /// 返回当前的进度快照。
    /// </summary>
    public ProjectStatusSnapshot GetSnapshot()
    {
        var current = Stages.Last(s => s.Completed);
        var overallPercent = CalculateOverallPercent();
        var frontend = new FrontendProgress(
            CompletionPercent: 0,
            DataPlaneReady: true,
            UiImplemented: false,
            IntegrationReady: false);

        return new ProjectStatusSnapshot(current.Id, Stages, NextSteps, overallPercent, frontend);
    }

    private static int CalculateOverallPercent()
    {
        var completed = Stages.Count(stage => stage.Completed);
        var percent = (double)completed / Stages.Count * 100;
        return (int)Math.Round(percent, MidpointRounding.AwayFromZero);
    }
}
