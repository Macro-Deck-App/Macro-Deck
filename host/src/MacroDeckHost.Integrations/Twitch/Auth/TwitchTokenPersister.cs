using System.Globalization;
using MacroDeck.Sdk.ConfigFlow;
using Serilog;

namespace MacroDeckHost.Integrations.Twitch.Auth;

internal sealed class TwitchTokenPersister : IDisposable
{
	private readonly IIntegrationConfig _config;
	private readonly ILogger _logger;
	private readonly object _sync = new();

	private Task _tail = Task.CompletedTask;
	private bool _accepting = true;
	private Exception? _firstFailure;

	public TwitchTokenPersister(IIntegrationConfig config, ILogger logger)
	{
		_config = config;
		_logger = logger;
	}

	public bool Enqueue(Guid entryId, TwitchTokens tokens)
	{
		lock (_sync)
		{
			if (!_accepting)
			{
				_logger.Warning("Ignoring a Twitch token rotation for entry {EntryId} after persistence stopped",
					entryId);
				return false;
			}

			_tail = PersistAfterAsync(_tail, entryId, tokens);
			return true;
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

	public void Dispose()
	{
		lock (_sync)
		{
			_accepting = false;
		}
	}

	private async Task PersistAfterAsync(Task predecessor, Guid entryId, TwitchTokens tokens)
	{
		await predecessor;
		await PersistAsync(entryId, tokens);
	}

	private async Task PersistAsync(Guid entryId, TwitchTokens tokens)
	{
		try
		{
			await _config.SetSecretAsync(entryId, TwitchConfigKeys.RefreshToken, tokens.RefreshToken);
			await _config.SetSecretAsync(entryId, TwitchConfigKeys.AccessToken, tokens.AccessToken);
			await _config.SetStringAsync(entryId,
				TwitchConfigKeys.ExpiresAt,
				tokens.ExpiresAt.ToString("o", CultureInfo.InvariantCulture));

			if (tokens.Scopes.Count > 0)
			{
				await _config.SetStringAsync(entryId, TwitchConfigKeys.Scopes, string.Join(' ', tokens.Scopes));
			}

			_logger.Debug("Persisted refreshed Twitch token for entry {EntryId}", entryId);
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to persist refreshed Twitch token for entry {EntryId}", entryId);
			lock (_sync)
			{
				_firstFailure ??= ex;
			}
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
}
