namespace MacroDeckHost.Domain.Entities;

public class UserEntity : BaseEntity
{
	public required string Username { get; set; }

	public required string PasswordHash { get; set; }

	public DateTime UpdatedAt { get; set; }
}
