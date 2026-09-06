using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.System;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetLockStateRequestMessageHandler
	: IUiTransportMessageHandler<GetLockStateRequest, GetLockStateResponse>
{
	private readonly IHostLockState _lockState;
	private readonly IAppPreferenceService _preferences;

	public GetLockStateRequestMessageHandler(IHostLockState lockState, IAppPreferenceService preferences)
	{
		_lockState = lockState;
		_preferences = preferences;
	}

	public async ValueTask<GetLockStateResponse> Handle(
		GetLockStateRequest request,
		CancellationToken cancellationToken)
	{
		var lockScreen = await _preferences.GetLockScreen();

		return new GetLockStateResponse
		{
			Locked = _lockState.IsLocked,
			LockScreenEnabled = lockScreen.Enabled,
			Supported = _lockState.IsSupported
		};
	}
}
