using System.Threading;
using System.Threading.Tasks;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Services;

namespace TlaPlugin.Providers;

/// <summary>
/// 翻译模型提供方的通用接口。
/// </summary>
public interface IModelProvider
{
    Task<DetectionResult> DetectAsync(string text, CancellationToken cancellationToken);
    Task<ModelTranslationResult> TranslateAsync(string text, string sourceLanguage, string targetLanguage, string promptPrefix, CancellationToken cancellationToken);
    Task<string> RewriteAsync(string translatedText, string tone, CancellationToken cancellationToken);
    Task<string> SummarizeAsync(string text, CancellationToken cancellationToken);
    ModelProviderOptions Options { get; }
}

/// <summary>
/// 表示模型调用结果的内部结构。
/// </summary>
public record ModelTranslationResult(string Text, string ModelId, double Confidence, int LatencyMs);
