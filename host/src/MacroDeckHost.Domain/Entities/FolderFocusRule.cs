using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Domain.Entities;

public class FolderFocusRule
{
	public Guid Id { get; set; }

	public bool Enabled { get; set; } = true;

	public required string ApplicationIdentity { get; set; }

	public ApplicationIdentityKind IdentityKind { get; set; }

	public Guid DeviceId { get; set; }

	public bool ReturnOnFocusLoss { get; set; }
}
