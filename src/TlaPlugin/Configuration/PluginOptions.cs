using System;
using System.Collections.Generic;

namespace TlaPlugin.Configuration;

/// <summary>
/// プラグイン全体の構成値を保持するオプション定義。
/// </summary>
public class PluginOptions
{
    public int MaxCharactersPerRequest { get; set; } = 50000;
    public decimal DailyBudgetUsd { get; set; } = 25m;
    public TimeSpan DraftRetention { get; set; } = TimeSpan.FromDays(7);
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(24);
    public int MaxConcurrentTranslations { get; set; } = 4;
    public int RequestsPerMinute { get; set; } = 120;
    public string OfflineDraftConnectionString { get; set; } = "Data Source=tla-offline.db";
    public IList<string> SupportedLanguages { get; set; } = new List<string>
    {
        "ja-JP",
        "en-US",
        "zh-CN",
        "zh-TW",
        "ko-KR",
        "fr-FR",
        "de-DE",
        "es-ES",
        "it-IT",
        "pt-BR",
        "pt-PT",
        "ru-RU",
        "nl-NL",
        "sv-SE",
        "da-DK",
        "fi-FI",
        "nb-NO",
        "pl-PL",
        "cs-CZ",
        "sk-SK",
        "hu-HU",
        "tr-TR",
        "ar-SA",
        "he-IL",
        "hi-IN",
        "th-TH",
        "vi-VN",
        "id-ID",
        "ms-MY",
        "uk-UA",
        "el-GR",
        "ro-RO",
        "bg-BG",
        "hr-HR",
        "sl-SI",
        "lt-LT",
        "lv-LV",
        "et-EE",
        "sr-RS",
        "ta-IN"
    };
    public IList<string> DefaultTargetLanguages { get; set; } = new List<string>
    {
        "ja-JP",
        "en-US"
    };
    public string DefaultUiLocale { get; set; } = "ja-JP";
    public IList<ModelProviderOptions> Providers { get; set; } = new List<ModelProviderOptions>();
    public CompliancePolicyOptions Compliance { get; set; } = new();
    public SecurityOptions Security { get; set; } = new();
}

/// <summary>
/// モデルプロバイダーを設定するためのオプション。
/// </summary>
public class ModelProviderOptions
{
    public string Id { get; set; } = "mock";
    public ModelProviderKind Kind { get; set; } = ModelProviderKind.Mock;
    public decimal CostPerCharUsd { get; set; } = 0.00002m;
    public int LatencyTargetMs { get; set; } = 300;
    public double Reliability { get; set; } = 0.99;
    public IList<string> Regions { get; set; } = new List<string> { "global" };
    public IList<string> Certifications { get; set; } = new List<string>();
    public int SimulatedFailures { get; set; }
        = 0;
    public string TranslationPrefix { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string ApiKeySecretName { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public IDictionary<string, string> DefaultHeaders { get; set; } = new Dictionary<string, string>();
}

/// <summary>
/// 表示模型提供方类型的枚举。
/// </summary>
public enum ModelProviderKind
{
    Mock,
    OpenAi,
    Anthropic,
    Groq,
    OpenWebUi,
    Ollama,
    Custom
}

/// <summary>
/// 合规策略相关配置。
/// </summary>
public class CompliancePolicyOptions
{
    public IList<string> RequiredRegionTags { get; set; } = new List<string>();
    public IList<string> AllowedRegionFallbacks { get; set; } = new List<string>();
    public IList<string> RequiredCertifications { get; set; } = new List<string>();
    public IList<string> BannedPhrases { get; set; } = new List<string>();
    public IDictionary<string, string> PiiPatterns { get; set; } = new Dictionary<string, string>
    {
        ["email"] = @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}",
        ["phone"] = @"\\+?[0-9]{8,15}",
        ["creditCard"] = @"[0-9]{13,16}"
    };
}

/// <summary>
/// 密钥管理与 OBO 认证的配置。
/// </summary>
public class SecurityOptions
{
    public string KeyVaultUri { get; set; } = "https://localhost.vault.azure.net/";
    public string ClientId { get; set; } = "00000000-0000-0000-0000-000000000000";
    public string ClientSecretName { get; set; } = "tla-client-secret";
    public string UserAssertionAudience { get; set; } = "api://tla-plugin";
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan SecretCacheTtl { get; set; } = TimeSpan.FromMinutes(10);
    public IDictionary<string, string> SeedSecrets { get; set; } = new Dictionary<string, string>
    {
        ["tla-client-secret"] = "local-dev-secret"
    };
}
