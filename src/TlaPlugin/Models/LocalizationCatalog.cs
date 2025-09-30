using System.Collections.Generic;

namespace TlaPlugin.Models;

/// <summary>
/// ローカライズ済みの UI 文字列を保持するカタログ。
/// </summary>
public record LocalizationCatalog(
    string Locale,
    IReadOnlyDictionary<string, string> Strings,
    string DefaultLocale,
    string DisplayName);
