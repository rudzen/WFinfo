using Mediator;

namespace WFInfo.Domain;

public sealed record WarframeMarketStatusUpdate(string Message) : INotification;

public sealed record WarframeMarketStatusAwayStatusResponse(bool IsAway);

public sealed record WarframeMarketStatusAwayStatusRequest(string Message) : IRequest<WarframeMarketStatusAwayStatusResponse>;

public sealed record WarframeMarketSignOut : INotification
{
    public static WarframeMarketSignOut Instance => new WarframeMarketSignOut();
}
