using System.Collections.Concurrent;
using MacroDeck.Plugin.Protocol.Logging;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// Everything a plugin under test has logged, in arrival order. Structure is preserved rather than
/// collapsed into plain text, so a test can assert on <see cref="CollectedLogEvent.SourceContext" /> or
/// a property without parsing a rendered string back apart.
/// </summary>
public sealed class PluginLogCollector
{
	private readonly ConcurrentQueue<CollectedLogEvent> _events = new();
	private int _dropped;

	/// <summary>Every event collected so far, in arrival order.</summary>
	public IReadOnlyList<CollectedLogEvent> Events => [.. _events];

	/// <summary>
	/// How many events a plugin's own sink reported dropping - see <c>LogPublishPayload.Dropped</c>.
	/// Non-zero means the plugin fell behind, not that anything here is missing an entry for it: a
	/// dropped event was never collected because it was never sent.
	/// </summary>
	public int Dropped => Volatile.Read(ref _dropped);

	/// <summary>
	/// Events at or above <paramref name="level" />'s severity, ordered as <see cref="LogLevels.All" />
	/// defines it - so <c>AtLeast(LogLevels.Warning)</c> includes <c>Warning</c>, <c>Error</c> and
	/// <c>Fatal</c> but not <c>Information</c>.
	/// </summary>
	/// <exception cref="ArgumentException"><paramref name="level" /> is not one of <see cref="LogLevels" />.</exception>
	public IReadOnlyList<CollectedLogEvent> AtLeast(string level)
	{
		var threshold = IndexOf(level);
		return [.. Events.Where(collected => IndexOf(collected.Level) >= threshold)];
	}

	/// <summary>Events carrying a property named <paramref name="key" /> whose value equals <paramref name="value" />.</summary>
	public IReadOnlyList<CollectedLogEvent> WithProperty(string key, string value)
		=>
		[
			.. Events.Where(collected
				=> collected.Properties.TryGetValue(key, out var actual) &&
				string.Equals(actual, value, StringComparison.Ordinal))
		];

	/// <summary>
	/// Waits until an event matching <paramref name="predicate" /> has been collected.
	/// </summary>
	/// <exception cref="PluginTestTimeoutException">No matching event arrived before the deadline.</exception>
	public Task WaitForAsync(Func<CollectedLogEvent, bool> predicate, TimeSpan? timeout = null)
	{
		ArgumentNullException.ThrowIfNull(predicate);
		return Wait.UntilAsync(() => Events.Any(predicate), timeout, because: "no matching log event was collected");
	}

	internal void Record(CollectedLogEvent logEvent) => _events.Enqueue(logEvent);

	internal void RecordDropped(int count) => Interlocked.Add(ref _dropped, count);

	private static int IndexOf(string level)
	{
		var levels = LogLevels.All;

		for (var index = 0; index < levels.Count; index++)
		{
			if (string.Equals(levels[index], level, StringComparison.Ordinal))
			{
				return index;
			}
		}

		throw new ArgumentException($"'{level}' is not one of {nameof(LogLevels)}.", nameof(level));
	}
}
