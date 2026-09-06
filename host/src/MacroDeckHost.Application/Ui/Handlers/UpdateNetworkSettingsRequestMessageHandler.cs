using System.Globalization;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateNetworkSettingsRequestMessageHandler
	: IUiTransportMessageHandler<UpdateNetworkSettingsRequest, UpdateNetworkSettingsResponse>
{
	private readonly IAppPreferenceService _preferences;
	private readonly IApplicationRestartService _restart;
	private readonly IHostListenerState _listenerState;
	private readonly INetworkRestartNotifier _restartNotifier;

	public UpdateNetworkSettingsRequestMessageHandler(IAppPreferenceService preferences,
		IApplicationRestartService restart,
		IHostListenerState listenerState,
		INetworkRestartNotifier restartNotifier)
	{
		_preferences = preferences;
		_restart = restart;
		_listenerState = listenerState;
		_restartNotifier = restartNotifier;
	}

	public async ValueTask<UpdateNetworkSettingsResponse> Handle(
		UpdateNetworkSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var current = await _preferences.GetNetwork();

		var effectiveTlsEnabled = request.TlsEnabled ?? current.TlsEnabled;
		var effectiveTlsMode = request.TlsMode is not null
			? PublicTlsSelector.ParseMode(request.TlsMode)
			: current.TlsMode;

		var effectiveTlsHttpsPort = effectiveTlsMode == PublicTlsMode.Replace
			? current.TlsHttpsPort
			: request.TlsHttpsPort ?? current.TlsHttpsPort;

		// Rejected rather than silently corrected: a value that cannot be used would look saved here
		// and then fall back to a default on the next start, which is exactly the surprise the
		// setting is supposed to avoid. Everything is validated before anything is written.
		if (Reject(request.PublicPort, current, effectiveTlsEnabled, effectiveTlsMode, effectiveTlsHttpsPort)
			is { } error)
		{
			return NetworkSettingsResponseFactory.Create(current, _restart.Availability, false, error);
		}

		var unchanged = request.PublicPort == current.PublicPort &&
			effectiveTlsEnabled == current.TlsEnabled &&
			effectiveTlsMode == current.TlsMode &&
			effectiveTlsHttpsPort == current.TlsHttpsPort;

		var updated = unchanged
			? current
			: await _preferences.SetNetwork(request.PublicPort,
				effectiveTlsEnabled,
				effectiveTlsMode.ToString(),
				effectiveTlsHttpsPort);

		await _restartNotifier.Sync(cancellationToken);

		return NetworkSettingsResponseFactory.Create(updated, _restart.Availability, true, null);
	}

	private string? Reject(int publicPort,
		NetworkSettings current,
		bool effectiveTlsEnabled,
		PublicTlsMode effectiveTlsMode,
		int effectiveTlsHttpsPort)
	{
		if (!PublicPortSelector.IsConfigurable(publicPort))
		{
			return string.Format(CultureInfo.InvariantCulture,
				"Enter a port between {0} and {1}. Lower ports are reserved by the operating system.",
				PublicPortSelector.MinimumConfigurablePort,
				PublicPortSelector.MaximumConfigurablePort);
		}

		if (publicPort == _listenerState.LoopbackPort)
		{
			return "This port is already used by Macro Deck itself. Choose a different one.";
		}

		// Only the moment the user switches HTTPS on, not every save made while it happens to be on. HTTPS
		// is on by default now, so the wider check would reject an unrelated port change on any
		// installation that has no certificate yet - and an enabled listener with nothing to serve is
		// already reported through PublicTlsRejection.NoCertificate rather than hidden.
		if (effectiveTlsEnabled && !current.TlsEnabled && !current.TlsCertificateConfigured)
		{
			return "Add a certificate before turning HTTPS on.";
		}

		if (effectiveTlsMode != PublicTlsMode.Additional)
		{
			return null;
		}

		if (!PublicPortSelector.IsConfigurable(effectiveTlsHttpsPort))
		{
			return string.Format(CultureInfo.InvariantCulture,
				"The HTTPS port must be between {0} and {1}.",
				PublicPortSelector.MinimumConfigurablePort,
				PublicPortSelector.MaximumConfigurablePort);
		}

		if (effectiveTlsHttpsPort == publicPort)
		{
			return "The HTTPS port must be different from the port Macro Deck already uses.";
		}

		return effectiveTlsHttpsPort == _listenerState.LoopbackPort
			? "The HTTPS port is used by Macro Deck itself. Choose a different one."
			: null;
	}
}
