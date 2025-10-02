using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
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

    [Fact]
    public async Task UsesTenantOverrideForAudienceAndSecret()
    {
        var options = Options.Create(new PluginOptions
        {
            Security = new SecurityOptions
            {
                SeedSecrets = new Dictionary<string, string>
                {
                    ["tla-client-secret"] = "default-secret",
                    ["enterprise-graph-secret"] = "enterprise-secret"
                },
                TenantOverrides = new Dictionary<string, TenantSecurityOverride>
                {
                    ["enterprise.onmicrosoft.com"] = new TenantSecurityOverride
                    {
                        ClientSecretName = "enterprise-graph-secret",
                        UserAssertionAudience = "api://enterprise-graph"
                    }
                }
            }
        });

        var resolver = new KeyVaultSecretResolver(options);
        var broker = new TokenBroker(resolver, options);

        var defaultToken = await broker.ExchangeOnBehalfOfAsync("contoso.onmicrosoft.com", "user", CancellationToken.None);
        var enterpriseToken = await broker.ExchangeOnBehalfOfAsync("enterprise.onmicrosoft.com", "user", CancellationToken.None);

        Assert.NotEqual(defaultToken.Value, enterpriseToken.Value);
        Assert.Equal("api://enterprise-graph", enterpriseToken.Audience);
        Assert.Equal(options.Value.Security.UserAssertionAudience, defaultToken.Audience);
    }

    [Fact]
    public async Task UsesMsalWhenEnabled()
    {
        var options = Options.Create(new PluginOptions
        {
            Security = new SecurityOptions
            {
                ClientId = "obo-client",
                ClientSecretName = "obo-secret",
                UseHmacFallback = false,
                GraphScopes = new List<string> { "https://graph.microsoft.com/.default" },
                SeedSecrets = new Dictionary<string, string>
                {
                    ["obo-secret"] = "obo-secret-value"
                }
            }
        });

        var resolver = new KeyVaultSecretResolver(options);
        var fakeClient = new StubOnBehalfOfTokenClient();
        var expiry = DateTimeOffset.UtcNow.AddMinutes(20);
        fakeClient.Results.Enqueue(new OnBehalfOfTokenResult("obo-token", expiry));
        var broker = new TokenBroker(resolver, options, fakeClient);

        var token = await broker.ExchangeOnBehalfOfAsync("contoso", "user-assertion", CancellationToken.None);

        Assert.Equal("obo-token", token.Value);
        Assert.Equal(expiry, token.ExpiresOn);
        Assert.Equal(options.Value.Security.UserAssertionAudience, token.Audience);

        var call = Assert.Single(fakeClient.Calls);
        Assert.Equal("contoso", call.TenantId);
        Assert.Equal("obo-client", call.ClientId);
        Assert.Equal("obo-secret-value", call.ClientSecret);
        Assert.Equal("user-assertion", call.UserAssertion);
        Assert.Equal(options.Value.Security.GraphScopes, call.Scopes);
    }

    [Fact]
    public async Task UsesTenantOverrideClientIdForMsal()
    {
        var options = Options.Create(new PluginOptions
        {
            Security = new SecurityOptions
            {
                ClientId = "default-client",
                ClientSecretName = "default-secret",
                UseHmacFallback = false,
                GraphScopes = new List<string> { "https://graph.microsoft.com/.default" },
                SeedSecrets = new Dictionary<string, string>
                {
                    ["default-secret"] = "default-secret-value",
                    ["enterprise-secret"] = "enterprise-secret-value"
                },
                TenantOverrides = new Dictionary<string, TenantSecurityOverride>
                {
                    ["enterprise.onmicrosoft.com"] = new TenantSecurityOverride
                    {
                        ClientId = "enterprise-client",
                        ClientSecretName = "enterprise-secret",
                        UserAssertionAudience = "api://enterprise-graph"
                    }
                }
            }
        });

        var resolver = new KeyVaultSecretResolver(options);
        var fakeClient = new StubOnBehalfOfTokenClient();
        fakeClient.Results.Enqueue(new OnBehalfOfTokenResult("enterprise-token", DateTimeOffset.UtcNow.AddMinutes(10)));
        var broker = new TokenBroker(resolver, options, fakeClient);

        var token = await broker.ExchangeOnBehalfOfAsync("enterprise.onmicrosoft.com", "enterprise-assertion", CancellationToken.None);

        Assert.Equal("api://enterprise-graph", token.Audience);
        var call = Assert.Single(fakeClient.Calls);
        Assert.Equal("enterprise-client", call.ClientId);
        Assert.Equal("enterprise-secret-value", call.ClientSecret);
        Assert.Equal("enterprise-assertion", call.UserAssertion);
    }

    [Fact]
    public async Task WrapsMsalExceptionsAsAuthenticationFailures()
    {
        var options = Options.Create(new PluginOptions
        {
            Security = new SecurityOptions
            {
                ClientId = "obo-client",
                ClientSecretName = "obo-secret",
                UseHmacFallback = false,
                GraphScopes = new List<string> { "https://graph.microsoft.com/.default" },
                SeedSecrets = new Dictionary<string, string>
                {
                    ["obo-secret"] = "obo-secret-value"
                }
            }
        });

        var resolver = new KeyVaultSecretResolver(options);
        var fakeClient = new StubOnBehalfOfTokenClient
        {
            Exception = new MsalServiceException("invalid_client", "invalid client")
        };
        var broker = new TokenBroker(resolver, options, fakeClient);

        await Assert.ThrowsAsync<AuthenticationException>(() =>
            broker.ExchangeOnBehalfOfAsync("contoso", "user-assertion", CancellationToken.None));
    }

    private sealed class StubOnBehalfOfTokenClient : IOnBehalfOfTokenClient
    {
        public Queue<OnBehalfOfTokenResult> Results { get; } = new();
        public List<Invocation> Calls { get; } = new();
        public Exception? Exception { get; set; }

        public Task<OnBehalfOfTokenResult> AcquireTokenAsync(string tenantId, string clientId, string clientSecret, string userAssertion, IEnumerable<string> scopes, CancellationToken cancellationToken)
        {
            Calls.Add(new Invocation(tenantId, clientId, clientSecret, userAssertion, scopes.ToArray()));

            if (Exception is not null)
            {
                throw Exception;
            }

            if (Results.Count == 0)
            {
                throw new InvalidOperationException("No OBO tokens configured for test.");
            }

            return Task.FromResult(Results.Dequeue());
        }

        public sealed record Invocation(string TenantId, string ClientId, string ClientSecret, string UserAssertion, IReadOnlyList<string> Scopes);
    }
}
