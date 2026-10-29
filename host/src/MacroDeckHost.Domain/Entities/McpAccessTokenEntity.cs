namespace MacroDeckHost.Domain.Entities;

public class McpAccessTokenEntity : BaseEntity
{
	public required string Name { get; set; }

	public required string TokenHash { get; set; }

	public DateTime? LastUsedAt { get; set; }

	public DateTime? RevokedAt { get; set; }
}
