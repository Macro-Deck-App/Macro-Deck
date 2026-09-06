namespace MacroDeckHost.Integrations.Discord;

internal sealed record DiscordState
{
	public static DiscordState Disconnected { get; } = new();

	public bool IsConnected { get; init; }

	public string? UserId { get; init; }

	public string? UserName { get; init; }

	public bool SelfMuted { get; init; }

	public bool SelfDeafened { get; init; }

	public bool ServerMuted { get; init; }

	public bool ServerDeafened { get; init; }

	public bool EffectivelyMuted => SelfMuted || ServerMuted || SelfDeafened || ServerDeafened;

	public bool EffectivelySelfMuted => SelfMuted || SelfDeafened;

	public bool Deafened => SelfDeafened || ServerDeafened;

	public bool InVoiceChannel => VoiceChannelId is not null;

	public string? VoiceChannelId { get; init; }

	public string? VoiceChannelName { get; init; }

	public string? VoiceGuildId { get; init; }

	public string? VoiceGuildName { get; init; }

	public double InputVolume { get; init; }

	public double OutputVolume { get; init; }

	public string? VoiceMode { get; init; }

	public bool NoiseSuppression { get; init; }

	public bool EchoCancellation { get; init; }

	public bool AutomaticGainControl { get; init; }

	public string? VoiceConnectionState { get; init; }

	public int? AveragePing { get; init; }

	public bool SelfSpeaking { get; init; }

	public DiscordState WithoutVoiceChannel() => this with
	{
		VoiceChannelId = null,
		VoiceChannelName = null,
		VoiceGuildId = null,
		VoiceGuildName = null,
		ServerMuted = false,
		ServerDeafened = false,
		SelfSpeaking = false,
		VoiceConnectionState = null,
		AveragePing = null
	};
}
