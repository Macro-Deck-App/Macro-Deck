using MacroDeckHost.Application.Icons.Included;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Icons.Ownership;

public sealed class BuiltInIconPackOwner : IIconPackOwner
{
	public bool Owns(IconPackEntity pack) => pack.Id == IncludedIconPack.PackId;

	public IconPackOwnerDescriptor Describe(IconPackEntity pack)
		=> new(IconPackOwnerKind.BuiltIn, CanRemove: false, IsReadOnly: true);

	public Task Release(IconPackEntity pack) => Task.CompletedTask;
}
