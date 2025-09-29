using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;

namespace TlaPlugin.Services;

/// <summary>
/// 地域、認証、PII 検知を評価するゲートウェイ。
/// </summary>
public class ComplianceGateway
{
    private readonly CompliancePolicyOptions _policy;
    private readonly IDictionary<string, Regex> _piiPatterns;

    public ComplianceGateway(IOptions<PluginOptions>? options = null)
    {
        _policy = options?.Value.Compliance ?? new CompliancePolicyOptions();
        _piiPatterns = _policy.PiiPatterns.ToDictionary(kvp => kvp.Key, kvp => new Regex(kvp.Value, RegexOptions.Compiled));
    }

    public ComplianceReport Evaluate(string text, ModelProviderOptions provider)
    {
        var violations = new List<string>();

        if (!IsRegionAllowed(provider))
        {
            violations.Add($"提供リージョン {string.Join(',', provider.Regions)} は許可されていません。");
        }

        if (!HasRequiredCertifications(provider))
        {
            violations.Add("必要な認証を満たしていません。");
        }

        var pii = DetectPii(text).ToList();
        if (pii.Any())
        {
            violations.Add($"PII 検出: {string.Join(',', pii.Select(p => p.Type))}");
        }

        if (_policy.BannedPhrases.Any(b => text.Contains(b, StringComparison.OrdinalIgnoreCase)))
        {
            violations.Add("禁則語が含まれています。");
        }

        return new ComplianceReport(violations.Count == 0, violations);
    }

    private bool IsRegionAllowed(ModelProviderOptions provider)
    {
        if (_policy.RequiredRegionTags.Count == 0)
        {
            return true;
        }
        if (provider.Regions.Any(r => _policy.RequiredRegionTags.Contains(r)))
        {
            return true;
        }
        return provider.Regions.Any(r => _policy.AllowedRegionFallbacks.Contains(r));
    }

    private bool HasRequiredCertifications(ModelProviderOptions provider)
    {
        if (_policy.RequiredCertifications.Count == 0)
        {
            return true;
        }
        return _policy.RequiredCertifications.All(c => provider.Certifications.Contains(c));
    }

    private IEnumerable<PiiFinding> DetectPii(string text)
    {
        foreach (var pair in _piiPatterns)
        {
            if (pair.Value.IsMatch(text))
            {
                yield return new PiiFinding(pair.Key);
            }
        }
    }
}

public record ComplianceReport(bool Allowed, IList<string> Violations);
public record PiiFinding(string Type);
