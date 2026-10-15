using System.Text.Json;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Obs.Configuration;
using MacroDeckHost.Integrations.Obs.Connections;
using MacroDeckHost.Integrations.Obs.Variables;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Obs;

namespace MacroDeckHost.Integrations.Obs;

public sealed partial class ObsIntegration
{
	public Task<ConfigEntryPreparationResult> PrepareEntryAsync(
		ConfigEntryPreparation preparation,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(preparation);

		var key = ConfigurationIdentityPreparation.ChooseKey(preparation,
			ReadIdentity,
			candidate => ObsVariables.Declare(candidate, preparation.EntryId),
			_suffixFactory);
		if (key is null)
		{
			return Task.FromResult(ConfigEntryPreparationResult.Refuse(Strings.Config.VariableIdentityExhausted()));
		}

		var values = new Dictionary<string, JsonElement>(preparation.Values, StringComparer.Ordinal)
		{
			[ObsConfigKeys.ConfigurationName] = JsonSerializer.SerializeToElement(preparation.Title),
			[ObsConfigurationMetadata.VariableIdentityKey] = JsonSerializer.SerializeToElement(
				ConfigurationIdentityJson.Serialize(new ConfigurationIdentity(preparation.Title, key))),
			[ObsConfigurationMetadata.SchemaKey]
				= JsonSerializer.SerializeToElement(ObsConfigurationMetadata.SchemaVersion)
		};

		return Task.FromResult(ConfigEntryPreparationResult.Accept(values,
				ObsVariables.Declare(key, preparation.EntryId, preparation.Title)) with
			{
				RetryOnConflict = true
			});
	}

	public async Task<ConfigEntryStatus> GetEntryStatusAsync(
		StoredConfigEntry entry,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entry);

		if (!await IsEntryUsableAsync(entry, cancellationToken).ConfigureAwait(false))
		{
			return ConfigEntryStatus.NeedsReconfiguration;
		}

		if (!TryGetStatus(entry.Id, out var status))
		{
			return ConfigEntryStatus.Disconnected;
		}

		return status switch
		{
			ObsConnectionStatus.Connecting => ConfigEntryStatus.Connecting,
			ObsConnectionStatus.Connected => ConfigEntryStatus.Connected,
			ObsConnectionStatus.Reconnecting => ConfigEntryStatus.Reconnecting,
			_ => ConfigEntryStatus.Disconnected
		};
	}

	public Task<bool> IsEntryUsableAsync(StoredConfigEntry entry, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entry);
		return Task.FromResult(IsUsable(entry));
	}

	public async Task<bool> ReloadEntriesAsync(CancellationToken cancellationToken = default)
	{
		if (IsInitialized)
		{
			await ReloadConfigurationsAsync(cancellationToken).ConfigureAwait(false);
		}

		return true;
	}

	public Task SynchronizeEntriesAsync(
		IReadOnlyList<StoredConfigEntry> entries,
		CancellationToken cancellationToken = default)
	{
		return _context is { } context
			? SynchronizeEntriesAsync(entries, context.Variables, cancellationToken)
			: Task.CompletedTask;
	}

	public Task SynchronizeEntriesAsync(
		IReadOnlyList<StoredConfigEntry> entries,
		IVariableApi variables,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(entries);
		ArgumentNullException.ThrowIfNull(variables);

		return ConfigurationIdentityPreparation.SynchronizeAsync(variables,
			[.. entries.Where(IsUsable).Select(entry => (entry, ReadIdentity(entry)!))],
			definitionId => ObsVariables.TryGetConfigurationEntryId(definitionId, out var entryId) ? entryId : null,
			(key, entryId, title) => ObsVariables.Declare(key, entryId, title),
			cancellationToken);
	}

	private static bool IsUsable(StoredConfigEntry entry)
	{
		return ConfigurationIdentityPreparation.HasSchema(entry,
				ObsConfigurationMetadata.SchemaKey,
				ObsConfigurationMetadata.SchemaVersion)
			&& ReadIdentity(entry) is not null;
	}

	private static ConfigurationIdentity? ReadIdentity(StoredConfigEntry entry)
	{
		return ConfigurationIdentityPreparation.ReadIdentity(entry, ObsConfigurationMetadata.VariableIdentityKey);
	}
}
