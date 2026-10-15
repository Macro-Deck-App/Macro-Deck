using MacroDeckHost.Integrations.Discord.Protocol;
using MacroDeckHost.Integrations.Discord.State;

namespace MacroDeckHost.Integrations.Discord.Actions;

internal static class DiscordVoicePatches
{
	public static DiscordVoiceSettingsPatch Mute(bool target, DiscordState state)
	{
		return target || !state.SelfDeafened
			? new DiscordVoiceSettingsPatch { Mute = target }
			: new DiscordVoiceSettingsPatch { Mute = false, Deaf = false };
	}

	public static DiscordVoiceSettingsPatch Deafen(bool target)
	{
		return new DiscordVoiceSettingsPatch { Deaf = target };
	}
}
