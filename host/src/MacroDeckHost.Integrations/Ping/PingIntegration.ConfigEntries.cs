using System.Text.Json;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Ping.Configuration;
using MacroDeckHost.Integrations.Ping.State;
using MacroDeckHost.Integrations.Ping.Variables;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Ping;

namespace MacroDeckHost.Integrations.Ping;

public sealed partial class PingIntegration
{
	public Task<ConfigEntryPreparationResult> PrepareEntryAsync(
		ConfigEntryPreparation preparation,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(preparation);

		var key = ConfigurationIdentityPreparation.ChooseKey(preparation,
			ReadIdentity,
			candidate => PingVariables.Declare(candidate, preparation.EntryId),
			null);
		if (key is null)
		{
			return Task.FromResult(ConfigEntryPreparationResult.Refuse(Strings.Config.VariableIdentityExhausted()));
		}

		var values = new Dictionary<string, JsonElement>(preparation.Values, StringComparer.Ordinal)
		{
			[PingConfigKeys.VariableIdentityKey] = JsonSerializer.SerializeToElement(
				ConfigurationIdentityJson.Serialize(new ConfigurationIdentity(preparation.Title, key)))
		};

		return Task.FromResult(ConfigEntryPreparationResult.Accept(values,
				PingVariables.Declare(key, preparation.EntryId, preparation.Title)) with
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

		return _hub.Find(entry.Id.ToString("D"))?.Reachability switch
		{
			PingReachability.Online => ConfigEntryStatus.Connected,
			PingReachability.Unknown => ConfigEntryStatus.Connecting,
			_ => ConfigEntryStatus.Disconnected
		};
	}

	public Task<bool> IsEntryUsableAsync(StoredConfigEntry entry, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entry);
		return Task.FromResult(ReadIdentity(entry) is not null);
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

	internal static Task SynchronizeEntriesAsync(
		IReadOnlyList<StoredConfigEntry> entries,
		IVariableApi variables,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(entries);
		ArgumentNullException.ThrowIfNull(variables);

		return ConfigurationIdentityPreparation.SynchronizeAsync(variables,
			[
				.. entries.Select(entry => (Entry: entry, Identity: ReadIdentity(entry)))
					.Where(item => item.Identity is not null)
					.Select(item => (item.Entry, item.Identity!))
			],
			definitionId => PingVariables.TrySplit(definitionId, out var entryId, out _) ? entryId : null,
			(key, entryId, title) => PingVariables.Declare(key, entryId, title),
			cancellationToken);
	}

	private static ConfigurationIdentity? ReadIdentity(StoredConfigEntry entry)
	{
		return ConfigurationIdentityPreparation.ReadIdentity(entry, PingConfigKeys.VariableIdentityKey);
	}
}
