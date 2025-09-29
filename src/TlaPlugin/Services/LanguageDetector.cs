using System.Text.RegularExpressions;
using TlaPlugin.Models;

namespace TlaPlugin.Services;

/// <summary>
<<<<<<< HEAD
/// 支撑 R1 要求的简易语言检测器。
=======
/// 簡易な言語検出器。R1 要件の初期段を支える。
>>>>>>> origin/main
/// </summary>
public class LanguageDetector
{
    private static readonly Regex Japanese = new("[\u3040-\u30ff\u3400-\u4dbf\u4e00-\u9fff]", RegexOptions.Compiled);
    private static readonly Regex Spanish = new("[áéíóúñü]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public DetectionResult Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new DetectionResult("unknown", 0);
        }

        if (Japanese.IsMatch(text))
        {
            return new DetectionResult("ja", 0.92);
        }

        if (Spanish.IsMatch(text))
        {
            return new DetectionResult("es", 0.75);
        }

        return new DetectionResult("en", 0.6);
    }
}
