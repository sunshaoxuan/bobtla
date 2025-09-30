using System;

namespace TlaPlugin.Models;

/// <summary>
/// 表示语言识别候选项。
/// </summary>
public record DetectionCandidate(string Language, double Confidence);

/// <summary>
/// 表示语言识别结果的值对象。
/// </summary>
public record DetectionResult(string Language, double Confidence, IReadOnlyList<DetectionCandidate> Candidates)
{
    public static DetectionResult Unknown()
        => new("unknown", 0, Array.Empty<DetectionCandidate>());
}
