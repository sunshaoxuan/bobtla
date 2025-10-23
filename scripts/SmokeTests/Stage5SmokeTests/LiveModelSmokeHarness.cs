using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Providers;
using TlaPlugin.Services;

public sealed class LiveModelSmokeHarness
{
    private readonly PluginOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly KeyVaultSecretResolver _secretResolver;
    private readonly ToneTemplateService _tones = new();
    private readonly LanguageDetector _detector = new();

    public LiveModelSmokeHarness(
        PluginOptions options,
        ILoggerFactory? loggerFactory = null,
        IHttpClientFactory? httpClientFactory = null,
        KeyVaultSecretResolver? secretResolver = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _loggerFactory = loggerFactory ?? CreateDefaultLoggerFactory();
        _httpClientFactory = httpClientFactory ?? new SimpleHttpClientFactory();
        _secretResolver = secretResolver ?? new KeyVaultSecretResolver(Options.Create(options));
    }

    public async Task<LiveModelSmokeResult> ExecuteAsync(
        TranslationRequest request,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? additionalLanguages = null)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new ArgumentException("Translation text is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.TargetLanguage))
        {
            throw new ArgumentException("Target language is required.", nameof(request));
        }

        var factory = new ModelProviderFactory(
            Options.Create(_options),
            _httpClientFactory,
            _secretResolver,
            _loggerFactory);

        var providers = factory.CreateProviders();
        if (providers.Count == 0)
        {
            throw new InvalidOperationException("配置中未定义任何模型 Provider。");
        }

        var detection = _detector.Detect(request.Text);
        var sourceLanguage = string.IsNullOrWhiteSpace(request.SourceLanguage)
            ? detection.Language ?? "auto"
            : request.SourceLanguage;

        var tone = string.IsNullOrWhiteSpace(request.Tone)
            ? TranslationRequest.DefaultTone
            : request.Tone;
        var promptPrefix = _tones.GetPromptPrefix(tone);

        var targets = (additionalLanguages ?? request.AdditionalTargetLanguages)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .Where(l => !string.Equals(l, request.TargetLanguage, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new LiveModelSmokeResult();

        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var providerId = provider.Options.Id;

            if (RequiresFallback(provider))
            {
                try
                {
                    await provider.TranslateAsync(request.Text, sourceLanguage!, request.TargetLanguage, promptPrefix, cancellationToken)
                        .ConfigureAwait(false);
                    result.FallbackProviders.Add(providerId);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException)
                {
                    result.Failures.Add(new LiveModelFailure(providerId, ex.Message));
                }

                continue;
            }

            try
            {
                var translation = await provider.TranslateAsync(request.Text, sourceLanguage!, request.TargetLanguage, promptPrefix, cancellationToken)
                    .ConfigureAwait(false);
                var rewritten = await provider.RewriteAsync(translation.Text, tone, cancellationToken)
                    .ConfigureAwait(false);

                var additional = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var language in targets)
                {
                    var extra = await provider.TranslateAsync(request.Text, sourceLanguage!, language, promptPrefix, cancellationToken)
                        .ConfigureAwait(false);
                    var extraRewritten = await provider.RewriteAsync(extra.Text, tone, cancellationToken)
                        .ConfigureAwait(false);
                    additional[language] = extraRewritten;
                }

                result.Success = new LiveModelSuccess(providerId, translation.ModelId, rewritten, translation.LatencyMs, additional);
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is InvalidOperationException or TaskCanceledException)
            {
                result.Failures.Add(new LiveModelFailure(providerId, ex.Message));
            }
        }

        if (result.Success is null)
        {
            throw new InvalidOperationException("未能通过任何 Provider 完成实时翻译，请检查日志。");
        }

        return result;
    }

    private static bool RequiresFallback(IModelProvider provider)
    {
        if (provider is not ConfigurableChatModelProvider configurable)
        {
            return false;
        }

        var options = configurable.Options;
        if (options.Kind == ModelProviderKind.Ollama)
        {
            return false;
        }

        var hasEndpoint = !string.IsNullOrWhiteSpace(options.Endpoint);
        var hasApiKey = !string.IsNullOrWhiteSpace(options.ApiKeySecretName) || options.DefaultHeaders.Count > 0;
        return !hasEndpoint || !hasApiKey;
    }

    private static ILoggerFactory CreateDefaultLoggerFactory()
    {
        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
        });
    }

    private sealed class SimpleHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public SimpleHttpClientFactory()
        {
            _handler = new SocketsHttpHandler();
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }
    }
}

public sealed class LiveModelSmokeResult
{
    public LiveModelSuccess? Success { get; set; }

    public List<string> FallbackProviders { get; } = new();

    public List<LiveModelFailure> Failures { get; } = new();
}

public sealed record LiveModelSuccess(
    string ProviderId,
    string ModelId,
    string TranslatedText,
    int LatencyMs,
    IReadOnlyDictionary<string, string> AdditionalTranslations);

public sealed record LiveModelFailure(string ProviderId, string Message);
