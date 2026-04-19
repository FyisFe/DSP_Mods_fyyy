using System;

namespace BlueprintSearch;

internal static class SearchFilter
{
    private static readonly char[] Separators = { ' ', '\t', '/', '\\' };

    /// <summary>
    /// Split the query into lowercased, non-empty tokens using whitespace and path separators.
    /// </summary>
    internal static string[] Tokenize(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<string>();
        return query.ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Returns true iff every token is a substring of pathLower (logical AND).
    /// Caller must ensure pathLower is already lowercased and tokens.Length > 0.
    /// </summary>
    internal static bool Matches(string pathLower, string[] tokens)
    {
        for (int i = 0; i < tokens.Length; i++)
        {
            if (pathLower.IndexOf(tokens[i], StringComparison.Ordinal) < 0)
                return false;
        }
        return true;
    }
}
