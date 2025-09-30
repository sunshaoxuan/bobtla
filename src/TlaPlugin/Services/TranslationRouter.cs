using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Authentication;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Providers;

namespace TlaPlugin.Services;

/// <summary>
/// 複数モデルを評価しフェールオーバーを担うルーター。
/// </summary>
public class TranslationRouter
{
    private const double MinimumDetectionConfidence = 0.75;
    private static readonly LanguageDetector FallbackDetector = new();

    private readonly IReadOnlyList<IModelProvider> _providers;
    private readonly ComplianceGateway _compliance;
    private readonly BudgetGuard _budget;
    private readonly AuditLogger _audit;
    private readonly UsageMetricsService _metrics;
    private readonly ToneTemplateService _tones;
    private readonly ITokenBroker _tokenBroker;
    private readonly LocalizationCatalogService _localization;

    public TranslationRouter(
        ModelProviderFactory providerFactory,
        ComplianceGateway compliance,
        BudgetGuard budget,
        AuditLogger audit,
        ToneTemplateService tones,
        ITokenBroker tokenBroker,
        UsageMetricsService metrics,
        LocalizationCatalogService localization,
        IOptions<PluginOptions>? options = null,
        IEnumerable<IModelProvider>? providerOverrides = null)
    {
        _providers = providerOverrides?.ToList() ?? providerFactory.CreateProviders();
        _compliance = compliance;
        _budget = budget;
        _audit = audit;
        _tones = tones;
        _tokenBroker = tokenBroker;
        _metrics = metrics;
        _localization = localization;
        Options = options?.Value ?? new PluginOptions();
    }

    public PluginOptions Options { get; }

    public async Task<DetectionResult> DetectAsync(LanguageDetectionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new TranslationException("検出対象のテキストは必須です。");
        }

        if (request.Text.Length > Options.MaxCharactersPerRequest)
        {
            throw new TranslationException("検出対象が許容される文字数を超えています。");
        }

        return await DetectWithFallbackAsync(request.Text, cancellationToken);
    }

    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new TranslationException("翻訳する本文は必須です。");
        }

        var sourceLanguage = request.SourceLanguage;
        DetectionResult? detection = null;
        if (string.IsNullOrEmpty(sourceLanguage))
        {
            detection = await DetectWithFallbackAsync(request.Text, cancellationToken);
            if (detection.Confidence < MinimumDetectionConfidence)
            {
                throw new LowConfidenceDetectionException(detection);
            }

            sourceLanguage = detection.Language;
        }

        var token = await AcquireTokenAsync(request.TenantId, request.UserId, cancellationToken);

        var promptPrefix = _tones.GetPromptPrefix(request.Tone);

        var allowedProviders = 0;
        foreach (var provider in _providers)
        {
            var report = _compliance.Evaluate(request.Text, provider.Options);
            if (!report.Allowed)
            {
                continue;
            }

            allowedProviders++;
            var translationCount = 1 + request.AdditionalTargetLanguages.Count;
            var estimatedCost = request.Text.Length * provider.Options.CostPerCharUsd * translationCount;
            if (!_budget.TryReserve(request.TenantId, estimatedCost))
            {
                _metrics.RecordFailure(request.TenantId, UsageMetricsService.FailureReasons.Budget);
                throw new BudgetExceededException("本日の翻訳予算を使い切りました。");
            }

            try
            {
                var result = await provider.TranslateAsync(request.Text, sourceLanguage!, request.TargetLanguage, promptPrefix, cancellationToken);
                var rewritten = await provider.RewriteAsync(result.Text, request.Tone, cancellationToken);

                var additional = new Dictionary<string, string>();
                foreach (var extraLanguage in request.AdditionalTargetLanguages)
                {
                    var extraResult = await provider.TranslateAsync(request.Text, sourceLanguage!, extraLanguage, promptPrefix, cancellationToken);
                    var extraRewritten = await provider.RewriteAsync(extraResult.Text, request.Tone, cancellationToken);
                    additional[extraLanguage] = extraRewritten;
                }

                _audit.Record(request.TenantId, request.UserId, result.ModelId, request.Text, rewritten, estimatedCost, result.LatencyMs, token.Audience, additional);
                _metrics.RecordSuccess(request.TenantId, result.ModelId, estimatedCost, result.LatencyMs, translationCount);

                var resolvedLocale = request.UiLocale ?? Options.DefaultUiLocale;
                var catalog = _localization.GetCatalog(resolvedLocale);

                return new TranslationResult
                {
                    RawTranslatedText = result.Text,
                    TranslatedText = rewritten,
                    SourceLanguage = sourceLanguage!,
                    TargetLanguage = request.TargetLanguage,
                    ModelId = result.ModelId,
                    Confidence = result.Confidence,
                    LatencyMs = result.LatencyMs,
                    CostUsd = estimatedCost,
                    UiLocale = catalog.Locale,
                    AdditionalTranslations = additional,
                    AdaptiveCard = BuildAdaptiveCard(catalog, rewritten, sourceLanguage!, request.TargetLanguage, result.ModelId, estimatedCost, result.LatencyMs, additional)
                };
            }
            catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException)
            {
                // 他のモデルを継続的に試行する。
            }
        }

        if (allowedProviders == 0)
        {
            _metrics.RecordFailure(request.TenantId, UsageMetricsService.FailureReasons.Compliance);
        }
        else
        {
            _metrics.RecordFailure(request.TenantId, UsageMetricsService.FailureReasons.Provider);
        }

        throw new TranslationException("利用可能なモデルが見つかりません。");
    }

    public async Task<RewriteResult> RewriteAsync(RewriteRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new TranslationException("改写する本文は必須です。");
        }

        if (string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new AuthenticationException("tenantId と userId は必須です。");
        }

        var token = await AcquireTokenAsync(request.TenantId, request.UserId, cancellationToken);
        _ = token; // トークン取得は成功の検証として使用する。

        var allowedProviders = 0;
        foreach (var provider in _providers)
        {
            var report = _compliance.Evaluate(request.Text, provider.Options);
            if (!report.Allowed)
            {
                continue;
            }

            allowedProviders++;
            var estimatedCost = request.Text.Length * provider.Options.CostPerCharUsd;
            if (!_budget.TryReserve(request.TenantId, estimatedCost))
            {
                _metrics.RecordFailure(request.TenantId, UsageMetricsService.FailureReasons.Budget);
                throw new BudgetExceededException("本日の翻訳予算を超過しています。");
            }

            try
            {
                var rewritten = await provider.RewriteAsync(request.Text, request.Tone, cancellationToken);
                var latency = provider.Options.LatencyTargetMs;
                _metrics.RecordSuccess(request.TenantId, provider.Options.Id, estimatedCost, latency, 1);
                return new RewriteResult(rewritten, provider.Options.Id, estimatedCost, latency);
            }
            catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException)
            {
                continue;
            }
        }

        if (allowedProviders == 0)
        {
            _metrics.RecordFailure(request.TenantId, UsageMetricsService.FailureReasons.Compliance);
        }
        else
        {
            _metrics.RecordFailure(request.TenantId, UsageMetricsService.FailureReasons.Provider);
        }

        throw new TranslationException("改写に使用できるモデルが見つかりません。");
    }

    public async Task<SummarizeResult> SummarizeAsync(SummarizeRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Context))
        {
            throw new TranslationException("要約対象のコンテキストは必須です。");
        }

        if (string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new AuthenticationException("tenantId と userId は必須です。");
        }

        var token = await AcquireTokenAsync(request.TenantId, request.UserId, cancellationToken);
        _ = token;

        var allowedProviders = 0;
        foreach (var provider in _providers)
        {
            var report = _compliance.Evaluate(request.Context, provider.Options);
            if (!report.Allowed)
            {
                continue;
            }

            allowedProviders++;
            var estimatedCost = request.Context.Length * provider.Options.CostPerCharUsd;
            if (!_budget.TryReserve(request.TenantId, estimatedCost))
            {
                _metrics.RecordFailure(request.TenantId, UsageMetricsService.FailureReasons.Budget);
                throw new BudgetExceededException("本日の翻訳予算を超過しています。");
            }

            try
            {
                var summary = await provider.SummarizeAsync(request.Context, cancellationToken);
                var latency = provider.Options.LatencyTargetMs;
                _metrics.RecordSuccess(request.TenantId, provider.Options.Id, estimatedCost, latency, 1);
                return new SummarizeResult(summary, provider.Options.Id, estimatedCost, latency);
            }
            catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException)
            {
                continue;
            }
        }

        if (allowedProviders == 0)
        {
            _metrics.RecordFailure(request.TenantId, UsageMetricsService.FailureReasons.Compliance);
        }
        else
        {
            _metrics.RecordFailure(request.TenantId, UsageMetricsService.FailureReasons.Provider);
        }

        throw new TranslationException("要約に使用できるモデルが見つかりません。");
    }

    private async Task<DetectionResult> DetectWithFallbackAsync(string text, CancellationToken cancellationToken)
    {
        var sanitized = text?.Trim() ?? string.Empty;
        DetectionResult? providerDetection = null;

        foreach (var provider in _providers)
        {
            try
            {
                providerDetection = await provider.DetectAsync(sanitized, cancellationToken);
                break;
            }
            catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException)
            {
                continue;
            }
        }

        var fallback = FallbackDetector.Detect(sanitized);
        return providerDetection is null
            ? fallback
            : DetectionResultExtensions.Merge(providerDetection, fallback);
    }

    private async Task<AccessToken> AcquireTokenAsync(string tenantId, string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(userId))
        {
            _metrics.RecordFailure(tenantId, UsageMetricsService.FailureReasons.Authentication);
            throw new AuthenticationException("ユーザー ID が空のため、トークンを取得できません。");
        }

        try
        {
            var token = await _tokenBroker.ExchangeOnBehalfOfAsync(tenantId, userId, cancellationToken);
            if (token.ExpiresOn <= DateTimeOffset.UtcNow)
            {
                _metrics.RecordFailure(tenantId, UsageMetricsService.FailureReasons.Authentication);
                throw new AuthenticationException("取得したトークンの有効期限が切れています。");
            }

            return token;
        }
        catch (AuthenticationException)
        {
            _metrics.RecordFailure(tenantId, UsageMetricsService.FailureReasons.Authentication);
            throw;
        }
    }

    private JsonObject BuildAdaptiveCard(
        LocalizationCatalog catalog,
        string translatedText,
        string sourceLanguage,
        string targetLanguage,
        string modelId,
        decimal cost,
        int latency,
        IReadOnlyDictionary<string, string> additionalTranslations)
    {
        static string Resolve(LocalizationCatalog catalog, string key) =>
            catalog.Strings.TryGetValue(key, out var value) ? value : key;

        var body = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "TextBlock",
                ["text"] = Resolve(catalog, "tla.ui.card.title"),
                ["wrap"] = true,
                ["weight"] = "Bolder",
                ["size"] = "Medium"
            },
            new JsonObject
            {
                ["type"] = "TextBlock",
                ["text"] = translatedText,
                ["wrap"] = true
            },
            new JsonObject
            {
                ["type"] = "TextBlock",
                ["text"] = string.Format(CultureInfo.InvariantCulture, Resolve(catalog, "tla.ui.card.modelLine"), sourceLanguage, targetLanguage, modelId),
                ["wrap"] = true,
                ["isSubtle"] = true
            },
            new JsonObject
            {
                ["type"] = "TextBlock",
                ["text"] = string.Format(CultureInfo.InvariantCulture, Resolve(catalog, "tla.ui.card.metrics"), cost, latency),
                ["wrap"] = true,
                ["spacing"] = "None",
                ["isSubtle"] = true
            }
        };

        if (additionalTranslations.Count > 0)
        {
            body.Add(new JsonObject
            {
                ["type"] = "TextBlock",
                ["text"] = Resolve(catalog, "tla.ui.card.additional"),
                ["wrap"] = true,
                ["weight"] = "Bolder",
                ["spacing"] = "Medium"
            });

            foreach (var kvp in additionalTranslations)
            {
                body.Add(new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["text"] = string.Format(CultureInfo.InvariantCulture, "{0}: {1}", kvp.Key, kvp.Value),
                    ["wrap"] = true
                });
            }
        }

        var actions = new JsonArray
        {
            CreateInsertAction(Resolve(catalog, "tla.ui.action.insert"), translatedText, targetLanguage)
        };

        foreach (var kvp in additionalTranslations)
        {
            var label = string.Format(CultureInfo.InvariantCulture, Resolve(catalog, "tla.ui.action.insertLocale"), kvp.Key);
            actions.Add(CreateInsertAction(label, kvp.Value, kvp.Key));
        }

        actions.Add(new JsonObject
        {
            ["type"] = "Action.Submit",
            ["title"] = Resolve(catalog, "tla.ui.action.showOriginal"),
            ["data"] = new JsonObject { ["action"] = "showOriginal" }
        });

        actions.Add(new JsonObject
        {
            ["type"] = "Action.Submit",
            ["title"] = Resolve(catalog, "tla.ui.action.changeLanguage"),
            ["data"] = new JsonObject { ["action"] = "changeLanguage" }
        });

        return new JsonObject
        {
            ["type"] = "AdaptiveCard",
            ["version"] = "1.5",
            ["body"] = body,
            ["actions"] = actions
        };
    }

    private static JsonObject CreateInsertAction(string title, string text, string language)
    {
        return new JsonObject
        {
            ["type"] = "Action.Submit",
            ["title"] = title,
            ["data"] = new JsonObject
            {
                ["action"] = "insertTranslation",
                ["text"] = text,
                ["language"] = language
            },
            ["msteams"] = new JsonObject
            {
                ["type"] = "messageBack",
                ["text"] = text,
                ["displayText"] = text,
                ["language"] = language
            }
        };
    }
}

public class TranslationException : Exception
{
    public TranslationException(string message) : base(message) { }
}

public class BudgetExceededException : Exception
{
    public BudgetExceededException(string message) : base(message) { }
}
