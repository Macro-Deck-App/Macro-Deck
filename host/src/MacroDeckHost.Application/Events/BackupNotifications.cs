using MacroDeckHost.Application.Backups;
using Mediator;

namespace MacroDeckHost.Application.Events;

public sealed record BackupOperationProgressNotification(BackupOperationStatus Status) : INotification;

public sealed record BackupListChangedNotification(string Reason) : INotification;

public sealed record BackupRecoveryKeyStateChangedNotification(BackupRecoveryKeyState State) : INotification;

public sealed record RestorePendingChangedNotification(PendingRestoreInfo? Pending, bool RestartSupported)
	: INotification;
