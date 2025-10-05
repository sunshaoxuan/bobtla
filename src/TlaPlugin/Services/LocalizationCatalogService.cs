using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// Teams クライアント向けの既定 UI 文言を提供するローカライズサービス。
/// </summary>
public class LocalizationCatalogService
{
    private const string DefaultLocale = "ja-JP";

    private sealed record CatalogDefinition(string DisplayName, IReadOnlyDictionary<string, string> Strings);

    private static readonly IReadOnlyDictionary<string, CatalogDefinition> Catalogs =
        new Dictionary<string, CatalogDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["ja-JP"] = new CatalogDefinition(
                "日本語 (日本)",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["tla.ui.card.title"] = "翻訳結果",
                    ["tla.ui.card.modelLine"] = "{0} → {1} | モデル: {2}",
                    ["tla.ui.card.metrics"] = "コスト: ${0:F4} | レイテンシ: {1}ms",
                    ["tla.ui.card.additional"] = "追加の翻訳",
                    ["tla.ui.action.insert"] = "チャットに挿入",
                    ["tla.ui.action.insertLocale"] = "{0} を挿入",
                    ["tla.ui.action.showOriginal"] = "原文を表示",
                    ["tla.ui.action.changeLanguage"] = "別の言語を選択",
                    ["tla.error.detection.title"] = "言語を選択",
                    ["tla.error.detection.body"] = "自動検出の信頼度が低いため、候補から源言語を選択してください。",
                    ["tla.error.budget.title"] = "予算制限",
                    ["tla.error.rate.title"] = "レート制限",
                    ["tla.error.translation.title"] = "翻訳エラー",
                    ["tla.ui.glossary.conflictTitle"] = "用語の選択が必要です",
                    ["tla.ui.glossary.conflictDescription"] = "一致した用語に複数の候補が見つかりました。使用する訳語を選択してください。",
                    ["tla.ui.glossary.conflictItem"] = "{0}（{1} 件）",
                    ["tla.ui.glossary.option.preferred"] = "推奨訳 {0} （{1}）",
                    ["tla.ui.glossary.option.alternative"] = "代替訳 {0} （{1}）",
                    ["tla.ui.glossary.option.original"] = "原文を保持",
                    ["tla.ui.glossary.submit"] = "選択を適用",
                    ["tla.ui.glossary.cancel"] = "取消",
                    ["tla.ui.queue.title"] = "長文の翻訳を受け付けました",
                    ["tla.ui.queue.body"] = "メッセージを {0} 個のセグメントに分割し、準備が整い次第草稿に反映します。ジョブ ID: {1}"
                }),
            ["zh-CN"] = new CatalogDefinition(
                "简体中文 (中国)",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["tla.ui.card.title"] = "翻译结果",
                    ["tla.ui.card.modelLine"] = "{0} → {1} | 模型: {2}",
                    ["tla.ui.card.metrics"] = "成本: ${0:F4} | 延迟: {1}ms",
                    ["tla.ui.card.additional"] = "额外翻译",
                    ["tla.ui.action.insert"] = "插入到聊天",
                    ["tla.ui.action.insertLocale"] = "插入 {0}",
                    ["tla.ui.action.showOriginal"] = "查看原文",
                    ["tla.ui.action.changeLanguage"] = "选择其他语言",
                    ["tla.error.detection.title"] = "请选择语言",
                    ["tla.error.detection.body"] = "自动检测置信度不足，请从候选项中选择源语言。",
                    ["tla.error.budget.title"] = "预算限制",
                    ["tla.error.rate.title"] = "速率限制",
                    ["tla.error.translation.title"] = "翻译失败",
                    ["tla.ui.glossary.conflictTitle"] = "请选择术语翻译",
                    ["tla.ui.glossary.conflictDescription"] = "检测到多个术语候选，请选择希望保留的译法。",
                    ["tla.ui.glossary.conflictItem"] = "{0}（{1} 次出现）",
                    ["tla.ui.glossary.option.preferred"] = "推荐译文 {0}（{1}）",
                    ["tla.ui.glossary.option.alternative"] = "备选译文 {0}（{1}）",
                    ["tla.ui.glossary.option.original"] = "保留原文",
                    ["tla.ui.glossary.submit"] = "提交选择",
                    ["tla.ui.glossary.cancel"] = "取消",
                    ["tla.ui.queue.title"] = "长文本翻译已排队",
                    ["tla.ui.queue.body"] = "消息已拆分为 {0} 个片段，完成后会同步到草稿。任务 ID: {1}"
                })
        };

    /// <summary>
    /// 指定カルチャのローカライズ済み文字列を取得する。
    /// </summary>
    public LocalizationCatalog GetCatalog(string? locale = null)
    {
        var resolved = ResolveLocale(locale);
        var strings = MergeWithDefault(resolved);
        var definition = Catalogs[resolved];
        return new LocalizationCatalog(resolved, strings, DefaultLocale, definition.DisplayName);
    }

    /// <summary>
    /// 指定キーの文言を取得し、存在しない場合は既定ロケールを返す。
    /// </summary>
    public string GetString(string key, string? locale = null)
    {
        var catalog = GetCatalog(locale);
        if (catalog.Strings.TryGetValue(key, out var value))
        {
            return value;
        }

        return Catalogs[DefaultLocale].Strings[key];
    }

    /// <summary>
    /// 利用可能なロケール一覧を返す。
    /// </summary>
    public IReadOnlyList<LocalizationLocale> GetAvailableLocales()
    {
        var locales = new List<LocalizationLocale>(Catalogs.Count);
        foreach (var entry in Catalogs)
        {
            var isDefault = string.Equals(entry.Key, DefaultLocale, StringComparison.OrdinalIgnoreCase);
            locales.Add(new LocalizationLocale(entry.Key, entry.Value.DisplayName, isDefault));
        }

        return locales
            .OrderByDescending(locale => locale.IsDefault)
            .ThenBy(locale => locale.Locale, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return DefaultLocale;
        }

        var candidate = Catalogs.Keys.FirstOrDefault(k =>
            string.Equals(k, locale, StringComparison.OrdinalIgnoreCase));

        if (candidate != null)
        {
            return candidate;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(locale);
            candidate = Catalogs.Keys.FirstOrDefault(k =>
                string.Equals(k, culture.Name, StringComparison.OrdinalIgnoreCase))
                ?? Catalogs.Keys.FirstOrDefault(k =>
                    string.Equals(k, culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase));
        }
        catch (CultureNotFoundException)
        {
            candidate = null;
        }

        return candidate ?? DefaultLocale;
    }

    private static IReadOnlyDictionary<string, string> MergeWithDefault(string locale)
    {
        var defaults = Catalogs[DefaultLocale].Strings;
        if (string.Equals(locale, DefaultLocale, StringComparison.OrdinalIgnoreCase) ||
            !Catalogs.TryGetValue(locale, out var definition))
        {
            return defaults;
        }

        var merged = new Dictionary<string, string>(defaults, StringComparer.Ordinal);
        foreach (var entry in definition.Strings)
        {
            merged[entry.Key] = entry.Value;
        }

        return merged;
    }
}
