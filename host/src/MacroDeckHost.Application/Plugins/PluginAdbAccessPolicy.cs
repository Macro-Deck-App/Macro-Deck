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

// ADR 0092: an installed plugin must declare host:adb.
public sealed class PluginAdbAccessPolicy(
	IAdbManager adbManager,
	IPluginSessionRegistry sessionRegistry,
	IPluginInstallationCatalog installationCatalog,
	IPluginManifestReader manifestReader,
	IServiceScopeFactory scopeFactory) : IPluginAdbAccessPolicy
{
	private readonly PluginPermissionDeclarations _declarations =
		new(PluginPermissions.HostAdb, sessionRegistry, installationCatalog, manifestReader);

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

		return _declarations.IsGrantable(pluginId) ? PluginAdbAccess.Available : PluginAdbAccess.NotAllowed;
	}

	public bool CanBeGranted(string pluginId)
	{
		return _declarations.IsGrantable(pluginId);
	}

	public void Refresh(AdbSettings settings)
	{
		_allowPlugins = new StrongBox<bool>(settings.AllowPlugins);
	}

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
}
