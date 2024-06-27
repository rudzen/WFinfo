using AutoUpdaterDotNET;
using Mediator;

namespace WFInfo.Domain;

public sealed record UpdateWindowShow(UpdateInfoEventArgs UpdateInfoEventArgs) : INotification;

public sealed record DownloadUpdate(UpdateInfoEventArgs UpdateInfoEventArgs) : INotification;
