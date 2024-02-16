using Newtonsoft.Json.Linq;

namespace WFInfo.Services;

public interface ILevenshteinDistanceService
{
    int LevenshteinDistanceSecond(string str1, string str2, int limit = -1);
    int LevenshteinDistance(string s, string t, string locale, JObject marketItems);
}
