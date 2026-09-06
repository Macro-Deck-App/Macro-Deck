using MacroDeckHost.Application.Persistence.Profiles;

namespace MacroDeckHost.Application.Persistence;

public interface IProfileStore
{
	ProfileLoadResult LoadAll();

	bool Save(ProfileFile profile);

	void Delete(Guid id);
}
