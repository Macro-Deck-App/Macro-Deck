namespace MacroDeckHost.Application.Mcp;

public sealed record McpConnection
{
	public required string SessionId { get; init; }

	public required Guid TokenId { get; init; }

	public string? ClientName { get; init; }

	public string? ClientVersion { get; init; }

	public required DateTime ConnectedAt { get; init; }

	public required DateTime LastSeenAt { get; init; }
}

public interface IMcpConnections
{
	IReadOnlyList<McpConnection> Snapshot();

	int TerminateForToken(Guid tokenId);
}
