using System.Text.Json.Serialization;

namespace WFInfo.Services.WarframeMarket.Models;

public sealed record MarketItems([property:JsonPropertyName("payload")] Payload payload)
{
    [JsonPropertyName("timestamp")]
    public DateTime TimeStamp { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; set; }

    public static MarketItems Create(MarketItems marketItems, DateTime timeStamp, Predicate<Items> predicate)
    {
        var items = marketItems.payload.items.Where(x => predicate(x)).ToList();
        return new MarketItems(new Payload(items))
        {
            TimeStamp = timeStamp
        };
    }

};

public sealed record Payload([property:JsonPropertyName("items")] List<Items> items);

public sealed class Items(
    string thumb,
    string id,
    string url_name,
    string item_name,
    bool vaulted
)
{
    [JsonPropertyName("thumb")]
    public string thumb { get; init; } = thumb;

    [JsonPropertyName("id")]
    public string id { get; init; } = id;

    [JsonPropertyName("url_name")]
    public string url_name { get; init; } = url_name;

    [JsonPropertyName("item_name")]
    public string item_name { get; init; } = item_name;

    [JsonPropertyName("vaulted")]
    public bool vaulted { get; init; } = vaulted;

    public void Deconstruct(
        out string thumb, out string id, out string url_name, out string item_name, out bool vaulted)
    {
        thumb = this.thumb;
        id = this.id;
        url_name = this.url_name;
        item_name = this.item_name;
        vaulted = this.vaulted;
    }
}
