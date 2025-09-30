namespace TlaPlugin.Models;

/// <summary>
/// 表示 /apply-glossary 接口的请求体。
/// </summary>
public class GlossaryApplicationRequest
{
    public string Text { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? ChannelId { get; set; }
        = null;
    public string Policy { get; set; } = GlossaryPolicy.Strict.ToString();
    public IList<string> GlossaryIds { get; set; } = new List<string>();
}
