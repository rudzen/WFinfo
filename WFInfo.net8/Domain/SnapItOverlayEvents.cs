using System.Drawing;
using Mediator;

namespace WFInfo.Domain;

public sealed record SnapItOverlayUpdate(Bitmap Image, Rectangle Window, double DpiScaling) : INotification;

public sealed record SnapItOverloadDisposeImage : INotification
{
    public static SnapItOverloadDisposeImage Instance => new();
}
