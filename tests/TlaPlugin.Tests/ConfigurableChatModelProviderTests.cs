using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Providers;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class ConfigurableChatModelProviderTests
{
    [Fact]
    public async Task TranslateAsyncAppliesTimeoutHeadersAndModelMetadata()
    {
        var providerOptions = new ModelProviderOptions
        {
            Id = "openai-gpt-4o",
            Kind = ModelProviderKind.OpenAi,
            Endpoint = "https://api.test/v1/chat/completions",
            Model = "gpt-4o-mini",
            ApiKeySecretName = "openai-api-key",
            ApiKeyTenantId = "contoso.onmicrosoft.com",
            LatencyTargetMs = 350,
            DefaultHeaders = new Dictionary<string, string>
            {
                ["OpenAI-Beta"] = "assistants=v2"
            }
        };

        var pluginOptions = Options.Create(new PluginOptions
        {
            Security = new SecurityOptions
            {
                SeedSecrets = new Dictionary<string, string>
                {
                    ["openai-api-key"] = "secret"
                }
            }
        });

        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(request =>
        {
            captured = request;
            var responsePayload = new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            content = "translated"
                        }
                    }
                }
            };
            var json = JsonSerializer.Serialize(responsePayload);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(100)
        };
        var factory = new StubHttpClientFactory(httpClient);
        var resolver = new KeyVaultSecretResolver(pluginOptions);
        var logger = new TestLogger<ConfigurableChatModelProvider>();
        var provider = new ConfigurableChatModelProvider(providerOptions, factory, resolver, logger);

        var result = await provider.TranslateAsync("hello", "en", "ja", "prompt", CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Contains(captured!.Headers.Accept, header => header.MediaType == "application/json");
        Assert.Equal("Bearer secret", captured.Headers.Authorization?.ToString());
        Assert.True(httpClient.Timeout <= TimeSpan.FromSeconds(12));
        Assert.Equal("gpt-4o-mini", result.ModelId);
        Assert.Equal("translated", result.Text);
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("已解析密钥", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("完成 translate 调用", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TranslateAsyncFallsBackWhenPrerequisitesMissing_LogsWarning()
    {
        var providerOptions = new ModelProviderOptions
        {
            Id = "openai-gpt-4o",
            Kind = ModelProviderKind.OpenAi,
            TranslationPrefix = "[MockFallback]"
        };

        var logger = new TestLogger<ConfigurableChatModelProvider>();
        var provider = new ConfigurableChatModelProvider(providerOptions, httpClientFactory: null, secretResolver: null, logger);

        var result = await provider.TranslateAsync("sample", "en", "ja", "prompt", CancellationToken.None);

        Assert.StartsWith("[MockFallback]", result.Text, StringComparison.Ordinal);
        var warning = Assert.Single(logger.Entries, entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
        Assert.Contains("回退模型", warning.Message, StringComparison.Ordinal);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StubHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name)
        {
            return _client;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
