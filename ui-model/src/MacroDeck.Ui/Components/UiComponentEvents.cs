namespace MacroDeck.Ui.Components;

/// <summary>
/// The event names the widget profile ships, in three families: the pair <see cref="UiSlider" />
/// uses for a continuous gesture the user works and then lets go of - which
/// <see cref="UiTextField" /> reuses verbatim for typing - the four <see cref="UiButton" />
/// uses for a press, and the one <see cref="UiList" /> uses to ask for more.
///
/// <para>
/// <b>Why the pair, rather than one event.</b> Splitting the gesture is what lets a producer act on the
/// value the user landed on without giving up a display that follows their finger: every intermediate
/// level arrives as <see cref="Adjust" />, and the one the interaction ended on arrives as
/// <see cref="Change" />. A producer for which acting on an intermediate value is a disruption - seeking a
/// track through every waypoint on the way to the target - acts only on <see cref="Change" /> while still
/// showing the rest.
/// </para>
///
/// <para>
/// <b>Why <see cref="Change" /> is the final value and not the streaming one.</b> A reader that ignores
/// <see cref="Adjust" /> still delivers a correct result and merely does not stream; a reader that ignored
/// a differently-named terminal event would lose the value the user chose. The failure mode of the
/// unimplemented half has to be the harmless one. It also keeps <c>change</c> meaning what
/// <see cref="Config.UiConfigEvents.Change" /> means, since the string is shared.
/// </para>
///
/// <para>
/// <b>No IsKnown, deliberately</b>, for the reason <see cref="UiComponents" /> gives: a newer renderer
/// sending a newer event name must be ignored rather than treated as an error, which is what
/// <see cref="Runtime.UiView.Dispatch" /> does.
/// </para>
/// </summary>
public static class UiComponentEvents
{
	/// <summary>The value a control ended on, sent once when the interaction ends. The payload is the new
	/// level as a bare number. A reader that offers interaction always sends it, even when the level equals
	/// the last <see cref="Adjust" />.</summary>
	public const string Change = "change";

	/// <summary>An intermediate value while the user is still working the control. The payload is the same
	/// bare number. A reader sends it no more than ten times a second, and never after the
	/// <see cref="Change" /> that ended the interaction.</summary>
	public const string Adjust = "adjust";

	/// <summary>The press a button exists for: one the user completed without holding. Carries no payload.
	///
	/// <para>
	/// <b>Why this is the primary name</b>, for the reason <see cref="Change" /> gives: a reader that
	/// implements only one press name implements this one and runs the button's main flow correctly. A
	/// reader that implemented only the boundaries would leave the user's press doing nothing at all,
	/// which is the failure mode that must not be the reachable one.
	/// </para></summary>
	public const string Press = "press";

	/// <summary>A press still held when the reader's long-press threshold elapsed. Carries no payload. Sent
	/// at most once per interaction, and never together with <see cref="Press" /> - a held press is a
	/// different action, not a slow one.</summary>
	public const string LongPress = "long-press";

	/// <summary>The moment a press began. Carries no payload. A boundary, not a phase of
	/// <see cref="Press" />: it exists because a producer may drive something for as long as the finger is
	/// down, and a reader must never infer <see cref="Press" /> from it.</summary>
	public const string PressStart = "press-start";

	/// <summary>The moment a press ended, however it ended. Carries no payload. Exactly one follows each
	/// <see cref="PressStart" />, including when the pointer left the element or the gesture was cancelled -
	/// so a producer holding something down always gets its release.</summary>
	public const string PressEnd = "press-end";

	/// <summary>
	/// The user has reached the end of what a <see cref="UiList" /> currently holds. The payload is
	/// the index of the furthest child brought into view, as a bare number.
	///
	/// <para>
	/// <b>An index rather than a page.</b> Paging is the producer's arithmetic, and a reader that counted
	/// pages would have to agree with it on a page size that only the producer knows. What a reader can
	/// state without ambiguity is how far down its own children the user has come; what that means is the
	/// producer's to decide.
	/// </para>
	///
	/// <para>
	/// A reader sends it no more than twice a second, and only for an index beyond the furthest one it has
	/// already sent for the same list - so a user scrolling back up asks for nothing, and a producer that
	/// appends nothing is not asked again for the same position.
	/// </para>
	/// </summary>
	public const string Reveal = "reveal";

	/// <summary>The event names this profile ships.</summary>
	public static readonly IReadOnlyList<string> WellKnown =
		[Change, Adjust, Press, LongPress, PressStart, PressEnd, Reveal];
}
