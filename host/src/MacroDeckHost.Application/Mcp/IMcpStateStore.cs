namespace MacroDeckHost.Application.Mcp;

public interface IMcpStateStore
{
	public const string JournalDocument = "journal";
	public const string GrantsDocument = "secret-grants";
	public const string DeferredScrubDocument = "deferred-scrub";

	T? Read<T>(string document)
		where T : class;

	void Write<T>(string document, T value)
		where T : class;

	void Delete(string document);
}

public interface IMcpSecretHolders
{
	Task<IReadOnlySet<Guid>> HeldElsewhere(IReadOnlyCollection<Guid> candidates, CancellationToken cancellationToken);
}
