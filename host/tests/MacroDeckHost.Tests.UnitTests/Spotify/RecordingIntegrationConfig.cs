using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

internal sealed class RecordingIntegrationConfig : IIntegrationConfig
{
	private readonly object _lock = new();

	public Dictionary<string, string?> Strings { get; } = new(StringComparer.Ordinal);
	public Dictionary<string, string> Secrets { get; } = new(StringComparer.Ordinal);
	public List<string> WriteOrder { get; } = [];
	public List<string> WriteValues { get; } = [];

	public Func<string, Task>? BeforeSecretWrite { get; init; }

	public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>([]);

	public Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			return Task.FromResult(Strings.GetValueOrDefault(key));
		}
	}

	public Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			return Task.FromResult<string?>(Secrets.GetValueOrDefault(key));
		}
	}

	public Task SetStringAsync(
		Guid entryId,
		string key,
		string? value,
		CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			WriteOrder.Add(key);
			Strings[key] = value;
		}

		return Task.CompletedTask;
	}

	public async Task SetSecretAsync(
		Guid entryId,
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
			WriteValues.Add(value);
			Secrets[key] = value;
		}
	}
}
