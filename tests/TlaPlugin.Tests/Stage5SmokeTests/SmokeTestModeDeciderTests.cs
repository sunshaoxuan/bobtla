using System.Collections.Generic;
using TlaPlugin.Configuration;
using Xunit;

namespace TlaPlugin.Tests.Stage5SmokeTests;

public class SmokeTestModeDeciderTests
{
    [Fact]
    public void Decide_AutoRemote_WhenHmacFallbackDisabled()
    {
        var options = new PluginOptions
        {
            Security =
            {
                UseHmacFallback = false
            }
        };

        var decision = SmokeTestModeDecider.Decide(options, new Dictionary<string, string?>());

        Assert.True(decision.UseRemoteApi);
        Assert.True(decision.IsAutomatic);
        Assert.Equal("配置中已禁用 UseHmacFallback", decision.Reason);
    }

    [Fact]
    public void Decide_AutoRemote_WhenBaseUrlProvided()
    {
        var options = new PluginOptions();
        var parameters = new Dictionary<string, string?>
        {
            ["baseUrl"] = "https://stage5.example" 
        };

        var decision = SmokeTestModeDecider.Decide(options, parameters);

        Assert.True(decision.UseRemoteApi);
        Assert.True(decision.IsAutomatic);
        Assert.Equal("检测到 --baseUrl 参数", decision.Reason);
        Assert.True(decision.AutoConditionMet);
        Assert.True(decision.BaseUrlProvided);
    }

    [Fact]
    public void Decide_RespectsLocalStubOverride()
    {
        var options = new PluginOptions
        {
            Security =
            {
                UseHmacFallback = false
            }
        };

        var parameters = new Dictionary<string, string?>
        {
            ["use-local-stub"] = null,
            ["baseUrl"] = "https://stage5.example"
        };

        var decision = SmokeTestModeDecider.Decide(options, parameters);

        Assert.False(decision.UseRemoteApi);
        Assert.True(decision.LocalStubRequested);
        Assert.True(decision.AutoConditionMet);
        Assert.True(decision.BaseUrlProvided);
    }
}
