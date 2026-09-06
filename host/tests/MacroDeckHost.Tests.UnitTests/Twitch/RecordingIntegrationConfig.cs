using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Tests.UnitTests.Twitch;

internal sealed class RecordingIntegrationConfig : IIntegrationConfig
{
	private readonly Lock _lock = new();

	public List<ConfigEntrySnapshot> Entries { get; } = [];

	public Dictionary<(Guid EntryId, string Key), string?> Strings { get; } = [];

	public Dictionary<(Guid EntryId, string Key), string> Secrets { get; } = [];

	public List<string> WriteOrder { get; } = [];

	public Func<string, Task>? BeforeSecretWrite { get; init; }

	public Guid AddEntry(string title,
		IReadOnlyDictionary<string, string?> values,
		IReadOnlyDictionary<string, string>? secrets = null)
	{
		var entryId = Guid.NewGuid();
		Entries.Add(new ConfigEntrySnapshot(entryId, title));

		foreach (var (key, value) in values)
		{
			Strings[(entryId, key)] = value;
		}

		foreach (var (key, value) in secrets ?? new Dictionary<string, string>(StringComparer.Ordinal))
		{
			Secrets[(entryId, key)] = value;
		}

		return entryId;
	}

	public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>(Entries.ToList());

	public Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			return Task.FromResult(Strings.GetValueOrDefault((entryId, key)));
		}
	}

	public Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			return Task.FromResult<string?>(Secrets.GetValueOrDefault((entryId, key)));
		}
	}

	public Task SetStringAsync(Guid entryId, string key, string? value, CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			WriteOrder.Add(key);
			Strings[(entryId, key)] = value;
		}

		return Task.CompletedTask;
	}

	public async Task SetSecretAsync(Guid entryId,
		string key,
		string value,
		CancellationToken cancellationToken = default)
	{
		if (BeforeSecretWrite is not null)
		{
			await BeforeSecretWrite(key);
		}

		lock (_lock)
		{
			WriteOrder.Add(key);
			Secrets[(entryId, key)] = value;
		}
	}
}
