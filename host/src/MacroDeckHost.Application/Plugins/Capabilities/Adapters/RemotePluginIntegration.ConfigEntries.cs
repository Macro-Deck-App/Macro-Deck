using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.ConfigEntries;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters;

public abstract partial class RemotePluginIntegration : IConfigEntryLifecycle
{
	private bool DeclaresConfigEntries => Snapshot.AcceptedKinds.Contains(CapabilityKinds.ConfigEntries);

	public async Task<ConfigEntryPreparationResult> PrepareEntryAsync(
		ConfigEntryPreparation preparation,
		CancellationToken cancellationToken = default)
	{
		var result = await InvokeConfigEntriesAsync<ConfigEntryPrepareResult>(
				CapabilityOperations.ConfigEntries.Prepare,
				new ConfigEntryPrepareArguments
				{
					EntryId = preparation.EntryId,
					Title = preparation.Title,
					Values = preparation.Values,
					Existing = preparation.Existing is null ? null : ToDto(preparation.Existing),
					Siblings = [.. preparation.Siblings.Select(ToDto)],
					TitleChanged = preparation.TitleChanged,
					RejectedVariableNames = preparation.RejectedVariableNames.Count == 0
						? null
						: preparation.RejectedVariableNames
				},
				cancellationToken)
			.ConfigureAwait(false);

		if (result is null)
		{
			return ConfigEntryPreparationResult.Accept();
		}

		return result.Success
			? ConfigEntryPreparationResult.Accept(result.Values,
				[
					.. result.ReservedVariables
						.Where(variable => Enum.TryParse<VariableType>(variable.Type, out _))
						.Select(variable
							=> VariableDefinition.Eager(variable.Name, Enum.Parse<VariableType>(variable.Type)) with
							{
								Id = variable.DefinitionId
							})
				]) with
				{
					RetryOnConflict = result.RetryOnConflict == true
				}
			: ConfigEntryPreparationResult.Refuse(result.Error ?? default);
	}

	public async Task<ConfigEntryStatus> GetEntryStatusAsync(
		StoredConfigEntry entry,
		CancellationToken cancellationToken = default)
	{
		var result = await InvokeConfigEntriesAsync<ConfigEntryStatusResult>(CapabilityOperations.ConfigEntries.Status,
				new ConfigEntryArguments { Entry = ToDto(entry) },
				cancellationToken)
			.ConfigureAwait(false);

		return result is not null && Enum.TryParse<ConfigEntryStatus>(result.Status, out var status)
			? status
			: ConfigEntryStatus.Ready;
	}

	public async Task<bool> IsEntryUsableAsync(StoredConfigEntry entry, CancellationToken cancellationToken = default)
	{
		var result = await InvokeConfigEntriesAsync<ConfigEntryUsableResult>(CapabilityOperations.ConfigEntries.Usable,
				new ConfigEntryArguments { Entry = ToDto(entry) },
				cancellationToken)
			.ConfigureAwait(false);

		return result?.Usable ?? true;
	}

	public async Task<bool> ReloadEntriesAsync(CancellationToken cancellationToken = default)
	{
		var result = await InvokeConfigEntriesAsync<ConfigEntryReloadResult>(CapabilityOperations.ConfigEntries.Reload,
				null,
				cancellationToken)
			.ConfigureAwait(false);

		return result?.Reloaded ?? false;
	}

	public Task SynchronizeEntriesAsync(
		IReadOnlyList<StoredConfigEntry> entries,
		CancellationToken cancellationToken = default)
	{
		return InvokeConfigEntriesAsync<JsonElement?>(CapabilityOperations.ConfigEntries.Synchronize,
			new ConfigEntrySynchronizeArguments { Entries = [.. entries.Select(ToDto)] },
			cancellationToken);
	}

	public Task ReleasePreparationAsync(Guid entryId, CancellationToken cancellationToken = default)
	{
		return InvokeConfigEntriesAsync<JsonElement?>(CapabilityOperations.ConfigEntries.Release,
			new ConfigEntryReleaseArguments { EntryId = entryId },
			cancellationToken);
	}

	private async Task<T?> InvokeConfigEntriesAsync<T>(
		string operation,
		object? arguments,
		CancellationToken cancellationToken)
	{
		if (!DeclaresConfigEntries)
		{
			return default;
		}

		try
		{
			var data = await _invoker.InvokeAsync(Id,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.ConfigEntries,
						LocalId = ProviderCapabilityId.LocalId,
						Operation = operation,
						Arguments = arguments
					},
					cancellationToken)
				.ConfigureAwait(false);

			return data is { } element ? element.Deserialize<T>(PluginProtocolJson.Options) : default;
		}
		catch (Exception exception) when (exception is RemoteCapabilityException or JsonException)
		{
			return default;
		}
	}

	private static ConfigEntryDto ToDto(StoredConfigEntry entry)
	{
		return new ConfigEntryDto
			{ Id = entry.Id, Title = entry.Title, CreatedAt = entry.CreatedAt, Values = entry.Values };
	}
}
