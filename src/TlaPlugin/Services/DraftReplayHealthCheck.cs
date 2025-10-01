using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace TlaPlugin.Services;

/// <summary>
/// Health check that validates the draft replay infrastructure is operational.
/// </summary>
public class DraftReplayHealthCheck : IHealthCheck
{
    private readonly OfflineDraftStore _store;
    private readonly ILogger<DraftReplayHealthCheck> _logger;

    public DraftReplayHealthCheck(OfflineDraftStore store, ILogger<DraftReplayHealthCheck> logger)
    {
        _store = store;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pending = _store.GetPendingDrafts(10);
            if (pending.Count == 0)
            {
                return Task.FromResult(HealthCheckResult.Healthy("No pending drafts."));
            }

            return Task.FromResult(HealthCheckResult.Degraded(
                description: $"There are {pending.Count} drafts waiting to be replayed."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Draft replay health check failed.");
            return Task.FromResult(HealthCheckResult.Unhealthy("Unable to query offline drafts.", ex));
        }
    }
}
