using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Authentication;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 负责处理回帖操作与语气校准的服务。
/// </summary>
public class ReplyService
{
    private readonly RewriteService _rewriteService;
    private readonly TranslationRouter _translationRouter;
    private readonly ITeamsReplyClient _teamsClient;
    private readonly ITokenBroker _tokenBroker;
    private readonly UsageMetricsService _metrics;
    private readonly PluginOptions _options;
    private readonly ILogger<ReplyService> _logger;

    public ReplyService(
        RewriteService rewriteService,
        TranslationRouter translationRouter,
        ITeamsReplyClient teamsClient,
        ITokenBroker tokenBroker,
        UsageMetricsService metrics,
        IOptions<PluginOptions>? options = null,
        ILogger<ReplyService>? logger = null)
    {
        _rewriteService = rewriteService;
        _translationRouter = translationRouter;
        _teamsClient = teamsClient;
        _tokenBroker = tokenBroker;
        _metrics = metrics;
        _options = options?.Value ?? new PluginOptions();
        _logger = logger ?? NullLogger<ReplyService>.Instance;
    }

    public async Task<ReplyResult> SendReplyAsync(ReplyRequest request, string finalTextOverride, string? toneApplied, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(finalTextOverride))
        {
            throw new ArgumentException("finalTextOverride は必須です。", nameof(finalTextOverride));
        }

        var context = PrepareContext(request);
        using var scope = BeginScope(context);
        _logger.LogInformation("Posting reply with override text and tone {Tone}", toneApplied ?? context.Tone ?? TranslationRequest.DefaultTone);
        return await PostAsync(context, finalTextOverride, toneApplied, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReplyResult> SendReplyAsync(ReplyRequest request, CancellationToken cancellationToken)
    {
        var context = PrepareContext(request);
        using var scope = BeginScope(context);

        var finalText = context.InitialText;
        string? toneApplied = null;

        if (!string.IsNullOrWhiteSpace(context.Tone) && context.Tone != TranslationRequest.DefaultTone)
        {
            _logger.LogInformation("Rewriting reply tone from {OriginalTone}", context.Tone);
            var rewrite = await _rewriteService.RewriteAsync(new RewriteRequest
            {
                Text = context.ReplyText,
                EditedText = request.EditedText,
                Tone = context.Tone!,
                TenantId = context.TenantId,
                UserId = context.UserId,
                ChannelId = context.ChannelId,
                UiLocale = context.UiLocale,
                UserAssertion = request.UserAssertion
            }, cancellationToken);

            finalText = rewrite.RewrittenText;
            toneApplied = context.Tone;
        }

        return await PostAsync(context, finalText, toneApplied, cancellationToken).ConfigureAwait(false);
    }

    private ReplyExecutionContext PrepareContext(ReplyRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.UserAssertion))
        {
            throw new AuthenticationException("缺少用户令牌。");
        }

        if (string.IsNullOrWhiteSpace(request.ThreadId))
        {
            throw new ArgumentException("threadId は必須です。", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new ArgumentException("tenantId と userId は必須です。", nameof(request));
        }

        if (_options.Security.AllowedReplyChannels.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(request.ChannelId) || !_options.Security.AllowedReplyChannels.Contains(request.ChannelId))
            {
                throw new ReplyAuthorizationException("指定されたチャネルでは返信権限がありません。");
            }
        }

        var replyText = string.IsNullOrWhiteSpace(request.ReplyText) ? request.Text : request.ReplyText;
        if (string.IsNullOrWhiteSpace(replyText))
        {
            throw new ArgumentException("replyText は必須です。", nameof(request));
        }

        var initialText = string.IsNullOrWhiteSpace(request.EditedText) ? replyText : request.EditedText!;
        if (string.IsNullOrWhiteSpace(initialText))
        {
            throw new ArgumentException("replyText は必須です。", nameof(request));
        }

        var tone = request.LanguagePolicy?.Tone;
        var targetLanguage = request.LanguagePolicy?.TargetLang ?? request.Language ?? string.Empty;

        var normalizedAdditionalLanguages = new List<string>();
        if (request.AdditionalTargetLanguages is not null)
        {
            var deduplicated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var language in request.AdditionalTargetLanguages)
            {
                if (string.IsNullOrWhiteSpace(language))
                {
                    continue;
                }

                if (language.Equals(targetLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (deduplicated.Add(language))
                {
                    normalizedAdditionalLanguages.Add(language);
                }
            }
        }

        return new ReplyExecutionContext(
            request.ThreadId,
            request.TenantId,
            request.UserId,
            request.ChannelId,
            replyText,
            initialText,
            request.UiLocale,
            targetLanguage,
            tone,
            request.BroadcastAdditionalLanguages,
            normalizedAdditionalLanguages,
            request.UserAssertion);
    }

    private async Task<ReplyResult> PostAsync(ReplyExecutionContext context, string finalText, string? toneApplied, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Resolving Teams reply token via OBO");

        AccessToken token;
        try
        {
            token = await _tokenBroker
                .ExchangeOnBehalfOfAsync(context.TenantId, context.UserId, context.UserAssertion, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AuthenticationException)
        {
            _metrics.RecordFailure(context.TenantId, UsageMetricsService.FailureReasons.Authentication);
            _logger.LogWarning("Token exchange failed for tenant {TenantId}", context.TenantId);
            throw;
        }

        var metadataTone = toneApplied ?? context.Tone ?? TranslationRequest.DefaultTone;

        var additionalTranslations = await TranslateAdditionalLanguagesAsync(context, finalText, cancellationToken).ConfigureAwait(false);
        var teamsTranslations = additionalTranslations
            .Select(translation => new TeamsReplyTranslation(
                translation.Language,
                translation.Text,
                translation.ModelId,
                translation.CostUsd,
                translation.LatencyMs))
            .ToList();

        var finalMessage = context.BroadcastAdditionalLanguages
            ? finalText
            : BuildMultilingualMessage(finalText, additionalTranslations, context.AdditionalTargetLanguages);

        var adaptiveCard = BuildAdaptiveCard(
            finalText,
            context.TargetLanguage,
            context.AdditionalTargetLanguages,
            additionalTranslations);

        _logger.LogInformation(
            "Posting Teams reply with {AdditionalCount} additional languages via {Mode}",
            context.AdditionalTargetLanguages.Count,
            context.BroadcastAdditionalLanguages ? "broadcast" : "attachment");

        try
        {
            var dispatches = new List<ReplyDispatch>();
            var response = await _teamsClient.SendReplyAsync(new TeamsReplyRequest(
                context.ThreadId,
                context.ChannelId,
                context.TenantId,
                finalMessage,
                context.TargetLanguage,
                metadataTone,
                token.Value,
                teamsTranslations,
                adaptiveCard,
                context.BroadcastAdditionalLanguages ? TeamsReplyDeliveryMode.Broadcast : TeamsReplyDeliveryMode.Attachment), cancellationToken).ConfigureAwait(false);

            dispatches.Add(new ReplyDispatch(response.MessageId, context.TargetLanguage, response.Status, response.SentAt));

            if (context.BroadcastAdditionalLanguages)
            {
                foreach (var translation in additionalTranslations)
                {
                    var broadcastResponse = await _teamsClient.SendReplyAsync(new TeamsReplyRequest(
                        context.ThreadId,
                        context.ChannelId,
                        context.TenantId,
                        translation.Text,
                        translation.Language,
                        metadataTone,
                        token.Value,
                        Array.Empty<TeamsReplyTranslation>(),
                        null,
                        TeamsReplyDeliveryMode.Broadcast), cancellationToken).ConfigureAwait(false);

                    dispatches.Add(new ReplyDispatch(
                        broadcastResponse.MessageId,
                        translation.Language,
                        broadcastResponse.Status,
                        broadcastResponse.SentAt,
                        translation.ModelId,
                        translation.CostUsd,
                        translation.LatencyMs));
                }
            }

            _logger.LogInformation(
                "Reply posted successfully with message {MessageId} and status {Status}",
                response.MessageId,
                response.Status);

            if (context.BroadcastAdditionalLanguages && additionalTranslations.Count > 0)
            {
                _logger.LogInformation(
                    "Broadcasted {Count} additional translations",
                    additionalTranslations.Count);
            }

            return new ReplyResult(response.MessageId, response.Status, finalMessage, toneApplied)
            {
                Language = context.TargetLanguage,
                PostedAt = response.SentAt,
                Dispatches = dispatches
            };
        }
        catch (TeamsReplyException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            _metrics.RecordFailure(context.TenantId, UsageMetricsService.FailureReasons.Authentication);
            _logger.LogWarning(
                "Reply forbidden for tenant {TenantId}, channel {ChannelId}: {Message}",
                context.TenantId,
                context.ChannelId ?? string.Empty,
                ex.Message);
            throw new ReplyAuthorizationException("指定されたチャネルでは返信権限がありません。");
        }
        catch (TeamsReplyException ex) when (ex.StatusCode == HttpStatusCode.PaymentRequired)
        {
            _metrics.RecordFailure(context.TenantId, UsageMetricsService.FailureReasons.Budget);
            _logger.LogWarning(
                "Reply rejected due to budget limit for tenant {TenantId}: {Message}",
                context.TenantId,
                ex.Message);
            throw new BudgetExceededException(string.IsNullOrWhiteSpace(ex.Message)
                ? "予算の上限を超えたため、返信を送信できません。"
                : ex.Message);
        }
        catch (TeamsReplyException ex)
        {
            _metrics.RecordFailure(context.TenantId, UsageMetricsService.FailureReasons.Provider);
            _logger.LogWarning(
                "Reply failed for tenant {TenantId} with status {StatusCode} and error code {ErrorCode}",
                context.TenantId,
                ex.StatusCode,
                ex.ErrorCode);
            throw new TranslationException(string.IsNullOrWhiteSpace(ex.Message)
                ? "返信の投稿に失敗しました。"
                : ex.Message);
        }
    }

    private async Task<IReadOnlyList<TranslationDelivery>> TranslateAdditionalLanguagesAsync(ReplyExecutionContext context, string finalText, CancellationToken cancellationToken)
    {
        if (context.AdditionalTargetLanguages.Count == 0)
        {
            return Array.Empty<TranslationDelivery>();
        }

        _logger.LogDebug(
            "Translating additional languages: {Languages}",
            context.AdditionalTargetLanguages);

        var results = new List<TranslationDelivery>();

        foreach (var language in context.AdditionalTargetLanguages)
        {
            var translation = await _translationRouter.TranslateAsync(new TranslationRequest
            {
                Text = finalText,
                SourceLanguage = context.TargetLanguage,
                TargetLanguage = language,
                TenantId = context.TenantId,
                UserId = context.UserId,
                ChannelId = context.ChannelId,
                ThreadId = context.ThreadId,
                Tone = context.Tone ?? TranslationRequest.DefaultTone,
                UiLocale = context.UiLocale,
                AdditionalTargetLanguages = new List<string>(),
                UserAssertion = context.UserAssertion
            }, cancellationToken).ConfigureAwait(false);

            results.Add(new TranslationDelivery(
                language,
                translation.TranslatedText,
                translation.ModelId,
                translation.CostUsd,
                translation.LatencyMs));
        }

        return results;
    }

    private static string BuildMultilingualMessage(
        string primaryText,
        IReadOnlyList<TranslationDelivery> additionalTranslations,
        IReadOnlyList<string> languageOrder)
    {
        if (additionalTranslations.Count == 0)
        {
            return primaryText;
        }

        var lookup = additionalTranslations.ToDictionary(
            translation => translation.Language,
            translation => translation.Text,
            StringComparer.OrdinalIgnoreCase);

        var builder = new StringBuilder();
        builder.Append(primaryText);

        foreach (var language in languageOrder)
        {
            if (!lookup.TryGetValue(language, out var translation))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append('[')
                .Append(language)
                .Append(']')
                .AppendLine();
            builder.Append(translation);
        }

        return builder.ToString().TrimEnd();
    }

    private static JsonObject? BuildAdaptiveCard(
        string finalText,
        string primaryLanguage,
        IReadOnlyList<string> languageOrder,
        IReadOnlyList<TranslationDelivery> additionalTranslations)
    {
        if (additionalTranslations.Count == 0)
        {
            return null;
        }

        var lookup = additionalTranslations.ToDictionary(
            translation => translation.Language,
            StringComparer.OrdinalIgnoreCase);

        var body = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "TextBlock",
                ["text"] = finalText,
                ["wrap"] = true,
                ["weight"] = "Bolder"
            },
            new JsonObject
            {
                ["type"] = "TextBlock",
                ["text"] = string.Format(System.Globalization.CultureInfo.InvariantCulture, "Primary language: {0}", primaryLanguage),
                ["wrap"] = true,
                ["spacing"] = "None",
                ["isSubtle"] = true
            }
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var language in languageOrder)
        {
            if (!lookup.TryGetValue(language, out var translation))
            {
                continue;
            }

            seen.Add(language);
            AppendTranslationBlocks(body, translation);
        }

        foreach (var translation in additionalTranslations)
        {
            if (!seen.Add(translation.Language))
            {
                continue;
            }

            AppendTranslationBlocks(body, translation);
        }

        return new JsonObject
        {
            ["type"] = "AdaptiveCard",
            ["version"] = "1.4",
            ["body"] = body
        };
    }

    private static void AppendTranslationBlocks(JsonArray body, TranslationDelivery translation)
    {
        body.Add(new JsonObject
        {
            ["type"] = "TextBlock",
            ["text"] = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}: {1}", translation.Language, translation.Text),
            ["wrap"] = true,
            ["spacing"] = "Medium"
        });

        body.Add(new JsonObject
        {
            ["type"] = "TextBlock",
            ["text"] = string.Format(System.Globalization.CultureInfo.InvariantCulture, "Model {0} • USD {1:F6} • {2} ms", translation.ModelId, translation.CostUsd, translation.LatencyMs),
            ["wrap"] = true,
            ["spacing"] = "None",
            ["isSubtle"] = true,
            ["size"] = "Small"
        });
    }

    private sealed record TranslationDelivery(
        string Language,
        string Text,
        string ModelId,
        decimal CostUsd,
        int LatencyMs);

    private sealed record ReplyExecutionContext(
        string ThreadId,
        string TenantId,
        string UserId,
        string? ChannelId,
        string ReplyText,
        string InitialText,
        string? UiLocale,
        string TargetLanguage,
        string? Tone,
        bool BroadcastAdditionalLanguages,
        IReadOnlyList<string> AdditionalTargetLanguages,
        string UserAssertion);

    private IDisposable BeginScope(ReplyExecutionContext context)
    {
        return _logger.BeginScope(new Dictionary<string, object>
        {
            ["ThreadId"] = context.ThreadId,
            ["TenantId"] = context.TenantId,
            ["UserId"] = context.UserId,
            ["ChannelId"] = context.ChannelId ?? string.Empty,
            ["TargetLanguage"] = context.TargetLanguage,
            ["Tone"] = context.Tone ?? TranslationRequest.DefaultTone,
            ["AdditionalLanguages"] = context.AdditionalTargetLanguages.Count,
            ["Broadcast"] = context.BroadcastAdditionalLanguages
        });
    }
}
