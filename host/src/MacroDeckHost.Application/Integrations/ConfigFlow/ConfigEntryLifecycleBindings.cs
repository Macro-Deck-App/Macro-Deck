using System.Collections.Concurrent;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Integrations.ConfigFlow;

internal sealed class LifecycleConfigEntryBinding
{
	internal const int MaximumAttempts = 32;

	private readonly string _integrationId;
	private readonly IConfigEntryLifecycle _lifecycle;
	private readonly VariableRegistry? _variables;
	private readonly ConcurrentDictionary<Guid, Guid> _reservations;

	public LifecycleConfigEntryBinding(
		string integrationId,
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
		var rejected = new List<string>();
		for (var attempt = 0; attempt < MaximumAttempts; attempt++)
		{
			var result = await _lifecycle.PrepareEntryAsync(new ConfigEntryPreparation
					{
						EntryId = preparation.EntryId,
						Title = preparation.Title,
						Values = preparation.Values,
						Existing = preparation.Existing is null ? null : ToStored(preparation.Existing),
						Siblings = [.. preparation.Siblings.Select(ToStored)],
						TitleChanged = preparation.TitleChanged,
						RejectedVariableNames = [.. rejected]
					},
					cancellationToken)
				.ConfigureAwait(false);

			var values = result.Values ?? preparation.Values;
			if (!result.Success)
			{
				return new IntegrationConfigMutationPreparationResult(false, values, result.Error);
			}

			if (TryReserve(preparation.EntryId, result.ReservedVariables, out var conflicts))
			{
				return new IntegrationConfigMutationPreparationResult(true, values);
			}

			var added = conflicts.Where(name => !rejected.Contains(name, StringComparer.Ordinal)).ToList();
			if (!result.RetryOnConflict || added.Count == 0)
			{
				break;
			}

			rejected.AddRange(added);
		}

		return new IntegrationConfigMutationPreparationResult(false,
			preparation.Values,
			AppStrings.Errors.Config.VariableNamesUnavailable());
	}

	public async Task<IntegrationConfigEntryStatus> GetStatusAsync(
		ConfigEntryRecord entry,
		CancellationToken cancellationToken)
	{
		return await _lifecycle.GetEntryStatusAsync(ToStored(entry), cancellationToken).ConfigureAwait(false) switch
		{
			ConfigEntryStatus.Connecting => IntegrationConfigEntryStatus.Connecting,
			ConfigEntryStatus.Connected => IntegrationConfigEntryStatus.Connected,
			ConfigEntryStatus.Reconnecting => IntegrationConfigEntryStatus.Reconnecting,
			ConfigEntryStatus.Disconnected => IntegrationConfigEntryStatus.Disconnected,
			ConfigEntryStatus.NeedsReconfiguration => IntegrationConfigEntryStatus.NeedsReconfiguration,
			_ => IntegrationConfigEntryStatus.Ready
		};
	}

	public Task<bool> IsUsableAsync(ConfigEntryRecord entry, CancellationToken cancellationToken)
	{
		return _lifecycle.IsEntryUsableAsync(ToStored(entry), cancellationToken);
	}

	public Task<bool> ReloadAsync(CancellationToken cancellationToken)
	{
		return _lifecycle.ReloadEntriesAsync(cancellationToken);
	}

	public Task SynchronizeAsync(
		IReadOnlyList<ConfigEntryRecord> entries,
		IVariableApi? variables,
		CancellationToken cancellationToken)
	{
		return variables is null
			? _lifecycle.SynchronizeEntriesAsync([.. entries.Select(ToStored)], cancellationToken)
			: _lifecycle.SynchronizeEntriesAsync([.. entries.Select(ToStored)], variables, cancellationToken);
	}

	public async Task ReleasePreparationAsync(Guid entryId, CancellationToken cancellationToken)
	{
		Release(entryId);
		await _lifecycle.ReleasePreparationAsync(entryId, cancellationToken).ConfigureAwait(false);
	}

	private bool TryReserve(
		Guid entryId,
		IReadOnlyList<VariableDefinition> variables,
		out IReadOnlyList<string> conflicts)
	{
		Release(entryId);
		conflicts = [];

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
		if (!_variables.TryReserveGlobalIntegrationVariables(token, _integrationId, reservations, out conflicts))
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
	{
		return new StoredConfigEntry
			{ Id = entry.Id, Title = entry.Title, CreatedAt = entry.CreatedAt, Values = entry.Values };
	}
}
