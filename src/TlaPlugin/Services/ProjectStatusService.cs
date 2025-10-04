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
        var diagnostics = EvaluateStageFiveDiagnostics(_options);
        var stageFiveCompleted = diagnostics.StageReady;
        var stages = BuildStages(roadmap, stageFiveCompleted);
        var current = stages.FirstOrDefault(s => !s.Completed) ?? stages.Last();
        var overallPercent = CalculateOverallPercent(stages);
        var frontend = BuildFrontendProgress(stages, diagnostics);

        return new ProjectStatusSnapshot(current.Id, stages, NextSteps, overallPercent, frontend, diagnostics);
    }

    private static FrontendProgress BuildFrontendProgress(IReadOnlyList<StageStatus> stages, StageFiveDiagnostics diagnostics)
    {
        var percent = CalculateOverallPercent(stages);
        return new FrontendProgress(
            CompletionPercent: percent,
            DataPlaneReady: true,
            UiImplemented: true,
            IntegrationReady: diagnostics.StageReady);
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
        return EvaluateStageFiveDiagnostics(options).StageReady;
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

    private StageFiveDiagnostics EvaluateStageFiveDiagnostics(PluginOptions options)
    {
        var security = options.Security ?? new SecurityOptions();
        var hmacConfigured = !security.UseHmacFallback;
        var graphScopesValid = HasValidGraphScopes(security.GraphScopes);
        var smoke = CalculateSmokeDiagnostics();

        var stageReady = hmacConfigured && graphScopesValid && smoke.RecentSuccess;
        var failureReason = stageReady ? null : DetermineFailureReason(hmacConfigured, graphScopesValid, smoke);

        var hmacStatus = hmacConfigured
            ? "已禁用 HMAC 回退，OBO 令牌链路就绪"
            : "仍启用了 HMAC 回退，需要切换到 AAD/OBO";

        var validScopes = security.GraphScopes?.Count(scope => !string.IsNullOrWhiteSpace(scope)) ?? 0;
        var graphStatus = graphScopesValid
            ? $"已配置 {validScopes} 项 Graph 作用域"
            : "Graph 作用域缺失或格式不符合 https://graph.microsoft.com/ 前缀";

        var smokeStatus = smoke.Message;

        return new StageFiveDiagnostics(
            stageReady,
            hmacConfigured,
            hmacStatus,
            graphScopesValid,
            graphStatus,
            smoke.RecentSuccess,
            smokeStatus,
            smoke.LastSuccess,
            failureReason);
    }

    private SmokeTestDiagnostics CalculateSmokeDiagnostics()
    {
        var now = DateTimeOffset.UtcNow;

        DateTimeOffset? persisted = _stageReadinessStore.ReadLastSuccess();
        if (persisted.HasValue && persisted.Value > now)
        {
            persisted = now;
        }

        var report = _usageMetrics.GetReport();
        DateTimeOffset? metricsTimestamp = null;
        foreach (var snapshot in report.Tenants)
        {
            if (snapshot.Translations <= 0)
            {
                continue;
            }

            var candidate = snapshot.LastUpdated <= now ? snapshot.LastUpdated : now;
            if (!metricsTimestamp.HasValue || candidate > metricsTimestamp.Value)
            {
                metricsTimestamp = candidate;
            }
        }

        var lastSuccess = persisted;
        if (metricsTimestamp.HasValue && (!lastSuccess.HasValue || metricsTimestamp.Value > lastSuccess.Value))
        {
            lastSuccess = metricsTimestamp.Value;
        }

        var recent = lastSuccess.HasValue && now - lastSuccess.Value <= SmokeSuccessWindow;

        string message;
        if (recent && lastSuccess.HasValue)
        {
            message = $"冒烟 {DescribeDuration(now - lastSuccess.Value)} 前通过";
        }
        else if (lastSuccess.HasValue)
        {
            message = $"最近一次冒烟 {DescribeDuration(now - lastSuccess.Value)} 前，需要重新执行";
        }
        else if (report.Tenants.Count == 0)
        {
            message = "暂无租户调用记录";
        }
        else
        {
            message = "尚未记录冒烟成功";
        }

        return new SmokeTestDiagnostics(recent, message, lastSuccess);
    }

    private static string? DetermineFailureReason(bool hmacConfigured, bool graphScopesValid, SmokeTestDiagnostics smoke)
    {
        if (!hmacConfigured)
        {
            return "HMAC 回退仍在生效";
        }

        if (!graphScopesValid)
        {
            return "Graph 作用域配置不完整";
        }

        if (!smoke.RecentSuccess)
        {
            return smoke.Message;
        }

        return null;
    }

    private static string DescribeDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalMinutes < 1)
        {
            return "少于 1 分钟";
        }

        if (duration.TotalHours < 1)
        {
            return $"{Math.Round(duration.TotalMinutes)} 分钟";
        }

        if (duration.TotalDays < 1)
        {
            return $"{Math.Round(duration.TotalHours)} 小时";
        }

        return $"{Math.Round(duration.TotalDays)} 天";
    }

    private readonly record struct SmokeTestDiagnostics(bool RecentSuccess, string Message, DateTimeOffset? LastSuccess);
}
