using System;
using System.Collections.Generic;
using TlaPlugin.Models;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class GlossaryServiceTests
{
    [Fact]
    public void Apply_DetectsConflict_AndHonorsAlternativeSelection()
    {
        var service = new GlossaryService();
        service.LoadEntries(new[]
        {
            new GlossaryEntry("GPU", "图形处理器", "tenant:contoso"),
            new GlossaryEntry("GPU", "显卡", "channel:finance")
        });

        var unresolved = service.Apply("GPU 加速", "contoso", "finance", "user");

        Assert.True(unresolved.HasConflicts);
        Assert.True(unresolved.RequiresResolution);
        var conflict = Assert.Single(unresolved.Matches);
        Assert.True(conflict.HasConflict);
        Assert.Equal(GlossaryDecisionKind.Unspecified, conflict.Resolution);
        Assert.Equal(2, conflict.Candidates.Count);

        var decisions = new Dictionary<string, GlossaryDecision>(StringComparer.OrdinalIgnoreCase)
        {
            ["GPU"] = new GlossaryDecision
            {
                Kind = GlossaryDecisionKind.UseAlternative,
                Target = "显卡",
                Scope = "channel:finance"
            }
        };

        var resolved = service.Apply("GPU 加速", "contoso", "finance", "user", decisions);

        Assert.False(resolved.RequiresResolution);
        var resolvedMatch = Assert.Single(resolved.Matches);
        Assert.Equal(GlossaryDecisionKind.UseAlternative, resolvedMatch.Resolution);
        Assert.Equal("显卡", resolvedMatch.AppliedTarget);
        Assert.Contains("显卡", resolved.Text);
    }

    [Fact]
    public void Apply_KeepsOriginal_WhenDecisionIsKeepOriginal()
    {
        var service = new GlossaryService();
        service.LoadEntries(new[]
        {
            new GlossaryEntry("Compliance", "合规", "tenant:contoso"),
            new GlossaryEntry("Compliance", "遵从", "channel:legal")
        });

        var decisions = new Dictionary<string, GlossaryDecision>(StringComparer.OrdinalIgnoreCase)
        {
            ["Compliance"] = new GlossaryDecision
            {
                Kind = GlossaryDecisionKind.KeepOriginal
            }
        };

        var result = service.Apply("Compliance review", "contoso", "legal", "user", decisions);

        Assert.True(result.HasConflicts);
        var match = Assert.Single(result.Matches);
        Assert.False(match.Replaced);
        Assert.Null(match.AppliedTarget);
        Assert.Equal("Compliance review", result.Text);
        Assert.Equal(GlossaryDecisionKind.KeepOriginal, match.Resolution);
    }
}
