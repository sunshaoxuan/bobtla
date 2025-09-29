using System.Threading;
using System.Threading.Tasks;
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
    ModelProviderOptions Options { get; }
}

/// <summary>
/// 用于在内部保存模型调用结果。
/// </summary>
public record ModelTranslationResult(string Text, string ModelId, double Confidence, int LatencyMs);
