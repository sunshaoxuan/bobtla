using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
/// 面向翻译流程的多语言检测器，覆盖一百余种常见语言并在置信度不足时给出候选列表。
/// </summary>
public class LanguageDetector
{
    private enum WritingSystem
    {
        Unknown,
        Latin,
        Cyrillic,
        Greek,
        Hebrew,
        Arabic,
        Devanagari,
        Bengali,
        Gurmukhi,
        Gujarati,
        Oriya,
        Tamil,
        Telugu,
        Kannada,
        Malayalam,
        Sinhala,
        Thai,
        Lao,
        Tibetan,
        Georgian,
        Armenian,
        Ethiopic,
        Khmer,
        Myanmar,
        Han,
        Japanese,
        Hangul
    }

    private sealed record LanguageDefinition(
        string Code,
        WritingSystem Script,
        Regex? Signature = null,
        double SignatureWeight = 0.28,
        double Bias = 0.0);

    private static readonly Regex SpanishSignature = new("[ñáéíóúü]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FrenchSignature = new("[àâçéèêëîïôûùüÿœæ]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GermanSignature = new("[äöüß]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PortugueseSignature = new("[ãõáâàêéíóôúç]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ItalianSignature = new("[àèéìíòóù]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DutchSignature = new("ij", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PolishSignature = new("[ąćęłńóśźż]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CzechSignature = new("[áčďěíňóřšťúůýž]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SlovakSignature = new("[áäčďéíĺľňóôŕšťúýž]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HungarianSignature = new("[áéíóöőúüű]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RomanianSignature = new("[ăâîșşțţ]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TurkishSignature = new("[çğıİöşü]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CatalanSignature = new("[àçèéíïòóúü·]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BasqueSignature = new("tx|tz|dd" , RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CroatianSignature = new("[čćđšž]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SlovenianSignature = new("[čšž]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SerbianSignature = new("[čćđšž]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SwedishSignature = new("[åäö]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NorwegianSignature = new("[æøå]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DanishSignature = new("[æøå]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FinnishSignature = new("[äö]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IcelandicSignature = new("[ðþæö]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IrishSignature = new("bh|mh|fh|gc" , RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex WelshSignature = new("dd|ff|ll|rh|th", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MalteseSignature = new("[ċġħż]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VietnameseSignature = new("[ăâđêôơưáàảãạắằẳẵặấầẩẫậéèẻẽẹếềểễệíìỉĩịóòỏõọốồổỗộớờởỡợúùủũụứừửữựýỳỷỹỵ]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FilipinoSignature = new("ng", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IndonesianSignature = new("[ë]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SwahiliSignature = new("[ñ]|[mw][bp]" , RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YorubaSignature = new("[áéẹíóọú]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HausaSignature = new("[ƙƴɓ]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IgboSignature = new("[ịọụ]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SomaliSignature = new("[xq']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TagalogSignature = new("ng", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CebuanoSignature = new("[ñ]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MaoriSignature = new("wh|ng", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EsperantoSignature = new("[ĉĝĥĵŝŭ]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AlbanianSignature = new("[ëç]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LatvianSignature = new("[āčēģīķļņŗšūž]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LithuanianSignature = new("[ąčęėįšųūž]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EstonianSignature = new("[äöõü]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AfrikaansSignature = new("[êëïîôû]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GalicianSignature = new("[ñáéíóú]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OccitanSignature = new("[àèíòóúü]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BretonSignature = new("c'h", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ScotsSignature = new("nae|dinnae", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PortugueseBrazilSignature = new("[ão]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ChinesePunctuation = new("[。！？、《》「」『』]", RegexOptions.Compiled);
    private static readonly Regex EnglishIndicatorPattern = new(
        "\\b(the|and|for|with|this|that|from|have|has|was|were|been|being|are|is|into|which|about|because|while|where|when|what|who|whose|than|then|them|these|those|your|their|there|over|after|before|between|without|should|would|could|can't|don't|doesn't|won't|it's|i'm|we're|you're|they're)\\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EnglishMorphologyPattern = new(
        "\\b\\p{L}+(?:ing|ings|ed|tion|tions)\\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly IReadOnlyList<LanguageDefinition> LanguageDefinitions = BuildLanguageDefinitions();

    private static readonly IReadOnlyDictionary<WritingSystem, IReadOnlyList<LanguageDefinition>> LanguagesByScript =
        LanguageDefinitions
            .GroupBy(definition => definition.Script)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<LanguageDefinition>)group.ToList());

    /// <summary>
    /// 对传入文本执行多语言检测。
    /// </summary>
    public DetectionResult Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return DetectionResult.Unknown();
        }

        var trimmed = text.Trim();
        var scriptCounts = AnalyzeScripts(trimmed, out var totalLetters);
        if (totalLetters == 0)
        {
            return DetectionResult.Unknown();
        }

        var primary = scriptCounts
            .OrderByDescending(entry => entry.Value)
            .First();

        if (!LanguagesByScript.TryGetValue(primary.Key, out var definitions) || definitions.Count == 0)
        {
            return DetectionResult.Unknown();
        }

        var coverage = (double)primary.Value / totalLetters;
        var baseScore = CalculateBaseScore(coverage, definitions.Count);
        var uniqueLetters = CountUniqueLetters(trimmed, primary.Key);
        var usesBasicLatinOnly = primary.Key == WritingSystem.Latin && UsesOnlyBasicLatinLetters(trimmed);

        var scored = new List<(LanguageDefinition Definition, double Score)>();
        var hasSignatureMatch = false;
        var hasJapaneseKana = false;
        var containsChinesePunctuation = false;
        var isAmbiguousHan = false;
        var penalizeHanForLackOfKana = false;

        if (primary.Key == WritingSystem.Han)
        {
            hasJapaneseKana = ContainsJapaneseKana(trimmed);
            containsChinesePunctuation = ChinesePunctuation.IsMatch(trimmed);
            isAmbiguousHan = !hasJapaneseKana;
            if (containsChinesePunctuation && !hasJapaneseKana)
            {
                // Shared punctuation does not disambiguate Han-only text.
                isAmbiguousHan = true;
            }
            if (!hasJapaneseKana)
            {
                penalizeHanForLackOfKana = true;
            }
        }

        foreach (var definition in definitions)
        {
            var score = baseScore + definition.Bias;
            if (definition.Signature != null && definition.Signature.IsMatch(trimmed))
            {
                hasSignatureMatch = true;
                score += definition.SignatureWeight;
            }

            if (primary.Key == WritingSystem.Han)
            {
                if (penalizeHanForLackOfKana)
                {
                    score -= 0.08;
                }

                if (string.Equals(definition.Code, "ja", StringComparison.OrdinalIgnoreCase))
                {
                    // 纯汉字不够判断日语，若缺少假名则降低分值。
                    if (isAmbiguousHan)
                    {
                        score -= 0.18;
                    }
                }
                else if (string.Equals(definition.Code, "zh", StringComparison.OrdinalIgnoreCase))
                {
                    if (hasJapaneseKana)
                    {
                        score -= 0.15;
                    }
                    if (isAmbiguousHan)
                    {
                        score -= 0.12;
                    }
                }
            }

            if (primary.Key == WritingSystem.Japanese && !string.Equals(definition.Code, "ja", StringComparison.OrdinalIgnoreCase))
            {
                score -= 0.4;
            }
            scored.Add((definition, score));
        }

        var featurePenalty = CalculateFeaturePenalty(
            primary.Key,
            hasSignatureMatch,
            uniqueLetters,
            primary.Value,
            usesBasicLatinOnly,
            trimmed);
        if (featurePenalty > 0)
        {
            for (var i = 0; i < scored.Count; i++)
            {
                var entry = scored[i];
                scored[i] = (entry.Definition, entry.Score - featurePenalty);
            }
        }

        for (var i = 0; i < scored.Count; i++)
        {
            var entry = scored[i];
            scored[i] = (entry.Definition, Math.Clamp(entry.Score, 0, 0.99));
        }

        scored.Sort((left, right) => right.Score.CompareTo(left.Score));

        if (scored.Count == 0 || scored[0].Score <= 0)
        {
            return DetectionResult.Unknown();
        }

        var topScore = scored[0].Score;
        var confidence = Math.Round(Math.Min(0.99, topScore), 2, MidpointRounding.AwayFromZero);
        var bestLanguage = scored[0].Definition.Code;

        var limit = confidence < 0.75 ? 6 : 3;
        var candidates = scored
            .Take(limit)
            .Select(tuple => new DetectionCandidate(tuple.Definition.Code, Math.Round(Math.Clamp(tuple.Score, 0, 0.99), 2)))
            .ToList();

        return new DetectionResult(bestLanguage, confidence, candidates);
    }

    private static bool ContainsJapaneseKana(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;
            if ((value >= 0x3040 && value <= 0x30FF) || (value >= 0x31F0 && value <= 0x31FF))
            {
                return true;
            }
        }
        return false;
    }

    private static double CalculateBaseScore(double coverage, int candidates)
    {
        double score = coverage switch
        {
            >= 0.95 => 0.76,
            >= 0.85 => 0.7,
            >= 0.7 => 0.64,
            >= 0.55 => 0.58,
            >= 0.4 => 0.52,
            _ => 0.48
        };

        if (candidates == 1)
        {
            score += 0.2;
        }

        return score;
    }

    private static double CalculateFeaturePenalty(
        WritingSystem script,
        bool hasSignatureMatch,
        int uniqueLetters,
        int scriptLetterCount,
        bool usesBasicLatinOnly,
        string text)
    {
        double penalty = 0;

        if (script == WritingSystem.Latin)
        {
            if (!hasSignatureMatch && usesBasicLatinOnly)
            {
                var wordCount = CountWords(text);
                var hasRepresentativeStructure =
                    wordCount >= 4 &&
                    scriptLetterCount >= 18 &&
                    uniqueLetters >= 7;

                var englishEvidenceScore = CalculateEnglishEvidenceScore(text);
                var hasAnyEnglishEvidence = englishEvidenceScore > 0;
                var hasStrongEnglishEvidence = englishEvidenceScore >= 2;

                if (!hasAnyEnglishEvidence)
                {
                    var basePenalty = scriptLetterCount >= 12 ? 0.22 : 0.18;
                    penalty = Math.Max(penalty, basePenalty);
                }
                else if (!hasRepresentativeStructure && scriptLetterCount >= 12)
                {
                    penalty = Math.Max(penalty, 0.1);
                }
                else if (hasRepresentativeStructure && !hasStrongEnglishEvidence && scriptLetterCount >= 18)
                {
                    penalty = Math.Max(penalty, 0.06);
                }
            }

            if (scriptLetterCount >= 6 && uniqueLetters <= 4)
            {
                penalty = Math.Max(penalty, 0.16);
            }
            else if (scriptLetterCount >= 10 && uniqueLetters <= 6)
            {
                penalty = Math.Max(penalty, 0.12);
            }
        }
        else if (scriptLetterCount >= 4 && uniqueLetters <= 2)
        {
            penalty = Math.Max(penalty, 0.12);
        }

        return penalty;
    }

    private static int CalculateEnglishEvidenceScore(string text)
    {
        var evidence = EnglishIndicatorPattern.Matches(text).Count;
        evidence += EnglishMorphologyPattern.Matches(text).Count;

        return evidence;
    }

    private static int CountWords(string text)
    {
        var count = 0;
        var inWord = false;

        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsLetter(rune))
            {
                if (!inWord)
                {
                    inWord = true;
                    count++;
                }
            }
            else
            {
                inWord = false;
            }
        }

        return count;
    }

    private static int CountUniqueLetters(string text, WritingSystem script)
    {
        var unique = new HashSet<Rune>();
        foreach (var rune in text.EnumerateRunes())
        {
            if (!Rune.IsLetter(rune))
            {
                continue;
            }

            if (ClassifyRune(rune) != script)
            {
                continue;
            }

            var upper = Rune.ToUpperInvariant(rune);
            unique.Add(upper);
        }

        return unique.Count;
    }

    private static bool UsesOnlyBasicLatinLetters(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (!Rune.IsLetter(rune))
            {
                continue;
            }

            if (ClassifyRune(rune) != WritingSystem.Latin)
            {
                continue;
            }

            if (rune.Value > 0x007A)
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyDictionary<WritingSystem, int> AnalyzeScripts(string text, out int totalLetters)
    {
        var counts = new Dictionary<WritingSystem, int>();
        totalLetters = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            if (!Rune.IsLetter(rune))
            {
                continue;
            }

            totalLetters++;
            var system = ClassifyRune(rune);
            if (system == WritingSystem.Unknown)
            {
                continue;
            }

            counts.TryGetValue(system, out var current);
            counts[system] = current + 1;
        }

        if (counts.Count == 0 && totalLetters > 0)
        {
            counts[WritingSystem.Latin] = totalLetters;
        }

        return counts;
    }

    private static WritingSystem ClassifyRune(Rune rune)
    {
        var value = rune.Value;
        if (value <= 0x024F || (value >= 0x1E00 && value <= 0x1EFF))
        {
            return WritingSystem.Latin;
        }

        if (value is >= 0x0400 and <= 0x052F)
        {
            return WritingSystem.Cyrillic;
        }

        if (value is >= 0x0370 and <= 0x03FF)
        {
            return WritingSystem.Greek;
        }

        if (value is >= 0x0590 and <= 0x05FF)
        {
            return WritingSystem.Hebrew;
        }

        if (value is >= 0x0600 and <= 0x06FF || value is >= 0x0750 and <= 0x077F)
        {
            return WritingSystem.Arabic;
        }

        if (value is >= 0x0900 and <= 0x097F)
        {
            return WritingSystem.Devanagari;
        }

        if (value is >= 0x0980 and <= 0x09FF)
        {
            return WritingSystem.Bengali;
        }

        if (value is >= 0x0A00 and <= 0x0A7F)
        {
            return WritingSystem.Gurmukhi;
        }

        if (value is >= 0x0A80 and <= 0x0AFF)
        {
            return WritingSystem.Gujarati;
        }

        if (value is >= 0x0B00 and <= 0x0B7F)
        {
            return WritingSystem.Oriya;
        }

        if (value is >= 0x0B80 and <= 0x0BFF)
        {
            return WritingSystem.Tamil;
        }

        if (value is >= 0x0C00 and <= 0x0C7F)
        {
            return WritingSystem.Telugu;
        }

        if (value is >= 0x0C80 and <= 0x0CFF)
        {
            return WritingSystem.Kannada;
        }

        if (value is >= 0x0D00 and <= 0x0D7F)
        {
            return WritingSystem.Malayalam;
        }

        if (value is >= 0x0D80 and <= 0x0DFF)
        {
            return WritingSystem.Sinhala;
        }

        if (value is >= 0x0E00 and <= 0x0E7F)
        {
            return WritingSystem.Thai;
        }

        if (value is >= 0x0E80 and <= 0x0EFF)
        {
            return WritingSystem.Lao;
        }

        if (value is >= 0x0F00 and <= 0x0FFF)
        {
            return WritingSystem.Tibetan;
        }

        if (value is >= 0x10A0 and <= 0x10FF)
        {
            return WritingSystem.Georgian;
        }

        if (value is >= 0x0530 and <= 0x058F)
        {
            return WritingSystem.Armenian;
        }

        if (value is >= 0x1200 and <= 0x137F)
        {
            return WritingSystem.Ethiopic;
        }

        if (value is >= 0x1780 and <= 0x17FF)
        {
            return WritingSystem.Khmer;
        }

        if (value is >= 0x1000 and <= 0x109F)
        {
            return WritingSystem.Myanmar;
        }

        if (value is >= 0x4E00 and <= 0x9FFF)
        {
            return WritingSystem.Han;
        }

        if (value is >= 0x3040 and <= 0x30FF)
        {
            return WritingSystem.Japanese;
        }

        if (value is >= 0xAC00 and <= 0xD7AF)
        {
            return WritingSystem.Hangul;
        }

        return WritingSystem.Unknown;
    }

    private static IReadOnlyList<LanguageDefinition> BuildLanguageDefinitions()
    {
        var list = new List<LanguageDefinition>
        {
            new("en", WritingSystem.Latin, null, 0.28, 0.14),
            new("es", WritingSystem.Latin, SpanishSignature, 0.3, 0.13),
            new("fr", WritingSystem.Latin, FrenchSignature, 0.32, 0.12),
            new("de", WritingSystem.Latin, GermanSignature, 0.32, 0.11),
            new("it", WritingSystem.Latin, ItalianSignature, 0.28, 0.1),
            new("pt", WritingSystem.Latin, PortugueseSignature, 0.32, 0.09),
            new("pt-BR", WritingSystem.Latin, PortugueseBrazilSignature, 0.26, 0.05),
            new("nl", WritingSystem.Latin, DutchSignature, 0.22, 0.08),
            new("pl", WritingSystem.Latin, PolishSignature, 0.3, 0.07),
            new("cs", WritingSystem.Latin, CzechSignature, 0.3, 0.06),
            new("sk", WritingSystem.Latin, SlovakSignature, 0.3, 0.05),
            new("hu", WritingSystem.Latin, HungarianSignature, 0.32, 0.05),
            new("ro", WritingSystem.Latin, RomanianSignature, 0.32, 0.05),
            new("tr", WritingSystem.Latin, TurkishSignature, 0.3, 0.06),
            new("ca", WritingSystem.Latin, CatalanSignature, 0.3, 0.05),
            new("eu", WritingSystem.Latin, BasqueSignature, 0.26, 0.04),
            new("gl", WritingSystem.Latin, GalicianSignature, 0.28, 0.04),
            new("hr", WritingSystem.Latin, CroatianSignature, 0.28, 0.04),
            new("sl", WritingSystem.Latin, SlovenianSignature, 0.28, 0.03),
            new("sr-Latn", WritingSystem.Latin, SerbianSignature, 0.28, 0.03),
            new("bs", WritingSystem.Latin, SerbianSignature, 0.26, 0.03),
            new("sv", WritingSystem.Latin, SwedishSignature, 0.3, 0.05),
            new("da", WritingSystem.Latin, DanishSignature, 0.28, 0.05),
            new("nb", WritingSystem.Latin, NorwegianSignature, 0.28, 0.04),
            new("nn", WritingSystem.Latin, NorwegianSignature, 0.28, 0.03),
            new("fi", WritingSystem.Latin, FinnishSignature, 0.28, 0.05),
            new("et", WritingSystem.Latin, EstonianSignature, 0.26, 0.03),
            new("lv", WritingSystem.Latin, LatvianSignature, 0.28, 0.03),
            new("lt", WritingSystem.Latin, LithuanianSignature, 0.28, 0.03),
            new("is", WritingSystem.Latin, IcelandicSignature, 0.32, 0.02),
            new("af", WritingSystem.Latin, AfrikaansSignature, 0.24, 0.02),
            new("sq", WritingSystem.Latin, AlbanianSignature, 0.24, 0.02),
            new("ga", WritingSystem.Latin, IrishSignature, 0.22, 0.01),
            new("cy", WritingSystem.Latin, WelshSignature, 0.22, 0.01),
            new("mt", WritingSystem.Latin, MalteseSignature, 0.24, 0.01),
            new("br", WritingSystem.Latin, BretonSignature, 0.22, 0.0),
            new("gd", WritingSystem.Latin, ScotsSignature, 0.2, 0.0),
            new("fy", WritingSystem.Latin, null, 0.26, 0.0),
            new("fo", WritingSystem.Latin, null, 0.24, 0.0),
            new("rm", WritingSystem.Latin, null, 0.24, 0.0),
            new("la", WritingSystem.Latin, null, 0.2, 0.0),
            new("eo", WritingSystem.Latin, EsperantoSignature, 0.32, 0.0),
            new("oc", WritingSystem.Latin, OccitanSignature, 0.24, 0.0),
            new("ast", WritingSystem.Latin, OccitanSignature, 0.22, 0.0),
            new("sc", WritingSystem.Latin, OccitanSignature, 0.2, 0.0),
            new("vec", WritingSystem.Latin, ItalianSignature, 0.22, 0.0),
            new("ms", WritingSystem.Latin, IndonesianSignature, 0.2, 0.02),
            new("id", WritingSystem.Latin, IndonesianSignature, 0.22, 0.03),
            new("fil", WritingSystem.Latin, FilipinoSignature, 0.22, 0.02),
            new("tl", WritingSystem.Latin, TagalogSignature, 0.22, 0.01),
            new("ceb", WritingSystem.Latin, CebuanoSignature, 0.2, 0.01),
            new("ilo", WritingSystem.Latin, TagalogSignature, 0.2, 0.0),
            new("war", WritingSystem.Latin, TagalogSignature, 0.2, 0.0),
            new("pam", WritingSystem.Latin, TagalogSignature, 0.2, 0.0),
            new("su", WritingSystem.Latin, null, 0.18, 0.0),
            new("jv", WritingSystem.Latin, null, 0.18, 0.01),
            new("bug", WritingSystem.Latin, null, 0.18, 0.0),
            new("vi", WritingSystem.Latin, VietnameseSignature, 0.34, 0.05),
            new("ace", WritingSystem.Latin, null, 0.18, 0.0),
            new("sw", WritingSystem.Latin, SwahiliSignature, 0.2, 0.02),
            new("yo", WritingSystem.Latin, YorubaSignature, 0.3, 0.02),
            new("ha", WritingSystem.Latin, HausaSignature, 0.32, 0.02),
            new("ig", WritingSystem.Latin, IgboSignature, 0.3, 0.02),
            new("mg", WritingSystem.Latin, null, 0.2, 0.0),
            new("so", WritingSystem.Latin, SomaliSignature, 0.26, 0.0),
            new("rw", WritingSystem.Latin, null, 0.2, 0.0),
            new("lg", WritingSystem.Latin, null, 0.2, 0.0),
            new("ny", WritingSystem.Latin, null, 0.2, 0.0),
            new("sn", WritingSystem.Latin, null, 0.2, 0.0),
            new("ss", WritingSystem.Latin, null, 0.2, 0.0),
            new("tn", WritingSystem.Latin, null, 0.2, 0.0),
            new("ts", WritingSystem.Latin, null, 0.2, 0.0),
            new("st", WritingSystem.Latin, null, 0.2, 0.0),
            new("ve", WritingSystem.Latin, null, 0.2, 0.0),
            new("xh", WritingSystem.Latin, null, 0.2, 0.0),
            new("zu", WritingSystem.Latin, null, 0.2, 0.01),
            new("nso", WritingSystem.Latin, null, 0.2, 0.0),
            new("rwk", WritingSystem.Latin, null, 0.2, 0.0),
            new("wo", WritingSystem.Latin, null, 0.2, 0.0),
            new("bm", WritingSystem.Latin, null, 0.2, 0.0),
            new("ff", WritingSystem.Latin, null, 0.2, 0.0),
            new("kj", WritingSystem.Latin, null, 0.2, 0.0),
            new("rn", WritingSystem.Latin, null, 0.2, 0.0),
            new("pap", WritingSystem.Latin, null, 0.2, 0.0),
            new("crs", WritingSystem.Latin, null, 0.2, 0.0),
            new("mi", WritingSystem.Latin, MaoriSignature, 0.22, 0.0),
            new("sm", WritingSystem.Latin, null, 0.2, 0.0),
            new("fj", WritingSystem.Latin, null, 0.2, 0.0),
            new("ban", WritingSystem.Latin, null, 0.18, 0.0),
            new("min", WritingSystem.Latin, null, 0.18, 0.0),
            new("ht", WritingSystem.Latin, FrenchSignature, 0.24, 0.02),
            new("pt-PT", WritingSystem.Latin, PortugueseSignature, 0.28, 0.02),
            new("pt-AO", WritingSystem.Latin, PortugueseSignature, 0.28, 0.0),
            new("es-MX", WritingSystem.Latin, SpanishSignature, 0.3, 0.03),

            new("ru", WritingSystem.Cyrillic, null, 0.28, 0.12),
            new("uk", WritingSystem.Cyrillic, null, 0.3, 0.08),
            new("be", WritingSystem.Cyrillic, null, 0.28, 0.05),
            new("bg", WritingSystem.Cyrillic, null, 0.28, 0.05),
            new("mk", WritingSystem.Cyrillic, null, 0.28, 0.04),
            new("sr", WritingSystem.Cyrillic, null, 0.28, 0.04),
            new("mn", WritingSystem.Cyrillic, null, 0.26, 0.03),
            new("kk", WritingSystem.Cyrillic, null, 0.26, 0.03),
            new("ky", WritingSystem.Cyrillic, null, 0.26, 0.02),
            new("tt", WritingSystem.Cyrillic, null, 0.24, 0.02),
            new("ba", WritingSystem.Cyrillic, null, 0.24, 0.01),
            new("tg", WritingSystem.Cyrillic, null, 0.24, 0.01),
            new("uz", WritingSystem.Cyrillic, null, 0.24, 0.01),
            new("ab", WritingSystem.Cyrillic, null, 0.22, 0.0),
            new("sah", WritingSystem.Cyrillic, null, 0.22, 0.0),

            new("el", WritingSystem.Greek, null, 0.3, 0.1),

            new("he", WritingSystem.Hebrew, null, 0.3, 0.08),
            new("yi", WritingSystem.Hebrew, null, 0.28, 0.03),

            new("ar", WritingSystem.Arabic, null, 0.32, 0.12),
            new("fa", WritingSystem.Arabic, null, 0.32, 0.09),
            new("ur", WritingSystem.Arabic, null, 0.32, 0.08),
            new("ps", WritingSystem.Arabic, null, 0.32, 0.06),
            new("ku", WritingSystem.Arabic, null, 0.32, 0.05),
            new("ug", WritingSystem.Arabic, null, 0.3, 0.05),
            new("sd", WritingSystem.Arabic, null, 0.3, 0.04),
            new("ks", WritingSystem.Arabic, null, 0.3, 0.03),
            new("dv", WritingSystem.Arabic, null, 0.28, 0.02),
            new("ckb", WritingSystem.Arabic, null, 0.3, 0.04),

            new("hi", WritingSystem.Devanagari, null, 0.32, 0.12),
            new("mr", WritingSystem.Devanagari, null, 0.32, 0.08),
            new("ne", WritingSystem.Devanagari, null, 0.32, 0.07),
            new("sa", WritingSystem.Devanagari, null, 0.32, 0.05),

            new("bn", WritingSystem.Bengali, null, 0.32, 0.12),
            new("as", WritingSystem.Bengali, null, 0.32, 0.06),
            new("or", WritingSystem.Oriya, null, 0.32, 0.08),

            new("pa", WritingSystem.Gurmukhi, null, 0.32, 0.08),
            new("gu", WritingSystem.Gujarati, null, 0.32, 0.08),
            new("ta", WritingSystem.Tamil, null, 0.32, 0.1),
            new("te", WritingSystem.Telugu, null, 0.32, 0.09),
            new("kn", WritingSystem.Kannada, null, 0.32, 0.09),
            new("ml", WritingSystem.Malayalam, null, 0.32, 0.09),
            new("si", WritingSystem.Sinhala, null, 0.32, 0.08),

            new("th", WritingSystem.Thai, null, 0.34, 0.12),
            new("lo", WritingSystem.Lao, null, 0.34, 0.09),
            new("km", WritingSystem.Khmer, null, 0.34, 0.1),
            new("my", WritingSystem.Myanmar, null, 0.34, 0.1),
            new("bo", WritingSystem.Tibetan, null, 0.34, 0.1),
            new("dz", WritingSystem.Tibetan, null, 0.34, 0.08),

            new("ka", WritingSystem.Georgian, null, 0.34, 0.1),
            new("hy", WritingSystem.Armenian, null, 0.34, 0.1),

            new("am", WritingSystem.Ethiopic, null, 0.34, 0.1),
            new("ti", WritingSystem.Ethiopic, null, 0.34, 0.08),

            new("zh", WritingSystem.Han, null, 0.34, 0.18),
            new("ja", WritingSystem.Han, null, 0.34, 0.16),
            new("ja", WritingSystem.Japanese, null, 0.34, 0.18),
            new("ko", WritingSystem.Hangul, null, 0.34, 0.18)
        };

        return list;
    }
}
