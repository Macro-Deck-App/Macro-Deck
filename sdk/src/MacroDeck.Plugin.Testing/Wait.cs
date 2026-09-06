namespace MacroDeck.Plugin.Testing;

/// <summary>
/// Polls a condition on real wall-clock time until it is true or a deadline passes.
///
/// <para>
/// Deliberately throws <see cref="PluginTestTimeoutException" /> rather than calling into a test
/// framework's own assertion API: this package references no test framework, so the same helper works
/// unmodified under NUnit, xUnit or anything else. A timeout here is always a genuine failure to
/// observe something happen - never use it to wait out a capability invocation's own deadline, which is
/// wall-clock by construction and belongs to <see cref="CapabilityInvokeOptions" /> instead.
/// </para>
/// </summary>
public static class Wait
{
	private static readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(20);

	/// <summary>
	/// Polls <paramref name="condition" /> until it returns <see langword="true" /> or
	/// <paramref name="timeout" /> (default 10 seconds) elapses.
	/// </summary>
	/// <param name="condition">Checked repeatedly; must be cheap and side-effect free.</param>
	/// <param name="timeout">How long to keep polling. Defaults to 10 seconds.</param>
	/// <param name="because">
	/// Included in the exception message on failure, so a timeout reads as what was being waited for
	/// rather than a bare "condition was not met".
	/// </param>
	/// <exception cref="PluginTestTimeoutException">
	/// <paramref name="condition" /> never returned <see langword="true" /> before the deadline.
	/// </exception>
	public static async Task UntilAsync(Func<bool> condition, TimeSpan? timeout = null, string? because = null)
	{
		ArgumentNullException.ThrowIfNull(condition);

		var budget = timeout ?? _defaultTimeout;
		var deadline = DateTime.UtcNow + budget;

		while (true)
		{
			if (condition())
			{
				return;
			}

			if (DateTime.UtcNow >= deadline)
			{
				throw new PluginTestTimeoutException(Describe(budget, because));
			}

			await Task.Delay(_pollInterval).ConfigureAwait(false);
		}
	}

	private static string Describe(TimeSpan budget, string? because)
		=> because is null
			? $"The condition was not met within {budget}."
			: $"The condition was not met within {budget}: {because}";
}
