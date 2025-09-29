using System.Linq;
using System.Text.RegularExpressions;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 三層の用語集と禁訳語リストを統合するサービス。
/// </summary>
public class GlossaryService
{
    private readonly IList<GlossaryEntry> _entries = new List<GlossaryEntry>();

    public void LoadEntries(IEnumerable<GlossaryEntry> entries)
    {
        foreach (var entry in entries)
        {
            _entries.Add(entry);
        }
    }

    /// <summary>
    /// 優先順位に沿って用語を適用する。
    /// </summary>
    public string Apply(string text, string tenantId, string? channelId, string userId)
    {
        var ordered = _entries
            .Where(e => e.Scope == $"tenant:{tenantId}" || e.Scope == $"channel:{channelId}" || e.Scope == $"user:{userId}")
            .OrderBy(e => Priority(e.Scope, tenantId, channelId, userId))
            .ToList();

        var result = text;
        foreach (var entry in ordered)
        {
            var pattern = Regex.Escape(entry.Source);
            result = Regex.Replace(result, pattern, entry.Target, RegexOptions.IgnoreCase);
        }

        return result;
    }

    private static int Priority(string scope, string tenantId, string? channelId, string userId)
    {
        if (scope == $"user:{userId}")
        {
            return 0;
        }
        if (!string.IsNullOrEmpty(channelId) && scope == $"channel:{channelId}")
        {
            return 1;
        }
        if (scope == $"tenant:{tenantId}")
        {
            return 2;
        }
        return 3;
    }
}
