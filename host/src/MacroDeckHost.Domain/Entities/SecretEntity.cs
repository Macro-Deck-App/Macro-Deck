using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Domain.Entities;

public class SecretEntity : BaseEntity
{
	public required SecretKind Kind { get; set; }

	public string EncryptedValue { get; set; } = string.Empty;

	public DateTime UpdatedAt { get; set; }
}
