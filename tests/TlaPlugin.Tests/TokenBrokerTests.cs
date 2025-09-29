using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class TokenBrokerTests
{
    [Fact]
    public async Task CachesTokensUntilNearExpiry()
    {
        var options = Options.Create(new PluginOptions());
        var resolver = new KeyVaultSecretResolver(options);
        var broker = new TokenBroker(resolver, options);

        var first = await broker.ExchangeOnBehalfOfAsync("contoso", "user", CancellationToken.None);
        var second = await broker.ExchangeOnBehalfOfAsync("contoso", "user", CancellationToken.None);

        Assert.Equal(first.Value, second.Value);
    }

    [Fact]
    public async Task RefreshesExpiredTokens()
    {
        var options = Options.Create(new PluginOptions
        {
            Security = new SecurityOptions
            {
                AccessTokenLifetime = TimeSpan.FromMilliseconds(50)
            }
        });
        var resolver = new KeyVaultSecretResolver(options);
        var broker = new TokenBroker(resolver, options);

        var first = await broker.ExchangeOnBehalfOfAsync("contoso", "user", CancellationToken.None);
        await Task.Delay(100);
        var second = await broker.ExchangeOnBehalfOfAsync("contoso", "user", CancellationToken.None);

        Assert.NotEqual(first.Value, second.Value);
    }
}
