using MacroDeck.Localization;
using MacroDeckHost.Integrations.Discord.Protocol;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Discord.Connections;

internal static class DiscordAccessDeniedText
{
	public static LocalizedText ConfigError(DiscordIpcDenial? denial)
	{
		return denial?.Cause switch
		{
			DiscordIpcDenialCause.PrivilegeMismatch => AppStrings.Integrations.Discord.Config.PrivilegeMismatch(),
			DiscordIpcDenialCause.OtherSession => AppStrings.Integrations.Discord.Config.AccessDeniedOtherSession(),
			DiscordIpcDenialCause.OtherProgram => AppStrings.Integrations.Discord.Config
				.AccessDeniedOtherProgram(Program(denial)),
			DiscordIpcDenialCause.Blocked => AppStrings.Integrations.Discord.Config.AccessDeniedBlocked(),
			_ => AppStrings.Integrations.Discord.Config.AccessDeniedUnknown()
		};
	}

	public static LocalizedText IssueDescription(DiscordIpcDenial? denial)
	{
		return denial?.Cause switch
		{
			DiscordIpcDenialCause.PrivilegeMismatch => AppStrings.Integrations.Discord.Issues
				.PrivilegeMismatchDescription(),
			DiscordIpcDenialCause.OtherSession => AppStrings.Integrations.Discord.Issues
				.AccessDeniedOtherSessionDescription(),
			DiscordIpcDenialCause.OtherProgram => AppStrings.Integrations.Discord.Issues
				.AccessDeniedOtherProgramDescription(Program(denial)),
			DiscordIpcDenialCause.Blocked => AppStrings.Integrations.Discord.Issues.AccessDeniedBlockedDescription(),
			_ => AppStrings.Integrations.Discord.Issues.AccessDeniedUnknownDescription()
		};
	}

	public static LocalizedText IssueResolution(DiscordIpcDenial? denial)
	{
		return denial?.Cause switch
		{
			DiscordIpcDenialCause.PrivilegeMismatch => AppStrings.Integrations.Discord.Issues
				.PrivilegeMismatchResolution(),
			DiscordIpcDenialCause.OtherSession => AppStrings.Integrations.Discord.Issues
				.AccessDeniedOtherSessionResolution(),
			DiscordIpcDenialCause.OtherProgram => AppStrings.Integrations.Discord.Issues
				.AccessDeniedOtherProgramResolution(Program(denial)),
			DiscordIpcDenialCause.Blocked => AppStrings.Integrations.Discord.Issues.AccessDeniedBlockedResolution(),
			_ => AppStrings.Integrations.Discord.Issues.AccessDeniedUnknownResolution()
		};
	}

	private static string Program(DiscordIpcDenial? denial)
	{
		return denial?.Server?.ProcessName ?? string.Empty;
	}
}
