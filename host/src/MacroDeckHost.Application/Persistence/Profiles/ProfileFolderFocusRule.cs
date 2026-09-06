using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Persistence.Profiles;

public sealed class ProfileFolderFocusRule
{
	public Guid Id { get; set; }

	public bool Enabled { get; set; } = true;

	public string ApplicationIdentity { get; set; } = string.Empty;

	public ApplicationIdentityKind IdentityKind { get; set; }

	public Guid DeviceId { get; set; }

	public bool ReturnOnFocusLoss { get; set; }
}
