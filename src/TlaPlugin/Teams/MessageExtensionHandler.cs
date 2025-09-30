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
}
