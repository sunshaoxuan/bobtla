using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class ReplyServiceTests
{
    [Fact]
    public async Task ThrowsWhenChannelNotAllowed()
    {
        var options = Options.Create(new PluginOptions
        {
            Security = new SecurityOptions
            {
                AllowedReplyChannels = new List<string> { "general" }
            },
            Providers = new List<ModelProviderOptions> { new() { Id = "primary" } }
        });

        var metrics = new UsageMetricsService();
        var teamsClient = new RecordingTeamsClient();
        var service = CreateService(options, teamsClient, metrics);

        await Assert.ThrowsAsync<ReplyAuthorizationException>(() => service.SendReplyAsync(new ReplyRequest
        {
            ThreadId = "thread",
            ReplyText = "hello",
            TenantId = "contoso",
            UserId = "user",
            UserAssertion = "assertion",
            ChannelId = "random"
        }, CancellationToken.None));
    }

    [Fact]
    public async Task AppliesToneWhenLanguagePolicySpecified()
    {
        var options = Options.Create(new PluginOptions
        {
            Providers = new List<ModelProviderOptions> { new() { Id = "primary" } }
        });

        var metrics = new UsageMetricsService();
        var teamsClient = new RecordingTeamsClient
        {
            Response = new TeamsReplyResponse("msg-1", DateTimeOffset.UtcNow, "sent")
        };
        var service = CreateService(options, teamsClient, metrics);

        var result = await service.SendReplyAsync(new ReplyRequest
        {
            ThreadId = "thread",
            ReplyText = "こんにちは",
            EditedText = "手动调整",
            TenantId = "contoso",
            UserId = "user",
            UserAssertion = "assertion",
            ChannelId = "general",
            LanguagePolicy = new ReplyLanguagePolicy { Tone = ToneTemplateService.Business, TargetLang = "ja" }
        }, CancellationToken.None);

        Assert.Equal("sent", result.Status);
        Assert.Contains("手动调整", result.FinalText);
        Assert.Contains("商务语气", result.FinalText);
        Assert.Equal(ToneTemplateService.Business, result.ToneApplied);
        Assert.Equal("ja", result.Language);
        Assert.Equal("msg-1", result.MessageId);
        Assert.Equal("ja", teamsClient.LastRequest?.Language);
        Assert.Equal(ToneTemplateService.Business, teamsClient.LastRequest?.Tone);
    }

    [Fact]
    public async Task SendsAdditionalTranslationsWhenRequested()
    {
        var options = Options.Create(new PluginOptions
        {
            Providers = new List<ModelProviderOptions> { new() { Id = "primary" } }
        });

        var metrics = new UsageMetricsService();
        var teamsClient = new RecordingTeamsClient
        {
            Response = new TeamsReplyResponse("msg-multi", DateTimeOffset.UtcNow, "sent")
        };
        var service = CreateService(options, teamsClient, metrics);

        var result = await service.SendReplyAsync(new ReplyRequest
        {
            ThreadId = "thread",
            ReplyText = "Bonjour",
            TenantId = "contoso",
            UserId = "user",
            UserAssertion = "assertion",
            ChannelId = "general",
            LanguagePolicy = new ReplyLanguagePolicy { TargetLang = "fr", Tone = ToneTemplateService.Casual },
            AdditionalTargetLanguages = new List<string> { "en", "en", "de" }
        }, CancellationToken.None);

        Assert.Equal("sent", result.Status);
        Assert.NotNull(teamsClient.LastRequest);
        var additional = teamsClient.LastRequest!.AdditionalTranslations;
        Assert.True(additional.ContainsKey("en"));
        Assert.True(additional.ContainsKey("de"));
        var card = teamsClient.LastRequest!.AdaptiveCard;
        Assert.NotNull(card);
        var body = card!["body"]?.AsArray();
        Assert.NotNull(body);
        var primaryInfo = body![1]!.AsObject();
        Assert.Equal("Primary language: fr", primaryInfo["text"]!.GetValue<string>());
        var translationBlocks = body
            .Skip(2)
            .Select(node => node!.AsObject()["text"]!.GetValue<string>())
            .ToList();
        Assert.Collection(translationBlocks,
            text => Assert.StartsWith("en:", text, StringComparison.OrdinalIgnoreCase),
            text => Assert.StartsWith("de:", text, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(result.FinalText, teamsClient.LastRequest!.FinalText);
        Assert.Contains("[en]", result.FinalText, StringComparison.Ordinal);
        Assert.Contains("[de]", result.FinalText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThrowsWhenThreadIdMissing()
    {
        var options = Options.Create(new PluginOptions
        {
            Providers = new List<ModelProviderOptions> { new() { Id = "primary" } }
        });

        var metrics = new UsageMetricsService();
        var service = CreateService(options, new RecordingTeamsClient(), metrics);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SendReplyAsync(new ReplyRequest
        {
            ReplyText = "hi",
            TenantId = "contoso",
            UserId = "user"
        }, CancellationToken.None));
    }

    [Fact]
    public async Task UsesFinalTextOverrideWhenProvided()
    {
        var options = Options.Create(new PluginOptions
        {
            Providers = new List<ModelProviderOptions> { new() { Id = "primary" } }
        });

        var metrics = new UsageMetricsService();
        var teamsClient = new RecordingTeamsClient
        {
            Response = new TeamsReplyResponse("msg-override", DateTimeOffset.UtcNow, "sent")
        };
        var service = CreateService(options, teamsClient, metrics);

        var result = await service.SendReplyAsync(new ReplyRequest
        {
            ThreadId = "thread",
            ReplyText = "ignored",
            TenantId = "contoso",
            UserId = "user",
            UserAssertion = "assertion",
            ChannelId = "general",
            LanguagePolicy = new ReplyLanguagePolicy { TargetLang = "ja", Tone = ToneTemplateService.Business }
        }, "最终文本", ToneTemplateService.Business, CancellationToken.None);

        Assert.Equal("最终文本", result.FinalText);
        Assert.Equal(ToneTemplateService.Business, result.ToneApplied);
        Assert.Equal("msg-override", result.MessageId);
        Assert.Equal("最终文本", teamsClient.LastRequest?.FinalText);
    }

    [Fact]
    public async Task ThrowsWhenFinalTextOverrideMissing()
    {
        var options = Options.Create(new PluginOptions
        {
            Providers = new List<ModelProviderOptions> { new() { Id = "primary" } }
        });

        var metrics = new UsageMetricsService();
        var service = CreateService(options, new RecordingTeamsClient(), metrics);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SendReplyAsync(new ReplyRequest
        {
            ThreadId = "thread",
            ReplyText = "ignored",
            TenantId = "contoso",
            UserId = "user",
            UserAssertion = "assertion",
            ChannelId = "general",
            LanguagePolicy = new ReplyLanguagePolicy { TargetLang = "ja" }
        }, string.Empty, null, CancellationToken.None));
    }

    [Fact]
    public async Task SendReplyAsyncUsesHttpClientAndMetadata()
    {
        var options = Options.Create(new PluginOptions
        {
            Providers = new List<ModelProviderOptions> { new() { Id = "primary" } }
        });

        JsonDocument? capturedPayload = null;
        string? capturedPath = null;
        string? authorization = null;

        var handler = new StubHttpMessageHandler(request =>
        {
            capturedPath = request.RequestUri?.PathAndQuery.TrimStart('/');
            authorization = request.Headers.Authorization?.ToString();
            var payloadText = request.Content is null ? string.Empty : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            capturedPayload = string.IsNullOrWhiteSpace(payloadText) ? null : JsonDocument.Parse(payloadText);

            var response = new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{\"id\":\"msg-http\",\"createdDateTime\":\"2024-02-01T12:34:56Z\"}", Encoding.UTF8, "application/json")
            };
            return response;
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://graph.test/v1.0/")
        };
        var teamsClient = new TeamsReplyClient(httpClient);
        var metrics = new UsageMetricsService();
        var service = CreateService(options, teamsClient, metrics, new RecordingTokenBroker("token-http"));

        var result = await service.SendReplyAsync(new ReplyRequest
        {
            ThreadId = "message",
            ReplyText = "ignored",
            TenantId = "contoso",
            UserId = "user",
            UserAssertion = "assertion",
            ChannelId = "general",
            LanguagePolicy = new ReplyLanguagePolicy { TargetLang = "ja", Tone = ToneTemplateService.Casual },
            AdditionalTargetLanguages = new List<string> { "fr" }
        }, "グラフ投稿", ToneTemplateService.Casual, CancellationToken.None);

        Assert.Equal("msg-http", result.MessageId);
        Assert.Equal("sent", result.Status);
        Assert.Equal("ja", result.Language);
        Assert.Equal(DateTimeOffset.Parse("2024-02-01T12:34:56Z", CultureInfo.InvariantCulture), result.PostedAt);
        Assert.Equal("teams/contoso/channels/general/messages/message/replies", capturedPath);
        Assert.Equal("Bearer token-http", authorization);
        Assert.NotNull(capturedPayload);
        var root = capturedPayload!.RootElement;
        Assert.Equal("message", root.GetProperty("replyToId").GetString());
        var metadata = root.GetProperty("channelData").GetProperty("metadata");
        Assert.Equal("ja", metadata.GetProperty("language").GetString());
        Assert.Equal(ToneTemplateService.Casual, metadata.GetProperty("tone").GetString());
        var extras = metadata.GetProperty("additionalTranslations");
        Assert.Equal("fr", Assert.Single(extras.EnumerateObject()).Name);
        Assert.Contains("fr", extras.GetProperty("fr").GetString());
        var html = root.GetProperty("body").GetProperty("content").GetString();
        Assert.Contains("グラフ投稿", html, StringComparison.Ordinal);
        Assert.Contains("[fr]", html, StringComparison.Ordinal);
        var attachments = root.GetProperty("attachments");
        var card = attachments[0].GetProperty("content");
        Assert.Equal("application/vnd.microsoft.card.adaptive", attachments[0].GetProperty("contentType").GetString());
        Assert.Contains("グラフ投稿", card.GetProperty("body")[0].GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendReplyAsyncThrowsReplyAuthorizationWhenForbidden()
    {
        var options = Options.Create(new PluginOptions
        {
            Providers = new List<ModelProviderOptions> { new() { Id = "primary" } }
        });

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"error\":{\"code\":\"Forbidden\",\"message\":\"access denied\"}}", Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://graph.test/") };
        var teamsClient = new TeamsReplyClient(httpClient);
        var metrics = new UsageMetricsService();
        var service = CreateService(options, teamsClient, metrics, new RecordingTokenBroker());

        await Assert.ThrowsAsync<ReplyAuthorizationException>(() => service.SendReplyAsync(new ReplyRequest
        {
            ThreadId = "thread",
            ReplyText = "text",
            TenantId = "contoso",
            UserId = "user",
            UserAssertion = "assertion",
            ChannelId = "general",
            LanguagePolicy = new ReplyLanguagePolicy { TargetLang = "ja" }
        }, "最终", null, CancellationToken.None));

        var report = metrics.GetReport();
        var tenant = Assert.Single(report.Tenants);
        Assert.Contains(tenant.Failures, failure => failure.Reason == UsageMetricsService.FailureReasons.Authentication && failure.Count == 1);
    }

    [Fact]
    public async Task SendReplyAsyncThrowsBudgetExceededWhenPaymentRequired()
    {
        var options = Options.Create(new PluginOptions
        {
            Providers = new List<ModelProviderOptions> { new() { Id = "primary" } }
        });

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.PaymentRequired)
        {
            Content = new StringContent("{\"error\":{\"code\":\"Budget\",\"message\":\"budget exceeded\"}}", Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://graph.test/") };
        var teamsClient = new TeamsReplyClient(httpClient);
        var metrics = new UsageMetricsService();
        var service = CreateService(options, teamsClient, metrics, new RecordingTokenBroker());

        await Assert.ThrowsAsync<BudgetExceededException>(() => service.SendReplyAsync(new ReplyRequest
        {
            ThreadId = "thread",
            ReplyText = "text",
            TenantId = "contoso",
            UserId = "user",
            UserAssertion = "assertion",
            ChannelId = "general",
            LanguagePolicy = new ReplyLanguagePolicy { TargetLang = "ja" }
        }, "最终", null, CancellationToken.None));

        var report = metrics.GetReport();
        var tenant = Assert.Single(report.Tenants);
        Assert.Contains(tenant.Failures, failure => failure.Reason == UsageMetricsService.FailureReasons.Budget && failure.Count == 1);
    }

    private static ReplyService CreateService(IOptions<PluginOptions> options, ITeamsReplyClient teamsClient, UsageMetricsService metrics, ITokenBroker? tokenBroker = null)
    {
        var broker = tokenBroker ?? new RecordingTokenBroker();
        var router = new TranslationRouter(new ModelProviderFactory(options), new ComplianceGateway(options), new BudgetGuard(options.Value), new AuditLogger(), new ToneTemplateService(), broker, metrics, new LocalizationCatalogService(), options);
        var throttle = new TranslationThrottle(options);
        var rewrite = new RewriteService(router, throttle);
        return new ReplyService(rewrite, router, teamsClient, broker, metrics, options);
    }

    private sealed class RecordingTeamsClient : ITeamsReplyClient
    {
        public TeamsReplyRequest? LastRequest { get; private set; }

        public TeamsReplyResponse Response { get; set; } = new TeamsReplyResponse("message", DateTimeOffset.UtcNow, "sent");

        public Task<TeamsReplyResponse> SendReplyAsync(TeamsReplyRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(Response);
        }
    }

    private sealed class RecordingTokenBroker : ITokenBroker
    {
        private readonly string _token;

        public RecordingTokenBroker(string token = "token")
        {
            _token = token;
        }

        public Task<AccessToken> ExchangeOnBehalfOfAsync(string tenantId, string userId, string? userAssertion, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AccessToken(_token, DateTimeOffset.UtcNow.AddMinutes(10), "audience"));
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}

public class ReplyServiceMinimalApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ReplyServiceMinimalApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ReplyEndpointUsesTypedHttpClientFactoryRegistration()
    {
        var responseBody = "{\"id\":\"msg-api\",\"createdDateTime\":\"2024-03-01T09:10:11Z\"}";
        var client = CreateReplyClient(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        }, out var state);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "assertion");

        var response = await client.PostAsJsonAsync("/api/reply", new ReplyRequest
        {
            ThreadId = "thread",
            ReplyText = "你好",
            TenantId = "contoso",
            UserId = "user",
            UserAssertion = "assertion",
            ChannelId = "general",
            LanguagePolicy = new ReplyLanguagePolicy { TargetLang = "zh-CN", Tone = ToneTemplateService.Casual }
        });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ReplyResult>();

        Assert.NotNull(payload);
        Assert.Equal("msg-api", payload!.MessageId);
        Assert.Equal("sent", payload.Status);

        Assert.Equal(1, state.RequestCount);
        Assert.Equal("/teams/contoso/channels/general/messages/thread/replies", state.LastPath);
        Assert.Equal("Bearer api-token", state.LastAuthorization);

        Assert.NotNull(state.LastContent);
        using var document = JsonDocument.Parse(state.LastContent);
        Assert.Equal("thread", document.RootElement.GetProperty("replyToId").GetString());
        Assert.Equal("zh-CN", document.RootElement.GetProperty("channelData").GetProperty("metadata").GetProperty("language").GetString());
        Assert.Equal(ToneTemplateService.Casual, document.RootElement.GetProperty("channelData").GetProperty("metadata").GetProperty("tone").GetString());
    }

    [Fact]
    public async Task ReplyEndpointReturnsForbiddenWhenTeamsClientRespondsForbidden()
    {
        var client = CreateReplyClient(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"error\":{\"message\":\"denied\"}}", Encoding.UTF8, "application/json")
        }, out var state);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "assertion");

        var response = await client.PostAsJsonAsync("/api/reply", new ReplyRequest
        {
            ThreadId = "thread",
            ReplyText = "内容",
            TenantId = "contoso",
            UserId = "user",
            UserAssertion = "assertion",
            ChannelId = "general",
            LanguagePolicy = new ReplyLanguagePolicy { TargetLang = "zh-CN" }
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, state.RequestCount);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal("指定されたチャネルでは返信権限がありません。", problem!.Detail);
    }

    [Fact]
    public async Task ReplyEndpointReturnsPaymentRequiredWhenTeamsClientRespondsPaymentRequired()
    {
        var client = CreateReplyClient(_ => new HttpResponseMessage(HttpStatusCode.PaymentRequired)
        {
            Content = new StringContent("{\"error\":{\"message\":\"budget exceeded\"}}", Encoding.UTF8, "application/json")
        }, out _);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "assertion");

        var response = await client.PostAsJsonAsync("/api/reply", new ReplyRequest
        {
            ThreadId = "thread",
            ReplyText = "内容",
            TenantId = "contoso",
            UserId = "user",
            UserAssertion = "assertion",
            ChannelId = "general",
            LanguagePolicy = new ReplyLanguagePolicy { TargetLang = "zh-CN" }
        });

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("budget exceeded", problem!.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private HttpClient CreateReplyClient(Func<HttpRequestMessage, HttpResponseMessage> responseFactory, out HandlerState state)
    {
        state = new HandlerState();
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITeamsReplyClient>();
                services.RemoveAll<ITokenBroker>();
                services.AddSingleton(state);
                services.AddSingleton(responseFactory);
                services.AddHttpClient<ITeamsReplyClient, TeamsReplyClient>()
                    .ConfigureHttpClient(client =>
                    {
                        client.BaseAddress = new Uri("https://graph.test/v1.0/", UriKind.Absolute);
                        client.Timeout = TimeSpan.FromSeconds(5);
                    })
                    .ConfigurePrimaryHttpMessageHandler(sp =>
                    {
                        var handlerState = sp.GetRequiredService<HandlerState>();
                        var factory = sp.GetRequiredService<Func<HttpRequestMessage, HttpResponseMessage>>();
                        return new CapturingHttpMessageHandler(handlerState, factory);
                    });
                services.AddSingleton<ITokenBroker>(new TestTokenBroker("api-token"));
            });
        }).CreateClient();
    }

    private sealed class TestTokenBroker : ITokenBroker
    {
        private readonly string _token;

        public TestTokenBroker(string token)
        {
            _token = token;
        }

        public Task<AccessToken> ExchangeOnBehalfOfAsync(string tenantId, string userId, string? userAssertion, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AccessToken(_token, DateTimeOffset.UtcNow.AddMinutes(5), "audience"));
        }
    }

    private sealed class HandlerState
    {
        public int RequestCount { get; set; }
        public string? LastPath { get; set; }
        public string? LastAuthorization { get; set; }
        public string? LastContent { get; set; }
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HandlerState _state;
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;

        public CapturingHttpMessageHandler(HandlerState state, Func<HttpRequestMessage, HttpResponseMessage> factory)
        {
            _state = state;
            _factory = factory;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _state.RequestCount++;
            _state.LastPath = request.RequestUri?.PathAndQuery;
            _state.LastAuthorization = request.Headers.Authorization?.ToString();
            if (request.Content is not null)
            {
                _state.LastContent = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            return _factory(request);
        }
    }
}
