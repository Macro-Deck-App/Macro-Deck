namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class StoreRegistryStatusBody
{
	public bool HasCatalog { get; set; }

	public long Sequence { get; set; }

	public DateTimeOffset? SignedAt { get; set; }

	public DateTimeOffset? FetchedAt { get; set; }

	public DateTimeOffset? LastSuccessAt { get; set; }

	public DateTimeOffset? LastAttemptAt { get; set; }

	public bool Refreshing { get; set; }

	public bool Stale { get; set; }

	public string? LastError { get; set; }

	public string? LastErrorMessage { get; set; }
}
