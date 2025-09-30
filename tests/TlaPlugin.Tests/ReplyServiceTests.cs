using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
            ChannelId = "general",
            LanguagePolicy = new ReplyLanguagePolicy { TargetLang = "ja", Tone = ToneTemplateService.Casual }
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
        Assert.Equal("ja", root.GetProperty("channelData").GetProperty("metadata").GetProperty("language").GetString());
        Assert.Equal(ToneTemplateService.Casual, root.GetProperty("channelData").GetProperty("metadata").GetProperty("tone").GetString());
        var html = root.GetProperty("body").GetProperty("content").GetString();
        Assert.Contains("グラフ投稿", html, StringComparison.Ordinal);
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
        return new ReplyService(rewrite, teamsClient, broker, metrics, options);
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

        public Task<AccessToken> ExchangeOnBehalfOfAsync(string tenantId, string userId, CancellationToken cancellationToken)
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
