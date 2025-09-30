using System;
using System.Collections.Generic;
using System.Linq;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 開発進捗スナップショットを提供するサービス。
/// </summary>
public class ProjectStatusService
{
    private static readonly IReadOnlyList<StageStatus> Stages = new List<StageStatus>
    {
        new("phase1", "阶段 1：平台基线", "完成需求解读、服务编排与消息扩展骨架。", true),
        new("phase2", "阶段 2：安全与合规", "交付合规网关、预算守卫与密钥/OBO 管理。", true),
        new("phase3", "阶段 3：性能与可观测", "完成缓存、速率控制与多模型互联。", true),
        new("phase4", "阶段 4：前端体验", "实现 Teams Tab 仪表盘与本地化界面。", false),
        new("phase5", "阶段 5：上线准备", "拉通真实模型、联调并准备发布清单。", false)
    };

    private static readonly IReadOnlyList<string> NextSteps = new List<string>
    {
        "完善设置页组件并补全前端校验",
        "串联状态/路标 API 与实时刷新机制",
        "安排跨团队联调与上线验收"
    };

    /// <summary>
    /// 返回当前的进度快照。
    /// </summary>
    public ProjectStatusSnapshot GetSnapshot()
    {
        var current = Stages.FirstOrDefault(s => !s.Completed) ?? Stages.Last();
        var overallPercent = CalculateOverallPercent();
        var frontend = new FrontendProgress(
            CompletionPercent: 55,
            DataPlaneReady: true,
            UiImplemented: true,
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
