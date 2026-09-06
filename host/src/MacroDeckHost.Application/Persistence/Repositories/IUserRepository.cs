using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Persistence.Repositories;

public interface IUserRepository
{
	Task<UserEntity?> GetSingle();

	Task<bool> AnyExists();

	Task Create(UserEntity user);

	Task Update(UserEntity user);
}
