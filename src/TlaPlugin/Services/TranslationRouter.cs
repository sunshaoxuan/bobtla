using System;
using System.Collections.Generic;
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
/// 複数モデルを評価しフォールバックを行うルーター。
/// </summary>
public class TranslationRouter
{
    private readonly IReadOnlyList<IModelProvider> _providers;
    private readonly ComplianceGateway _compliance;
    private readonly BudgetGuard _budget;
    private readonly AuditLogger _audit;
    private readonly ToneTemplateService _tones;
    private readonly ITokenBroker _tokenBroker;

    public TranslationRouter(ModelProviderFactory providerFactory, ComplianceGateway compliance, BudgetGuard budget, AuditLogger audit, ToneTemplateService tones, ITokenBroker tokenBroker, IOptions<PluginOptions>? options = null)
    {
        _providers = providerFactory.CreateProviders();
        _compliance = compliance;
        _budget = budget;
        _audit = audit;
        _tones = tones;
        _tokenBroker = tokenBroker;
        Options = options?.Value ?? new PluginOptions();
    }

    public PluginOptions Options { get; }

    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        var sourceLanguage = request.SourceLanguage;
        DetectionResult? detection = null;
        if (string.IsNullOrEmpty(sourceLanguage))
        {
            detection = await _providers.First().DetectAsync(request.Text, cancellationToken);
            sourceLanguage = detection.Language;
        }

        if (string.IsNullOrEmpty(request.UserId))
        {
            throw new AuthenticationException("ユーザー ID が空のためトークンを取得できません。");
        }

        var token = await _tokenBroker.ExchangeOnBehalfOfAsync(request.TenantId, request.UserId, cancellationToken);
        if (token.ExpiresOn <= DateTimeOffset.UtcNow)
        {
            throw new AuthenticationException("取得したトークンが既に失効しています。");
        }

        var promptPrefix = _tones.GetPromptPrefix(request.Tone);

        foreach (var provider in _providers)
        {
            var report = _compliance.Evaluate(request.Text, provider.Options);
            if (!report.Allowed)
            {
                continue;
            }

            var translationCount = 1 + request.AdditionalTargetLanguages.Count;
            var estimatedCost = request.Text.Length * provider.Options.CostPerCharUsd * translationCount;
            if (!_budget.TryReserve(request.TenantId, estimatedCost))
            {
                throw new BudgetExceededException("本日の翻訳予算を超過しました。");
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

                return new TranslationResult
                {
                    TranslatedText = rewritten,
                    SourceLanguage = sourceLanguage!,
                    TargetLanguage = request.TargetLanguage,
                    ModelId = result.ModelId,
                    Confidence = result.Confidence,
                    LatencyMs = result.LatencyMs,
                    CostUsd = estimatedCost,
                    AdditionalTranslations = additional,
                    AdaptiveCard = BuildAdaptiveCard(rewritten, sourceLanguage!, request.TargetLanguage, result.ModelId, estimatedCost, result.LatencyMs, additional)
                };
            }
            catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException)
            {
                // フォールバック継続。
            }
        }

        throw new TranslationException("利用可能なモデルが見つかりませんでした。");
    }

    private static JsonObject BuildAdaptiveCard(string translatedText, string sourceLanguage, string targetLanguage, string modelId, decimal cost, int latency, IReadOnlyDictionary<string, string> additionalTranslations)
    {
        var body = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "TextBlock",
                ["text"] = "翻訳結果",
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
                ["text"] = $"{sourceLanguage} → {targetLanguage} | モデル: {modelId}",
                ["wrap"] = true,
                ["isSubtle"] = true
            },
            new JsonObject
            {
                ["type"] = "TextBlock",
                ["text"] = $"コスト: ${cost:F4} | レイテンシ: {latency}ms",
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
                ["text"] = "追加翻訳",
                ["wrap"] = true,
                ["weight"] = "Bolder",
                ["spacing"] = "Medium"
            });

            foreach (var kvp in additionalTranslations)
            {
                body.Add(new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["text"] = $"{kvp.Key}: {kvp.Value}",
                    ["wrap"] = true
                });
            }
        }

        return new JsonObject
        {
            ["type"] = "AdaptiveCard",
            ["version"] = "1.5",
            ["body"] = body,
            ["actions"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "Action.Submit",
                    ["title"] = "原文を表示",
                    ["data"] = new JsonObject { ["action"] = "showOriginal" }
                },
                new JsonObject
                {
                    ["type"] = "Action.Submit",
                    ["title"] = "他言語を選択",
                    ["data"] = new JsonObject { ["action"] = "changeLanguage" }
                }
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
