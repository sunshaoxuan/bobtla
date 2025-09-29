namespace TlaPlugin.Configuration;

/// <summary>
/// プラグインの主要構成を保持するオプション。
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
    public IList<ModelProviderOptions> Providers { get; set; } = new List<ModelProviderOptions>();
    public CompliancePolicyOptions Compliance { get; set; } = new();
}

/// <summary>
/// モデル提供者の構成値。
/// </summary>
public class ModelProviderOptions
{
    public string Id { get; set; } = "mock";
    public decimal CostPerCharUsd { get; set; } = 0.00002m;
    public int LatencyTargetMs { get; set; } = 300;
    public double Reliability { get; set; } = 0.99;
    public IList<string> Regions { get; set; } = new List<string> { "global" };
    public IList<string> Certifications { get; set; } = new List<string>();
    public int SimulatedFailures { get; set; }
        = 0;
    public string TranslationPrefix { get; set; } = string.Empty;
}

/// <summary>
/// 合規ポリシーの設定値。
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
