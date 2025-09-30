namespace TlaPlugin.Models;

/// <summary>
/// 表示成本与延迟估算的返回值。
/// </summary>
public record CostLatencyEstimate(decimal Cost, int LatencyMs, string ModelId);
