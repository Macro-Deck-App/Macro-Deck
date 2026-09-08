using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.References;

namespace MacroDeck.Ui.Components;

/// <summary>
/// A widget container that participates in its parent stack's layout. The
/// <see cref="MainSize" />/<see cref="Fill" /> pair is repeated on <see cref="UiComponentLeaf" /> rather than
/// hoisted into one base, because a container derives from <see cref="UiContainer" /> and a leaf from
/// <see cref="UiLeaf" />, and those two carry different child semantics.
/// </summary>
public abstract record UiComponentContainer : UiContainer
{
	/// <summary>A fixed extent on the parent stack's main axis. Absent means the element sizes to its
	/// content.</summary>
	public UiSize MainSize { get; init; }

	/// <summary>Whether this element shares the parent stack's remaining main axis equally with its filling
	/// siblings.</summary>
	public UiValue<bool> Fill { get; init; }

	/// <summary>
	/// The answer this container gives, when it is pressed inside a dialog opened by an action. Absent -
	/// and on any surface that is not a dialog - means pressing it is an ordinary event and nothing else.
	///
	/// <para>
	/// On any container that claims the press, not just <see cref="UiButton" />: the affordance
	/// belongs to the declared events rather than to the node type, and a list row wants a
	/// <see cref="UiStack" />'s absent background rather than a button's accent.
	/// </para>
	///
	/// <para>
	/// <b>Why the reader settles the dialog rather than the producer.</b> The dialog belongs to the client
	/// that opened it: it is the side that knows the press happened, the side that has to take the dialog
	/// down, and the side whose principal the modal is bound to. A producer settling it would have to be
	/// told the answer, told to close, and told again when the user closed it first - three round trips to
	/// express one press.
	/// </para>
	///
	/// <para>
	/// <b>An identifier, not a payload.</b> Whatever a producer needs beyond it stays on the producer's own
	/// side, looked up by this: an answer travelling through a client is a value the client could have
	/// changed, and a producer that reads back only an id it issued cannot be told anything it did not
	/// already know.
	/// </para>
	/// </summary>
	public UiValue<string> Answer { get; init; }

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiComponentProperties.MainSize, MainSize.Value);
		properties.Set(UiComponentProperties.Fill, Fill);
		properties.Set(UiComponentProperties.Answer, Answer);
	}
}

/// <summary>A childless widget element that participates in its parent stack's layout.</summary>
public abstract record UiComponentLeaf : UiLeaf
{
	/// <summary>A fixed extent on the parent stack's main axis. Absent means the element sizes to its
	/// content.</summary>
	public UiSize MainSize { get; init; }

	/// <summary>Whether this element shares the parent stack's remaining main axis equally with its filling
	/// siblings.</summary>
	public UiValue<bool> Fill { get; init; }

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiComponentProperties.MainSize, MainSize.Value);
		properties.Set(UiComponentProperties.Fill, Fill);
	}
}

/// <summary>
/// A one-directional layout container, and the only structural element the widget profile ships. Nesting
/// stacks is how a view expresses everything a per-child margin property would otherwise be needed for.
/// </summary>
// Renaming this would make the type disagree with the wire name a renderer switches on.
#pragma warning disable CA1711
public sealed record UiStack : UiComponentContainer
#pragma warning restore CA1711
{
	/// <summary>The layout axis - see <see cref="UiComponentDirections" />. Absent means
	/// <see cref="UiComponentDirections.Vertical" />.</summary>
	public UiValue<string> Direction { get; init; }

	/// <summary>How free space is distributed along the main axis - see <see cref="UiComponentJustify" />.
	/// Absent means <see cref="UiComponentJustify.Start" />.</summary>
	public UiValue<string> Justify { get; init; }

	/// <summary>How children are aligned on the cross axis - see <see cref="UiComponentAlignments" />. Absent
	/// means <see cref="UiComponentAlignments.Stretch" />.</summary>
	public UiValue<string> Align { get; init; }

	/// <summary>The gap between children. Absent means none.</summary>
	public UiSize Gap { get; init; }

	/// <summary>Inner padding on every edge. Absent means none.</summary>
	public UiSize Padding { get; init; }

	/// <summary>The stack's own background, as <c>#rrggbb</c>. A literal colour rather than a role, for the
	/// reason <see cref="UiTextRun.Color" /> gives. Absent means the stack paints nothing behind its
	/// children. A reader rejects any other spelling rather than passing it through to its styling
	/// layer.</summary>
	public UiValue<string> Background { get; init; }

	/// <inheritdoc />
	public override string Type => UiComponents.Stack;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiComponentProperties.Direction, Direction);
		properties.Set(UiComponentProperties.Justify, Justify);
		properties.Set(UiComponentProperties.Align, Align);
		properties.Set(UiComponentProperties.Gap, Gap.Value);
		properties.Set(UiComponentProperties.Padding, Padding.Value);
		properties.Set(UiComponentProperties.Background, Background);
	}
}

/// <summary>
/// A single run of text. Its box is its font size - a reader lays it out with a line height of exactly one,
/// so a stack's gaps are the only vertical spacing in a widget view and two readers agree on where the run
/// sits.
/// </summary>
public sealed record UiTextRun : UiComponentLeaf
{
	/// <summary>The content. A localization reference resolves in each reader's own active language, which
	/// is what lets one shared session serve clients in different languages.</summary>
	public UiText Text { get; init; }

	/// <summary>The font size. Absent leaves the size to the reader.</summary>
	public UiSize Size { get; init; }

	/// <summary>The floor <see cref="Size" /> may shrink to so the run fits its box. Absent means the run
	/// never shrinks and ellipsizes instead.</summary>
	public UiSize MinSize { get; init; }

	/// <summary>The font weight - see <see cref="UiComponentTextWeights" />. Absent means
	/// <see cref="UiComponentTextWeights.Regular" />.</summary>
	public UiValue<string> Weight { get; init; }

	/// <summary>The semantic colour - see <see cref="UiComponentTextRoles" />. Absent means
	/// <see cref="UiComponentTextRoles.Primary" />. Ignored when <see cref="Color" /> is present.</summary>
	public UiValue<string> Role { get; init; }

	/// <summary>A literal colour, as <c>#rrggbb</c>, overriding <see cref="Role" />. For a colour the
	/// <i>user</i> chose rather than one the theme owns - what they picked must not change with the reader's
	/// theme, which is the same split <see cref="UiRangeBar.StartColor" /> makes. Absent means the role
	/// decides. A reader rejects any other spelling rather than passing it through to its styling
	/// layer.</summary>
	public UiValue<string> Color { get; init; }

	/// <summary>Alignment within the run's own box - see <see cref="UiComponentAlignments" />. Absent means
	/// <see cref="UiComponentAlignments.Start" />.</summary>
	public UiValue<string> Align { get; init; }

	/// <summary>How many lines the run may occupy before it ellipsizes. Absent means one.</summary>
	public UiValue<int> MaxLines { get; init; }

	/// <summary>Whether the run may break across lines at all. Absent means it stays on one line - see
	/// <see cref="UiComponentProperties.Wrap" /> for why that is the absent meaning rather than the
	/// permissive one.</summary>
	public UiValue<bool> Wrap { get; init; }

	/// <summary>The typeface, as the host's font catalogue identifies it. Absent - and equally, one the
	/// reader cannot resolve - means the reader's own default face. A reader holds the run's text back
	/// until the face is usable and then reveals it, rather than drawing it in a fallback and swapping,
	/// and reveals it in its own default face if the face never arrives: a run that never appears is worse
	/// than one in the wrong font.</summary>
	public UiValue<string> FontFace { get; init; }

	/// <summary>
	/// How many digit widths the run reserves. Absent means the run is exactly as wide as its content.
	///
	/// <para>
	/// <b>What it is for.</b> A live readout changes length as its value changes, and a run that is
	/// exactly as wide as "9" and then exactly as wide as "10" drags whatever sits beside it sideways once
	/// a second. Reserving the width the widest expected value needs settles the layout instead. A
	/// reserved run is drawn with every digit on the same advance width, so the run does not shift as it
	/// counts even before the reservation binds, and the content is centred in the reservation when it is
	/// narrower - the reservation is space around the value, never a change to the value.
	/// </para>
	///
	/// <para>
	/// A count of digits rather than a length, because the width of a digit is the reader's to know: it
	/// follows from the face and the size the reader ends up drawing with, which the producer cannot
	/// compute for it. Fractional, because a run is not always whole digits: a decimal separator is
	/// materially narrower than a digit, and a producer reserving room for one counts it as the fraction
	/// of a digit it takes rather than rounding a visible gap into the layout.
	/// </para>
	/// </summary>
	public UiValue<double> Digits { get; init; }

	/// <inheritdoc />
	public override string Type => UiComponents.Text;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiComponentProperties.Text, Text.Value);
		properties.Set(UiComponentProperties.Size, Size.Value);
		properties.Set(UiComponentProperties.MinSize, MinSize.Value);
		properties.Set(UiComponentProperties.Weight, Weight);
		properties.Set(UiComponentProperties.Role, Role);
		properties.Set(UiComponentProperties.Color, Color);
		properties.Set(UiComponentProperties.Align, Align);
		properties.Set(UiComponentProperties.MaxLines, MaxLines);
		properties.Set(UiComponentProperties.Wrap, Wrap);
		properties.Set(UiComponentProperties.FontFace, FontFace);
		properties.Set(UiComponentProperties.Digits, Digits);
	}
}

/// <summary>
/// An image resolved from a resource handle. The handle names bytes the host serves; a tree never carries
/// them, so the same view costs the same whether one client renders it or eight.
/// </summary>
public sealed record UiImage : UiComponentLeaf
{
	/// <summary>The resource to draw. A reader that cannot resolve it draws nothing rather than failing the
	/// tree.</summary>
	public UiValue<UiResource> Source { get; init; }

	/// <summary>The edge of the square box the image is fitted into, preserving aspect ratio and never
	/// cropping.</summary>
	public UiSize Size { get; init; }

	/// <summary>How a change of <see cref="Source" /> is drawn - see
	/// <see cref="UiComponentImageTransitions" />. Absent means the new image simply replaces the old
	/// one.</summary>
	public UiValue<string> Transition { get; init; }

	/// <summary>How opaque the image is drawn, in <c>0..1</c>. Absent means fully opaque. The same key
	/// <see cref="UiButton.Opacity" /> carries, and for the same job - dimming artwork against what
	/// is behind it - so a producer that dims a full-bleed cover one way can dim a square one the
	/// same way.</summary>
	public UiValue<double> Opacity { get; init; }

	/// <summary>
	/// How brightly the image is drawn, as a multiplier of its own luminance in <c>0..2</c>. Absent means
	/// <c>1</c>.
	///
	/// <para>
	/// <b>Distinct from <see cref="Opacity" />, and not a substitute for it.</b> Opacity lets whatever is
	/// behind the artwork show through, so what a half-transparent cover ends up looking like depends on
	/// the colour behind it; brightness changes the artwork itself and looks the same on any ground. A
	/// producer that wants "the same picture, darker" - a paused album cover - means this one.
	/// </para>
	///
	/// <para>
	/// <b>The result is normative</b>, for the reason <see cref="UiRangeBar" />'s geometry is: each
	/// channel is multiplied by the value, before <see cref="Saturation" /> and before the artwork is
	/// composited at its <see cref="Opacity" />. A reader clamps each resulting channel to its own range
	/// rather than wrapping it.
	/// </para>
	/// </summary>
	public UiValue<double> Brightness { get; init; }

	/// <summary>
	/// How colourful the image is drawn, as a multiplier of its own saturation in <c>0..2</c>. Absent
	/// means <c>1</c>; <c>0</c> draws it in greys.
	///
	/// <para>
	/// <b>The result is normative.</b> Each pixel is mixed with its own luminance -
	/// <c>out = luma + saturation * (channel - luma)</c>, with <c>luma = 0.213 R + 0.715 G + 0.072 B</c> -
	/// applied after <see cref="Brightness" />. The coefficients are stated because two readers using
	/// different ones desaturate the same cover to visibly different greys.
	/// </para>
	/// </summary>
	public UiValue<double> Saturation { get; init; }

	/// <inheritdoc />
	public override string Type => UiComponents.Image;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiComponentProperties.Source, Source);
		properties.Set(UiComponentProperties.Transition, Transition);
		properties.Set(UiComponentProperties.Opacity, Opacity);
		properties.Set(UiComponentProperties.Brightness, Brightness);
		properties.Set(UiComponentProperties.Saturation, Saturation);
		properties.Set(UiComponentProperties.Size, Size.Value);
	}
}

/// <summary>
/// A horizontal track carrying one gradient-filled span, with an optional point marker.
///
/// <para>
/// <b>The geometry below is normative</b>, not a suggestion: a range bar is the one primitive in this
/// profile whose appearance is not fully determined by its properties, and two readers that disagree on it
/// draw visibly different widgets. A reader implements it exactly.
/// </para>
///
/// <list type="bullet">
/// <item>The track spans the element's full main-axis extent, is <see cref="Thickness" /> tall, is centred
/// on the cross axis, and has fully rounded ends.</item>
/// <item>The unfilled track is painted in the reader's tertiary surface colour.</item>
/// <item>The filled span runs from <see cref="Start" /> to <see cref="End" /> as fractions of the track,
/// with a linear gradient from <see cref="StartColor" /> to <see cref="EndColor" /> along the main axis.
/// It carries the track's own fully rounded ends, so a span that stops short of either end still reads as
/// a pill rather than as a cut-off block.</item>
/// <item>The marker is a filled disc of radius <c>0.75 * Thickness</c> painted in the reader's primary text
/// colour - not in <see cref="EndColor" />, which would leave it invisible wherever it lands on its own
/// gradient - ringed by a stroke of width <c>0.28 * Thickness</c> in the widget's own background colour so
/// the disc reads as sitting above the track.</item>
/// <item>The marker's centre is inset from both ends by <c>radius + strokeWidth / 2</c> so the ring never
/// clips; when the track is narrower than twice that inset, the marker is centred instead.</item>
/// </list>
/// </summary>
public sealed record UiRangeBar : UiComponentLeaf
{
	/// <summary>Where the filled span begins, as a fraction of the track in <c>0..1</c>.</summary>
	public UiValue<double> Start { get; init; }

	/// <summary>Where the filled span ends, as a fraction of the track in <c>0..1</c>.</summary>
	public UiValue<double> End { get; init; }

	/// <summary>The colour at <see cref="Start" />, as <c>#rrggbb</c>. A literal colour rather than a role
	/// because it encodes data, not theme: what a value looks like must not change with the reader's
	/// theme. A reader rejects any other spelling rather than passing it through to its styling
	/// layer.</summary>
	public UiValue<string> StartColor { get; init; }

	/// <summary>The colour at <see cref="End" />, as <c>#rrggbb</c>. See <see cref="StartColor" />.</summary>
	public UiValue<string> EndColor { get; init; }

	/// <summary>Where the point marker sits, as a fraction of the track in <c>0..1</c>. Absent means no
	/// marker.</summary>
	public UiValue<double> Marker { get; init; }

	/// <summary>The track's thickness on the cross axis.</summary>
	public UiSize Thickness { get; init; }

	/// <inheritdoc />
	public override string Type => UiComponents.RangeBar;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiComponentProperties.Start, Start);
		properties.Set(UiComponentProperties.End, End);
		properties.Set(UiComponentProperties.StartColor, StartColor);
		properties.Set(UiComponentProperties.EndColor, EndColor);
		properties.Set(UiComponentProperties.Marker, Marker);
		properties.Set(UiComponentProperties.Thickness, Thickness.Value);
	}
}

/// <summary>
/// A run of text the reader derives from a reference it resolves itself, rather than text the producer
/// wrote. Its box behaves exactly like a <see cref="UiTextRun" />'s - the font size, with a line
/// height of one.
///
/// <para>
/// A separate type rather than another shape on <see cref="UiTextRun.Text" />, because degradation
/// has to be visible: a reader that does not know a <i>type</i> is caught while the tree is negotiated
/// and draws the node's <see cref="UiElement.Fallback" />, whereas a reader that does not know a
/// <i>property</i> ignores it and draws something wrong with no sign that it did.
/// </para>
/// </summary>
public sealed record UiDynamicText : UiComponentLeaf
{
	/// <summary>The reference the reader resolves. Absent draws nothing.</summary>
	public UiValue<UiTimeReference> Value { get; init; }

	/// <summary>Which derivation of <see cref="Value" /> to show - see
	/// <see cref="UiTimeFormats" />. A reader draws nothing for a format it does not know.</summary>
	public UiValue<string> Format { get; init; }

	/// <summary>Whether the run includes seconds. Absent means it does not. See
	/// <see cref="UiTimeFormats.Time" /> for how they are drawn.</summary>
	public UiValue<bool> Seconds { get; init; }

	/// <summary>The font size. Absent leaves the size to the reader.</summary>
	public UiSize Size { get; init; }

	/// <summary>The floor <see cref="Size" /> may shrink to so the run fits its box. Absent means the run
	/// never shrinks and ellipsizes instead.</summary>
	public UiSize MinSize { get; init; }

	/// <summary>The font weight - see <see cref="UiComponentTextWeights" />. Absent means
	/// <see cref="UiComponentTextWeights.Regular" />.</summary>
	public UiValue<string> Weight { get; init; }

	/// <summary>The semantic colour - see <see cref="UiComponentTextRoles" />. Absent means
	/// <see cref="UiComponentTextRoles.Primary" />. Ignored when <see cref="Color" /> is present.</summary>
	public UiValue<string> Role { get; init; }

	/// <summary>A literal run colour, as <c>#rrggbb</c>, for the reason <see cref="UiTextRun.Color" />
	/// gives. Absent means the <see cref="Role" /> colour. The seconds keep their own muted treatment
	/// either way: they are the one part of a time run whose colour is normative rather than
	/// chosen.</summary>
	public UiValue<string> Color { get; init; }

	/// <summary>Alignment within the run's own box - see <see cref="UiComponentAlignments" />. Absent means
	/// <see cref="UiComponentAlignments.Start" />.</summary>
	public UiValue<string> Align { get; init; }

	/// <inheritdoc />
	public override string Type => UiMacroDeckComponents.DynamicText;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiComponentProperties.Value, Value);
		properties.Set(UiComponentProperties.Format, Format);
		properties.Set(UiComponentProperties.Seconds, Seconds);
		properties.Set(UiComponentProperties.Size, Size.Value);
		properties.Set(UiComponentProperties.MinSize, MinSize.Value);
		properties.Set(UiComponentProperties.Weight, Weight);
		properties.Set(UiComponentProperties.Role, Role);
		properties.Set(UiComponentProperties.Color, Color);
		properties.Set(UiComponentProperties.Align, Align);
	}
}

/// <summary>
/// An analogue clock face, drawn by the reader from a time reference it resolves itself.
///
/// <para>
/// <b>The geometry below is normative</b>, for the reason <see cref="UiRangeBar" /> states: none
/// of it follows from the properties, and two readers that disagree on it draw visibly different
/// clocks. A reader implements it exactly. Lengths are fractions of <c>d</c>, the largest square that
/// fits the element's box, centred in it - a dial in a box that is not square is centred rather than
/// stretched.
/// </para>
///
/// <list type="bullet">
/// <item>The face is a filled disc of radius <c>0.48 d</c> at the centre, in the reader's tertiary
/// surface colour.</item>
/// <item>Twelve round-capped tick marks sit every 30°, the first at twelve o'clock, running clockwise.
/// Each one's outer end is at radius <c>0.44 d</c>. Every third tick - twelve, three, six and nine -
/// runs inward to <c>0.37 d</c> with a stroke of <c>0.025 d</c> in the reader's secondary text colour;
/// the other eight run inward to <c>0.405 d</c> with a stroke of <c>0.015 d</c> in its muted text
/// colour.</item>
/// <item>Each hand is a round-capped line through the centre, extending a tail behind it so it reads as
/// pivoting rather than sprouting. The hour hand runs <c>0.05 d</c> behind the centre and <c>0.21 d</c>
/// in front of it with a stroke of <c>0.04 d</c>, the minute hand <c>0.05 d</c> and <c>0.33 d</c> with a
/// stroke of <c>0.025 d</c>, both in the reader's primary text colour.</item>
/// <item>The second hand is drawn only when <see cref="Seconds" /> is set: <c>0.08 d</c> behind and
/// <c>0.37 d</c> in front, stroke <c>0.0125 d</c>, in the reader's accent colour.</item>
/// <item>A filled disc of radius <c>0.026 d</c> in the accent colour is drawn over the hands at the
/// centre.</item>
/// <item>When <see cref="Color" /> is present, every mark the dial would otherwise draw in one of the
/// reader's text colours - both tick weights, the hour hand and the minute hand - is drawn in it
/// instead. The face, the second hand and the hub are untouched, so the moving hand still reads against
/// a tinted face; the major and minor ticks stay told apart by the stroke and length they already
/// differ by, rather than by an opacity this profile would have to invent.</item>
/// <item>Angles are measured clockwise from twelve o'clock, from the referenced instant's hour
/// <c>h</c>, minute <c>m</c> and second <c>s</c> <b>in the reference's own zone</b>: the hour hand at
/// <c>(h mod 12) * 30 + m * 0.5</c> degrees, the minute hand at <c>m * 6 + s * 0.1</c>, the second hand
/// at <c>s * 6</c>.</item>
/// <item>A reader re-evaluates at least once a second and computes the angles from the whole second. A
/// sub-second sweep is deliberately excluded: it is not derivable from the reference, so two readers
/// that each chose their own interpolation would disagree.</item>
/// </list>
/// </summary>
public sealed record UiClockDial : UiComponentLeaf
{
	/// <summary>The reference the hands are drawn from. Absent draws nothing.</summary>
	public UiValue<UiTimeReference> Value { get; init; }

	/// <summary>Whether the second hand is drawn. Absent means it is not.</summary>
	public UiValue<bool> Seconds { get; init; }

	/// <summary>A literal colour for the dial's text-coloured marks, as <c>#rrggbb</c> - see the tint rule
	/// above. Absent leaves every mark on the reader's own theme colours. A reader that predates this
	/// property ignores it and draws a themed dial, which is why it carries no version requirement: the
	/// wrong colour beats a face that vanished into a fallback.</summary>
	public UiValue<string> Color { get; init; }

	/// <inheritdoc />
	public override string Type => UiMacroDeckComponents.ClockDial;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiComponentProperties.Value, Value);
		properties.Set(UiComponentProperties.Seconds, Seconds);
		properties.Set(UiComponentProperties.Color, Color);
	}
}

/// <summary>
/// A track whose filled span the <i>reader</i> derives from a
/// <see cref="UiProgressReference" /> it carries forward on its own clock, rather than from a
/// fraction the producer wrote.
///
/// <para>
/// A separate type rather than another shape on <see cref="UiRangeBar.End" />, for the reason
/// <see cref="UiDynamicText" /> gives: a reader that does not know a <i>type</i> is caught while the
/// tree is negotiated and draws the node's <see cref="UiElement.Fallback" /> - here, a range bar frozen at
/// the anchor position, which is honest - whereas a reader that does not know a <i>property</i> would paint
/// a bar that silently never moves.
/// </para>
///
/// <para>
/// <b>Its geometry is <see cref="UiRangeBar" />'s, exactly</b>, with the span always beginning at the
/// track's start and no marker: the two draw the same picture, and only where the end of the span comes
/// from differs. The end is <c>position(t) / durationMs</c> clamped to <c>0..1</c>, and it is <c>0</c> when
/// the reference carries no <see cref="UiProgressReference.DurationMs" /> - a fraction of an unknown
/// whole is not a number a reader may invent.
/// </para>
/// </summary>
public sealed record UiProgressBar : UiComponentLeaf
{
	/// <summary>The reference the reader resolves. Absent draws an empty track.</summary>
	public UiValue<UiProgressReference> Value { get; init; }

	/// <summary>The colour at the start of the filled span, as <c>#rrggbb</c>. A literal colour rather than
	/// a role, for the reason <see cref="UiRangeBar.StartColor" /> gives.</summary>
	public UiValue<string> StartColor { get; init; }

	/// <summary>The colour at the head of the filled span, as <c>#rrggbb</c>. See
	/// <see cref="StartColor" />.</summary>
	public UiValue<string> EndColor { get; init; }

	/// <summary>The track's thickness on the cross axis.</summary>
	public UiSize Thickness { get; init; }

	/// <inheritdoc />
	public override string Type => UiMacroDeckComponents.ProgressBar;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiComponentProperties.Value, Value);
		properties.Set(UiComponentProperties.StartColor, StartColor);
		properties.Set(UiComponentProperties.EndColor, EndColor);
		properties.Set(UiComponentProperties.Thickness, Thickness.Value);
	}
}

/// <summary>
/// A run of text the reader derives from a <see cref="UiProgressReference" />. Its box behaves
/// exactly like a <see cref="UiTextRun" />'s - the font size, with a line height of one.
///
/// <para>
/// A separate type from <see cref="UiDynamicText" /> rather than a second reference shape on that
/// type's <c>value</c>: an older reader does not merely fail to understand <c>{"$progress":…}</c> there, it
/// <i>rejects</i> it, because that property's reader is written against the time reference's shape alone. A
/// new type degrades through <see cref="UiElement.Fallback" /> instead - here, a plain
/// <see cref="UiTextRun" /> holding the duration at the anchor.
/// </para>
/// </summary>
public sealed record UiProgressText : UiComponentLeaf
{
	/// <summary>The reference the reader resolves. Absent draws nothing.</summary>
	public UiValue<UiProgressReference> Value { get; init; }

	/// <summary>Which derivation of <see cref="Value" /> to show - see
	/// <see cref="UiProgressFormats" />. A reader draws nothing for a format it does not know.</summary>
	public UiValue<string> Format { get; init; }

	/// <summary>The font size. Absent leaves the size to the reader.</summary>
	public UiSize Size { get; init; }

	/// <summary>The floor <see cref="Size" /> may shrink to so the run fits its box. Absent means the run
	/// never shrinks and ellipsizes instead.</summary>
	public UiSize MinSize { get; init; }

	/// <summary>The font weight - see <see cref="UiComponentTextWeights" />. Absent means
	/// <see cref="UiComponentTextWeights.Regular" />.</summary>
	public UiValue<string> Weight { get; init; }

	/// <summary>The semantic colour - see <see cref="UiComponentTextRoles" />. Absent means
	/// <see cref="UiComponentTextRoles.Primary" />.</summary>
	public UiValue<string> Role { get; init; }

	/// <summary>Alignment within the run's own box - see <see cref="UiComponentAlignments" />. Absent means
	/// <see cref="UiComponentAlignments.Start" />.</summary>
	public UiValue<string> Align { get; init; }

	/// <inheritdoc />
	public override string Type => UiMacroDeckComponents.ProgressText;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiComponentProperties.Value, Value);
		properties.Set(UiComponentProperties.Format, Format);
		properties.Set(UiComponentProperties.Size, Size.Value);
		properties.Set(UiComponentProperties.MinSize, MinSize.Value);
		properties.Set(UiComponentProperties.Weight, Weight);
		properties.Set(UiComponentProperties.Role, Role);
		properties.Set(UiComponentProperties.Align, Align);
	}
}

/// <summary>
/// A draggable level: a rounded track carrying a filled span, and the interactive counterpart of
/// <see cref="UiRangeBar" />.
///
/// <para>
/// <b>The level is a fraction, not a value in the producer's units.</b> A reader snaps and paints it
/// locally while the user drags, so the control follows the finger without a round trip; and it never
/// formats the number, which a <see cref="UiTextRun" /> beside it carries as a localization reference.
/// A track duration and a volume percentage mean nothing to a renderer, and giving it either would put
/// the producer's units - and their formatting - into every renderer.
/// </para>
///
/// <para>
/// <b>Interaction is offered only where it is declared.</b> A node whose <see cref="UiElement.Events" />
/// is empty carries no <c>events</c> property and is drawn without any affordance at all. There is
/// deliberately no disabled property: a second source of truth for the same question is one that can
/// contradict the first.
/// </para>
///
/// <para>
/// <b>The geometry below is normative</b>, for the reason <see cref="UiRangeBar" /> states: none of
/// it follows from the properties, and two readers that disagree on it draw visibly different controls. A
/// reader implements it exactly.
/// </para>
///
/// <list type="bullet">
/// <item>The element's <b>whole box</b> is the interactive surface, and the drawn track is smaller than
/// it. The gap between the two is the point, not an oversight: a pill a few units thick on a deck tile is
/// not a target a thumb can hit, so a reader that made only the track draggable would draw the right
/// picture and ship an unusable control.</item>
/// <item>The track spans the element's full extent along <see cref="Direction" />, is
/// <see cref="Thickness" /> across, is centred on the cross axis, and has fully rounded ends.</item>
/// <item>The unfilled track is painted in the reader's tertiary surface colour.</item>
/// <item>The filled span runs from the track's start to <see cref="Level" /> of its length, painted flat
/// in <see cref="LevelColor" />, or in the reader's <b>accent</b> colour when that is absent. It carries
/// the track's own fully rounded ends, so a span short of the end still reads as a pill rather than a
/// cut-off block. A <see cref="Level" /> of <c>0</c> paints nothing - not a rounded stub one radius
/// wide.</item>
/// <item>A <b>thumb</b> is drawn at <see cref="Level" />, and it is what makes the element read as
/// something to take hold of rather than a bar that happens to move. It is a filled disc of radius
/// <c>1.25 * Thickness</c> in the reader's primary text colour - not in <see cref="LevelColor" />,
/// which would leave it invisible wherever it lands on its own fill - ringed by a stroke of width
/// <c>0.28 * Thickness</c> in the widget's own background colour, so it reads as sitting above the
/// track. Its centre is inset from both ends by <c>radius + strokeWidth / 2</c> so the ring never
/// clips; when the track is shorter than twice that inset, the thumb is centred instead. It is drawn
/// whether or not the node declares any event: it says where the level is, and a reader that offers
/// no interaction still has to say that.
///
/// <para>
/// Deliberately larger than <see cref="UiRangeBar" />'s marker, which is the same shape at
/// <c>0.75 * Thickness</c>. That one only has to be seen; this one has to be hit, and a handle sized
/// like an indicator reads as a bar that happens to have a dot on it.
/// </para></item>
/// <item><see cref="Direction" /> is the axis the level travels along. <c>horizontal</c> runs from the
/// leading edge to the trailing edge in the reader's writing direction; <c>vertical</c> runs <b>bottom to
/// top</b> - up is more. Absent means <c>horizontal</c>, unlike on a <see cref="UiStack" />, where
/// absent means vertical.</item>
/// <item>A pointer anywhere in the box sets the level to its position projected onto that axis, clamped
/// to <c>0..1</c>; the cross-axis position is ignored, and a pointer that leaves the box mid-drag keeps
/// controlling the element until it is released.</item>
/// <item>When <see cref="Step" /> is present the level is snapped to <c>round(level / step) * step</c>,
/// clamped to <c>0..1</c>, before it is painted or sent. <b>A tie rounds up</b> - stated because the
/// obvious rounding primitive differs by platform, and a reader that rounded half to even would paint one
/// grid point while the producer acted on its neighbour.</item>
/// <item><b>A reader paints the level it computed immediately, without waiting for the producer.</b>
/// While an interaction is in progress it paints its own level and ignores producer updates to
/// <see cref="Level" />; when the interaction ends it adopts the next level the producer sends. This is
/// what the fraction buys: the paint is local, and the round trip only reconciles.</item>
/// <item>Every intermediate level is sent as <see cref="UiComponentEvents.Adjust" />, no more than ten times
/// a second; the level the interaction ended on is sent once as <see cref="UiComponentEvents.Change" />. A
/// reader sends only the names the node declares.</item>
/// </list>
/// </summary>
public sealed record UiSlider : UiComponentLeaf
{
	/// <summary>The filled fraction of the track, in <c>0..1</c>. Absent means <c>0</c>.</summary>
	public UiValue<double> Level { get; init; }

	/// <summary>The granularity <see cref="Level" /> snaps to, in the same fraction space - a producer
	/// whose value moves in whole steps divides one step by its own range. Absent means the level is
	/// continuous.</summary>
	public UiValue<double> Step { get; init; }

	/// <summary>The filled span's colour, as <c>#rrggbb</c>. A literal for the reason
	/// <see cref="UiRangeBar.StartColor" /> gives. <b>Absent means the reader's own accent
	/// colour</b> - spelled by omitting the key rather than by a role, because a colour vocabulary with
	/// one member is an absence with extra steps. A reader rejects any other spelling rather than passing
	/// it through to its styling layer.</summary>
	public UiValue<string> LevelColor { get; init; }

	/// <summary>The axis the level travels along - see <see cref="UiComponentDirections" />. Absent means
	/// <see cref="UiComponentDirections.Horizontal" />.</summary>
	public UiValue<string> Direction { get; init; }

	/// <summary>The drawn track's thickness on the cross axis. It does not size the element: the box is
	/// the drag surface and the track is paint inside it.</summary>
	public UiSize Thickness { get; init; }

	/// <inheritdoc />
	public override string Type => UiComponents.Slider;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiComponentProperties.Level, Level);
		properties.Set(UiComponentProperties.Step, Step);
		properties.Set(UiComponentProperties.LevelColor, LevelColor);
		properties.Set(UiComponentProperties.Direction, Direction);
		properties.Set(UiComponentProperties.Thickness, Thickness.Value);
	}
}

/// <summary>
/// A layout container the user presses. It lays its children out exactly as
/// <see cref="UiStack" /> does, and adds three things a stack has no business carrying: artwork of
/// its own behind the children, a ring around the edge, and a press.
///
/// <para>
/// <b>The artwork is the element's own, not a child.</b> It fills the box behind everything else, which is
/// not something a stack's layout can express - and making it a property is what keeps the tree's
/// <i>shape</i> fixed while the artwork changes, so a button that swaps its face is a
/// <c>set-properties</c> patch rather than a reconcile. It is the same generalisation
/// <see cref="Background" /> already is: a thing painted behind the children.
/// </para>
///
/// <para>
/// <b>Paint order</b>, which none of the properties imply and a reader owes: <see cref="Background" />,
/// then the artwork, then the children, then the press feedback, then the ring. The ring is above
/// everything so artwork covering the whole box cannot hide it, and the press feedback is below the ring
/// for the same reason.
/// </para>
///
/// <para>
/// <b>The corner</b> is the button's own height times <c>0.12</c>, so a button half the height of another
/// is half as round - unless it is the whole widget tree, which fills the tile and takes the tile's corner
/// instead, or unless it says otherwise through <see cref="Corner" />.
/// </para>
///
/// <para>
/// <b>The ring</b> is drawn along the inner edge at a fixed <c>2</c> device-independent units, following
/// the element's corner radius. That is the one length in this profile that is not a fraction of the
/// basis, deliberately: a ring that grew with the element would read wildly inconsistently across
/// differently sized widgets. Every looping <see cref="UiComponentBorderStyles" /> is phase-locked to a
/// clock shared with the host rather than to its own element, so one button shows the same phase on every
/// client displaying it, and it keeps animating whatever the viewer's reduced-motion preference says: a
/// border a deck owner configured to move is the same border on every client, so a reader must not freeze
/// it.
/// </para>
///
/// <para>
/// <b>Press feedback is the reader's, painted immediately.</b> On press the reader tints the whole element
/// white at <c>0.2</c> alpha, fading in over <c>20</c>ms and out over <c>140</c>ms, and holds it visible at
/// least <c>60</c>ms so a tap shorter than that still registers. A reader never waits for the producer to
/// answer before painting it - the round trip runs the flow, it does not confirm the touch. This is the
/// whole reason a producer needs no say in how a press looks.
/// </para>
///
/// <para>
/// <b>Interaction is offered only where it is declared</b>, exactly as <see cref="UiSlider" /> has
/// it, and there is deliberately no disabled property. A button that declares no events is drawn - it is a
/// container, and its face is worth as much unpressed - but it accepts nothing. The names it may declare
/// are <see cref="UiComponentEvents.Press" />, <see cref="UiComponentEvents.LongPress" />,
/// <see cref="UiComponentEvents.PressStart" /> and <see cref="UiComponentEvents.PressEnd" />, and a reader owes
/// this ordering:
/// </para>
///
/// <list type="bullet">
/// <item>One interaction at a time. A second pointer arriving during one is ignored until it ends.</item>
/// <item><see cref="UiComponentEvents.PressStart" /> precedes every other name in an interaction, and exactly
/// one <see cref="UiComponentEvents.PressEnd" /> follows it - including when the pointer left the element or
/// the gesture was cancelled.</item>
/// <item><see cref="UiComponentEvents.LongPress" /> is sent once, <c>600</c>ms after the press began, while it
/// is still held. The threshold is normative: two readers disagreeing on it would run different flows for
/// the same gesture.</item>
/// <item><see cref="UiComponentEvents.Press" /> is sent at most once, after
/// <see cref="UiComponentEvents.PressEnd" />, and never in an interaction where
/// <see cref="UiComponentEvents.LongPress" /> already fired. A cancelled press sends no
/// <see cref="UiComponentEvents.Press" /> at all.</item>
/// <item>A reader sends only the names the node declares, and never infers one from another - in
/// particular, <see cref="UiComponentEvents.Press" /> is never inferred from a
/// <see cref="UiComponentEvents.PressStart" />/<see cref="UiComponentEvents.PressEnd" /> pair.</item>
/// <item>There is no rate limit, unlike <see cref="UiComponentEvents.Adjust" />: a finger bounds the rate. The
/// producer is the side that coalesces.</item>
/// </list>
/// </summary>
public sealed record UiButton : UiComponentContainer
{
	/// <summary>The layout axis - see <see cref="UiComponentDirections" />. Absent means
	/// <see cref="UiComponentDirections.Vertical" />.</summary>
	public UiValue<string> Direction { get; init; }

	/// <summary>How free space is distributed along the main axis - see <see cref="UiComponentJustify" />.
	/// Absent means <see cref="UiComponentJustify.Start" />.</summary>
	public UiValue<string> Justify { get; init; }

	/// <summary>How children are aligned on the cross axis - see <see cref="UiComponentAlignments" />. Absent
	/// means <see cref="UiComponentAlignments.Stretch" />.</summary>
	public UiValue<string> Align { get; init; }

	/// <summary>The gap between children. Absent means none.</summary>
	public UiSize Gap { get; init; }

	/// <summary>Inner padding on every edge. Absent means none.</summary>
	public UiSize Padding { get; init; }

	/// <summary>The button's face, as <c>#rrggbb</c>. <b>Absent means the reader's own accent colour</b>,
	/// not a transparent face - see <see cref="UiComponentProperties.Background" />.</summary>
	public UiValue<string> Background { get; init; }

	/// <summary>Artwork drawn across the whole box behind the children. Absent means none.</summary>
	public UiValue<UiResource> Source { get; init; }

	/// <summary>How a change of <see cref="Source" /> is drawn - see
	/// <see cref="UiComponentImageTransitions" />. Absent means the new artwork simply replaces the old
	/// one.</summary>
	public UiValue<string> Transition { get; init; }

	/// <summary>How the artwork fills the box - see <see cref="UiComponentImageFits" />. Absent means
	/// <see cref="UiComponentImageFits.Contain" />.</summary>
	public UiValue<string> Fit { get; init; }

	/// <summary>A multiplier scaling the artwork about its own centre, in <c>0.1..4</c>. Absent means
	/// <c>1</c>.</summary>
	public UiValue<double> Zoom { get; init; }

	/// <summary>The artwork shifted across, as a fraction of the element's own width in <c>-1..1</c>,
	/// applied after <see cref="Zoom" />. Absent means <c>0</c>.</summary>
	public UiValue<double> OffsetX { get; init; }

	/// <summary>The same down the element's own height. Absent means <c>0</c>.</summary>
	public UiValue<double> OffsetY { get; init; }

	/// <summary>How opaque the artwork is drawn, in <c>0..1</c>. Absent means fully opaque.</summary>
	public UiValue<double> Opacity { get; init; }

	/// <summary>How brightly the artwork is drawn - see <see cref="UiImage.Brightness" />, which
	/// states the normative result. Absent means <c>1</c>.</summary>
	public UiValue<double> Brightness { get; init; }

	/// <summary>How colourful the artwork is drawn - see <see cref="UiImage.Saturation" />, which
	/// states the normative result. Absent means <c>1</c>.</summary>
	public UiValue<double> Saturation { get; init; }

	/// <summary>How the ring is drawn - see <see cref="UiComponentBorderStyles" />. Absent means no
	/// ring.</summary>
	public UiValue<string> BorderStyle { get; init; }

	/// <summary>The ring's colour, as <c>#rrggbb</c>. Absent means the style supplies its own.</summary>
	public UiValue<string> BorderColor { get; init; }

	/// <summary>How round the button's own corners are - see <see cref="UiComponentButtonCorners" />,
	/// which states both the rule absence means and what an older reader does with a value it has never
	/// heard of.</summary>
	public UiValue<string> Corner { get; init; }

	/// <inheritdoc />
	public override string Type => UiComponents.Button;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiComponentProperties.Direction, Direction);
		properties.Set(UiComponentProperties.Justify, Justify);
		properties.Set(UiComponentProperties.Align, Align);
		properties.Set(UiComponentProperties.Gap, Gap.Value);
		properties.Set(UiComponentProperties.Padding, Padding.Value);
		properties.Set(UiComponentProperties.Background, Background);
		properties.Set(UiComponentProperties.Source, Source);
		properties.Set(UiComponentProperties.Transition, Transition);
		properties.Set(UiComponentProperties.Fit, Fit);
		properties.Set(UiComponentProperties.Zoom, Zoom);
		properties.Set(UiComponentProperties.OffsetX, OffsetX);
		properties.Set(UiComponentProperties.OffsetY, OffsetY);
		properties.Set(UiComponentProperties.Opacity, Opacity);
		properties.Set(UiComponentProperties.Brightness, Brightness);
		properties.Set(UiComponentProperties.Saturation, Saturation);
		properties.Set(UiComponentProperties.BorderStyle, BorderStyle);
		properties.Set(UiComponentProperties.BorderColor, BorderColor);
		properties.Set(UiComponentProperties.Corner, Corner);
	}
}

/// <summary>
/// A container that stacks its children through the depth of the box: every child is drawn across the whole
/// content box, in declaration order, the first one furthest back.
///
/// <para>
/// <b>Why the profile needs one.</b> A <see cref="UiStack" /> can only put children beside each other,
/// so until now the only way to draw anything behind anything else was a <see cref="UiButton" />'s
/// artwork - which is a resource, and so can only ever be a picture. A backdrop that is itself an element,
/// and content that must be centred on the whole box rather than on whatever space the elements above it
/// left over, both need the children to share the box instead of dividing it.
/// </para>
///
/// <para>
/// <b>A type rather than a third <see cref="UiComponentDirections">direction</see>.</b> A reader that has not
/// heard of a direction ignores the property and lays the children out in a column - three layers stacked
/// vertically, which is wrong and gives no sign that it is. An unknown <i>type</i> is caught while the tree
/// is negotiated and draws the node's <see cref="UiElement.Fallback" /> instead.
/// </para>
///
/// <para>
/// It has no padding, gap, justify or align of its own: a layer that needs any of those wraps its content in
/// a <see cref="UiStack" />, which is also what lets two layers inset their content differently. It
/// paints no background, and a child's <see cref="MainSize" />/<see cref="UiComponentLeaf.Fill" /> means
/// nothing here - every child gets the whole box.
/// </para>
/// </summary>
public sealed record UiLayer : UiComponentContainer
{
	/// <inheritdoc />
	public override string Type => UiComponents.Layer;
}

/// <summary>
/// A series drawn as a line across the element with the area beneath it filled.
///
/// <para>
/// <b>The series arrives already normalised</b>, as fractions in <c>0..1</c> of the plot band. A chart that
/// carried raw values would drag a scale, an axis, a unit and a rounding rule onto the wire with it, and two
/// readers would disagree wherever any of those did; a producer that has the values also has the range they
/// are meaningful in, and is the only side that knows whether that range is fixed or follows the data.
/// Values outside <c>0..1</c> are clamped rather than rejected, so a series that outgrows a fixed scale
/// flattens against the top of the band instead of failing the tree.
/// </para>
///
/// <para>
/// <b>The geometry below is normative</b>, for the reason <see cref="UiRangeBar" />'s is: none of it
/// follows from the properties, and two readers that disagree on it draw visibly different charts.
/// </para>
///
/// <list type="bullet">
/// <item>The plot band spans the element's full width and runs from <see cref="PlotTop" /> of its height to
/// its bottom edge. <c>0</c> in the series is the bottom of that band and <c>1</c> is the top.</item>
/// <item>Points sit at equal horizontal spacing with the first on the leading edge and the last on the
/// trailing edge, joined by straight segments with round joins and caps. A series of exactly one point is
/// drawn as a flat line across the whole width at that point's height - a single sample is a value that has
/// held, not a dot.</item>
/// <item>The line is drawn in <see cref="Color" /> at <c>0.9</c> opacity and <see cref="Thickness" />
/// wide.</item>
/// <item>The area between the line and the element's bottom edge is filled in <see cref="Color" /> at
/// <c>0.16</c> opacity, with no stroke of its own.</item>
/// <item>An absent or empty series draws nothing at all - neither line nor fill, and in particular not a
/// flat line along the foot of the band, which would read as a real value of zero.</item>
/// </list>
/// </summary>
public sealed record UiChart : UiComponentLeaf
{
	/// <summary>The series, oldest first, as fractions of the plot band in <c>0..1</c>.</summary>
	public UiValue<IReadOnlyList<double>> Points { get; init; }

	/// <summary>The line and fill colour, as <c>#rrggbb</c>. A literal colour rather than a role because it
	/// encodes data, not theme - the same split <see cref="UiRangeBar.StartColor" /> makes. Absent
	/// means the reader's own accent colour, which is how this profile spells "the chart has no colour of
	/// its own" (see <see cref="UiSlider.LevelColor" />).</summary>
	public UiValue<string> Color { get; init; }

	/// <summary>Where the plot band begins, as a fraction of the element's own height in <c>0..1</c>.
	/// Absent means <c>0</c> - the band is the whole element.</summary>
	public UiValue<double> PlotTop { get; init; }

	/// <summary>The line's width. Absent leaves it to the reader.</summary>
	public UiSize Thickness { get; init; }

	/// <inheritdoc />
	public override string Type => UiComponents.Chart;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiComponentProperties.Points, Points);
		properties.Set(UiComponentProperties.Color, Color);
		properties.Set(UiComponentProperties.PlotTop, PlotTop);
		properties.Set(UiComponentProperties.Thickness, Thickness.Value);
	}
}

/// <summary>
/// A single line of text the user types, and the profile's only text entry.
///
/// <para>
/// <b>It reuses the slider's event pair verbatim</b> rather than inventing names for typing, because it is
/// the same shape of interaction: a value the user works continuously and then settles on.
/// <see cref="UiComponentEvents.Adjust" /> carries every intermediate value, so a producer can filter a list
/// as the user types; <see cref="UiComponentEvents.Change" /> carries the one they settled on. A producer for
/// which acting on every keystroke is a disruption - a query that costs a network call each time - acts
/// only on <see cref="UiComponentEvents.Change" /> and still shows the rest.
/// </para>
///
/// <para>
/// <b>The producer is authoritative, but never while the field has focus.</b> A reader shows what the user
/// has typed for as long as they are typing and takes <see cref="Text" /> when they are not, exactly as
/// <see cref="UiSlider" /> paints its own level until the interaction ends. Applying a patch to a
/// focused field would move the caret to a place the user did not put it, and a producer echoing the value
/// back - which a filtering producer naturally does - would do it on every keystroke.
/// </para>
///
/// <para>
/// <b>Interaction is offered only where it is declared</b>, the same rule <see cref="UiSlider" /> and
/// <see cref="UiButton" /> follow. A field that declares no events is drawn and reads back what the
/// producer put in it, and accepts no typing at all - which is what makes it usable as a read-only display
/// of a value the user elsewhere entered, without a second element for the purpose.
/// </para>
///
/// <para>
/// A reader draws one line that scrolls horizontally rather than wrapping, and sends
/// <see cref="UiComponentEvents.Adjust" /> no more than ten times a second - the same ceiling
/// <see cref="UiSlider" /> works to, and for the same reason: each accepted event is one state write
/// on the producer.
/// </para>
/// </summary>
public sealed record UiTextField : UiComponentLeaf
{
	/// <summary>The value the field holds. A literal string, not localized text: this is what the user
	/// typed, or what the producer put there for them to edit, and neither is written for a reader to
	/// translate.</summary>
	public UiValue<string> Text { get; init; }

	/// <summary>What the field shows while it is empty. Localized text, unlike <see cref="Text" /> - a
	/// prompt is written for the reader. Absent means the field shows nothing.</summary>
	public UiText Placeholder { get; init; }

	/// <summary>The font size. Absent leaves the size to the reader.</summary>
	public UiSize Size { get; init; }

	/// <inheritdoc />
	public override string Type => UiComponents.TextField;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiComponentProperties.Text, Text);
		properties.Set(UiComponentProperties.Placeholder, Placeholder.Value);
		properties.Set(UiComponentProperties.Size, Size.Value);
	}
}

/// <summary>
/// A container that scrolls, and asks its producer for more as the user reaches the end of what it holds.
///
/// <para>
/// <b>Why this is not a <see cref="UiStack" /> with a scroll flag.</b> A stack divides a bounded main
/// axis between its children, and every length in this profile is a fraction of the widget basis because
/// of it. A list's main axis is unbounded by definition, so a fraction of it would mean nothing: children
/// take their natural extent along it, and <see cref="UiComponentLeaf.MainSize" /> and
/// <see cref="UiComponentContainer.Fill" /> are ignored on that axis. Two different layouts under one type is
/// how a reader ends up guessing which one a tree meant.
/// </para>
///
/// <para>
/// <b>Only for a surface that has somewhere to scroll.</b> A deck tile does not: it is a fixed box, and a
/// list inside one would hide content behind a gesture the deck itself uses. This element exists for the
/// dialog surface, where the reader's own chrome already bounds the height.
/// </para>
///
/// <para>
/// <b>Loading more is a conversation, not a protocol.</b> The list declares
/// <see cref="UiComponentEvents.Reveal" /> and the reader reports how far the user has come; the producer
/// appends children by patch, or does not. Nothing here says how many, how often, or whether there are
/// more - a producer that has reached the end of its data simply appends nothing, and the reader asks
/// again only when the user goes further than they have been before. There is deliberately no "loading"
/// or "has more" property: both are the producer's own state, and both are already expressible as
/// children.
/// </para>
/// </summary>
// Renaming this would make the type disagree with the wire name a renderer switches on.
#pragma warning disable CA1711
public sealed record UiList : UiComponentContainer
#pragma warning restore CA1711
{
	/// <summary>The gap between children. Absent means none.</summary>
	public UiSize Gap { get; init; }

	/// <summary>Inner padding on every edge. Absent means none.</summary>
	public UiSize Padding { get; init; }

	/// <summary>The list's own background, as <c>#rrggbb</c>. Absent means it paints nothing behind its
	/// children.</summary>
	public UiValue<string> Background { get; init; }

	/// <inheritdoc />
	public override string Type => UiComponents.List;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiComponentProperties.Gap, Gap.Value);
		properties.Set(UiComponentProperties.Padding, Padding.Value);
		properties.Set(UiComponentProperties.Background, Background);
	}
}
