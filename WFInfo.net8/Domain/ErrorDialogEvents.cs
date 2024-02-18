using Mediator;

namespace WFInfo.Domain;

public sealed record ErrorDialogShow(DateTime TimeStamp, int Gap = 30) : INotification;
