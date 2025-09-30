using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Services;

namespace TlaPlugin.Providers;

/// <summary>
/// 面向单元测试的模拟模型提供方。
/// </summary>
public class MockModelProvider : IModelProvider
{
    private int _remainingFailures;

    public MockModelProvider(ModelProviderOptions options)
    {
        Options = options;
        _remainingFailures = options.SimulatedFailures;
    }

    public ModelProviderOptions Options { get; }

    public Task<DetectionResult> DetectAsync(string text, CancellationToken cancellationToken)
    {
        // 使用极简启发式完成语言检测。
        var detector = new LanguageDetector();
        return Task.FromResult(detector.Detect(text));
    }

    public async Task<ModelTranslationResult> TranslateAsync(string text, string sourceLanguage, string targetLanguage, string promptPrefix, CancellationToken cancellationToken)
    {
        if (_remainingFailures > 0)
        {
            _remainingFailures--;
            throw new InvalidOperationException($"模型 {Options.Id} 的模拟失败。");
        }

        var sw = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(Options.LatencyTargetMs, 500)), cancellationToken);
        sw.Stop();

        var prefix = string.IsNullOrEmpty(Options.TranslationPrefix) ? $"[{Options.Id}]" : Options.TranslationPrefix;
        var output = $"{prefix} {promptPrefix} {text} ({targetLanguage})";
        return new ModelTranslationResult(output, Options.Id, Options.Reliability, (int)sw.ElapsedMilliseconds);
    }

    public Task<string> RewriteAsync(string translatedText, string tone, CancellationToken cancellationToken)
    {
        // 以简单方式模拟改写逻辑。
        var suffix = tone switch
        {
            ToneTemplateService.Business => "※已调整为商务语气",
            ToneTemplateService.Technical => "※已调整为技术语气",
            ToneTemplateService.Casual => "※已调整为轻松语气",
            _ => "※已调整为敬语"
        };
        return Task.FromResult($"{translatedText} {suffix}");
    }

    public Task<string> SummarizeAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(string.Empty);
        }

        var trimmed = text.Length > 60 ? text[..60] + "…" : text;
        return Task.FromResult($"概要: {trimmed}");
    }
}
