using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PersonalLearningAgent;

internal sealed record MemoryMatch(MemoryEntry Entry, double Similarity);

internal static partial class MemoryMatcher
{
    private static readonly HashSet<string> StopWords =
    [
        "a", "an", "cac", "cho", "co", "cua", "da", "dang", "de", "den", "doc",
        "duoc", "gi", "ghi", "hay", "la", "lai", "len", "mot", "nay", "nho",
        "nhu", "nhung", "noi", "o", "qua", "ra", "rang", "roi", "se", "tai",
        "theo", "thi", "tu", "tren", "trong", "ve", "va", "voi", "kien",
        "thuc", "noi", "dung", "thong", "tin", "tim", "nhat", "gan",
        "giong", "muc", "tra", "cuu", "yeu", "cau"
    ];

    public static MemoryMatch? FindBest(string query, IReadOnlyCollection<MemoryEntry> entries)
    {
        if (entries.Count == 0 || string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var exactId = entries.FirstOrDefault(
            entry => string.Equals(entry.Id, query.Trim(), StringComparison.OrdinalIgnoreCase));
        if (exactId is not null)
        {
            return new MemoryMatch(exactId, 1);
        }

        var queryTokens = GetTokens(query);
        var meaningfulQueryTokens = queryTokens.Where(token => !StopWords.Contains(token)).ToArray();
        if (meaningfulQueryTokens.Length > 0)
        {
            queryTokens = meaningfulQueryTokens;
        }

        if (queryTokens.Length == 0)
        {
            return null;
        }

        return entries
            .Select(entry => new MemoryMatch(entry, Score(query, queryTokens, entry.Text)))
            .Where(match => match.Similarity > 0)
            .OrderByDescending(match => match.Similarity)
            .ThenByDescending(match => match.Entry.CreatedAt)
            .FirstOrDefault();
    }

    private static double Score(string query, IReadOnlyList<string> queryTokens, string text)
    {
        var normalizedQuery = Normalize(query);
        var normalizedText = Normalize(text);
        if (normalizedText.Contains(normalizedQuery, StringComparison.Ordinal))
        {
            return 1;
        }

        var textTokens = GetTokens(text);
        if (textTokens.Length == 0)
        {
            return 0;
        }

        var total = queryTokens.Sum(queryToken =>
        {
            var best = textTokens.Max(textToken => TokenSimilarity(queryToken, textToken));
            return best >= 0.6 ? best : 0;
        });

        return total / queryTokens.Count;
    }

    private static double TokenSimilarity(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return 1;
        }

        var shorterLength = Math.Min(left.Length, right.Length);
        var longerLength = Math.Max(left.Length, right.Length);
        if (shorterLength >= 4 && (left.Contains(right, StringComparison.Ordinal)
                                  || right.Contains(left, StringComparison.Ordinal)))
        {
            return (double)shorterLength / longerLength;
        }

        if (shorterLength < 4)
        {
            return 0;
        }

        var distance = LevenshteinDistance(left, right);
        return 1 - (double)distance / longerLength;
    }

    private static int LevenshteinDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];

        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitutionCost = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                current[rightIndex] = Math.Min(
                    Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1),
                    previous[rightIndex - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private static string[] GetTokens(string text) =>
        TokenRegex().Matches(Normalize(text))
            .Select(match => match.Value)
            .ToArray();

    private static string Normalize(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character) is 'đ' ? 'd' : char.ToLowerInvariant(character));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}
