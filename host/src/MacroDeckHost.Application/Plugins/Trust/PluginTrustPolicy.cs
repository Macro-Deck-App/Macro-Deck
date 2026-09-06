using MacroDeckHost.Application.Plugins.Installation;

namespace MacroDeckHost.Application.Plugins.Trust;

public static class PluginTrustPolicy
{
	// A local path or an upload is a file the user picked, and consent for it never depends on developer
	// mode. A Url is the store's and the registry's source kind, so it carries consent only while the
	// host itself reports developer mode on - the caller must have read that from IAppPreferenceService,
	// never from anything a client sent. Everything else is refused outright.
	public static bool PermitsUnsignedConsent(PluginArtifactSourceKind kind, bool developerMode) => kind switch
	{
		PluginArtifactSourceKind.LocalPath => true,
		PluginArtifactSourceKind.Upload => true,
		PluginArtifactSourceKind.Url => developerMode,
		_ => false
	};

	// A trust tier is monotonic: once a plugin id has been admitted as Trusted, no later version may be
	// admitted at a lower tier, consent or not - otherwise an attacker who cannot forge a signature simply
	// ships an unsigned update instead.
	public static bool IsDowngrade(PluginTrustVerdict? admittedVerdict, PluginTrustVerdict newVerdict)
		=> admittedVerdict == PluginTrustVerdict.Trusted && newVerdict != PluginTrustVerdict.Trusted;
}
