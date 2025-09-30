using System.Collections.Generic;
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
    public GlossaryApplicationResult Apply(
        string text,
        string tenantId,
        string? channelId,
        string userId,
        IDictionary<string, GlossaryDecision>? decisions = null)
    {
        var detailed = ApplyDetailed(text, tenantId, channelId, userId, GlossaryPolicy.Fallback);
        return detailed.ProcessedText;
    }

    /// <summary>
    /// 返回详细的术语命中信息。
    /// </summary>
    public GlossaryApplicationResult ApplyDetailed(
        string text,
        string tenantId,
        string? channelId,
        string userId,
        GlossaryPolicy policy,
        IEnumerable<string>? glossaryIds = null)
    {
        var candidates = _entries
            .Where(e => e.Scope == $"tenant:{tenantId}" || e.Scope == $"channel:{channelId}" || e.Scope == $"user:{userId}")
            .OrderBy(e => Priority(e.Scope, tenantId, channelId, userId))
            .ToList();

        if (glossaryIds is not null && glossaryIds.Any())
        {
            candidates = candidates
                .Where(entry => entry.Metadata != null && entry.Metadata.TryGetValue("id", out var id) && glossaryIds.Contains(id))
                .ToList();
        }

        var matches = new List<GlossaryMatch>();
        var result = text;
        foreach (var entry in candidates)
        {
            var pattern = Regex.Escape(entry.Source);
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            var hitCount = regex.Matches(result).Count;
            if (hitCount == 0)
            {
                continue;
            }

            result = regex.Replace(result, entry.Target);
            matches.Add(new GlossaryMatch(entry.Source, entry.Target, entry.Scope, hitCount));
        }

        if (policy == GlossaryPolicy.Strict && matches.Count == 0)
        {
            throw new GlossaryApplicationException("指定された用語が見つかりませんでした。");
        }

        return new GlossaryApplicationResult(result, matches);
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
