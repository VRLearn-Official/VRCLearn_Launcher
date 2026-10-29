using System.Text;

namespace VRCLearn.Core.Text;

/// <summary>搜索匹配：先按包含找，再容忍少量打字错误。</summary>
public static class ApproximateMatch
{
    /// <summary>查询短于这个长度时只按包含匹配，免得什么都算命中。</summary>
    public const int MinimumFuzzyLength = 4;

    /// <summary>越小越贴：0 完全相同，1 前缀，2 包含，3 起是错了一两个字母。null 表示不匹配。</summary>
    public static int? Rank(string query, params string[] candidates)
    {
        var term = query.Trim().ToLowerInvariant();

        if (term.Length == 0)
        {
            return null;
        }

        int? best = null;

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (RankOf(term, candidate.ToLowerInvariant()) is not { } rank)
            {
                continue;
            }

            if (best is null || rank < best)
            {
                best = rank;
            }
        }

        return best;
    }

    /// <summary>Damerau-Levenshtein，相邻两个字母对调算一次。</summary>
    public static int Distance(string left, string right)
    {
        var a = left.ToLowerInvariant();
        var b = right.ToLowerInvariant();
        var d = new int[a.Length + 1, b.Length + 1];

        for (var i = 0; i <= a.Length; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= b.Length; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;

                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);

                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
        }

        return d[a.Length, b.Length];
    }

    private static int? RankOf(string term, string text)
    {
        if (text == term)
        {
            return 0;
        }

        if (text.StartsWith(term, StringComparison.Ordinal))
        {
            return 1;
        }

        if (text.Contains(term, StringComparison.Ordinal))
        {
            return 2;
        }

        var allowed = Allowed(term.Length);

        if (allowed == 0)
        {
            return null;
        }

        var closest = int.MaxValue;

        foreach (var token in Tokens(text))
        {
            var distance = Distance(term, token);

            if (distance <= allowed && distance < closest)
            {
                closest = distance;
            }
        }

        return closest == int.MaxValue ? null : 2 + closest;
    }

    private static int Allowed(int length) => length switch
    {
        < MinimumFuzzyLength => 0,
        <= 6 => 1,
        _ => 2,
    };

    /// <summary>按非字母数字切开，包名里的点和连字符都当分隔。</summary>
    private static IEnumerable<string> Tokens(string text)
    {
        var current = new StringBuilder();

        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                current.Append(character);
                continue;
            }

            if (current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }
}
