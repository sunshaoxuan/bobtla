using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TlaPlugin.Services;

/// <summary>
/// 持久化审计日志以提供合规追溯。
/// </summary>
public class AuditLogger
{
    private readonly IList<JsonObject> _logs = new List<JsonObject>();

    public void Record(string tenantId, string userId, string modelId, string text, string translation, decimal cost, int latencyMs, string? audience = null, IReadOnlyDictionary<string, string>? additionalTranslations = null)
    {
        var hashed = HashText(text);
        var entry = new JsonObject
        {
            ["tenantId"] = tenantId,
            ["userId"] = userId,
            ["modelId"] = modelId,
            ["originalHash"] = hashed,
            ["translation"] = translation,
            ["costUsd"] = cost,
            ["latencyMs"] = latencyMs,
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
        };
        if (!string.IsNullOrEmpty(audience))
        {
            entry["audience"] = audience;
        }
        if (additionalTranslations is { Count: > 0 })
        {
            var extras = new JsonObject();
            foreach (var kvp in additionalTranslations)
            {
                extras[kvp.Key] = kvp.Value;
            }
            entry["additionalTranslations"] = extras;
        }
        _logs.Add(entry);
    }

    private static string HashText(string text)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

    public IReadOnlyList<JsonObject> Export() => _logs.ToList();
}
