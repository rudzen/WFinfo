// using System.Collections.Frozen;
// using System.Text.Json;
//
// namespace WFInfo.Models.Market;
//
// public sealed record MarketItem(string Id, string Name, string Code, string DisplayName);
//
// public sealed record MarketItems
// {
//     public FrozenDictionary<string, MarketItem> Data { get; init; }
//
//     public MarketItems(string json)
//     {
//         Data = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
//                              .ToFrozenDictionary(
//                                  pair => pair.Key,
//                                  pair => new MarketItem(pair.Key, pair.Value.Split('|')[0], pair.Value.Split('|')[1], pair.Value.Split('|')[2])
//                              );
//     }
//
//     public override string ToString()
//     {
//         var dataAsString = Data.ToDictionary(
//             pair => pair.Key,
//             pair => $"{pair.Value.Name}|{pair.Value.Code}|{pair.Value.DisplayName}"
//         );
//
//         return JsonSerializer.Serialize(dataAsString);
//     }
// }