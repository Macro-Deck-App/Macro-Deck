using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public class DownloadAdbPlatformToolsRequestMessageHandler
	: IUiTransportMessageHandler<DownloadAdbPlatformToolsRequest, DownloadAdbPlatformToolsResponse>
{
	private readonly IAppPreferenceService _preferences;
	private readonly IAdbManager _adbManager;
	private readonly IAdbPlatformToolsInstaller _installer;
	private readonly IHostListenerState _listenerState;

	public DownloadAdbPlatformToolsRequestMessageHandler(IAppPreferenceService preferences,
		IAdbManager adbManager,
		IAdbPlatformToolsInstaller installer,
		IHostListenerState listenerState)
	{
		_preferences = preferences;
		_adbManager = adbManager;
		_installer = installer;
		_listenerState = listenerState;
	}

	public async ValueTask<DownloadAdbPlatformToolsResponse> Handle(DownloadAdbPlatformToolsRequest request,
		CancellationToken cancellationToken)
	{
		var result = await _installer.InstallAsync(cancellationToken);
		if (!result.Success)
		{
			return Respond(await _preferences.GetAdb(), false, result.ErrorMessage);
		}

		var current = await _preferences.GetAdb();
		var updated = await _preferences.SetAdb(current.Enabled,
			result.Data,
			current.UsbConnectionsEnabled,
			current.DefaultDeviceSerial);

		await _adbManager.ApplySettingsAsync(cancellationToken);

		return Respond(updated, true, null);
	}

	private DownloadAdbPlatformToolsResponse Respond(AdbSettings settings, bool success, string? error)
	{
		var view = AdbSettingsProjection.From(settings,
			_adbManager.Status,
			_adbManager.Devices,
			_listenerState.PublicPort);

		return new DownloadAdbPlatformToolsResponse
		{
			Success = success,
			Error = error,
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
