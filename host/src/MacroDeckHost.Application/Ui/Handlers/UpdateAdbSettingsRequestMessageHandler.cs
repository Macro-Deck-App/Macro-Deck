using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateAdbSettingsRequestMessageHandler
	: IUiTransportMessageHandler<UpdateAdbSettingsRequest, UpdateAdbSettingsResponse>
{
	private readonly IAppPreferenceService _preferences;
	private readonly IAdbManager _adbManager;
	private readonly IHostListenerState _listenerState;

	public UpdateAdbSettingsRequestMessageHandler(IAppPreferenceService preferences,
		IAdbManager adbManager,
		IHostListenerState listenerState)
	{
		_preferences = preferences;
		_adbManager = adbManager;
		_listenerState = listenerState;
	}

	public async ValueTask<UpdateAdbSettingsResponse> Handle(UpdateAdbSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var current = await _preferences.GetAdb();

		var trimmedPath = request.ExecutablePath?.Trim();
		if (!string.IsNullOrEmpty(trimmedPath) && !File.Exists(trimmedPath))
		{
			return Respond(current, false, $"The adb executable was not found at '{trimmedPath}'.");
		}

		// An unknown default device serial is never rejected: adb may be off, or the phone simply
		// unplugged, and refusing would make the field unusable in exactly the normal case.
		//
		// Omitted fields keep their stored value. The request is all-nullable, so treating a missing
		// field as "write the default" would make a caller that sends only the field it changed
		// silently switch ADB off. An empty string still clears the two text fields, because SetAdb
		// normalizes blank to null.
		var updated = await _preferences.SetAdb(request.Enabled ?? current.Enabled,
			request.ExecutablePath ?? current.ExecutablePath,
			request.UsbConnectionsEnabled ?? current.UsbConnectionsEnabled,
			request.DefaultDeviceSerial ?? current.DefaultDeviceSerial);

		await _adbManager.ApplySettingsAsync(cancellationToken);

		return Respond(updated, true, null);
	}

	private UpdateAdbSettingsResponse Respond(AdbSettings settings, bool success, string? error)
	{
		var view = AdbSettingsProjection.From(settings,
			_adbManager.Status,
			_adbManager.Devices,
			_listenerState.PublicPort);

		return new UpdateAdbSettingsResponse
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
