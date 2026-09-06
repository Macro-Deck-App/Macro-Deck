using System.Net;

namespace MacroDeckHost.Application.Network.Tls;

public enum PublicTlsBootstrapAction
{
	None,

	CreateAuthorityAndHostCertificate,

	ReissueHostCertificate
}

public readonly record struct PublicTlsCertificateState(
	PublicTlsCertificateInfo? HostCertificate,
	PublicTlsCertificateInfo? Authority,
	bool AuthorityKeyUsable,
	bool HostCertificateIssuedByAuthority,
	IReadOnlyList<IPAddress> UncoveredAddresses,
	bool KeyRingLocked);

public static class PublicTlsBootstrapPlanner
{
	public static PublicTlsBootstrapAction Decide(PublicTlsCertificateState state, DateTimeOffset now)
	{
		// The key ring is pointed at a scratch directory while locked, so anything written now would be
		// unreadable once the real ring returns.
		if (state.KeyRingLocked)
		{
			return PublicTlsBootstrapAction.None;
		}

		// Checked before anything else and without looking at the certificate itself: material the user
		// uploaded is never replaced, however expired or incomplete Macro Deck thinks it is.
		if (state.HostCertificate?.Source == PublicTlsCertificateSource.Custom)
		{
			return PublicTlsBootstrapAction.None;
		}

		if (state.Authority is null)
		{
			return PublicTlsBootstrapAction.CreateAuthorityAndHostCertificate;
		}

		// An authority that exists but cannot be opened must never become a reason to mint a second one:
		// a transient key ring failure would otherwise silently invalidate every device that already
		// trusts this installation.
		if (!state.AuthorityKeyUsable)
		{
			return PublicTlsBootstrapAction.None;
		}

		if (state.HostCertificate is null || !state.HostCertificateIssuedByAuthority)
		{
			return PublicTlsBootstrapAction.ReissueHostCertificate;
		}

		return NeedsRenewal(state, now)
			? PublicTlsBootstrapAction.ReissueHostCertificate
			: PublicTlsBootstrapAction.None;
	}

	private static bool NeedsRenewal(PublicTlsCertificateState state, DateTimeOffset now)
	{
		var certificate = state.HostCertificate!;

		if (certificate.IsNotYetValid(now) || certificate.IsExpired(now))
		{
			return true;
		}

		if (certificate.NotAfter - now <= LocalCertificateAuthority.HostCertificateRenewalWindow)
		{
			return true;
		}

		// Only a currently reachable address that the certificate does not name forces a reissue. A name
		// left over from an address the machine no longer has costs nothing, and reacting to it would
		// reissue every time a VPN or a dock appears and disappears.
		return state.UncoveredAddresses.Count > 0;
	}
}
