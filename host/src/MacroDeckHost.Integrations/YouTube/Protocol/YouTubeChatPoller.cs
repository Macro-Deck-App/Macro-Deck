using MacroDeckHost.Integrations.YouTube.Auth;
using Serilog;

namespace MacroDeckHost.Integrations.YouTube.Protocol;

internal sealed class YouTubeChatPoller : IDisposable
{
	private static readonly HashSet<string> _endingReasons =
		new(["liveChatEnded", "liveChatNotFound", "liveChatDisabled"], StringComparer.Ordinal);

	private readonly IYouTubeApiClient _api;
	private readonly YouTubeQuotaBudget _budget;
	private readonly string _liveChatId;
	private readonly Action<IReadOnlyList<YouTubeChatMessage>, bool> _received;
	private readonly Action _ended;
	private readonly Action _authorizationLost;
	private readonly ILogger _logger;
	private readonly YouTubePollerOptions _options;
	private readonly CancellationTokenSource _cts = new();

	private Task? _loop;
	private bool _disposed;
	private bool _primed;
	private string? _pageToken;

	public YouTubeChatPoller(
		IYouTubeApiClient api,
		YouTubeQuotaBudget budget,
		string liveChatId,
		Action<IReadOnlyList<YouTubeChatMessage>, bool> received,
		Action ended,
		Action authorizationLost,
		ILogger logger,
		YouTubePollerOptions? options = null)
	{
		_api = api;
		_budget = budget;
		_liveChatId = liveChatId;
		_received = received;
		_ended = ended;
		_authorizationLost = authorizationLost;
		_logger = logger;
		_options = options ?? new YouTubePollerOptions();
	}

	public string LiveChatId => _liveChatId;

	public bool HasEnded { get; private set; }

	public void Start()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		_loop ??= Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_cts.Cancel();
		_cts.Dispose();
	}

	internal async Task RunAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested && !HasEnded)
		{
			try
			{
				var wait = await NextStepAsync(cancellationToken);
				if (HasEnded)
				{
					return;
				}

				await _options.Delay(wait, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
	}

	internal async Task<TimeSpan> NextStepAsync(CancellationToken cancellationToken)
	{
		if (_budget.Level is YouTubeQuotaLevel.Paused)
		{
			return UntilReset();
		}

		YouTubeChatPage page;
		try
		{
			page = await _api.ListChatMessagesAsync(_liveChatId, _pageToken, cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (YouTubeApiException ex) when (ex.Reason is { } reason && _endingReasons.Contains(reason))
		{
			End();
			return TimeSpan.Zero;
		}
		catch (Exception ex) when (ex is YouTubeOAuthRejectedException ||
			(ex is YouTubeApiException { IsUnauthorized: true }))
		{
			_authorizationLost();
			End();
			return TimeSpan.Zero;
		}
		catch (ObjectDisposedException)
		{
			End();
			return TimeSpan.Zero;
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Could not read the YouTube live chat {LiveChatId}", _liveChatId);
			return _budget.Level is YouTubeQuotaLevel.Paused ? UntilReset() : _options.ChatRetryInterval;
		}

		var live = _primed;
		_primed = true;
		_pageToken = page.NextPageToken ?? _pageToken;

		var ended = page.OfflineAt is not null || page.Items.Any(YouTubeChatMapper.EndsChat);
		var items = page.Items.Where(item => !YouTubeChatMapper.EndsChat(item)).ToList();
		if (items.Count > 0)
		{
			_received(items, live);
		}

		if (ended)
		{
			End();
			return TimeSpan.Zero;
		}

		var interval = TimeSpan.FromMilliseconds(Math.Max(page.PollingIntervalMillis,
			_options.MinimumChatInterval.TotalMilliseconds));

		return _budget.Level switch
		{
			YouTubeQuotaLevel.Paused => UntilReset(),
			YouTubeQuotaLevel.Throttled => interval * _options.ThrottledChatFactor,
			_ => interval
		};
	}

	private void End()
	{
		if (HasEnded)
		{
			return;
		}

		HasEnded = true;
		_ended();
	}

	private TimeSpan UntilReset()
	{
		var wait = _budget.ResetsAt - _options.Time.GetUtcNow();
		return wait > TimeSpan.Zero ? wait : _options.ChatRetryInterval;
	}
}
