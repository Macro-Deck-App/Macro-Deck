using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Application.Network.Tls;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public class ReissueTlsCertificateRequestMessageHandler
	: IUiTransportMessageHandler<ReissueTlsCertificateRequest, UpdateNetworkSettingsResponse>
{
	private readonly IAppPreferenceService _preferences;
	private readonly IApplicationRestartService _restart;
	private readonly PublicTlsBootstrapper _bootstrapper;
	private readonly INetworkRestartNotifier _restartNotifier;

	public ReissueTlsCertificateRequestMessageHandler(IAppPreferenceService preferences,
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
		ReissueTlsCertificateRequest request,
		CancellationToken cancellationToken)
	{
		// Reuses the existing authority, so every device that already trusts this installation keeps
		// working and nothing has to be installed again.
		_bootstrapper.ReissueHostCertificate(DateTimeOffset.UtcNow);

		var updated = await _preferences.GetNetwork();
		await _restartNotifier.Sync(cancellationToken);

		return NetworkSettingsResponseFactory.Create(updated, _restart.Availability, true, null);
	}
}
