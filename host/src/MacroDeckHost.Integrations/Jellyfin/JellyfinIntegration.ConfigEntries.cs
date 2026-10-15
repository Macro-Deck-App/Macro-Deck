using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Jellyfin.Configuration;
using MacroDeckHost.Integrations.Jellyfin.Connections;
using MacroDeckHost.Integrations.Jellyfin.State;
using MacroDeckHost.Integrations.Jellyfin.Variables;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Jellyfin;

namespace MacroDeckHost.Integrations.Jellyfin;

public sealed partial class JellyfinIntegration
{
	private readonly ConcurrentDictionary<Guid, string> _pendingKeys = new();

	public Task<ConfigEntryPreparationResult> PrepareEntryAsync(
		ConfigEntryPreparation preparation,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(preparation);

		var values = new Dictionary<string, JsonElement>(preparation.Values, StringComparer.Ordinal);

		// An edit dialog carries the values it loaded when it opened; devices learned while it was open
		// must survive the save, or their variable names would be handed out again.
		if (preparation.Existing?.Values.TryGetValue(JellyfinConfigKeys.Devices, out var devices) == true)
		{
			values[JellyfinConfigKeys.Devices] = devices;
		}

		var current = preparation.Existing is null ? null : ReadKey(preparation.Existing);
		var key = current;
		if (current is null || preparation.TitleChanged)
		{
			var occupied = preparation.Siblings
				.Where(sibling => sibling.Id != preparation.EntryId)
				.Select(ReadKey)
				.OfType<string>()
				.Concat(_pendingKeys.Where(pair => pair.Key != preparation.EntryId).Select(pair => pair.Value))
				.ToHashSet(StringComparer.Ordinal);
			key = ConfigurationIdentityAllocator.Allocate(
				JellyfinKeys.Stem(preparation.Title, "server", JellyfinVariables.MaxServerKeyLength - 5),
				occupied,
				preparation.TitleChanged ? current : null,
				_suffixFactory);
		}

		if (key is null)
		{
			return Task.FromResult(ConfigEntryPreparationResult.Refuse(Strings.Config.VariableIdentityExhausted()));
		}

		_pendingKeys[preparation.EntryId] = key;
		values[VariableKeyConfigKey] = JsonSerializer.SerializeToElement(key);
		return Task.FromResult(ConfigEntryPreparationResult.Accept(values,
			JellyfinVariables.DeclareServer(key, preparation.EntryId, preparation.Title)));
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
			JellyfinConnectionStatus.Connecting => ConfigEntryStatus.Connecting,
			JellyfinConnectionStatus.Connected => ConfigEntryStatus.Connected,
			JellyfinConnectionStatus.Reconnecting => ConfigEntryStatus.Reconnecting,
			JellyfinConnectionStatus.AuthenticationFailed => ConfigEntryStatus.NeedsReconfiguration,
			_ => ConfigEntryStatus.Disconnected
		};
	}

	public Task<bool> IsEntryUsableAsync(StoredConfigEntry entry, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entry);
		return Task.FromResult(ReadKey(entry) is not null);
	}

	public async Task<bool> ReloadEntriesAsync(CancellationToken cancellationToken = default)
	{
		if (IsInitialized)
		{
			await ReloadConfigurationsAsync(cancellationToken).ConfigureAwait(false);
		}

		return true;
	}

	public async Task SynchronizeEntriesAsync(
		IReadOnlyList<StoredConfigEntry> entries,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entries);

		if (_context is not { } context)
		{
			return;
		}

		var usable = entries.Where(entry => ReadKey(entry) is not null).ToList();
		var usableIds = usable.Select(entry => entry.Id).ToHashSet();

		foreach (var variable in await context.Variables.GetAllAsync().ConfigureAwait(false))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (JellyfinVariables.TryGetEntryId(variable.DefinitionId, out var entryId) && !usableIds.Contains(entryId))
			{
				await context.Variables.DeleteAsync(variable.Id).ConfigureAwait(false);
			}
		}

		foreach (var entry in usable)
		{
			foreach (var variable in EntryVariables(entry))
			{
				cancellationToken.ThrowIfCancellationRequested();
				await context.Variables.CreateAsync(variable).ConfigureAwait(false);
			}
		}
	}

	public Task ReleasePreparationAsync(Guid entryId, CancellationToken cancellationToken = default)
	{
		_pendingKeys.TryRemove(entryId, out _);
		return Task.CompletedTask;
	}

	private static IEnumerable<VariableDefinition> EntryVariables(StoredConfigEntry entry)
	{
		var key = ReadKey(entry)!;
		var devices = new JellyfinDeviceRegistry();
		devices.Load(ReadString(entry, JellyfinConfigKeys.Devices));
		return JellyfinVariables.DeclareServer(key, entry.Id, entry.Title)
			.Concat(devices.Devices.SelectMany(device =>
				JellyfinVariables.DeclareDevice(key, device.Key, entry.Id, device.LocalId, entry.Title)));
	}

	private static string? ReadKey(StoredConfigEntry entry)
	{
		return ReadString(entry, VariableKeyConfigKey) is { Length: > 0 } key ? key : null;
	}

	private static string? ReadString(StoredConfigEntry entry, string name)
	{
		return entry.Values.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.String
			? element.GetString()
			: null;
	}
}
