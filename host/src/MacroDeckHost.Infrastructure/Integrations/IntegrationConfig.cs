using System.Text.Json;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Domain.Enums;
using MacroDeck.Sdk.ConfigFlow;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Infrastructure.Integrations;

public class IntegrationConfig : IIntegrationConfig
{
	private readonly string _integrationId;
	private readonly IServiceScopeFactory _scopeFactory;

	public IntegrationConfig(string integrationId, IServiceScopeFactory scopeFactory)
	{
		_integrationId = integrationId;
		_scopeFactory = scopeFactory;
	}

	public async Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
	{
		using var scope = _scopeFactory.CreateScope();
		var store = scope.ServiceProvider.GetRequiredService<IIntegrationConfigStore>();
		var entries = await store.List(_integrationId);
		return entries.Select(e => new ConfigEntrySnapshot(e.Id, e.Title)).ToList();
	}

	public async Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
	{
		if (!TryGetValue(await GetOwnedEntry(entryId), key, out var element))
		{
			return null;
		}

		return element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
	}

	public async Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
	{
		var record = await GetOwnedEntry(entryId);
		if (!TryGetValue(record, key, out var element))
		{
			return null;
		}

		if (!SecretReferenceJson.TryGet(element, out var secretId))
		{
			return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
		}

		using var scope = _scopeFactory.CreateScope();
		var secretService = scope.ServiceProvider.GetRequiredService<ISecretService>();
		return await secretService.Resolve(secretId);
	}

	public async Task SetStringAsync(Guid entryId,
		string key,
		string? value,
		CancellationToken cancellationToken = default)
	{
		using var scope = _scopeFactory.CreateScope();
		var store = scope.ServiceProvider.GetRequiredService<IIntegrationConfigStore>();
		var record = OwnedOrNull(await store.Find(entryId)) ?? throw MissingEntry(entryId, key);

		var values = new Dictionary<string, JsonElement>(record.Values)
		{
			[key] = JsonSerializer.SerializeToElement(value)
		};
		await store.UpdateValues(entryId, values);
	}

	public async Task SetSecretAsync(Guid entryId,
		string key,
		string value,
		CancellationToken cancellationToken = default)
	{
		using var scope = _scopeFactory.CreateScope();
		var store = scope.ServiceProvider.GetRequiredService<IIntegrationConfigStore>();
		var secretService = scope.ServiceProvider.GetRequiredService<ISecretService>();

		var record = OwnedOrNull(await store.Find(entryId)) ?? throw MissingEntry(entryId, key);

		var values = new Dictionary<string, JsonElement>(record.Values);

		Guid secretId;
		if (values.TryGetValue(key, out var existing) && SecretReferenceJson.TryGet(existing, out var existingId))
		{
			await secretService.Replace(existingId, value);
			secretId = existingId;
		}
		else
		{
			secretId = await secretService.Create(value, SecretKind.Secret);
		}

		values[key] = JsonSerializer.SerializeToElement(new Dictionary<string, string>
			{ [SecretReferenceJson.PropertyName] = secretId.ToString() });
		await store.UpdateValues(entryId, values);
	}

	private InvalidOperationException MissingEntry(Guid entryId, string key)
		=> new($"Config entry {entryId} for integration {_integrationId} no longer exists; '{key}' was not stored.");

	private async Task<ConfigEntryRecord?> GetOwnedEntry(Guid entryId)
	{
		using var scope = _scopeFactory.CreateScope();
		var store = scope.ServiceProvider.GetRequiredService<IIntegrationConfigStore>();
		return OwnedOrNull(await store.Find(entryId));
	}

	// This is the whole boundary: an entry id is a bare GUID with no owner check anywhere below this
	// class (IIntegrationConfigStore.Find takes only the id), and every caller here now reaches the
	// store through this class. Since #413, entryId travels in from an untrusted plugin process via
	// PluginCallbackRouter's host.invoke - without this check any entry GUID a plugin could guess or
	// enumerate would be readable and writable, including another integration's secret. Mirrors
	// IntegrationVariableApi's NotOwnedByIntegration rule for the equivalent variable-store boundary.
	private ConfigEntryRecord? OwnedOrNull(ConfigEntryRecord? record)
		=> record is not null && string.Equals(record.IntegrationId, _integrationId, StringComparison.Ordinal)
			? record
			: null;

	private static bool TryGetValue(ConfigEntryRecord? record, string key, out JsonElement element)
	{
		if (record is not null && record.Values.TryGetValue(key, out element))
		{
			return true;
		}

		element = default;
		return false;
	}
}
