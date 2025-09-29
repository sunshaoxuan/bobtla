using System.Linq;
using System.Text.RegularExpressions;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 负责整合三层术语表与禁用词列表的服务。
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
    /// 按优先级顺序应用术语表。
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

    /// <summary>
    /// 返回当前登记的术语条目。
    /// </summary>
    public IReadOnlyList<GlossaryEntry> GetEntries()
    {
        return _entries.ToList();
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
