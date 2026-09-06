using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Domain.Entities;

public class RefreshTokenEntity : BaseEntity
{
	public required Guid UserId { get; set; }

	public required string TokenHash { get; set; }

	public Guid? DeviceId { get; set; }

	public required AuthScope Scope { get; set; }

	public DateTime ExpiresAt { get; set; }

	public DateTime? RevokedAt { get; set; }

	public Guid? ReplacedById { get; set; }
}
