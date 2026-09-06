namespace MacroDeck.Ui.Previews;

/// <summary>
/// The primitive vocabularies a preview scenario can author its tree in. Open like every other UI
/// vocabulary: a value this build does not recognise travels unchanged and the tree decides how it
/// renders, so a future profile never needs a new member here first.
/// </summary>
public static class UiPreviewProfiles
{
	/// <summary>The configuration primitives - a settings or setup surface.</summary>
	public const string Config = "config";

	/// <summary>The widget primitives - a glanceable view sized off the box it is given.</summary>
	public const string Widget = "widget";

	/// <summary>The profiles this package ships names for. Not exhaustive.</summary>
	public static readonly IReadOnlyList<string> WellKnown = [Config, Widget];
}
