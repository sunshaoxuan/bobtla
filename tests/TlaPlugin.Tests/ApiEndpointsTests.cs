using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class ApiEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DetectEndpointReturnsLanguage()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/detect", new LanguageDetectionRequest
        {
            Text = "こんにちは",
            TenantId = "contoso"
        });

        response.EnsureSuccessStatusCode();
        var detection = await response.Content.ReadFromJsonAsync<DetectionResult>();
        Assert.NotNull(detection);
        Assert.Equal("ja", detection!.Language);
    }

    [Fact]
    public async Task ApplyGlossaryReturnsMatches()
    {
        var client = _factory.CreateClient();
        var request = new GlossaryApplicationRequest
        {
            Text = "CPU compliance",
            TenantId = "contoso",
            UserId = "admin",
            Policy = GlossaryPolicy.Fallback.ToString()
        };

        var response = await client.PostAsJsonAsync("/api/apply-glossary", request);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<GlossaryResponse>();
        Assert.NotNull(payload);
        Assert.Contains("中央处理器", payload!.ProcessedText);
        Assert.NotEmpty(payload.Matches);
    }

    [Fact]
    public async Task ReplyEndpointHonorsChannelRestrictions()
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.PostConfigure<PluginOptions>(options =>
                {
                    options.Security.AllowedReplyChannels = new List<string> { "allowed" };
                });
            });
        }).CreateClient();

        var response = await client.PostAsJsonAsync("/api/reply", new ReplyRequest
        {
            ThreadId = "thread",
            ReplyText = "hello",
            TenantId = "contoso",
            UserId = "user",
            ChannelId = "blocked"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CostLatencyEndpointReturnsNotFoundForUnknownModel()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/cost-latency?payloadSize=100&modelId=unknown");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RewriteEndpointReturnsPaymentRequiredWhenOverBudget()
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.PostConfigure<PluginOptions>(options =>
                {
                    options.DailyBudgetUsd = 0.00001m;
                });
            });
        }).CreateClient();

        var response = await client.PostAsJsonAsync("/api/rewrite", new RewriteRequest
        {
            Text = new string('a', 10),
            TenantId = "contoso",
            UserId = "user",
            Tone = ToneTemplateService.Business
        });

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    private sealed class GlossaryResponse
    {
        public string ProcessedText { get; set; } = string.Empty;
        public List<GlossaryMatch> Matches { get; set; } = new();
    }
}
