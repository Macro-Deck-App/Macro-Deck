using MacroDeck.Sdk.Deprecation;

namespace MacroDeck.Sdk.Widgets;

/// <summary>Changes persisted widget appearance through the host.</summary>
public interface IWidgetApi
{
	/// <summary>Returns user widgets with profile and folder context for pickers.</summary>
	IReadOnlyList<WidgetTargetInfo> GetWidgets();

	/// <summary>Whether the widget currently exists.</summary>
	bool Exists(string widgetId);

	/// <summary>
	/// Applies an appearance change. Returns <c>false</c> for unknown widgets, empty patches, or unsupported properties.
	/// </summary>
	Task<bool> ApplyAsync(WidgetAppearanceRequest request, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets the target's active state by id. Fails - rather than silently doing nothing - when a state
	/// provider or a state mapping is authoritative for the target, or when the id does not name one of
	/// its states.
	///
	/// <para>
	/// Defaults to <see cref="WidgetStateWriteError.NotFound" /> so an implementation written against an
	/// earlier SDK keeps compiling and running. An out-of-process plugin currently gets that default:
	/// there is no wire operation for an explicit state write yet, so the state a plugin can influence
	/// is the one it supplies as a state provider.
	/// </para>
	/// </summary>
	Task<WidgetStateWriteResult> SetStateAsync(string widgetId,
		string stateId,
		CancellationToken cancellationToken = default)
		=> Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));

	/// <summary>
	/// Advances the target to the next state in its declared order, wrapping past the last. Same
	/// failure modes and the same default as <see cref="SetStateAsync" />.
	/// </summary>
	Task<WidgetStateWriteResult> AdvanceStateAsync(string widgetId, CancellationToken cancellationToken = default)
		=> Task.FromResult(WidgetStateWriteResult.Failed(WidgetStateWriteError.NotFound));

	/// <summary>
	/// Tells the host the icon <paramref name="actionId" /> provides has changed, so widgets following it
	/// re-read it now rather than at the next poll. A hint, not a guarantee: the host still compares the
	/// reported identity before refetching bytes. <paramref name="actionId" /> is the action's declared
	/// local id, not a configured instance - the host qualifies it with the calling integration, which is
	/// also the trust boundary, so a plugin can only invalidate its own actions.
	///
	/// <para>
	/// Defaults to <see cref="Task.CompletedTask" /> so an implementation written against an earlier SDK
	/// keeps compiling and running. An out-of-process plugin calling this against an older host gets
	/// silence, exactly as <see cref="SetStateAsync" /> documents for its own default.
	/// </para>
	/// </summary>
	Task InvalidateIconAsync(string actionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>A widget exposed to integration pickers.</summary>
public sealed class WidgetTargetInfo
{
	public required string Id { get; init; }

	/// <summary>Display label, falling back to the widget type when necessary.</summary>
	public required string Label { get; init; }

	/// <summary>Profile and folder path used to distinguish similarly named widgets.</summary>
	public required string Location { get; init; }

	/// <summary>Widget type name.</summary>
	public required string Type { get; init; }

	/// <summary>
	/// Whether the widget exposes separate On and Off appearances.
	/// </summary>
	[Obsolete("Use States.Count > 1. Removed in Macro Deck 4.0.0.")]
	[MacroDeckDeprecated("3.0.0",
		"4.0.0",
		"Read States.Count > 1 instead of this collapsed on/off flag.",
		Replacement = "MacroDeck.Sdk.Widgets.WidgetTargetInfo.States")]
	public bool HasOnOffStates { get; init; }

	/// <summary>
	/// The states this widget can be in, in display order. Empty when the widget has exactly one
	/// appearance - a toggle-mode button reports two, a state-provider-bound button reports whatever
	/// its provider last declared.
	/// </summary>
	public IReadOnlyList<WidgetStateInfo> States { get; init; } = [];

	/// <summary>The id of whichever of <see cref="States" /> the widget is currently showing, or
	/// <c>null</c> when <see cref="States" /> is empty.</summary>
	public string? CurrentStateId { get; init; }

	/// <summary>Appearance properties supported by this widget type.</summary>
	public IReadOnlyCollection<WidgetAppearanceProperty> AppearanceProperties { get; init; } = [];

	/// <summary>
	/// Whether an icon-provider action is currently authoritative for this widget's rendered icon. While
	/// true, an icon-bearing <see cref="WidgetAppearancePatch" /> is not applied - <see cref="ApplyAsync" />
	/// drops the icon property from the patch exactly as it drops a property the target type does not
	/// support, and reports whether anything else in the same patch still changed.
	/// </summary>
	public bool HasActiveIconProvider { get; init; }
}

/// <summary>Describes an appearance change for a widget and state.</summary>
public sealed class WidgetAppearanceRequest
{
	public required string WidgetId { get; init; }

	public required WidgetAppearancePatch Patch { get; init; }

	/// <summary>
	/// Which of a toggle-mode action button's two appearances to change.
	/// </summary>
	[Obsolete(
		"Use StateIds with a stable state id, WidgetStates.Current or WidgetStates.All. Removed in Macro Deck 4.0.0.")]
	[MacroDeckDeprecated("3.0.0",
		"4.0.0",
		"Set StateIds to a stable state id, WidgetStates.Current or WidgetStates.All instead.",
		Replacement = "MacroDeck.Sdk.Widgets.WidgetAppearanceRequest.StateIds")]
	public WidgetStateSelector State { get; init; } = WidgetStateSelector.Current;

	/// <summary>
	/// Which states to change, by id - a real state id, <see cref="WidgetStates.Current" /> or
	/// <see cref="WidgetStates.All" />. Defaults to <see cref="WidgetStates.Current" />.
	///
	/// <para>
	/// Precedence against the deprecated <see cref="State" />: a <see cref="StateIds" /> left at its
	/// default defers to <see cref="State" /> whenever that is not <see cref="WidgetStateSelector.Current" /> -
	/// <see cref="WidgetStateSelector.On" /> and <see cref="WidgetStateSelector.Off" /> resolve to the
	/// legacy "on"/"off" ids a toggle-mode button already stores its two appearances under, and
	/// <see cref="WidgetStateSelector.Both" /> resolves to <see cref="WidgetStates.All" />. Setting
	/// <see cref="StateIds" /> to anything other than its default always wins outright. Leaving both at
	/// their defaults means the current state, exactly as leaving everything unset always has. See
	/// <see cref="ResolveStateIds" />, which every consumer should call instead of reading either
	/// property directly - that is what keeps a request an older plugin built with only
	/// <see cref="State" /> set behaving exactly as one built with <see cref="StateIds" />.
	/// </para>
	/// </summary>
	public IReadOnlyCollection<string> StateIds { get; init; } = [WidgetStates.Current];

	/// <summary>Properties to remove from stored appearance data.</summary>
	public IReadOnlyCollection<WidgetAppearanceProperty> ClearProperties { get; init; } = [];

	/// <summary>
	/// <see cref="StateIds" /> resolved against the deprecated <see cref="State" /> selector - see the
	/// precedence rule documented on <see cref="StateIds" />.
	/// </summary>
	public IReadOnlyCollection<string> ResolveStateIds()
	{
		if (!(StateIds.Count == 1 && WidgetStates.IsCurrent(StateIds.First())))
		{
			return StateIds;
		}

#pragma warning disable CS0618 // Reading the deprecated selector here is the translation this member exists to perform.
		var legacy = State;

		return legacy switch
		{
			WidgetStateSelector.On => ["on"],
			WidgetStateSelector.Off => ["off"],
			WidgetStateSelector.Both => [WidgetStates.All],
			_ => [WidgetStates.Current]
		};
#pragma warning restore CS0618
	}
}
