using System.Drawing;

namespace WFInfo.Services.OpticalCharacterRecognition;

public sealed record FoundItem(List<InventoryItem> Items, Rectangle Rectangle);
