using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();

        Assert.NotNull(options.Security);
        Assert.False(options.Security!.UseHmacFallback);

        var scopes = options.Security.GraphScopes ?? Array.Empty<string>();
        Assert.Contains("https://graph.microsoft.com/.default", scopes);
        Assert.Contains("https://graph.microsoft.com/Chat.ReadWrite", scopes);

        var stageFile = environment.ContentRootFileProvider.GetFileInfo("appsettings.Stage.json");
        Assert.True(stageFile.Exists);
        Assert.True(stageFile.PhysicalPath!.EndsWith("appsettings.Stage.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnvironmentVariablesOverrideConfiguration()
    {
        const string variableName = "Plugin__Security__UseHmacFallback";
        var originalValue = Environment.GetEnvironmentVariable(variableName);

        try
        {
            Environment.SetEnvironmentVariable(variableName, "true");

            using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseSetting(WebHostDefaults.EnvironmentKey, "Stage");
                });

            using var scope = factory.Services.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<PluginOptions>>().Value;

            Assert.NotNull(options.Security);
            Assert.True(options.Security!.UseHmacFallback);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, originalValue);
        }
    }
}
