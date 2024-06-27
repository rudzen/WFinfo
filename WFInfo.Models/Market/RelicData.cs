using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WFInfo.Models.Market;

using System;

public enum RelicType
{
    Lith,
    Meso,
    Neo,
    Axi
}

public partial class RelicData
{
    [JsonPropertyName("errors")]
    public string[] Errors { get; set; } = [];

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("Axi")]
    public Dictionary<string, Relic> Axi { get; set; } = new();

    [JsonPropertyName("Lith")]
    public Dictionary<string, Relic> Lith { get; set; } = new();

    [JsonPropertyName("Meso")]
    public Dictionary<string, Relic> Meso { get; set; } = new();

    [JsonPropertyName("Neo")]
    public Dictionary<string, Relic> Neo { get; set; } = new();

    [JsonIgnore]
    private FrozenDictionary<string, Dictionary<string, Relic>> _relics =>
        new Dictionary<string, Dictionary<string, Relic>>()
        {
            { "Lith", Lith },
            { "Meso", Meso },
            { "Neo", Neo },
            { "Axi", Axi }
        }.ToFrozenDictionary();

    [JsonIgnore]
    public Dictionary<string, Relic> this[string key] => _relics[key];

    public bool TryGetValue(string era, string relicName, out Relic relic)
    {
        return _relics[era].TryGetValue(relicName, out relic);
    }
}

public partial class Relic
{
    [JsonPropertyName("vaulted")]
    public bool Vaulted { get; set; }

    [JsonPropertyName("rare1")]
    public string Rare1 { get; set; }

    [JsonPropertyName("uncommon1")]
    public string Uncommon1 { get; set; }

    [JsonPropertyName("uncommon2")]
    public string Uncommon2 { get; set; }

    [JsonPropertyName("common1")]
    public string Common1 { get; set; }

    [JsonPropertyName("common2")]
    public string Common2 { get; set; }

    [JsonPropertyName("common3")]
    public string Common3 { get; set; }
}

public partial class RelicData
{
    public static RelicData FromJson(string json) => JsonSerializer.Deserialize<RelicData>(json, Converter.Options);
}

public static class Serialize
{
    public static string ToJson(this RelicData self) => JsonSerializer.Serialize(self, Converter.Options);
}

internal static class Converter
{
    public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };
}