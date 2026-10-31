using MacroDeck.Localization;
using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Backups;
using Serilog;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class TriggerBeforeHostUpdateBackupRequestMessageHandler
	: IUiTransportMessageHandler<TriggerBeforeHostUpdateBackupRequest, TriggerBeforeHostUpdateBackupResponse>
{
	private readonly IPreUpdateBackupCoordinator _coordinator;
	private readonly IAppPreferenceService _preferences;
	private readonly ILocalizationResolver _localization;

	public TriggerBeforeHostUpdateBackupRequestMessageHandler(
		IPreUpdateBackupCoordinator coordinator,
		IAppPreferenceService preferences,
		ILocalizationResolver localization)
	{
		_coordinator = coordinator;
		_preferences = preferences;
		_localization = localization;
	}

	public async ValueTask<TriggerBeforeHostUpdateBackupResponse> Handle(
		TriggerBeforeHostUpdateBackupRequest request,
		CancellationToken cancellationToken)
	{
		var outcome = await _coordinator.CreateBeforeHostUpdate(request.Version, cancellationToken);

		return new TriggerBeforeHostUpdateBackupResponse
		{
			Success = outcome.Success,
			Skipped = outcome.Skipped,
			Cancelled = outcome.Cancelled,
			Reason = await UserFacingReason(outcome),
			BackupId = outcome.BackupId
		};
	}

	private async Task<string?> UserFacingReason(PreUpdateBackupOutcome outcome)
	{
		if (outcome.Success || outcome.Cancelled || outcome.Error is not { } error)
		{
			return outcome.Reason;
		}

		Log.ForContext<TriggerBeforeHostUpdateBackupRequestMessageHandler>()
			.Warning("The backup before the update failed ({Error}): {Reason}", error, outcome.Reason);

		var culture = (await _preferences.GetLocalization()).Culture;

		return _localization.Resolve(BackupDtoMapper.DefaultMessage(error), culture);
	}
}
