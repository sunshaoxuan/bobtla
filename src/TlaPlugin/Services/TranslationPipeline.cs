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
    private readonly RewriteService _rewrite;
    private readonly ReplyService _replyService;
    private readonly PluginOptions _options;

    public TranslationPipeline(
        TranslationRouter router,
        GlossaryService glossary,
        OfflineDraftStore drafts,
        LanguageDetector detector,
        TranslationCache cache,
        TranslationThrottle throttle,
        RewriteService rewrite,
        ReplyService replyService,
        IOptions<PluginOptions>? options = null)
    {
        _router = router;
        _glossary = glossary;
        _drafts = drafts;
        _detector = detector;
        _cache = cache;
        _throttle = throttle;
        _rewrite = rewrite;
        _replyService = replyService;
        _options = options?.Value ?? new PluginOptions();
    }

    public Task<DetectionResult> DetectAsync(LanguageDetectionRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new TranslationException("検出対象のテキストは必須です。");
        }

        if (request.Text.Length > _options.MaxCharactersPerRequest)
        {
            throw new TranslationException("検出対象が許容される文字数を超えています。");
        }

        return Task.FromResult(_detector.Detect(request.Text));
    }

    public Task<PipelineExecutionResult> ExecuteAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        return TranslateAsync(request, cancellationToken);
    }

    public async Task<PipelineExecutionResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        ValidateTranslationRequest(request);

        var glossaryResult = ResolveGlossary(request);
        var matchSnapshots = glossaryResult.Matches.Select(match => match.Clone()).ToList();
        var normalizedRequest = NormalizeRequestForTranslation(request, glossaryResult.Text);

        var detectionOutcome = DetectLanguageIfNeeded(normalizedRequest);
        if (detectionOutcome is DetectionResult pendingSelection)
        {
            return PipelineExecutionResult.FromDetection(pendingSelection);
        }

        if (_cache.TryGet(normalizedRequest, out var cached))
        {
            cached.SetGlossaryMatches(matchSnapshots.Select(match => match.Clone()));
            return PipelineExecutionResult.FromTranslation(cached);
        }

        try
        {
            var translation = await TranslateWithRouterAsync(normalizedRequest, matchSnapshots, cancellationToken);
            return PipelineExecutionResult.FromTranslation(translation);
        }
        catch (LowConfidenceDetectionException ex)
        {
            return PipelineExecutionResult.FromDetection(ex.Detection);
        }
    }

    private void ValidateTranslationRequest(TranslationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new TranslationException("翻訳する本文は必須です。");
        }

        if (request.Text.Length > _options.MaxCharactersPerRequest)
        {
            throw new TranslationException("許容される文字数を超えています。");
        }
    }

    private GlossaryApplicationResult ResolveGlossary(TranslationRequest request)
    {
        if (!request.UseGlossary)
        {
            return new GlossaryApplicationResult
            {
                Text = request.Text,
                Matches = Array.Empty<GlossaryMatchDetail>()
            };
        }

        var result = _glossary.Apply(request.Text, request.TenantId, request.ChannelId, request.UserId, request.GlossaryDecisions);
        if (result.RequiresResolution)
        {
            throw new GlossaryConflictException(result, request);
        }

        return result;
    }

    private TranslationRequest NormalizeRequestForTranslation(TranslationRequest request, string resolvedText)
    {
        return new TranslationRequest
        {
            Text = resolvedText,
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
    }

    private DetectionResult? DetectLanguageIfNeeded(TranslationRequest request)
    {
        if (!string.IsNullOrEmpty(request.SourceLanguage))
        {
            return null;
        }

        var detection = _detector.Detect(request.Text);
        if (detection.Confidence < 0.75)
        {
            return detection;
        }

        request.SourceLanguage = detection.Language;
        return null;
    }

    private async Task<TranslationResult> TranslateWithRouterAsync(
        TranslationRequest normalizedRequest,
        IReadOnlyList<GlossaryMatchDetail> matchSnapshots,
        CancellationToken cancellationToken)
    {
        using var lease = await _throttle.AcquireAsync(normalizedRequest.TenantId, cancellationToken);
        var result = await _router.TranslateAsync(normalizedRequest, cancellationToken);
        result.SetGlossaryMatches(matchSnapshots);
        _cache.Set(normalizedRequest, result);
        return result;
    }

    public OfflineDraftRecord SaveDraft(OfflineDraftRequest request)
    {
        return _drafts.SaveDraft(request);
    }

    public Task<RewriteResult> RewriteAsync(RewriteRequest request, CancellationToken cancellationToken)
    {
        return _rewrite.RewriteAsync(request, cancellationToken);
    }

    public Task<ReplyResult> PostReplyAsync(ReplyRequest request, CancellationToken cancellationToken)
    {
        return _replyService.SendReplyAsync(request, cancellationToken);
    }
}
