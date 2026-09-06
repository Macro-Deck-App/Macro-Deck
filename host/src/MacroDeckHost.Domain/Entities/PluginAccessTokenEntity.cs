namespace MacroDeckHost.Domain.Entities;

public class PluginAccessTokenEntity : BaseEntity
{
	public required string Name { get; set; }

	public required string TokenHash { get; set; }

	public required string Scopes { get; set; }

	public DateTime? ExpiresAt { get; set; }

	public DateTime? LastUsedAt { get; set; }

	public DateTime? RevokedAt { get; set; }
}
