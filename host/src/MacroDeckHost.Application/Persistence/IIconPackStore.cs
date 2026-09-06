using MacroDeckHost.Application.Persistence.Icons;

namespace MacroDeckHost.Application.Persistence;

public interface IIconPackStore
{
	IReadOnlyList<IconPackManifest> LoadAll();

	void Save(IconPackManifest manifest);

	void Delete(Guid packId);
}
