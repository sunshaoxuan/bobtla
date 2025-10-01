using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// Builds the metadata payload consumed by the Teams front-end.
/// </summary>
public class MetadataService
{
    private readonly IOptions<PluginOptions> _options;
    private readonly LocalizationCatalogService _localization;
    private readonly ToneTemplateService _tones;
    private readonly GlossaryService _glossary;

    public MetadataService(
        IOptions<PluginOptions> options,
        LocalizationCatalogService localization,
        ToneTemplateService tones,
        GlossaryService glossary)
    {
        _options = options;
        _localization = localization;
        _tones = tones;
        _glossary = glossary;
    }

    /// <summary>
    /// Creates the metadata response sent to the client.
    /// </summary>
    public MetadataResponse CreateMetadata()
    {
        var options = _options.Value;
        var models = BuildModelMetadata(options);
        var languages = BuildLanguageMetadata(options);
        var features = BuildFeatureFlags(options);
        var pricing = new MetadataPricing("USD", options.DailyBudgetUsd);

        return new MetadataResponse(models, languages, features, pricing);
    }

    private IReadOnlyList<ModelMetadata> BuildModelMetadata(PluginOptions options)
    {
        if (options.Providers is null || options.Providers.Count == 0)
        {
            return Array.Empty<ModelMetadata>();
        }

        return options.Providers
            .Select(provider => new ModelMetadata(
                provider.Id,
                ResolveModelDisplayName(provider),
                provider.CostPerCharUsd,
                provider.LatencyTargetMs))
            .ToList();
    }

    private IReadOnlyList<LanguageMetadata> BuildLanguageMetadata(PluginOptions options)
    {
        var languages = new List<LanguageMetadata>
        {
            new("auto", "自动检测", true)
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "auto"
        };

        var defaultTarget = options.DefaultTargetLanguages?.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(defaultTarget))
        {
            languages.Add(new LanguageMetadata(
                defaultTarget!,
                ResolveLanguageName(defaultTarget!),
                true));
            seen.Add(defaultTarget!);
        }

        if (options.DefaultTargetLanguages != null)
        {
            foreach (var language in options.DefaultTargetLanguages.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(language) || !seen.Add(language))
                {
                    continue;
                }

                languages.Add(new LanguageMetadata(language, ResolveLanguageName(language), false));
            }
        }

        foreach (var locale in _localization.GetAvailableLocales())
        {
            if (!seen.Add(locale.Locale))
            {
                if (locale.IsDefault)
                {
                    var existingIndex = languages.FindIndex(lang =>
                        string.Equals(lang.Id, locale.Locale, StringComparison.OrdinalIgnoreCase));
                    if (existingIndex >= 0)
                    {
                        var entry = languages[existingIndex];
                        languages[existingIndex] = entry with { IsDefault = true };
                    }
                }

                continue;
            }

            languages.Add(new LanguageMetadata(locale.Locale, locale.DisplayName, locale.IsDefault));
        }

        if (!languages.Any(lang => lang.Id != "auto" && lang.IsDefault))
        {
            var fallbackIndex = languages.FindIndex(lang => lang.Id != "auto");
            if (fallbackIndex >= 0)
            {
                var entry = languages[fallbackIndex];
                languages[fallbackIndex] = entry with { IsDefault = true };
            }
        }

        return languages;
    }

    private MetadataFeatureFlags BuildFeatureFlags(PluginOptions options)
    {
        var hasTerminology = _glossary.GetEntries().Count > 0;
        var offlineDraftEnabled = !string.IsNullOrWhiteSpace(options.OfflineDraftConnectionString);
        var toneAvailable = _tones.GetAvailableTones().Count > 0;
        return new MetadataFeatureFlags(
            hasTerminology,
            offlineDraftEnabled,
            toneAvailable,
            options.Rag?.Enabled ?? false);
    }

    private static string ResolveModelDisplayName(ModelProviderOptions provider)
    {
        var kindName = provider.Kind switch
        {
            ModelProviderKind.OpenAi => "OpenAI",
            ModelProviderKind.Anthropic => "Anthropic",
            ModelProviderKind.Groq => "Groq",
            ModelProviderKind.OpenWebUi => "Open WebUI",
            ModelProviderKind.Ollama => "Ollama",
            ModelProviderKind.Custom => "Custom",
            _ => "Mock"
        };

        if (!string.IsNullOrWhiteSpace(provider.Model))
        {
            return $"{kindName} {provider.Model}";
        }

        if (!string.IsNullOrWhiteSpace(provider.Id))
        {
            return $"{kindName} ({provider.Id})";
        }

        return kindName;
    }

    private static string ResolveLanguageName(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return language;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(language);
            return culture.NativeName;
        }
        catch (CultureNotFoundException)
        {
            var normalized = language.Replace('_', '-');
            var dashIndex = normalized.IndexOf('-');
            if (dashIndex > 0)
            {
                var parent = normalized[..dashIndex];
                try
                {
                    var culture = CultureInfo.GetCultureInfo(parent);
                    return culture.NativeName;
                }
                catch (CultureNotFoundException)
                {
                    // Ignore and fallback below.
                }
            }
        }

        return language;
    }
}
