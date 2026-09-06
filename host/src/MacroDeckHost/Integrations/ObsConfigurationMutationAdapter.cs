using System.Text.Json;
using System.Collections.Concurrent;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Integrations.Obs;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations;

internal sealed class ObsConfigurationMutationAdapter : IIntegrationConfigMutationAdapter
{
	private readonly IIntegrationRegistry _registry;
	private readonly VariableRegistry _variableRegistry;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly Func<int>? _suffixFactory;
	private readonly ConcurrentDictionary<Guid, Guid> _reservations = new();

	public ObsConfigurationMutationAdapter(
		IIntegrationRegistry registry,
		VariableRegistry variableRegistry,
		IServiceScopeFactory scopeFactory)
		: this(registry, variableRegistry, scopeFactory, null)
	{
	}

	internal ObsConfigurationMutationAdapter(
		IIntegrationRegistry registry,
		VariableRegistry variableRegistry,
		IServiceScopeFactory scopeFactory,
		Func<int>? suffixFactory)
	{
		_registry = registry;
		_variableRegistry = variableRegistry;
		_scopeFactory = scopeFactory;
		_suffixFactory = suffixFactory;
	}

	public string IntegrationId => ObsIntegration.IntegrationId;

	public IntegrationConfigMutationPreparationResult Prepare(IntegrationConfigMutationPreparation preparation)
	{
		var values = new Dictionary<string, JsonElement>(preparation.Values);
		values[ObsConfigKeys.ConfigurationName] = JsonSerializer.SerializeToElement(preparation.Title);
		var occupied = preparation.Siblings
			.Select(ReadIdentity)
			.Where(identity => identity is not null)
			.Select(identity => identity!.Key)
			.ToHashSet(StringComparer.Ordinal);

		var current = preparation.Existing is null ? null : ReadIdentity(preparation.Existing);
		var identity = current;
		if (current is null || preparation.TitleChanged)
		{
			identity = null;
			for (var attempt = 0; attempt < ObsConfigurationIdentityAllocator.MaximumAttempts; attempt++)
			{
				var candidate = ObsConfigurationIdentityAllocator.Allocate(preparation.Title,
					occupied,
					preparation.TitleChanged ? current?.Key : null,
					_suffixFactory);
				if (candidate is null)
				{
					break;
				}

				if (TryReserve(preparation.EntryId, candidate.Key))
				{
					identity = candidate;
					break;
				}

				occupied.Add(candidate.Key);
			}
		}
		else if (!TryReserve(preparation.EntryId, current.Key))
		{
			identity = null;
		}

		if (identity is null)
		{
			return new IntegrationConfigMutationPreparationResult(false,
				values,
				AppStrings.Integrations.Obs.Config.VariableIdentityExhausted());
		}

		values[ObsConfigurationMetadata.VariableIdentityKey] =
			JsonSerializer.SerializeToElement(ObsConfigurationMetadata.SerializeIdentity(identity));

		values[ObsConfigurationMetadata.SchemaKey] =
			JsonSerializer.SerializeToElement(ObsConfigurationMetadata.SchemaVersion);
		return new IntegrationConfigMutationPreparationResult(true, values);
	}

	public IntegrationConfigEntryStatus GetStatus(ConfigEntryRecord entry)
	{
		if (!IsUsable(entry))
		{
			return IntegrationConfigEntryStatus.NeedsReconfiguration;
		}

		var integration = _registry.Integrations.OfType<ObsIntegration>().FirstOrDefault();
		if (integration is null || !integration.TryGetStatus(entry.Id, out var status))
		{
			return IntegrationConfigEntryStatus.Disconnected;
		}

		return status switch
		{
			ObsConnectionStatus.Connecting => IntegrationConfigEntryStatus.Connecting,
			ObsConnectionStatus.Connected => IntegrationConfigEntryStatus.Connected,
			ObsConnectionStatus.Reconnecting => IntegrationConfigEntryStatus.Reconnecting,
			_ => IntegrationConfigEntryStatus.Disconnected
		};
	}

	public bool IsUsable(ConfigEntryRecord entry)
		=> entry.Values.TryGetValue(ObsConfigurationMetadata.SchemaKey, out var schema) &&
			string.Equals(ReadString(schema), ObsConfigurationMetadata.SchemaVersion, StringComparison.Ordinal) &&
			ReadIdentity(entry) is not null;

	public Task ReloadAsync(CancellationToken cancellationToken)
	{
		var integration = _registry.Integrations.OfType<ObsIntegration>().FirstOrDefault();
		return integration is { IsInitialized: true }
			? integration.ReloadConfigurationsAsync(cancellationToken)
			: Task.CompletedTask;
	}

	public async Task SynchronizeVariablesAsync(
		IReadOnlyList<ConfigEntryRecord> entries,
		CancellationToken cancellationToken)
	{
		var usable = entries
			.Where(IsUsable)
			.Select(entry => (Entry: entry, Identity: ReadIdentity(entry)!))
			.ToList();
		var usableIds = usable.Select(item => item.Entry.Id).ToHashSet();

		await using var scope = _scopeFactory.CreateAsyncScope();
		var service = scope.ServiceProvider.GetRequiredService<IVariableService>();
		foreach (var variable in await service.GetByOwnerIntegration(IntegrationId))
		{
			if (ObsVariables.TryGetConfigurationEntryId(variable.DefinitionId, out var entryId) &&
				!usableIds.Contains(entryId))
			{
				await service.DeleteIntegrationVariable(IntegrationId, variable.Id);
			}
		}

		foreach (var (entry, identity) in usable)
		{
			foreach (var declared in ObsVariables.Declare(identity.Key, entry.Id, entry.Title))
			{
				var result = await service.CreateIntegrationVariable(IntegrationId,
					declared.Name!,
					VariableScope.Global,
					null,
					SdkVariableTypeMapper.ToDomain(declared.Type),
					null,
					declared.DecimalPlaces,
					declared.Id,
					VariableDeclarationFactory.From(declared));
				if (!result.Success)
				{
					throw new InvalidOperationException(
						$"Could not synchronize OBS variable '{declared.Name}': {result.Error}");
				}
			}
		}
	}

	public void ReleasePreparation(Guid entryId)
	{
		if (_reservations.TryRemove(entryId, out var token))
		{
			_variableRegistry.ReleaseIntegrationVariableReservation(token);
		}
	}

	private bool TryReserve(Guid entryId, string key)
	{
		ReleasePreparation(entryId);
		var token = Guid.NewGuid();
		var variables = ObsVariables.Declare(key, entryId)
			.Select(variable => new VariableRegistry.IntegrationVariableReservation(variable.Name!,
				variable.Id!,
				SdkVariableTypeMapper.ToDomain(variable.Type)))
			.ToList();
		if (!_variableRegistry.TryReserveGlobalIntegrationVariables(token, IntegrationId, variables))
		{
			return false;
		}

		_reservations[entryId] = token;
		return true;
	}

	private static ObsConfigurationIdentity? ReadIdentity(ConfigEntryRecord entry)
		=> entry.Values.TryGetValue(ObsConfigurationMetadata.VariableIdentityKey, out var identity) &&
			ObsConfigurationMetadata.TryParseIdentity(ReadString(identity), out var parsed)
				? parsed
				: null;

	private static string? ReadString(JsonElement element)
		=> element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
}
