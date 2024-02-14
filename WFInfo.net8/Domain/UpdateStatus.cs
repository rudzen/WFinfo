﻿using Mediator;

namespace WFInfo.Domain;

public sealed record UpdateStatus(string Message, int Severity) : INotification;
