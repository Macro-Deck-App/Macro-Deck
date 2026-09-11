namespace MacroDeck.Ui.Components;

/// <summary>
/// The node property keys the widget profile ships. Bare camelCase, and deliberately not prefixed the way
/// <see cref="UiComponents" /> is: a property is only ever read in the context of the node type that
/// declared it, so <c>text</c> here and <c>text</c> in the configuration profile never meet.
/// </summary>
public static class UiComponentProperties
{
	/// <summary>The event names a node accepts - see <see cref="UiComponentEvents" />. Absent means the node
	/// offers no interaction, which is how this profile spells a control the reader must not let the user
	/// work.</summary>
	public const string Events = "events";

	/// <summary>A fixed extent on the parent stack's main axis, as a <see cref="UiLength" />.</summary>
	public const string MainSize = "mainSize";

	/// <summary>Whether this child shares the parent stack's remaining main axis equally with its filling
	/// siblings. Mutually exclusive with <see cref="MainSize" /> in practice, though not enforced.</summary>
	public const string Fill = "fill";

	/// <summary>A stack's layout axis, a list's scroll axis, or the axis a slider's level travels along - see
	/// <see cref="UiComponentDirections" />. The defaults when absent differ by type, which each type
	/// states.</summary>
	public const string Direction = "direction";

	/// <summary>How a stack distributes free space along its main axis - see
	/// <see cref="UiComponentJustify" />.</summary>
	public const string Justify = "justify";

	/// <summary>How a stack aligns children on its cross axis, or how a text aligns within its own box -
	/// see <see cref="UiComponentAlignments" />.</summary>
	public const string Align = "align";

	/// <summary>The gap between a stack's children, as a <see cref="UiLength" />.</summary>
	public const string Gap = "gap";

	/// <summary>A stack's inner padding on every edge, as a <see cref="UiLength" />.</summary>
	public const string Padding = "padding";

	/// <summary>A container's own background, as <c>#rrggbb</c>. A literal rather than a role, for the
	/// reason <see cref="Color" /> gives. Absent means a <see cref="UiComponents.Stack" /> paints no
	/// background and a <see cref="UiComponents.Button" /> paints the reader's own accent colour - a
	/// button always has a face, and spelling that face's default as a role would freeze it against the
	/// reader's theme, which is the split <see cref="LevelColor" /> already makes.</summary>
	public const string Background = "background";

	/// <summary>A text's content. Either a literal string or a localization reference the reader resolves in
	/// its own active language.</summary>
	public const string Text = "text";

	/// <summary>A text's font size, or an image's square edge, as a <see cref="UiLength" />. Never a
	/// main-axis extent - that is <see cref="MainSize" />.</summary>
	public const string Size = "size";

	/// <summary>The floor a text may shrink to in order to fit its box, as a
	/// <see cref="UiLength" />. Absent means the text never shrinks and ellipsizes instead.</summary>
	public const string MinSize = "minSize";

	/// <summary>A text's font weight - see <see cref="UiComponentTextWeights" />.</summary>
	public const string Weight = "weight";

	/// <summary>A text's semantic colour - see <see cref="UiComponentTextRoles" />. A role, so the reader
	/// resolves it against its own live theme. Overridden by <see cref="Color" /> when that is
	/// present.</summary>
	public const string Role = "role";

	/// <summary>A text's literal colour, as <c>#rrggbb</c>, overriding its <see cref="Role" />. For a colour
	/// the <i>user</i> chose rather than one the theme owns: a theme change must not repaint it, which is the
	/// same split <see cref="StartColor" /> makes. Absent means the role decides.</summary>
	public const string Color = "color";

	/// <summary>How many lines a text may occupy before it ellipsizes. Absent means one.</summary>
	public const string MaxLines = "maxLines";

	/// <summary>Whether a text may break across lines at all. Absent means it stays on one line, which is
	/// what a reader that does not implement the key does - the harmless failure, where treating an absent
	/// key as unbounded would let a run swallow its own widget.</summary>
	public const string Wrap = "wrap";

	/// <summary>The identifier of the typeface a text is drawn in, as the host's font catalogue names it.
	/// An identifier rather than a resource handle because a face is host-owned rather than producer-owned
	/// and is routinely far larger than one resource may be. Absent - and equally, one the reader cannot
	/// resolve - means the reader's own default face.</summary>
	public const string FontFace = "fontFace";

	/// <summary>The resource handle an element draws: an image's bytes, or a button's artwork.</summary>
	public const string Source = "source";

	/// <summary>How a change of <see cref="Source" /> is drawn - see <see cref="UiComponentImageTransitions" />.
	/// Absent means the new artwork simply replaces the old one, which is also what a reader that does not
	/// implement the key does. That is the harmless failure, and the reason this is a property rather than a
	/// type: ignoring it still draws the right picture, only without the movement.</summary>
	public const string Transition = "transition";

	/// <summary>How a button's artwork fills its box - see <see cref="UiComponentImageFits" />. Absent means
	/// <see cref="UiComponentImageFits.Contain" />.</summary>
	public const string Fit = "fit";

	/// <summary>A multiplier scaling a button's artwork about its own centre. Absent means <c>1</c>.</summary>
	public const string Zoom = "zoom";

	/// <summary>A button's artwork shifted across, as a fraction of the element's own width, applied after
	/// <see cref="Zoom" /> so an offset means the same distance at any zoom. Absent means <c>0</c>.</summary>
	public const string OffsetX = "offsetX";

	/// <summary>The same down the element's own height. Absent means <c>0</c>.</summary>
	public const string OffsetY = "offsetY";

	/// <summary>How opaque artwork is drawn, in <c>0..1</c> - a button's backdrop or an image's own
	/// bytes. Absent means fully opaque.</summary>
	public const string Opacity = "opacity";

	/// <summary>How brightly artwork is drawn, as a multiplier of its own luminance. Absent means
	/// <c>1</c>. See <see cref="UiImage.Brightness" /> for the normative result.</summary>
	public const string Brightness = "brightness";

	/// <summary>How colourful artwork is drawn, as a multiplier of its own saturation. Absent means
	/// <c>1</c>, and <c>0</c> draws it in greys. See <see cref="UiImage.Saturation" /> for the
	/// normative result.</summary>
	public const string Saturation = "saturation";

	/// <summary>Where a range bar's filled span begins, as a fraction of the track.</summary>
	public const string Start = "start";

	/// <summary>Where a range bar's filled span ends, as a fraction of the track.</summary>
	public const string End = "end";

	/// <summary>The colour at a range bar's <see cref="Start" />, as <c>#rrggbb</c>.</summary>
	public const string StartColor = "startColor";

	/// <summary>The colour at a range bar's <see cref="End" />, as <c>#rrggbb</c>.</summary>
	public const string EndColor = "endColor";

	/// <summary>Where a range bar's point marker sits, as a fraction of the track. Absent means no
	/// marker.</summary>
	public const string Marker = "marker";

	/// <summary>A range bar's or a slider's track thickness, as a <see cref="UiLength" />.</summary>
	public const string Thickness = "thickness";

	/// <summary>A reference the reader resolves against its own state rather than reading a value the
	/// producer wrote: a <see cref="Model.Widgets.UiTimeReference" /> on the clock types, a
	/// <see cref="Model.Widgets.UiProgressReference" /> on the progress types. Which of the two a
	/// given node carries is fixed by that node's type, never mixed on one type - a reader parses the shape
	/// its type declares and rejects the other, which is why the second reference could be added at
	/// all.</summary>
	public const string Value = "value";

	/// <summary>Which derivation of a <see cref="Value" /> reference a text run shows - see
	/// <see cref="UiTimeFormats" /> and <see cref="UiProgressFormats" />.</summary>
	public const string Format = "format";

	/// <summary>Whether seconds are shown: on a dynamic text they are part of the run, on a clock dial
	/// they are the second hand. Absent means <c>false</c>.</summary>
	public const string Seconds = "seconds";

	/// <summary>A slider's filled fraction of its track, in <c>0..1</c>. A fraction rather than a value in
	/// the producer's own units - see <see cref="UiSlider.Level" />.</summary>
	public const string Level = "level";

	/// <summary>The granularity a slider's <see cref="Level" /> snaps to, in the same fraction
	/// space.</summary>
	public const string Step = "step";

	/// <summary>A slider's filled span colour, as <c>#rrggbb</c>. Absent means the reader's own accent
	/// colour.</summary>
	public const string LevelColor = "levelColor";

	/// <summary>How a button's ring is drawn - see <see cref="UiComponentBorderStyles" />. Absent means no ring
	/// at all; there is deliberately no value spelling "off", because absence already spells it and a
	/// second spelling of one state is one that can contradict the first.</summary>
	public const string BorderStyle = "borderStyle";

	/// <summary>A button ring's colour, as <c>#rrggbb</c>. Absent means the style supplies its own - which
	/// is what the styles that cycle through colours of their own do.</summary>
	public const string BorderColor = "borderColor";

	/// <summary>How round a button's own corners are - see <see cref="UiComponentButtonCorners" />. Absent
	/// means the profile's own rule, which is also what a reader that does not implement the key does: it
	/// still draws the button, only with a corner of its own choosing.</summary>
	public const string Corner = "corner";

	/// <summary>A chart's series, as fractions of its plot band in <c>0..1</c>. Absent - and equally an
	/// empty series - draws nothing.</summary>
	public const string Points = "points";

	/// <summary>Where a chart's plot band begins, as a fraction of the element's own height. Absent means
	/// the whole element.</summary>
	public const string PlotTop = "plotTop";

	/// <summary>How many digit widths a text run reserves, so a number that changes length does not move
	/// what sits beside it. Absent means the run is exactly as wide as its content.</summary>
	public const string Digits = "digits";

	/// <summary>The value a pressable container completes a dialog with when it is pressed.
	/// Meaningful only on the dialog surface; ignored anywhere else.</summary>
	public const string Answer = "answer";

	/// <summary>What a <see cref="UiTextField" /> shows while it is empty. Localized text, unlike the
	/// field's own value: a prompt is written for the reader, a value is the user's.</summary>
	public const string Placeholder = "placeholder";

	/// <summary>The property keys this profile ships.</summary>
	public static readonly IReadOnlyList<string> WellKnown =
	[
		Events, MainSize, Fill, Direction, Justify, Align, Gap, Padding, Background, Text, Size, MinSize,
		Weight, Role, Color, MaxLines, Wrap, FontFace, Source, Transition, Fit, Zoom, OffsetX, OffsetY,
		Opacity, Brightness, Saturation, Start, End, StartColor, EndColor, Marker, Thickness, Value,
		Format, Seconds, Level, Step, LevelColor, BorderStyle, BorderColor, Corner, Points, PlotTop,
		Digits, Answer, Placeholder,
	];
}
