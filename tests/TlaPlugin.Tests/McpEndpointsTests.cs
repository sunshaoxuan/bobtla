using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TlaPlugin.Configuration;
using Xunit;

namespace TlaPlugin.Tests;

public class McpEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public McpEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListRequiresAuthorization()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/mcp/tools/list");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListReturnsSchemas()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/mcp/tools/list");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "token");

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<McpListResponse>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Tools, tool => tool.Name == "tla.translate");
        Assert.All(payload.Tools, tool => Assert.NotNull(tool.InputSchema));
    }

    [Fact]
    public async Task CallValidatesSchema()
    {
        var client = _factory.CreateClient();
        var request = CreateCallRequest(new
        {
            name = "tla.translate",
            arguments = new
            {
                targetLanguage = "ja",
                tenantId = "contoso",
                userId = "user"
            }
        });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(payload);
        Assert.Equal("Missing required property 'text'.", payload!["error"]);
    }

    [Fact]
    public async Task CallMapsGlossaryConflicts()
    {
        var client = _factory.CreateClient();
        var request = CreateCallRequest(new
        {
            name = "tla.applyGlossary",
            arguments = new
            {
                text = "no matches here",
                tenantId = "contoso",
                userId = "user",
                policy = "Strict"
            }
        });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsPayload>();
        Assert.NotNull(problem);
        Assert.Contains("用語", problem!.Detail);
    }

    [Fact]
    public async Task CallRespectsComplianceGuard()
    {
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.PostConfigure<PluginOptions>(options =>
                {
                    options.Compliance.RequiredRegionTags = new List<string> { "restricted" };
                    foreach (var provider in options.Providers)
                    {
                        provider.Regions = new List<string> { "elsewhere" };
                    }
                });
            });
        }).CreateClient();

        var request = CreateCallRequest(new
        {
            name = "tla.translate",
            arguments = new
            {
                text = "hello world",
                targetLanguage = "ja",
                tenantId = "contoso",
                userId = "user"
            }
        });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsPayload>();
        Assert.NotNull(problem);
        Assert.Contains("モデル", problem!.Detail);
    }

    private static HttpRequestMessage CreateCallRequest(object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp/tools/call")
        {
            Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "token");
        return request;
    }

    private sealed class McpListResponse
    {
        public IReadOnlyList<McpToolDescriptor> Tools { get; set; } = new List<McpToolDescriptor>();
    }

    private sealed class McpToolDescriptor
    {
        public string Name { get; set; } = string.Empty;
        public JsonElement InputSchema { get; set; }
            = JsonDocument.Parse("{}" ).RootElement;
    }

    private sealed class ProblemDetailsPayload
    {
        public string? Title { get; set; }
            = string.Empty;
        public string Detail { get; set; } = string.Empty;
    }
}
