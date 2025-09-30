namespace TlaPlugin.Models;

/// <summary>
/// 控制术语应用行为的策略枚举。
/// </summary>
public enum GlossaryPolicy
{
    Strict,
    Fallback
}

/// <summary>
/// 表示术语匹配结果。
/// </summary>
public record GlossaryMatch(string Term, string Translation, string Scope, int Count);

/// <summary>
/// 表示术语应用后的结果。
/// </summary>
public record GlossaryApplicationResult(string ProcessedText, IReadOnlyList<GlossaryMatch> Matches);
