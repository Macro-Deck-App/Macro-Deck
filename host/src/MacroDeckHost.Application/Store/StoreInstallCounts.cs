using MacroDeckHost.Application.Store.Reviews;

namespace MacroDeckHost.Application.Store;

internal static class StoreInstallCounts
{
	// Null when the Platform cannot answer: callers fall back to name order instead of failing.
	public static async Task<Dictionary<string, long>?> Fetch(IStoreReviewService reviews,
		IEnumerable<string> ids,
		CancellationToken cancellationToken)
	{
		var installs = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
		foreach (var chunk in ids.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(StorePlatformOptions.MaxIdsPerRequest))
		{
			var response = await reviews.GetInstalls(chunk, cancellationToken);
			if (!response.Available)
			{
				return null;
			}

			foreach (var (id, count) in response.Installs)
			{
				installs[id] = count;
			}
		}

		return installs;
	}
}
