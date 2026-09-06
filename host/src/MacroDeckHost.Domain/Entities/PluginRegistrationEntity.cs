namespace MacroDeckHost.Domain.Entities;

public class PluginRegistrationEntity : BaseEntity
{
	public required string PluginId { get; set; }

	public required string DisplayName { get; set; }

	public required string SecretHash { get; set; }

	public Guid? AccessTokenId { get; set; }

	public required string Origin { get; set; }

	public DateTime? LastSeenAt { get; set; }

	public DateTime? RevokedAt { get; set; }
}
