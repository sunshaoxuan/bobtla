namespace TlaPlugin.Models;

/// <summary>
/// 表示语言检测 API 的请求负载。
/// </summary>
public class LanguageDetectionRequest
{
    public string Text { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
}
