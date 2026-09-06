namespace MacroDeckHost.Application.Plugins.Trust;

/// <summary>The one-time marker that separates plugins grandfathered into signature enforcement from
/// plugins that must always carry a trust record. See <see cref="PluginTrustGate" /> for how the two are
/// treated differently.</summary>
public interface IPluginTrustBaseline
{
	Task<bool> Exists(CancellationToken cancellationToken = default);

	Task Establish(CancellationToken cancellationToken = default);
}
