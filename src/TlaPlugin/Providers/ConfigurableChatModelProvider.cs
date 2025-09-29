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
/// OpenAI / Anthropic / Groq / OpenWebUI / Ollama といったチャット補完 API を統一的に扱うモデルプロバイダー。
/// 実際の API が構成されていない場合は <see cref="MockModelProvider"/> にフォールバックする。
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

        var instructions = $"{promptPrefix} 元言語: {sourceLanguage} / 目標言語: {targetLanguage}";
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
            throw new InvalidOperationException($"モデル {Options.Id} の呼び出しに失敗しました。", ex);
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
            ToneTemplateService.Business => "翻訳済み文章をビジネス敬体に整えてください。",
            ToneTemplateService.Technical => "翻訳済み文章を技術文書向けに明確化してください。",
            ToneTemplateService.Casual => "翻訳済み文章をカジュアルな文体に調整してください。",
            _ => "翻訳済み文章を丁寧な敬体に整えてください。"
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
            throw new InvalidOperationException($"モデル {Options.Id} のリライト処理に失敗しました。", ex);
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
                // Ollama はデフォルトで認証不要だが、API キーが設定されていれば独自ヘッダーとして送付する。
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
            throw new InvalidOperationException("モデル応答の解析に失敗しました。");
        }

        return Options.Kind switch
        {
            ModelProviderKind.Anthropic => node["content"]?.AsArray().FirstOrDefault()?.AsObject()["text"]?.GetValue<string>()
                ?? throw new InvalidOperationException("Anthropic 応答にテキストが含まれていません。"),
            ModelProviderKind.Ollama => node["response"]?.GetValue<string>()
                ?? throw new InvalidOperationException("Ollama 応答にテキストが含まれていません。"),
            _ => node["choices"]?.AsArray().FirstOrDefault()?.AsObject()["message"]?.AsObject()["content"]?.GetValue<string>()
                ?? throw new InvalidOperationException("OpenAI 互換応答にテキストが含まれていません。")
        };
    }

    private static string EnsureToneSuffix(string text, string tone)
    {
        var suffix = tone switch
        {
            ToneTemplateService.Business => "※ビジネス調整済み",
            ToneTemplateService.Technical => "※技術調整済み",
            ToneTemplateService.Casual => "※カジュアル調整済み",
            _ => "※丁寧調整済み"
        };

        return text.EndsWith(suffix, StringComparison.Ordinal) ? text : $"{text} {suffix}";
    }
}
