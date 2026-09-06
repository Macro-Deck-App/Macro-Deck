namespace MacroDeckHost.Integrations.Discord;

internal sealed record DiscordGuild(string Id, string Name);

internal sealed record DiscordChannel(string Id, string Name, int Type);

internal static class DiscordChannelTypes
{
	public const int GuildText = 0;
	public const int GuildVoice = 2;
	public const int GuildStageVoice = 13;
}
