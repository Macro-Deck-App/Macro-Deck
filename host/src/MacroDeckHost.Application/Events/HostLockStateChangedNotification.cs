using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record HostLockStateChangedNotification(bool Locked, bool LockScreenEnabled, bool Supported)
	: INotification;
