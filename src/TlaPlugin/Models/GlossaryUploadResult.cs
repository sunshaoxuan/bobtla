using System;
using System.Collections.Generic;

namespace TlaPlugin.Models;

/// <summary>
/// 术语库导入冲突详情。
/// </summary>
public record GlossaryUploadConflict(
    string Source,
    string ExistingTarget,
    string IncomingTarget,
    string Scope);

/// <summary>
/// 术语库导入条目。
/// </summary>
public record GlossaryUploadEntry(
    string Source,
    string Target,
    IDictionary<string, string>? Metadata = null);

/// <summary>
/// 术语库导入结果。
/// </summary>
public class GlossaryUploadResult
{
    public int ImportedCount { get; init; }
    public int UpdatedCount { get; init; }
    public IReadOnlyList<GlossaryUploadConflict> Conflicts { get; init; }
        = Array.Empty<GlossaryUploadConflict>();
    public IReadOnlyList<string> Errors { get; init; }
        = Array.Empty<string>();

    public bool HasConflicts => Conflicts.Count > 0;
    public bool HasErrors => Errors.Count > 0;
}
