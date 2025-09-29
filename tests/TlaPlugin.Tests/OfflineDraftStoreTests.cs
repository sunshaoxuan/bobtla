using System;
using System.IO;
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

        Assert.True(record.Id > 0);
        var drafts = store.ListDrafts("alice");
        Assert.Single(drafts);
        Assert.Equal("test", drafts[0].OriginalText);

        store.Cleanup();
    }
}
