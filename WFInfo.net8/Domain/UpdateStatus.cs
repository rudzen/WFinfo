using Mediator;

namespace WFInfo.Domain;

public enum StatusSeverity
{
    None = 0,
    Error = 1,
    Warning = 2
}

public sealed record UpdateStatus(string Message, StatusSeverity Severity = StatusSeverity.None) : INotification;
