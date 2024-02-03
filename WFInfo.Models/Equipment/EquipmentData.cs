namespace WFInfo.Models.Equipment;

public sealed record EquipmentData(DateTime Timestamp, Dictionary<string, EquipmentItem> Items);
