using System.Collections.Generic;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class ComplianceGatewayTests
{
    [Fact]
    public void BlocksBannedPhrases()
    {
        var options = Options.Create(new PluginOptions
        {
            Compliance = new CompliancePolicyOptions
            {
                BannedPhrases = new List<string> { "forbidden" }
            }
        });

        var gateway = new ComplianceGateway(options);
        var report = gateway.Evaluate("this contains forbidden term", new ModelProviderOptions { Regions = new List<string> { "global" } });

        Assert.False(report.Allowed);
        Assert.Contains(report.Violations, v => v.Contains("禁則語"));
    }

    [Fact]
    public void AllowsCertifiedRegion()
    {
        var options = Options.Create(new PluginOptions
        {
            Compliance = new CompliancePolicyOptions
            {
                RequiredRegionTags = new List<string> { "japan" },
                RequiredCertifications = new List<string> { "iso" }
            }
        });

        var gateway = new ComplianceGateway(options);
        var report = gateway.Evaluate("hello", new ModelProviderOptions
        {
            Regions = new List<string> { "japan" },
            Certifications = new List<string> { "iso" }
        });

        Assert.True(report.Allowed);
    }
}
