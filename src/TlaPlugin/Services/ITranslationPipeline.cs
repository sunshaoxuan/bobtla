using System.Threading;
using System.Threading.Tasks;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

public interface ITranslationPipeline
{
    Task<DetectionResult> DetectAsync(LanguageDetectionRequest request, CancellationToken cancellationToken);
    Task<PipelineExecutionResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken);
    Task<PipelineExecutionResult> TranslateAsync(TranslationRequest request, DetectionResult? detection, CancellationToken cancellationToken);
    OfflineDraftRecord SaveDraft(OfflineDraftRequest request);
    OfflineDraftRecord MarkDraftProcessing(long draftId);
    Task<RewriteResult> RewriteAsync(RewriteRequest request, CancellationToken cancellationToken);
    Task<ReplyResult> PostReplyAsync(ReplyRequest request, CancellationToken cancellationToken);
    Task<ReplyResult> PostReplyAsync(ReplyRequest request, string finalText, string? toneApplied, CancellationToken cancellationToken);
}
