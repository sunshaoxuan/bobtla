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

    private const double MinimumDetectionConfidence = 0.75;

    public Task<PipelineExecutionResult> ExecuteAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        return TranslateAsync(request, cancellationToken);
    }

    public async Task<PipelineExecutionResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
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

        DetectionResult? detection = null;

        if (string.IsNullOrEmpty(resolvedRequest.SourceLanguage))
        {
            detection = _detector.Detect(resolvedRequest.Text);
            if (detection.Confidence < MinimumDetectionConfidence)
            {
                try
                {
                    var providerDetection = await _router.DetectAsync(new LanguageDetectionRequest
                    {
                        Text = resolvedRequest.Text,
                        TenantId = resolvedRequest.TenantId
                    }, cancellationToken);

                    detection = DetectionResultExtensions.Merge(detection, providerDetection);
                }
                catch (TranslationException)
                {
                    // 如果路由器检测失败，则保留启发式检测结果以供兜底。
                }
            }

            if (detection.Confidence < MinimumDetectionConfidence)
            {
                return PipelineExecutionResult.FromDetection(detection);
            }
            resolvedRequest.SourceLanguage = detection.Language;
        }

        if (_cache.TryGet(resolvedRequest, out var cached))
        {
            cached.SetGlossaryMatches(matchSnapshots.Select(match => match.Clone()));
            return PipelineExecutionResult.FromTranslation(cached);
        }

        using var lease = await _throttle.AcquireAsync(resolvedRequest.TenantId, cancellationToken);
        try
        {
            var result = await _router.TranslateAsync(resolvedRequest, cancellationToken);
            result.SetGlossaryMatches(matchSnapshots);
            _cache.Set(resolvedRequest, result);
            return PipelineExecutionResult.FromTranslation(result);
        }
        catch (LowConfidenceDetectionException ex)
        {
            var merged = detection is null
                ? ex.Detection
                : DetectionResultExtensions.Merge(detection, ex.Detection);
            return PipelineExecutionResult.FromDetection(merged);
        }
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
