namespace MacroDeckHost.Application.Mcp;

public sealed record McpAccessToken
{
	public required Guid Id { get; init; }

	public required string Name { get; init; }

	public required DateTime CreatedAt { get; init; }

	public DateTime? LastUsedAt { get; init; }

	public DateTime? RevokedAt { get; init; }
}

public sealed record McpAccessTokenCreated
{
	public required McpAccessToken Token { get; init; }

	public required string PlaintextToken { get; init; }
}

public enum McpAccessTokenDeleteResult
{
	Deleted,
	NotFound,
	NotRevoked
}

public enum McpAccessTokenRevokeResult
{
	Revoked,
	NotFound,
	AlreadyRevoked
}
