using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WFInfo.Models.Market;

public class InfoToStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var jsonDoc = JsonDocument.ParseValue(ref reader);
        return jsonDoc.RootElement.GetRawText();
    }

    public override void Write(
        Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}

public sealed class MarketItem
{
    [JsonPropertyName("plat")]
    public decimal Plat { get; set; }

    [JsonPropertyName("ducats")]
    public int Ducats { get; set; }

    [JsonPropertyName("volume")]
    public int Volume { get; set; }
}

public sealed class MarketData
{
    public Dictionary<string, MarketItem> Items { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTime TimeStamp { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; }
    
    public static MarketData? Create(string json)
    {
        var options = new JsonSerializerOptions
        {
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
        options.Converters.Add(new InfoToStringConverter());
        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json, options);

        if (raw is null)
            return null;

        var items = new Dictionary<string, MarketItem>(raw.Count);
        var version = string.Empty;
        var timestamp = DateTime.MinValue;

        foreach (var (key, value) in raw)
        {
            switch (key)
            {
                case "version":
                    version = value.AsSpan().Trim().Trim('"').ToString();
                    break;
                case "timestamp":
                {
                    var v = value.AsSpan().Trim().Trim('"');
                    var offset = DateTimeOffset.Parse(v, formatProvider: default);
                    timestamp = offset.DateTime;
                    break;
                }
                default:
                {
                    var item = JsonSerializer.Deserialize<MarketItem>(value, options);

                    if (item is null)
                        continue;

                    items.Add(key, item);
                    break;
                }
            }
        }

        return new MarketData
        {
            Items = items,
            TimeStamp = timestamp,
            Version = version
        };
    }
};