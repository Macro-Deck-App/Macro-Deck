using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

internal static class NetworkSettingsResponseFactory
{
	public static UpdateNetworkSettingsResponse Create(NetworkSettings settings,
		RestartAvailability restart,
		bool success,
		string? error)
	{
		var view = NetworkSettingsProjection.From(settings, restart);

		return new UpdateNetworkSettingsResponse
		{
			Success = success,
			Error = error,
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
			DiscoveryEnabled = view.DiscoveryEnabled,
			MinimumPublicPort = PublicPortSelector.MinimumConfigurablePort,
			MaximumPublicPort = PublicPortSelector.MaximumConfigurablePort
		};
	}
}
