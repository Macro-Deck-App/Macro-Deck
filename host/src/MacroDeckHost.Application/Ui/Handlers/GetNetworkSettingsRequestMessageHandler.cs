using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetNetworkSettingsRequestMessageHandler
	: IUiTransportMessageHandler<GetNetworkSettingsRequest, GetNetworkSettingsResponse>
{
	private readonly IAppPreferenceService _preferences;
	private readonly IApplicationRestartService _restart;

	public GetNetworkSettingsRequestMessageHandler(IAppPreferenceService preferences,
		IApplicationRestartService restart)
	{
		_preferences = preferences;
		_restart = restart;
	}

	public async ValueTask<GetNetworkSettingsResponse> Handle(
		GetNetworkSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var view = NetworkSettingsProjection.From(await _preferences.GetNetwork(), _restart.Availability);

		return new GetNetworkSettingsResponse
		{
			PublicPort = view.PublicPort,
			DefaultPublicPort = view.DefaultPublicPort,
			ActivePublicPort = view.ActivePublicPort,
			OverriddenByEnvironment = view.OverriddenByEnvironment,
			ConfiguredPortIgnored = view.ConfiguredPortIgnored,
			PublicListenerUnavailable = view.PublicListenerUnavailable,
			TlsEnabled = view.TlsEnabled,
			TlsMode = view.TlsMode,
			TlsHttpsPort = view.TlsHttpsPort,
			DefaultTlsHttpsPort = view.DefaultTlsHttpsPort,
			ActiveTlsEnabled = view.ActiveTlsEnabled,
			ActiveTlsMode = view.ActiveTlsMode,
			ActiveTlsHttpsPort = view.ActiveTlsHttpsPort,
			TlsFailure = view.TlsFailure,
			TlsRejection = view.TlsRejection,
			TlsCertificateConfigured = view.TlsCertificateConfigured,
			TlsCertificateSource = view.TlsCertificateSource,
			TlsCertificateSubject = view.TlsCertificateSubject,
			TlsCertificateFingerprint = view.TlsCertificateFingerprint,
			TlsCertificateNotBefore = view.TlsCertificateNotBefore,
			TlsCertificateNotAfter = view.TlsCertificateNotAfter,
			TlsCertificateExpired = view.TlsCertificateExpired,
			TlsCertificateNotYetValid = view.TlsCertificateNotYetValid,
			TlsCertificateIssuedByAuthority = view.TlsCertificateIssuedByAuthority,
			TlsAuthorityConfigured = view.TlsAuthorityConfigured,
			TlsAuthoritySubject = view.TlsAuthoritySubject,
			TlsAuthorityFingerprint = view.TlsAuthorityFingerprint,
			TlsAuthorityNotBefore = view.TlsAuthorityNotBefore,
			TlsAuthorityNotAfter = view.TlsAuthorityNotAfter,
			RestartRequired = view.RestartRequired,
			RestartSupported = view.RestartSupported,
			RestartUnsupportedReason = view.RestartUnsupportedReason,
			MinimumPublicPort = PublicPortSelector.MinimumConfigurablePort,
			MaximumPublicPort = PublicPortSelector.MaximumConfigurablePort
		};
	}
}
