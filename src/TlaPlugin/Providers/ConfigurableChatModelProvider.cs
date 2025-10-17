using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Services;

namespace TlaPlugin.Providers;

/// <summary>
/// 统一封装 OpenAI、Anthropic、Groq、OpenWebUI、Ollama 等聊天补全 API 的模型提供方。
/// 若缺少必要配置则回退到 <see cref="MockModelProvider"/>。
/// </summary>
public class ConfigurableChatModelProvider : IModelProvider
{
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly KeyVaultSecretResolver? _secretResolver;
    private readonly LanguageDetector _detector = new();
    private readonly MockModelProvider _fallback;
    private readonly ILogger<ConfigurableChatModelProvider> _logger;

    public ConfigurableChatModelProvider(
        ModelProviderOptions options,
        IHttpClientFactory? httpClientFactory,
        KeyVaultSecretResolver? secretResolver,
        ILogger<ConfigurableChatModelProvider>? logger = null)
    {
        Options = options;
        _httpClientFactory = httpClientFactory;
        _secretResolver = secretResolver;
        _fallback = new MockModelProvider(options);
        _logger = logger ?? NullLogger<ConfigurableChatModelProvider>.Instance;
    }

    public ModelProviderOptions Options { get; }

    public Task<DetectionResult> DetectAsync(string text, CancellationToken cancellationToken)
    {
        return Task.FromResult(_detector.Detect(text));
    }

    public async Task<ModelTranslationResult> TranslateAsync(string text, string sourceLanguage, string targetLanguage, string promptPrefix, CancellationToken cancellationToken)
    {
        if (!CanInvokeExternalEndpoint)
        {
            LogFallback("translate");
            return await _fallback.TranslateAsync(text, sourceLanguage, targetLanguage, promptPrefix, cancellationToken);
        }

        var instructions = $"{promptPrefix} 源语言: {sourceLanguage} / 目标语言: {targetLanguage}";
        var messages = new List<(string role, string content)>
        {
            ("user", text)
        };

        var stopwatch = Stopwatch.StartNew();
        LogInvocationStart("translate");
        try
        {
            var content = await InvokeChatCompletionAsync(instructions, messages, cancellationToken);
            stopwatch.Stop();
            LogInvocationSuccess("translate", stopwatch.ElapsedMilliseconds);
            return new ModelTranslationResult(content, GetModelIdentifier(), Options.Reliability, (int)stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            stopwatch.Stop();
            LogInvocationFailure("translate", ex, stopwatch.ElapsedMilliseconds);
            throw new InvalidOperationException($"调用模型 {Options.Id} 失败。", ex);
        }
    }

    public async Task<string> RewriteAsync(string translatedText, string tone, CancellationToken cancellationToken)
    {
        if (!CanInvokeExternalEndpoint)
        {
            LogFallback("rewrite");
            return await _fallback.RewriteAsync(translatedText, tone, cancellationToken);
        }

        var instructions = tone switch
        {
            ToneTemplateService.Business => "请将译文润色为正式的商务语气。",
            ToneTemplateService.Technical => "请将译文润色为精准的技术说明语气。",
            ToneTemplateService.Casual => "请将译文润色为轻松随和的语气。",
            _ => "请将译文润色为礼貌的敬语风格。"
        };

        var messages = new List<(string role, string content)>
        {
            ("user", translatedText)
        };

        var stopwatch = Stopwatch.StartNew();
        LogInvocationStart("rewrite");
        try
        {
            var content = await InvokeChatCompletionAsync(instructions, messages, cancellationToken);
            stopwatch.Stop();
            LogInvocationSuccess("rewrite", stopwatch.ElapsedMilliseconds);
            return EnsureToneSuffix(content, tone);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            stopwatch.Stop();
            LogInvocationFailure("rewrite", ex, stopwatch.ElapsedMilliseconds);
            throw new InvalidOperationException($"模型 {Options.Id} 的改写流程失败。", ex);
        }
    }

    public async Task<string> SummarizeAsync(string text, CancellationToken cancellationToken)
    {
        if (!CanInvokeExternalEndpoint)
        {
            LogFallback("summarize");
            return await _fallback.SummarizeAsync(text, cancellationToken);
        }

        var instructions = "请对以下内容生成两句话以内的摘要。";
        var messages = new List<(string role, string content)>
        {
            ("user", text)
        };

        var stopwatch = Stopwatch.StartNew();
        LogInvocationStart("summarize");
        try
        {
            var content = await InvokeChatCompletionAsync(instructions, messages, cancellationToken);
            stopwatch.Stop();
            LogInvocationSuccess("summarize", stopwatch.ElapsedMilliseconds);
            return content;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            stopwatch.Stop();
            LogInvocationFailure("summarize", ex, stopwatch.ElapsedMilliseconds);
            throw new InvalidOperationException($"模型 {Options.Id} 的摘要流程失败。", ex);
        }
    }

    private void LogFallback(string operation)
    {
        if (!_logger.IsEnabled(LogLevel.Warning))
        {
            return;
        }

        _logger.LogWarning(
            "Provider {ProviderId} 使用回退模型执行 {Operation}，HasHttpClient={HasClient} HasSecretResolver={HasResolver} EndpointConfigured={HasEndpoint} ApiKeyConfigured={HasApiKey}。",
            Options.Id,
            operation,
            _httpClientFactory != null,
            _secretResolver != null,
            !string.IsNullOrWhiteSpace(Options.Endpoint),
            !string.IsNullOrWhiteSpace(Options.ApiKeySecretName) || Options.Kind == ModelProviderKind.Ollama || Options.DefaultHeaders.Count > 0);
    }

    private void LogInvocationStart(string operation)
    {
        if (!_logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        _logger.LogInformation(
            "Provider {ProviderId} 开始执行 {Operation} 调用，模型 {ModelId}，终端 {Endpoint}。",
            Options.Id,
            operation,
            GetModelIdentifier(),
            string.IsNullOrWhiteSpace(Options.Endpoint) ? "(none)" : Options.Endpoint);
    }

    private void LogInvocationSuccess(string operation, long durationMs)
    {
        if (!_logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        _logger.LogInformation(
            "Provider {ProviderId} 完成 {Operation} 调用，耗时 {Duration}ms。",
            Options.Id,
            operation,
            durationMs);
    }

    private void LogInvocationFailure(string operation, Exception exception, long durationMs)
    {
        _logger.LogError(
            exception,
            "Provider {ProviderId} 在执行 {Operation} 调用时失败，耗时 {Duration}ms。",
            Options.Id,
            operation,
            durationMs);
    }

    private bool CanInvokeExternalEndpoint =>
        _httpClientFactory != null && _secretResolver != null &&
        !string.IsNullOrWhiteSpace(Options.Endpoint) &&
        Options.Kind != ModelProviderKind.Mock &&
        (Options.Kind == ModelProviderKind.Ollama || !string.IsNullOrWhiteSpace(Options.ApiKeySecretName) || Options.DefaultHeaders.Count > 0);

    private async Task<string> InvokeChatCompletionAsync(string instructions, IList<(string role, string content)> messages, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory!.CreateClient($"provider-{Options.Id}");
        ApplyTimeout(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, Options.Endpoint);

        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        AppendHeaders(request.Headers);
        await AppendAuthorizationAsync(request.Headers, cancellationToken);

        request.Content = new StringContent(BuildRequestPayload(instructions, messages), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ExtractContent(JsonNode.Parse(json));
    }

    private void AppendHeaders(HttpRequestHeaders headers)
    {
        foreach (var kv in Options.DefaultHeaders)
        {
            headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }

        if (!string.IsNullOrEmpty(Options.Organization) && Options.Kind is ModelProviderKind.OpenAi or ModelProviderKind.Groq)
        {
            headers.TryAddWithoutValidation("OpenAI-Organization", Options.Organization);
        }

        if (Options.Kind == ModelProviderKind.Anthropic)
        {
            headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        }
    }

    private async Task AppendAuthorizationAsync(HttpRequestHeaders headers, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Options.ApiKeySecretName))
        {
            return;
        }

        var secret = await _secretResolver!.GetSecretAsync(
            Options.ApiKeySecretName,
            Options.ApiKeyTenantId,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(secret))
        {
            _logger.LogWarning(
                "Provider {ProviderId} 未能解析密钥 {SecretName}，将尝试无凭据调用。",
                Options.Id,
                Options.ApiKeySecretName);
            return;
        }

        _logger.LogInformation(
            "Provider {ProviderId} 已解析密钥 {SecretName}。",
            Options.Id,
            Options.ApiKeySecretName);

        switch (Options.Kind)
        {
            case ModelProviderKind.Anthropic:
                headers.TryAddWithoutValidation("x-api-key", secret);
                break;
            case ModelProviderKind.Ollama:
                // Ollama 默认无需认证，若配置了密钥则附带自定义头。
                headers.TryAddWithoutValidation("Authorization", secret);
                break;
            default:
                headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
                break;
        }
    }

    private string BuildRequestPayload(string instructions, IList<(string role, string content)> messages)
    {
        return Options.Kind switch
        {
            ModelProviderKind.Anthropic => BuildAnthropicPayload(instructions, messages),
            ModelProviderKind.Ollama => BuildOllamaPayload(instructions, messages),
            _ => BuildOpenAiCompatiblePayload(instructions, messages)
        };
    }

    private string BuildOpenAiCompatiblePayload(string instructions, IList<(string role, string content)> messages)
    {
        var node = new JsonObject
        {
            ["model"] = string.IsNullOrWhiteSpace(Options.Model) ? "gpt-3.5-turbo" : Options.Model,
            ["temperature"] = 0.3,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = instructions }
            }
        };

        var messageArray = node["messages"]!.AsArray();
        foreach (var message in messages)
        {
            messageArray.Add(new JsonObject
            {
                ["role"] = message.role,
                ["content"] = message.content
            });
        }

        return node.ToJsonString();
    }

    private string BuildAnthropicPayload(string instructions, IList<(string role, string content)> messages)
    {
        var node = new JsonObject
        {
            ["model"] = string.IsNullOrWhiteSpace(Options.Model) ? "claude-3-sonnet-20240229" : Options.Model,
            ["max_tokens"] = 2048,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = instructions + "\n" + string.Join("\n\n", messages.Select(m => m.content))
                        }
                    }
                }
            }
        };

        return node.ToJsonString();
    }

    private string BuildOllamaPayload(string instructions, IList<(string role, string content)> messages)
    {
        var builder = new StringBuilder();
        builder.AppendLine(instructions);
        foreach (var message in messages)
        {
            builder.AppendLine(message.content);
        }

        var node = new JsonObject
        {
            ["model"] = string.IsNullOrWhiteSpace(Options.Model) ? "llama3" : Options.Model,
            ["prompt"] = builder.ToString(),
            ["stream"] = false
        };

        return node.ToJsonString();
    }

    private string ExtractContent(JsonNode? node)
    {
        if (node is null)
        {
            throw new InvalidOperationException("解析模型响应失败。");
        }

        return Options.Kind switch
        {
            ModelProviderKind.Anthropic => node["content"]?.AsArray().FirstOrDefault()?.AsObject()["text"]?.GetValue<string>()
                ?? throw new InvalidOperationException("Anthropic 响应缺少文本内容。"),
            ModelProviderKind.Ollama => node["response"]?.GetValue<string>()
                ?? throw new InvalidOperationException("Ollama 响应缺少文本内容。"),
            _ => node["choices"]?.AsArray().FirstOrDefault()?.AsObject()["message"]?.AsObject()["content"]?.GetValue<string>()
                ?? throw new InvalidOperationException("OpenAI 兼容响应缺少文本内容。")
        };
    }

    private static string EnsureToneSuffix(string text, string tone)
    {
        var suffix = tone switch
        {
            ToneTemplateService.Business => "※ビジネス文体に調整済み",
            ToneTemplateService.Technical => "※技術文体に調整済み",
            ToneTemplateService.Casual => "※カジュアル文体に調整済み",
            _ => "※敬体に調整済み"
        };

        return text.EndsWith(suffix, StringComparison.Ordinal) ? text : $"{text} {suffix}";
    }

    private void ApplyTimeout(HttpClient client)
    {
        if (Options.LatencyTargetMs <= 0)
        {
            return;
        }

        var multiplier = Options.LatencyTargetMs * 4L;
        if (multiplier < 10_000)
        {
            multiplier = 10_000;
        }
        else if (multiplier > 120_000)
        {
            multiplier = 120_000;
        }

        var desiredTimeout = TimeSpan.FromMilliseconds(multiplier);
        if (client.Timeout != desiredTimeout)
        {
            client.Timeout = desiredTimeout;
        }
    }

    private string GetModelIdentifier()
    {
        return string.IsNullOrWhiteSpace(Options.Model) ? Options.Id : Options.Model;
    }
}
