namespace MacroDeckHost.Integrations.Discord.Protocol;

internal enum DiscordIpcDenialCause
{
	Unknown,
	OtherSession,
	OtherProgram,
	PrivilegeMismatch,
	Blocked
}

internal sealed record DiscordIpcProcess(
	int ProcessId,
	string? ProcessName,
	int? SessionId,
	bool? Elevated,
	string? UserSid);

internal sealed record DiscordIpcDenial(
	string Endpoint,
	DiscordIpcProcess? Server,
	DiscordIpcProcess? Self,
	DiscordIpcDenialCause Cause)
{
	public bool HasKnownOwner => Server?.ProcessName is not null;

	public bool IsDiscordOwned => IsDiscord(Server?.ProcessName);

	public bool WasProbed => Self is not null;

	public bool? SameUser => Server?.UserSid is { } server && Self?.UserSid is { } self
		? string.Equals(server, self, StringComparison.OrdinalIgnoreCase)
		: null;

	public static DiscordIpcDenial Create(string endpoint, DiscordIpcProcess? server, DiscordIpcProcess? self)
	{
		return new DiscordIpcDenial(endpoint, server, self, Classify(server, self));
	}

	public static DiscordIpcDenialCause Classify(DiscordIpcProcess? server, DiscordIpcProcess? self)
	{
		if (server?.ProcessName is null)
		{
			return DiscordIpcDenialCause.Unknown;
		}

		if (!IsDiscord(server.ProcessName))
		{
			return DiscordIpcDenialCause.OtherProgram;
		}

		if (server.SessionId is { } serverSession && self?.SessionId is { } ownSession && serverSession != ownSession)
		{
			return DiscordIpcDenialCause.OtherSession;
		}

		if (server.UserSid is { } serverUser
			&& self?.UserSid is { } ownUser
			&& !string.Equals(serverUser, ownUser, StringComparison.OrdinalIgnoreCase))
		{
			return DiscordIpcDenialCause.OtherSession;
		}

		if (server.Elevated is { } serverElevated && self?.Elevated is { } ownElevated)
		{
			return serverElevated == ownElevated
				? DiscordIpcDenialCause.Blocked
				: DiscordIpcDenialCause.PrivilegeMismatch;
		}

		return DiscordIpcDenialCause.Unknown;
	}

	public static DiscordIpcDenial Prefer(IReadOnlyList<DiscordIpcDenial> denials)
	{
		return denials.FirstOrDefault(d => d.IsDiscordOwned)
			?? denials.FirstOrDefault(d => d.HasKnownOwner)
			?? denials[0];
	}

	private static bool IsDiscord(string? processName)
	{
		return processName?.StartsWith("Discord", StringComparison.OrdinalIgnoreCase) == true;
	}
}
