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

    private static readonly IReadOnlyDictionary<string, string> JapaneseStrings =
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
            ["tla.ui.queue.body"] = "メッセージを {0} 個のセグメントに分割し、準備が整い次第草稿に反映します。ジョブ ID: {1}",
            ["tla.toast.dashboard.status"] = "プロジェクト状況を読み込めません。ダッシュボードはキャッシュデータを表示しています。",
            ["tla.toast.dashboard.roadmap"] = "ロードマップを読み込めません。組み込みテンプレートを表示しています。",
            ["tla.toast.dashboard.locales"] = "利用可能な言語を取得できません。既定値を使用します。",
            ["tla.toast.dashboard.configuration"] = "構成を取得できません。言語一覧は既定値に基づきます。",
            ["tla.toast.dashboard.metrics"] = "メトリックを取得できません。最後のキャッシュを表示しています。",
            ["tla.toast.fetchGeneric"] = "リクエスト {0} に失敗しました。後でもう一度お試しください。",
            ["tla.toast.settings.glossaryFetch"] = "用語集を読み込めません。後でもう一度お試しください。",
            ["tla.toast.settings.configuration"] = "テナント ポリシーを読み込めません。既定値を使用します。",
            ["tla.settings.glossary.empty"] = "登録済みの用語はありません。",
            ["tla.settings.glossary.noConflicts"] = "競合は検出されませんでした。",
            ["tla.settings.policies.noBannedTerms"] = "禁止語は設定されていません。",
            ["tla.settings.policies.noStyleTemplates"] = "スタイル テンプレートはありません。",
            ["tla.settings.upload.selectFile"] = "先に CSV または TermBase ファイルを選択してください。",
            ["tla.settings.upload.progress.uploading"] = "アップロード中…",
            ["tla.settings.upload.progress.parsing"] = "ファイルを解析しています…",
            ["tla.settings.upload.progress.complete"] = "アップロードが完了しました",
            ["tla.settings.upload.summary"] = "{0} 件を取り込み、{1} 件を更新しました。",
            ["tla.settings.upload.error.noFile"] = "用語ファイルを先に選択してください。",
            ["tla.settings.upload.error.http"] = "アップロードに失敗しました: {0}"
        };

    private static readonly IReadOnlyDictionary<string, string> EnglishStrings =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tla.ui.card.title"] = "Translation Result",
            ["tla.ui.card.modelLine"] = "{0} → {1} | Model: {2}",
            ["tla.ui.card.metrics"] = "Cost: ${0:F4} | Latency: {1}ms",
            ["tla.ui.card.additional"] = "Additional Translations",
            ["tla.ui.action.insert"] = "Insert into chat",
            ["tla.ui.action.insertLocale"] = "Insert {0}",
            ["tla.ui.action.showOriginal"] = "Show original",
            ["tla.ui.action.changeLanguage"] = "Select another language",
            ["tla.error.detection.title"] = "Select language",
            ["tla.error.detection.body"] = "Detection confidence is low. Choose the source language from the suggestions.",
            ["tla.error.budget.title"] = "Budget limit",
            ["tla.error.rate.title"] = "Rate limit",
            ["tla.error.translation.title"] = "Translation error",
            ["tla.ui.glossary.conflictTitle"] = "Glossary choice required",
            ["tla.ui.glossary.conflictDescription"] = "Multiple glossary candidates were found. Select the translation to apply.",
            ["tla.ui.glossary.conflictItem"] = "{0} ({1} matches)",
            ["tla.ui.glossary.option.preferred"] = "Preferred {0} ({1})",
            ["tla.ui.glossary.option.alternative"] = "Alternative {0} ({1})",
            ["tla.ui.glossary.option.original"] = "Keep original",
            ["tla.ui.glossary.submit"] = "Apply selection",
            ["tla.ui.glossary.cancel"] = "Cancel",
            ["tla.ui.queue.title"] = "Queued long-form translation",
            ["tla.ui.queue.body"] = "The message was split into {0} segments and will sync to your draft when ready. Job ID: {1}",
            ["tla.toast.dashboard.status"] = "Unable to load project status. Showing cached dashboard data.",
            ["tla.toast.dashboard.roadmap"] = "Unable to load roadmap information. Displaying the built-in template.",
            ["tla.toast.dashboard.locales"] = "Unable to fetch available locales. Using defaults.",
            ["tla.toast.dashboard.configuration"] = "Unable to load configuration. Language list is based on defaults.",
            ["tla.toast.dashboard.metrics"] = "Unable to retrieve metrics. Displaying the last cached snapshot.",
            ["tla.toast.fetchGeneric"] = "Request to {0} failed. Please try again later.",
            ["tla.toast.settings.glossaryFetch"] = "Unable to load glossary entries. Please try again later.",
            ["tla.toast.settings.configuration"] = "Unable to load tenant policies. Using defaults.",
            ["tla.settings.glossary.empty"] = "No glossary entries yet.",
            ["tla.settings.glossary.noConflicts"] = "No conflicts detected.",
            ["tla.settings.policies.noBannedTerms"] = "No banned terms configured.",
            ["tla.settings.policies.noStyleTemplates"] = "No style templates available.",
            ["tla.settings.upload.selectFile"] = "Select a CSV or TermBase file before uploading.",
            ["tla.settings.upload.progress.uploading"] = "Uploading…",
            ["tla.settings.upload.progress.parsing"] = "Parsing file…",
            ["tla.settings.upload.progress.complete"] = "Upload complete",
            ["tla.settings.upload.summary"] = "Imported {0} entries and updated {1}.",
            ["tla.settings.upload.error.noFile"] = "Select a terminology file first.",
            ["tla.settings.upload.error.http"] = "Upload failed: {0}"
        };

    private static readonly IReadOnlyDictionary<string, string> SimplifiedChineseStrings =
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
            ["tla.ui.queue.body"] = "消息已拆分为 {0} 个片段，完成后会同步到草稿。任务 ID: {1}",
            ["tla.toast.dashboard.status"] = "无法加载项目状态，仪表盘展示的是缓存数据。",
            ["tla.toast.dashboard.roadmap"] = "无法加载路线图信息，展示的是内置模板。",
            ["tla.toast.dashboard.locales"] = "无法加载可用语言列表，将使用默认配置。",
            ["tla.toast.dashboard.configuration"] = "无法加载配置，语言列表基于本地默认值。",
            ["tla.toast.dashboard.metrics"] = "无法获取实时指标，显示的是最近一次缓存。",
            ["tla.toast.fetchGeneric"] = "无法获取 {0}，请稍后重试。",
            ["tla.toast.settings.glossaryFetch"] = "无法加载术语表，请稍后重试。",
            ["tla.toast.settings.configuration"] = "无法加载租户策略，将使用默认设置。",
            ["tla.settings.glossary.empty"] = "暂无术语。",
            ["tla.settings.glossary.noConflicts"] = "未检测到冲突。",
            ["tla.settings.policies.noBannedTerms"] = "暂无禁译词。",
            ["tla.settings.policies.noStyleTemplates"] = "暂无风格模板。",
            ["tla.settings.upload.selectFile"] = "请先选择 CSV 或 TermBase 文件。",
            ["tla.settings.upload.progress.uploading"] = "上传中…",
            ["tla.settings.upload.progress.parsing"] = "解析文件…",
            ["tla.settings.upload.progress.complete"] = "上传完成",
            ["tla.settings.upload.summary"] = "已导入 {0} 条，更新 {1} 条。",
            ["tla.settings.upload.error.noFile"] = "请先选择术语文件。",
            ["tla.settings.upload.error.http"] = "上传失败: {0}"
        };

    private static readonly IReadOnlyDictionary<string, string> DisplayNameOverrides =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ja-JP"] = "日本語 (日本)",
            ["zh-CN"] = "简体中文 (中国)",
            ["zh-SG"] = "简体中文 (新加坡)",
            ["zh-TW"] = "繁體中文 (台灣)",
            ["zh-HK"] = "繁體中文 (香港)"
        };

    private static readonly IReadOnlyDictionary<string, CatalogDefinition> Catalogs = BuildCatalogs();

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

    private static IReadOnlyDictionary<string, CatalogDefinition> BuildCatalogs()
    {
        var catalogs = new Dictionary<string, CatalogDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultLocale] = new CatalogDefinition(ResolveDisplayName(DefaultLocale), JapaneseStrings)
        };

        AddCatalog(catalogs, "en-US", EnglishStrings);

        var englishVariants = new[] { "en-GB", "en-AU", "en-CA", "en-IN", "en-SG" };
        foreach (var locale in englishVariants)
        {
            AddCatalog(catalogs, locale, EnglishStrings);
        }

        AddCatalog(catalogs, "zh-CN", SimplifiedChineseStrings, DisplayNameOverrides["zh-CN"]);
        AddCatalog(catalogs, "zh-SG", SimplifiedChineseStrings, DisplayNameOverrides["zh-SG"]);
        AddCatalog(catalogs, "zh-TW", SimplifiedChineseStrings, DisplayNameOverrides["zh-TW"]);
        AddCatalog(catalogs, "zh-HK", SimplifiedChineseStrings, DisplayNameOverrides["zh-HK"]);

        var additionalLocales = new[]
        {
            "ko-KR",
            "fr-FR", "fr-CA", "fr-BE", "fr-CH",
            "de-DE", "de-AT", "de-CH",
            "es-ES", "es-MX", "es-AR", "es-CL", "es-CO", "es-US",
            "pt-BR", "pt-PT", "pt-AO",
            "it-IT", "it-CH",
            "nl-NL", "nl-BE",
            "sv-SE", "da-DK", "fi-FI", "nb-NO",
            "pl-PL", "cs-CZ", "sk-SK", "hu-HU", "ro-RO", "bg-BG",
            "hr-HR", "sr-RS", "sl-SI",
            "el-GR", "tr-TR",
            "ru-RU", "uk-UA",
            "hi-IN", "th-TH", "vi-VN", "id-ID"
        };

        foreach (var locale in additionalLocales)
        {
            AddCatalog(catalogs, locale, EnglishStrings);
        }

        return catalogs;
    }

    private static void AddCatalog(
        IDictionary<string, CatalogDefinition> target,
        string locale,
        IReadOnlyDictionary<string, string> strings,
        string? displayNameOverride = null)
    {
        var displayName = displayNameOverride ?? ResolveDisplayName(locale);
        target[locale] = new CatalogDefinition(displayName, strings);
    }

    private static string ResolveDisplayName(string locale)
    {
        if (DisplayNameOverrides.TryGetValue(locale, out var name))
        {
            return name;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(locale);
            return culture.EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return locale;
        }
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
