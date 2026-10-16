using MacroDeckHost.Application.Ui.Transport.Messages.Store;

namespace MacroDeckHost.Application.Store.Issues;

public interface IStoreRepositoryIssueService
{
	Task<GetStoreRepositoryIssuesResponse> GetIssues(string? repository, CancellationToken cancellationToken);
}

public sealed class StoreRepositoryIssueService : IStoreRepositoryIssueService
{
	public const int MaxCachedRepositories = 256;

	public static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);
	public static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(15);

	private readonly IStoreRepositoryIssueClient _client;
	private readonly TimeProvider _timeProvider;
	private readonly Lock _gate = new();
	private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Task<CacheEntry>> _inFlight = new(StringComparer.Ordinal);

	public StoreRepositoryIssueService(IStoreRepositoryIssueClient client, TimeProvider timeProvider)
	{
		_client = client;
		_timeProvider = timeProvider;
	}

	public async Task<GetStoreRepositoryIssuesResponse> GetIssues(
		string? repository,
		CancellationToken cancellationToken)
	{
		if (StoreRepository.Parse(repository) is not { } parsed)
		{
			return new GetStoreRepositoryIssuesResponse { Error = StoreRepositoryIssuesError.NoRepository };
		}

		Task<CacheEntry>? pending;
		lock (_gate)
		{
			_cache.TryGetValue(parsed.CacheKey, out var cached);
			if (cached is not null && cached.FreshUntil > _timeProvider.GetUtcNow())
			{
				return ToResponse(parsed, cached);
			}

			// Task.Run keeps a synchronously completing fetch from leaving its entry before it is registered.
			if (!_inFlight.TryGetValue(parsed.CacheKey, out pending))
			{
				var lastGood = cached?.Issues;
				pending = Task.Run(() => Fetch(parsed, lastGood), CancellationToken.None);
				_inFlight[parsed.CacheKey] = pending;
			}
		}

		return ToResponse(parsed, await pending.WaitAsync(cancellationToken));
	}

	private async Task<CacheEntry> Fetch(StoreRepository repository, CachedIssues? lastGood)
	{
		CacheEntry? entry = null;
		try
		{
			var fetched = await _client.GetOpenIssues(repository, lastGood?.ETag, CancellationToken.None);
			entry = ToEntry(fetched, lastGood, _timeProvider.GetUtcNow());
			return entry;
		}
		finally
		{
			lock (_gate)
			{
				_inFlight.Remove(repository.CacheKey);
				if (entry is not null)
				{
					Store(repository.CacheKey, entry);
				}
			}
		}
	}

	private void Store(string key, CacheEntry entry)
	{
		_cache[key] = entry;
		while (_cache.Count > MaxCachedRepositories)
		{
			var oldest = _cache.MinBy(pair => pair.Value.StoredAt).Key;
			_cache.Remove(oldest);
		}
	}

	private static CacheEntry ToEntry(
		StoreRepositoryIssueFetch fetched,
		CachedIssues? lastGood,
		DateTimeOffset now)
	{
		return fetched.Status switch
		{
			StoreRepositoryIssueFetchStatus.Ok => new CacheEntry(null,
				new CachedIssues(fetched.Issues, fetched.Truncated, fetched.ETag),
				now + CacheLifetime,
				now),
			StoreRepositoryIssueFetchStatus.NotModified when lastGood is not null => new CacheEntry(null,
				lastGood,
				now + CacheLifetime,
				now),
			StoreRepositoryIssueFetchStatus.NotFound => new CacheEntry(StoreRepositoryIssuesError.NotFound,
				lastGood,
				now + CacheLifetime,
				now),
			StoreRepositoryIssueFetchStatus.IssuesDisabled => new CacheEntry(StoreRepositoryIssuesError.IssuesDisabled,
				lastGood,
				now + CacheLifetime,
				now),
			StoreRepositoryIssueFetchStatus.RateLimited => new CacheEntry(StoreRepositoryIssuesError.RateLimited,
				lastGood,
				now + FailureCooldown,
				now),
			_ => new CacheEntry(StoreRepositoryIssuesError.Unavailable, lastGood, now + FailureCooldown, now)
		};
	}

	private static GetStoreRepositoryIssuesResponse ToResponse(StoreRepository repository, CacheEntry entry)
	{
		if (entry.Error is { } error)
		{
			return new GetStoreRepositoryIssuesResponse
			{
				NewIssueUrl = error == StoreRepositoryIssuesError.IssuesDisabled ? null : repository.NewIssueUrl,
				Error = error
			};
		}

		var issues = entry.Issues?.Issues ?? [];
		return new GetStoreRepositoryIssuesResponse
		{
			Issues = issues
				.Select(issue => new StoreRepositoryIssueBody
				{
					Number = issue.Number,
					Title = issue.Title,
					Url = issue.Url,
					Author = issue.Author,
					CreatedAt = issue.CreatedAt,
					Comments = issue.Comments
				})
				.ToList(),
			OpenCount = issues.Count,
			Truncated = entry.Issues?.Truncated ?? false,
			NewIssueUrl = repository.NewIssueUrl
		};
	}

	private sealed record CachedIssues(IReadOnlyList<StoreRepositoryIssue> Issues, bool Truncated, string? ETag);

	private sealed record CacheEntry(
		StoreRepositoryIssuesError? Error,
		CachedIssues? Issues,
		DateTimeOffset FreshUntil,
		DateTimeOffset StoredAt);
}
