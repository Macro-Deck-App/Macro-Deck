using MacroDeckHost.Integrations.YouTube.Auth;
using Serilog;

namespace MacroDeckHost.Integrations.YouTube.Protocol;

internal sealed record YouTubePollerOptions
{
	public TimeSpan StateInterval { get; init; } = TimeSpan.FromSeconds(60);

	public TimeSpan ThrottledStateInterval { get; init; } = TimeSpan.FromMinutes(2);

	public TimeSpan ChannelInterval { get; init; } = TimeSpan.FromMinutes(10);

	public TimeSpan MinimumChatInterval { get; init; } = TimeSpan.FromSeconds(1);

	public int ThrottledChatFactor { get; init; } = 4;

	public TimeSpan ChatRetryInterval { get; init; } = TimeSpan.FromSeconds(10);

	public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

	public TimeProvider Time { get; init; } = TimeProvider.System;
}

internal sealed record YouTubeLiveTransition(bool IsLive, YouTubeAccountState State);

internal sealed class YouTubeStatePoller : IDisposable
{
	private readonly IYouTubeApiClient _api;
	private readonly YouTubeQuotaBudget _budget;
	private readonly YouTubeAccount _account;
	private readonly Func<Func<YouTubeAccountState, YouTubeAccountState>, YouTubeAccountState> _merge;
	private readonly Action<YouTubeLiveTransition> _transition;
	private readonly Action<YouTubeAccountState> _polled;
	private readonly Action _authorizationLost;
	private readonly ILogger _logger;
	private readonly YouTubePollerOptions _options;
	private readonly CancellationTokenSource _cts = new();

	private Task? _loop;
	private bool _disposed;
	private bool _primed;
	private DateTimeOffset? _channelReadAt;

	public YouTubeStatePoller(
		IYouTubeApiClient api,
		YouTubeQuotaBudget budget,
		YouTubeAccount account,
		Func<Func<YouTubeAccountState, YouTubeAccountState>, YouTubeAccountState> merge,
		Action<YouTubeLiveTransition> transition,
		Action<YouTubeAccountState> polled,
		Action authorizationLost,
		ILogger logger,
		YouTubePollerOptions? options = null)
	{
		_api = api;
		_budget = budget;
		_account = account;
		_merge = merge;
		_transition = transition;
		_polled = polled;
		_authorizationLost = authorizationLost;
		_logger = logger;
		_options = options ?? new YouTubePollerOptions();
	}

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
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await _options.Delay(await NextStepAsync(cancellationToken), cancellationToken);
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

		await PollOnceAsync(cancellationToken);

		return _budget.Level switch
		{
			YouTubeQuotaLevel.Paused => UntilReset(),
			YouTubeQuotaLevel.Throttled => _options.ThrottledStateInterval,
			_ => _options.StateInterval
		};
	}

	internal async Task PollOnceAsync(CancellationToken cancellationToken)
	{
		var previous = _merge(state => state).IsLive;
		var read = await TryAsync("broadcast", () => ReadBroadcastAsync(cancellationToken));

		var now = _options.Time.GetUtcNow();
		if (_budget.Level is not YouTubeQuotaLevel.Paused &&
			(_channelReadAt is null || now - _channelReadAt >= _options.ChannelInterval))
		{
			if (await TryAsync("channel", () => ReadChannelAsync(cancellationToken)))
			{
				_channelReadAt = now;
			}
		}

		var state = _merge(current => current);
		if (!read)
		{
			return;
		}

		var wasPrimed = _primed;
		_primed = true;
		_polled(state);

		if (wasPrimed && previous is { } before && state.IsLive is { } after && before != after)
		{
			_transition(new YouTubeLiveTransition(after, state));
		}
	}

	private async Task ReadBroadcastAsync(CancellationToken cancellationToken)
	{
		var broadcasts = await _api.ListBroadcastsAsync(YouTubeBroadcastStatus.Active, cancellationToken);
		var live = broadcasts.FirstOrDefault(broadcast =>
			string.Equals(broadcast.LifeCycleStatus, YouTubeLifeCycleStatus.Live, StringComparison.Ordinal));

		if (live is null)
		{
			_merge(state => state with
			{
				IsLive = false,
				BroadcastId = null,
				LiveChatId = null,
				ViewerCount = null,
				LikeCount = null,
				StreamStartedAt = null,
				StreamThumbnailUrl = null
			});

			return;
		}

		var video = await _api.GetVideoAsync(live.Id, cancellationToken);

		_merge(state => state with
		{
			IsLive = true,
			BroadcastId = live.Id,
			LiveChatId = video?.ActiveLiveChatId ?? live.LiveChatId,
			StreamTitle = video?.Snippet.Title is { Length: > 0 } title ? title : live.Title,
			ViewerCount = video?.ConcurrentViewers,
			LikeCount = video?.LikeCount,
			StreamStartedAt = video?.ActualStartTime ?? live.ActualStartTime,
			StreamThumbnailUrl = video?.Snippet.ThumbnailUrl
		});
	}

	private async Task ReadChannelAsync(CancellationToken cancellationToken)
	{
		var channel = await _api.GetMyChannelAsync(cancellationToken);
		if (channel is null)
		{
			return;
		}

		_merge(state => state with
		{
			ChannelTitle = channel.Title is { Length: > 0 } title ? title : state.ChannelTitle,
			SubscriberCount = channel.SubscriberCount
		});
	}

	private TimeSpan UntilReset()
	{
		var wait = _budget.ResetsAt - _options.Time.GetUtcNow();
		return wait > TimeSpan.Zero ? wait : _options.StateInterval;
	}

	private async Task<bool> TryAsync(string what, Func<Task> call)
	{
		try
		{
			await call();
			return true;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (YouTubeOAuthRejectedException)
		{
			_authorizationLost();
		}
		catch (YouTubeApiException ex) when (ex.IsUnauthorized)
		{
			_authorizationLost();
		}
		catch (ObjectDisposedException)
		{
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Could not read the YouTube {What} of {Channel}", what, _account.ChannelId);
		}

		return false;
	}
}
