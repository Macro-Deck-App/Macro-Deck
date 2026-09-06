using System.Text.Json.Serialization;

namespace MacroDeckHost.Integrations.Discord.Rpc;

internal sealed record DiscordVoiceSettingsPatch
{
	[JsonPropertyName("mute")]
	public bool? Mute { get; init; }

	[JsonPropertyName("deaf")]
	public bool? Deaf { get; init; }

	[JsonPropertyName("noise_suppression")]
	public bool? NoiseSuppression { get; init; }

	[JsonPropertyName("echo_cancellation")]
	public bool? EchoCancellation { get; init; }

	[JsonPropertyName("automatic_gain_control")]
	public bool? AutomaticGainControl { get; init; }

	[JsonPropertyName("qos")]
	public bool? Qos { get; init; }

	[JsonPropertyName("input")]
	public DiscordVoiceDevicePatch? Input { get; init; }

	[JsonPropertyName("output")]
	public DiscordVoiceDevicePatch? Output { get; init; }

	[JsonPropertyName("mode")]
	public DiscordVoiceModePatch? Mode { get; init; }
}

internal sealed record DiscordVoiceDevicePatch
{
	[JsonPropertyName("volume")]
	public double? Volume { get; init; }
}

internal sealed record DiscordVoiceModePatch
{
	[JsonPropertyName("type")]
	public string? Type { get; init; }
}

internal static class DiscordVoiceModes
{
	public const string PushToTalk = "PUSH_TO_TALK";
	public const string VoiceActivity = "VOICE_ACTIVITY";
}
