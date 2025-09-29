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
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Services;

namespace TlaPlugin.Providers;

/// <summary>
/// 为 OpenAI、Anthropic、Groq、OpenWebUI、Ollama 等聊天补全 API 提供统一封装的模型提供方。
/// 当未配置实际 API 时会回退到 <see cref="MockModelProvider"/>。
/// </summary>
public class ConfigurableChatModelProvider : IModelProvider
{
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly KeyVaultSecretResolver? _secretResolver;
    private readonly LanguageDetector _detector = new();
    private readonly MockModelProvider _fallback;

    public ConfigurableChatModelProvider(ModelProviderOptions options, IHttpClientFactory? httpClientFactory, KeyVaultSecretResolver? secretResolver)
    {
        Options = options;
        _httpClientFactory = httpClientFactory;
        _secretResolver = secretResolver;
        _fallback = new MockModelProvider(options);
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
            return await _fallback.TranslateAsync(text, sourceLanguage, targetLanguage, promptPrefix, cancellationToken);
        }

        var instructions = $"{promptPrefix} 源语言: {sourceLanguage} / 目标语言: {targetLanguage}";
        var messages = new List<(string role, string content)>
        {
            ("user", text)
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var content = await InvokeChatCompletionAsync(instructions, messages, cancellationToken);
            stopwatch.Stop();
            return new ModelTranslationResult(content, Options.Id, Options.Reliability, (int)stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new InvalidOperationException($"模型 {Options.Id} 调用失败。", ex);
        }
    }

    public async Task<string> RewriteAsync(string translatedText, string tone, CancellationToken cancellationToken)
    {
        if (!CanInvokeExternalEndpoint)
        {
            return await _fallback.RewriteAsync(translatedText, tone, cancellationToken);
        }

        var instructions = tone switch
        {
            ToneTemplateService.Business => "请将译文润色为正式商务语气。",
            ToneTemplateService.Technical => "请将译文润色为技术文档所需的严谨表达。",
            ToneTemplateService.Casual => "请将译文调整为亲切随和的语气。",
            _ => "请将译文润色为礼貌的敬语。"
        };

        var messages = new List<(string role, string content)>
        {
            ("user", translatedText)
        };

        try
        {
            var content = await InvokeChatCompletionAsync(instructions, messages, cancellationToken);
            return EnsureToneSuffix(content, tone);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new InvalidOperationException($"模型 {Options.Id} 重写处理失败。", ex);
        }
    }

    private bool CanInvokeExternalEndpoint =>
        _httpClientFactory != null && _secretResolver != null &&
        !string.IsNullOrWhiteSpace(Options.Endpoint) &&
        Options.Kind != ModelProviderKind.Mock &&
        (Options.Kind == ModelProviderKind.Ollama || !string.IsNullOrWhiteSpace(Options.ApiKeySecretName) || Options.DefaultHeaders.Count > 0);

    private async Task<string> InvokeChatCompletionAsync(string instructions, IList<(string role, string content)> messages, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory!.CreateClient($"provider-{Options.Id}");
        using var request = new HttpRequestMessage(HttpMethod.Post, Options.Endpoint);

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

        var secret = await _secretResolver!.GetSecretAsync(Options.ApiKeySecretName, cancellationToken);
        if (string.IsNullOrWhiteSpace(secret))
        {
            return;
        }

        switch (Options.Kind)
        {
            case ModelProviderKind.Anthropic:
                headers.TryAddWithoutValidation("x-api-key", secret);
                break;
            case ModelProviderKind.Ollama:
                // Ollama 默认无需鉴权，但若配置了 API Key 则以自定义头发送。
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
                ?? throw new InvalidOperationException("Anthropic 响应未包含文本。"),
            ModelProviderKind.Ollama => node["response"]?.GetValue<string>()
                ?? throw new InvalidOperationException("Ollama 响应未包含文本。"),
            _ => node["choices"]?.AsArray().FirstOrDefault()?.AsObject()["message"]?.AsObject()["content"]?.GetValue<string>()
                ?? throw new InvalidOperationException("OpenAI 兼容响应未包含文本。")
        };
    }

    private static string EnsureToneSuffix(string text, string tone)
    {
        var suffix = tone switch
        {
            ToneTemplateService.Business => "※已调整为商务语气",
            ToneTemplateService.Technical => "※已调整为技术语气",
            ToneTemplateService.Casual => "※已调整为亲切语气",
            _ => "※已调整为敬语"
        };

        return text.EndsWith(suffix, StringComparison.Ordinal) ? text : $"{text} {suffix}";
    }
}
