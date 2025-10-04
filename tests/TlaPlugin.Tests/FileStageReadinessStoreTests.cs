using System;
using System.Globalization;
using System.IO;
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
}
