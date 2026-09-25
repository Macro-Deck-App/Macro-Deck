using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Application.Store;

public static class StoreRevocations
{
	public static bool IsRevoked(StoreCatalogSnapshot snapshot, string certificateId, string? issuerCertificateId)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		return snapshot.RevokedKeyIds.Any(keyId =>
			string.Equals(keyId, certificateId, StringComparison.OrdinalIgnoreCase) ||
			(issuerCertificateId is not null &&
				string.Equals(keyId, issuerCertificateId, StringComparison.OrdinalIgnoreCase)));
	}
}
