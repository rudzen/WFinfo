using Mediator;

namespace WFInfo.Domain;

public sealed record WebSocketAliveStatusRequest(DateTime RequestedAt) : IRequest<WebSocketAliveStatusResponse>;

public sealed record WebSocketAliveStatusResponse(bool IsAlive);

public sealed record WebSocketSetStatus(string Status) : INotification;
