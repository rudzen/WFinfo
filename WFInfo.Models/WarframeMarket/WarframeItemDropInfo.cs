namespace WFInfo.Models.WarframeMarket;

// https://api.warframe.market/v1/items/mirage_prime_systems/dropsources?include=item

// root
public class WarframeItemDropInfo
{
    public WarframeMarketDropSourcePayload payload { get; set; }
    public Include include { get; set; }
}

public class WarframeMarketDropSourcePayload
{
    public Dropsources[] dropsources { get; set; }
}

public class Dropsources
{
    public string type { get; set; }
    public string item { get; set; }
    public string relic { get; set; }
    public Rates rates { get; set; }
    public string rarity { get; set; }
    public string id { get; set; }
}

public class Rates
{
    public int intact { get; set; }
    public int exceptional { get; set; }
    public int flawless { get; set; }
    public int radiant { get; set; }
}

public class Include
{
    public Item item { get; set; }
}
