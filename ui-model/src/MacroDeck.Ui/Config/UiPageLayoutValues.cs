namespace MacroDeck.Ui.Config;

/// <summary>
/// The values <see cref="UiConfigButton.Variant" /> takes. Open, like every vocabulary in this profile: a reader
/// draws a value it does not know, or an absent one, as <see cref="Secondary" />, which is how every button was
/// drawn before variants existed.
/// </summary>
public static class UiButtonVariants
{
	/// <summary>The one action a region is about, such as Save or Run. Drawn filled in the accent colour.</summary>
	public const string Primary = "primary";

	/// <summary>An ordinary action. The default.</summary>
	public const string Secondary = "secondary";

	/// <summary>An action that destroys something, drawn as a warning.</summary>
	public const string Danger = "danger";

	/// <summary>A quiet action with no frame, for a toolbar or a row of many actions.</summary>
	public const string Ghost = "ghost";

	/// <summary>The variants this package ships, in declaration order.</summary>
	public static readonly IReadOnlyList<string> WellKnown = [Primary, Secondary, Danger, Ghost];
}

/// <summary>
/// The values <see cref="UiConfigStack.Align" /> takes: where the children sit along the stack's run. Absent, or a
/// value a reader does not know, is <see cref="Start" />, which is how every stack was drawn before alignment
/// existed.
/// </summary>
public static class UiStackAlignments
{
	/// <summary>Packed at the leading end. The default.</summary>
	public const string Start = "start";

	/// <summary>Packed in the middle.</summary>
	public const string Center = "center";

	/// <summary>Packed at the trailing end, as a dialog's footer buttons are.</summary>
	public const string End = "end";

	/// <summary>The first child at the leading end, the last at the trailing end, the rest spread between.</summary>
	public const string SpaceBetween = "space-between";

	/// <summary>The alignments this package ships, in declaration order.</summary>
	public static readonly IReadOnlyList<string> WellKnown = [Start, Center, End, SpaceBetween];
}

/// <summary>
/// The values <see cref="UiConfigStack.Gap" /> takes: the space between the stack's children, in the reader's own
/// spacing scale. Absent, or a value a reader does not know, is <see cref="Medium" />, which is the space every
/// stack had before gaps existed.
/// </summary>
public static class UiStackGaps
{
	/// <summary>No space.</summary>
	public const string None = "none";

	/// <summary>Less space than the default.</summary>
	public const string Small = "small";

	/// <summary>The default space.</summary>
	public const string Medium = "medium";

	/// <summary>More space than the default.</summary>
	public const string Large = "large";

	/// <summary>The gaps this package ships, in declaration order.</summary>
	public static readonly IReadOnlyList<string> WellKnown = [None, Small, Medium, Large];
}

/// <summary>
/// The values <see cref="UiConfigStack.Variant" /> takes. Absent, or a value a reader does not know, is a plain
/// stack.
/// </summary>
public static class UiStackVariants
{
	/// <summary>A framed section on the theme's card surface, as Macro Deck draws a group of settings.</summary>
	public const string Card = "card";

	/// <summary>The variants this package ships, in declaration order.</summary>
	public static readonly IReadOnlyList<string> WellKnown = [Card];
}
