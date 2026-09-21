using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Services;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class FixedAdbAccessPolicy(PluginAdbAccess access = PluginAdbAccess.NotEnabled) : IPluginAdbAccessPolicy
{
	public PluginAdbAccess Access { get; set; } = access;

	public List<AdbSettings> Refreshed { get; } = [];

	public Task<PluginAdbAccess> EvaluateAsync(string pluginId, CancellationToken cancellationToken = default)
		=> Task.FromResult(Access);

	public bool Grantable { get; set; } = true;

	public void Refresh(AdbSettings settings) => Refreshed.Add(settings);

	public bool CanBeGranted(string pluginId) => Grantable;
}
