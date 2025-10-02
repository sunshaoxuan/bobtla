using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace TlaPlugin.Services;

/// <summary>
/// Represents the result of an on-behalf-of token acquisition.
/// </summary>
/// <param name="AccessToken">The bearer token issued by AAD.</param>
/// <param name="ExpiresOn">The moment when the token expires.</param>
public sealed record OnBehalfOfTokenResult(string AccessToken, DateTimeOffset ExpiresOn);

/// <summary>
/// Abstraction over MSAL's confidential client flow to simplify testing.
/// </summary>
public interface IOnBehalfOfTokenClient
{
    Task<OnBehalfOfTokenResult> AcquireTokenAsync(
        string tenantId,
        string clientId,
        string clientSecret,
        string userAssertion,
        IEnumerable<string> scopes,
        CancellationToken cancellationToken);
}

/// <summary>
/// Default MSAL-based implementation that caches confidential client applications per tenant/client pair.
/// </summary>
public sealed class MsalOnBehalfOfTokenClient : IOnBehalfOfTokenClient
{
    private readonly ConcurrentDictionary<string, IConfidentialClientApplication> _applications =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<OnBehalfOfTokenResult> AcquireTokenAsync(
        string tenantId,
        string clientId,
        string clientSecret,
        string userAssertion,
        IEnumerable<string> scopes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Tenant ID must be provided.", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("Client ID must be provided.", nameof(clientId));
        }

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new ArgumentException("Client secret must be provided.", nameof(clientSecret));
        }

        if (string.IsNullOrWhiteSpace(userAssertion))
        {
            throw new ArgumentException("User assertion must be provided.", nameof(userAssertion));
        }

        var authority = $"https://login.microsoftonline.com/{tenantId}";
        var cacheKey = $"{authority}|{clientId}";
        var application = _applications.GetOrAdd(cacheKey, _ => ConfidentialClientApplicationBuilder
            .Create(clientId)
            .WithClientSecret(clientSecret)
            .WithAuthority(authority)
            .Build());

        var assertion = new UserAssertion(userAssertion);
        var result = await application
            .AcquireTokenOnBehalfOf(scopes, assertion)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        return new OnBehalfOfTokenResult(result.AccessToken, result.ExpiresOn);
    }
}
