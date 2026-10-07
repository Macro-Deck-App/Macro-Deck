using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Integrations.Jellyfin;
using MacroDeckHost.Integrations.Obs;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations;

internal sealed class JellyfinConfigurationMutationAdapter : IIntegrationConfigMutationAdapter
{
	private readonly IIntegrationRegistry _registry;
	private readonly VariableRegistry _variableRegistry;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly Func<int>? _suffixFactory;
	private readonly ConcurrentDictionary<Guid, Guid> _reservations = new();

	public JellyfinConfigurationMutationAdapter(
		IIntegrationRegistry registry,
		VariableRegistry variableRegistry,
		IServiceScopeFactory scopeFactory)
		: this(registry, variableRegistry, scopeFactory, null)
	{
	}

	internal JellyfinConfigurationMutationAdapter(
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

	public string IntegrationId => JellyfinIntegration.IntegrationId;

	public IntegrationConfigMutationPreparationResult Prepare(IntegrationConfigMutationPreparation preparation)
	{
		var values = new Dictionary<string, JsonElement>(preparation.Values);

		// An edit dialog carries the values it loaded when it opened; devices learned while it was open
		// must survive the save, or their variable names would be handed out again.
		if (preparation.Existing?.Values.TryGetValue(JellyfinConfigKeys.Devices, out var devices) == true)
		{
			values[JellyfinConfigKeys.Devices] = devices;
		}

		var occupied = preparation.Siblings
			.Select(ReadKey)
			.Where(key => key is not null)
			.Select(key => key!)
			.ToHashSet(StringComparer.Ordinal);

		var current = preparation.Existing is null ? null : ReadKey(preparation.Existing);
		string? key = current;
		if (current is null || preparation.TitleChanged)
		{
			key = null;
			for (var attempt = 0; attempt < ObsConfigurationIdentityAllocator.MaximumAttempts; attempt++)
			{
				var candidate = ObsConfigurationIdentityAllocator.Allocate(
					JellyfinKeys.Stem(preparation.Title, "server", JellyfinVariables.MaxServerKeyLength - 5),
					occupied,
					preparation.TitleChanged ? current : null,
					_suffixFactory);
				if (candidate is null)
				{
					break;
				}

				if (TryReserve(preparation.EntryId, candidate.Key))
				{
					key = candidate.Key;
					break;
				}

				occupied.Add(candidate.Key);
			}
		}
		else if (!TryReserve(preparation.EntryId, current))
		{
			key = null;
		}

		if (key is null)
		{
			return new IntegrationConfigMutationPreparationResult(false,
				values,
				AppStrings.Integrations.Jellyfin.Config.VariableIdentityExhausted());
		}

		values[JellyfinIntegration.VariableKeyConfigKey] = JsonSerializer.SerializeToElement(key);
		return new IntegrationConfigMutationPreparationResult(true, values);
	}

	public IntegrationConfigEntryStatus GetStatus(ConfigEntryRecord entry)
	{
		if (!IsUsable(entry))
		{
			return IntegrationConfigEntryStatus.NeedsReconfiguration;
		}

		var integration = _registry.Integrations.OfType<JellyfinIntegration>().FirstOrDefault();
		if (integration is null || !integration.TryGetStatus(entry.Id, out var status))
		{
			return IntegrationConfigEntryStatus.Disconnected;
		}

		return status switch
		{
			JellyfinConnectionStatus.Connecting => IntegrationConfigEntryStatus.Connecting,
			JellyfinConnectionStatus.Connected => IntegrationConfigEntryStatus.Connected,
			JellyfinConnectionStatus.Reconnecting => IntegrationConfigEntryStatus.Reconnecting,
			JellyfinConnectionStatus.AuthenticationFailed => IntegrationConfigEntryStatus.NeedsReconfiguration,
			_ => IntegrationConfigEntryStatus.Disconnected
		};
	}

	public bool IsUsable(ConfigEntryRecord entry) => ReadKey(entry) is not null;

	public Task ReloadAsync(CancellationToken cancellationToken)
	{
		var integration = _registry.Integrations.OfType<JellyfinIntegration>().FirstOrDefault();
		return integration is { IsInitialized: true }
			? integration.ReloadConfigurationsAsync(cancellationToken)
			: Task.CompletedTask;
	}

	public async Task SynchronizeVariablesAsync(
		IReadOnlyList<ConfigEntryRecord> entries,
		CancellationToken cancellationToken)
	{
		var usable = entries.Where(IsUsable).ToList();
		var usableIds = usable.Select(entry => entry.Id).ToHashSet();

		await using var scope = _scopeFactory.CreateAsyncScope();
		var service = scope.ServiceProvider.GetRequiredService<IVariableService>();
		foreach (var variable in await service.GetByOwnerIntegration(IntegrationId))
		{
			if (JellyfinVariables.TryGetEntryId(variable.DefinitionId, out var entryId) && !usableIds.Contains(entryId))
			{
				await service.DeleteIntegrationVariable(IntegrationId, variable.Id);
			}
		}

		foreach (var entry in usable)
		{
			var declared = JellyfinVariables.DeclareServer(ReadKey(entry)!, entry.Id, entry.Title)
				.Concat(DeclaredDevices(entry));
			foreach (var variable in declared)
			{
				var result = await service.CreateIntegrationVariable(IntegrationId,
					variable.Name!,
					VariableScope.Global,
					null,
					SdkVariableTypeMapper.ToDomain(variable.Type),
					null,
					variable.DecimalPlaces,
					variable.Id,
					VariableDeclarationFactory.From(variable));
				if (!result.Success)
				{
					throw new InvalidOperationException(
						$"Could not synchronize Jellyfin variable '{variable.Name}': {result.Error}");
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

	private static IEnumerable<MacroDeck.Sdk.Variables.VariableDefinition> DeclaredDevices(ConfigEntryRecord entry)
	{
		var registry = new JellyfinDeviceRegistry();
		registry.Load(ReadString(entry, JellyfinConfigKeys.Devices));
		var key = ReadKey(entry)!;
		return registry.Devices.SelectMany(device =>
			JellyfinVariables.DeclareDevice(key, device.Key, entry.Id, device.LocalId, entry.Title));
	}

	private bool TryReserve(Guid entryId, string key)
	{
		ReleasePreparation(entryId);
		var token = Guid.NewGuid();
		var variables = JellyfinVariables.DeclareServer(key, entryId, default)
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

	private static string? ReadKey(ConfigEntryRecord entry)
		=> ReadString(entry, JellyfinIntegration.VariableKeyConfigKey) is { Length: > 0 } key ? key : null;

	private static string? ReadString(ConfigEntryRecord entry, string name)
		=> entry.Values.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String
			? element.GetString()
			: null;
}
