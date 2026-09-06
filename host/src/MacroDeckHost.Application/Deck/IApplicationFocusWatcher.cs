namespace MacroDeckHost.Application.Deck;

/// <summary>
/// Publishes focus-change events. The first element observed by an enumeration is the focus state at
/// subscription time; further elements arrive on each focus change and may repeat a previously-seen
/// <see cref="FocusedApplication.ProcessId"/>. Elements are never null. The enumeration owns whatever
/// native subscription backs it - established before the first element is produced, and torn down when
/// the enumerator is disposed or its cancellation token fires. Capability
/// (<see cref="IsSupported"/>/<see cref="UnsupportedReason"/>) is fixed at construction and can be read
/// without starting a subscription. Only one enumeration may be active at a time: starting a second one
/// concurrently throws <see cref="InvalidOperationException"/>; starting a new one after a prior
/// enumeration has ended is supported.
/// </summary>
public interface IApplicationFocusWatcher
{
	bool IsSupported { get; }

	string? UnsupportedReason { get; }

	IAsyncEnumerable<FocusedApplication> WatchAsync(CancellationToken cancellationToken);
}
