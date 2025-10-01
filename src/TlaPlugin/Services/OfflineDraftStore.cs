using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 基于 SQLite 的草稿存储。
/// </summary>
public class OfflineDraftStore
{
    private const string SelectColumns = "Id, UserId, TenantId, OriginalText, TargetLanguage, CreatedAt, Status, ResultText, ErrorReason, Attempts, CompletedAt";
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
            @"INSERT INTO Drafts(UserId, TenantId, OriginalText, TargetLanguage, CreatedAt, Status, ResultText, ErrorReason, Attempts, CompletedAt)
              VALUES ($userId, $tenantId, $text, $target, $createdAt, $status, NULL, NULL, 0, NULL);
              SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$userId", request.UserId);
        command.Parameters.AddWithValue("$tenantId", request.TenantId);
        command.Parameters.AddWithValue("$text", request.OriginalText);
        command.Parameters.AddWithValue("$target", request.TargetLanguage);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$status", OfflineDraftStatus.Pending);

        var id = (long)(command.ExecuteScalar() ?? 0);
        return new OfflineDraftRecord
        {
            Id = id,
            UserId = request.UserId,
            TenantId = request.TenantId,
            OriginalText = request.OriginalText,
            TargetLanguage = request.TargetLanguage,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OfflineDraftStatus.Pending,
            Attempts = 0
        };
    }

    public OfflineDraftRecord MarkProcessing(long id)
    {
        using var connection = new SqliteConnection(_options.OfflineDraftConnectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "UPDATE Drafts SET Status = $status, ErrorReason = NULL WHERE Id = $id";
            command.Parameters.AddWithValue("$status", OfflineDraftStatus.Processing);
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        var record = GetDraftById(connection, id, transaction);
        transaction.Commit();
        return record;
    }

    public int BeginProcessing(long id)
    {
        using var connection = new SqliteConnection(_options.OfflineDraftConnectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = @"UPDATE Drafts
SET Status = $status,
    Attempts = Attempts + 1,
    ErrorReason = NULL,
    ResultText = NULL,
    CompletedAt = NULL
WHERE Id = $id";
            command.Parameters.AddWithValue("$status", OfflineDraftStatus.Processing);
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        int attempts;
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT Attempts FROM Drafts WHERE Id = $id";
            select.Parameters.AddWithValue("$id", id);
            attempts = Convert.ToInt32(select.ExecuteScalar());
        }

        transaction.Commit();
        return attempts;
    }

    public void MarkCompleted(long id, string translatedText)
    {
        using var connection = new SqliteConnection(_options.OfflineDraftConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"UPDATE Drafts
SET Status = $status,
    ResultText = $result,
    ErrorReason = NULL,
    CompletedAt = $completedAt
WHERE Id = $id";
        command.Parameters.AddWithValue("$status", OfflineDraftStatus.Completed);
        command.Parameters.AddWithValue("$result", translatedText);
        command.Parameters.AddWithValue("$completedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public void MarkFailed(long id, string errorReason, bool finalFailure)
    {
        using var connection = new SqliteConnection(_options.OfflineDraftConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        if (finalFailure)
        {
            command.CommandText = @"UPDATE Drafts
SET Status = $failedStatus,
    ErrorReason = $reason,
    ResultText = NULL,
    CompletedAt = $completedAt
WHERE Id = $id";
            command.Parameters.AddWithValue("$failedStatus", OfflineDraftStatus.Failed);
            command.Parameters.AddWithValue("$completedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
        else
        {
            command.CommandText = @"UPDATE Drafts
SET Status = $pendingStatus,
    ErrorReason = $reason,
    ResultText = NULL,
    CompletedAt = NULL
WHERE Id = $id";
            command.Parameters.AddWithValue("$pendingStatus", OfflineDraftStatus.Pending);
        }

        command.Parameters.AddWithValue("$reason", errorReason);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<OfflineDraftRecord> ListDrafts(string userId)
    {
        var results = new List<OfflineDraftRecord>();
        using var connection = new SqliteConnection(_options.OfflineDraftConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM Drafts WHERE UserId = $user";
        command.Parameters.AddWithValue("$user", userId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadRecord(reader));
        }
        return results;
    }

    public IReadOnlyList<OfflineDraftRecord> GetPendingDrafts(int maxCount)
    {
        var results = new List<OfflineDraftRecord>();
        using var connection = new SqliteConnection(_options.OfflineDraftConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM Drafts WHERE Status IN ($pending, $processing) AND CompletedAt IS NULL ORDER BY CreatedAt LIMIT $limit";
        command.Parameters.AddWithValue("$pending", OfflineDraftStatus.Pending);
        command.Parameters.AddWithValue("$processing", OfflineDraftStatus.Processing);
        command.Parameters.AddWithValue("$limit", maxCount);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadRecord(reader));
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
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                @"CREATE TABLE IF NOT EXISTS Drafts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                TenantId TEXT NOT NULL,
                OriginalText TEXT NOT NULL,
                TargetLanguage TEXT NOT NULL,
                CreatedAt INTEGER NOT NULL,
                Status TEXT NOT NULL,
                ResultText TEXT,
                ErrorReason TEXT,
                Attempts INTEGER NOT NULL DEFAULT 0,
                CompletedAt INTEGER
            );";
            command.ExecuteNonQuery();
        }

        EnsureColumn(connection, "ResultText", "TEXT");
        EnsureColumn(connection, "ErrorReason", "TEXT");
        EnsureColumn(connection, "Attempts", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "CompletedAt", "INTEGER");
    }

    private static void EnsureColumn(SqliteConnection connection, string columnName, string definition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT 1 FROM pragma_table_info('Drafts') WHERE name = $name";
        check.Parameters.AddWithValue("$name", columnName);
        var exists = check.ExecuteScalar();
        if (exists is null)
        {
            using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE Drafts ADD COLUMN {columnName} {definition}";
            alter.ExecuteNonQuery();
        }
    }

    private static OfflineDraftRecord GetDraftById(SqliteConnection connection, long id, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {SelectColumns} FROM Drafts WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException($"Draft {id} does not exist.");
        }

        return ReadRecord(reader);
    }

    private static OfflineDraftRecord ReadRecord(SqliteDataReader reader)
    {
        return new OfflineDraftRecord
        {
            Id = reader.GetInt64(0),
            UserId = reader.GetString(1),
            TenantId = reader.GetString(2),
            OriginalText = reader.GetString(3),
            TargetLanguage = reader.GetString(4),
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(5)),
            Status = reader.GetString(6),
            ResultText = reader.IsDBNull(7) ? null : reader.GetString(7),
            ErrorReason = reader.IsDBNull(8) ? null : reader.GetString(8),
            Attempts = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
            CompletedAt = reader.IsDBNull(10) ? null : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(10))
        };
    }
}
