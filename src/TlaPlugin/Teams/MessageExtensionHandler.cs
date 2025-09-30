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

    public async Task<JsonObject> HandleTranslateAsync(TranslationRequest request)
    {
        var locale = request.UiLocale ?? _options.DefaultUiLocale;
        var catalog = _localization.GetCatalog(locale);
        try
        {
            var result = await _pipeline.ExecuteAsync(request, CancellationToken.None);
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
}
