using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Plugins.Trust;

public static class PluginTrustPolicy
{
	// A local path or an upload is a file the user picked, and consent for it never depends on developer
	// mode. A Url is the store's and the registry's source kind, so it carries consent only while the
	// host itself reports developer mode on - the caller must have read that from IAppPreferenceService,
	// never from anything a client sent. Everything else is refused outright.
	public static bool PermitsUnsignedConsent(PluginArtifactSourceKind kind, bool developerMode)
	{
		return kind switch
		{
			PluginArtifactSourceKind.LocalPath => true,
			PluginArtifactSourceKind.Upload => true,
			PluginArtifactSourceKind.Url => developerMode,
			// Never reviewed, so a test build is installed only with the consent the user gives for that one install.
			PluginArtifactSourceKind.TestBuild => true,
			_ => false
		};
	}

	// A trust tier is monotonic: once a plugin id has been admitted as Trusted, no later version may be
	// admitted at a lower tier, consent or not - otherwise an attacker who cannot forge a signature simply
	// ships an unsigned update instead.
	public static bool IsDowngrade(
			PluginTrustVerdict? admittedVerdict,
			PluginTrustVerdict newVerdict,
			PluginArtifactSourceKind sourceKind)
		// A test build's URL and digest come from the Platform for an invited account, never from the package,
		// and testing the next version of an installed plugin is its purpose.
	{
		return sourceKind != PluginArtifactSourceKind.TestBuild
			&& admittedVerdict == PluginTrustVerdict.Trusted
			&& newVerdict != PluginTrustVerdict.Trusted;
	}

	// The certificate subject is the publisher the Platform verified, stable across certificate rotation. A row
	// written before signers were recorded has no subject, so only its own certificate still matches it.
	public static bool ChangesSigner(IEnumerable<PluginTrustRecordEntity> records, PluginTrustResult incoming)
	{
		ArgumentNullException.ThrowIfNull(records);
		ArgumentNullException.ThrowIfNull(incoming);

		if (incoming.Verdict != PluginTrustVerdict.Trusted)
		{
			return false;
		}

		var pin = records
			.Where(record => PluginTrustRecordVerdicts.Parse(record.AdmittedVerdict) == PluginTrustVerdict.Trusted)
			.OrderByDescending(record => record.InstalledAt)
			.ThenByDescending(record => record.CreatedAt)
			.FirstOrDefault();

		if (pin is null)
		{
			return false;
		}

		if (pin.SignerKind is null || pin.SignerId is null)
		{
			return !string.Equals(pin.CertificateId, incoming.CertificateId, StringComparison.Ordinal);
		}

		return incoming.Signer is not { } signer
			|| !string.Equals(pin.SignerKind, signer.Kind, StringComparison.Ordinal)
			|| !string.Equals(pin.SignerId, signer.Id, StringComparison.Ordinal);
	}
}
