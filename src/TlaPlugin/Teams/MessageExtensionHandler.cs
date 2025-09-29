using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TlaPlugin.Models;
using TlaPlugin.Services;

namespace TlaPlugin.Teams;

/// <summary>
/// 承载 Teams 消息扩展业务逻辑的处理器。
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
            return BuildErrorCard("预算限制", ex.Message);
        }
        catch (RateLimitExceededException ex)
        {
            return BuildErrorCard("速率限制", ex.Message);
        }
        catch (TranslationException ex)
        {
            return BuildErrorCard("翻译错误", ex.Message);
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
