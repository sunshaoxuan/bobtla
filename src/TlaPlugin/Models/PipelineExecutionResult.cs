using System;

namespace TlaPlugin.Models;

/// <summary>
/// 表示翻译管线执行的结果，可能是翻译内容或语言识别候选。
/// </summary>
public class PipelineExecutionResult
{
    private PipelineExecutionResult(TranslationResult? translation, DetectionResult? detection)
    {
        Translation = translation;
        Detection = detection;
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

    public static PipelineExecutionResult FromTranslation(TranslationResult translation)
    {
        if (translation is null)
        {
            throw new ArgumentNullException(nameof(translation));
        }

        return new PipelineExecutionResult(translation, null);
    }

    public static PipelineExecutionResult FromDetection(DetectionResult detection)
    {
        if (detection is null)
        {
            throw new ArgumentNullException(nameof(detection));
        }

        return new PipelineExecutionResult(null, detection);
    }
}
