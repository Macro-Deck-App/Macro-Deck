using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Backups;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class UpdateBackupSettingsRequestMessageHandler
	: IUiTransportMessageHandler<UpdateBackupSettingsRequest, UpdateBackupSettingsResponse>
{
	private readonly IAppPreferenceService _preferences;

	public UpdateBackupSettingsRequestMessageHandler(IAppPreferenceService preferences) => _preferences = preferences;

	public async ValueTask<UpdateBackupSettingsResponse> Handle(
		UpdateBackupSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var settings = await _preferences.SetBackups(request.ScheduleFrequency,
			request.ScheduleTimeOfDay,
			request.ScheduleDayOfWeek,
			request.ScheduleDayOfMonth,
			request.RetentionPolicy,
			request.RetentionKeepLatest,
			request.BeforeHostUpdate,
			request.BeforePluginUpdate);

		var support = BackupDtoMapper.PreUpdateBackupSupport();

		return new UpdateBackupSettingsResponse
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
