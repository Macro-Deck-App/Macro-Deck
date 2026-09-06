using System.Globalization;
using MacroDeckHost.Integrations.Spotify;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

internal sealed class SpotifyConfigStub : IIntegrationConfig
{
	private readonly Lock _sync = new();

	private readonly Dictionary<string, string?> _strings = new(StringComparer.Ordinal)
	{
		[SpotifyConfigKeys.ClientId] = "client-id",
		[SpotifyConfigKeys.ExpiresAt] = DateTime.UtcNow.AddHours(1).ToString("o", CultureInfo.InvariantCulture),
		[SpotifyConfigKeys.Scope] = string.Join(' ', SpotifyScopes.Required)
	};

	private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal)
	{
		[SpotifyConfigKeys.ClientSecret] = "client-secret",
		[SpotifyConfigKeys.AccessToken] = "access-token",
		[SpotifyConfigKeys.RefreshToken] = "refresh-token"
	};

	public Guid EntryId { get; init; } = Guid.Parse("22222222-2222-2222-2222-222222222222");

	public Func<Task>? BeforeGetEntries { get; init; }

	public Func<string, Task>? BeforeSecretWrite { get; init; }

	public IReadOnlyDictionary<string, string?> Strings => Snapshot(_strings);

	public IReadOnlyDictionary<string, string> Secrets => Snapshot(_secrets);

	public SpotifyConfigStub WithExpiredAccessToken()
	{
		_strings[SpotifyConfigKeys.ExpiresAt] =
			DateTime.UtcNow.AddHours(-1).ToString("o", CultureInfo.InvariantCulture);
		return this;
	}

	public SpotifyConfigStub WithOutdatedScopes()
	{
		_strings[SpotifyConfigKeys.Scope] = "user-read-playback-state user-read-currently-playing";
		return this;
	}

	public SpotifyConfigStub WithoutStoredScope()
	{
		_strings[SpotifyConfigKeys.Scope] = null;
		return this;
	}

	public async Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(
		CancellationToken cancellationToken = default)
	{
		if (BeforeGetEntries is not null)
		{
			await BeforeGetEntries();
		}

		return [new(EntryId, "Spotify")];
	}

	public Task<string?> GetStringAsync(
		Guid entryId,
		string key,
		CancellationToken cancellationToken = default)
	{
		lock (_sync)
		{
			return Task.FromResult(_strings.GetValueOrDefault(key));
		}
	}

	public Task<string?> GetSecretAsync(
		Guid entryId,
		string key,
		CancellationToken cancellationToken = default)
	{
		lock (_sync)
		{
			return Task.FromResult<string?>(_secrets.GetValueOrDefault(key));
		}
	}

	public Task SetStringAsync(
		Guid entryId,
		string key,
		string? value,
		CancellationToken cancellationToken = default)
	{
		lock (_sync)
		{
			_strings[key] = value;
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

		lock (_sync)
		{
			_secrets[key] = value;
		}
	}

	private Dictionary<string, TValue> Snapshot<TValue>(Dictionary<string, TValue> source)
	{
		lock (_sync)
		{
			return new Dictionary<string, TValue>(source, StringComparer.Ordinal);
		}
	}
}
