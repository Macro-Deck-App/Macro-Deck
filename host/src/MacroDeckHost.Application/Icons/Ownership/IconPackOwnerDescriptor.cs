using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Icons.Ownership;

public sealed record IconPackOwnerDescriptor(IconPackOwnerKind Kind, bool CanRemove)
{
	public static readonly IconPackOwnerDescriptor UserCreated = new(IconPackOwnerKind.User, true);
}
