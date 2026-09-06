using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Persistence.Repositories;

public interface IPluginRegistrationRepository
{
	Task<PluginRegistrationEntity?> GetByPluginId(string pluginId);

	Task<IReadOnlyList<PluginRegistrationEntity>> GetByAccessTokenId(Guid accessTokenId);

	Task<IReadOnlyList<PluginRegistrationEntity>> GetAll();

	Task<IReadOnlyList<PluginRegistrationEntity>> GetNonRevoked();

	Task Create(PluginRegistrationEntity registration);

	Task<bool> Reactivate(string pluginId,
		string displayName,
		string secretHash,
		Guid? accessTokenId,
		string origin,
		DateTime at);

	Task<bool> RotateSecret(string pluginId,
		string displayName,
		string secretHash,
		Guid? accessTokenId,
		string origin);

	Task TouchLastSeen(string pluginId, DateTime at);

	Task Revoke(string pluginId, DateTime at);

	Task<IReadOnlyList<string>> RevokeByAccessTokenId(Guid accessTokenId, DateTime at);

	Task<IReadOnlyList<string>> DeleteByAccessTokenId(Guid accessTokenId);
}
