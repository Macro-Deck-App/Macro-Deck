using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Application.Network.Tls;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateNetworkTlsCertificateRequestMessageHandler
	: IUiTransportMessageHandler<UpdateNetworkTlsCertificateRequest, UpdateNetworkSettingsResponse>
{
	private readonly IAppPreferenceService _preferences;
	private readonly IApplicationRestartService _restart;
	private readonly IPublicTlsCertificateStore _certificateStore;
	private readonly INetworkRestartNotifier _restartNotifier;
	private readonly IServiceScopeFactory _scopeFactory;

	public UpdateNetworkTlsCertificateRequestMessageHandler(IAppPreferenceService preferences,
		IApplicationRestartService restart,
		IPublicTlsCertificateStore certificateStore,
		INetworkRestartNotifier restartNotifier,
		IServiceScopeFactory scopeFactory)
	{
		_preferences = preferences;
		_restart = restart;
		_certificateStore = certificateStore;
		_restartNotifier = restartNotifier;
		_scopeFactory = scopeFactory;
	}

	public async ValueTask<UpdateNetworkSettingsResponse> Handle(
		UpdateNetworkTlsCertificateRequest request,
		CancellationToken cancellationToken)
	{
		var current = await _preferences.GetNetwork();

		var validation = PublicTlsCertificateValidator.Validate(request.CertificatePem,
			request.PrivateKeyPem,
			PublicTlsCertificateSource.Custom);

		if (!validation.Valid)
		{
			var error = validation.Error is { } text ? await ActiveLocalization.Resolve(_scopeFactory, text) : null;
			return NetworkSettingsResponseFactory.Create(current, _restart.Availability, false, error);
		}

		_certificateStore.Save(request.CertificatePem, request.PrivateKeyPem, PublicTlsCertificateSource.Custom);

		var updated = await _preferences.GetNetwork();
		await _restartNotifier.Sync(cancellationToken);

		return NetworkSettingsResponseFactory.Create(updated, _restart.Availability, true, null);
	}
}
