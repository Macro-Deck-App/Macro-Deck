namespace MacroDeckHost.Domain.Widgets;

/// <summary>
/// The widget types Macro Deck itself ships.
///
/// <para>
/// A widget's type is an <b>open string</b>, not a closed set: the ids below are the ones the host
/// registers at startup, and a widget carrying any other id is stored, exported and re-imported
/// unchanged rather than rejected. That is what lets a widget belonging to an integration survive the
/// integration being uninstalled, and what a provider registering its own types plugs into.
/// </para>
///
/// <para>
/// <b>The spellings are frozen.</b> They are what a widget's <c>type</c> has always been on the client
/// wire, what <see cref="MacroDeck.Sdk.Widgets.WidgetTargetInfo.Type" /> reports to plugins, and what a
/// plugin's <c>WidgetTypes</c> parameter restriction is matched against - so a plugin compiled against
/// any past SDK keeps matching. Add ids; never rename one.
/// </para>
/// </summary>
public static class WidgetTypeIds
{
	public const string ActionButton = "ActionButton";

	public const string MusicPlayer = "MusicPlayer";

	public const string Slider = "Slider";

	public const string Weather = "Weather";

	public const string HistoryGraph = "HistoryGraph";

	public const string Clock = "Clock";

	/// <summary>The built-in ids, in the order they were introduced.</summary>
	public static readonly IReadOnlyList<string> BuiltIn =
		[ActionButton, MusicPlayer, Slider, Weather, HistoryGraph, Clock];

	/// <summary>
	/// The id a widget stored before types became strings resolves to. The integers are the values the
	/// retired <c>WidgetType</c> enum persisted, and this is the only place that still knows them - the
	/// database migration and the readers of any file written by a pre-string build.
	/// </summary>
	public static string? FromLegacyValue(int value) => value switch
	{
		1 => ActionButton,
		2 => MusicPlayer,
		3 => Slider,
		4 => Weather,
		5 => HistoryGraph,
		6 => Clock,
		_ => null,
	};
}
