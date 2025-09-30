using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class LanguageDetectorTests
{
    [Fact]
    public void Detect_DowngradesPlainAsciiConfidence()
    {
        var detector = new LanguageDetector();

        var result = detector.Detect("This is a simple test message written with plain ASCII letters only.");

        Assert.Equal("en", result.Language);
        Assert.True(result.Confidence < 0.75);
    }

    [Fact]
    public void Detect_KeepsConfidenceForTextWithDistinctFeatures()
    {
        var detector = new LanguageDetector();

        var result = detector.Detect("¿Dónde está la biblioteca? Necesito información rápida.");

        Assert.Equal("es", result.Language);
        Assert.True(result.Confidence >= 0.75);
    }
}
