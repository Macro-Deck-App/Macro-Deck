using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Plugins;

public enum PluginAdbAccess
{
	Available,
	NotEnabled,
	NotAllowed
}

public interface IPluginAdbAccessPolicy
{
	Task<PluginAdbAccess> EvaluateAsync(string pluginId, CancellationToken cancellationToken = default);

	void Refresh(AdbSettings settings);

	bool CanBeGranted(string pluginId);
}

// ADR 0092: an installed plugin must declare host:adb; a self-registered session was admitted by the user
// through a developer token or pairing and declares nothing to the host, so it is exempt.
public sealed class PluginAdbAccessPolicy(
	IAdbManager adbManager,
	IPluginSessionRegistry sessionRegistry,
	IPluginInstallationCatalog installationCatalog,
	IPluginManifestReader manifestReader,
	IServiceScopeFactory scopeFactory) : IPluginAdbAccessPolicy
{
	private readonly ConcurrentDictionary<string, bool> _declaresAdbByManifest = new(StringComparer.Ordinal);
	private volatile StrongBox<bool>? _allowPlugins;

	public async Task<PluginAdbAccess> EvaluateAsync(string pluginId, CancellationToken cancellationToken = default)
	{
		var status = adbManager.Status;
		if (!status.Enabled || !status.Supported)
		{
			return PluginAdbAccess.NotEnabled;
		}

		if (!await AllowPluginsAsync())
		{
			return PluginAdbAccess.NotAllowed;
		}

		return IsSelfRegistered(pluginId) || DeclaresAdb(pluginId) ? PluginAdbAccess.Available : PluginAdbAccess.NotAllowed;
	}

	public bool CanBeGranted(string pluginId)
		=> IsSelfRegistered(pluginId) || DeclaresAdb(pluginId);

	private bool IsSelfRegistered(string pluginId)
		=> sessionRegistry.Snapshot()
			.Any(session => session.PluginId == pluginId &&
				session.State == PluginSessionState.Connected &&
				session.Origin == PluginSessionOrigin.SelfRegistered);

	public void Refresh(AdbSettings settings) => _allowPlugins = new StrongBox<bool>(settings.AllowPlugins);

	private async Task<bool> AllowPluginsAsync()
	{
		if (_allowPlugins is { } cached)
		{
			return cached.Value;
		}

		await using var scope = scopeFactory.CreateAsyncScope();
		var settings = await scope.ServiceProvider.GetRequiredService<IAppPreferenceService>().GetAdb();
		_allowPlugins ??= new StrongBox<bool>(settings.AllowPlugins);
		return _allowPlugins.Value;
	}

	private bool DeclaresAdb(string pluginId)
	{
		if (!installationCatalog.TryResolveActive(pluginId, out var active) || active is null)
		{
			return false;
		}

		var key = $"{active.ManifestPath}|{File.GetLastWriteTimeUtc(active.ManifestPath).Ticks}";
		if (_declaresAdbByManifest.TryGetValue(key, out var declares))
		{
			return declares;
		}

		var manifest = manifestReader.Read(active.ManifestPath, pluginId, active.Version).Manifest;
		if (manifest is null)
		{
			return false;
		}

		declares = manifest.Permissions?.Contains(PluginPermissions.HostAdb, StringComparer.Ordinal) == true;
		_declaresAdbByManifest[key] = declares;
		return declares;
	}
}
