using MacroDeckHost.Localization;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.SoundPad;

internal static class SoundPadVariables
{
	public const string CurrentSound = "soundpad_current_sound";
	public const string CurrentArtist = "soundpad_current_artist";
	public const string PlaybackState = "soundpad_playback_state";
	public const string IsPlaying = "soundpad_is_playing";

	// Macro Deck 2's SoundPad plugin published this exact name; migrated buttons bind to it unchanged.
	public const string Recording = "soundpad_recording";

	public const string Volume = "soundpad_volume";
	public const string Duration = "soundpad_duration";
	public const string Position = "soundpad_position";
	public const string IsMuted = "soundpad_is_muted";
	public const string IsConnected = "soundpad_is_connected";

	private static readonly TimeSpan _fast = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan _normal = TimeSpan.FromSeconds(2);

	public static IReadOnlyList<VariableDefinition> All { get; } =
	[
		VariableDefinition.Eager(CurrentSound, VariableType.Text, refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.SoundPad.Variables.CurrentSound()
			},
		VariableDefinition.Eager(CurrentArtist, VariableType.Text, refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.SoundPad.Variables.CurrentArtist()
			},
		VariableDefinition.Eager(PlaybackState, VariableType.Text, refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.SoundPad.Variables.PlaybackState()
			},
		VariableDefinition.Eager(IsPlaying, VariableType.Boolean, refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.SoundPad.Variables.IsPlaying()
			},
		VariableDefinition.Eager(Recording, VariableType.Boolean, refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.SoundPad.Variables.Recording()
			},
		VariableDefinition.Eager(Volume, VariableType.Numeric, 0, _normal)
			with
			{
				DisplayName = AppStrings.Integrations.SoundPad.Variables.Volume(),
				Unit = "%",
				SemanticKind = VariableSemanticKinds.Percentage,
				Write = MusicPlayerVariableWrites.Volume
			},
		VariableDefinition.Eager(Duration, VariableType.Numeric, decimalPlaces: 0, refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.SoundPad.Variables.Duration(),
				SemanticKind = VariableSemanticKinds.Duration
			},
		VariableDefinition.Eager(Position, VariableType.Numeric, decimalPlaces: 0, refreshInterval: _fast)
			with
			{
				DisplayName = AppStrings.Integrations.SoundPad.Variables.Position(),
				SemanticKind = VariableSemanticKinds.Duration,
				Write = MusicPlayerVariableWrites.Position
			},
		VariableDefinition.Eager(IsMuted, VariableType.Boolean, refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.SoundPad.Variables.IsMuted()
			},
		VariableDefinition.Eager(IsConnected, VariableType.Boolean, refreshInterval: _normal)
			with
			{
				DisplayName = AppStrings.Integrations.SoundPad.Variables.IsConnected()
			}
	];
}
