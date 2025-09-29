namespace TlaPlugin.Models;

/// <summary>
/// 项目整体进度的快照模型。
/// </summary>
public record ProjectStatusSnapshot(
    string CurrentStageId,
    IReadOnlyList<StageStatus> Stages,
    IReadOnlyList<string> NextSteps,
    int OverallCompletionPercent,
    FrontendProgress Frontend);

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
