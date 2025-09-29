using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class KeyVaultSecretResolverTests
{
    [Fact]
    public async Task ReturnsSeedSecret()
    {
        var options = Options.Create(new PluginOptions
        {
            Security = new SecurityOptions
            {
                SeedSecrets = new Dictionary<string, string> { ["custom-secret"] = "value" }
            }
        });

        var resolver = new KeyVaultSecretResolver(options);
        var value = await resolver.GetSecretAsync("custom-secret", CancellationToken.None);

        Assert.Equal("value", value);
    }

    [Fact]
    public async Task UsesCacheUntilInvalidated()
    {
        var options = Options.Create(new PluginOptions
        {
            Security = new SecurityOptions
            {
                SeedSecrets = new Dictionary<string, string> { ["custom-secret"] = "value" },
                SecretCacheTtl = TimeSpan.FromMinutes(5)
            }
        });

        var resolver = new KeyVaultSecretResolver(options);
        var first = await resolver.GetSecretAsync("custom-secret", CancellationToken.None);
        options.Value.Security.SeedSecrets["custom-secret"] = "updated";
        var cached = await resolver.GetSecretAsync("custom-secret", CancellationToken.None);

        Assert.Equal(first, cached);

        resolver.Invalidate("custom-secret");
        var refreshed = await resolver.GetSecretAsync("custom-secret", CancellationToken.None);
        Assert.Equal("updated", refreshed);
    }

    [Fact]
    public async Task ThrowsWhenSecretMissing()
    {
        var resolver = new KeyVaultSecretResolver();

        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.GetSecretAsync("missing", CancellationToken.None));
    }
}
