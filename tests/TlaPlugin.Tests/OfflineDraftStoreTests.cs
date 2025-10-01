using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using TlaPlugin.Configuration;
using TlaPlugin.Models;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class OfflineDraftStoreTests
{
    [Fact]
    public void PersistsDraftsToSqlite()
    {
        var dbPath = Path.GetTempFileName();
        try
        {
            var options = Options.Create(new PluginOptions
            {
                OfflineDraftConnectionString = $"Data Source={dbPath}",
                DraftRetention = TimeSpan.FromDays(1)
            });

            var store = new OfflineDraftStore(options);
            var record = store.SaveDraft(new OfflineDraftRequest
            {
                OriginalText = "test",
                TargetLanguage = "ja",
                TenantId = "contoso",
                UserId = "alice"
            });

            Assert.Equal(OfflineDraftStatus.Pending, record.Status);
            Assert.Equal(0, record.Attempts);

            var processing = store.MarkProcessing(record.Id);
            Assert.Equal(OfflineDraftStatus.Processing, processing.Status);

            var attempts = store.BeginProcessing(record.Id);
            Assert.Equal(1, attempts);

            store.MarkCompleted(record.Id, "完成翻译");

            var drafts = store.ListDrafts("alice");
            var saved = Assert.Single(drafts);
            Assert.Equal("完成翻译", saved.ResultText);
            Assert.Equal(OfflineDraftStatus.Completed, saved.Status);
            Assert.NotNull(saved.CompletedAt);

            using var connection = new SqliteConnection(options.Value.OfflineDraftConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Drafts SET CreatedAt = $createdAt WHERE Id = $id";
            command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeSeconds());
            command.Parameters.AddWithValue("$id", saved.Id);
            command.ExecuteNonQuery();

            store.Cleanup();
            Assert.Empty(store.ListDrafts("alice"));
        }
        finally
        {
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void MarkFailedTransitionsBetweenStates()
    {
        var dbPath = Path.GetTempFileName();
        try
        {
            var options = Options.Create(new PluginOptions
            {
                OfflineDraftConnectionString = $"Data Source={dbPath}"
            });

            var store = new OfflineDraftStore(options);
            var record = store.SaveDraft(new OfflineDraftRequest
            {
                OriginalText = "需要重试",
                TargetLanguage = "zh",
                TenantId = "contoso",
                UserId = "user"
            });

            store.MarkProcessing(record.Id);
            store.BeginProcessing(record.Id);

            store.MarkFailed(record.Id, "temporary", finalFailure: false);
            var pending = Assert.Single(store.ListDrafts("user"));
            Assert.Equal(OfflineDraftStatus.Pending, pending.Status);
            Assert.Equal("temporary", pending.ErrorReason);
            Assert.Null(pending.CompletedAt);

            store.MarkFailed(record.Id, "permanent", finalFailure: true);
            var failed = Assert.Single(store.ListDrafts("user"));
            Assert.Equal(OfflineDraftStatus.Failed, failed.Status);
            Assert.Equal("permanent", failed.ErrorReason);
            Assert.NotNull(failed.CompletedAt);
        }
        finally
        {
            File.Delete(dbPath);
        }
    }
}
