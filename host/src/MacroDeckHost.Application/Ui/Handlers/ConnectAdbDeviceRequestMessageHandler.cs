using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public class ConnectAdbDeviceRequestMessageHandler
	: IUiTransportMessageHandler<ConnectAdbDeviceRequest, ConnectAdbDeviceResponse>
{
	private readonly IAppPreferenceService _preferences;
	private readonly IAdbManager _adbManager;
	private readonly IHostListenerState _listenerState;
	private readonly IAdbDeviceOperations _operations;

	public ConnectAdbDeviceRequestMessageHandler(IAppPreferenceService preferences,
		IAdbManager adbManager,
		IHostListenerState listenerState,
		IAdbDeviceOperations operations)
	{
		_operations = operations;
		_preferences = preferences;
		_adbManager = adbManager;
		_listenerState = listenerState;
	}

	public async ValueTask<ConnectAdbDeviceResponse> Handle(ConnectAdbDeviceRequest request,
		CancellationToken cancellationToken)
	{
		var result = await _operations.ConnectAsync((request.Address ?? string.Empty).Trim(), cancellationToken);

		var view = AdbSettingsProjection.From(await _preferences.GetAdb(),
			_adbManager.Status,
			_adbManager.Devices,
			_listenerState.PublicPort);

		return new ConnectAdbDeviceResponse
		{
			Success = result.Success,
			Error = result.Success ? null : result.ErrorMessage,
			ErrorCode = result.Success ? null : result.Error?.ToString(),
			Enabled = view.Enabled,
			ExecutablePath = view.ExecutablePath,
			ResolvedExecutablePath = view.ResolvedExecutablePath,
			ExecutableSource = view.ExecutableSource,
			AdbVersion = view.AdbVersion,
			ServerReachable = view.ServerReachable,
			ServerStartedByMacroDeck = view.ServerStartedByMacroDeck,
			UsbConnectionsEnabled = view.UsbConnectionsEnabled,
			DefaultDeviceSerial = view.DefaultDeviceSerial,
			StopServerOnExit = view.StopServerOnExit,
			AllowPlugins = view.AllowPlugins,
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
