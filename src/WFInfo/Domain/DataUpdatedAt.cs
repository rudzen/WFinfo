using Mediator;

namespace WFInfo.Domain;

public sealed record DataUpdatedAt(
    string Date,
    DataTypes Type
) : INotification;
