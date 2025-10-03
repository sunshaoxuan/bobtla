using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using Xunit;

namespace TlaPlugin.Tests.Configuration;

public class AppConfigurationTests
{
    [Fact]
    public void StageEnvironmentLoadsStageAppSettings()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(WebHostDefaults.EnvironmentKey, "Stage");
            });

        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<PluginOptions>>().Value;

        Assert.NotNull(options.Security);
        Assert.False(options.Security!.UseHmacFallback);

        var scopes = options.Security.GraphScopes ?? Array.Empty<string>();
        Assert.Contains("https://graph.microsoft.com/.default", scopes);
        Assert.Contains("https://graph.microsoft.com/Chat.ReadWrite", scopes);
    }
}
