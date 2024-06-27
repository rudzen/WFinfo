using System.Text.Json.Serialization;

namespace WFInfo.Models.WarframeMarket;

// https://api.warframe.market/v1/items

public sealed class WarframeMarketItems
{
    [JsonPropertyName("payload")]
    public Payload payload { get; set; }
}

public class Payload
{
    [JsonPropertyName("items")]
    public Items[] items { get; set; }
}

public class Items
{
    [JsonPropertyName("id")]
    public string id { get; set; }

    [JsonPropertyName("thumb")]
    public string thumb { get; set; }

    [JsonPropertyName("url_name")]
    public string url_name { get; set; }

    [JsonPropertyName("item_name")]
    public string item_name { get; set; }

    [JsonPropertyName("vaulted")]
    public bool vaulted { get; set; }
}

/*
CREATE TABLE WarframeMarketItems (
    payload_id INT,
    PRIMARY KEY (payload_id),
    FOREIGN KEY (payload_id) REFERENCES Payload(id)
);

CREATE TABLE Payload (
    id INT AUTO_INCREMENT,
    PRIMARY KEY (id)
);

CREATE TABLE Items (
    id VARCHAR(255),
    payload_id INT,
    thumb VARCHAR(255),
    url_name VARCHAR(255),
    item_name VARCHAR(255),
    vaulted BOOLEAN,
    PRIMARY KEY (id),
    FOREIGN KEY (payload_id) REFERENCES Payload(id)
);
*/
