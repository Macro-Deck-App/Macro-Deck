namespace MacroDeck.Plugin.Hosting.Credentials;

/// <summary>
/// Where a plugin's credentials come from, and - when it owns them - where they go.
///
/// <para>
/// Two implementations, one per registration mode: a managed plugin reads what the supervisor put in
/// its environment and can never save, a self-registering plugin reads and writes a file it owns.
/// Keeping the modes behind one interface is what lets everything above this line be mode-agnostic.
/// </para>
/// </summary>
public interface IPluginCredentialStore
{
	/// <summary>Whether this store can persist a newly issued secret at all.</summary>
	bool CanSave { get; }

	/// <summary>The stored credentials, or null when the plugin has not registered yet.</summary>
	Task<PluginCredentials?> LoadAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Persists credentials issued by registration. Throws <see cref="NotSupportedException" /> when
	/// <see cref="CanSave" /> is false.
	/// </summary>
	Task SaveAsync(PluginCredentials credentials, CancellationToken cancellationToken = default);
}
