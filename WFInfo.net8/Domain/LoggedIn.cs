using Mediator;

namespace WFInfo.Domain;

// from login window -> main
public sealed record StartLoggedInTimer(string Email) : INotification;

// from main -> main window
public sealed record LoggedIn(string Email) : INotification;

