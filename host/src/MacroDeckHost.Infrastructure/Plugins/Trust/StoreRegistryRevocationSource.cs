using MacroDeckHost.Application.Plugins.Trust;
using MacroDeckHost.Application.Store;

namespace MacroDeckHost.Infrastructure.Plugins.Trust;

/// <summary>Answers from the revoked keys of the signed Store registry snapshot currently loaded. Without a
/// loaded snapshot it reports <see cref="PluginRevocationStatus.Unavailable" />, which never blocks:
/// treating "we cannot check" as "revoked" would refuse every signed plugin on a machine that has not
/// fetched the registry yet.</summary>
public sealed class StoreRegistryRevocationSource : IPluginRevocationSource
{
	private readonly IStoreCatalog _catalog;

	public StoreRegistryRevocationSource(IStoreCatalog catalog)
	{
		_catalog = catalog;
	}

	public Task<PluginRevocationResult> CheckAsync(string certificateId,
		string? issuerCertificateId,
		CancellationToken cancellationToken = default)
	{
		var snapshot = _catalog.Snapshot;
		if (snapshot.Sequence == 0)
		{
			return Task.FromResult(new PluginRevocationResult(PluginRevocationStatus.Unavailable, null));
		}

		return Task.FromResult(StoreRevocations.IsRevoked(snapshot, certificateId, issuerCertificateId)
			? new PluginRevocationResult(PluginRevocationStatus.Revoked,
				"The package's signing certificate or its issuer is revoked by the Macro Deck registry.")
			: new PluginRevocationResult(PluginRevocationStatus.NotRevoked, null));
	}
}
