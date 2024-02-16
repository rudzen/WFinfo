using Newtonsoft.Json.Linq;

namespace WFInfo.Services;

public sealed class LevenshteinDistanceService : ILevenshteinDistanceService
{
    private static readonly List<Dictionary<int, List<int>>> Korean =
    [
        new Dictionary<int, List<int>>()
        {
            { 0, [6, 7, 8, 16] },           // ㅁ, ㅂ, ㅃ, ㅍ
            { 1, [2, 3, 4, 16, 5, 9, 10] }, // ㄴ, ㄷ, ㄸ, ㅌ, ㄹ, ㅅ, ㅆ
            { 2, [12, 13, 14] },            // ㅈ, ㅉ, ㅊ
            { 3, [0, 1, 15, 11, 18] }       // ㄱ, ㄲ, ㅋ, ㅇ, ㅎ
        },

        new Dictionary<int, List<int>>()
        {
            { 0, [20, 5, 1, 7, 3, 19] }, // ㅣ, ㅔ, ㅐ, ㅖ, ㅒ, ㅢ
            { 1, [16, 11, 15, 10] },     // ㅟ, ㅚ, ㅞ, ㅙ
            { 2, [4, 0, 6, 2, 14, 9] },  // ㅓ, ㅏ, ㅕ, ㅑ, ㅝ, ㅘ
            { 3, [18, 13, 8, 17, 12] }   // ㅡ, ㅜ, ㅗ, ㅠ, ㅛ
        },

        new Dictionary<int, List<int>>()
        {
            { 0, [16, 17, 18, 26] }, // ㅁ, ㅂ, ㅄ, ㅍ
            {
                1, [4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 19, 20, 25]
            },                            // ㄴ, ㄵ, ㄶ, ㄷ, ㄹ, ㄺ, ㄻ, ㄼ, ㄽ, ㄾ, ㄿ, ㅀ, ㅅ, ㅆ, ㅌ
            { 2, [22, 23] },              // ㅈ, ㅊ
            { 3, [1, 2, 3, 24, 21, 27] }, // ㄱ, ㄲ, ㄳ, ㅋ, ㅑ, ㅎ
            { 4, [0] }
        }
    ];

    private readonly char[,]? _replacementList = null;

    public int LevenshteinDistance(string s, string t, string locale, JObject marketItems)
    {
        return locale switch
        {
            // for korean
            "ko" => LevenshteinDistanceKorean(s, t, marketItems),
            _    => LevenshteinDistanceDefault(s, t)
        };
    }

    public int LevenshteinDistanceSecond(string str1, string str2, int limit = -1)
    {
        var s = str1.AsSpan();
        var t = str2.AsSpan();
        var n = s.Length;
        var m = t.Length;

        if (n == 0 || m == 0)
            return n + m;

        var d = new int[n + 1 + 1 - 1, m + 1 + 1 - 1];
        List<int> activeX = [];
        List<int> activeY = [];
        d[0, 0] = 1;
        activeX.Add(0);
        activeY.Add(0);
        bool maxY;
        bool maxX;
        do
        {
            var currX = activeX[0];
            activeX.RemoveAt(0);
            var currY = activeY[0];
            activeY.RemoveAt(0);

            var temp = d[currX, currY];

            if (limit != -1 && temp > limit)
                return temp;

            maxX = currX == n;
            maxY = currY == m;

            if (!maxX)
            {
                temp = d[currX, currY] + 1;
                if (temp < d[currX + 1, currY] || d[currX + 1, currY] == 0)
                {
                    d[currX + 1, currY] = temp;
                    AddElement(d, activeX, activeY, currX + 1, currY);
                }
            }

            if (!maxY)
            {
                temp = d[currX, currY] + 1;
                if (temp < d[currX, currY + 1] || d[currX, currY + 1] == 0)
                {
                    d[currX, currY + 1] = temp;
                    AddElement(d, activeX, activeY, currX, currY + 1);
                }
            }

            if (maxX || maxY)
                continue;

            var diff = GetDifference(
                c1: char.ToLower(s[currX], ApplicationConstants.Culture),
                c2: char.ToLower(t[currY], ApplicationConstants.Culture)
            );
            temp = d[currX, currY] + diff;

            if (temp >= d[currX + 1, currY + 1] && d[currX + 1, currY + 1] != 0)
                continue;

            d[currX + 1, currY + 1] = temp;
            AddElement(d, activeX, activeY, currX + 1, currY + 1);
        } while (!(maxX && maxY));

        return d[n, m] - 1;
    }

    private static void AddElement(
        int[,] d,
        List<int> xList,
        List<int> yList,
        int x,
        int y)
    {
        var loc = 0;
        var temp = d[x, y];
        while (loc < xList.Count && temp > d[xList[loc], yList[loc]])
            loc++;

        if (loc == xList.Count)
        {
            xList.Add(x);
            yList.Add(y);
            return;
        }

        xList.Insert(loc, x);
        yList.Insert(loc, y);
    }

    private int GetDifference(char c1, char c2)
    {
        if (c1 == c2 || c1 == '?' || c2 == '?')
            return 0;

        for (var i = 0; i < _replacementList.GetLength(0) - 1; i++)
        {
            if ((c1 == _replacementList[i, 0] || c2 == _replacementList[i, 0]) &&
                (c1 == _replacementList[i, 1] || c2 == _replacementList[i, 1]))
            {
                return 0;
            }
        }

        return 1;
    }

    private int LevenshteinDistanceDefault(string sIn, string tIn)
    {
        // Levenshtein Distance determines how many character changes it takes to form a known result
        // For example: Nuvo Prime is closer to Nova Prime (2) then Ash Prime (4)
        // For more info see: https://en.wikipedia.org/wiki/Levenshtein_distance
        var s = sIn.AsSpan();
        var t = tIn.AsSpan();// .ToLower(ApplicationConstants.Culture);
        var n = s.Length;
        var m = t.Length;

        if (n == 0 || m == 0)
            return n + m;

        var d = new int[n + 1, m + 1];

        d[0, 0] = 0;

        var count = 0;
        for (var i = 1; i <= n; i++)
            d[i, 0] = (s[i - 1] == ' ' ? count : ++count);

        count = 0;
        for (var j = 1; j <= m; j++)
            d[0, j] = (t[j - 1] == ' ' ? count : ++count);

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                // deletion of s
                var opt1 = d[i - 1, j];
                if (s[i - 1] != ' ')
                    opt1++;

                // deletion of t
                var opt2 = d[i, j - 1];
                if (t[j - 1] != ' ')
                    opt2++;

                // swapping s to t
                var opt3 = d[i - 1, j - 1];
                if (t[j - 1] != s[i - 1])
                    opt3++;
                d[i, j] = Math.Min(Math.Min(opt1, opt2), opt3);
            }
        }

        return d[n, m];
    }

    private int LevenshteinDistanceKorean(string s, string t, JObject marketItems)
    {
        // NameData s 를 한글명으로 가져옴
        s = GetLocaleNameData(s, marketItems);

        // i18n korean edit distance algorithm
        s = $" {s.Replace("설계도", string.Empty).Replace(" ", string.Empty)}";
        t = $" {t.Replace("설계도", string.Empty).Replace(" ", string.Empty)}";

        var n = s.Length;
        var m = t.Length;
        var d = new int[n + 1, m + 1];

        if (n == 0 || m == 0)
            return n + m;
        int i, j;

        for (i = 1; i < s.Length; i++)
            d[i, 0] = i * 9;

        for (j = 1; j < t.Length; j++)
            d[0, j] = j * 9;

        Span<int> a = stackalloc int[3];
        Span<int> b = stackalloc int[3];

        for (i = 1; i < s.Length; i++)
        {
            for (j = 1; j < t.Length; j++)
            {
                var s1 = 0;
                var s2 = 0;

                var cha = s[i];
                var chb = t[j];
                a.Clear();
                b.Clear();
                a[0] = (((cha - 0xAC00) - (cha - 0xAC00) % 28) / 28) / 21;
                a[1] = (((cha - 0xAC00) - (cha - 0xAC00) % 28) / 28) % 21;
                a[2] = (cha - 0xAC00) % 28;

                b[0] = (((chb - 0xAC00) - (chb - 0xAC00) % 28) / 28) / 21;
                b[1] = (((chb - 0xAC00) - (chb - 0xAC00) % 28) / 28) % 21;
                b[2] = (chb - 0xAC00) % 28;

                if (!a.SequenceEqual(b))
                {
                    s1 = 9;
                }
                else
                {
                    for (var k = 0; k < 3; k++)
                    {
                        if (a[k] == b[k])
                            continue;

                        if (GroupEquals(Korean[k], a[k], b[k]))
                            s2 += 1;
                        else
                            s1 += 1;
                    }

                    s1 *= 3;
                    s2 *= 2;
                }

                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 9, d[i, j - 1] + 9), d[i - 1, j - 1] + s1 + s2);
            }
        }

        return d[s.Length - 1, t.Length - 1];
    }

    private static bool GroupEquals(Dictionary<int, List<int>> group, int ak, int bk)
    {
        return group.Any(entry => entry.Value.Contains(ak) && entry.Value.Contains(bk));
    }

    private static string GetLocaleNameData(string s, JObject marketItems)
    {
        var localeName = string.Empty;
        foreach (var marketItem in marketItems)
        {
            if (marketItem.Key == "version")
                continue;
            var split = marketItem.Value.ToString().Split('|');
            if (split[0] == s)
            {
                var splitIndex = split.Length == 3 ? 2 : 0;
                localeName = split[splitIndex];
                break;
            }
        }

        return localeName;
    }

    private bool IsKorean(string str)
    {
        var c = str[0];
        if (0x1100 <= c && c <= 0x11FF) return true;
        if (0x3130 <= c && c <= 0x318F) return true;
        if (0xAC00 <= c && c <= 0xD7A3) return true;
        return false;
    }
}
