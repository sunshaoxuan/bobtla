namespace TlaPlugin.Models;

/// <summary>
/// 仕様書の三層術語合成を表すエントリー。
/// </summary>
public record GlossaryEntry(string Source, string Target, string Scope, IDictionary<string, string>? Metadata = null);
