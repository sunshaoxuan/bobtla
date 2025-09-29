namespace TlaPlugin.Models;

/// <summary>
/// 表示规范中三层术语合并规则的条目。
/// </summary>
public record GlossaryEntry(string Source, string Target, string Scope, IDictionary<string, string>? Metadata = null);
