using Mediator;

namespace WFInfo.Domain;

public sealed record GnfWarningShow(bool Show) : INotification;
