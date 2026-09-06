using MacroDeckHost.Application.Plugins.Trust;

namespace MacroDeckHost.Infrastructure.Plugins.Trust;

/// <summary>Macro Deck publishes no revocation feed yet. Reporting <see cref="PluginRevocationStatus.Unavailable" />
/// for every certificate id - rather than failing closed - is a deliberate policy, not a placeholder to
/// come back to: treating "we cannot check" as "revoked" would refuse every signed plugin on every machine,
/// which is an outage, not a security posture. <see cref="PluginTrustEvaluator" /> only consults this at
/// all once the cryptographic checks have already passed.</summary>
public sealed class NoRevocationDataSource : IPluginRevocationSource
{
	public Task<PluginRevocationResult> CheckAsync(string certificateId, CancellationToken cancellationToken = default)
		=> Task.FromResult(new PluginRevocationResult(PluginRevocationStatus.Unavailable, null));
}
