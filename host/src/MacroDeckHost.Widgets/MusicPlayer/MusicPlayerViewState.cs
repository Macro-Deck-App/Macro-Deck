using MacroDeck.Localization;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.References;

namespace MacroDeckHost.Widgets.MusicPlayer;

/// <summary>
/// Everything the Music Player view draws, resolved once by the session and written as one value.
///
/// <para>
/// One record rather than a state per field on purpose: the runtime diffs each <c>(node, property)</c>
/// cell against the value it last produced, so writing the whole record still emits only the property
/// keys that actually changed. A track advancing by two seconds is one <c>set-properties</c> carrying the
/// timeline's <c>value</c>, not a rebuild - and it is not even that when
/// <see cref="Position" /> was left where it was, which is what the session's re-anchoring rule is for.
/// </para>
/// </summary>
internal sealed record MusicPlayerViewState
{
	/// <summary>The state before anything has been read - what a widget shows for the moment between the
	/// session opening and the first poll answering.</summary>
	public static readonly MusicPlayerViewState Loading = new();

	/// <summary>Whether no state has been observed yet. Distinct from a disconnected player, which is a
	/// read that came back and said so.</summary>
	public bool IsLoading { get; init; } = true;

	/// <summary>The provider's own name for itself, or the user's name for this instance.</summary>
	public LocalizedText Label { get; init; } = LocalizedText.FromLiteral(null);

	/// <summary>The provider's icon, when it ships one.</summary>
	public UiResource? ProviderIcon { get; init; }

	/// <summary>The cover art currently showing, when there is any.</summary>
	public UiResource? Artwork { get; init; }

	/// <summary>The brightened artwork colour the timeline is drawn in. Absent leaves it on the theme's
	/// accent.</summary>
	public string? Accent { get; init; }

	/// <summary>The darkened artwork colour the widget paints itself in. Absent leaves it on the tile's own
	/// surface, which is what keeps a themeless widget following the reader's theme.</summary>
	public string? Background { get; init; }

	/// <summary>True when a specific instance was selected but no longer exists - re-authenticating an
	/// integration used to mint a new config entry id, which leaves widgets pointing at a dead instance.
	/// Falling back silently would be indistinguishable from a broken connection, so the widget says so.</summary>
	public bool InstanceMissing { get; init; }

	public bool IsConnected { get; init; }

	/// <summary>The provider is set up but cannot be reached right now (rate limit, outage). Deliberately
	/// distinct from "not connected": the host keeps retrying, so telling the user to set up a provider
	/// would send them to fix something that is not broken.</summary>
	public bool IsUnavailable { get; init; }

	public bool IsPlaying { get; init; }

	/// <summary>Paused is a state of its own, not "not playing": a disconnected instance keeps its last
	/// known playback state, so a dead player would otherwise render as a paused one.</summary>
	public bool IsPaused { get; init; }

	public string? TrackName { get; init; }

	public string? ArtistName { get; init; }

	public string? AlbumName { get; init; }

	/// <summary>Whatever the provider said about why it is unreachable.</summary>
	public string? StatusMessage { get; init; }

	/// <summary>Where playback had reached and how fast it is moving, for the reader to carry forward.
	/// Absent when there is nothing to advance.</summary>
	public UiProgressReference? Position { get; init; }

	/// <summary>Whether a track is loaded at all - the disc and the music note answer this, not the
	/// playback state.</summary>
	public bool HasTrack => !string.IsNullOrEmpty(TrackName);

	/// <summary>Playing and paused both get a badge; stopped, loading and disconnected get none.</summary>
	public bool ShowPlaybackBadge => (IsConnected && IsPlaying) || IsPaused;

	/// <summary>Whether the corner carries the badge that says this widget is not showing live state.
	/// A stale selection is the more specific problem, so it wins over the outage hint, and both win over
	/// the playback badge - a player that cannot be reached has no live state to report.</summary>
	public bool ShowWarningBadge => InstanceMissing || IsUnavailable;
}
