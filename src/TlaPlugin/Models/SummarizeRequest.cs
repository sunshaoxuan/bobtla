namespace TlaPlugin.Models;

/// <summary>
/// 表示摘要接口的请求。
/// </summary>
public class SummarizeRequest
{
    public string Context { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? UserAssertion { get; set; }
        = null;
}

/// <summary>
/// 提供摘要操作的响应模型。
/// </summary>
public record SummarizeResult(string Summary, string ModelId, decimal CostUsd, int LatencyMs);
