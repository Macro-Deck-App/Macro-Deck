using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Backups.Storage;
using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Backups;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class GetBackupsRequestMessageHandler : IUiTransportMessageHandler<GetBackupsRequest, GetBackupsResponse>
{
	private readonly IBackupService _backupService;
	private readonly IBackupStorageRegistry _storageRegistry;
	private readonly IAppPreferenceService _preferences;
	private readonly IRestoreService _restoreService;
	private readonly IApplicationRestartService _restartService;

	public GetBackupsRequestMessageHandler(IBackupService backupService,
		IBackupStorageRegistry storageRegistry,
		IAppPreferenceService preferences,
		IRestoreService restoreService,
		IApplicationRestartService restartService)
	{
		_backupService = backupService;
		_storageRegistry = storageRegistry;
		_preferences = preferences;
		_restoreService = restoreService;
		_restartService = restartService;
	}

	public async ValueTask<GetBackupsResponse> Handle(GetBackupsRequest request, CancellationToken cancellationToken)
	{
		var result = await _backupService.List(cancellationToken);
		var backups = result.Success ? result.Data! : [];
		var settings = await _preferences.GetBackups();
		var pending = _restoreService.Pending;

		return new GetBackupsResponse
		{
			Backups = [.. backups.Select(ToSummary)],
			RetentionKeepLatest = settings.RetentionKeepLatest,
			PendingRestore = pending is null
				? null
				: new PendingRestoreSummary
				{
					RestoreId = pending.RestoreId,
					BackupId = pending.BackupId,
					StagedAt = pending.StagedAt,
					Components = BackupDtoMapper.ToNames(pending.Components),
					RestartSupported = _restartService.Availability.Supported,
					RestartUnavailableReason = _restartService.Availability.Reason
				}
		};
	}

	private BackupSummary ToSummary(BackupDescriptor descriptor)
		=> BackupDtoMapper.ToSummary(descriptor, DisplayName(descriptor.ProviderId));

	private string DisplayName(string providerId)
		=> _storageRegistry.Find(providerId)?.DisplayName ?? providerId;
}
