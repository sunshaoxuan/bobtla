using System;

namespace TlaPlugin.Models;

/// <summary>
/// 项目整体进度的快照模型。
/// </summary>
public record ProjectStatusSnapshot(
    string CurrentStageId,
    IReadOnlyList<StageStatus> Stages,
    IReadOnlyList<string> NextSteps,
    int OverallCompletionPercent,
    FrontendProgress Frontend,
    StageFiveDiagnostics StageFiveDiagnostics);

/// <summary>
/// 单个开发阶段的状态信息。
/// </summary>
public record StageStatus(
    string Id,
    string Name,
    string Description,
    bool Completed);

/// <summary>
/// 前端准备度的详细指标。
/// </summary>
public record FrontendProgress(
    int CompletionPercent,
    bool DataPlaneReady,
    bool UiImplemented,
    bool IntegrationReady);

/// <summary>
/// 阶段五（上线准备）的诊断明细，帮助定位阻塞原因。
/// </summary>
public record StageFiveDiagnostics(
    bool StageReady,
    bool HmacConfigured,
    string HmacStatus,
    bool GraphScopesValid,
    string GraphScopesStatus,
    bool SmokeTestRecent,
    string SmokeStatus,
    DateTimeOffset? LastSmokeSuccess,
    string? FailureReason);
