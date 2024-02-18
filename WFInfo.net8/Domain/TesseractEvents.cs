using Mediator;

namespace WFInfo.Domain;

public sealed record TesseractReloadEngines : INotification
{
    public static TesseractReloadEngines Instance => new();
}
