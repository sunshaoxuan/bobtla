using System;
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
    private readonly TranslationRouter _router;
    private readonly PluginOptions _options;

    public ReplyService(TranslationRouter router, IOptions<PluginOptions>? options = null)
    {
        _router = router;
        _options = options?.Value ?? new PluginOptions();
    }

    public async Task<ReplyResult> SendReplyAsync(ReplyRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ThreadId))
        {
            throw new ArgumentException("threadId は必須です。", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ReplyText))
        {
            throw new ArgumentException("replyText は必須です。", nameof(request));
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

        string finalText = request.ReplyText;
        string? toneApplied = null;
        if (!string.IsNullOrWhiteSpace(request.LanguagePolicy?.Tone) && request.LanguagePolicy!.Tone != TranslationRequest.DefaultTone)
        {
            var rewrite = await _router.RewriteAsync(new RewriteRequest
            {
                Text = request.ReplyText,
                Tone = request.LanguagePolicy.Tone,
                TenantId = request.TenantId,
                UserId = request.UserId,
                ChannelId = request.ChannelId
            }, cancellationToken);
            finalText = rewrite.RewrittenText;
            toneApplied = request.LanguagePolicy.Tone;
        }

        return new ReplyResult(Guid.NewGuid().ToString(), "sent", finalText, toneApplied);
    }
}
