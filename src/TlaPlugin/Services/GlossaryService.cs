using System;
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
        var scopedEntries = _entries
            .Select(entry => new
            {
                Entry = entry,
                Priority = Priority(entry.Scope, tenantId, channelId, userId)
            })
            .Where(candidate => candidate.Entry.Scope == $"tenant:{tenantId}" ||
                candidate.Entry.Scope == $"channel:{channelId}" ||
                candidate.Entry.Scope == $"user:{userId}")
            .OrderBy(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.Entry.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Entry.Target, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (scopedEntries.Count == 0)
        {
            return new GlossaryApplicationResult
            {
                Text = text,
                Matches = Array.Empty<GlossaryMatchDetail>()
            };
        }

        var resolutionMap = decisions != null
            ? new Dictionary<string, GlossaryDecision>(decisions, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, GlossaryDecision>(StringComparer.OrdinalIgnoreCase);

        var workingText = text;
        var matches = new List<GlossaryMatchDetail>();

        var grouped = scopedEntries
            .GroupBy(candidate => candidate.Entry.Source, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Source = group.Key,
                Items = group.ToList(),
                MinPriority = group.Min(item => item.Priority)
            })
            .OrderBy(group => group.MinPriority)
            .ThenBy(group => group.Source, StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            var pattern = Regex.Escape(group.Source);
            var occurrences = Regex.Matches(workingText, pattern, RegexOptions.IgnoreCase).Count;
            if (occurrences == 0)
            {
                continue;
            }

            var candidates = group.Items
                .GroupBy(item => item.Entry.Target, StringComparer.OrdinalIgnoreCase)
                .Select(targetGroup => targetGroup
                    .OrderBy(item => item.Priority)
                    .ThenBy(item => item.Entry.Scope, StringComparer.OrdinalIgnoreCase)
                    .First())
                .OrderBy(item => item.Priority)
                .ThenBy(item => item.Entry.Target, StringComparer.OrdinalIgnoreCase)
                .Select(item => new GlossaryCandidateDetail(item.Entry.Target, item.Entry.Scope, item.Priority))
                .ToList();

            var hasConflict = candidates
                .Select(candidate => candidate.Target)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Skip(1)
                .Any();

            var resolution = GlossaryDecisionKind.Unspecified;
            GlossaryCandidateDetail? selectedCandidate = null;

            if (resolutionMap.TryGetValue(group.Source, out var decision))
            {
                resolution = decision.Kind;
                switch (decision.Kind)
                {
                    case GlossaryDecisionKind.UsePreferred:
                        selectedCandidate = candidates.FirstOrDefault();
                        break;
                    case GlossaryDecisionKind.UseAlternative:
                        if (!string.IsNullOrWhiteSpace(decision.Target))
                        {
                            selectedCandidate = candidates.FirstOrDefault(candidate =>
                                string.Equals(candidate.Target, decision.Target, StringComparison.OrdinalIgnoreCase));
                        }

                        selectedCandidate ??= candidates.Skip(1).FirstOrDefault();
                        break;
                    case GlossaryDecisionKind.KeepOriginal:
                        selectedCandidate = null;
                        break;
                }
            }

            if (!hasConflict && resolution == GlossaryDecisionKind.Unspecified)
            {
                resolution = GlossaryDecisionKind.UsePreferred;
                selectedCandidate = candidates.FirstOrDefault();
            }

            var replaced = false;
            string? appliedTarget = null;
            if (selectedCandidate != null)
            {
                workingText = Regex.Replace(workingText, pattern, selectedCandidate.Target, RegexOptions.IgnoreCase);
                replaced = true;
                appliedTarget = selectedCandidate.Target;
                if (resolution == GlossaryDecisionKind.Unspecified)
                {
                    resolution = GlossaryDecisionKind.UsePreferred;
                }
            }
            else if (!hasConflict && candidates.Count > 0)
            {
                var fallback = candidates.First();
                workingText = Regex.Replace(workingText, pattern, fallback.Target, RegexOptions.IgnoreCase);
                replaced = true;
                appliedTarget = fallback.Target;
                resolution = GlossaryDecisionKind.UsePreferred;
            }

            matches.Add(new GlossaryMatchDetail
            {
                Source = group.Source,
                AppliedTarget = appliedTarget,
                Replaced = replaced,
                HasConflict = hasConflict,
                Resolution = resolution,
                Occurrences = occurrences,
                Candidates = candidates
            });
        }

        return new GlossaryApplicationResult
        {
            Text = workingText,
            Matches = matches
        };
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
