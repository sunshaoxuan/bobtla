using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 開発進捗スナップショットを提供するサービス。
/// </summary>
public class ProjectStatusService
{
    private const string StageFiveId = "phase5";
    private static readonly IReadOnlyList<(string Id, string Name, string Description, bool Completed)> StageBlueprint = new List<(string, string, string, bool)>
    {
        ("phase1", "阶段 1：平台基线", "完成需求解读、服务编排与消息扩展骨架。", true),
        ("phase2", "阶段 2：安全与合规", "交付合规网关、预算守卫与密钥/OBO 管理。", true),
        ("phase3", "阶段 3：性能与可观测", "完成缓存、速率控制与多模型互联。", true),
        ("phase4", "阶段 4：前端体验", "实现 Teams Tab 仪表盘与本地化界面。", true),
        (StageFiveId, "阶段 5：上线准备", "拉通真实模型、联调并准备发布清单。", false)
    };

    private static readonly IReadOnlyList<string> NextSteps = new List<string>
    {
        "完成密钥映射 Runbook 并固化凭据分发",
        "安排 Graph/OBO 冒烟测试验证令牌链路",
        "切换至真实模型并执行发布 SmokeTest"
    };

    private static readonly TimeSpan SmokeSuccessWindow = TimeSpan.FromHours(24);

    private readonly PluginOptions _options;
    private readonly UsageMetricsService _usageMetrics;
    private readonly DevelopmentRoadmapService _roadmapService;
    private readonly IStageReadinessStore _stageReadinessStore;

    public ProjectStatusService(
        IOptions<PluginOptions> options,
        UsageMetricsService usageMetrics,
        DevelopmentRoadmapService roadmapService,
        IStageReadinessStore stageReadinessStore)
    {
        _options = options?.Value ?? new PluginOptions();
        _usageMetrics = usageMetrics ?? throw new ArgumentNullException(nameof(usageMetrics));
        _roadmapService = roadmapService ?? throw new ArgumentNullException(nameof(roadmapService));
        _stageReadinessStore = stageReadinessStore ?? throw new ArgumentNullException(nameof(stageReadinessStore));
    }

    /// <summary>
    /// 返回当前的进度快照。
    /// </summary>
    public ProjectStatusSnapshot GetSnapshot()
    {
        var roadmap = _roadmapService.GetRoadmap();
        var stageFiveCompleted = IsStageFiveCompleted(_options);
        var stages = BuildStages(roadmap, stageFiveCompleted);
        var current = stages.FirstOrDefault(s => !s.Completed) ?? stages.Last();
        var overallPercent = CalculateOverallPercent(stages);
        var frontend = BuildFrontendProgress(stages, stageFiveCompleted);

        return new ProjectStatusSnapshot(current.Id, stages, NextSteps, overallPercent, frontend);
    }

    private static FrontendProgress BuildFrontendProgress(IReadOnlyList<StageStatus> stages, bool stageFiveCompleted)
    {
        var percent = CalculateOverallPercent(stages);
        return new FrontendProgress(
            CompletionPercent: percent,
            DataPlaneReady: true,
            UiImplemented: true,
            IntegrationReady: stageFiveCompleted);
    }

    private static int CalculateOverallPercent(IReadOnlyList<StageStatus> stages)
    {
        if (stages.Count == 0)
        {
            return 0;
        }

        var completed = stages.Count(stage => stage.Completed);
        var percent = (double)completed / stages.Count * 100;
        return (int)Math.Round(percent, MidpointRounding.AwayFromZero);
    }

    private static IReadOnlyList<StageStatus> BuildStages(DevelopmentRoadmap roadmap, bool stageFiveCompleted)
    {
        if (roadmap?.Stages is null || roadmap.Stages.Count == 0)
        {
            return StageBlueprint
                .Select(stage => new StageStatus(
                    stage.Id,
                    stage.Name,
                    stage.Description,
                    stage.Id == StageFiveId ? stageFiveCompleted : stage.Completed))
                .ToList();
        }

        var overrides = roadmap.Stages
            .ToDictionary(stage => stage.Id, stage => stage, StringComparer.OrdinalIgnoreCase);

        return StageBlueprint
            .Select(template =>
            {
                var hasOverride = overrides.TryGetValue(template.Id, out var stageOverride);
                var completed = template.Id == StageFiveId
                    ? stageFiveCompleted
                    : (hasOverride ? stageOverride!.Completed : template.Completed);
                var name = hasOverride ? stageOverride!.Name : template.Name;
                var description = hasOverride ? stageOverride!.Objective : template.Description;
                return new StageStatus(template.Id, name, description, completed);
            })
            .ToList();
    }

    private bool IsStageFiveCompleted(PluginOptions options)
    {
        var security = options.Security ?? new SecurityOptions();
        if (security.UseHmacFallback)
        {
            return false;
        }

        if (!HasValidGraphScopes(security.GraphScopes))
        {
            return false;
        }

        return HasRecentSmokeSuccess();
    }

    private static bool HasValidGraphScopes(IList<string> scopes)
    {
        if (scopes is null || scopes.Count == 0)
        {
            return false;
        }

        const string Prefix = "https://graph.microsoft.com/";
        return scopes.All(scope =>
            !string.IsNullOrWhiteSpace(scope)
            && scope.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            && scope.Length > Prefix.Length);
    }

    private bool HasRecentSmokeSuccess()
    {
        var now = DateTimeOffset.UtcNow;
        var persisted = _stageReadinessStore.ReadLastSuccess();
        if (persisted.HasValue)
        {
            var timestamp = persisted.Value <= now ? persisted.Value : now;
            if (now - timestamp <= SmokeSuccessWindow)
            {
                return true;
            }
        }

        var report = _usageMetrics.GetReport();
        if (report.Tenants.Count == 0)
        {
            return false;
        }

        return report.Tenants.Any(snapshot =>
            snapshot.Translations > 0
            && now - snapshot.LastUpdated <= SmokeSuccessWindow);
    }
}
