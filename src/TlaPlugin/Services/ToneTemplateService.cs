using System.Collections.Generic;

namespace TlaPlugin.Services;

/// <summary>
/// 文体テンプレートを提供するユーティリティ。
/// </summary>
public class ToneTemplateService
{
    public const string Polite = "polite";
    public const string Casual = "casual";
    public const string Business = "business";
    public const string Technical = "technical";
    public const string DefaultTone = Polite;

    private static readonly IDictionary<string, string> Templates = new Dictionary<string, string>
    {
        [Polite] = "以下の文章を丁寧語で翻訳してください。",
        [Casual] = "以下の文章をカジュアルな口調で翻訳してください。",
        [Business] = "以下の文章をビジネス向けの敬体で翻訳してください。",
        [Technical] = "以下の文章を技術文書向けの正確な文体で翻訳してください。"
    };

    /// <summary>
    /// 指定トーンのプロンプトを取得する。
    /// </summary>
    public string GetPromptPrefix(string tone)
    {
        if (string.IsNullOrWhiteSpace(tone))
        {
            return Templates[DefaultTone];
        }

        if (Templates.TryGetValue(tone, out var template))
        {
            return template;
        }

        return Templates[DefaultTone];
    }

    /// <summary>
    /// 利用可能なトーンテンプレート一覧を返す。
    /// </summary>
    public IReadOnlyDictionary<string, string> GetAvailableTones()
    {
        return new Dictionary<string, string>(Templates);
    }
}
