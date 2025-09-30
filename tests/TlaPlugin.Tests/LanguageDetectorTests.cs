using System;
using TlaPlugin.Services;
using Xunit;

namespace TlaPlugin.Tests;

public class LanguageDetectorTests
{
    [Fact]
    public void Detect_PlainAsciiSentenceMaintainsConfidence()
    {
        var detector = new LanguageDetector();

        var result = detector.Detect("This is a simple test message written with plain ASCII letters only.");

        Assert.Equal("en", result.Language);
        Assert.True(result.Confidence >= 0.75);
    }

    [Fact]
    public void Detect_KeepsConfidenceForTextWithDistinctFeatures()
    {
        var detector = new LanguageDetector();

        var result = detector.Detect("¿Dónde está la biblioteca? Necesito información rápida.");

        Assert.Equal("es", result.Language);
        Assert.True(result.Confidence >= 0.75);
    }

    [Fact]
    public void Detect_ReturnsJapaneseCandidateForKanjiOnlyText()
    {
        var detector = new LanguageDetector();

        var result = detector.Detect("東京都庁");

        Assert.True(result.Confidence < 0.75);
        Assert.Contains(result.Candidates, candidate => string.Equals(candidate.Language, "ja", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Candidates, candidate => string.Equals(candidate.Language, "zh", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Detect_KanjiOnlyTextWithSharedPunctuationRemainsUncertain()
    {
        var detector = new LanguageDetector();

        var result = detector.Detect("東京都大阪府。");

        Assert.True(result.Confidence < 0.75);
        Assert.Contains(result.Candidates, candidate => string.Equals(candidate.Language, "ja", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Candidates, candidate => string.Equals(candidate.Language, "zh", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Detect_PureHanTextWithSharedPunctuationKeepsBothCandidates()
    {
        var detector = new LanguageDetector();

        var result = detector.Detect("漢字表記のみ、句読点。");

        Assert.True(result.Confidence < 0.75);
        Assert.Contains(result.Candidates, candidate => string.Equals(candidate.Language, "ja", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Candidates, candidate => string.Equals(candidate.Language, "zh", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Detect_DiacriticFreeForeignSentenceRemainsUncertain()
    {
        var detector = new LanguageDetector();

        var result = detector.Detect("Besok pagi kami akan berangkat ke pasar untuk membeli sayur segar dan buah segar.");

        Assert.True(result.Confidence < 0.75);
        Assert.NotEmpty(result.Candidates);
    }
}
