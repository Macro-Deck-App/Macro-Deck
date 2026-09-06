namespace MacroDeck.Ui.Model.Surfaces;

/// <summary>
/// The well-known <see cref="UiSurface.SessionMode" /> values this package ships names for today.
/// Deliberately exposes no <c>IsKnown</c>, for the same reason <see cref="UiSurfaceKinds" /> does not:
/// "exclusive-with-observers" and "read-only attach" are plausible third answers, and closing the set
/// here would violate the same openness this model states for surface kinds. The issue asks only that
/// shared and exclusive be expressible, not that the set of session modes be closed.
/// </summary>
public static class UiSessionModes
{
	/// <summary>N clients receive the same patches. Per-client state - focus, scroll, hover - is
	/// client-local and must never appear in the tree.</summary>
	public const string Shared = "shared";

	/// <summary>One client owns the session, so the tree may carry entered values and step state.</summary>
	public const string Exclusive = "exclusive";

	/// <summary>The session modes this package ships names for. Not exhaustive - see the type's
	/// remarks.</summary>
	public static readonly IReadOnlyList<string> WellKnown = [Shared, Exclusive];
}
