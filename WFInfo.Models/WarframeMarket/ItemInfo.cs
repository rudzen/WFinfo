using System.Text.Json.Serialization;

namespace WFInfo.Models.WarframeMarket;

// https://api.warframe.market/v1/items/mirage_prime_systems

// root
public class ItemInfo
{
    [JsonPropertyName("payload")]
    public ItemInfoPayload payload { get; set; }
}

public class ItemInfoPayload
{
    [JsonPropertyName("item")]
    public Item item { get; set; }
}

public class Item
{
    [JsonPropertyName("id")]
    public string id { get; set; }

    [JsonPropertyName("items_in_set")]
    public Items_in_set[] items_in_set { get; set; }
}

public interface ILanguageSpecificItem
{
    [JsonPropertyName("item_name")]
    string item_name { get; set; }

    [JsonPropertyName("description")]
    string description { get; set; }

    [JsonPropertyName("wiki_link")]
    public string wiki_link { get; set; }

    [JsonPropertyName("icon")]
    public string icon { get; set; }

    [JsonPropertyName("thumb")]
    public string thumb { get; set; }

    [JsonPropertyName("drop")]
    public object[] drop { get; set; }
}

public class Items_in_set
{
    [JsonPropertyName("item")]
    public string id { get; set; }

    [JsonPropertyName("item")]
    public int mastery_level { get; set; }

    [JsonPropertyName("item")]
    public bool set_root { get; set; }

    [JsonPropertyName("item")]
    public string icon { get; set; }

    [JsonPropertyName("item")]
    public string[] tags { get; set; }

    [JsonPropertyName("item")]
    public int quantity_for_set { get; set; }

    [JsonPropertyName("item")]
    public int ducats { get; set; }

    [JsonPropertyName("item")]
    public string sub_icon { get; set; }

    [JsonPropertyName("item")]
    public string thumb { get; set; }

    [JsonPropertyName("item")]
    public string icon_format { get; set; }

    [JsonPropertyName("item")]
    public int trading_tax { get; set; }

    [JsonPropertyName("item")]
    public string url_name { get; set; }

    public Dictionary<string, ILanguageSpecificItem> language_specific_items { get; set; }
}

public class En : ILanguageSpecificItem
{
    public string item_name { get; set; }
    public string description { get; set; }
    public string wiki_link { get; set; }
    public string thumb { get; set; }
    public string icon { get; set; }
    public object[] drop { get; set; }
}

public class Ru : ILanguageSpecificItem
{
    public string item_name { get; set; }
    public string description { get; set; }
    public string wiki_link { get; set; }
    public string icon { get; set; }
    public string thumb { get; set; }
    public object[] drop { get; set; }
}

public class Ko : ILanguageSpecificItem
{
    public string item_name { get; set; }
    public string description { get; set; }
    public string wiki_link { get; set; }
    public string icon { get; set; }
    public string thumb { get; set; }
    public object[] drop { get; set; }
}

public class Fr : ILanguageSpecificItem
{
    public string item_name { get; set; }
    public string description { get; set; }
    public string wiki_link { get; set; }
    public string icon { get; set; }
    public string thumb { get; set; }
    public object[] drop { get; set; }
}

public class Sv : ILanguageSpecificItem
{
    public string item_name { get; set; }
    public string description { get; set; }
    public string wiki_link { get; set; }
    public string thumb { get; set; }
    public string icon { get; set; }
    public object[] drop { get; set; }
}

public class De : ILanguageSpecificItem
{
    public string item_name { get; set; }
    public string description { get; set; }
    public string wiki_link { get; set; }
    public string icon { get; set; }
    public string thumb { get; set; }
    public object[] drop { get; set; }
}

public class Zh_hant : ILanguageSpecificItem
{
    public string item_name { get; set; }
    public string description { get; set; }
    public string wiki_link { get; set; }
    public string icon { get; set; }
    public string thumb { get; set; }
    public object[] drop { get; set; }
}

public class Zh_hans : ILanguageSpecificItem
{
    public string item_name { get; set; }
    public string description { get; set; }
    public string wiki_link { get; set; }
    public string icon { get; set; }
    public string thumb { get; set; }
    public object[] drop { get; set; }
}

public class Pt : ILanguageSpecificItem
{
    public string item_name { get; set; }
    public string description { get; set; }
    public string wiki_link { get; set; }
    public string icon { get; set; }
    public string thumb { get; set; }
    public object[] drop { get; set; }
}

public class Es : ILanguageSpecificItem
{
    public string item_name { get; set; }
    public string description { get; set; }
    public string wiki_link { get; set; }
    public string icon { get; set; }
    public string thumb { get; set; }
    public object[] drop { get; set; }
}

public class Pl : ILanguageSpecificItem
{
    public string item_name { get; set; }
    public string description { get; set; }
    public string wiki_link { get; set; }
    public string icon { get; set; }
    public string thumb { get; set; }
    public object[] drop { get; set; }
}

public class Cs : ILanguageSpecificItem
{
    public string item_name { get; set; }
    public string description { get; set; }
    public string wiki_link { get; set; }
    public string thumb { get; set; }
    public string icon { get; set; }
    public object[] drop { get; set; }
}

public class Uk : ILanguageSpecificItem
{
    public string item_name { get; set; }
    public string description { get; set; }
    public string wiki_link { get; set; }
    public string icon { get; set; }
    public string thumb { get; set; }
    public object[] drop { get; set; }
}
