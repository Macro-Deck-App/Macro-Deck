using System.Globalization;
using MacroDeck.Sdk.ConfigFlow;
using Serilog;

namespace MacroDeckHost.Integrations.Spotify;

internal sealed class SpotifyApiLimitStore(IIntegrationConfig config, ILogger logger)
{
	private readonly Lock _sync = new();
	private readonly Dictionary<Guid, string> _lastWritten = [];

	private Task _tail = Task.CompletedTask;

	internal async Task<SpotifyApiLimitStatus?> LoadAsync(Guid entryId, CancellationToken cancellationToken = default)
	{
		try
		{
			var until = await config.GetStringAsync(entryId, SpotifyConfigKeys.ApiLimitUntil, cancellationToken);
			if (!TryParse(until, out var pausedUntil) || pausedUntil <= DateTimeOffset.UtcNow)
			{
				return null;
			}

			var since = await config.GetStringAsync(entryId, SpotifyConfigKeys.ApiLimitSince, cancellationToken);
			var kind = await config.GetStringAsync(entryId, SpotifyConfigKeys.ApiLimitKind, cancellationToken);

			return new SpotifyApiLimitStatus(
				string.Equals(kind, nameof(SpotifyApiLimitKind.Quota), StringComparison.Ordinal)
					? SpotifyApiLimitKind.Quota
					: SpotifyApiLimitKind.RateLimit,
				TryParse(since, out var startedAt) ? startedAt : pausedUntil,
				pausedUntil);
		}
		catch (Exception ex)
		{
			logger.Warning(ex, "Could not read the stored Spotify rate-limit state for entry {EntryId}", entryId);
			return null;
		}
	}

	internal void Save(Guid entryId, SpotifyApiLimitStatus status)
	{
		var stamp = status.PausedUntil.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
		Write(entryId,
			stamp,
			async () =>
			{
				await config.SetStringAsync(entryId, SpotifyConfigKeys.ApiLimitKind, status.Kind.ToString());
				await config.SetStringAsync(entryId,
					SpotifyConfigKeys.ApiLimitSince,
					status.Since.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
				await config.SetStringAsync(entryId, SpotifyConfigKeys.ApiLimitUntil, stamp);
			});
	}

	internal void Clear(Guid entryId)
		=> Write(entryId,
			string.Empty,
			async () =>
			{
				await config.SetStringAsync(entryId, SpotifyConfigKeys.ApiLimitUntil, null);
				await config.SetStringAsync(entryId, SpotifyConfigKeys.ApiLimitSince, null);
				await config.SetStringAsync(entryId, SpotifyConfigKeys.ApiLimitKind, null);
			});

	internal Task FlushAsync()
	{
		lock (_sync)
		{
			return _tail;
		}
	}

	private void Write(Guid entryId, string state, Func<Task> write)
	{
		lock (_sync)
		{
			if (_lastWritten.TryGetValue(entryId, out var previous) && previous == state)
			{
				return;
			}

			_lastWritten[entryId] = state;
			_tail = WriteAfterAsync(_tail, entryId, write);
		}
	}

	private async Task WriteAfterAsync(Task predecessor, Guid entryId, Func<Task> write)
	{
		await predecessor;
		try
		{
			await write();
		}
		catch (Exception ex)
		{
			logger.Error(ex, "Could not persist the Spotify rate-limit state for entry {EntryId}", entryId);
			lock (_sync)
			{
				_lastWritten.Remove(entryId);
			}
		}
	}

	private static bool TryParse(string? value, out DateTimeOffset parsed)
		=> DateTimeOffset.TryParse(value,
			CultureInfo.InvariantCulture,
			DateTimeStyles.RoundtripKind,
			out parsed);
}
