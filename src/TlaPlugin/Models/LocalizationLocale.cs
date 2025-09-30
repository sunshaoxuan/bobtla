namespace TlaPlugin.Models;

/// <summary>
/// 利用可能な UI ロケールのメタデータ。
/// </summary>
public record LocalizationLocale(string Locale, string DisplayName, bool IsDefault);
