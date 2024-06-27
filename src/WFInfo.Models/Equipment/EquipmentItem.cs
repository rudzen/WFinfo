namespace WFInfo.Models.Equipment;

public sealed record EquipmentItem
{
    public bool Vaulted { get; init; }
    public string Type { get; init; }
    public bool Mastered { get; init; }
    public Dictionary<string, Part> Parts { get; init; }
}
