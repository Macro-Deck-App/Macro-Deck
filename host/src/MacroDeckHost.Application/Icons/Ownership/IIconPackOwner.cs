using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Icons.Ownership;

public interface IIconPackOwner
{
	bool Owns(IconPackEntity pack);

	IconPackOwnerDescriptor Describe(IconPackEntity pack);

	Task Release(IconPackEntity pack);
}
