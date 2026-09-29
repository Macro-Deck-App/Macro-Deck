namespace MacroDeck.Ui.Components;

/// <summary>
/// The node type strings that are Macro Deck's rather than the framework's: two runs of text and two
/// tracks whose displayed value the <i>reader</i> derives from a Macro Deck-defined reference it
/// resolves against its own clock, and a video stream the reader asks Macro Deck for itself.
///
/// <para>
/// <b>What puts a component here.</b> Not who ships it, and not where it happens to be drawn - a button
/// on a deck tile is still <see cref="UiComponents.Button" />. A component belongs here when a reader
/// cannot draw it from the tree alone: it needs
/// <see cref="Model.References.UiTimeReference" /> (ADR 0064) or
/// <see cref="Model.References.UiProgressReference" /> (ADR 0064), and therefore has to know what those
/// value shapes mean and advance them itself, or it needs
/// <see cref="Model.References.UiVideoStreamReference" /> and a session Macro Deck brokers for it.
/// </para>
///
/// <para>
/// <b>No IsKnown, deliberately</b>, for the reason <see cref="UiComponents" /> gives.
/// </para>
/// </summary>
public static class UiMacroDeckComponents
{
	/// <summary>A run of text the <i>reader</i> derives from a time reference it resolves itself, rather
	/// than text the producer wrote.</summary>
	public const string DynamicText = "macrodeck.dynamic-text";

	/// <summary>An analogue clock face, drawn from a time reference the reader resolves itself.</summary>
	public const string ClockDial = "macrodeck.clock-dial";

	/// <summary>A track whose filled span the <i>reader</i> derives from a position that is still moving,
	/// rather than one the producer wrote.</summary>
	public const string ProgressBar = "macrodeck.progress-bar";

	/// <summary>A run of text the reader derives from the same moving position.</summary>
	public const string ProgressText = "macrodeck.progress-text";

	/// <summary>A live video stream a provider offers, played from a session the <i>reader</i> opens on
	/// it.</summary>
	public const string VideoStream = "macrodeck.video-stream";

	/// <summary>The types Macro Deck ships on top of the core framework. Not exhaustive of what a renderer
	/// may meet - see the type's remarks.</summary>
	public static readonly IReadOnlyList<string> WellKnown =
	[
		DynamicText, ClockDial, ProgressBar, ProgressText, VideoStream,
	];
}
