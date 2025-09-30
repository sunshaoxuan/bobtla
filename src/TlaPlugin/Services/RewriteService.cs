using System;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 提供面向 API 的改写调用封装，处理配额与用户编辑后的内容。
/// </summary>
public class RewriteService
{
    private readonly TranslationRouter _router;
    private readonly TranslationThrottle _throttle;

    public RewriteService(TranslationRouter router, TranslationThrottle throttle)
    {
        _router = router;
        _throttle = throttle;
    }

    public async Task<RewriteResult> RewriteAsync(RewriteRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new AuthenticationException("tenantId と userId は必須です。");
        }

        var textToRewrite = !string.IsNullOrWhiteSpace(request.EditedText)
            ? request.EditedText!
            : request.Text;

        if (string.IsNullOrWhiteSpace(textToRewrite))
        {
            throw new TranslationException("改写内容不能为空。");
        }

        using var lease = await _throttle.AcquireAsync(request.TenantId, cancellationToken);
        var normalizedRequest = new RewriteRequest
        {
            Text = textToRewrite,
            Tone = request.Tone,
            TenantId = request.TenantId,
            UserId = request.UserId,
            ChannelId = request.ChannelId
        };

        return await _router.RewriteAsync(normalizedRequest, cancellationToken);
    }
}
