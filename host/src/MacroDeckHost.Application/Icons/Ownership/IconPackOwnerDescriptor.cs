using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Icons.Ownership;

public sealed record IconPackOwnerDescriptor(IconPackOwnerKind Kind, bool CanRemove, bool IsReadOnly = false)
{
	public static readonly IconPackOwnerDescriptor UserCreated = new(IconPackOwnerKind.User, true);
}
