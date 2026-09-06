using System.Diagnostics;
using System.Globalization;
using MacroDeck.Sdk.ConfigFlow;
using Serilog;

namespace MacroDeckHost.Integrations.Spotify;

internal sealed class SpotifyTokenPersister : IDisposable
{
	private readonly IIntegrationConfig _config;
	private readonly ILogger _logger;
	private readonly object _sync = new();

	private Task _tail = Task.CompletedTask;
	private bool _accepting = true;
	private Exception? _firstFailure;

	public SpotifyTokenPersister(IIntegrationConfig config, ILogger logger)
	{
		_config = config;
		_logger = logger;
	}

	public bool Enqueue(Guid entryId, string accessToken, string? refreshToken, DateTime expiresAtUtc)
	{
		lock (_sync)
		{
			if (!_accepting)
			{
				_logger.Warning("Ignoring a Spotify token rotation for entry {EntryId} after persistence stopped",
					entryId);
				return false;
			}

			_tail = PersistAfterAsync(_tail, entryId, accessToken, refreshToken, expiresAtUtc);
			return true;
		}
	}

	public Task<bool> EnqueueAndWaitAsync(
		Guid entryId,
		string accessToken,
		string? refreshToken,
		DateTime expiresAtUtc)
	{
		lock (_sync)
		{
			if (!_accepting)
			{
				_logger.Warning("Ignoring a Spotify token rotation for entry {EntryId} after persistence stopped",
					entryId);
				return Task.FromResult(false);
			}

			var mine = PersistAfterAsync(_tail, entryId, accessToken, refreshToken, expiresAtUtc);
			_tail = mine;
			return mine;
		}
	}

	public async Task FlushAsync()
	{
		Task tail;
		lock (_sync)
		{
			tail = _tail;
		}

		await tail;
		ThrowPersistenceFailure();
	}

	public async Task CompleteAsync()
	{
		Task tail;
		lock (_sync)
		{
			_accepting = false;
			tail = _tail;
		}

		await tail;
		ThrowPersistenceFailure();
	}

	private async Task<bool> PersistAfterAsync(
		Task predecessor,
		Guid entryId,
		string accessToken,
		string? refreshToken,
		DateTime expiresAtUtc)
	{
		await predecessor;
		return await PersistAsync(entryId, accessToken, refreshToken, expiresAtUtc);
	}

	private async Task<bool> PersistAsync(Guid entryId,
		string accessToken,
		string? refreshToken,
		DateTime expiresAtUtc)
	{
		var stopwatch = Stopwatch.StartNew();
		try
		{
			if (!string.IsNullOrEmpty(refreshToken))
			{
				await _config.SetSecretAsync(entryId, SpotifyConfigKeys.RefreshToken, refreshToken);
			}

			await _config.SetSecretAsync(entryId, SpotifyConfigKeys.AccessToken, accessToken);
			await _config.SetStringAsync(entryId,
				SpotifyConfigKeys.ExpiresAt,
				expiresAtUtc.ToString("o", CultureInfo.InvariantCulture));

			_logger.Debug("Persisted refreshed Spotify token for entry {EntryId} in {ElapsedMs} ms",
				entryId,
				stopwatch.ElapsedMilliseconds);
			return true;
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to persist refreshed Spotify token for entry {EntryId}", entryId);
			lock (_sync)
			{
				_firstFailure ??= ex;
			}

			return false;
		}
	}

	private void ThrowPersistenceFailure()
	{
		Exception? failure;
		lock (_sync)
		{
			failure = _firstFailure;
		}

		if (failure is not null)
		{
			throw failure;
		}
	}

	public void Dispose()
	{
		lock (_sync)
		{
			_accepting = false;
		}
	}
}
