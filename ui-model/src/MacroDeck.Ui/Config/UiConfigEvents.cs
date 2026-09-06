namespace MacroDeck.Ui.Config;

/// <summary>
/// The event names the configuration profile ships. Twelve, and every one of them is an interaction the
/// existing editor and config flow dialog already have: editing a field, the dialog's Continue and Cancel
/// buttons, going back a step, following a link, the advanced-configuration switch, opening a picker, typing
/// into an autocomplete, the reload button next to a dynamic choice, and the add and remove buttons on an
/// array.
///
/// <para>
/// Absent on purpose: <c>click</c>, <c>input</c>, <c>blur</c> and above all <c>focus</c>. Focus is per-client
/// state - in a shared session one user's caret would move another's - and the model forbids it from the
/// tree; an event that reports it would be the same leak arriving from the other direction.
/// </para>
///
/// <para>
/// <b>No IsKnown, deliberately</b>, for the reason <see cref="UiConfigPrimitives" /> gives: a newer renderer
/// sending a newer event name must be ignored, not treated as an error, which is what
/// <see cref="Runtime.UiView.Dispatch" /> does.
/// </para>
/// </summary>
public static class UiConfigEvents
{
	/// <summary>An input's value changed. The payload is the new value; a writable binding is the write path,
	/// so an input accepts this without a handler.</summary>
	public const string Change = "change";

	/// <summary>A step or flow was submitted - the dialog's Continue.</summary>
	public const string Submit = "submit";

	/// <summary>The flow was abandoned - the dialog's Cancel.</summary>
	public const string Cancel = "cancel";

	/// <summary>The previous step was requested.</summary>
	public const string Back = "back";

	/// <summary>A link was followed. Raised as an event rather than navigated as an href because a link opens
	/// through the shell, not inside the surface.</summary>
	public const string Activate = "activate";

	/// <summary>A collapsed section was opened.</summary>
	public const string Expand = "expand";

	/// <summary>An open section was closed.</summary>
	public const string Collapse = "collapse";

	/// <summary>A picker was opened, which is when a dynamic option list is first worth fetching.</summary>
	public const string Open = "open";

	/// <summary>The filter text of an option list changed. The payload is the text.</summary>
	public const string Filter = "filter";

	/// <summary>A refetch of an option list was requested.</summary>
	public const string Reload = "reload";

	/// <summary>An item was added to an array.</summary>
	public const string Add = "add";

	/// <summary>An item was removed from an array. The payload names which.</summary>
	public const string Remove = "remove";

	/// <summary>The event names this package ships, in declaration order. Not exhaustive - see the type's
	/// remarks.</summary>
	public static readonly IReadOnlyList<string> WellKnown =
	[
		Change, Submit, Cancel, Back, Activate, Expand, Collapse, Open, Filter, Reload, Add, Remove,
	];
}
