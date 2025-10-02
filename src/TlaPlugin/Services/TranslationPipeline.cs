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
public class TranslationPipeline : ITranslationPipeline
{
    private readonly TranslationRouter _router;
    private readonly GlossaryService _glossary;
    private readonly OfflineDraftStore _drafts;
    private readonly LanguageDetector _detector;
    private readonly TranslationCache _cache;
    private readonly TranslationThrottle _throttle;
    private readonly ContextRetrievalService _contextRetrieval;
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
        ContextRetrievalService contextRetrieval,
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
        _contextRetrieval = contextRetrieval;
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

    public Task<PipelineExecutionResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        return TranslateAsync(request, null, cancellationToken);
    }

    public async Task<PipelineExecutionResult> TranslateAsync(TranslationRequest request, DetectionResult? detection, CancellationToken cancellationToken)
    {
        ValidateTranslationRequest(request);

        var preview = PreviewGlossary(request);
        if (preview.RequiresResolution)
        {
            return PipelineExecutionResult.FromGlossaryConflict(preview, request);
        }

        var glossaryResult = ApplyGlossary(request);
        var matchSnapshots = glossaryResult.Matches.Select(match => match.Clone()).ToList();
        var normalizedRequest = NormalizeRequestForTranslation(request, glossaryResult.Text);

        if (detection is not null && string.IsNullOrEmpty(normalizedRequest.SourceLanguage))
        {
            if (detection.Confidence < 0.75)
            {
                return PipelineExecutionResult.FromDetection(detection);
            }

            normalizedRequest.SourceLanguage = detection.Language;
        }

        var detectionOutcome = DetectLanguageIfNeeded(normalizedRequest, detection);
        if (detectionOutcome is DetectionResult pendingSelection)
        {
            return PipelineExecutionResult.FromDetection(pendingSelection);
        }

        await ApplyContextAsync(normalizedRequest, cancellationToken);

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

    private GlossaryApplicationResult PreviewGlossary(TranslationRequest request)
    {
        if (!request.UseGlossary)
        {
            return new GlossaryApplicationResult
            {
                Text = request.Text,
                Matches = Array.Empty<GlossaryMatchDetail>()
            };
        }

        return _glossary.Preview(
            request.Text,
            request.TenantId,
            request.ChannelId,
            request.UserId,
            GlossaryPolicy.Fallback,
            null,
            request.GlossaryDecisions);
    }

    private GlossaryApplicationResult ApplyGlossary(TranslationRequest request)
    {
        if (!request.UseGlossary)
        {
            return new GlossaryApplicationResult
            {
                Text = request.Text,
                Matches = Array.Empty<GlossaryMatchDetail>()
            };
        }

        return _glossary.Apply(
            request.Text,
            request.TenantId,
            request.ChannelId,
            request.UserId,
            request.GlossaryDecisions);
    }

    private TranslationRequest NormalizeRequestForTranslation(TranslationRequest request, string resolvedText)
    {
        var additionalTargets = request.AdditionalTargetLanguages ?? new List<string>();
        var contextHints = request.ContextHints ?? new List<string>();

        return new TranslationRequest
        {
            Text = resolvedText,
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            TenantId = request.TenantId,
            UserId = request.UserId,
            ChannelId = request.ChannelId,
            ThreadId = request.ThreadId,
            Tone = request.Tone,
            AdditionalTargetLanguages = new List<string>(additionalTargets),
            UseGlossary = request.UseGlossary,
            UseRag = request.UseRag,
            ContextHints = new List<string>(contextHints),
            ContextSummary = request.ContextSummary,
            UiLocale = request.UiLocale,
            UserAssertion = request.UserAssertion,
            GlossaryDecisions = request.GlossaryDecisions.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Clone(),
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private async Task ApplyContextAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        if (!ShouldUseRag(request))
        {
            request.ContextSummary = null;
            return;
        }

        ContextRetrievalResult retrieval;
        try
        {
            retrieval = await _contextRetrieval.GetContextAsync(new ContextRetrievalRequest
            {
                TenantId = request.TenantId,
                UserId = request.UserId,
                ChannelId = request.ChannelId,
                ThreadId = request.ThreadId,
                MaxMessages = _options.Rag.MaxMessages,
                ContextHints = new List<string>(request.ContextHints),
                UserAssertion = request.UserAssertion
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            request.ContextSummary = null;
            return;
        }

        if (!retrieval.HasContext)
        {
            request.ContextSummary = null;
            return;
        }

        var ordered = retrieval.Messages
            .OrderBy(message => message.Timestamp)
            .Select(message => $"{message.Author}: {message.Text}");
        var combined = string.Join(Environment.NewLine, ordered);

        var summary = combined;
        if (_options.Rag.SummaryThreshold > 0 && combined.Length > _options.Rag.SummaryThreshold && !string.IsNullOrEmpty(request.UserId))
        {
            var summarize = await _router.SummarizeAsync(new SummarizeRequest
            {
                Context = combined,
                TenantId = request.TenantId,
                UserId = request.UserId,
                UserAssertion = request.UserAssertion
            }, cancellationToken);

            summary = summarize.Summary;
            if (_options.Rag.SummaryTargetLength > 0 && summary.Length > _options.Rag.SummaryTargetLength)
            {
                summary = summary[.._options.Rag.SummaryTargetLength];
            }
        }

        request.ContextSummary = summary;
        request.Text = BuildContextPrompt(summary, request.Text);
    }

    private bool ShouldUseRag(TranslationRequest request)
    {
        if (!request.UseRag && !_options.Rag.Enabled)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(request.ChannelId) || request.ContextHints.Count > 0;
    }

    private static string BuildContextPrompt(string context, string text)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            return text;
        }

        return $"[Context]\n{context}\n\n[Message]\n{text}";
    }

    private DetectionResult? DetectLanguageIfNeeded(TranslationRequest request, DetectionResult? providedDetection)
    {
        if (!string.IsNullOrEmpty(request.SourceLanguage))
        {
            return null;
        }

        var detection = providedDetection ?? _detector.Detect(request.Text);
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

    public OfflineDraftRecord MarkDraftProcessing(long draftId)
    {
        return _drafts.MarkProcessing(draftId);
    }

    public Task<RewriteResult> RewriteAsync(RewriteRequest request, CancellationToken cancellationToken)
    {
        return _rewrite.RewriteAsync(request, cancellationToken);
    }

    public Task<ReplyResult> PostReplyAsync(ReplyRequest request, CancellationToken cancellationToken)
    {
        return _replyService.SendReplyAsync(request, cancellationToken);
    }

    public Task<ReplyResult> PostReplyAsync(ReplyRequest request, string finalText, string? toneApplied, CancellationToken cancellationToken)
    {
        return _replyService.SendReplyAsync(request, finalText, toneApplied, cancellationToken);
    }
}
