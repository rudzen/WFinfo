using System.Drawing;
using Mediator;

namespace WFInfo.Domain;

public enum OverlayUpdateType
{
    Owned,
    Ducat,
    Plat
}

public sealed record OverlayUpdate(int Index, OverlayUpdateType Tyoe) : INotification;

public sealed record OverlayUpdateData(
    int Index,
    string CorrectName,
    string Plat,
    string PrimeSetPlat,
    string Ducats,
    string Volume,
    bool Vaulted,
    bool Mastered,
    string PartsOwned,
    string PartsCount,
    bool HideRewardInfo,
    int OverWid,
    Point Position
) : INotification;
