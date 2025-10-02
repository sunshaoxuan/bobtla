using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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

    private sealed record OfflineDraftSavedResponse(string Type, long DraftId, string Status, string CreatedAt);

    private sealed record OfflineDraftListResponse(List<OfflineDraftRecord> Drafts);

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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DetectEndpointReturnsBadRequestForMissingText(string? text)
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/detect", new
        {
            Text = text,
            TenantId = "contoso"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(payload);
        Assert.Equal("Text is required.", payload!["error"]);
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
    public async Task GlossaryUploadEndpointRejectsRequestWithoutFile()
    {
        var client = _factory.CreateClient();
        using var form = new MultipartFormDataContent
        {
            { new StringContent("tenant"), "scopeType" },
            { new StringContent("contoso"), "scopeId" }
        };

        var response = await client.PostAsync("/api/glossary/upload", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GlossaryUploadEndpointReturnsConflicts()
    {
        var client = _factory.CreateClient();
        var csv = "source,target\nCPU,处理器\nGPU,显卡";
        using var form = new MultipartFormDataContent
        {
            { new StringContent("tenant"), "scopeType" },
            { new StringContent("contoso"), "scopeId" },
            { new StringContent("false"), "overwrite" }
        };

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        form.Add(fileContent, "file", "upload.csv");

        var response = await client.PostAsync("/api/glossary/upload", form);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<GlossaryUploadResponse>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Imported);
        Assert.Equal(0, payload.Updated);
        Assert.Single(payload.Conflicts);
        Assert.Empty(payload.Errors);

        var conflicts = payload.Conflicts[0];
        Assert.Equal("CPU", conflicts.Source);
        Assert.Equal("tenant:contoso", conflicts.Scope);

        var glossary = await client.GetFromJsonAsync<List<GlossaryEntry>>("/api/glossary");
        Assert.Contains(glossary!, entry => entry.Source == "GPU" && entry.Target == "显卡");
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

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "assertion");

        var response = await client.PostAsJsonAsync("/api/rewrite", new RewriteRequest
        {
            Text = new string('a', 10),
            TenantId = "contoso",
            UserId = "user",
            UserAssertion = "assertion",
            Tone = ToneTemplateService.Business
        });

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    [Fact]
    public async Task RewriteEndpointReturnsRewrittenTextForEditedInput()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "assertion");

        var response = await client.PostAsJsonAsync("/api/rewrite", new RewriteRequest
        {
            Text = "Original",
            EditedText = "用户自定义",
            TenantId = "contoso",
            UserId = "user",
            UserAssertion = "assertion",
            Tone = ToneTemplateService.Business
        });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<RewriteResponse>();
        Assert.NotNull(payload);
        Assert.Contains("用户自定义", payload!.RewrittenText);
    }

    [Fact]
    public async Task ReplyEndpointReturnsSuccessPayload()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "assertion");

        var response = await client.PostAsJsonAsync("/api/reply", new ReplyRequest
        {
            ThreadId = "thread",
            Text = "Hello team",
            EditedText = "手动编辑",
            TenantId = "contoso",
            UserId = "user",
            UserAssertion = "assertion",
            ChannelId = "general",
            LanguagePolicy = new ReplyLanguagePolicy { TargetLang = "ja", Tone = ToneTemplateService.Business }
        });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ReplyResult>();
        Assert.NotNull(payload);
        Assert.Equal("sent", payload!.Status);
        Assert.Contains("手动编辑", payload.FinalText);
    }

    [Fact]
    public async Task OfflineDraftEndpointsRequireAuthorization()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/offline-draft", new OfflineDraftRequest
        {
            OriginalText = "hello",
            TargetLanguage = "ja",
            TenantId = "contoso",
            UserId = "user"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OfflineDraftEndpointsReturnDraftList()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/offline-draft")
        {
            Content = JsonContent.Create(new OfflineDraftRequest
            {
                OriginalText = "需要审核的文本",
                TargetLanguage = "zh-Hans",
                TenantId = "contoso",
                UserId = "user"
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "token");

        var postResponse = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        var saved = await postResponse.Content.ReadFromJsonAsync<OfflineDraftSavedResponse>();
        Assert.NotNull(saved);
        Assert.Equal("offlineDraftSaved", saved!.Type);
        Assert.True(saved.DraftId > 0);
        Assert.Equal(OfflineDraftStatus.Processing, saved.Status);

        var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/offline-draft?userId=user")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", "token") }
        };
        var listResponse = await client.SendAsync(listRequest);
        listResponse.EnsureSuccessStatusCode();
        var list = await listResponse.Content.ReadFromJsonAsync<OfflineDraftListResponse>();
        Assert.NotNull(list);
        Assert.Contains(list!.Drafts, draft => draft.Id == saved.DraftId);
        var stored = list!.Drafts.Single(draft => draft.Id == saved.DraftId);
        Assert.Equal(OfflineDraftStatus.Processing, stored.Status);
        Assert.Equal(0, stored.Attempts);
        Assert.Null(stored.CompletedAt);
    }

    private sealed class GlossaryResponse
    {
        public string ProcessedText { get; set; } = string.Empty;
        public List<GlossaryMatchDetail> Matches { get; set; } = new();
    }

    private sealed class GlossaryUploadResponse
    {
        public int Imported { get; set; }
        public int Updated { get; set; }
        public List<GlossaryUploadConflict> Conflicts { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    private sealed class RewriteResponse
    {
        public string RewrittenText { get; set; } = string.Empty;
        public string ModelId { get; set; } = string.Empty;
    }
}
