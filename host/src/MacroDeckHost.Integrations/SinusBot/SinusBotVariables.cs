using MacroDeckHost.Localization;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.SinusBot;

internal static class SinusBotVariables
{
	public const string VolumeName = "volume";

	public const string CurrentPositionName = "current_position";

	private const string PercentUnit = "%";

	public static IReadOnlyList<VariableDefinition> Declare(string instanceKey,
		VariableConfiguration? configuration = null) =>
	[
		VariableDefinition.Eager($"sinusbot_{instanceKey}_current_track_name",
				VariableType.Text,
				refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.SinusBot.Variables.CurrentTrackName(),
				Configuration = configuration
			},
		VariableDefinition.Eager($"sinusbot_{instanceKey}_current_artist",
				VariableType.Text,
				refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.SinusBot.Variables.CurrentArtist(),
				Configuration = configuration
			},
		VariableDefinition.Eager($"sinusbot_{instanceKey}_current_album",
				VariableType.Text,
				refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.SinusBot.Variables.CurrentAlbum(),
				Configuration = configuration
			},
		VariableDefinition.Eager($"sinusbot_{instanceKey}_playback_state",
				VariableType.Text,
				refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.SinusBot.Variables.PlaybackState(),
				Configuration = configuration
			},
		VariableDefinition.Eager($"sinusbot_{instanceKey}_is_playing",
				VariableType.Boolean,
				refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.SinusBot.Variables.IsPlaying(),
				Configuration = configuration
			},
		VariableDefinition.Eager($"sinusbot_{instanceKey}_volume", VariableType.Numeric, 0, TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.SinusBot.Variables.Volume(),
				Configuration = configuration,
				Unit = PercentUnit,
				SemanticKind = VariableSemanticKinds.Percentage,
				Write = MusicPlayerVariableWrites.Volume
			},
		VariableDefinition.Eager($"sinusbot_{instanceKey}_track_duration",
				VariableType.Numeric,
				decimalPlaces: 0,
				refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.SinusBot.Variables.TrackDuration(),
				Configuration = configuration,
				SemanticKind = VariableSemanticKinds.Duration
			},
		VariableDefinition.Eager($"sinusbot_{instanceKey}_current_position",
				VariableType.Numeric,
				decimalPlaces: 0,
				refreshInterval: TimeSpan.FromSeconds(1))
			with
			{
				DisplayName = AppStrings.Integrations.SinusBot.Variables.CurrentPosition(),
				Configuration = configuration,
				SemanticKind = VariableSemanticKinds.Duration,
				Write = MusicPlayerVariableWrites.Position
			},
		VariableDefinition.Eager($"sinusbot_{instanceKey}_progress_percentage",
				VariableType.Numeric,
				decimalPlaces: 0,
				refreshInterval: TimeSpan.FromSeconds(1))
			with
			{
				DisplayName = AppStrings.Integrations.SinusBot.Variables.ProgressPercentage(),
				Configuration = configuration
			},
		VariableDefinition.Eager($"sinusbot_{instanceKey}_album_art_url",
				VariableType.Text,
				refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.SinusBot.Variables.AlbumArtUrl(),
				Configuration = configuration
			},
		VariableDefinition.Eager($"sinusbot_{instanceKey}_device_name",
				VariableType.Text,
				refreshInterval: TimeSpan.FromSeconds(5))
			with
			{
				DisplayName = AppStrings.Integrations.SinusBot.Variables.DeviceName(),
				Configuration = configuration
			},
		VariableDefinition.Eager($"sinusbot_{instanceKey}_device_type",
				VariableType.Text,
				refreshInterval: TimeSpan.FromSeconds(5))
			with
			{
				DisplayName = AppStrings.Integrations.SinusBot.Variables.DeviceType(),
				Configuration = configuration
			},
		VariableDefinition.Eager($"sinusbot_{instanceKey}_shuffle_enabled",
				VariableType.Boolean,
				refreshInterval: TimeSpan.FromSeconds(5))
			with
			{
				DisplayName = AppStrings.Integrations.SinusBot.Variables.ShuffleEnabled(),
				Configuration = configuration
			},
		VariableDefinition.Eager($"sinusbot_{instanceKey}_repeat_mode",
				VariableType.Text,
				refreshInterval: TimeSpan.FromSeconds(5))
			with
			{
				DisplayName = AppStrings.Integrations.SinusBot.Variables.RepeatMode(),
				Configuration = configuration
			},
		VariableDefinition.Eager($"sinusbot_{instanceKey}_is_connected",
				VariableType.Boolean,
				refreshInterval: TimeSpan.FromSeconds(5))
			with
			{
				DisplayName = AppStrings.Integrations.SinusBot.Variables.IsConnected(),
				Configuration = configuration
			}
	];
}
