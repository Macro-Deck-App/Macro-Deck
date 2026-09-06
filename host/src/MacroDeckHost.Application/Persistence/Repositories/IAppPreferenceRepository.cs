using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Persistence.Repositories;

public interface IAppPreferenceRepository
{
	Task<AppPreferenceEntity?> GetByKey(string key);

	Task SetValue(string key, string value);
}
