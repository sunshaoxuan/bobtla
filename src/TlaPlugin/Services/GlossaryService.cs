using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    private static readonly string[] AllowedScopes = new[] { "tenant", "channel", "user" };

    public void LoadEntries(IEnumerable<GlossaryEntry> entries)
    {
        foreach (var entry in entries)
        {
            _entries.Add(entry);
        }
    }

    /// <summary>
    /// 从上传数据导入术语条目，并返回导入结果与冲突信息。
    /// </summary>
    public GlossaryUploadResult ImportEntries(
        string scope,
        IEnumerable<GlossaryUploadEntry> items,
        bool overwriteConflicts)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        var normalizedScope = NormalizeScope(scope);
        var imported = 0;
        var updated = 0;
        var conflicts = new List<GlossaryUploadConflict>();
        var errors = new List<string>();
        var comparer = StringComparer.OrdinalIgnoreCase;
        var index = 0;

        foreach (var item in items)
        {
            index++;
            if (item is null)
            {
                errors.Add($"行 {index}: 项目为空。");
                continue;
            }

            var source = NormalizeTerm(item.Source);
            var target = NormalizeTerm(item.Target);
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
            {
                errors.Add($"行 {index}: 缺少源词或译文。");
                continue;
            }

            var existing = _entries.LastOrDefault(entry =>
                comparer.Equals(entry.Scope, normalizedScope)
                && comparer.Equals(entry.Source, source));

            if (existing is not null)
            {
                if (comparer.Equals(existing.Target, target))
                {
                    continue;
                }

                if (!overwriteConflicts)
                {
                    conflicts.Add(new GlossaryUploadConflict(source, existing.Target, target, normalizedScope));
                    continue;
                }

                var metadata = existing.Metadata is null
                    ? new Dictionary<string, string>(comparer)
                    : new Dictionary<string, string>(existing.Metadata, comparer);
                EnsureIdentifier(metadata);

                _entries.Remove(existing);
                _entries.Add(existing with { Target = target, Metadata = metadata });
                updated++;
                continue;
            }

            var entryMetadata = item.Metadata is null
                ? new Dictionary<string, string>(comparer)
                : new Dictionary<string, string>(item.Metadata, comparer);
            EnsureIdentifier(entryMetadata);

            _entries.Add(new GlossaryEntry(source, target, normalizedScope, entryMetadata));
            imported++;
        }

        return new GlossaryUploadResult
        {
            ImportedCount = imported,
            UpdatedCount = updated,
            Conflicts = new ReadOnlyCollection<GlossaryUploadConflict>(conflicts),
            Errors = new ReadOnlyCollection<string>(errors)
        };
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
        return ApplyInternal(
            text,
            tenantId,
            channelId,
            userId,
            GlossaryPolicy.Fallback,
            null,
            decisions,
            applyReplacements: true);
    }

    /// <summary>
    /// 预览将要命中的术语及其冲突信息，但不会修改文本。
    /// </summary>
    public GlossaryApplicationResult Preview(
        string text,
        string tenantId,
        string? channelId,
        string userId,
        GlossaryPolicy policy = GlossaryPolicy.Fallback,
        IEnumerable<string>? glossaryIds = null,
        IDictionary<string, GlossaryDecision>? decisions = null)
    {
        return ApplyInternal(
            text,
            tenantId,
            channelId,
            userId,
            policy,
            glossaryIds,
            decisions,
            applyReplacements: false);
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
        IEnumerable<string>? glossaryIds = null,
        IDictionary<string, GlossaryDecision>? decisions = null)
    {
        return ApplyInternal(
            text,
            tenantId,
            channelId,
            userId,
            policy,
            glossaryIds,
            decisions,
            applyReplacements: true);
    }

    /// <summary>
    /// 返回当前登记的术语条目。
    /// </summary>
    public IReadOnlyList<GlossaryEntry> GetEntries()
    {
        return _entries.ToList();
    }

    private GlossaryApplicationResult ApplyInternal(
        string text,
        string tenantId,
        string? channelId,
        string userId,
        GlossaryPolicy policy,
        IEnumerable<string>? glossaryIds,
        IDictionary<string, GlossaryDecision>? decisions,
        bool applyReplacements)
    {
        var scopedEntries = _entries
            .Select(entry => new
            {
                Entry = entry,
                Priority = Priority(entry.Scope, tenantId, channelId, userId)
            })
            .Where(candidate => candidate.Priority < 3)
            .ToList();

        if (glossaryIds is not null)
        {
            var idSet = new HashSet<string>(glossaryIds, StringComparer.OrdinalIgnoreCase);
            if (idSet.Count > 0)
            {
                scopedEntries = scopedEntries
                    .Where(candidate => candidate.Entry.Metadata != null
                        && candidate.Entry.Metadata.TryGetValue("id", out var id)
                        && id is not null
                        && idSet.Contains(id))
                    .ToList();
            }
        }

        var groups = scopedEntries
            .GroupBy(candidate => candidate.Entry.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var currentText = text;
        var matches = new List<GlossaryMatchDetail>();

        foreach (var group in groups)
        {
            var pattern = Regex.Escape(group.Key);
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            var occurrences = regex.Matches(currentText).Count;
            if (occurrences == 0)
            {
                continue;
            }

            var candidates = group
                .Select(candidate => new GlossaryCandidateDetail(
                    candidate.Entry.Target,
                    candidate.Entry.Scope,
                    candidate.Priority))
                .OrderBy(candidate => candidate.Priority)
                .ThenBy(candidate => candidate.Target, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var match = new GlossaryMatchDetail
            {
                Source = group.Key,
                Occurrences = occurrences,
                HasConflict = candidates.Count > 1,
                Candidates = candidates
            };

            var decision = TryGetDecision(decisions, group.Key);
            var selected = ResolveSelectedCandidate(candidates, decision);

            if (decision is not null && decision.Kind == GlossaryDecisionKind.KeepOriginal)
            {
                match.Resolution = GlossaryDecisionKind.KeepOriginal;
                match.Replaced = false;
            }
            else if (selected is not null)
            {
                if (applyReplacements)
                {
                    currentText = regex.Replace(currentText, selected.Target);
                }
                match.AppliedTarget = selected.Target;
                match.Replaced = true;
                match.Resolution = decision?.Kind switch
                {
                    GlossaryDecisionKind.UseAlternative => GlossaryDecisionKind.UseAlternative,
                    GlossaryDecisionKind.UsePreferred => GlossaryDecisionKind.UsePreferred,
                    _ when match.HasConflict => GlossaryDecisionKind.UsePreferred,
                    _ => GlossaryDecisionKind.UsePreferred
                };
            }
            else if (decision is not null && decision.Kind == GlossaryDecisionKind.UseAlternative)
            {
                match.Resolution = GlossaryDecisionKind.UseAlternative;
            }

            matches.Add(match);
        }

        if (policy == GlossaryPolicy.Strict && matches.Count == 0)
        {
            throw new GlossaryApplicationException("指定された用語が見つかりませんでした。");
        }

        var result = new GlossaryApplicationResult
        {
            Text = applyReplacements ? currentText : text,
            Matches = matches
        };

        return result;
    }

    private static GlossaryCandidateDetail? ResolveSelectedCandidate(
        IList<GlossaryCandidateDetail> candidates,
        GlossaryDecision? decision)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        if (decision is null)
        {
            return candidates.Count == 1 ? candidates[0] : null;
        }

        return decision.Kind switch
        {
            GlossaryDecisionKind.UsePreferred => FindCandidate(candidates, decision.Target, decision.Scope) ?? candidates[0],
            GlossaryDecisionKind.UseAlternative => FindCandidate(candidates, decision.Target, decision.Scope)
                ?? (candidates.Count > 1 ? candidates[1] : candidates[0]),
            GlossaryDecisionKind.KeepOriginal => null,
            _ => null
        };
    }

    private static GlossaryCandidateDetail? FindCandidate(
        IEnumerable<GlossaryCandidateDetail> candidates,
        string? target,
        string? scope)
    {
        return candidates.FirstOrDefault(candidate =>
            (target is null || string.Equals(candidate.Target, target, StringComparison.OrdinalIgnoreCase))
            && (scope is null || string.Equals(candidate.Scope, scope, StringComparison.OrdinalIgnoreCase)));
    }

    private static GlossaryDecision? TryGetDecision(
        IDictionary<string, GlossaryDecision>? decisions,
        string source)
    {
        if (decisions is null)
        {
            return null;
        }

        return decisions.TryGetValue(source, out var decision) ? decision : null;
    }


    private static string NormalizeScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new ArgumentException("Scope is required.", nameof(scope));
        }

        var trimmed = scope.Trim();
        var parts = trimmed.Split(':', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            throw new ArgumentException("Scope must follow the pattern '<kind>:<id>'.", nameof(scope));
        }

        var prefix = parts[0].ToLowerInvariant();
        if (!AllowedScopes.Contains(prefix))
        {
            throw new ArgumentException($"Unsupported scope '{parts[0]}'.", nameof(scope));
        }

        return string.Concat(prefix, ":", parts[1]);
    }

    private static string NormalizeTerm(string value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private static void EnsureIdentifier(IDictionary<string, string> metadata)
    {
        const string key = "id";
        if (!metadata.ContainsKey(key))
        {
            metadata[key] = Guid.NewGuid().ToString("N");
        }
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
