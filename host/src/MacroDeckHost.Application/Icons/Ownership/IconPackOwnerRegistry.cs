using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Icons.Ownership;

public sealed class IconPackOwnerRegistry : IIconPackOwnerRegistry
{
	private readonly IReadOnlyList<IIconPackOwner> _owners;

	public IconPackOwnerRegistry(IEnumerable<IIconPackOwner> owners)
	{
		_owners = owners.ToList();
	}

	public IIconPackOwner? Resolve(IconPackEntity pack) => _owners.FirstOrDefault(owner => owner.Owns(pack));

	public IconPackOwnerDescriptor Describe(IconPackEntity pack) =>
		Resolve(pack)?.Describe(pack) ?? IconPackOwnerDescriptor.UserCreated;
}
