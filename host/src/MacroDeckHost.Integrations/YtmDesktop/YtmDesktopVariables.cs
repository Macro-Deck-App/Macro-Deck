using MacroDeckHost.Localization;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.YtmDesktop;

internal static class YtmDesktopVariables
{
	public const string Prefix = "ytmdesktop_";

	public const string IsConnected = "ytmdesktop_is_connected";

	public const string IsConnectedId = "ytmdesktop-is-connected";

	public const string CurrentPositionId = "ytmdesktop-current-position";

	public const string VolumeId = "ytmdesktop-volume";

	private const string PercentUnit = "%";

	private static readonly TimeSpan _fast = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan _normal = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan _slow = TimeSpan.FromSeconds(5);

	public static IReadOnlyList<VariableDefinition> All { get; } =
	[
		VariableDefinition.Eager(IsConnected,
				VariableType.Boolean,
				refreshInterval: _slow)
			with
			{
				DisplayName = AppStrings.Integrations.YtmDesktop.Variables.IsConnected()
			},
		VariableDefinition.Eager("ytmdesktop_current_track_name",
				VariableType.Text,
				refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.YtmDesktop.Variables.CurrentTrackName()
			},
		VariableDefinition.Eager("ytmdesktop_current_artist",
				VariableType.Text,
				refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.YtmDesktop.Variables.CurrentArtist()
			},
		VariableDefinition.Eager("ytmdesktop_current_album",
				VariableType.Text,
				refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.YtmDesktop.Variables.CurrentAlbum()
			},
		VariableDefinition.Eager("ytmdesktop_album_art_url",
				VariableType.Text,
				refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.YtmDesktop.Variables.AlbumArtUrl()
			},
		VariableDefinition.Eager("ytmdesktop_playback_state",
				VariableType.Text,
				refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.YtmDesktop.Variables.PlaybackState()
			},
		VariableDefinition.Eager("ytmdesktop_is_playing",
				VariableType.Boolean,
				refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.YtmDesktop.Variables.IsPlaying()
			},
		VariableDefinition.Eager("ytmdesktop_current_position", VariableType.Numeric, 0, _fast)
			with
			{
				DisplayName = AppStrings.Integrations.YtmDesktop.Variables.CurrentPosition(),
				SemanticKind = VariableSemanticKinds.Duration,
				Write = MusicPlayerVariableWrites.Position
			},
		VariableDefinition.Eager("ytmdesktop_track_duration", VariableType.Numeric, 0, _normal)
			with
			{
				DisplayName = AppStrings.Integrations.YtmDesktop.Variables.TrackDuration(),
				SemanticKind = VariableSemanticKinds.Duration
			},
		VariableDefinition.Eager("ytmdesktop_progress_percentage", VariableType.Numeric, 0, _fast)
			with
			{
				DisplayName = AppStrings.Integrations.YtmDesktop.Variables.ProgressPercentage()
			},
		VariableDefinition.Eager("ytmdesktop_volume", VariableType.Numeric, 0, _normal)
			with
			{
				DisplayName = AppStrings.Integrations.YtmDesktop.Variables.Volume(),
				Unit = PercentUnit,
				SemanticKind = VariableSemanticKinds.Percentage,
				Write = MusicPlayerVariableWrites.Volume
			},
		VariableDefinition.Eager("ytmdesktop_is_muted",
				VariableType.Boolean,
				refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.YtmDesktop.Variables.IsMuted()
			},
		VariableDefinition.Eager("ytmdesktop_like_status",
				VariableType.Text,
				refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.YtmDesktop.Variables.LikeStatus()
			},
		VariableDefinition.Eager("ytmdesktop_is_liked",
				VariableType.Boolean,
				refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.YtmDesktop.Variables.IsLiked()
			},
		VariableDefinition.Eager("ytmdesktop_shuffle_enabled",
				VariableType.Boolean,
				refreshInterval: _slow)
			with
			{
				DisplayName = AppStrings.Integrations.YtmDesktop.Variables.ShuffleEnabled()
			},
		VariableDefinition.Eager("ytmdesktop_repeat_mode",
				VariableType.Text,
				refreshInterval: _slow)
			with
			{
				DisplayName = AppStrings.Integrations.YtmDesktop.Variables.RepeatMode()
			},
		VariableDefinition.Eager("ytmdesktop_is_live", VariableType.Boolean, refreshInterval: _slow)
			with
			{
				DisplayName = AppStrings.Integrations.YtmDesktop.Variables.IsLive()
			},
		VariableDefinition.Eager("ytmdesktop_media_type", VariableType.Text, refreshInterval: _slow)
			with
			{
				DisplayName = AppStrings.Integrations.YtmDesktop.Variables.MediaType()
			},
		VariableDefinition.Eager("ytmdesktop_ad_playing",
				VariableType.Boolean,
				refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.YtmDesktop.Variables.AdPlaying()
			},
		VariableDefinition.Eager("ytmdesktop_video_id", VariableType.Text, refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.YtmDesktop.Variables.VideoId()
			},
		VariableDefinition.Eager("ytmdesktop_device_name",
				VariableType.Text,
				refreshInterval: _slow)
			with
			{
				DisplayName = AppStrings.Integrations.YtmDesktop.Variables.DeviceName()
			}
	];
}
