using Mediator;

namespace WFInfo.Domain;

public sealed record ThemeAdjusterShow : INotification
{
    public static ThemeAdjusterShow Instance { get; } = new();
}
