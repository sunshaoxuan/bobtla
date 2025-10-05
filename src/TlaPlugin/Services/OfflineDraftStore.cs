using System;
using System.Collections.Generic;
using System.Linq;
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
    private const string SelectColumns = "Id, UserId, TenantId, OriginalText, TargetLanguage, CreatedAt, Status, ResultText, ErrorReason, Attempts, CompletedAt, JobId, SegmentIndex, SegmentCount, AggregatedResult";
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
            @"INSERT INTO Drafts(UserId, TenantId, OriginalText, TargetLanguage, CreatedAt, Status, ResultText, ErrorReason, Attempts, CompletedAt, JobId, SegmentIndex, SegmentCount, AggregatedResult)
              VALUES ($userId, $tenantId, $text, $target, $createdAt, $status, NULL, NULL, 0, NULL, $jobId, $segmentIndex, $segmentCount, NULL);
              SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$userId", request.UserId);
        command.Parameters.AddWithValue("$tenantId", request.TenantId);
        command.Parameters.AddWithValue("$text", request.OriginalText);
        command.Parameters.AddWithValue("$target", request.TargetLanguage);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$status", OfflineDraftStatus.Pending);
        command.Parameters.AddWithValue("$jobId", (object?)request.JobId ?? DBNull.Value);
        command.Parameters.AddWithValue("$segmentIndex", request.SegmentIndex);
        command.Parameters.AddWithValue("$segmentCount", request.SegmentCount);

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
            Attempts = 0,
            JobId = request.JobId,
            SegmentIndex = request.SegmentIndex,
            SegmentCount = request.SegmentCount
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
    CompletedAt = NULL,
    AggregatedResult = NULL
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
    CompletedAt = $completedAt,
    AggregatedResult = NULL
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
    CompletedAt = NULL,
    AggregatedResult = NULL
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

    public IReadOnlyList<OfflineDraftRecord> GetDraftsByJob(string jobId)
    {
        var results = new List<OfflineDraftRecord>();
        using var connection = new SqliteConnection(_options.OfflineDraftConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM Drafts WHERE JobId = $jobId ORDER BY SegmentIndex";
        command.Parameters.AddWithValue("$jobId", jobId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadRecord(reader));
        }

        return results;
    }

    public string? TryFinalizeJob(string jobId)
    {
        using var connection = new SqliteConnection(_options.OfflineDraftConnectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT AggregatedResult FROM Drafts WHERE JobId = $jobId AND AggregatedResult IS NOT NULL LIMIT 1";
            check.Parameters.AddWithValue("$jobId", jobId);
            var existing = check.ExecuteScalar();
            if (existing is string alreadyMerged && !string.IsNullOrEmpty(alreadyMerged))
            {
                transaction.Commit();
                return alreadyMerged;
            }
        }

        var segments = new List<(long Id, int Index, string Status, string? Result)>();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT Id, SegmentIndex, Status, ResultText FROM Drafts WHERE JobId = $jobId ORDER BY SegmentIndex";
            command.Parameters.AddWithValue("$jobId", jobId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                segments.Add((reader.GetInt64(0), reader.GetInt32(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
        }

        if (segments.Count == 0)
        {
            transaction.Commit();
            return null;
        }

        if (segments.Exists(segment => !string.Equals(segment.Status, OfflineDraftStatus.Completed, StringComparison.OrdinalIgnoreCase)))
        {
            transaction.Commit();
            return null;
        }

        if (segments.Exists(segment => segment.Result is null))
        {
            transaction.Commit();
            return null;
        }

        var merged = string.Concat(segments.OrderBy(segment => segment.Index).Select(segment => segment.Result));

        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE Drafts SET AggregatedResult = $merged, ResultText = $merged WHERE JobId = $jobId";
            update.Parameters.AddWithValue("$merged", merged);
            update.Parameters.AddWithValue("$jobId", jobId);
            update.ExecuteNonQuery();
        }

        transaction.Commit();
        return merged;
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
                CompletedAt INTEGER,
                JobId TEXT,
                SegmentIndex INTEGER NOT NULL DEFAULT 0,
                SegmentCount INTEGER NOT NULL DEFAULT 1,
                AggregatedResult TEXT
            );";
            command.ExecuteNonQuery();
        }

        EnsureColumn(connection, "ResultText", "TEXT");
        EnsureColumn(connection, "ErrorReason", "TEXT");
        EnsureColumn(connection, "Attempts", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "CompletedAt", "INTEGER");
        EnsureColumn(connection, "JobId", "TEXT");
        EnsureColumn(connection, "SegmentIndex", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "SegmentCount", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(connection, "AggregatedResult", "TEXT");
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
            CompletedAt = reader.IsDBNull(10) ? null : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(10)),
            JobId = reader.IsDBNull(11) ? null : reader.GetString(11),
            SegmentIndex = reader.IsDBNull(12) ? 0 : reader.GetInt32(12),
            SegmentCount = reader.IsDBNull(13) ? 1 : reader.GetInt32(13),
            AggregatedResult = reader.IsDBNull(14) ? null : reader.GetString(14)
        };
    }
}
