using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Application.Network.Tls;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public class RegenerateTlsCertificateAuthorityRequestMessageHandler
	: IUiTransportMessageHandler<RegenerateTlsCertificateAuthorityRequest, UpdateNetworkSettingsResponse>
{
	private readonly IAppPreferenceService _preferences;
	private readonly IApplicationRestartService _restart;
	private readonly PublicTlsBootstrapper _bootstrapper;
	private readonly INetworkRestartNotifier _restartNotifier;

	public RegenerateTlsCertificateAuthorityRequestMessageHandler(IAppPreferenceService preferences,
		IApplicationRestartService restart,
		PublicTlsBootstrapper bootstrapper,
		INetworkRestartNotifier restartNotifier)
	{
		_preferences = preferences;
		_restart = restart;
		_bootstrapper = bootstrapper;
		_restartNotifier = restartNotifier;
	}

	public async ValueTask<UpdateNetworkSettingsResponse> Handle(
		RegenerateTlsCertificateAuthorityRequest request,
		CancellationToken cancellationToken)
	{
		// The destructive path, unlike a reissue: a new authority means every device has to install and
		// trust the new certificate before it can reach this host over HTTPS again.
		_bootstrapper.RegenerateAuthority(DateTimeOffset.UtcNow);

		var updated = await _preferences.GetNetwork();
		await _restartNotifier.Sync(cancellationToken);

		return NetworkSettingsResponseFactory.Create(updated, _restart.Availability, true, null);
	}
}
