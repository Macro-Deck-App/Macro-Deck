using MacroDeckHost.Localization;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.WebNowPlaying;

internal static class WebNowPlayingVariables
{
	public const string IsConnected = "webnowplaying_is_connected";

	public const string IsConnectedId = "webnowplaying-is-connected";

	public const string CurrentPositionId = "webnowplaying-current-position";

	public const string VolumeId = "webnowplaying-volume";

	private const string PercentUnit = "%";

	private static readonly TimeSpan _fast = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan _normal = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan _slow = TimeSpan.FromSeconds(5);

	public static IReadOnlyList<VariableDefinition> All { get; } =
	[
		VariableDefinition.Eager(IsConnected, VariableType.Boolean, refreshInterval: _slow)
			with
			{
				DisplayName = AppStrings.Integrations.WebNowPlaying.Variables.IsConnected()
			},
		VariableDefinition.Eager("webnowplaying_player_name", VariableType.Text, refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.WebNowPlaying.Variables.PlayerName()
			},
		VariableDefinition.Eager("webnowplaying_current_track_name", VariableType.Text, refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.WebNowPlaying.Variables.CurrentTrackName()
			},
		VariableDefinition.Eager("webnowplaying_current_artist", VariableType.Text, refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.WebNowPlaying.Variables.CurrentArtist()
			},
		VariableDefinition.Eager("webnowplaying_current_album", VariableType.Text, refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.WebNowPlaying.Variables.CurrentAlbum()
			},
		VariableDefinition.Eager("webnowplaying_album_art_url", VariableType.Text, refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.WebNowPlaying.Variables.AlbumArtUrl()
			},
		VariableDefinition.Eager("webnowplaying_playback_state", VariableType.Text, refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.WebNowPlaying.Variables.PlaybackState()
			},
		VariableDefinition.Eager("webnowplaying_is_playing", VariableType.Boolean, refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.WebNowPlaying.Variables.IsPlaying()
			},
		VariableDefinition.Eager("webnowplaying_current_position", VariableType.Numeric, 0, _fast)
			with
			{
				DisplayName = AppStrings.Integrations.WebNowPlaying.Variables.CurrentPosition(),
				SemanticKind = VariableSemanticKinds.Duration,
				Write = MusicPlayerVariableWrites.Position
			},
		VariableDefinition.Eager("webnowplaying_track_duration", VariableType.Numeric, 0, _normal)
			with
			{
				DisplayName = AppStrings.Integrations.WebNowPlaying.Variables.TrackDuration(),
				SemanticKind = VariableSemanticKinds.Duration
			},
		VariableDefinition.Eager("webnowplaying_progress_percentage", VariableType.Numeric, 0, _fast)
			with
			{
				DisplayName = AppStrings.Integrations.WebNowPlaying.Variables.ProgressPercentage()
			},
		VariableDefinition.Eager("webnowplaying_volume", VariableType.Numeric, 0, _normal)
			with
			{
				DisplayName = AppStrings.Integrations.WebNowPlaying.Variables.Volume(),
				Unit = PercentUnit,
				SemanticKind = VariableSemanticKinds.Percentage,
				Write = MusicPlayerVariableWrites.Volume
			},
		VariableDefinition.Eager("webnowplaying_shuffle_enabled", VariableType.Boolean, refreshInterval: _slow)
			with
			{
				DisplayName = AppStrings.Integrations.WebNowPlaying.Variables.ShuffleEnabled()
			},
		VariableDefinition.Eager("webnowplaying_repeat_mode", VariableType.Text, refreshInterval: _slow)
			with
			{
				DisplayName = AppStrings.Integrations.WebNowPlaying.Variables.RepeatMode()
			},
		VariableDefinition.Eager("webnowplaying_rating", VariableType.Numeric, 0, _normal)
			with
			{
				DisplayName = AppStrings.Integrations.WebNowPlaying.Variables.Rating()
			}
	];
}
