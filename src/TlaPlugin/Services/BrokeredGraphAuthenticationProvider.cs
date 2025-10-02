using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graph;

namespace TlaPlugin.Services;

/// <summary>
/// Authentication provider that sources access tokens from the <see cref="ITokenBroker"/> using the ambient Graph context.
/// </summary>
public sealed class BrokeredGraphAuthenticationProvider : IAuthenticationProvider
{
    private readonly ITokenBroker _tokenBroker;
    private readonly IGraphRequestContextAccessor _contextAccessor;

    public BrokeredGraphAuthenticationProvider(ITokenBroker tokenBroker, IGraphRequestContextAccessor contextAccessor)
    {
        _tokenBroker = tokenBroker ?? throw new ArgumentNullException(nameof(tokenBroker));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
    }

    public async Task AuthenticateRequestAsync(
        HttpRequestMessage request,
        IDictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var context = _contextAccessor.Current
            ?? throw new AuthenticationException("Graph request context is unavailable.");

        var token = context.AccessToken;
        if (token is null || token.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(-1))
        {
            if (string.IsNullOrWhiteSpace(context.UserId))
            {
                throw new AuthenticationException("User context is required to authorize Graph requests.");
            }

            token = await _tokenBroker
                .ExchangeOnBehalfOfAsync(context.TenantId, context.UserId, context.UserAssertion, cancellationToken)
                .ConfigureAwait(false);

            context.UpdateToken(token);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
    }
}
