using System.Text.Json;

namespace MacroDeckHost.Application.Integrations.ConfigFlow;

public sealed record ConfigEntrySummary(Guid Id, string IntegrationId, string Title, DateTime CreatedAt);

public sealed record ConfigEntryRecord(
	Guid Id,
	string IntegrationId,
	string Title,
	DateTime CreatedAt,
	IReadOnlyDictionary<string, JsonElement> Values);

public interface IIntegrationConfigStore
{
	Task<IReadOnlyList<ConfigEntrySummary>> List(string integrationId);

	Task<ConfigEntryRecord?> Find(Guid entryId);

	Task<Guid> Create(string integrationId, string title, IReadOnlyDictionary<string, JsonElement> values);

	Task<bool> Create(Guid entryId,
		string integrationId,
		string title,
		IReadOnlyDictionary<string, JsonElement> values)
		=> Task.FromException<bool>(
			new NotSupportedException("Explicit configuration entry identities are not supported by this store."));

	Task<bool> UpdateValues(Guid entryId, IReadOnlyDictionary<string, JsonElement> values);

	Task<bool> Replace(Guid entryId, string title, IReadOnlyDictionary<string, JsonElement> values);

	Task<bool> Rename(Guid entryId, string title, IReadOnlyDictionary<string, JsonElement> values)
		=> Replace(entryId, title, values);

	Task Delete(Guid entryId);
}
