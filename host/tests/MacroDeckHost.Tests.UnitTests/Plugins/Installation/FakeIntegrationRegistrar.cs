using MacroDeckHost.Application.Plugins.Capabilities;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Installation;

internal sealed class FakeIntegrationRegistrar : IRemotePluginIntegrationRegistrar
{
	private readonly HashSet<string> _registered = new(StringComparer.Ordinal);

	public bool IsRegistered(string pluginId) => _registered.Contains(pluginId);

	public Task<bool> RegisterAsync(string pluginId, CancellationToken cancellationToken = default)
	{
		_registered.Add(pluginId);
		return Task.FromResult(true);
	}

	public Task UnregisterAsync(string pluginId, CancellationToken cancellationToken = default)
	{
		_registered.Remove(pluginId);
		return Task.CompletedTask;
	}

	public Task RegisterInstalledButStoppedAsync(CancellationToken cancellationToken = default)
		=> Task.CompletedTask;

	public Task UnregisterVanishedInstallationsAsync(CancellationToken cancellationToken = default)
		=> Task.CompletedTask;

	public Task RegisterInstalledDetachedAsync(string pluginId,
		CancellationToken cancellationToken = default)
	{
		_registered.Add(pluginId);
		return Task.CompletedTask;
	}

	public Task ApplyRefreshedSnapshotAsync(string pluginId,
		RemotePluginCapabilitySnapshot snapshot,
		CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task RefreshLocalizationCatalogsAsync(CancellationToken cancellationToken = default)
		=> Task.CompletedTask;
}
