using System;
using System.Collections.Generic;
using TlaPlugin.Models;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class GlossaryServiceTests
{
    [Fact]
    public void Preview_DetectsConflict_AndHonorsAlternativeSelection()
    {
        var service = new GlossaryService();
        service.LoadEntries(new[]
        {
            new GlossaryEntry("GPU", "图形处理器", "tenant:contoso"),
            new GlossaryEntry("GPU", "显卡", "channel:finance")
        });

        var unresolved = service.Preview("GPU 加速", "contoso", "finance", "user");

        Assert.True(unresolved.HasConflicts);
        Assert.True(unresolved.RequiresResolution);
        Assert.Equal("GPU 加速", unresolved.Text);
        var conflict = Assert.Single(unresolved.Matches);
        Assert.True(conflict.HasConflict);
        Assert.Equal(GlossaryDecisionKind.Unspecified, conflict.Resolution);
        Assert.Equal(2, conflict.Candidates.Count);
        Assert.False(conflict.Replaced);
        Assert.Equal(0, conflict.Candidates[0].Priority);
        Assert.Equal(1, conflict.Candidates[1].Priority);

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

    [Fact]
    public void Apply_OrdersCandidatesByScopePriority()
    {
        var service = new GlossaryService();
        service.LoadEntries(new[]
        {
            new GlossaryEntry("CPU", "中央处理器", "tenant:contoso"),
            new GlossaryEntry("CPU", "处理器", "channel:finance"),
            new GlossaryEntry("CPU", "自定义翻译", "user:owner")
        });

        var result = service.Preview("CPU upgrade", "contoso", "finance", "owner");

        Assert.True(result.HasConflicts);
        Assert.True(result.RequiresResolution);
        Assert.Equal("CPU upgrade", result.Text);
        var match = Assert.Single(result.Matches);
        Assert.Equal("CPU", match.Source);
        Assert.True(match.HasConflict);
        Assert.Equal(3, match.Candidates.Count);
        Assert.Equal("user:owner", match.Candidates[0].Scope);
        Assert.Equal(0, match.Candidates[0].Priority);
        Assert.Equal("channel:finance", match.Candidates[1].Scope);
        Assert.Equal(1, match.Candidates[1].Priority);
        Assert.Equal("tenant:contoso", match.Candidates[2].Scope);
        Assert.Equal(2, match.Candidates[2].Priority);
        Assert.Equal(GlossaryDecisionKind.Unspecified, match.Resolution);
        Assert.False(match.Replaced);
        Assert.Equal("CPU upgrade", result.Text);
    }
}
