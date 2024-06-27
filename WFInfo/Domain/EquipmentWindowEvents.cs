using Mediator;

namespace WFInfo.Domain;

public sealed record EventWindowReloadItems : INotification
{
    public static EventWindowReloadItems Instance { get; } = new();
}
