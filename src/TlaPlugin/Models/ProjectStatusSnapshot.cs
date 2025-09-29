namespace TlaPlugin.Models;

/// <summary>
/// プロジェクト全体の進捗スナップショット。
/// </summary>
public record ProjectStatusSnapshot(
    string CurrentStageId,
    IReadOnlyList<StageStatus> Stages,
    IReadOnlyList<string> NextSteps,
    int OverallCompletionPercent,
    FrontendProgress Frontend);

/// <summary>
/// 各開発ステージの状態。
/// </summary>
public record StageStatus(
    string Id,
    string Name,
    string Description,
    bool Completed);

/// <summary>
/// 前端準備度の詳細指標。
/// </summary>
public record FrontendProgress(
    int CompletionPercent,
    bool DataPlaneReady,
    bool UiImplemented,
    bool IntegrationReady);
