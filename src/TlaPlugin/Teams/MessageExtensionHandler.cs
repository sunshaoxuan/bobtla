using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TlaPlugin.Models;
using TlaPlugin.Services;

namespace TlaPlugin.Teams;

/// <summary>
/// Teams メッセージ拡張の業務ロジック。
/// </summary>
public class MessageExtensionHandler
{
    private readonly TranslationPipeline _pipeline;
    private readonly LocalizationCatalogService _localization;
    private readonly PluginOptions _options;

    public MessageExtensionHandler(TranslationPipeline pipeline, LocalizationCatalogService localization, IOptions<PluginOptions>? options = null)
    {
        _pipeline = pipeline;
        _localization = localization;
        _options = options?.Value ?? new PluginOptions();
    }

    private const double MinimumDetectionConfidence = 0.75;

    public async Task<JsonObject> HandleTranslateAsync(TranslationRequest request)
    {
        ApplyGlossarySelections(request);

        var locale = request.UiLocale ?? _options.DefaultUiLocale;
        var catalog = _localization.GetCatalog(locale);
        try
        {
            DetectionResult? detection = null;
            if (string.IsNullOrWhiteSpace(request.SourceLanguage))
            {
                detection = await _pipeline.DetectAsync(new LanguageDetectionRequest
                {
                    Text = request.Text,
                    TenantId = request.TenantId
                }, CancellationToken.None);

                if (detection.Confidence < MinimumDetectionConfidence)
                {
                    return BuildLanguageSelectionResponse(catalog, detection);
                }
            }

            var execution = await _pipeline.TranslateAsync(request, detection, CancellationToken.None);
            if (execution.RequiresLanguageSelection && execution.Detection is { } detectionResult)
            {
                return BuildLanguageSelectionResponse(catalog, detectionResult);
            }

            var result = execution.Translation ?? throw new TranslationException("翻訳結果を取得できませんでした。");
            return new JsonObject
            {
                ["type"] = "message",
                ["attachments"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["contentType"] = "application/vnd.microsoft.card.adaptive",
                        ["content"] = result.AdaptiveCard
                    }
                }
            };
        }
        catch (GlossaryConflictException ex)
        {
            return BuildGlossaryConflictCard(catalog, ex);
        }
        catch (BudgetExceededException ex)
        {
            return BuildErrorCard(catalog, "tla.error.budget.title", ex.Message);
        }
        catch (RateLimitExceededException ex)
        {
            return BuildErrorCard(catalog, "tla.error.rate.title", ex.Message);
        }
        catch (TranslationException ex)
        {
            return BuildErrorCard(catalog, "tla.error.translation.title", ex.Message);
        }
    }

    public Task<JsonObject> HandleOfflineDraftAsync(OfflineDraftRequest request)
    {
        var record = _pipeline.SaveDraft(request);
        return Task.FromResult(new JsonObject
        {
            ["type"] = "offlineDraftSaved",
            ["draftId"] = record.Id,
            ["status"] = record.Status,
            ["createdAt"] = record.CreatedAt.ToString("O")
        });
    }

    public async Task<JsonObject> HandleRewriteAsync(RewriteRequest request)
    {
        var locale = request.UiLocale ?? _options.DefaultUiLocale;
        var catalog = _localization.GetCatalog(locale);
        try
        {
            var rewritten = await _pipeline.RewriteAsync(request, CancellationToken.None);
            return new JsonObject
            {
                ["type"] = "rewriteResult",
                ["text"] = rewritten.RewrittenText,
                ["tone"] = request.Tone,
                ["modelId"] = rewritten.ModelId,
                ["cost"] = rewritten.CostUsd,
                ["latencyMs"] = rewritten.LatencyMs
            };
        }
        catch (BudgetExceededException ex)
        {
            return BuildErrorCard(catalog, "tla.error.budget.title", ex.Message);
        }
        catch (RateLimitExceededException ex)
        {
            return BuildErrorCard(catalog, "tla.error.rate.title", ex.Message);
        }
        catch (TranslationException ex)
        {
            return BuildErrorCard(catalog, "tla.error.translation.title", ex.Message);
        }
    }

    public async Task<JsonObject> HandleReplyAsync(ReplyRequest request)
    {
        var locale = request.UiLocale ?? _options.DefaultUiLocale;
        var catalog = _localization.GetCatalog(locale);
        try
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                throw new ArgumentException("text は必須です。", nameof(request));
            }

            var detection = await _pipeline.DetectAsync(new LanguageDetectionRequest
            {
                Text = request.Text,
                TenantId = request.TenantId
            }, CancellationToken.None);

            if (detection.Confidence < MinimumDetectionConfidence)
            {
                return BuildLanguageSelectionResponse(catalog, detection);
            }

            var targetLanguage = request.LanguagePolicy?.TargetLang
                ?? request.Language
                ?? _options.DefaultTargetLanguages.FirstOrDefault()
                ?? "ja";

            var translationRequest = new TranslationRequest
            {
                Text = request.Text,
                SourceLanguage = detection.Language,
                TargetLanguage = targetLanguage,
                TenantId = request.TenantId,
                UserId = request.UserId,
                ChannelId = request.ChannelId,
                Tone = TranslationRequest.DefaultTone,
                UiLocale = request.UiLocale,
                UseGlossary = false
            };

            var translationStep = await _pipeline.TranslateAsync(translationRequest, detection, CancellationToken.None);
            if (translationStep.RequiresLanguageSelection && translationStep.Detection is { } pendingDetection)
            {
                return BuildLanguageSelectionResponse(catalog, pendingDetection);
            }

            var translation = translationStep.Translation ?? throw new TranslationException("翻訳結果を取得できませんでした。");

            var tone = request.LanguagePolicy?.Tone ?? TranslationRequest.DefaultTone;

            var rewrite = await _pipeline.RewriteAsync(new RewriteRequest
            {
                Text = translation.TranslatedText,
                EditedText = request.EditedText,
                Tone = tone,
                TenantId = request.TenantId,
                UserId = request.UserId,
                ChannelId = request.ChannelId,
                UiLocale = request.UiLocale
            }, CancellationToken.None);

            var languagePolicy = new ReplyLanguagePolicy
            {
                TargetLang = string.IsNullOrWhiteSpace(request.LanguagePolicy?.TargetLang)
                    ? translation.TargetLanguage
                    : request.LanguagePolicy!.TargetLang,
                Tone = request.LanguagePolicy?.Tone ?? tone
            };

            var replyPayload = new ReplyRequest
            {
                ThreadId = request.ThreadId,
                ReplyText = translation.TranslatedText,
                Text = request.Text,
                EditedText = request.EditedText,
                TenantId = request.TenantId,
                UserId = request.UserId,
                ChannelId = request.ChannelId,
                Language = request.Language,
                UiLocale = request.UiLocale,
                LanguagePolicy = languagePolicy
            };

            var replyTone = tone != TranslationRequest.DefaultTone ? tone : null;
            var result = await _pipeline.PostReplyAsync(replyPayload, rewrite.RewrittenText, replyTone, CancellationToken.None);
            return new JsonObject
            {
                ["type"] = "replyPosted",
                ["status"] = result.Status,
                ["messageId"] = result.MessageId,
                ["language"] = result.Language,
                ["postedAt"] = result.PostedAt.ToString("O"),
                ["finalText"] = result.FinalText,
                ["toneApplied"] = result.ToneApplied,
                ["title"] = catalog.Strings.TryGetValue("tla.ui.reply.success", out var title) ? title : "Reply sent"
            };
        }
        catch (BudgetExceededException ex)
        {
            return BuildErrorCard(catalog, "tla.error.budget.title", ex.Message);
        }
        catch (RateLimitExceededException ex)
        {
            return BuildErrorCard(catalog, "tla.error.rate.title", ex.Message);
        }
        catch (TranslationException ex)
        {
            return BuildErrorCard(catalog, "tla.error.translation.title", ex.Message);
        }
        catch (ReplyAuthorizationException ex)
        {
            return BuildErrorCard(catalog, "tla.error.reply.title", ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BuildErrorCard(catalog, "tla.error.translation.title", ex.Message);
        }
    }

    private static JsonObject BuildErrorCard(LocalizationCatalog catalog, string titleKey, string message)
    {
        static string Resolve(LocalizationCatalog catalog, string key) =>
            catalog.Strings.TryGetValue(key, out var value) ? value : key;

        return new JsonObject
        {
            ["type"] = "message",
            ["attachments"] = new JsonArray
            {
                new JsonObject
                {
                    ["contentType"] = "application/vnd.microsoft.card.adaptive",
                    ["content"] = new JsonObject
                    {
                        ["type"] = "AdaptiveCard",
                        ["version"] = "1.5",
                        ["body"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "TextBlock",
                                ["text"] = Resolve(catalog, titleKey),
                                ["wrap"] = true,
                                ["weight"] = "Bolder"
                            },
                            new JsonObject
                            {
                                ["type"] = "TextBlock",
                                ["text"] = message,
                                ["wrap"] = true
                            }
                        }
                    }
                }
            }
        };
    }

    private static JsonObject BuildLanguageSelectionResponse(LocalizationCatalog catalog, DetectionResult detection)
    {
        static string Resolve(LocalizationCatalog catalog, string key) =>
            catalog.Strings.TryGetValue(key, out var value) ? value : key;

        var candidates = new JsonArray();
        foreach (var candidate in detection.Candidates)
        {
            candidates.Add(new JsonObject
            {
                ["language"] = candidate.Language,
                ["confidence"] = candidate.Confidence
            });
        }

        return new JsonObject
        {
            ["type"] = "languageSelection",
            ["title"] = Resolve(catalog, "tla.error.detection.title"),
            ["message"] = Resolve(catalog, "tla.error.detection.body"),
            ["candidates"] = candidates
        };
    }

    private JsonObject BuildGlossaryConflictCard(LocalizationCatalog catalog, GlossaryConflictException exception)
    {
        static string Resolve(LocalizationCatalog catalog, string key) =>
            catalog.Strings.TryGetValue(key, out var value) ? value : key;

        var body = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "TextBlock",
                ["text"] = Resolve(catalog, "tla.ui.glossary.conflictTitle"),
                ["wrap"] = true,
                ["weight"] = "Bolder",
                ["size"] = "Medium"
            },
            new JsonObject
            {
                ["type"] = "TextBlock",
                ["text"] = Resolve(catalog, "tla.ui.glossary.conflictDescription"),
                ["wrap"] = true,
                ["spacing"] = "Small"
            }
        };

        var conflicts = exception.Result.Matches
            .Where(match => match.HasConflict)
            .ToList();

        foreach (var conflict in conflicts)
        {
            body.Add(new JsonObject
            {
                ["type"] = "TextBlock",
                ["text"] = string.Format(CultureInfo.InvariantCulture, Resolve(catalog, "tla.ui.glossary.conflictItem"), conflict.Source, conflict.Occurrences),
                ["wrap"] = true,
                ["weight"] = "Bolder",
                ["spacing"] = "Medium"
            });

            var ordered = conflict.Candidates
                .OrderBy(candidate => candidate.Priority)
                .ThenBy(candidate => candidate.Target, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ordered.Count == 0)
            {
                continue;
            }

            var preferred = ordered.First();
            var preferredChoice = EncodeChoice(conflict.Source, GlossaryDecisionKind.UsePreferred, preferred);

            var choices = new JsonArray
            {
                new JsonObject
                {
                    ["title"] = string.Format(CultureInfo.InvariantCulture, Resolve(catalog, "tla.ui.glossary.option.preferred"), preferred.Target, preferred.Scope),
                    ["value"] = preferredChoice
                }
            };

            if (ordered.Count > 1)
            {
                var alternate = ordered.Skip(1).First();
                choices.Add(new JsonObject
                {
                    ["title"] = string.Format(CultureInfo.InvariantCulture, Resolve(catalog, "tla.ui.glossary.option.alternative"), alternate.Target, alternate.Scope),
                    ["value"] = EncodeChoice(conflict.Source, GlossaryDecisionKind.UseAlternative, alternate)
                });
            }

            choices.Add(new JsonObject
            {
                ["title"] = Resolve(catalog, "tla.ui.glossary.option.original"),
                ["value"] = EncodeChoice(conflict.Source, GlossaryDecisionKind.KeepOriginal, null)
            });

            body.Add(new JsonObject
            {
                ["type"] = "Input.ChoiceSet",
                ["id"] = $"glossary::{conflict.Source}",
                ["style"] = "expanded",
                ["isMultiSelect"] = false,
                ["value"] = preferredChoice,
                ["choices"] = choices
            });
        }

        var data = new JsonObject
        {
            ["action"] = "resolveGlossary",
            ["tenantId"] = exception.Request.TenantId,
            ["userId"] = exception.Request.UserId,
            ["channelId"] = exception.Request.ChannelId ?? string.Empty,
            ["targetLanguage"] = exception.Request.TargetLanguage,
            ["sourceLanguage"] = exception.Request.SourceLanguage ?? string.Empty,
            ["tone"] = exception.Request.Tone,
            ["useGlossary"] = exception.Request.UseGlossary,
            ["uiLocale"] = exception.Request.UiLocale ?? string.Empty,
            ["text"] = exception.Request.Text,
            ["additionalTargetLanguages"] = new JsonArray(exception.Request.AdditionalTargetLanguages.Select(language => (JsonNode)language).ToArray())
        };

        var actions = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "Action.Submit",
                ["title"] = Resolve(catalog, "tla.ui.glossary.submit"),
                ["data"] = data
            },
            new JsonObject
            {
                ["type"] = "Action.Submit",
                ["title"] = Resolve(catalog, "tla.ui.glossary.cancel"),
                ["data"] = new JsonObject
                {
                    ["action"] = "cancelGlossary"
                }
            }
        };

        return new JsonObject
        {
            ["type"] = "message",
            ["attachments"] = new JsonArray
            {
                new JsonObject
                {
                    ["contentType"] = "application/vnd.microsoft.card.adaptive",
                    ["content"] = new JsonObject
                    {
                        ["type"] = "AdaptiveCard",
                        ["version"] = "1.5",
                        ["body"] = body,
                        ["actions"] = actions
                    }
                }
            }
        };
    }

    private static string EncodeChoice(string source, GlossaryDecisionKind kind, GlossaryCandidateDetail? candidate)
    {
        var payload = new JsonObject
        {
            ["source"] = source,
            ["kind"] = kind.ToString()
        };

        if (candidate is not null)
        {
            payload["target"] = candidate.Target;
            payload["scope"] = candidate.Scope;
        }

        return payload.ToJsonString(new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    private static void ApplyGlossarySelections(TranslationRequest request)
    {
        if (request.ExtensionData is null || request.ExtensionData.Count == 0)
        {
            return;
        }

        var processedKeys = new List<string>();
        foreach (var pair in request.ExtensionData)
        {
            if (!pair.Key.StartsWith("glossary::", StringComparison.Ordinal))
            {
                continue;
            }

            processedKeys.Add(pair.Key);

            var source = pair.Key.Substring("glossary::".Length);
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var decision = DecodeGlossaryDecision(pair.Value);
            if (decision is null)
            {
                continue;
            }

            request.GlossaryDecisions[source] = decision;
        }

        foreach (var key in processedKeys)
        {
            request.ExtensionData.Remove(key);
        }
    }

    private static GlossaryDecision? DecodeGlossaryDecision(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => DecodeGlossaryDecisionFromString(element.GetString()),
            JsonValueKind.Object => DecodeGlossaryDecisionFromElement(element),
            _ => null
        };
    }

    private static GlossaryDecision? DecodeGlossaryDecisionFromString(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            return DecodeGlossaryDecisionFromElement(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static GlossaryDecision? DecodeGlossaryDecisionFromElement(JsonElement payload)
    {
        if (!payload.TryGetProperty("kind", out var kindProperty) || kindProperty.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        if (!Enum.TryParse(kindProperty.GetString(), true, out GlossaryDecisionKind kind))
        {
            return null;
        }

        var decision = new GlossaryDecision
        {
            Kind = kind
        };

        if (payload.TryGetProperty("target", out var targetProperty) && targetProperty.ValueKind == JsonValueKind.String)
        {
            decision.Target = targetProperty.GetString();
        }

        if (payload.TryGetProperty("scope", out var scopeProperty) && scopeProperty.ValueKind == JsonValueKind.String)
        {
            decision.Scope = scopeProperty.GetString();
        }

        return decision;
    }
}
