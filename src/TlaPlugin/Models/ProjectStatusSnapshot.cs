namespace TlaPlugin.Models;

/// <summary>
/// プロジェクト全体の進捗スナップショット。
/// </summary>
public record ProjectStatusSnapshot(
    string CurrentStageId,
    IReadOnlyList<StageStatus> Stages,
    IReadOnlyList<string> NextSteps);

/// <summary>
/// 各開発ステージの状態。
/// </summary>
public record StageStatus(
    string Id,
    string Name,
    string Description,
    bool Completed);
