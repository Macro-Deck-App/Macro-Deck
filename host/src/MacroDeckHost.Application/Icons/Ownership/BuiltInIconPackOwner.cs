using MacroDeckHost.Application.Icons.Included;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Icons.Ownership;

public sealed class BuiltInIconPackOwner : IIconPackOwner
{
	public bool Owns(IconPackEntity pack)
	{
		return pack.Id == IncludedIconPack.PackId;
	}

	public IconPackOwnerDescriptor Describe(IconPackEntity pack)
	{
		return new IconPackOwnerDescriptor(IconPackOwnerKind.BuiltIn, false, true);
	}

	public Task Release(IconPackEntity pack)
	{
		return Task.CompletedTask;
	}
}
