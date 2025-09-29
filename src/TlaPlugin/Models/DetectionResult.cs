namespace TlaPlugin.Models;

/// <summary>
/// 言語判定の結果を保持する値オブジェクト。
/// </summary>
public record DetectionResult(string Language, double Confidence);
