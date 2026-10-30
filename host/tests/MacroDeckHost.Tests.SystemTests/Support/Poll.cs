namespace MacroDeckHost.Tests.SystemTests.Support;

// The asynchronous counterpart to MacroDeck.Plugin.Testing's Wait.UntilAsync, for conditions that need
// an HTTP round trip to evaluate.
internal static class Poll
{
	private static readonly TimeSpan _interval = TimeSpan.FromMilliseconds(250);

	public static async Task UntilAsync(Func<Task<bool>> condition, TimeSpan timeout, string because)
	{
		await ForAsync(async () => await condition() ? string.Empty : null, timeout, because);
	}

	public static async Task<T> ForAsync<T>(Func<Task<T?>> probe, TimeSpan timeout, string because)
		where T : class
	{
		var deadline = DateTime.UtcNow + timeout;
		while (true)
		{
			if (await probe() is { } value)
			{
				return value;
			}

			if (DateTime.UtcNow >= deadline)
			{
				throw new TimeoutException($"Not met within {timeout}: {because}.");
			}

			await Task.Delay(_interval);
		}
	}
}
