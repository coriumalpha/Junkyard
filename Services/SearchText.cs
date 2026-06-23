using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Inventario.Services;

public static class SearchText
{
    private static readonly Regex AcronymRegex = new(@"[A-Z0-9]{2,}", RegexOptions.Compiled);

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    public static List<string> Tokenize(string? value)
    {
        return Normalize(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static string BuildIndex(params object?[] values)
    {
        return BuildIndex(values.Select(value => value?.ToString()));
    }

    public static string BuildIndex(IEnumerable<string?> values)
    {
        var raw = string.Join(' ', values.Where(value => !string.IsNullOrWhiteSpace(value)));
        var aliasParts = new List<string>();
        foreach (Match match in AcronymRegex.Matches(raw))
        {
            aliasParts.Add(match.Value);
        }

        return string.IsNullOrWhiteSpace(raw)
            ? string.Join(' ', aliasParts)
            : string.Join(' ', new[] { raw, string.Join(' ', aliasParts) }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    public static int ScoreEntry(
        string rawQuery,
        string normalizedQuery,
        IReadOnlyList<string> terms,
        string? normalizedCode,
        string? ctDigits,
        IReadOnlyList<(string? Value, int Weight)> fields)
    {
        if (terms.Count == 0)
        {
            return int.MinValue;
        }

        var normalizedFields = fields
            .Select(field => (Value: Normalize(field.Value), field.Weight))
            .ToList();

        var combined = string.Join(' ', normalizedFields.Select(field => field.Value).Where(value => !string.IsNullOrWhiteSpace(value)));
        if (combined.Length == 0)
        {
            return int.MinValue;
        }

        var score = 0;
        var matchedTerms = 0;
        foreach (var term in terms)
        {
            var termMatched = false;
            foreach (var field in normalizedFields)
            {
                if (string.IsNullOrWhiteSpace(field.Value))
                {
                    continue;
                }

                if (field.Value == term)
                {
                    score += field.Weight * 5;
                    termMatched = true;
                }
                else if (field.Value.StartsWith(term, StringComparison.Ordinal))
                {
                    score += field.Weight * 4;
                    termMatched = true;
                }
                else if (field.Value.Contains(term, StringComparison.Ordinal))
                {
                    score += field.Weight * 2;
                    termMatched = true;
                }
            }

            if (!termMatched)
            {
                return int.MinValue;
            }

            matchedTerms++;
        }

        score += matchedTerms * 20;

        if (combined == normalizedQuery)
        {
            score += 800;
        }
        else if (combined.StartsWith(normalizedQuery, StringComparison.Ordinal))
        {
            score += 300;
        }
        else if (combined.Contains(normalizedQuery, StringComparison.Ordinal))
        {
            score += 120;
        }

        var firstField = normalizedFields.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstField.Value))
        {
            if (firstField.Value == normalizedQuery)
            {
                score += 200;
            }
            else if (firstField.Value.StartsWith(normalizedQuery, StringComparison.Ordinal))
            {
                score += 80;
            }
        }

        if (!string.IsNullOrWhiteSpace(normalizedCode) && normalizedFields.Any(field => field.Value == normalizedCode))
        {
            score += 1000;
        }

        if (!string.IsNullOrWhiteSpace(ctDigits) && normalizedFields.Any(field => field.Value != null && field.Value.EndsWith(ctDigits, StringComparison.Ordinal)))
        {
            score += 160;
        }

        if (rawQuery.IndexOf(' ') >= 0)
        {
            score += 10;
        }

        return score;
    }
}
