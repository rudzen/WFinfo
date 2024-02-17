using System.Drawing;
using Mediator;

namespace WFInfo.Domain;

public sealed record FullscreenReminderShow(Point Xy, Point Hw) : INotification;
