using System;
using TlaPlugin.Services;

namespace TlaPlugin.Tests;

public sealed class InMemoryStageReadinessStore : IStageReadinessStore
{
    private DateTimeOffset? _lastSuccess;

    public DateTimeOffset? LastSuccess => _lastSuccess;

    public DateTimeOffset? ReadLastSuccess() => _lastSuccess;

    public void WriteLastSuccess(DateTimeOffset timestamp)
    {
        _lastSuccess = timestamp;
    }

    public void Seed(DateTimeOffset? timestamp)
    {
        _lastSuccess = timestamp;
    }
}
