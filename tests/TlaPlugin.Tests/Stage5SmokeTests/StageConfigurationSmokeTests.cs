using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using TlaPlugin.Configuration;
using Xunit;

namespace TlaPlugin.Tests.Stage5SmokeTests;

public class StageConfigurationSmokeTests
{
    [Fact]
    public void StageAppsettings_DisablesHmacFallback_TriggersRemoteModeAutomatically()
    {
        var repositoryRoot = LocateRepositoryRoot();
        var stageAppsettings = Path.Combine(repositoryRoot, "src", "TlaPlugin", "appsettings.Stage.json");
        Assert.True(File.Exists(stageAppsettings));

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(stageAppsettings, optional: false, reloadOnChange: false)
            .Build();

        var options = new PluginOptions();
        configuration.GetSection("Plugin").Bind(options);

        Assert.False(options.Security.UseHmacFallback);

        var decision = SmokeTestModeDecider.Decide(options, new Dictionary<string, string?>());

        Assert.True(decision.UseRemoteApi);
        Assert.True(decision.IsAutomatic);
        Assert.Equal("配置中已禁用 UseHmacFallback", decision.Reason);
    }

    static string LocateRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "bobtla.sln")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }
}
