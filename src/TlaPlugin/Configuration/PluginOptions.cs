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
        "en-GB",
        "en-AU",
        "en-CA",
        "en-IN",
        "en-SG",
        "zh-CN",
        "zh-TW",
        "zh-HK",
        "zh-SG",
        "ko-KR",
        "fr-FR",
        "fr-CA",
        "fr-BE",
        "fr-CH",
        "de-DE",
        "de-AT",
        "de-CH",
        "es-ES",
        "es-MX",
        "es-AR",
        "es-CL",
        "es-CO",
        "es-US",
        "pt-BR",
        "pt-PT",
        "pt-AO",
        "it-IT",
        "it-CH",
        "nl-NL",
        "nl-BE",
        "sv-SE",
        "da-DK",
        "fi-FI",
        "nb-NO",
        "nn-NO",
        "is-IS",
        "fo-FO",
        "pl-PL",
        "cs-CZ",
        "sk-SK",
        "hu-HU",
        "ro-RO",
        "bg-BG",
        "hr-HR",
        "sr-RS",
        "bs-BA",
        "sl-SI",
        "mk-MK",
        "el-GR",
        "tr-TR",
        "tk-TM",
        "az-AZ",
        "kk-KZ",
        "ky-KG",
        "tt-RU",
        "ba-RU",
        "tg-TJ",
        "uz-UZ",
        "mn-MN",
        "uk-UA",
        "ru-RU",
        "be-BY",
        "ka-GE",
        "hy-AM",
        "he-IL",
        "yi-001",
        "ar-SA",
        "ar-EG",
        "ar-AE",
        "fa-IR",
        "ur-PK",
        "ur-IN",
        "ps-AF",
        "ku-TR",
        "ckb-IQ",
        "ug-CN",
        "sd-PK",
        "dv-MV",
        "hi-IN",
        "bn-IN",
        "bn-BD",
        "mr-IN",
        "ne-NP",
        "sa-IN",
        "ta-IN",
        "ta-SG",
        "te-IN",
        "ml-IN",
        "kn-IN",
        "pa-IN",
        "gu-IN",
        "or-IN",
        "as-IN",
        "si-LK",
        "th-TH",
        "lo-LA",
        "km-KH",
        "my-MM",
        "bo-CN",
        "dz-BT",
        "vi-VN",
        "id-ID",
        "ms-MY",
        "ms-SG",
        "jv-ID",
        "su-ID",
        "fil-PH",
        "tl-PH",
        "ceb-PH",
        "ilo-PH",
        "war-PH",
        "pam-PH",
        "sm-WS",
        "fj-FJ",
        "mi-NZ",
        "mg-MG",
        "rw-RW",
        "lg-UG",
        "sw-KE",
        "sw-TZ",
        "yo-NG",
        "ha-NG",
        "ig-NG",
        "am-ET",
        "ti-ER",
        "so-SO",
        "om-ET",
        "rwk-TZ",
        "wo-SN",
        "ff-SN",
        "bm-ML",
        "kj-NA",
        "rn-BI",
        "sn-ZW",
        "st-ZA",
        "tn-ZA",
        "ts-ZA",
        "ve-ZA",
        "xh-ZA",
        "zu-ZA",
        "nso-ZA",
        "pap-CW",
        "crs-SC",
        "gl-ES",
        "ca-ES",
        "eu-ES",
        "oc-FR",
        "ast-ES",
        "sc-IT",
        "vec-IT",
        "rm-CH",
        "br-FR",
        "cy-GB",
        "ga-IE",
        "gd-GB",
        "mt-MT",
        "fy-NL",
        "af-ZA"
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
    public IList<string> AllowedReplyChannels { get; set; } = new List<string>();
}
