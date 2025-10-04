using System;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class FileStageReadinessStoreTests
{
    [Fact]
    public void WriteAndRead_UsesConfiguredPath()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "tla-plugin-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDirectory);
        var filePath = Path.Combine(tempDirectory, "stage-ready.txt");
        try
        {
            var store = new FileStageReadinessStore(filePath);
            var timestamp = DateTimeOffset.UtcNow;

            store.WriteLastSuccess(timestamp);
            var storedValue = File.ReadAllText(filePath);
            var parsed = DateTimeOffset.Parse(storedValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

            Assert.Equal(timestamp.ToString("O", CultureInfo.InvariantCulture), storedValue.Trim());
            Assert.Equal(timestamp.ToUnixTimeSeconds(), parsed.ToUnixTimeSeconds());
            Assert.Equal(timestamp.ToUnixTimeSeconds(), store.ReadLastSuccess()?.ToUnixTimeSeconds());
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void WriteLastSuccess_WhenWriteFails_LogsWarningAndSwallows()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "tla-plugin-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var logger = new TestLogger<FileStageReadinessStore>();
            var store = new FileStageReadinessStore(tempDirectory, logger);

            var timestamp = DateTimeOffset.UtcNow;

            var exception = Record.Exception(() => store.WriteLastSuccess(timestamp));

            Assert.Null(exception);
            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Same(logger, entry.Logger);
            Assert.NotNull(entry.Exception);
            Assert.Contains(tempDirectory, entry.Message);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ReadLastSuccess_WhenReadFails_LogsErrorAndReturnsNull()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "tla-plugin-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDirectory);
        var filePath = Path.Combine(tempDirectory, "locked.txt");
        var timestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        File.WriteAllText(filePath, timestamp);
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            var logger = new TestLogger<FileStageReadinessStore>();
            var store = new FileStageReadinessStore(filePath, logger);

            var result = store.ReadLastSuccess();

            Assert.Null(result);
            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Error, entry.Level);
            Assert.Same(logger, entry.Logger);
            Assert.NotNull(entry.Exception);
            Assert.Contains(filePath, entry.Message);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

}
