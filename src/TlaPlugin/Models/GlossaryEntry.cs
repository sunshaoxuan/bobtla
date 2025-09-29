namespace TlaPlugin.Models;

/// <summary>
/// 表示需求文档三层术语整合的条目。
/// </summary>
public record GlossaryEntry(string Source, string Target, string Scope, IDictionary<string, string>? Metadata = null);
