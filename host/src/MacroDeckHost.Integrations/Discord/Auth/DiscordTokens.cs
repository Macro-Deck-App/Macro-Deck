namespace MacroDeckHost.Integrations.Discord.Auth;

internal sealed record DiscordTokens(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt, string? Scope)
{
	public bool NeedsRefresh(DateTimeOffset now)
	{
		return ExpiresAt is not null && ExpiresAt <= now.AddMinutes(5);
	}

	public bool HasScope(string scope)
	{
		return Scope is not null
			&& Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Contains(scope, StringComparer.Ordinal);
	}
}
