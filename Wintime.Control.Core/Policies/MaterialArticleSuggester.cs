using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Wintime.Control.Core.Policies;

/// <summary>Предложение следующего артикула материала по последнему добавленному.</summary>
public static class MaterialArticleSuggester
{
    private static readonly Regex DigitRuns = new("[0-9]+", RegexOptions.Compiled);

    public static string? Suggest(string? latestArticle, IEnumerable<string> existingArticles)
    {
        if (latestArticle == null)
            return null;

        var taken = existingArticles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = latestArticle;
        while (true)
        {
            candidate = Increment(candidate);
            if (candidate.Length > MaterialRules.ArticleMaxLength)
                return null;
            if (taken.Add(candidate))
                return candidate;
        }
    }

    private static string Increment(string article)
    {
        Match? longest = null;
        foreach (Match match in DigitRuns.Matches(article))
        {
            // При равной длине выбираем самую правую последовательность.
            if (longest == null || match.Length >= longest.Length)
                longest = match;
        }

        if (longest == null)
            return article + "0";

        var number = BigInteger.Parse(longest.Value, CultureInfo.InvariantCulture) + BigInteger.One;
        var replacement = number.ToString(CultureInfo.InvariantCulture).PadLeft(longest.Length, '0');
        return article[..longest.Index] + replacement + article[(longest.Index + longest.Length)..];
    }
}
