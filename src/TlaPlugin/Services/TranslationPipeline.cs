using System;
using System.Collections.Generic;
using System.Linq;
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

        GlossaryApplicationResult glossaryResult;
        if (request.UseGlossary)
        {
            glossaryResult = _glossary.Apply(request.Text, request.TenantId, request.ChannelId, request.UserId, request.GlossaryDecisions);
            if (glossaryResult.RequiresResolution)
            {
                throw new GlossaryConflictException(glossaryResult, request);
            }
        }
        else
        {
            glossaryResult = new GlossaryApplicationResult
            {
                Text = request.Text,
                Matches = Array.Empty<GlossaryMatchDetail>()
            };
        }

        var matchSnapshots = glossaryResult.Matches.Select(match => match.Clone()).ToList();

        var resolvedRequest = new TranslationRequest
        {
            Text = glossaryResult.Text,
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            TenantId = request.TenantId,
            UserId = request.UserId,
            ChannelId = request.ChannelId,
            Tone = request.Tone,
            AdditionalTargetLanguages = new List<string>(request.AdditionalTargetLanguages),
            UseGlossary = request.UseGlossary,
            UiLocale = request.UiLocale,
            GlossaryDecisions = request.GlossaryDecisions.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Clone(),
                StringComparer.OrdinalIgnoreCase)
        };

        if (string.IsNullOrEmpty(resolvedRequest.SourceLanguage))
        {
            var detection = _detector.Detect(resolvedRequest.Text);
            if (detection.Confidence < 0.75)
            {
                throw new LanguageDetectionLowConfidenceException(detection);
            }
            resolvedRequest.SourceLanguage = detection.Language;
        }

        if (_cache.TryGet(resolvedRequest, out var cached))
        {
            cached.SetGlossaryMatches(matchSnapshots.Select(match => match.Clone()));
            return cached;
        }

        using var lease = await _throttle.AcquireAsync(resolvedRequest.TenantId, cancellationToken);
        var result = await _router.TranslateAsync(resolvedRequest, cancellationToken);
        result.SetGlossaryMatches(matchSnapshots);
        _cache.Set(resolvedRequest, result);
        return result;
    }

    public OfflineDraftRecord SaveDraft(OfflineDraftRequest request)
    {
        return _drafts.SaveDraft(request);
    }

    public async Task<string> RewriteAsync(RewriteRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new TranslationException("改写内容不能为空。");
        }

        using var lease = await _throttle.AcquireAsync(request.TenantId, cancellationToken);
        return await _router.RewriteAsync(request, cancellationToken);
    }

    public Task<ReplyResult> PostReplyAsync(ReplyRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new TranslationException("回帖内容不能为空。");
        }

        return _router.PostReplyAsync(request, cancellationToken);
    }
}
