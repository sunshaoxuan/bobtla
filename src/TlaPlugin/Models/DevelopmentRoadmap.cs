using System.Collections.Generic;

namespace TlaPlugin.Models;

/// <summary>
/// 開発工程全体のロードマップを表現するモデル。
/// </summary>
public record DevelopmentRoadmap(
    IReadOnlyList<RoadmapStage> Stages,
    IReadOnlyList<RoadmapTest> Tests,
    string ActiveStageId);

/// <summary>
/// 各段階の目標と成果を保持するレコード。
/// </summary>
public record RoadmapStage(
    string Id,
    string Name,
    string Objective,
    bool Completed,
    IReadOnlyList<string> Deliverables,
    IReadOnlyList<string> TestFocuses);

/// <summary>
/// 実施済みまたは計画中のテスト情報を表現するレコード。
/// </summary>
public record RoadmapTest(
    string Id,
    string Name,
    string Description,
    bool Automated);
