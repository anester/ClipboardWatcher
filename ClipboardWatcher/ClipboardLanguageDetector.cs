using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClipboardWatcher;

public static class ClipboardLanguageDetector
{
    public const string Text = "Text";
    public const string Xml = "XML";
    public const string Json = "JSON";
    public const string JavaScript = "JavaScript";
    public const string CSharp = "CSharp";
    public const string TypeScript = "TypeScript";
    public const string Java = "Java";
    public const string Python = "Python";

    public static string Detect(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Text;
        }

        var trimmed = content.Trim();
        if (LooksLikeJson(trimmed))
        {
            return Json;
        }

        if (LooksLikeXml(trimmed))
        {
            return Xml;
        }

        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [CSharp] = 0,
            [Java] = 0,
            [TypeScript] = 0,
            [JavaScript] = 0,
            [Python] = 0
        };

        var lower = trimmed.ToLowerInvariant();
        ScoreContains(scores, CSharp, lower, "using system", 2);
        ScoreRegex(scores, CSharp, trimmed, @"\bnamespace\s+\w+", 2);
        ScoreRegex(scores, CSharp, trimmed, @"\bpublic\s+class\b", 1);
        ScoreRegex(scores, CSharp, trimmed, @"\bConsole\.WriteLine\b", 2);
        ScoreRegex(scores, CSharp, trimmed, @"\basync\s+Task\b", 2);
        ScoreRegex(scores, CSharp, trimmed, @"\bget;\b", 1);
        ScoreRegex(scores, CSharp, trimmed, @"\bset;\b", 1);

        ScoreRegex(scores, Java, trimmed, @"\bpublic\s+class\b", 1);
        ScoreRegex(scores, Java, trimmed, @"\bstatic\s+void\s+main\b", 3);
        ScoreContains(scores, Java, lower, "system.out.println", 3);
        ScoreRegex(scores, Java, trimmed, @"\bimport\s+java\.", 2);
        ScoreRegex(scores, Java, trimmed, @"\bpackage\s+[\w\.]+;", 2);

        ScoreRegex(scores, TypeScript, trimmed, @"\binterface\s+\w+", 2);
        ScoreRegex(scores, TypeScript, trimmed, @"\btype\s+\w+\s*=", 2);
        ScoreRegex(scores, TypeScript, trimmed, @"\benum\s+\w+", 2);
        ScoreRegex(scores, TypeScript, trimmed, @"\bimplements\s+\w+", 1);
        ScoreRegex(scores, TypeScript, trimmed, @"\breadonly\s+\w+", 1);
        ScoreRegex(scores, TypeScript, trimmed, @"\bimport\s+type\b", 2);
        ScoreRegex(scores, TypeScript, trimmed, @"\b:\s*(string|number|boolean|any|unknown|void|never|object|Record<|Array<)", 2);

        ScoreRegex(scores, JavaScript, trimmed, @"\bfunction\s+\w+\s*\(", 1);
        ScoreContains(scores, JavaScript, lower, "=>", 1);
        ScoreRegex(scores, JavaScript, trimmed, @"\b(const|let|var)\s+\w+", 1);
        ScoreRegex(scores, JavaScript, trimmed, @"\bimport\s+.+\s+from\s+['""][^'""]+['""]", 1);
        ScoreRegex(scores, JavaScript, trimmed, @"\bexport\s+(default|function|const|class|async)\b", 1);
        ScoreContains(scores, JavaScript, lower, "console.log", 2);

        ScoreRegex(scores, Python, trimmed, @"\bdef\s+\w+\s*\(", 2);
        ScoreRegex(scores, Python, trimmed, @"\bclass\s+\w+\s*:", 2);
        ScoreRegex(scores, Python, trimmed, @"\bimport\s+\w+", 1);
        ScoreRegex(scores, Python, trimmed, @"\bfrom\s+\w+\s+import\s+\w+", 1);
        ScoreContains(scores, Python, lower, "if __name__ == \"__main__\"", 3);
        ScoreContains(scores, Python, lower, "print(", 1);

        return ChooseBest(scores);
    }

    private static bool LooksLikeJson(string trimmed)
    {
        if (trimmed.Length < 2 || (trimmed[0] != '{' && trimmed[0] != '['))
        {
            return false;
        }

        try
        {
            using var _ = JsonDocument.Parse(trimmed);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool LooksLikeXml(string trimmed)
    {
        if (!trimmed.StartsWith("<", StringComparison.Ordinal))
        {
            return false;
        }

        if (trimmed.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return trimmed.Contains("</", StringComparison.Ordinal)
            || trimmed.Contains("/>", StringComparison.Ordinal)
            || trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase);
    }

    private static void ScoreContains(IDictionary<string, int> scores, string key, string haystack, string needle, int points)
    {
        if (haystack.Contains(needle, StringComparison.Ordinal))
        {
            scores[key] += points;
        }
    }

    private static void ScoreRegex(IDictionary<string, int> scores, string key, string content, string pattern, int points)
    {
        if (Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline))
        {
            scores[key] += points;
        }
    }

    private static string ChooseBest(Dictionary<string, int> scores)
    {
        var bestScore = 0;
        string? bestKey = null;
        var tie = false;

        foreach (var pair in scores)
        {
            if (pair.Value <= 0)
            {
                continue;
            }

            if (pair.Value > bestScore)
            {
                bestScore = pair.Value;
                bestKey = pair.Key;
                tie = false;
                continue;
            }

            if (pair.Value == bestScore && bestKey is not null)
            {
                tie = true;
            }
        }

        if (bestKey is null || tie)
        {
            return Text;
        }

        return bestKey;
    }
}
