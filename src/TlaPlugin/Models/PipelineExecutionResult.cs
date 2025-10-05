using System;
using System.Collections.Generic;
using System.Linq;

namespace TlaPlugin.Models;

/// <summary>
/// 表示翻译管线执行的结果，可能是翻译内容或语言识别候选。
/// </summary>
public class PipelineExecutionResult
{
    private PipelineExecutionResult(
        TranslationResult? translation,
        DetectionResult? detection,
        GlossaryApplicationResult? glossaryConflicts,
        TranslationRequest? pendingRequest,
        string? queuedJobId,
        int queuedSegmentCount)
    {
        Translation = translation;
        Detection = detection;
        GlossaryConflicts = glossaryConflicts;
        PendingRequest = pendingRequest;
        QueuedJobId = queuedJobId;
        QueuedSegmentCount = queuedSegmentCount;
    }

    /// <summary>
    /// 获取翻译结果，如果本次执行未进行翻译则为 null。
    /// </summary>
    public TranslationResult? Translation { get; }

    /// <summary>
    /// 获取语言识别结果，当需要用户确认语言时提供候选列表。
    /// </summary>
    public DetectionResult? Detection { get; }

    /// <summary>
    /// 指示是否需要用户选择源语言。
    /// </summary>
    public bool RequiresLanguageSelection => Detection is not null && Translation is null;

    /// <summary>
    /// 获取需要用户处理的术语冲突详情。
    /// </summary>
    public GlossaryApplicationResult? GlossaryConflicts { get; }

    /// <summary>
    /// 当存在术语冲突时，包含重新提交所需的原始请求副本。
    /// </summary>
    public TranslationRequest? PendingRequest { get; }

    /// <summary>
    /// 指示是否需要用户解决术语冲突。
    /// </summary>
    public bool RequiresGlossaryResolution => GlossaryConflicts is not null && GlossaryConflicts.RequiresResolution;

    /// <summary>
    /// 長文翻訳が非同期キューに登録された場合に、そのジョブ ID を返す。
    /// </summary>
    public string? QueuedJobId { get; }

    /// <summary>
    /// キューに登録されたセグメント数。
    /// </summary>
    public int QueuedSegmentCount { get; }

    /// <summary>
    /// 翻訳がバックグラウンド処理に移行したかを示す。
    /// </summary>
    public bool IsQueued => !string.IsNullOrEmpty(QueuedJobId);

    public static PipelineExecutionResult FromTranslation(TranslationResult translation)
    {
        if (translation is null)
        {
            throw new ArgumentNullException(nameof(translation));
        }

        return new PipelineExecutionResult(translation, null, null, null, null, 0);
    }

    public static PipelineExecutionResult FromDetection(DetectionResult detection)
    {
        if (detection is null)
        {
            throw new ArgumentNullException(nameof(detection));
        }

        return new PipelineExecutionResult(null, detection, null, null, null, 0);
    }

    public static PipelineExecutionResult FromGlossaryConflict(
        GlossaryApplicationResult conflicts,
        TranslationRequest request)
    {
        if (conflicts is null)
        {
            throw new ArgumentNullException(nameof(conflicts));
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return new PipelineExecutionResult(
            null,
            null,
            conflicts.Clone(),
            CloneRequest(request),
            null,
            0);
    }

    public static PipelineExecutionResult FromQueuedJob(string jobId, int segmentCount)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("ジョブ ID は必須です。", nameof(jobId));
        }

        if (segmentCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentCount));
        }

        return new PipelineExecutionResult(null, null, null, null, jobId, segmentCount);
    }

    private static TranslationRequest CloneRequest(TranslationRequest request)
    {
        return new TranslationRequest
        {
            Text = request.Text,
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            TenantId = request.TenantId,
            UserId = request.UserId,
            ChannelId = request.ChannelId,
            ThreadId = request.ThreadId,
            Tone = request.Tone,
            UseGlossary = request.UseGlossary,
            UiLocale = request.UiLocale,
            UserAssertion = request.UserAssertion,
            AdditionalTargetLanguages = new List<string>(request.AdditionalTargetLanguages),
            GlossaryDecisions = request.GlossaryDecisions.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Clone(),
                StringComparer.OrdinalIgnoreCase)
        };
    }
}
