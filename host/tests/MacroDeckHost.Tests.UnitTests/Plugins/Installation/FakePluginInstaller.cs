using System.Diagnostics.CodeAnalysis;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Application.Plugins.Runtime;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Installation;

internal sealed class FakePluginInstaller : IPluginInstaller
{
	public PluginInstallResult ResultToReturn { get; set; } =
		PluginInstallResult.Ok("com.example.plugin", "1.0.0", null, activated: false);

	public PluginUninstallRequest? LastUninstallRequest { get; private set; }

	public Task<PluginInstallResult> Inspect(PluginArtifactSource source,
		CancellationToken cancellationToken = default) => Task.FromResult(ResultToReturn);

	public Task<PluginInstallResult> Install(PluginArtifactSource source,
		PluginInstallRequest request,
		CancellationToken cancellationToken = default) => Task.FromResult(ResultToReturn);

	public Task<PluginInstallResult> Activate(string pluginId,
		string version,
		CancellationToken cancellationToken = default) => Task.FromResult(ResultToReturn);

	public Task<PluginInstallResult> Uninstall(string pluginId,
		PluginUninstallRequest request,
		CancellationToken cancellationToken = default)
	{
		LastUninstallRequest = request;
		return Task.FromResult(ResultToReturn);
	}
}

internal sealed class FakeInstallationCatalog : IPluginInstallationCatalog
{
	public IReadOnlyList<InstalledPlugin> Discover() => [];

	public bool TryResolveActive(string pluginId, out InstalledPluginVersion? version)
	{
		version = null;
		return false;
	}

	public void Invalidate()
	{
	}
}

internal sealed class FakeArtifactCache : IPluginArtifactCache
{
	public bool TryGet(string sha256, [NotNullWhen(true)] out string? cachedPath)
	{
		cachedPath = null;
		return false;
	}

	public Task<string> Put(string sourcePath,
		string sha256,
		CancellationToken cancellationToken = default) => Task.FromResult(sourcePath);

	public void Prune()
	{
	}

	public void Clear()
	{
	}
}
