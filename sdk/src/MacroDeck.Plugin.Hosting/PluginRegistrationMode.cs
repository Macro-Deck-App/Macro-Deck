namespace MacroDeck.Plugin.Hosting;

/// <summary>
/// How a plugin comes by the credentials it authenticates a session with. The protocol specifies the
/// registration endpoint but not who calls it, so these two modes are the SDK's answer rather than a
/// wire concept.
/// </summary>
public enum PluginRegistrationMode
{
	/// <summary>
	/// The host registered this plugin and launched the process, passing the id and secret in the
	/// environment. The plugin never calls the registration endpoint and persists nothing.
	/// </summary>
	Managed,

	/// <summary>
	/// The plugin runs on the same machine as the host - plugin endpoints are local-only - and
	/// registers itself once with an enrollment token the user obtained from the host, then persists
	/// the secret it is handed. Every later start reuses that secret.
	/// </summary>
	SelfRegistering
}
