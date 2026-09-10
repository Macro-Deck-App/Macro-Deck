using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Network.Tls;
using MacroDeckHost.Application.Services;

namespace MacroDeckHost.Tests.UnitTests.Configuration;

public class NetworkListenerIdentityTests
{
	private const int PublicPort = 8193;
	private const int HttpsPort = 8194;
	private const string Fingerprint = "AABBCC";

	private static NetworkSettings Settings(bool tlsEnabled = false,
		PublicTlsMode tlsMode = PublicTlsMode.Additional,
		int tlsHttpsPort = HttpsPort,
		PublicTlsMode activeTlsMode = PublicTlsMode.Disabled,
		int? activeTlsHttpsPort = null,
		string? certificateFingerprint = null,
		string? activeCertificateFingerprint = null)
		=> new(PublicPort,
			DefaultPublicPort: PublicPort,
			ActivePublicPort: PublicPort,
			OverriddenByEnvironment: false,
			ConfiguredPortIgnored: false,
			PublicListenerUnavailable: false,
			TlsEnabled: tlsEnabled,
			TlsMode: tlsMode,
			TlsHttpsPort: tlsHttpsPort,
			DefaultTlsHttpsPort: HttpsPort,
			ActiveTlsEnabled: activeTlsMode != PublicTlsMode.Disabled,
			ActiveTlsMode: activeTlsMode,
			ActiveTlsHttpsPort: activeTlsHttpsPort,
			TlsFailure: PublicTlsFailure.None,
			TlsRejection: PublicTlsRejection.None,
			TlsCertificateConfigured: certificateFingerprint is not null,
			TlsCertificateSource: null,
			TlsCertificateSubject: null,
			TlsCertificateFingerprint: certificateFingerprint,
			TlsCertificateNotBefore: null,
			TlsCertificateNotAfter: null,
			TlsCertificateExpired: false,
			TlsCertificateNotYetValid: false,
			ActiveTlsCertificateFingerprint: activeCertificateFingerprint,
			TlsCertificateIssuedByAuthority: false,
			TlsAuthorityConfigured: false,
			TlsAuthoritySubject: null,
			TlsAuthorityFingerprint: null,
			TlsAuthorityNotBefore: null,
			TlsAuthorityNotAfter: null,
			DiscoveryEnabled: true);

	// A fresh installation stores no TLS rows at all, so the reported configuration comes from the same
	// default the listener was opened with. If the two ever drift apart, every installation gets a
	// "restart to apply" prompt that restarting cannot clear.
	[Test]
	public void A_fresh_install_serving_http_and_https_does_not_demand_a_restart()
	{
		var settings = Settings(tlsEnabled: PublicTlsSelector.DefaultTlsEnabled,
			tlsMode: PublicTlsMode.Additional,
			activeTlsMode: PublicTlsMode.Additional,
			activeTlsHttpsPort: HttpsPort);

		Assert.That(NetworkListenerIdentity.TlsOrCertificateDiffers(settings), Is.False);
	}

	[Test]
	public void A_host_serving_exactly_what_is_configured_needs_no_restart()
	{
		Assert.Multiple(() =>
		{
			Assert.That(NetworkListenerIdentity.TlsOrCertificateDiffers(Settings()), Is.False);
			Assert.That(NetworkListenerIdentity.TlsOrCertificateDiffers(Settings(tlsEnabled: true,
					activeTlsMode: PublicTlsMode.Additional,
					activeTlsHttpsPort: HttpsPort,
					certificateFingerprint: Fingerprint,
					activeCertificateFingerprint: Fingerprint)),
				Is.False);
		});
	}

	[Test]
	public void A_stored_certificate_needs_no_restart_while_https_is_switched_off()
	{
		var settings = Settings(tlsEnabled: false,
			activeTlsMode: PublicTlsMode.Disabled,
			certificateFingerprint: Fingerprint,
			activeCertificateFingerprint: null);

		Assert.That(NetworkListenerIdentity.TlsOrCertificateDiffers(settings), Is.False);
	}

	[Test]
	public void Turning_https_on_needs_a_restart()
	{
		var settings = Settings(tlsEnabled: true,
			activeTlsMode: PublicTlsMode.Disabled,
			certificateFingerprint: Fingerprint);

		Assert.That(NetworkListenerIdentity.TlsOrCertificateDiffers(settings), Is.True);
	}

	[Test]
	public void Replacing_the_certificate_while_https_is_on_needs_a_restart()
	{
		var settings = Settings(tlsEnabled: true,
			activeTlsMode: PublicTlsMode.Additional,
			activeTlsHttpsPort: HttpsPort,
			certificateFingerprint: "NEWFINGERPRINT",
			activeCertificateFingerprint: Fingerprint);

		Assert.That(NetworkListenerIdentity.TlsOrCertificateDiffers(settings), Is.True);
	}

	[Test]
	public void Changing_the_https_port_needs_a_restart()
	{
		var settings = Settings(tlsEnabled: true,
			tlsHttpsPort: 9443,
			activeTlsMode: PublicTlsMode.Additional,
			activeTlsHttpsPort: HttpsPort,
			certificateFingerprint: Fingerprint,
			activeCertificateFingerprint: Fingerprint);

		Assert.That(NetworkListenerIdentity.TlsOrCertificateDiffers(settings), Is.True);
	}

	[Test]
	public void Switching_between_replace_and_additional_mode_needs_a_restart()
	{
		var settings = Settings(tlsEnabled: true,
			tlsMode: PublicTlsMode.Replace,
			activeTlsMode: PublicTlsMode.Additional,
			activeTlsHttpsPort: HttpsPort,
			certificateFingerprint: Fingerprint,
			activeCertificateFingerprint: Fingerprint);

		Assert.That(NetworkListenerIdentity.TlsOrCertificateDiffers(settings), Is.True);
	}
}
