using System;
using System.Collections.Generic;
using System.Linq;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 表示术语冲突需要用户干预的异常。
/// </summary>
public class GlossaryConflictException : TranslationException
{
    public GlossaryConflictException(GlossaryApplicationResult result, TranslationRequest request)
        : base("Glossary conflicts require resolution.")
    {
        Result = result.Clone();

        Request = new TranslationRequest
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
            AdditionalTargetLanguages = new List<string>(request.AdditionalTargetLanguages),
            GlossaryDecisions = request.GlossaryDecisions.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Clone(),
                StringComparer.OrdinalIgnoreCase)
        };
    }

    /// <summary>
    /// 获取触发异常的术语匹配详情。
    /// </summary>
    public GlossaryApplicationResult Result { get; }

    /// <summary>
    /// 获取需要重新提交的原始翻译请求。
    /// </summary>
    public TranslationRequest Request { get; }
}
