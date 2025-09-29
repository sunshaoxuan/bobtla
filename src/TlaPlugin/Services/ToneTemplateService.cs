using System.Collections.Generic;

namespace TlaPlugin.Services;

/// <summary>
<<<<<<< HEAD
/// 提供不同语气模板的工具类。
=======
/// 文体テンプレートを提供するユーティリティ。
>>>>>>> origin/main
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
<<<<<<< HEAD
        [Polite] = "请将以下内容翻译成礼貌且正式的语气。",
        [Casual] = "请将以下内容翻译成轻松随和的口吻。",
        [Business] = "请将以下内容翻译成适合商务场景的正式语气。",
        [Technical] = "请将以下内容翻译成技术文档需要的严谨语气。"
    };

    /// <summary>
    /// 获取指定语气的提示词。
=======
        [Polite] = "以下の文章を丁寧語で翻訳してください。",
        [Casual] = "以下の文章をカジュアルな口調で翻訳してください。",
        [Business] = "以下の文章をビジネス向けの敬体で翻訳してください。",
        [Technical] = "以下の文章を技術文書向けの正確な文体で翻訳してください。"
    };

    /// <summary>
    /// 指定トーンのプロンプトを取得する。
>>>>>>> origin/main
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
<<<<<<< HEAD
    /// 返回可用的语气模板列表。
=======
    /// 利用可能なトーンテンプレート一覧を返す。
>>>>>>> origin/main
    /// </summary>
    public IReadOnlyDictionary<string, string> GetAvailableTones()
    {
        return new Dictionary<string, string>(Templates);
    }
}
