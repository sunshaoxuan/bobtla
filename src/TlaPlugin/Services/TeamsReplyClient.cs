using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace TlaPlugin.Services;

/// <summary>
/// Graph API を通じて Teams への返信メッセージを送信するクライアント。
/// </summary>
public class TeamsReplyClient : ITeamsReplyClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public TeamsReplyClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/", UriKind.Absolute);
        }
    }

    public async Task<TeamsReplyResponse> SendReplyAsync(TeamsReplyRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var path = BuildRequestPath(request);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.AccessToken);

        var payload = new
        {
            replyToId = request.ThreadId,
            body = new
            {
                contentType = "html",
                content = FormatContent(request.FinalText)
            },
            channelIdentity = string.IsNullOrEmpty(request.ChannelId)
                ? null
                : new
                {
                    channelId = request.ChannelId,
                    teamId = request.TenantId
                },
            channelData = new
            {
                metadata = new
                {
                    language = request.Language,
                    tone = request.Tone,
                    additionalTranslations = request.AdditionalTranslations.Count > 0
                        ? request.AdditionalTranslations
                        : null
                }
            },
            attachments = request.AdaptiveCard is null
                ? null
                : new object[]
                {
                    new
                    {
                        contentType = "application/vnd.microsoft.card.adaptive",
                        content = request.AdaptiveCard
                    }
                }
        };

        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var responseBody = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateException(response.StatusCode, responseBody);
        }

        var messageId = Guid.NewGuid().ToString();
        DateTimeOffset postedAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            try
            {
                using var document = JsonDocument.Parse(responseBody);
                if (document.RootElement.TryGetProperty("id", out var idProperty) && idProperty.ValueKind == JsonValueKind.String)
                {
                    messageId = idProperty.GetString() ?? messageId;
                }

                if (document.RootElement.TryGetProperty("createdDateTime", out var createdProperty) && createdProperty.ValueKind == JsonValueKind.String)
                {
                    if (DateTimeOffset.TryParse(createdProperty.GetString(), out var parsed))
                    {
                        postedAt = parsed;
                    }
                }

                if (document.RootElement.TryGetProperty("lastModifiedDateTime", out var modifiedProperty) && modifiedProperty.ValueKind == JsonValueKind.String)
                {
                    if (DateTimeOffset.TryParse(modifiedProperty.GetString(), out var parsedModified))
                    {
                        postedAt = parsedModified;
                    }
                }
            }
            catch (JsonException)
            {
                // 成功レスポンスだが JSON ではない場合は既定値を利用する。
            }
        }

        var status = response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK
            ? "sent"
            : response.StatusCode.ToString().ToLowerInvariant();

        return new TeamsReplyResponse(messageId, postedAt, status);
    }

    private static string BuildRequestPath(TeamsReplyRequest request)
    {
        var thread = Uri.EscapeDataString(request.ThreadId);
        if (!string.IsNullOrEmpty(request.ChannelId))
        {
            var tenant = Uri.EscapeDataString(request.TenantId);
            var channel = Uri.EscapeDataString(request.ChannelId);
            return $"teams/{tenant}/channels/{channel}/messages/{thread}/replies";
        }

        return $"chats/{thread}/messages";
    }

    private static string FormatContent(string text)
    {
        var encoded = HtmlEncoder.Default.Encode(text ?? string.Empty);
        return encoded.Replace("\n", "<br />", StringComparison.Ordinal);
    }

    private static TeamsReplyException CreateException(HttpStatusCode statusCode, string body)
    {
        string message = string.IsNullOrWhiteSpace(body)
            ? statusCode.ToString()
            : body;

        string? errorCode = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                {
                    if (error.TryGetProperty("message", out var messageProperty) && messageProperty.ValueKind == JsonValueKind.String)
                    {
                        message = messageProperty.GetString() ?? message;
                    }

                    if (error.TryGetProperty("code", out var codeProperty) && codeProperty.ValueKind == JsonValueKind.String)
                    {
                        errorCode = codeProperty.GetString();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // ignore malformed error payloads
        }

        return new TeamsReplyException(statusCode, message, errorCode);
    }
}

public interface ITeamsReplyClient
{
    Task<TeamsReplyResponse> SendReplyAsync(TeamsReplyRequest request, CancellationToken cancellationToken);
}

public sealed record TeamsReplyRequest(
    string ThreadId,
    string? ChannelId,
    string TenantId,
    string FinalText,
    string Language,
    string Tone,
    string AccessToken,
    IReadOnlyDictionary<string, string> AdditionalTranslations,
    JsonObject? AdaptiveCard);

public sealed record TeamsReplyResponse(string MessageId, DateTimeOffset SentAt, string Status);

public class TeamsReplyException : Exception
{
    public TeamsReplyException(HttpStatusCode statusCode, string message, string? errorCode)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public HttpStatusCode StatusCode { get; }

    public string? ErrorCode { get; }
}
