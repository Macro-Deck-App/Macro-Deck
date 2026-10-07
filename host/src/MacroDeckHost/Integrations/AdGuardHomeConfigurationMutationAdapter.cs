using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeckHost.Application.AdGuardHome;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Integrations.AdGuardHome;

namespace MacroDeckHost.Integrations;

internal sealed class AdGuardHomeConfigurationMutationAdapter : IIntegrationConfigMutationAdapter
{
	private readonly IIntegrationRegistry _registry;
	private readonly IAdGuardHomeInstances _instances;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ConcurrentDictionary<Guid, string> _pending = new();

	public AdGuardHomeConfigurationMutationAdapter(
		IIntegrationRegistry registry,
		IAdGuardHomeInstances instances,
		IServiceScopeFactory scopeFactory)
	{
		_registry = registry;
		_instances = instances;
		_scopeFactory = scopeFactory;
	}

	public string IntegrationId => AdGuardHomeIntegration.IntegrationId;

	public IntegrationConfigMutationPreparationResult Prepare(IntegrationConfigMutationPreparation preparation)
	{
		var values = new Dictionary<string, JsonElement>(preparation.Values);

		var key = preparation.Existing is { } existing ? ReadKey(existing) : null;
		if (key is null)
		{
			var occupied = preparation.Siblings
				.Where(sibling => sibling.Id != preparation.EntryId)
				.Select(ReadKey)
				.OfType<string>()
				.Concat(_pending.Where(pair => pair.Key != preparation.EntryId).Select(pair => pair.Value))
				.ToHashSet(StringComparer.Ordinal);
			key = AdGuardHomeIntegration.UniqueKey(preparation.Title, occupied);
		}

		_pending[preparation.EntryId] = key;
		values[AdGuardHomeConfigKeys.VariableKey] = JsonSerializer.SerializeToElement(key);
		return new IntegrationConfigMutationPreparationResult(true, values);
	}

	public IntegrationConfigEntryStatus GetStatus(ConfigEntryRecord entry)
		=> _instances.Find(entry.Id.ToString("D"))?.Connection switch
		{
			null => IntegrationConfigEntryStatus.Disconnected,
			AdGuardHomeConnection.Connected => IntegrationConfigEntryStatus.Connected,
			AdGuardHomeConnection.Connecting => IntegrationConfigEntryStatus.Connecting,
			AdGuardHomeConnection.Unauthorized => IntegrationConfigEntryStatus.NeedsReconfiguration,
			_ => IntegrationConfigEntryStatus.Disconnected,
		};

	public bool IsUsable(ConfigEntryRecord entry) => true;

	public Task ReloadAsync(CancellationToken cancellationToken)
		=> _registry.Integrations.OfType<AdGuardHomeIntegration>().FirstOrDefault() is { IsInitialized: true } integration
			? integration.ReloadConfigurationsAsync(cancellationToken)
			: Task.CompletedTask;

	public void ReleasePreparation(Guid entryId) => _pending.TryRemove(entryId, out _);

	public async Task SynchronizeVariablesAsync(
		IReadOnlyList<ConfigEntryRecord> entries,
		CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var service = scope.ServiceProvider.GetRequiredService<IVariableService>();

		foreach (var id in OrphanedVariables(await service.GetByOwnerIntegration(IntegrationId), entries))
		{
			await service.DeleteIntegrationVariable(IntegrationId, id);
		}
	}

	internal static IReadOnlyList<Guid> OrphanedVariables(
		IReadOnlyList<VariableEntity> variables,
		IReadOnlyList<ConfigEntryRecord> entries)
	{
		var keys = entries.Select(ReadKey).ToList();
		if (keys.Contains(null))
		{
			return [];
		}

		return
		[
			.. variables
				.Where(variable => variable.DefinitionId is { } definitionId &&
					AdGuardHomeVariables.SplitDefinitionId(definitionId) is { } split &&
					!keys.Contains(split.VariableKey, StringComparer.Ordinal))
				.Select(variable => variable.Id),
		];
	}

	private static string? ReadKey(ConfigEntryRecord entry)
		=> entry.Values.TryGetValue(AdGuardHomeConfigKeys.VariableKey, out var element) &&
			element.ValueKind == JsonValueKind.String &&
			element.GetString() is { Length: > 0 } key
				? key
				: null;
}
