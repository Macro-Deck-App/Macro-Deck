using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Backups;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class GetBackupSettingsRequestMessageHandler
	: IUiTransportMessageHandler<GetBackupSettingsRequest, GetBackupSettingsResponse>
{
	private readonly IAppPreferenceService _preferences;

	public GetBackupSettingsRequestMessageHandler(IAppPreferenceService preferences) => _preferences = preferences;

	public async ValueTask<GetBackupSettingsResponse> Handle(
		GetBackupSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var settings = await _preferences.GetBackups();
		var support = BackupDtoMapper.PreUpdateBackupSupport();

		return new GetBackupSettingsResponse
		{
			ScheduleFrequency = settings.ScheduleFrequency,
			ScheduleTimeOfDay = settings.ScheduleTimeOfDay,
			ScheduleDayOfWeek = settings.ScheduleDayOfWeek.ToString(),
			ScheduleDayOfMonth = settings.ScheduleDayOfMonth,
			RetentionPolicy = settings.RetentionPolicy,
			RetentionKeepLatest = settings.RetentionKeepLatest,
			BeforeHostUpdate = settings.BeforeHostUpdate,
			BeforePluginUpdate = settings.BeforePluginUpdate,
			PreUpdateBackupSupported = support.Supported,
			PreUpdateBackupUnsupportedReason = support.Reason,
			MinimumRetentionCount = AppPreferenceService.MinBackupRetentionKeepLatest,
			MaximumRetentionCount = AppPreferenceService.MaxBackupRetentionKeepLatest,
			NextScheduledAt = null
		};
	}
}
