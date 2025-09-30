using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly ITeamsReplyClient _teamsClient;
    private readonly ITokenBroker _tokenBroker;
    private readonly UsageMetricsService _metrics;
    private readonly PluginOptions _options;

    public ReplyService(
        RewriteService rewriteService,
        ITeamsReplyClient teamsClient,
        ITokenBroker tokenBroker,
        UsageMetricsService metrics,
        IOptions<PluginOptions>? options = null)
    {
        _rewriteService = rewriteService;
        _teamsClient = teamsClient;
        _tokenBroker = tokenBroker;
        _metrics = metrics;
        _options = options?.Value ?? new PluginOptions();
    }

    public async Task<ReplyResult> SendReplyAsync(ReplyRequest request, string finalTextOverride, string? toneApplied, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(finalTextOverride))
        {
            throw new ArgumentException("finalTextOverride は必須です。", nameof(finalTextOverride));
        }

        var context = PrepareContext(request);
        return await PostAsync(context, finalTextOverride, toneApplied, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReplyResult> SendReplyAsync(ReplyRequest request, CancellationToken cancellationToken)
    {
        var context = PrepareContext(request);

        var finalText = context.InitialText;
        string? toneApplied = null;

        if (!string.IsNullOrWhiteSpace(context.Tone) && context.Tone != TranslationRequest.DefaultTone)
        {
            var rewrite = await _rewriteService.RewriteAsync(new RewriteRequest
            {
                Text = context.ReplyText,
                EditedText = request.EditedText,
                Tone = context.Tone!,
                TenantId = context.TenantId,
                UserId = context.UserId,
                ChannelId = context.ChannelId,
                UiLocale = context.UiLocale
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

        return new ReplyExecutionContext(
            request.ThreadId,
            request.TenantId,
            request.UserId,
            request.ChannelId,
            replyText,
            initialText,
            request.UiLocale,
            targetLanguage,
            tone);
    }

    private async Task<ReplyResult> PostAsync(ReplyExecutionContext context, string finalText, string? toneApplied, CancellationToken cancellationToken)
    {
        var token = await _tokenBroker.ExchangeOnBehalfOfAsync(context.TenantId, context.UserId, cancellationToken).ConfigureAwait(false);

        var metadataTone = toneApplied ?? context.Tone ?? TranslationRequest.DefaultTone;

        try
        {
            var response = await _teamsClient.SendReplyAsync(new TeamsReplyRequest(
                context.ThreadId,
                context.ChannelId,
                context.TenantId,
                finalText,
                context.TargetLanguage,
                metadataTone,
                token.Value), cancellationToken).ConfigureAwait(false);

            return new ReplyResult(response.MessageId, response.Status, finalText, toneApplied)
            {
                Language = context.TargetLanguage,
                PostedAt = response.SentAt
            };
        }
        catch (TeamsReplyException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            _metrics.RecordFailure(context.TenantId, UsageMetricsService.FailureReasons.Authentication);
            throw new ReplyAuthorizationException("指定されたチャネルでは返信権限がありません。");
        }
        catch (TeamsReplyException ex) when (ex.StatusCode == HttpStatusCode.PaymentRequired)
        {
            _metrics.RecordFailure(context.TenantId, UsageMetricsService.FailureReasons.Budget);
            throw new BudgetExceededException(string.IsNullOrWhiteSpace(ex.Message)
                ? "予算の上限を超えたため、返信を送信できません。"
                : ex.Message);
        }
        catch (TeamsReplyException ex)
        {
            _metrics.RecordFailure(context.TenantId, UsageMetricsService.FailureReasons.Provider);
            throw new TranslationException(string.IsNullOrWhiteSpace(ex.Message)
                ? "返信の投稿に失敗しました。"
                : ex.Message);
        }
    }

    private sealed record ReplyExecutionContext(
        string ThreadId,
        string TenantId,
        string UserId,
        string? ChannelId,
        string ReplyText,
        string InitialText,
        string? UiLocale,
        string TargetLanguage,
        string? Tone);
}
