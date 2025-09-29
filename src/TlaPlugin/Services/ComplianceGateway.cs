using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;

namespace TlaPlugin.Services;

/// <summary>
/// 负责评估地域、认证与 PII 检测的合规网关。
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
            violations.Add($"模型提供区域 {string.Join(',', provider.Regions)} 不在允许列表中。");
        }

        if (!HasRequiredCertifications(provider))
        {
            violations.Add("未满足所需的合规认证。");
        }

        var pii = DetectPii(text).ToList();
        if (pii.Any())
        {
            violations.Add($"检测到敏感信息: {string.Join(',', pii.Select(p => p.Type))}");
        }

        if (_policy.BannedPhrases.Any(b => text.Contains(b, StringComparison.OrdinalIgnoreCase)))
        {
            violations.Add("文本中包含禁止词。");
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
