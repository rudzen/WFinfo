using Mediator;

namespace WFInfo.Domain;

public sealed record AutoCountShow : INotification
{
    public static AutoCountShow Instance { get; } = new();
};
