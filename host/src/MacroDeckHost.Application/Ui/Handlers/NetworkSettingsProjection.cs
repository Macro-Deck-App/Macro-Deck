using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Lifecycle;
using MacroDeckHost.Application.Network.Tls;
using MacroDeckHost.Application.Services;

namespace MacroDeckHost.Application.Ui.Handlers;

internal readonly record struct NetworkSettingsProjection(
	int PublicPort,
	int DefaultPublicPort,
	int ActivePublicPort,
	bool OverriddenByEnvironment,
	bool ConfiguredPortIgnored,
	bool PublicListenerUnavailable,
	bool TlsEnabled,
	string TlsMode,
	int TlsHttpsPort,
	int DefaultTlsHttpsPort,
	bool ActiveTlsEnabled,
	string ActiveTlsMode,
	int? ActiveTlsHttpsPort,
	string TlsFailure,
	string TlsRejection,
	bool TlsCertificateConfigured,
	string? TlsCertificateSource,
	string? TlsCertificateSubject,
	string? TlsCertificateFingerprint,
	string? TlsCertificateNotBefore,
	string? TlsCertificateNotAfter,
	bool TlsCertificateExpired,
	bool TlsCertificateNotYetValid,
	bool TlsCertificateIssuedByAuthority,
	bool TlsAuthorityConfigured,
	string? TlsAuthoritySubject,
	string? TlsAuthorityFingerprint,
	string? TlsAuthorityNotBefore,
	string? TlsAuthorityNotAfter,
	bool RestartRequired,
	bool RestartSupported,
	string? RestartUnsupportedReason)
{
	public static NetworkSettingsProjection From(NetworkSettings settings, RestartAvailability restart)
	{
		var portRestartRequired =
			(!settings.OverriddenByEnvironment &&
				!settings.ConfiguredPortIgnored &&
				settings.PublicPort != settings.ActivePublicPort) ||
			(settings.PublicListenerUnavailable && !settings.OverriddenByEnvironment);

		var restartRequired = portRestartRequired || NetworkListenerIdentity.TlsOrCertificateDiffers(settings);

		return new NetworkSettingsProjection(settings.PublicPort,
			settings.DefaultPublicPort,
			settings.ActivePublicPort,
			settings.OverriddenByEnvironment,
			settings.ConfiguredPortIgnored,
			settings.PublicListenerUnavailable,
			settings.TlsEnabled,
			settings.TlsMode.ToString(),
			settings.TlsHttpsPort,
			settings.DefaultTlsHttpsPort,
			settings.ActiveTlsEnabled,
			settings.ActiveTlsMode.ToString(),
			settings.ActiveTlsHttpsPort,
			settings.TlsFailure == PublicTlsFailure.None ? string.Empty : settings.TlsFailure.ToString(),
			settings.TlsRejection == PublicTlsRejection.None ? string.Empty : settings.TlsRejection.ToString(),
			settings.TlsCertificateConfigured,
			settings.TlsCertificateSource?.ToString(),
			settings.TlsCertificateSubject,
			settings.TlsCertificateFingerprint,
			settings.TlsCertificateNotBefore?.ToString("O"),
			settings.TlsCertificateNotAfter?.ToString("O"),
			settings.TlsCertificateExpired,
			settings.TlsCertificateNotYetValid,
			settings.TlsCertificateIssuedByAuthority,
			settings.TlsAuthorityConfigured,
			settings.TlsAuthoritySubject,
			settings.TlsAuthorityFingerprint,
			settings.TlsAuthorityNotBefore?.ToString("O"),
			settings.TlsAuthorityNotAfter?.ToString("O"),
			restartRequired,
			restart.Supported,
			restart.Reason);
	}
}
