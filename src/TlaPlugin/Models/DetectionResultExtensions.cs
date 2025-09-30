using System;
using System.Collections.Generic;
using System.Linq;

namespace TlaPlugin.Models;

/// <summary>
/// 提供语言检测结果的合并与去重帮助方法。
/// </summary>
public static class DetectionResultExtensions
{
    public static DetectionResult Merge(DetectionResult primary, DetectionResult secondary)
    {
        if (primary is null)
        {
            throw new ArgumentNullException(nameof(primary));
        }

        if (secondary is null)
        {
            throw new ArgumentNullException(nameof(secondary));
        }

        var registry = new Dictionary<string, (string Label, double Score)>(StringComparer.OrdinalIgnoreCase);

        static void RegisterCandidate(Dictionary<string, (string Label, double Score)> store, string? language, double score)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                return;
            }

            var clamped = Math.Clamp(score, 0, 0.99);
            if (!store.TryGetValue(language, out var existing) || existing.Score < clamped)
            {
                store[language] = (language, clamped);
            }
        }

        void Register(DetectionResult result)
        {
            RegisterCandidate(registry, result.Language, result.Confidence);
            foreach (var candidate in result.Candidates)
            {
                RegisterCandidate(registry, candidate.Language, candidate.Confidence);
            }
        }

        Register(primary);
        Register(secondary);

        var ordered = registry.Values
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new DetectionCandidate(entry.Label, Math.Round(entry.Score, 2, MidpointRounding.AwayFromZero)))
            .Take(6)
            .ToList();

        var top = ordered.FirstOrDefault();
        var fallbackConfidence = Math.Round(Math.Clamp(primary.Confidence, 0, 0.99), 2, MidpointRounding.AwayFromZero);

        return new DetectionResult(
            top?.Language ?? primary.Language,
            top?.Confidence ?? fallbackConfidence,
            ordered);
    }
}
