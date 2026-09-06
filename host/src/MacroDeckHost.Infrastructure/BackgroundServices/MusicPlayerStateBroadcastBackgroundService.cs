using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Triggers.Providers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;
using MacroDeck.Sdk.Logging;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class MusicPlayerStateBroadcastBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _defaultInterval = TimeSpan.FromSeconds(2);

	private static readonly TimeSpan _pollTimeout = TimeSpan.FromSeconds(10);

	// An enumeration that momentarily drops to zero while an integration is (re)initializing must not
	// blank an already-populated widget to "Not connected". The list is held for a few ticks and only a
	// persistent empty is pushed, so a config-flow completion or an enable toggle no longer flickers.
	// 10 ticks (~20s): a reconfigure's shutdown+drain is bounded at ~12s now, so this covers the
	// realistic worst case; a genuine removal shows "Not connected" those seconds later, which is fine.
	private const int EmptyInstancesConfirmTicks = 10;

	private const int ResyncTicks = 30;

	private readonly IMusicPlayerRegistry _registry;
	private readonly IUiTransport _transport;
	private readonly MusicPlayerEventProvider _events;
	private readonly IMusicPlayerStateCache _stateCache;
	private readonly IMusicPlayerPollNudge _pollNudge;
	private readonly IMusicPlayerInstancesSnapshot _instancesSnapshot;
	private readonly IMusicPlayerStateNotifier _notifier;
	private readonly ILogger _logger;
	private readonly TimeSpan _interval;

	private readonly Dictionary<string, string> _lastByInstance = new(StringComparer.Ordinal);

	// Tracks the widget-visible connection flips - the ground truth for "the UI showed Not connected
	// at 14:32" that no provider-side log line can give, because providers only see their own reads.
	private readonly Dictionary<string, bool> _lastConnectedByInstance = new(StringComparer.Ordinal);

	// Concurrent because ReadState runs for all instances at once (PollStates).
	private readonly ConcurrentDictionary<string, FailureEpisodeTracker> _readFailures = new(StringComparer.Ordinal);
	private readonly TimeSpan? _failureSummaryInterval;

	private string _lastInstancesJson = "[]";
	private int _consecutiveEmptyTicks;
	private int _ticksUntilResync = ResyncTicks;

	public MusicPlayerStateBroadcastBackgroundService(
		IHostApplicationLifetime lifetime,
		IMusicPlayerRegistry registry,
		IUiTransport transport,
		MusicPlayerEventProvider events,
		IMusicPlayerStateCache stateCache,
		IMusicPlayerPollNudge pollNudge,
		IMusicPlayerInstancesSnapshot instancesSnapshot,
		IMusicPlayerStateNotifier notifier,
		ILogger logger,
		TimeSpan? failureSummaryInterval = null,
		TimeSpan? pollInterval = null)
		: base(lifetime)
	{
		_registry = registry;
		_transport = transport;
		_events = events;
		_stateCache = stateCache;
		_pollNudge = pollNudge;
		_instancesSnapshot = instancesSnapshot;
		_notifier = notifier;
		_logger = logger.ForContext<MusicPlayerStateBroadcastBackgroundService>();
		_failureSummaryInterval = failureSummaryInterval;
		_interval = pollInterval ?? _defaultInterval;
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(_interval);
		try
		{
			await SafeTick(stoppingToken);

			Task<bool>? tick = null;
			while (!stoppingToken.IsCancellationRequested)
			{
				tick ??= timer.WaitForNextTickAsync(stoppingToken).AsTask();
				var completed = await Task.WhenAny(tick, _pollNudge.Due);
				if (completed == tick)
				{
					if (!await tick)
					{
						break;
					}

					tick = null;
				}
				else
				{
					_pollNudge.Rearm();
				}

				await SafeTick(stoppingToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async Task SafeTick(CancellationToken stoppingToken)
	{
		try
		{
			await Tick(stoppingToken);
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Music player broadcast tick failed");
		}
	}

	internal async Task Tick(CancellationToken ct)
	{
		var resync = --_ticksUntilResync <= 0;
		if (resync)
		{
			_ticksUntilResync = ResyncTicks;
		}

		var instances = _registry.GetInstances();

		if (instances.Count == 0 && _lastInstancesJson != "[]")
		{
			_consecutiveEmptyTicks++;
			if (_consecutiveEmptyTicks < EmptyInstancesConfirmTicks)
			{
				return;
			}
		}
		else
		{
			_consecutiveEmptyTicks = 0;
		}

		await BroadcastInstancesIfChanged(instances, resync, ct);

		var payloads = await PollStates(instances, ct);

		foreach (var (instanceId, payload) in payloads)
		{
			_events.Observe(payload);
			_stateCache.Record(instanceId, payload);
			await BroadcastIfChanged(instanceId, payload, resync, ct);
		}

		var seen = instances.Select(i => i.InstanceId).ToHashSet(StringComparer.Ordinal);
		foreach (var goneId in _lastByInstance.Keys.Where(id => !seen.Contains(id)).ToList())
		{
			var gone = MusicPlayerStatePayload.Disconnected(goneId);
			_events.Observe(gone);
			await BroadcastIfChanged(goneId, gone, force: false, ct);
			_lastByInstance.Remove(goneId);
			_lastConnectedByInstance.Remove(goneId);
			_readFailures.TryRemove(goneId, out _);
		}

		_events.Forget(seen);
		_stateCache.Forget(seen);
	}

	private async Task<IReadOnlyList<(string InstanceId, MusicPlayerStatePayload Payload)>> PollStates(
		IReadOnlyList<MusicPlayerInstanceDescriptor> instances,
		CancellationToken ct)
	{
		var reads = instances.Select(instance => ReadState(instance.InstanceId, ct));
		var results = await Task.WhenAll(reads);

		return results.Where(r => r.Payload is not null)
			.Select(r => (r.InstanceId, Payload: r.Payload!))
			.ToList();
	}

	private async Task<(string InstanceId, MusicPlayerStatePayload? Payload)> ReadState(
		string instanceId,
		CancellationToken ct)
	{
		var player = _registry.GetPlayer(instanceId);
		if (player is null)
		{
			return (instanceId, MusicPlayerStatePayload.Disconnected(instanceId));
		}

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeout.CancelAfter(_pollTimeout);

		var started = Stopwatch.GetTimestamp();
		try
		{
			var payload = MusicPlayerStatePayload.From(await player.GetStateAsync(timeout.Token), instanceId);
			NoteReadRecovered(instanceId);
			return (instanceId, payload);
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			throw;
		}
		catch (OperationCanceledException)
		{
			if (NoteReadFailure(instanceId, $"no answer within {_pollTimeout}"))
			{
				_logger.Debug(
					"Music player instance {InstanceId} did not answer within {Timeout} (gave up after {ElapsedMs} ms); " +
					"keeping last state",
					instanceId,
					_pollTimeout,
					(long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
			}

			return (instanceId, null);
		}
		catch (Exception ex)
		{
			if (NoteReadFailure(instanceId, ex.Message))
			{
				_logger.Debug(ex,
					"Failed to read music player state for {InstanceId} after {ElapsedMs} ms; keeping last state",
					instanceId,
					(long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
			}

			return (instanceId, null);
		}
	}

	private bool NoteReadFailure(string instanceId, string error)
	{
		var tracker = _readFailures.GetOrAdd(instanceId, _ => new FailureEpisodeTracker(_failureSummaryInterval));
		var signal = tracker.RecordFailure(error);
		switch (signal.Kind)
		{
			case FailureEpisodeSignalKind.Onset:
				_logger.Warning(
					"Music player instance {InstanceId} state polls started failing ({LastError}); keeping last state",
					instanceId,
					error);
				return false;
			case FailureEpisodeSignalKind.SummaryDue:
				_logger.Information(
					"Music player instance {InstanceId} state polls failing for {Duration} ({Failures} consecutive; " +
					"last: {LastError})",
					instanceId,
					signal.Duration,
					signal.ConsecutiveFailures,
					signal.LastError);
				return false;
			default:
				return true;
		}
	}

	private void NoteReadRecovered(string instanceId)
	{
		if (_readFailures.TryGetValue(instanceId, out var tracker) && tracker.RecordSuccess() is { } episode)
		{
			_logger.Information(
				"Music player instance {InstanceId} state polls recovered after {Duration} ({Failures} failures)",
				instanceId,
				episode.Duration,
				episode.Failures);
		}
	}

	private async Task BroadcastInstancesIfChanged(
		IReadOnlyList<MusicPlayerInstanceDescriptor> instances,
		bool force,
		CancellationToken ct)
	{
		var dtos = instances.Select(MusicPlayerInstanceDto.From).ToList();
		var json = JsonSerializer.Serialize(dtos);
		var changed = json != _lastInstancesJson;
		if (!changed && !force)
		{
			return;
		}

		await _transport.Send(new MusicPlayerInstancesChangedNotification { Instances = dtos }, ct);
		_lastInstancesJson = json;
		_instancesSnapshot.Record(dtos);
		_notifier.PublishInstancesChanged();

		if (changed)
		{
			// Rare by construction (lifecycle events only), so this cannot spam the file sink.
			_logger.Information("Music player instances changed: {InstanceIds}",
				dtos.Select(d => d.InstanceId).ToList());
		}
	}

	private async Task BroadcastIfChanged(
		string instanceId,
		MusicPlayerStatePayload payload,
		bool force,
		CancellationToken ct)
	{
		var json = JsonSerializer.Serialize(payload);
		if (!force && _lastByInstance.TryGetValue(instanceId, out var previous) && previous == json)
		{
			return;
		}

		await _transport.Send(new MusicPlayerStateChangedNotification { State = payload }, ct);
		_notifier.Publish(instanceId, payload);
		_lastByInstance[instanceId] = json;
		LogConnectionTransition(instanceId, payload.IsConnected);
	}

	private void LogConnectionTransition(string instanceId, bool isConnected)
	{
		if (!_lastConnectedByInstance.TryGetValue(instanceId, out var previous))
		{
			_logger.Information("Music player instance {InstanceId} initial state: {ConnectionState}",
				instanceId,
				isConnected ? "connected" : "disconnected");
		}
		else if (previous != isConnected)
		{
			_logger.Information("Music player instance {InstanceId}: {Previous} -> {Current}",
				instanceId,
				previous ? "connected" : "disconnected",
				isConnected ? "connected" : "disconnected");
		}

		_lastConnectedByInstance[instanceId] = isConnected;
	}
}
