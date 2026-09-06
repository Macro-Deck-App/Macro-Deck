using Serilog;

namespace MacroDeckHost.Integrations.Twitch.Protocol;

internal sealed record TwitchStatePollerOptions
{
	public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(30);

	public TimeSpan RewardInterval { get; init; } = TimeSpan.FromMinutes(5);

	public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;
}

internal sealed class TwitchStatePoller : IDisposable
{
	private readonly ITwitchHelixClient _helix;
	private readonly TwitchAccount _account;
	private readonly Func<Func<TwitchAccountState, TwitchAccountState>, TwitchAccountState> _merge;
	private readonly Action<IReadOnlyList<TwitchCustomReward>> _rewards;
	private readonly ILogger _logger;
	private readonly TwitchStatePollerOptions _options;
	private readonly CancellationTokenSource _cts = new();

	private Task? _loop;
	private bool _disposed;

	public TwitchStatePoller(
		ITwitchHelixClient helix,
		TwitchAccount account,
		Func<Func<TwitchAccountState, TwitchAccountState>, TwitchAccountState> merge,
		Action<IReadOnlyList<TwitchCustomReward>> rewards,
		ILogger logger,
		TwitchStatePollerOptions? options = null)
	{
		_helix = helix;
		_account = account;
		_merge = merge;
		_rewards = rewards;
		_logger = logger;
		_options = options ?? new TwitchStatePollerOptions();
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

	public async Task PollOnceAsync(bool includeRewards, CancellationToken cancellationToken)
	{
		await TryAsync("stream",
			async () =>
			{
				var stream = await _helix.GetStreamAsync(_account.UserId, cancellationToken);
				_merge(state => state with
				{
					IsLive = stream.IsLive,
					ViewerCount = stream.ViewerCount ?? state.ViewerCount,
					StreamStartedAt = stream.StartedAt ?? (stream.IsLive ? state.StreamStartedAt : null),
					StreamTitle = stream.Title ?? state.StreamTitle,
					StreamCategory = stream.CategoryName ?? state.StreamCategory
				});
			});

		await TryAsync("channel",
			async () =>
			{
				var channel = await _helix.GetChannelAsync(_account.UserId, cancellationToken);
				_merge(state => state with
				{
					StreamTitle = channel.Title ?? state.StreamTitle,
					StreamCategory = channel.CategoryName ?? state.StreamCategory
				});
			});

		await TryAsync("followers",
			async () =>
			{
				var followers = await _helix.GetFollowerCountAsync(_account.UserId, cancellationToken);
				if (followers is { } count)
				{
					_merge(state => state with { FollowerCount = count });
				}
			});

		await TryAsync("subscribers",
			async () =>
			{
				var subscribers = await _helix.GetSubscriberInfoAsync(_account.UserId, cancellationToken);
				_merge(state => state with
				{
					SubscriberCount = subscribers.Count ?? state.SubscriberCount,
					SubscriberPoints = subscribers.Points ?? state.SubscriberPoints
				});
			});

		await TryAsync("chat settings",
			async () =>
			{
				var settings = await _helix.GetChatSettingsAsync(_account.UserId, cancellationToken);
				_merge(state => state with { ChatSettings = settings });
			});

		if (includeRewards)
		{
			await TryAsync("rewards",
				async () =>
					_rewards(await _helix.GetCustomRewardsAsync(_account.UserId, cancellationToken)));
		}
	}

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		var sinceRewards = _options.RewardInterval;

		while (!cancellationToken.IsCancellationRequested)
		{
			var withRewards = sinceRewards >= _options.RewardInterval;
			await PollOnceAsync(withRewards, cancellationToken);
			sinceRewards = withRewards ? TimeSpan.Zero : sinceRewards + _options.Interval;

			try
			{
				await _options.Delay(_options.Interval, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
	}

	private async Task TryAsync(string what, Func<Task> call)
	{
		try
		{
			await call();
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (TwitchScopeException)
		{
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Could not read the Twitch {What} of {Login}", what, _account.Login);
		}
	}
}
