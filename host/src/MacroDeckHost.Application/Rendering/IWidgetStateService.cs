using MacroDeck.Sdk.Actions;
using MacroDeck.Localization;

namespace MacroDeckHost.Application.Rendering;

public sealed record WidgetStateOption(string Id, LocalizedText Label);

/// <summary>The state an action button is showing right now, and the live set it was chosen from.</summary>
/// <param name="States">What the client is told the button can be in - id and label only.</param>
public sealed record WidgetStateResolution(
	string StateId,
	LocalizedText StateLabel,
	IReadOnlyList<WidgetStateOption> States,
	bool ProviderSetChanged)
{
	/// <summary>
	/// The provider's own declaration behind <see cref="States" />, carried separately because it also
	/// holds each state's default appearance, which adoption needs and the client push does not. Empty
	/// unless a provider answered.
	/// </summary>
	public IReadOnlyList<ActionStateDefinition> ProvidedStates { get; init; } = [];

	/// <summary>
	/// Identifies the optimistic entry that produced this resolution. Reconciliation must discard the
	/// result if that entry is no longer current before the state is committed or published.
	/// </summary>
	public WidgetOptimisticStateVersion? OptimisticState { get; init; }
}

public interface IWidgetStateService
{
	/// <summary>
	/// Resolves the state of an action button. Null means the widget has no state at all - not an
	/// action button, or State Mode disabled.
	/// </summary>
	Task<WidgetStateResolution?> Resolve(Guid widgetId, CancellationToken cancellationToken = default);

	/// <summary>
	/// How often the widget's state-provider action asked to be polled, or null when the widget has no
	/// usable provider right now. Synchronous and side-effect free - it never reads the provider itself,
	/// only the declared interval, so the background poller can call it on every tick without cost.
	/// </summary>
	TimeSpan? GetProviderPollInterval(Guid widgetId);
}
