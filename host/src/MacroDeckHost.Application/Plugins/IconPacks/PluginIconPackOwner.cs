using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Plugins.IconPacks;

public sealed class PluginIconPackOwner(IPluginBundledIconPackDeclarations declarations) : IIconPackOwner
{
	public bool Owns(IconPackEntity pack) => FindDeclaration(pack) is not null;

	public IconPackOwnerDescriptor Describe(IconPackEntity pack)
		=> new(IconPackOwnerKind.Plugin, CanRemove: false, IsReadOnly: true, OwnerName: FindDeclaration(pack)?.PluginName);

	public Task Release(IconPackEntity pack) => Task.CompletedTask;

	private DeclaredIconPackSet? FindDeclaration(IconPackEntity pack)
	{
		if (pack.SourceType != IconPackSourceType.Plugin || pack.SourceId is not { } sourceId)
		{
			return null;
		}

		var separator = sourceId.IndexOf('/');
		if (separator <= 0)
		{
			return null;
		}

		var set = declarations.Find(sourceId[..separator]);
		return set is not null && set.Declares(sourceId[(separator + 1)..]) ? set : null;
	}
}
