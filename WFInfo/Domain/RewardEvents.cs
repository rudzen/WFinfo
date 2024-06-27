using Mediator;

namespace WFInfo.Domain;

public sealed record LoadRewardTextData(
    string Name,
    string Plat,
    string PrimeSetPlat,
    string Ducats,
    string Volume,
    bool Vaulted,
    bool Mastered,
    string Owned,
    int PartNumber,
    bool Resize = true,
    bool HideReward = false
) : INotification;
