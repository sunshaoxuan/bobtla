using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 翻訳指揮を行い Teams への応答形を整えるパイプライン。
/// </summary>
public class TranslationPipeline
{
    private readonly TranslationRouter _router;
    private readonly GlossaryService _glossary;
    private readonly OfflineDraftStore _drafts;
    private readonly LanguageDetector _detector;
    private readonly PluginOptions _options;

    public TranslationPipeline(TranslationRouter router, GlossaryService glossary, OfflineDraftStore drafts, LanguageDetector detector, IOptions<PluginOptions>? options = null)
    {
        _router = router;
        _glossary = glossary;
        _drafts = drafts;
        _detector = detector;
        _options = options?.Value ?? new PluginOptions();
    }

    public async Task<TranslationResult> ExecuteAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new TranslationException("翻訳対象テキストが空です。");
        }

        if (request.Text.Length > _options.MaxCharactersPerRequest)
        {
            throw new TranslationException("文字数が上限を超えています。");
        }

        var resolvedRequest = new TranslationRequest
        {
            Text = request.UseGlossary ? _glossary.Apply(request.Text, request.TenantId, request.ChannelId, request.UserId) : request.Text,
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            TenantId = request.TenantId,
            UserId = request.UserId,
            ChannelId = request.ChannelId,
            Tone = request.Tone,
            AdditionalTargetLanguages = request.AdditionalTargetLanguages,
            UseGlossary = request.UseGlossary
        };

        if (string.IsNullOrEmpty(resolvedRequest.SourceLanguage))
        {
            var detection = _detector.Detect(resolvedRequest.Text);
            if (detection.Confidence < 0.7)
            {
                throw new TranslationException("言語を特定できませんでした。手動で指定してください。");
            }
            resolvedRequest.SourceLanguage = detection.Language;
        }

        return await _router.TranslateAsync(resolvedRequest, cancellationToken);
    }

    public OfflineDraftRecord SaveDraft(OfflineDraftRequest request)
    {
        return _drafts.SaveDraft(request);
    }
}
