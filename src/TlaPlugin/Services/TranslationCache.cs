using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Text.Json.Nodes;
using TlaPlugin.Configuration;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 用于避免重复翻译的内存缓存。
/// </summary>
public class TranslationCache : IDisposable
{
    private readonly IMemoryCache _cache;
    private readonly PluginOptions _options;
    private readonly bool _ownsCache;

    public TranslationCache(IMemoryCache cache, IOptions<PluginOptions>? options = null)
    {
        _cache = cache;
        _options = options?.Value ?? new PluginOptions();
    }

    public TranslationCache(IOptions<PluginOptions>? options = null)
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
        _options = options?.Value ?? new PluginOptions();
        _ownsCache = true;
    }

    public bool TryGet(TranslationRequest request, out TranslationResult result)
    {
        if (_cache.TryGetValue(BuildKey(request), out TranslationResult cached) && cached is not null)
        {
            result = Clone(cached);
            return true;
        }

        result = default!;
        return false;
    }

    public void Set(TranslationRequest request, TranslationResult result)
    {
        _cache.Set(BuildKey(request), Clone(result), _options.CacheTtl);
    }

    private static string BuildKey(TranslationRequest request)
    {
        var extras = string.Join(',', request.AdditionalTargetLanguages.OrderBy(l => l));
        return string.Join('|', new[]
        {
            request.TenantId,
            request.SourceLanguage ?? string.Empty,
            request.TargetLanguage,
            request.Tone,
            request.UseGlossary.ToString(),
            request.UseRag.ToString(),
            request.Text,
            extras,
            request.UiLocale ?? string.Empty,
            request.ContextSummary ?? string.Empty
        });
    }

    private static TranslationResult Clone(TranslationResult source)
    {
        return new TranslationResult
        {
            RawTranslatedText = source.RawTranslatedText,
            TranslatedText = source.TranslatedText,
            SourceLanguage = source.SourceLanguage,
            TargetLanguage = source.TargetLanguage,
            ModelId = source.ModelId,
            Confidence = source.Confidence,
            CostUsd = source.CostUsd,
            LatencyMs = source.LatencyMs,
            UiLocale = source.UiLocale,
            AdaptiveCard = (JsonObject?)(source.AdaptiveCard?.DeepClone()) ?? new JsonObject(),
            AdditionalTranslations = new Dictionary<string, string>(source.AdditionalTranslations),
            GlossaryMatches = source.GlossaryMatches.Select(match => match.Clone()).ToList()
        };
    }
    public void Dispose()
    {
        if (_ownsCache && _cache is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
