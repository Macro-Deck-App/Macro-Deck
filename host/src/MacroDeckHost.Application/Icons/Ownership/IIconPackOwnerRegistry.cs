using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Icons.Ownership;

public interface IIconPackOwnerRegistry
{
	IIconPackOwner? Resolve(IconPackEntity pack);

	IconPackOwnerDescriptor Describe(IconPackEntity pack);
}
