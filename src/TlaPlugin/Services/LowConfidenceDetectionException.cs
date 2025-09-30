using System;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 表示模型语言检测置信度不足，需要人工确认的异常。
/// </summary>
public class LowConfidenceDetectionException : TranslationException
{
    public LowConfidenceDetectionException(DetectionResult detection)
        : base("源语言置信度不足，需要人工确认。")
    {
        Detection = detection ?? throw new ArgumentNullException(nameof(detection));
    }

    /// <summary>
    /// 获取需要前端兜底展示的候选列表。
    /// </summary>
    public DetectionResult Detection { get; }
}
