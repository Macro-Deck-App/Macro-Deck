using System.Text.Json;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Infrastructure.Integrations;

public class IntegrationConfigStore : IIntegrationConfigStore
{
	private readonly IIntegrationConfigEntryRepository _repository;
	private readonly ISecretService _secretService;

	public IntegrationConfigStore(
		IIntegrationConfigEntryRepository repository,
		ISecretService secretService)
	{
		_repository = repository;
		_secretService = secretService;
	}

	public async Task<IReadOnlyList<ConfigEntrySummary>> List(string integrationId)
	{
		var entities = await _repository.GetByIntegrationId(integrationId);
		return entities
			.Select(e => new ConfigEntrySummary(e.Id, e.IntegrationId, e.Title, e.CreatedAt))
			.ToList();
	}

	public async Task<ConfigEntryRecord?> Find(Guid entryId)
	{
		var entity = await _repository.GetById(entryId);
		return entity is null ? null : ToRecord(entity);
	}

	public async Task<Guid> Create(
		string integrationId,
		string title,
		IReadOnlyDictionary<string, JsonElement> values)
	{
		var entity = new IntegrationConfigEntryEntity
		{
			Id = Guid.NewGuid(),
			IntegrationId = integrationId,
			Title = title,
			ValuesJson = JsonSerializer.Serialize(values),
			UpdatedAt = DateTime.UtcNow
		};

		await _repository.Create(entity);

		return entity.Id;
	}

	public async Task<bool> Create(
		Guid entryId,
		string integrationId,
		string title,
		IReadOnlyDictionary<string, JsonElement> values)
	{
		if (await _repository.GetById(entryId) is not null)
		{
			return false;
		}

		await _repository.Create(new IntegrationConfigEntryEntity
		{
			Id = entryId,
			IntegrationId = integrationId,
			Title = title,
			ValuesJson = JsonSerializer.Serialize(values),
			UpdatedAt = DateTime.UtcNow
		});
		return true;
	}

	public async Task<bool> UpdateValues(Guid entryId, IReadOnlyDictionary<string, JsonElement> values)
	{
		var entity = await _repository.GetById(entryId);
		if (entity is null)
		{
			return false;
		}

		entity.ValuesJson = JsonSerializer.Serialize(values);
		entity.UpdatedAt = DateTime.UtcNow;
		await _repository.Update(entity);
		return true;
	}

	public async Task<bool> Replace(Guid entryId, string title, IReadOnlyDictionary<string, JsonElement> values)
	{
		var entity = await _repository.GetById(entryId);
		if (entity is null)
		{
			return false;
		}

		var superseded = SecretIds(ParseValues(entity.ValuesJson));
		superseded.ExceptWith(SecretIds(values));

		entity.Title = title;
		entity.ValuesJson = JsonSerializer.Serialize(values);
		entity.UpdatedAt = DateTime.UtcNow;
		await _repository.Update(entity);

		foreach (var secretId in superseded)
		{
			await _secretService.Delete(secretId);
		}

		return true;
	}

	public Task<bool> Rename(Guid entryId, string title, IReadOnlyDictionary<string, JsonElement> values)
		=> Replace(entryId, title, values);

	public async Task Delete(Guid entryId)
	{
		var entity = await _repository.GetById(entryId);
		if (entity is null)
		{
			return;
		}

		foreach (var secretId in SecretIds(ParseValues(entity.ValuesJson)))
		{
			await _secretService.Delete(secretId);
		}

		await _repository.TryDeleteById(entryId);
	}

	private static HashSet<Guid> SecretIds(IReadOnlyDictionary<string, JsonElement> values)
	{
		var ids = new HashSet<Guid>();
		foreach (var value in values.Values)
		{
			if (SecretReferenceJson.TryGet(value, out var secretId))
			{
				ids.Add(secretId);
			}
		}

		return ids;
	}

	private static ConfigEntryRecord ToRecord(IntegrationConfigEntryEntity entity)
		=> new(entity.Id, entity.IntegrationId, entity.Title, entity.CreatedAt, ParseValues(entity.ValuesJson));

	private static Dictionary<string, JsonElement> ParseValues(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return new Dictionary<string, JsonElement>();
		}

		return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ??
			new Dictionary<string, JsonElement>();
	}
}
