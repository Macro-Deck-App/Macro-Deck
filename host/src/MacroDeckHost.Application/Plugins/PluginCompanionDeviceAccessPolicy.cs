using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Plugins;

public enum PluginCompanionDeviceAccess
{
	Available,
	NotAllowed
}

public interface IPluginCompanionDeviceAccessPolicy
{
	event Action? Changed;

	Task<PluginCompanionDeviceAccess> EvaluateAsync(string pluginId, CancellationToken cancellationToken = default);

	bool CanBeGranted(string pluginId);

	Task<bool> GetAllowPluginsAsync(CancellationToken cancellationToken = default);

	Task SetAllowPluginsAsync(bool allow, CancellationToken cancellationToken = default);
}

// ADR 0110: off by default, and an installed plugin must also declare host:companion-devices.
[SuppressMessage("Design",
	"CA1001:Types that own disposable fields should be disposable",
	Justification = "A process-lifetime singleton the container disposes at shutdown while a read can still be in "
		+ "flight; a disposed gate would fault that read from its finally block. The gate holds no unmanaged "
		+ "resource.")]
public sealed class PluginCompanionDeviceAccessPolicy(
	IPluginSessionRegistry sessionRegistry,
	IPluginInstallationCatalog installationCatalog,
	IPluginManifestReader manifestReader,
	IServiceScopeFactory scopeFactory) : IPluginCompanionDeviceAccessPolicy
{
	public const string AllowPluginsKey = "companion.allowPlugins";

	private readonly PluginPermissionDeclarations _declarations =
		new(PluginPermissions.HostCompanionDevices, sessionRegistry, installationCatalog, manifestReader);

	private readonly SemaphoreSlim _gate = new(1, 1);
	private volatile StrongBox<bool>? _allowPlugins;

	public event Action? Changed;

	public async Task<PluginCompanionDeviceAccess> EvaluateAsync(
		string pluginId,
		CancellationToken cancellationToken = default)
	{
		return await GetAllowPluginsAsync(cancellationToken) && _declarations.IsGrantable(pluginId)
			? PluginCompanionDeviceAccess.Available
			: PluginCompanionDeviceAccess.NotAllowed;
	}

	public bool CanBeGranted(string pluginId)
	{
		return _declarations.IsGrantable(pluginId);
	}

	public async Task<bool> GetAllowPluginsAsync(CancellationToken cancellationToken = default)
	{
		if (_allowPlugins is { } cached)
		{
			return cached.Value;
		}

		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (_allowPlugins is null)
			{
				await using var scope = scopeFactory.CreateAsyncScope();
				var stored = await scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>()
					.GetByKey(AllowPluginsKey);
				_allowPlugins = new StrongBox<bool>(bool.TryParse(stored?.Value, out var allow) && allow);
			}

			return _allowPlugins.Value;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task SetAllowPluginsAsync(bool allow, CancellationToken cancellationToken = default)
	{
		bool changed;
		await _gate.WaitAsync(cancellationToken);
		try
		{
			await using (var scope = scopeFactory.CreateAsyncScope())
			{
				await scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>()
					.SetValue(AllowPluginsKey, allow ? "true" : "false");
			}

			changed = _allowPlugins?.Value != allow;
			_allowPlugins = new StrongBox<bool>(allow);
		}
		finally
		{
			_gate.Release();
		}

		if (changed)
		{
			Changed?.Invoke();
		}
	}
}
