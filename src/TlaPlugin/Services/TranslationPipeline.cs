using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 翻訳フローを編成し Teams 応答を生成するパイプライン。
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
            throw new TranslationException("翻訳する本文は必須です。");
        }

        if (request.Text.Length > _options.MaxCharactersPerRequest)
        {
            throw new TranslationException("許容される文字数を超えています。");
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
                throw new TranslationException("言語を自動判定できません。手動で選択してください。");
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
