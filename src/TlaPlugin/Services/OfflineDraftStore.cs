using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
<<<<<<< HEAD
/// 基于 SQLite 的草稿存储。
=======
/// SQLite を用いた草稿保管庫。
>>>>>>> origin/main
/// </summary>
public class OfflineDraftStore
{
    private readonly PluginOptions _options;

    public OfflineDraftStore(IOptions<PluginOptions>? options = null)
    {
        _options = options?.Value ?? new PluginOptions();
        EnsureSchema();
    }

    public OfflineDraftRecord SaveDraft(OfflineDraftRequest request)
    {
        using var connection = new SqliteConnection(_options.OfflineDraftConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            @"INSERT INTO Drafts(UserId, TenantId, OriginalText, TargetLanguage, CreatedAt, Status)
              VALUES ($userId, $tenantId, $text, $target, $createdAt, 'PENDING');
              SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$userId", request.UserId);
        command.Parameters.AddWithValue("$tenantId", request.TenantId);
        command.Parameters.AddWithValue("$text", request.OriginalText);
        command.Parameters.AddWithValue("$target", request.TargetLanguage);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        var id = (long)(command.ExecuteScalar() ?? 0);
        return new OfflineDraftRecord
        {
            Id = id,
            UserId = request.UserId,
            TenantId = request.TenantId,
            OriginalText = request.OriginalText,
            TargetLanguage = request.TargetLanguage,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = "PENDING"
        };
    }

    public IReadOnlyList<OfflineDraftRecord> ListDrafts(string userId)
    {
        var results = new List<OfflineDraftRecord>();
        using var connection = new SqliteConnection(_options.OfflineDraftConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, UserId, TenantId, OriginalText, TargetLanguage, CreatedAt, Status FROM Drafts WHERE UserId = $user";
        command.Parameters.AddWithValue("$user", userId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new OfflineDraftRecord
            {
                Id = reader.GetInt64(0),
                UserId = reader.GetString(1),
                TenantId = reader.GetString(2),
                OriginalText = reader.GetString(3),
                TargetLanguage = reader.GetString(4),
                CreatedAt = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(5)),
                Status = reader.GetString(6)
            });
        }
        return results;
    }

    public void Cleanup()
    {
        using var connection = new SqliteConnection(_options.OfflineDraftConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Drafts WHERE CreatedAt < $cutoff";
        var cutoff = DateTimeOffset.UtcNow.Subtract(_options.DraftRetention).ToUnixTimeSeconds();
        command.Parameters.AddWithValue("$cutoff", cutoff);
        command.ExecuteNonQuery();
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_options.OfflineDraftConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            @"CREATE TABLE IF NOT EXISTS Drafts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                TenantId TEXT NOT NULL,
                OriginalText TEXT NOT NULL,
                TargetLanguage TEXT NOT NULL,
                CreatedAt INTEGER NOT NULL,
                Status TEXT NOT NULL
            );";
        command.ExecuteNonQuery();
    }
}
