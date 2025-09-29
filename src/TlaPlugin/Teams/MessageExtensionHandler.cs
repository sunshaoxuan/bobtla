using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TlaPlugin.Models;
using TlaPlugin.Services;

namespace TlaPlugin.Teams;

/// <summary>
/// Teams メッセージ拡張のビジネスロジック。
/// </summary>
public class MessageExtensionHandler
{
    private readonly TranslationPipeline _pipeline;

    public MessageExtensionHandler(TranslationPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public async Task<JsonObject> HandleTranslateAsync(TranslationRequest request)
    {
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
            return BuildErrorCard("予算制限", ex.Message);
        }
        catch (RateLimitExceededException ex)
        {
            return BuildErrorCard("レート制限", ex.Message);
        }
        catch (TranslationException ex)
        {
            return BuildErrorCard("翻訳エラー", ex.Message);
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

    private static JsonObject BuildErrorCard(string title, string message)
    {
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
                                ["text"] = title,
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
