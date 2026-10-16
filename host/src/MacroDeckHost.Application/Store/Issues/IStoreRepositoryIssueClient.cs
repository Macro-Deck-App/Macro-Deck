namespace MacroDeckHost.Application.Store.Issues;

public interface IStoreRepositoryIssueClient
{
	Task<StoreRepositoryIssueFetch> GetOpenIssues(
		StoreRepository repository,
		string? etag,
		CancellationToken cancellationToken);
}

public enum StoreRepositoryIssueFetchStatus
{
	Ok,
	NotModified,
	NotFound,
	IssuesDisabled,
	RateLimited,
	Unavailable
}

public sealed record StoreRepositoryIssueFetch
{
	public required StoreRepositoryIssueFetchStatus Status { get; init; }

	public IReadOnlyList<StoreRepositoryIssue> Issues { get; init; } = [];

	public bool Truncated { get; init; }

	public string? ETag { get; init; }
}

public sealed record StoreRepositoryIssue
{
	public required int Number { get; init; }

	public required string Title { get; init; }

	public required string Url { get; init; }

	public string? Author { get; init; }

	public DateTimeOffset? CreatedAt { get; init; }

	public int Comments { get; init; }
}
