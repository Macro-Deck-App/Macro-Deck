namespace MacroDeck.Plugin.Protocol.Reconnection;

/// <summary>
/// Full-jitter exponential backoff for reconnect attempts: 1 s initial, 30 s max, factor 2. The jitter
/// fraction is injected rather than sampled internally, so <see cref="DelayFor" /> stays a pure,
/// deterministically testable function.
/// </summary>
public static class ReconnectPolicy
{
	public static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(1);

	public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

	public const double BackoffFactor = 2;

	/// <param name="attempt">1-based reconnect attempt number.</param>
	/// <param name="jitterSample">A sample in <c>[0, 1]</c>, e.g. from a caller-owned random source.</param>
	public static TimeSpan DelayFor(int attempt, double jitterSample)
	{
		if (attempt < 1)
		{
			throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "Attempt must be at least 1.");
		}

		if (jitterSample is < 0 or > 1)
		{
			throw new ArgumentOutOfRangeException(nameof(jitterSample),
				jitterSample,
				"Jitter sample must be within [0, 1].");
		}

		var exponential = InitialDelay.TotalMilliseconds * Math.Pow(BackoffFactor, attempt - 1);
		var capped = Math.Min(exponential, MaxDelay.TotalMilliseconds);
		return TimeSpan.FromMilliseconds(capped * jitterSample);
	}
}
