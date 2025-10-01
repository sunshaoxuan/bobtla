using System;
using System.Threading;

namespace TlaPlugin.Services;

/// <summary>
/// Maintains ambient Graph request context so authentication providers can access per-request tokens.
/// </summary>
public interface IGraphRequestContextAccessor
{
    GraphRequestContext? Current { get; }

    IDisposable Push(GraphRequestContext context);
}

/// <summary>
/// Default AsyncLocal-based implementation of <see cref="IGraphRequestContextAccessor"/>.
/// </summary>
public sealed class GraphRequestContextAccessor : IGraphRequestContextAccessor
{
    private readonly AsyncLocal<GraphRequestContext?> _current = new();

    public GraphRequestContext? Current => _current.Value;

    public IDisposable Push(GraphRequestContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var previous = _current.Value;
        _current.Value = context;
        return new Scope(this, previous);
    }

    private void Restore(GraphRequestContext? previous)
    {
        _current.Value = previous;
    }

    private sealed class Scope : IDisposable
    {
        private readonly GraphRequestContextAccessor _accessor;
        private readonly GraphRequestContext? _previous;
        private bool _disposed;

        public Scope(GraphRequestContextAccessor accessor, GraphRequestContext? previous)
        {
            _accessor = accessor;
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _accessor.Restore(_previous);
        }
    }
}

/// <summary>
/// Represents the context required to authorize Graph requests.
/// </summary>
public sealed class GraphRequestContext
{
    public GraphRequestContext(string tenantId, string? userId, AccessToken? accessToken)
    {
        TenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
        UserId = userId;
        AccessToken = accessToken;
    }

    public string TenantId { get; }

    public string? UserId { get; }

    public AccessToken? AccessToken { get; private set; }

    public void UpdateToken(AccessToken token)
    {
        AccessToken = token ?? throw new ArgumentNullException(nameof(token));
    }
}
