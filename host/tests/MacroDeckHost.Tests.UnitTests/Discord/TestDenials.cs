using MacroDeckHost.Integrations.Discord.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Discord;

internal static class TestDenials
{
	private const string UserSid = "S-1-5-21-1000";

	public static DiscordIpcProcess Self { get; } = new(100, "MacroDeckHost", 1, false, UserSid);

	public static DiscordIpcProcess Server(
		string name = "Discord",
		int session = 1,
		bool? elevated = false,
		string? userSid = UserSid)
	{
		return new DiscordIpcProcess(4242, name, session, elevated, userSid);
	}

	public static DiscordIpcDenial Of(DiscordIpcDenialCause cause, string processName = "Discord")
	{
		return new DiscordIpcDenial("discord-ipc-0", Server(processName), Self, cause);
	}

	public static DiscordIpcDenial ProbeFailed(string endpoint = "discord-ipc-0")
	{
		return new DiscordIpcDenial(endpoint, null, Self, DiscordIpcDenialCause.Unknown);
	}

	public static DiscordIpcDenial Unattributed(string endpoint = "discord-ipc-0")
	{
		return new DiscordIpcDenial(endpoint, null, null, DiscordIpcDenialCause.Unknown);
	}
}
