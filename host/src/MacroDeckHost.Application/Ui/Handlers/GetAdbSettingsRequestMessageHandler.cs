using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetAdbSettingsRequestMessageHandler
	: IUiTransportMessageHandler<GetAdbSettingsRequest, GetAdbSettingsResponse>
{
	private readonly IAppPreferenceService _preferences;
	private readonly IAdbManager _adbManager;
	private readonly IHostListenerState _listenerState;

	public GetAdbSettingsRequestMessageHandler(IAppPreferenceService preferences,
		IAdbManager adbManager,
		IHostListenerState listenerState)
	{
		_preferences = preferences;
		_adbManager = adbManager;
		_listenerState = listenerState;
	}

	public async ValueTask<GetAdbSettingsResponse> Handle(GetAdbSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var view = AdbSettingsProjection.From(await _preferences.GetAdb(),
			_adbManager.Status,
			_adbManager.Devices,
			_listenerState.PublicPort);

		return new GetAdbSettingsResponse
		{
			Enabled = view.Enabled,
			ExecutablePath = view.ExecutablePath,
			ResolvedExecutablePath = view.ResolvedExecutablePath,
			ExecutableSource = view.ExecutableSource,
			AdbVersion = view.AdbVersion,
			ServerReachable = view.ServerReachable,
			ServerStartedByMacroDeck = view.ServerStartedByMacroDeck,
			UsbConnectionsEnabled = view.UsbConnectionsEnabled,
			DefaultDeviceSerial = view.DefaultDeviceSerial,
			ActivePublicPort = view.ActivePublicPort,
			DeviceSidePortCandidates = AdbUsbTunnelPorts.DeviceSideCandidates,
			Devices = view.Devices,
			LastError = view.LastError,
			LastErrorAt = view.LastErrorAt,
			PreviousShutdownWasUnclean = view.PreviousShutdownWasUnclean,
			StaleTunnelsCleaned = view.StaleTunnelsCleaned,
			Supported = view.Supported,
			UnsupportedReason = view.UnsupportedReason
		};
	}
}
