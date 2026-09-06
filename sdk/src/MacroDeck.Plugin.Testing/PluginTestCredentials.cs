using System.Security.Cryptography;
using MacroDeck.Plugin.Hosting;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// How <see cref="MacroDeckTestHost.HostAsync" /> should make a plugin authenticate, mirroring
/// <see cref="PluginRegistrationMode" />'s two real modes.
///
/// <para>
/// Each access to <see cref="Managed" /> or <see cref="SelfRegistering" /> produces a fresh identity;
/// capture the result in a variable and reuse it across calls to give two plugins under test the same
/// identity on purpose - as a genuine second instance of the same managed plugin does.
/// </para>
/// </summary>
public sealed class PluginTestCredentials
{
	private PluginTestCredentials(PluginRegistrationMode mode, string? id, string? secret, string? enrollmentToken)
	{
		Mode = mode;
		Id = id;
		Secret = secret;
		EnrollmentToken = enrollmentToken;
	}

	/// <summary>
	/// As if a supervisor had already registered this plugin and launched it with an id and secret in
	/// its environment: <see cref="MacroDeckTestHost.HostAsync" /> writes both into configuration, so
	/// the plugin never calls the registration endpoint. Identity now comes from <c>manifest.json</c> at
	/// the content root, not a builder call - <see cref="MacroDeckTestHost.HostAsync" />'s own default
	/// manifest declares this same id when the caller supplies none, so the two can never disagree the
	/// way <c>PluginHostBuilder.Build</c> now rejects (a manifest declaring a different id is a build
	/// error, not something configuration silently overrides).
	/// </summary>
	public static PluginTestCredentials Managed
		=> new(PluginRegistrationMode.Managed, GenerateOwnerId(), GenerateSecret(), enrollmentToken: null);

	/// <summary>
	/// <see cref="Managed" />, but with a caller-chosen id instead of a fresh one. What
	/// <see cref="MacroDeckTestHost.LaunchAsync" /> uses once it has peeked the id an already-built
	/// executable's own <c>manifest.json</c> declares, so the id it hands the process through
	/// <c>MACRO_DECK_PLUGIN_ID</c> is the same one that process's manifest will report - exactly what a
	/// real supervisor does, and the only way a managed launch avoids the "manifest declares id X, but
	/// configuration says Y" build error a mismatched pair now produces.
	/// </summary>
	internal static PluginTestCredentials ManagedWithId(string id)
		=> new(PluginRegistrationMode.Managed, id, GenerateSecret(), enrollmentToken: null);

	/// <summary>
	/// As if a user had obtained a one-time enrollment token from the host: the plugin registers itself
	/// on first connect and persists the secret it is issued under whichever id its own
	/// <c>manifest.json</c> declares.
	/// </summary>
	public static PluginTestCredentials SelfRegistering
		=> new(PluginRegistrationMode.SelfRegistering, id: null, secret: null, GenerateEnrollmentToken());

	/// <summary>
	/// As if a user had never obtained an enrollment token at all: the plugin has no stored credential
	/// and no <see cref="EnrollmentToken" />, so - per <c>PluginHostOptions.PairingEnabled</c>'s default -
	/// it falls back to interactive pairing against the host's <c>POST /api/plugins/pairing</c> family
	/// instead. Identity comes from <c>manifest.json</c>, exactly as <see cref="SelfRegistering" />'s own does.
	/// </summary>
	public static PluginTestCredentials Pairing
		=> new(PluginRegistrationMode.SelfRegistering, id: null, secret: null, enrollmentToken: null);

	internal PluginRegistrationMode Mode { get; }

	internal string? Id { get; }

	internal string? Secret { get; }

	/// <summary>
	/// The one-time enrollment token this identity presents at <c>POST /api/plugins/registration</c> -
	/// null for <see cref="Managed" />, which never registers. Public so a test can prove the token a
	/// self-registering plugin actually presented (<see cref="MacroDeckTestHost.EnrollmentTokens" />) is
	/// exactly this one, not merely that some token arrived.
	/// </summary>
	public string? EnrollmentToken { get; }

	/// <summary>A fresh, valid reverse-domain id. Shared with <see cref="PluginTestManifest" />'s own
	/// default id generation, so a defaulted credential and a defaulted manifest are the same *shape* of
	/// id even when they are not literally the same value.</summary>
	internal static string GenerateOwnerId() => $"test.harness.p{Guid.NewGuid():N}";

	/// <summary>32 random bytes, base64url-encoded without padding - the same shape a real
	/// registration issues, and at least <c>PluginAuthDefaults.MinPluginSecretLength</c> long.</summary>
	private static string GenerateSecret()
		=> Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
			.TrimEnd('=')
			.Replace('+', '-')
			.Replace('/', '_');

	private static string GenerateEnrollmentToken() => $"test-enrollment-{Guid.NewGuid():N}";
}
