using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Backups;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class TriggerBeforeHostUpdateBackupRequestMessageHandler
	: IUiTransportMessageHandler<TriggerBeforeHostUpdateBackupRequest, TriggerBeforeHostUpdateBackupResponse>
{
	private readonly IPreUpdateBackupCoordinator _coordinator;

	public TriggerBeforeHostUpdateBackupRequestMessageHandler(IPreUpdateBackupCoordinator coordinator)
		=> _coordinator = coordinator;

	public async ValueTask<TriggerBeforeHostUpdateBackupResponse> Handle(
		TriggerBeforeHostUpdateBackupRequest request,
		CancellationToken cancellationToken)
	{
		var outcome = await _coordinator.CreateBeforeHostUpdate(request.Version, cancellationToken);

		return new TriggerBeforeHostUpdateBackupResponse
		{
			Success = outcome.Success,
			Skipped = outcome.Skipped,
			Reason = outcome.Reason,
			BackupId = outcome.BackupId
		};
	}
}
