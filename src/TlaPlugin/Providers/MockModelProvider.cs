using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Services;

namespace TlaPlugin.Providers;

/// <summary>
/// 単体試験向けのモックモデル。
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
        // 非常に簡素なヒューリスティック検知。
        var detector = new LanguageDetector();
        return Task.FromResult(detector.Detect(text));
    }

    public async Task<ModelTranslationResult> TranslateAsync(string text, string sourceLanguage, string targetLanguage, string promptPrefix, CancellationToken cancellationToken)
    {
        if (_remainingFailures > 0)
        {
            _remainingFailures--;
            throw new InvalidOperationException($"モデル {Options.Id} のシミュレーション失敗");
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
        // ここではリライト処理をシンプルに模倣する。
        var suffix = tone switch
        {
            ToneTemplateService.Business => "※ビジネス調整済み",
            ToneTemplateService.Technical => "※技術調整済み",
            ToneTemplateService.Casual => "※カジュアル調整済み",
            _ => "※丁寧調整済み"
        };
        return Task.FromResult($"{translatedText} {suffix}");
    }
}
