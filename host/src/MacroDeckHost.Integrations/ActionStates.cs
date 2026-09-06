using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations;

/// <summary>
/// The state sets built-in integrations expose to a button, shared so that every integration answers
/// the same question with the same ids, labels and default colours. Every set carries
/// <see cref="Unavailable" /> alongside its real states, so a user can style "we cannot tell" apart
/// from a connected target that is simply off.
///
/// <para>
/// A two-state set is ordered inactive-first, which is what <see cref="Snapshot" /> relies on to map a
/// nullable read onto it.
/// </para>
/// </summary>
internal static class ActionStates
{
	private const string InactiveColor = "#4a5568";
	private const string ActiveColor = "#2f855a";
	private const string LiveColor = "#c53030";

	public static readonly ActionStateDefinition Unavailable =
		new("unavailable", MacroDeckStrings.States.Unavailable())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = InactiveColor, LabelColor = "#cbd5e0" }
		};

	public static IReadOnlyList<ActionStateDefinition> OnOff { get; } =
		Pair("off", MacroDeckStrings.States.Off(), "on", MacroDeckStrings.States.On());

	public static IReadOnlyList<ActionStateDefinition> ActiveInactive { get; } =
		Pair("inactive", MacroDeckStrings.States.Inactive(), "active", MacroDeckStrings.States.Active());

	public static IReadOnlyList<ActionStateDefinition> Enablement { get; } =
		Pair("disabled", MacroDeckStrings.Common.Disabled(), "enabled", MacroDeckStrings.Common.Enabled());

	public static IReadOnlyList<ActionStateDefinition> Mute { get; } =
		Pair("unmuted", MacroDeckStrings.States.Unmuted(), "muted", MacroDeckStrings.States.Muted(), LiveColor);

	public static IReadOnlyList<ActionStateDefinition> Visibility { get; } =
		Pair("hidden", MacroDeckStrings.States.Hidden(), "visible", MacroDeckStrings.States.Visible());

	public static IReadOnlyList<ActionStateDefinition> Monitoring { get; } =
		Pair("not-monitoring",
			MacroDeckStrings.States.NotMonitoring(),
			"monitoring",
			MacroDeckStrings.States.Monitoring());

	public static IReadOnlyList<ActionStateDefinition> Streaming { get; } =
		Pair("not-streaming",
			MacroDeckStrings.States.NotStreaming(),
			"streaming",
			MacroDeckStrings.States.Streaming(),
			LiveColor);

	public static IReadOnlyList<ActionStateDefinition> Recording { get; } =
		Pair("not-recording",
			MacroDeckStrings.States.NotRecording(),
			"recording",
			MacroDeckStrings.States.Recording(),
			LiveColor);

	/// <summary>Recording, for a target that can also report the recording as paused.</summary>
	public static IReadOnlyList<ActionStateDefinition> RecordingWithPause { get; } =
	[
		new("not-recording", MacroDeckStrings.States.NotRecording())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = InactiveColor }
		},
		new("recording", MacroDeckStrings.States.Recording())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = LiveColor }
		},
		new("paused", MacroDeckStrings.States.RecordingPaused())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#b7791f" }
		},
		Unavailable
	];

	public static IReadOnlyList<ActionStateDefinition> Pair(
		string inactiveId,
		LocalizedText inactiveLabel,
		string activeId,
		LocalizedText activeLabel,
		string activeColor = ActiveColor) =>
	[
		new(inactiveId, inactiveLabel)
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = InactiveColor }
		},
		new(activeId, activeLabel)
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = activeColor }
		},
		Unavailable
	];

	/// <summary>
	/// Maps a nullable read onto a two-state set: <c>true</c> is the active state, <c>false</c> the
	/// inactive one, and "cannot tell" the set's unavailable state.
	/// </summary>
	public static ActionStateSnapshot Snapshot(IReadOnlyList<ActionStateDefinition> states, bool? active)
		=> new(states,
			active switch
			{
				true => states[1].Id,
				false => states[0].Id,
				null => Unavailable.Id
			});
}
