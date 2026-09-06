using MacroDeck.Localization;

namespace MacroDeck.Sdk.Actions;

/// <summary>
/// How a button should look in a state before the user styles it. Every field is optional and is only
/// ever applied to a state the button is adopting for the first time - re-reading a provider never
/// restyles a state the user has already configured, so a provider cannot overwrite their work.
/// </summary>
public sealed record ActionStateAppearance
{
	/// <summary>
	/// Text the button shows in this state. Defaults to the state's own
	/// <see cref="ActionStateDefinition.Label" /> when null; set it to the empty string for a state
	/// that should show no text at all (an icon-only state).
	/// </summary>
	public string? Label { get; init; }

	/// <summary>Background colour, as a CSS hex colour (<c>#rrggbb</c>). Null leaves the button's default.</summary>
	public string? BackgroundColor { get; init; }

	/// <summary>Label colour, as a CSS hex colour (<c>#rrggbb</c>). Null leaves the button's default.</summary>
	public string? LabelColor { get; init; }

	/// <summary>
	/// Icon to show in this state, named in the host's icon id format. Leave it null unless the id is
	/// one the host can actually resolve - an unknown id is stored on the button as given and renders
	/// as no icon, which the user then has to clear by hand.
	/// </summary>
	public string? IconId { get; init; }
}

/// <summary>One state a button bound to a state-provider action can show.</summary>
/// <param name="Id">
/// Stable across reconfiguration - lowercase kebab-case, following the declared local id grammar - because
/// a state id is persisted in a user's profile once a button adopts it.
/// </param>
/// <param name="Label">Display label for this state.</param>
public sealed record ActionStateDefinition(string Id, LocalizedText Label)
{
	/// <summary>
	/// What the button should look like in this state until the user styles it themselves. Null means
	/// the button keeps its own defaults, except for the label, which falls back to <see cref="Label" />.
	/// </summary>
	public ActionStateAppearance? DefaultAppearance { get; init; }
}

/// <summary>The live state set and current state of a state-provider action instance.</summary>
/// <param name="States">Never empty - a provider with nothing to report returns <c>null</c> instead.</param>
/// <param name="ActiveStateId">
/// Names one of <see cref="States" /> when non-null. Null means no state is currently active.
/// </param>
public sealed record ActionStateSnapshot(IReadOnlyList<ActionStateDefinition> States, string? ActiveStateId);

/// <summary>
/// Implemented by actions whose configured instance can drive a button's N-state appearance: the
/// instance declares the states it can be in and which one is current, and a button adopts it as its
/// state provider to follow along.
///
/// <para>
/// <see cref="GetActionStateAsync" /> answers for the <em>configured instance</em>, not the action type:
/// everything is answered from the <paramref name="parameters" /> that instance was configured with, so
/// the same action may appear on several buttons with different configurations and each answers for
/// itself. Which instance is a given
/// button's provider is the host's decision alone and is never asserted by the action; at most one
/// instance is taken up on any one button.
/// </para>
/// </summary>
public interface IStateProviderActionDefinition
{
	/// <summary>
	/// Returns the current state set and active state for the given configured parameters, or
	/// <c>null</c> when no state is available (the target is unconfigured, disconnected, or gone).
	///
	/// <para>
	/// Called on two different cadences and must be safe on both: once per settled editor draft while
	/// the user is still configuring the action - with a partially filled, possibly half-typed
	/// parameter set, so this must not throw on a missing or incomplete value - and repeatedly at
	/// <see cref="StatePollInterval" /> while a button follows the instance.
	/// </para>
	///
	/// <para>
	/// Must be side-effect free: answer from state already held, honour <paramref name="cancellationToken" />,
	/// and never connect or authenticate to produce an answer.
	/// </para>
	///
	/// <para>
	/// A returned snapshot always carries a non-empty <c>States</c>. Returning a different state set than
	/// last time is allowed - it is how a provider whose states depend on live conditions reports them -
	/// and the host re-adopts by state id, keeping the appearance configured for every id that survived.
	/// </para>
	/// </summary>
	Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken);

	/// <summary>
	/// How often a button following this instance should poll while it is on screen. A request, not a
	/// guarantee: the host clamps it and may read less often - or not at all - while nothing displays
	/// the button.
	/// </summary>
	TimeSpan StatePollInterval => TimeSpan.FromSeconds(2);
}
