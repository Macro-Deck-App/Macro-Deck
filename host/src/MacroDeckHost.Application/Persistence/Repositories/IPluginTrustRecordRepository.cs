using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Persistence.Repositories;

public interface IPluginTrustRecordRepository
{
	Task<PluginTrustRecordEntity?> GetVersion(string pluginId, string version);

	Task<IReadOnlyList<PluginTrustRecordEntity>> GetForPlugin(string pluginId);

	Task Upsert(string pluginId, string version, string admittedVerdict, string? certificateId, DateTime installedAt);

	Task DeleteForPlugin(string pluginId);

	Task DeleteVersion(string pluginId, string version);
}
