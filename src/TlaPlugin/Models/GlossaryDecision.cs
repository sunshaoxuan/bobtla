using System;
using System.Collections.Generic;
using System.Linq;

namespace TlaPlugin.Models;

/// <summary>
/// 表示用户针对术语冲突所做出的决策。
/// </summary>
public class GlossaryDecision
{
    public GlossaryDecisionKind Kind { get; set; } = GlossaryDecisionKind.Unspecified;
    public string? Target { get; set; }
        = null;
    public string? Scope { get; set; }
        = null;

    public GlossaryDecision Clone()
    {
        return new GlossaryDecision
        {
            Kind = Kind,
            Target = Target,
            Scope = Scope
        };
    }
}

/// <summary>
/// 表示用户对冲突术语的选择类型。
/// </summary>
public enum GlossaryDecisionKind
{
    Unspecified,
    UsePreferred,
    UseAlternative,
    KeepOriginal
}

/// <summary>
/// 表示同一术语不同翻译的候选项。
/// </summary>
public record GlossaryCandidateDetail(string Target, string Scope, int Priority);

/// <summary>
/// 表示一次术语匹配及其解析结果。
/// </summary>
public class GlossaryMatchDetail
{
    public string Source { get; set; } = string.Empty;
    public string? AppliedTarget { get; set; }
        = null;
    public bool Replaced { get; set; }
        = false;
    public bool HasConflict { get; set; }
        = false;
    public GlossaryDecisionKind Resolution { get; set; }
        = GlossaryDecisionKind.Unspecified;
    public int Occurrences { get; set; }
        = 0;
    public IList<GlossaryCandidateDetail> Candidates { get; set; }
        = new List<GlossaryCandidateDetail>();

    public GlossaryMatchDetail Clone()
    {
        return new GlossaryMatchDetail
        {
            Source = Source,
            AppliedTarget = AppliedTarget,
            Replaced = Replaced,
            HasConflict = HasConflict,
            Resolution = Resolution,
            Occurrences = Occurrences,
            Candidates = new List<GlossaryCandidateDetail>(Candidates)
        };
    }
}

/// <summary>
/// 表示术语应用后的综合结果。
/// </summary>
public class GlossaryApplicationResult
{
    public string Text { get; set; } = string.Empty;
    public IReadOnlyList<GlossaryMatchDetail> Matches { get; set; } = Array.Empty<GlossaryMatchDetail>();

    public bool HasConflicts => Matches.Any(match => match.HasConflict);

    public bool RequiresResolution => Matches.Any(match => match.HasConflict && match.Resolution == GlossaryDecisionKind.Unspecified);
}
