namespace MacroDeckHost.Application.Plugins.Capabilities;

public interface IRemotePluginIntegrationRegistrar
{
	Task<bool> RegisterAsync(string pluginId, CancellationToken cancellationToken = default);

	Task UnregisterAsync(string pluginId, CancellationToken cancellationToken = default);

	Task ApplyRefreshedSnapshotAsync(
		string pluginId,
		RemotePluginCapabilitySnapshot snapshot,
		CancellationToken cancellationToken = default);

	Task RegisterInstalledButStoppedAsync(CancellationToken cancellationToken = default);

	Task RegisterInstalledDetachedAsync(string pluginId, CancellationToken cancellationToken = default);

	/// <summary>Unregisters plugin adapters whose installation directory is no longer on disk and that
	/// have no session, so a plugin removed behind the host's back stops appearing as an integration
	/// instead of lingering until the next restart.</summary>
	Task UnregisterVanishedInstallationsAsync(CancellationToken cancellationToken = default);

	/// <summary>Re-fetches every currently registered plugin's localization catalog for whichever
	/// cultures the fallback chain now needs, after the active language changed.</summary>
	Task RefreshLocalizationCatalogsAsync(CancellationToken cancellationToken = default);
}
