using System.Text.Json;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Companion.Configuration;
using MacroDeckHost.Integrations.Companion.Variables;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Companion;

namespace MacroDeckHost.Integrations.Companion;

public sealed partial class CompanionIntegration
{
	public Task<ConfigEntryPreparationResult> PrepareEntryAsync(
		ConfigEntryPreparation preparation,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(preparation);

		var key = ConfigurationIdentityPreparation.ChooseKey(preparation,
			ReadIdentity,
			candidate => CompanionVariables.Declare(candidate, preparation.EntryId),
			_suffixFactory);
		if (key is null)
		{
			return Task.FromResult(ConfigEntryPreparationResult.Refuse(Strings.Config.VariableIdentityExhausted()));
		}

		var values = new Dictionary<string, JsonElement>(preparation.Values, StringComparer.Ordinal)
		{
			[CompanionConfigurationMetadata.VariableIdentityKey] = JsonSerializer.SerializeToElement(
				ConfigurationIdentityJson.Serialize(new ConfigurationIdentity(preparation.Title, key))),
			[CompanionConfigurationMetadata.SchemaKey] =
				JsonSerializer.SerializeToElement(CompanionConfigurationMetadata.SchemaVersion)
		};

		return Task.FromResult(ConfigEntryPreparationResult.Accept(values,
				CompanionVariables.Declare(key, preparation.EntryId, preparation.Title)) with
			{
				RetryOnConflict = true
			});
	}

	public Task<ConfigEntryStatus> GetEntryStatusAsync(
		StoredConfigEntry entry,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entry);

		if (!IsUsable(entry))
		{
			return Task.FromResult(ConfigEntryStatus.NeedsReconfiguration);
		}

		return Task.FromResult(IsConnected(entry.Id) ? ConfigEntryStatus.Connected : ConfigEntryStatus.Disconnected);
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
			definitionId
				=> CompanionVariables.TryGetConfigurationEntryId(definitionId, out var entryId) ? entryId : null,
			(key, entryId, title) => CompanionVariables.Declare(key, entryId, title),
			cancellationToken);
	}

	private static bool IsUsable(StoredConfigEntry entry)
	{
		return ConfigurationIdentityPreparation.HasSchema(entry,
				CompanionConfigurationMetadata.SchemaKey,
				CompanionConfigurationMetadata.SchemaVersion)
			&& ReadIdentity(entry) is not null;
	}

	private static ConfigurationIdentity? ReadIdentity(StoredConfigEntry entry)
	{
		return ConfigurationIdentityPreparation.ReadIdentity(entry, CompanionConfigurationMetadata.VariableIdentityKey);
	}
}
