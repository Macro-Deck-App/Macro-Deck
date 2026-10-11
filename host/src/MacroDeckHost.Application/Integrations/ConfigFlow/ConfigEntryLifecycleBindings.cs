using System.Collections.Concurrent;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Integrations.ConfigFlow;

internal interface IConfigEntryBinding
{
	Task<IntegrationConfigMutationPreparationResult> PrepareAsync(IntegrationConfigMutationPreparation preparation,
		CancellationToken cancellationToken);

	Task<IntegrationConfigEntryStatus> GetStatusAsync(ConfigEntryRecord entry, CancellationToken cancellationToken);

	Task<bool> IsUsableAsync(ConfigEntryRecord entry, CancellationToken cancellationToken);

	Task<bool> ReloadAsync(CancellationToken cancellationToken);

	Task SynchronizeAsync(IReadOnlyList<ConfigEntryRecord> entries, CancellationToken cancellationToken);

	Task ReleasePreparationAsync(Guid entryId, CancellationToken cancellationToken);
}

internal sealed class AdapterConfigEntryBinding(IIntegrationConfigMutationAdapter adapter) : IConfigEntryBinding
{
	public Task<IntegrationConfigMutationPreparationResult> PrepareAsync(
		IntegrationConfigMutationPreparation preparation,
		CancellationToken cancellationToken)
		=> Task.FromResult(adapter.Prepare(preparation));

	public Task<IntegrationConfigEntryStatus> GetStatusAsync(ConfigEntryRecord entry,
		CancellationToken cancellationToken)
		=> Task.FromResult(adapter.GetStatus(entry));

	public Task<bool> IsUsableAsync(ConfigEntryRecord entry, CancellationToken cancellationToken)
		=> Task.FromResult(adapter.IsUsable(entry));

	public async Task<bool> ReloadAsync(CancellationToken cancellationToken)
	{
		await adapter.ReloadAsync(cancellationToken).ConfigureAwait(false);
		return true;
	}

	public Task SynchronizeAsync(IReadOnlyList<ConfigEntryRecord> entries, CancellationToken cancellationToken)
		=> adapter.SynchronizeVariablesAsync(entries, cancellationToken);

	public Task ReleasePreparationAsync(Guid entryId, CancellationToken cancellationToken)
	{
		adapter.ReleasePreparation(entryId);
		return Task.CompletedTask;
	}
}

internal sealed class LifecycleConfigEntryBinding : IConfigEntryBinding
{
	private readonly string _integrationId;
	private readonly IConfigEntryLifecycle _lifecycle;
	private readonly VariableRegistry? _variables;
	private readonly ConcurrentDictionary<Guid, Guid> _reservations;

	public LifecycleConfigEntryBinding(string integrationId,
		IConfigEntryLifecycle lifecycle,
		VariableRegistry? variables,
		ConcurrentDictionary<Guid, Guid> reservations)
	{
		_integrationId = integrationId;
		_lifecycle = lifecycle;
		_variables = variables;
		_reservations = reservations;
	}

	public async Task<IntegrationConfigMutationPreparationResult> PrepareAsync(
		IntegrationConfigMutationPreparation preparation,
		CancellationToken cancellationToken)
	{
		var result = await _lifecycle.PrepareEntryAsync(new ConfigEntryPreparation
				{
					EntryId = preparation.EntryId,
					Title = preparation.Title,
					Values = preparation.Values,
					Existing = preparation.Existing is null ? null : ToStored(preparation.Existing),
					Siblings = [.. preparation.Siblings.Select(ToStored)],
					TitleChanged = preparation.TitleChanged
				},
				cancellationToken)
			.ConfigureAwait(false);

		var values = result.Values ?? preparation.Values;
		if (!result.Success)
		{
			return new IntegrationConfigMutationPreparationResult(false, values, result.Error);
		}

		if (!TryReserve(preparation.EntryId, result.ReservedVariables))
		{
			return new IntegrationConfigMutationPreparationResult(false,
				values,
				AppStrings.Errors.Config.VariableNamesUnavailable());
		}

		return new IntegrationConfigMutationPreparationResult(true, values);
	}

	public async Task<IntegrationConfigEntryStatus> GetStatusAsync(ConfigEntryRecord entry,
		CancellationToken cancellationToken)
		=> await _lifecycle.GetEntryStatusAsync(ToStored(entry), cancellationToken).ConfigureAwait(false) switch
		{
			ConfigEntryStatus.Connecting => IntegrationConfigEntryStatus.Connecting,
			ConfigEntryStatus.Connected => IntegrationConfigEntryStatus.Connected,
			ConfigEntryStatus.Reconnecting => IntegrationConfigEntryStatus.Reconnecting,
			ConfigEntryStatus.Disconnected => IntegrationConfigEntryStatus.Disconnected,
			ConfigEntryStatus.NeedsReconfiguration => IntegrationConfigEntryStatus.NeedsReconfiguration,
			_ => IntegrationConfigEntryStatus.Ready
		};

	public Task<bool> IsUsableAsync(ConfigEntryRecord entry, CancellationToken cancellationToken)
		=> _lifecycle.IsEntryUsableAsync(ToStored(entry), cancellationToken);

	public Task<bool> ReloadAsync(CancellationToken cancellationToken) => _lifecycle.ReloadEntriesAsync(cancellationToken);

	public Task SynchronizeAsync(IReadOnlyList<ConfigEntryRecord> entries, CancellationToken cancellationToken)
		=> _lifecycle.SynchronizeEntriesAsync([.. entries.Select(ToStored)], cancellationToken);

	public async Task ReleasePreparationAsync(Guid entryId, CancellationToken cancellationToken)
	{
		Release(entryId);
		await _lifecycle.ReleasePreparationAsync(entryId, cancellationToken).ConfigureAwait(false);
	}

	private bool TryReserve(Guid entryId, IReadOnlyList<MacroDeck.Sdk.Variables.VariableDefinition> variables)
	{
		Release(entryId);

		var reservations = variables
			.Where(variable => !string.IsNullOrEmpty(variable.Name) && !string.IsNullOrEmpty(variable.ResolvedId))
			.Select(variable => new VariableRegistry.IntegrationVariableReservation(variable.Name!,
				variable.ResolvedId!,
				SdkVariableTypeMapper.ToDomain(variable.Type)))
			.ToList();

		if (reservations.Count == 0 || _variables is null)
		{
			return true;
		}

		var token = Guid.NewGuid();
		if (!_variables.TryReserveGlobalIntegrationVariables(token, _integrationId, reservations))
		{
			return false;
		}

		_reservations[entryId] = token;
		return true;
	}

	private void Release(Guid entryId)
	{
		if (_reservations.TryRemove(entryId, out var token))
		{
			_variables?.ReleaseIntegrationVariableReservation(token);
		}
	}

	private static StoredConfigEntry ToStored(ConfigEntryRecord entry)
		=> new() { Id = entry.Id, Title = entry.Title, CreatedAt = entry.CreatedAt, Values = entry.Values };
}
