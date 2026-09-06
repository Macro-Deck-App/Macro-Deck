namespace MacroDeck.Plugin.Testing.Tests.UnitTests.Support;

/// <summary>
/// Polls <see cref="PluginUnderTest.ProbeHealthAsync" /> until <paramref name="condition" /> holds.
/// Works for either hosting mode - <c>ExternalPlugin</c> and <c>InProcessPlugin</c> both derive from
/// <see cref="PluginUnderTest" />.
///
/// <para>
/// <c>MacroDeckTestHost.WaitForSessionAsync</c>, <c>HostAsync</c> and <c>LaunchAsync</c> all signal from
/// the host's own side of an exchange - the moment the host sent <c>session.welcome</c>, or the moment
/// its own health probe first answered. None of them waits for the plugin to have actually processed
/// that signal and updated its own <c>/_macrodeck/ready</c> state, which needs at least one more network
/// round trip, loopback or not. Probing exactly once immediately after any of them is a real,
/// reproducible race, not a hang - this polls instead of asserting on the first snapshot.
/// </para>
/// </summary>
internal static class HealthPolling
{
	public static async Task<PluginHealthReport> WaitUntilAsync(
		PluginUnderTest plugin,
		Func<PluginHealthReport, bool> condition,
		TimeSpan? timeout = null)
	{
		var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
		var last = await plugin.ProbeHealthAsync();

		while (!condition(last) && DateTime.UtcNow < deadline)
		{
			await Task.Delay(50);
			last = await plugin.ProbeHealthAsync();
		}

		return last;
	}
}
