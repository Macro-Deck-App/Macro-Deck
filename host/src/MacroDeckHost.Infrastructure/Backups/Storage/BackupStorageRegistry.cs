using MacroDeckHost.Application.Backups.Storage;

namespace MacroDeckHost.Infrastructure.Backups.Storage;

public sealed class BackupStorageRegistry : IBackupStorageRegistry
{
	public BackupStorageRegistry(IEnumerable<IBackupStorageProvider> providers)
		=> Providers = [.. providers];

	public IReadOnlyList<IBackupStorageProvider> Providers { get; }

	public IBackupStorageProvider Primary => Providers[0];

	public IBackupStorageProvider? Find(string providerId)
		=> Providers.FirstOrDefault(provider =>
			string.Equals(provider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
}
