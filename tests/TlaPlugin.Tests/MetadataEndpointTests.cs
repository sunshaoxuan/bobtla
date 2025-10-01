using System.Linq;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using TlaPlugin.Models;
using Xunit;

namespace TlaPlugin.Tests;

public class MetadataEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MetadataEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MetadataEndpoint_ExposesModelsLanguagesAndFeatures()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/metadata");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<MetadataResponse>();
        Assert.NotNull(payload);

        Assert.NotEmpty(payload!.Models);
        Assert.All(payload.Models, model =>
        {
            Assert.False(string.IsNullOrWhiteSpace(model.Id));
            Assert.False(string.IsNullOrWhiteSpace(model.DisplayName));
            Assert.True(model.CostPerCharUsd >= 0);
        });

        Assert.Contains(payload.Languages, lang => lang.Id == "auto");
        Assert.Contains(payload.Languages, lang => lang.IsDefault && lang.Id != "auto");

        Assert.True(payload.Features.TerminologyToggle);
        Assert.True(payload.Features.OfflineDraft);
        Assert.True(payload.Features.ToneToggle);
        Assert.Equal("USD", payload.Pricing.Currency);
        Assert.True(payload.Pricing.DailyBudgetUsd > 0);
    }
}
