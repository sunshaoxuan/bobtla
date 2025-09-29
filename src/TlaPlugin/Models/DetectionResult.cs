namespace TlaPlugin.Models;

/// <summary>
/// 表示语言识别结果的值对象。
/// </summary>
public record DetectionResult(string Language, double Confidence);
