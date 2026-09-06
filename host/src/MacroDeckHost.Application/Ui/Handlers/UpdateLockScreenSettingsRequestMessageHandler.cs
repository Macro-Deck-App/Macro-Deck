using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using Mediator;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateLockScreenSettingsRequestMessageHandler
	: IUiTransportMessageHandler<UpdateLockScreenSettingsRequest, UpdateLockScreenSettingsResponse>
{
	private readonly IAppPreferenceService _service;
	private readonly IHostLockState _lockState;
	private readonly IMediator _mediator;

	public UpdateLockScreenSettingsRequestMessageHandler(
		IAppPreferenceService service,
		IHostLockState lockState,
		IMediator mediator)
	{
		_service = service;
		_lockState = lockState;
		_mediator = mediator;
	}

	public async ValueTask<UpdateLockScreenSettingsResponse> Handle(
		UpdateLockScreenSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var settings = await _service.SetLockScreen(request.Enabled);

		await _mediator.Publish(
			new HostLockStateChangedNotification(_lockState.IsLocked, settings.Enabled, _lockState.IsSupported),
			cancellationToken);

		return new UpdateLockScreenSettingsResponse { Enabled = settings.Enabled };
	}
}
