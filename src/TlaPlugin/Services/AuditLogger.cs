using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TlaPlugin.Services;

/// <summary>
/// 監査ログを保持し合規証跡を提供する。
/// </summary>
public class AuditLogger
{
    private readonly IList<JsonObject> _logs = new List<JsonObject>();

    public void Record(string tenantId, string userId, string modelId, string text, string translation, decimal cost, int latencyMs)
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
