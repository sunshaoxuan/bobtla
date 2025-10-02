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
        TranslationRequest? pendingRequest)
    {
        Translation = translation;
        Detection = detection;
        GlossaryConflicts = glossaryConflicts;
        PendingRequest = pendingRequest;
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

    public static PipelineExecutionResult FromTranslation(TranslationResult translation)
    {
        if (translation is null)
        {
            throw new ArgumentNullException(nameof(translation));
        }

        return new PipelineExecutionResult(translation, null, null, null);
    }

    public static PipelineExecutionResult FromDetection(DetectionResult detection)
    {
        if (detection is null)
        {
            throw new ArgumentNullException(nameof(detection));
        }

        return new PipelineExecutionResult(null, detection, null, null);
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
            CloneRequest(request));
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
