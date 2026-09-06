using MacroDeckHost.Integrations.Discord.Rpc;

namespace MacroDeckHost.Integrations.Discord.Actions;

internal static class DiscordVoicePatches
{
	public static DiscordVoiceSettingsPatch Mute(bool target, DiscordState state)
		=> target || !state.SelfDeafened
			? new DiscordVoiceSettingsPatch { Mute = target }
			: new DiscordVoiceSettingsPatch { Mute = false, Deaf = false };

	public static DiscordVoiceSettingsPatch Deafen(bool target) => new() { Deaf = target };
}
