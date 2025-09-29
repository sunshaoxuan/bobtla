using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 负责调度翻译并生成 Teams 响应的管线。
/// </summary>
public class TranslationPipeline
{
    private readonly TranslationRouter _router;
    private readonly GlossaryService _glossary;
    private readonly OfflineDraftStore _drafts;
    private readonly LanguageDetector _detector;
    private readonly TranslationCache _cache;
    private readonly TranslationThrottle _throttle;
    private readonly PluginOptions _options;

    public TranslationPipeline(TranslationRouter router, GlossaryService glossary, OfflineDraftStore drafts, LanguageDetector detector, TranslationCache cache, TranslationThrottle throttle, IOptions<PluginOptions>? options = null)
    {
        _router = router;
        _glossary = glossary;
        _drafts = drafts;
        _detector = detector;
        _cache = cache;
        _throttle = throttle;
        _options = options?.Value ?? new PluginOptions();
    }

    public async Task<TranslationResult> ExecuteAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new TranslationException("翻译内容为空。");
        }

        if (request.Text.Length > _options.MaxCharactersPerRequest)
        {
            throw new TranslationException("文本长度超过上限。");
        }

        var resolvedRequest = new TranslationRequest
        {
            Text = request.UseGlossary ? _glossary.Apply(request.Text, request.TenantId, request.ChannelId, request.UserId) : request.Text,
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            TenantId = request.TenantId,
            UserId = request.UserId,
            ChannelId = request.ChannelId,
            Tone = request.Tone,
            AdditionalTargetLanguages = new List<string>(request.AdditionalTargetLanguages),
            UseGlossary = request.UseGlossary
        };

        if (string.IsNullOrEmpty(resolvedRequest.SourceLanguage))
        {
            var detection = _detector.Detect(resolvedRequest.Text);
            if (detection.Confidence < 0.7)
            {
                throw new TranslationException("无法识别语言，请手动指定。");
            }
            resolvedRequest.SourceLanguage = detection.Language;
        }

        if (_cache.TryGet(resolvedRequest, out var cached))
        {
            return cached;
        }

        using var lease = await _throttle.AcquireAsync(resolvedRequest.TenantId, cancellationToken);
        var result = await _router.TranslateAsync(resolvedRequest, cancellationToken);
        _cache.Set(resolvedRequest, result);
        return result;
    }

    public OfflineDraftRecord SaveDraft(OfflineDraftRequest request)
    {
        return _drafts.SaveDraft(request);
    }
}
