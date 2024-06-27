using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WFInfo.Models.Market;

public sealed class MarketData
{
    [JsonPropertyName("items")]
    public IDictionary<string, MarketItem> Items { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime TimeStamp { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; }

    public void Clear()
    {
        Items.Clear();
        TimeStamp = default;
        Version = string.Empty;
    }
}

public sealed class MarketItem
{
    [JsonPropertyName("plat")]
    public double Plat { get; set; }

    [JsonPropertyName("ducats")]
    public int Ducats { get; set; }

    [JsonPropertyName("volume")]
    public int Volume { get; set; }
}

/// <summary>
/// This is used to read data from the wf market
/// </summary>
/// <param name="Name"></param>
/// <param name="YesterdaysVolume"></param>
/// <param name="TodayVol"></param>
/// <param name="CustomAvg"></param>
public sealed record MarketPrice(
    [property:JsonPropertyName("name")] string Name,
    [property:JsonPropertyName("yesterday_vol")] int YesterdaysVolume,
    [property:JsonPropertyName("today_vol")] int TodayVol,
    [property:JsonPropertyName("custom_avg")] decimal CustomAvg
);
