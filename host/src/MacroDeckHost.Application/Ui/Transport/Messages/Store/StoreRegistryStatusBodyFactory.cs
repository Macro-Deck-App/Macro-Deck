using MacroDeckHost.Application.Store;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public static class StoreRegistryStatusBodyFactory
{
	public static StoreRegistryStatusBody Create(StoreRegistryStatus status) => new()
	{
		HasCatalog = status.HasCatalog,
		Sequence = status.Sequence,
		SignedAt = status.SignedAt,
		FetchedAt = status.FetchedAt,
		LastSuccessAt = status.LastSuccessAt,
		LastAttemptAt = status.LastAttemptAt,
		Refreshing = status.Refreshing,
		Stale = status.Stale,
		LastError = status.LastError?.ToString(),
		LastErrorMessage = status.LastErrorMessage
	};
}
