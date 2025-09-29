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
    public async Task GeneratesDifferentTokensForDifferentUsers()
    {
        var options = Options.Create(new PluginOptions());
        var resolver = new KeyVaultSecretResolver(options);
        var broker = new TokenBroker(resolver, options);

        var userToken = await broker.ExchangeOnBehalfOfAsync("contoso", "user1", CancellationToken.None);
        var adminToken = await broker.ExchangeOnBehalfOfAsync("contoso", "user2", CancellationToken.None);

        // 验证不同用户得到不同的token
        Assert.NotEqual(userToken.Value, adminToken.Value);
        Assert.Equal(userToken.Audience, adminToken.Audience);
    }
}
