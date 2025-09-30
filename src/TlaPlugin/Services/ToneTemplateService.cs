using System.Collections.Generic;

namespace TlaPlugin.Services;

/// <summary>
/// 提供不同语气模板的工具类。
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
        [Polite] = "请将以下内容翻译成礼貌且正式的语气。",
        [Casual] = "请将以下内容翻译成轻松随和的口吻。",
        [Business] = "请将以下内容翻译成适合商务场景的正式语气。",
        [Technical] = "请将以下内容翻译成技术文档需要的严谨语气。"
    };

    /// <summary>
    /// 获取指定语气的提示词。
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
    /// 返回可用的语气模板列表。
    /// </summary>
    public IReadOnlyDictionary<string, string> GetAvailableTones()
    {
        return new Dictionary<string, string>(Templates);
    }
}
