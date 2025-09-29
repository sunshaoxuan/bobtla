using System.Threading;
using System.Threading.Tasks;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Services;

namespace TlaPlugin.Providers;

/// <summary>
/// 翻訳モデル提供者の共通インターフェース。
/// </summary>
public interface IModelProvider
{
    Task<DetectionResult> DetectAsync(string text, CancellationToken cancellationToken);
    Task<ModelTranslationResult> TranslateAsync(string text, string sourceLanguage, string targetLanguage, string promptPrefix, CancellationToken cancellationToken);
    Task<string> RewriteAsync(string translatedText, string tone, CancellationToken cancellationToken);
    ModelProviderOptions Options { get; }
}

/// <summary>
/// モデル呼び出しの結果を内部的に保持する。
/// </summary>
public record ModelTranslationResult(string Text, string ModelId, double Confidence, int LatencyMs);
