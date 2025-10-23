using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Providers;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class LiveModelSmokeHarnessTests
{
    [Fact]
    public async Task ExecuteAsync_WithLiveModelAndFallback_EmitsSuccessAndFallbackLogs()
    {
        var options = new PluginOptions
        {
            Providers =
            {
                new ModelProviderOptions
                {
                    Id = "incomplete",
                    Kind = ModelProviderKind.OpenAi,
                    Endpoint = string.Empty,
                    ApiKeySecretName = string.Empty
                },
                new ModelProviderOptions
                {
                    Id = "primary",
                    Kind = ModelProviderKind.OpenAi,
                    Endpoint = "https://api.test/v1/chat/completions",
                    Model = "gpt-4o-mini",
                    ApiKeySecretName = "openai-api-key"
                }
            },
            Security = new SecurityOptions
            {
                SeedSecrets = new Dictionary<string, string>
                {
                    ["openai-api-key"] = "live-secret"
                }
            }
        };

        var request = new TranslationRequest
        {
            Text = "Hello world",
            SourceLanguage = "en",
            TargetLanguage = "ja",
            TenantId = "tenant",
            UserId = "user",
            ThreadId = "thread",
            Tone = ToneTemplateService.Business,
            UserAssertion = "assertion"
        };

        var handler = new StubMessageHandler(_ =>
        {
            var responsePayload = new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            content = "翻訳結果"
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

        var httpFactory = new StaticHttpClientFactory(handler);
        var resolver = new KeyVaultSecretResolver(Options.Create(options));

        var loggerProvider = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(loggerProvider);
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var harness = new LiveModelSmokeHarness(options, loggerFactory, httpFactory, resolver);

        var result = await harness.ExecuteAsync(request, CancellationToken.None);

        Assert.NotNull(result.Success);
        Assert.Equal("primary", result.Success!.ProviderId);
        Assert.Contains("incomplete", result.FallbackProviders);

        var providerLogs = loggerProvider.Entries
            .Where(entry => entry.Category == typeof(ConfigurableChatModelProvider).FullName)
            .ToList();

        Assert.Contains(providerLogs, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("回退模型", StringComparison.Ordinal));
        Assert.Contains(providerLogs, entry => entry.Level == LogLevel.Information && entry.Message.Contains("完成 translate 调用", StringComparison.Ordinal));
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StaticHttpClientFactory(HttpMessageHandler handler)
        {
            _client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        private readonly List<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries => _entries;

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, _entries);

        public void Dispose()
        {
        }

        public sealed record LogEntry(string Category, LogLevel Level, string Message, Exception? Exception);

        private sealed class RecordingLogger : ILogger
        {
            private readonly string _category;
            private readonly List<LogEntry> _entries;

            public RecordingLogger(string category, List<LogEntry> entries)
            {
                _category = category;
                _entries = entries;
            }

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var message = formatter(state, exception);
                _entries.Add(new LogEntry(_category, logLevel, message, exception));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
