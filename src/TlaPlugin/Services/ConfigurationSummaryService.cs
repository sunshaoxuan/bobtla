using System.Linq;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// フロントエンド向けの構成情報を生成するサービス。
/// </summary>
public class ConfigurationSummaryService
{
    private readonly IOptions<PluginOptions> _options;
    private readonly ToneTemplateService _toneTemplates;
    private readonly GlossaryService _glossary;

    public ConfigurationSummaryService(
        IOptions<PluginOptions> options,
        ToneTemplateService toneTemplates,
        GlossaryService glossary)
    {
        _options = options;
        _toneTemplates = toneTemplates;
        _glossary = glossary;
    }

    /// <summary>
    /// UI が利用できるサマリー情報を作成する。
    /// </summary>
    public ConfigurationSummary CreateSummary()
    {
        var options = _options.Value;
        var providers = options.Providers
            .Select(p => new ModelProviderSummary(
                p.Id,
                p.Kind,
                p.Reliability,
                p.CostPerCharUsd,
                p.LatencyTargetMs,
                p.Regions.ToList(),
                p.Certifications.ToList()))
            .ToList();

        return new ConfigurationSummary(
            options.MaxCharactersPerRequest,
            options.DailyBudgetUsd,
            options.RequestsPerMinute,
            options.MaxConcurrentTranslations,
            options.SupportedLanguages.ToList(),
            options.DefaultTargetLanguages.ToList(),
            _toneTemplates.GetAvailableTones(),
            providers,
            _glossary.GetEntries().Count);
    }
}
