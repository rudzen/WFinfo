namespace WFInfo.Models.Equipment;

public sealed record Part
{
    public int Owned { get; init; }
    public bool Vaulted { get; init; }
    public int Count { get; init; }
    public int Ducats { get; init; }
}
