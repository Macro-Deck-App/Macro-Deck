using System.Threading.Channels;
using MacroDeck.Sdk.Logging;
using Serilog;
using Serilog.Events;

namespace MacroDeckHost.Integrations.Obs;

internal enum ObsConnectionStatus
{
	Connecting,
	Connected,
	Reconnecting,
	Disconnected
}

internal enum ObsCommandOutcome
{
	Success,
	NotConnected,
	NotRecording,
	Rejected
}

internal readonly record struct ObsCommandResult(ObsCommandOutcome Outcome, string? Message = null, int? Code = null)
{
	public static ObsCommandResult Success { get; } = new(ObsCommandOutcome.Success);

	public static ObsCommandResult NotConnected { get; } = new(ObsCommandOutcome.NotConnected);

	public static ObsCommandResult NotRecording { get; } = new(ObsCommandOutcome.NotRecording);

	public static ObsCommandResult Rejected(string message, int? code = null)
		=> new(ObsCommandOutcome.Rejected, message, code);
}

internal sealed class ObsConnection : IDisposable, IAsyncDisposable
{
	private static readonly TimeSpan _maxReconnectDelay = TimeSpan.FromMinutes(1);

	// Per-target reads (an input's mute, a scene item's visibility, a filter's enabled flag) are not part
	// of the polled status, so a button following one has to ask. The in-flight read is cached rather than
	// its result, which makes concurrent askers share one obs-websocket round trip: N buttons on the same
	// input cost one query per interval instead of one query each per poll.
	private static readonly TimeSpan _targetReadTtl = TimeSpan.FromSeconds(1);

	private readonly IObsClient _client;
	private readonly ILogger _logger;
	private readonly string _url;
	private readonly string? _password;
	private readonly TimeSpan _reconnectDelay;
	private readonly TimeSpan _pollInterval;
	private readonly Action? _onVariablesChanged;
	private readonly CancellationTokenSource _cts = new();

	private readonly Channel<ConnectionSignal> _signals = Channel.CreateUnbounded<ConnectionSignal>(
		new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

	private readonly ObsEventEmitter? _events;
	private readonly FailureEpisodeTracker _failures;
	private readonly object _publicationGate = new();
	private readonly ObsChangeCoalescer _changes;
	private readonly ObsCustomEventLimiter _customEventLimiter;
	private readonly Action<IReadOnlyList<ObsTargetChange>>? _onTargetsChanged;

	private readonly Lock _targetReadGate = new();

	// Holds the pending read as a bare Task so one cache can serve every value type, which means the
	// cast back to Task<T?> is sound only while each kind of read owns a distinct key prefix
	// ("mute:", "volume:", "settings:", ...). Reusing a prefix for a different type is an
	// InvalidCastException at runtime, not a compile error.
	private readonly Dictionary<string, (DateTime AtUtc, Task Read)> _targetReads = new(StringComparer.Ordinal);

	private Timer? _pollTimer;
	private Task? _lifetime;
	private volatile ObsState _state = ObsState.Disconnected;
	private volatile ObsConnectionStatus _status = ObsConnectionStatus.Disconnected;
	private int _started;
	private int _disposed;
	private int _disconnectedLogged;
	private int _reconnectFailures;
	private volatile string? _lastError;
	private volatile string? _lastRecordingFilePath;
	private volatile string? _lastScreenshotPath;

	internal ObsConnection(
		IObsClient client,
		string url,
		string? password,
		TimeSpan? reconnectDelay = null,
		ObsEventEmitter? events = null,
		ILogger? logger = null,
		TimeSpan? failureSummaryInterval = null,
		Action? onVariablesChanged = null,
		TimeSpan? pollInterval = null,
		Action<IReadOnlyList<ObsTargetChange>>? onTargetsChanged = null,
		TimeProvider? timeProvider = null)
	{
		_client = client;
		_logger = logger ?? IntegrationLog.For<ObsConnection>(ObsIntegration.IntegrationId);
		_url = url;
		_password = password;
		_reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(5);
		_events = events;
		_onVariablesChanged = onVariablesChanged;
		_pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
		_failures = new FailureEpisodeTracker(failureSummaryInterval);
		_onTargetsChanged = onTargetsChanged;
		_changes = new ObsChangeCoalescer(FlushTargetChanges, TimeSpan.FromMilliseconds(100), timeProvider);
		_customEventLimiter = new ObsCustomEventLimiter(timeProvider);

		_client.Connected += OnConnected;
		_client.Disconnected += OnDisconnected;
		_client.StateChanged += OnStateChanged;
		_client.InputMuteChanged += OnInputMuteChanged;
		_client.ReplayBufferSaved += OnReplayBufferSaved;
		_client.RecordFileChanged += OnRecordFileChanged;
		_client.ScreenshotSaved += OnScreenshotSaved;
		_client.InputSettingsChanged += OnInputSettingsChanged;
		_client.SourceFilterChanged += OnSourceFilterChanged;
		_client.InputActiveChanged += OnInputActiveChanged;
		_client.InputShowingChanged += OnInputShowingChanged;
		_client.CustomEventReceived += OnCustomEventReceived;
	}

	public ObsState State => _state;

	public ObsConnectionStatus Status => _status;

	public string? LastRecordingFilePath => _lastRecordingFilePath;

	public string? LastScreenshotPath => _lastScreenshotPath;

	public bool IsConnected => _client.IsConnected;

	public void Start()
	{
		if (Interlocked.Exchange(ref _started, 1) != 0 || _cts.IsCancellationRequested)
		{
			return;
		}

		_lifetime = RunLifetimeAsync();
	}

	public Task<IReadOnlyList<string>> GetSceneNamesAsync() => QueryAsync(_client.GetSceneNames);

	public Task<IReadOnlyList<string>> GetSceneItemNamesAsync(string sceneName)
		=> QueryAsync(() => _client.GetSceneItemNames(sceneName));

	public Task<IReadOnlyList<string>> GetGroupNamesAsync() => QueryAsync(_client.GetGroupNames);

	public Task<IReadOnlyList<string>> GetGroupItemNamesAsync(string groupName)
		=> QueryAsync(() => _client.GetGroupItemNames(groupName));

	public Task<IReadOnlyList<string>> GetInputNamesAsync() => QueryAsync(_client.GetInputNames);

	public Task<IReadOnlyList<string>> GetSourceNamesAsync() => QueryAsync(_client.GetSourceNames);

	public Task<IReadOnlyList<string>> GetSourceFilterNamesAsync(string sourceName)
		=> QueryAsync(() => _client.GetSourceFilterNames(sourceName));

	public Task<IReadOnlyList<string>> GetProfileNamesAsync() => QueryAsync(_client.GetProfileNames);

	public Task<IReadOnlyList<string>> GetSceneCollectionNamesAsync() => QueryAsync(_client.GetSceneCollectionNames);

	public Task<IReadOnlyList<string>> GetOutputNamesAsync() => QueryAsync(_client.GetOutputNames);

	public Task<ObsOutputOutcome> StartOutputAsync(string outputName)
		=> OutputCommandAsync(() => _client.StartOutput(outputName));

	public Task<ObsOutputOutcome> StopOutputAsync(string outputName)
		=> OutputCommandAsync(() => _client.StopOutput(outputName));

	public Task<ObsOutputOutcome> ToggleOutputAsync(string outputName)
		=> OutputCommandAsync(() => _client.ToggleOutput(outputName));

	public Task<ObsOutputRead> GetOutputActiveAsync(string outputName) => Task.Run(() =>
	{
		var active = false;
		var outcome = RunOutputCommand(() => active = _client.GetOutputActive(outputName));
		return new ObsOutputRead(outcome, active);
	});

	public Task<bool?> GetOutputActiveCachedAsync(string outputName)
		=> CachedTargetReadAsync<bool>($"output-active:{outputName}", () => _client.GetOutputActive(outputName));

	public Task<bool> SetSceneAsync(string sceneName) => RunAsync(() => _client.SetCurrentScene(sceneName));
	public Task<bool> SetPreviewSceneAsync(string sceneName) => RunAsync(() => _client.SetPreviewScene(sceneName));
	public Task<bool> SetProfileAsync(string profileName) => RunAsync(() => _client.SetCurrentProfile(profileName));

	public Task<ObsCommandResult> SetSceneCollectionAsync(string sceneCollectionName)
		=> RunForResultAsync(() => _client.SetCurrentSceneCollection(sceneCollectionName));

	public Task<bool> StartRecordingAsync() => RunAsync(_client.StartRecord);
	public Task<bool> StopRecordingAsync() => RunAsync(_client.StopRecord);
	public Task<bool> ToggleRecordingAsync() => RunAsync(_client.ToggleRecord);
	public Task<bool> TogglePauseRecordingAsync() => RunAsync(_client.ToggleRecordPause);
	public Task<bool> StartStreamingAsync() => RunAsync(_client.StartStream);
	public Task<bool> StopStreamingAsync() => RunAsync(_client.StopStream);
	public Task<bool> ToggleStreamingAsync() => RunAsync(_client.ToggleStream);
	public Task<bool> StartVirtualCamAsync() => RunAsync(_client.StartVirtualCam);
	public Task<bool> StopVirtualCamAsync() => RunAsync(_client.StopVirtualCam);
	public Task<bool> ToggleVirtualCamAsync() => RunAsync(_client.ToggleVirtualCam);
	public Task<bool> StartReplayBufferAsync() => RunAsync(_client.StartReplayBuffer);
	public Task<bool> StopReplayBufferAsync() => RunAsync(_client.StopReplayBuffer);
	public Task<bool> ToggleReplayBufferAsync() => RunAsync(_client.ToggleReplayBuffer);
	public Task<bool> SaveReplayBufferAsync() => RunAsync(_client.SaveReplayBuffer);
	public Task<ObsCommandResult> SplitRecordFileAsync() => RunForResultAsync(_client.SplitRecordFile);

	public Task<ObsCommandResult> CreateRecordChapterAsync(string? chapterName)
		=> RunForResultAsync(() => _client.CreateRecordChapter(chapterName));

	public Task<ObsCommandResult> SetRecordDirectoryAsync(string directory)
		=> RunForResultAsync(() => _client.SetRecordDirectory(directory));

	public Task<ObsCommandResult> SetInputSettingsAsync(string inputName, string settingsJson)
		=> RunForResultAsync(() => _client.SetInputSettings(inputName, settingsJson));

	public Task<bool> SetSourceVisibleAsync(string sceneName, string sourceName, bool visible)
		=> RunAndForgetTargetReadAsync(() => _client.SetSourceVisible(sceneName, sourceName, visible),
			$"visible:{sceneName}\u0000{sourceName}");

	public Task<bool> ToggleSourceVisibleAsync(string sceneName, string sourceName)
		=> RunAndForgetTargetReadAsync(() => _client.ToggleSourceVisible(sceneName, sourceName),
			$"visible:{sceneName}\u0000{sourceName}");

	/// <summary>Whether an input is muted, or <c>null</c> when it cannot be read.</summary>
	public Task<bool?> GetInputMutedAsync(string inputName)
		=> QueryValueAsync<bool?>(() => _client.GetInputMuted(inputName), null);

	/// <summary>
	/// The same read as <see cref="GetInputMutedAsync" />, coalesced across callers - see the remark on
	/// <see cref="GetSourceFilterEnabledCachedAsync" /> for why a cached and an uncached form of the same
	/// read coexist. Used by the dynamic variable provider and the "Set Input Mute" state provider, both
	/// of which poll on their own schedule; the explicit "Get Input Mute State" action keeps the uncached
	/// read.
	/// </summary>
	public Task<bool?> GetInputMutedCachedAsync(string inputName)
		=> CachedTargetReadAsync<bool>($"mute:{inputName}", () => _client.GetInputMuted(inputName));

	/// <summary>Whether a scene item is visible, or <c>null</c> when it cannot be read.</summary>
	public Task<bool?> GetSourceVisibleAsync(string sceneName, string sourceName)
		=> QueryValueAsync<bool?>(() => _client.GetSourceVisible(sceneName, sourceName), null);

	/// <summary>
	/// The same read as <see cref="GetSourceVisibleAsync" />, coalesced across callers. Used by the
	/// dynamic variable provider and the "Set Source Visibility" state provider, both of which poll on
	/// their own schedule; the explicit "Get Source Visibility" action keeps the uncached read.
	/// </summary>
	public Task<bool?> GetSourceVisibleCachedAsync(string sceneName, string sourceName)
		=> CachedTargetReadAsync<bool>($"visible:{sceneName}\u0000{sourceName}",
			() => _client.GetSourceVisible(sceneName, sourceName));

	/// <summary>
	/// The same read as <see cref="GetInputVolumePercentAsync"/>, coalesced across callers - see the remark
	/// on <see cref="GetSourceFilterEnabledCachedAsync"/> for why a cached and an uncached form of the same
	/// read coexist. Used by the dynamic variable provider, which polls on its own schedule.
	/// </summary>
	public Task<double?> GetInputVolumePercentCachedAsync(string inputName)
		=> CachedTargetReadAsync<double>($"volume:{inputName}", () => _client.GetInputVolume(inputName) * 100d);

	/// <summary>How the input is monitored, as OBS's own string value, or <c>null</c> when it cannot be read.</summary>
	public Task<string?> GetInputAudioMonitorTypeAsync(string inputName)
		=> CachedTargetReadReferenceAsync<string>($"monitor-type:{inputName}",
			() => _client.GetInputAudioMonitorType(inputName));

	/// <summary>The input's audio sync offset in milliseconds, or <c>null</c> when it cannot be read.</summary>
	public Task<int?> GetInputAudioSyncOffsetMillisecondsAsync(string inputName)
		=> CachedTargetReadAsync<int>($"sync-offset:{inputName}",
			() => _client.GetInputAudioSyncOffsetMilliseconds(inputName));

	/// <summary>Which of the six audio tracks the input is assigned to, or <c>null</c> when it cannot be read.</summary>
	public Task<ObsAudioTracks?> GetInputAudioTracksAsync(string inputName)
		=> CachedTargetReadReferenceAsync<ObsAudioTracks>($"audio-tracks:{inputName}",
			() => _client.GetInputAudioTracks(inputName));

	/// <summary>
	/// Whether a source is rendered in the program output and/or the preview, or <c>null</c> when it cannot
	/// be read.
	/// </summary>
	public Task<ObsSourceActivity?> GetSourceActiveAsync(string sourceName)
		=> CachedTargetReadReferenceAsync<ObsSourceActivity>($"source-active:{sourceName}",
			() => _client.GetSourceActive(sourceName));

	/// <summary>
	/// An input's settings as raw JSON text, or <c>null</c> when it cannot be read. Cached per input rather
	/// than per setting key: one bound input commonly has several setting resources bound at once, and this
	/// is what lets all of them share a single obs-websocket round trip instead of costing one query each.
	/// </summary>
	public Task<string?> GetInputSettingsJsonAsync(string inputName)
		=> CachedTargetReadReferenceAsync<string>($"settings:{inputName}", () => _client.GetInputSettings(inputName));

	public Task<string?> GetInputDefaultSettingsJsonAsync(string inputName)
		=> QueryValueAsync<string?>(() => _client.GetInputDefaultSettings(inputName), null);

	// Two near-identical methods, not one, because an unconstrained "T?" erases to bare T for a value type
	// at this generic method's own definition - only a "where T : struct" constraint makes the C# compiler
	// actually emit Nullable<T> for it, and a value type can never also satisfy "where T : class". Both
	// share the same coalescing cache; only the constraint (and so the fallback null literal's shape) differs.
	private Task<T?> CachedTargetReadAsync<T>(string key, Func<T> read)
		where T : struct
	{
		if (!IsConnected)
		{
			return Task.FromResult<T?>(null);
		}

		lock (_targetReadGate)
		{
			var now = DateTime.UtcNow;
			if (_targetReads.TryGetValue(key, out var cached) && now - cached.AtUtc < _targetReadTtl)
			{
				return (Task<T?>)cached.Read;
			}

			PruneStaleTargetReads(now);

			var task = QueryValueAsync<T?>(() => read(), null);
			_targetReads[key] = (now, task);
			return task;
		}
	}

	private Task<T?> CachedTargetReadReferenceAsync<T>(string key, Func<T> read)
		where T : class
	{
		if (!IsConnected)
		{
			return Task.FromResult<T?>(null);
		}

		lock (_targetReadGate)
		{
			var now = DateTime.UtcNow;
			if (_targetReads.TryGetValue(key, out var cached) && now - cached.AtUtc < _targetReadTtl)
			{
				return (Task<T?>)cached.Read;
			}

			PruneStaleTargetReads(now);

			var task = QueryValueAsync<T?>(() => read(), null);
			_targetReads[key] = (now, task);
			return task;
		}
	}

	// Forgotten after the write completes, so a read already in flight from before the write is
	// dropped too and the next asker queries OBS again.
	private async Task<bool> RunAndForgetTargetReadAsync(Action action, string key)
	{
		var succeeded = await RunAsync(action).ConfigureAwait(false);
		ForgetTargetRead(key);
		return succeeded;
	}

	private void ForgetTargetRead(string key)
	{
		lock (_targetReadGate)
		{
			_targetReads.Remove(key);
		}
	}

	private void PruneStaleTargetReads(DateTime now)
	{
		foreach (var stale in _targetReads
			.Where(entry => now - entry.Value.AtUtc >= _targetReadTtl)
			.Select(entry => entry.Key)
			.ToList())
		{
			_targetReads.Remove(stale);
		}
	}

	public Task<bool> SetInputMuteAsync(string inputName, bool muted)
		=> RunAndForgetTargetReadAsync(() => _client.SetInputMute(inputName, muted), $"mute:{inputName}");

	public Task<bool> ToggleInputMuteAsync(string inputName)
		=> RunAndForgetTargetReadAsync(() => _client.ToggleInputMute(inputName), $"mute:{inputName}");

	public Task<double?> GetInputVolumePercentAsync(string inputName)
		=> QueryValueAsync<double?>(() => _client.GetInputVolume(inputName) * 100d, null);

	public Task<bool> SetInputVolumePercentAsync(string inputName, double percent)
		=> RunAsync(() => _client.SetInputVolume(inputName, ToMultiplier(percent)));

	public Task<bool> AdjustInputVolumePercentAsync(string inputName, double deltaPercent) => RunAsync(() =>
	{
		var target = _client.GetInputVolume(inputName) * 100d + deltaPercent;
		_client.SetInputVolume(inputName, ToMultiplier(target));
	});

	public Task<bool> SetInputVolumeDecibelsAsync(string inputName, double decibels)
		=> RunAsync(() => _client.SetInputVolume(inputName, ObsVolumeScale.ToMultiplier(decibels)));

	public Task<bool> AdjustInputVolumeDecibelsAsync(string inputName, double deltaDecibels) => RunAsync(() =>
	{
		var target = ObsVolumeScale.ToDecibels(_client.GetInputVolume(inputName)) + deltaDecibels;
		_client.SetInputVolume(inputName, ObsVolumeScale.ToMultiplier(target));
	});

	public Task<bool?> GetSourceFilterEnabledAsync(string sourceName, string filterName)
		=> QueryValueAsync<bool?>(() => _client.GetSourceFilterEnabled(sourceName, filterName), null);

	/// <summary>
	/// The same read as <see cref="GetSourceFilterEnabledAsync" />, coalesced across callers. Used by the
	/// polled state provider; the explicit "Get Source Filter State" action keeps the uncached read so a
	/// user asking for the value right after changing it is not answered from the previous second.
	/// </summary>
	public Task<bool?> GetSourceFilterEnabledCachedAsync(string sourceName, string filterName)
		=> CachedTargetReadAsync<bool>($"filter:{sourceName}\u0000{filterName}",
			() => _client.GetSourceFilterEnabled(sourceName, filterName));

	public Task<bool> SetSourceFilterEnabledAsync(string sourceName, string filterName, bool enabled)
		=> RunAndForgetTargetReadAsync(() => _client.SetSourceFilterEnabled(sourceName, filterName, enabled),
			$"filter:{sourceName}\u0000{filterName}");

	public Task<bool> ToggleSourceFilterAsync(string sourceName, string filterName)
		=> RunAndForgetTargetReadAsync(() => _client.ToggleSourceFilterEnabled(sourceName, filterName),
			$"filter:{sourceName}\u0000{filterName}");

	public Task<bool> SetStudioModeAsync(bool enabled) => RunAsync(() => _client.SetStudioMode(enabled));

	public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		_cts.Cancel();
		_signals.Writer.TryComplete();
		_client.Connected -= OnConnected;
		_client.Disconnected -= OnDisconnected;
		_client.InputMuteChanged -= OnInputMuteChanged;
		_client.ReplayBufferSaved -= OnReplayBufferSaved;
		_client.RecordFileChanged -= OnRecordFileChanged;
		_client.ScreenshotSaved -= OnScreenshotSaved;
		_client.StateChanged -= OnStateChanged;
		_client.InputSettingsChanged -= OnInputSettingsChanged;
		_client.SourceFilterChanged -= OnSourceFilterChanged;
		_client.InputActiveChanged -= OnInputActiveChanged;
		_client.InputShowingChanged -= OnInputShowingChanged;
		_client.CustomEventReceived -= OnCustomEventReceived;
		_changes.Dispose();
		StopPolling();

		try
		{
			_client.Disconnect();
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Error while disconnecting OBS client");
		}

		if (_lifetime is not null)
		{
			try
			{
				await _lifetime.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
			}
			catch (ChannelClosedException)
			{
			}
		}

		lock (_publicationGate)
		{
			_state = ObsState.Disconnected;
			_status = ObsConnectionStatus.Disconnected;
		}

		_cts.Dispose();
	}

	private async Task RunLifetimeAsync()
	{
		var firstAttempt = true;
		var everConnected = false;
		var attempt = 0;
		while (!_cts.IsCancellationRequested)
		{
			while (_signals.Reader.TryRead(out _))
			{
			}

			_status = firstAttempt ? ObsConnectionStatus.Connecting : ObsConnectionStatus.Reconnecting;
			attempt++;
			_logger.Debug("OBS connecting to {Url} (attempt {Attempt})", _url, attempt);
			firstAttempt = false;

			string? error = null;
			try
			{
				_client.Connect(_url, _password);
			}
			catch (Exception ex)
			{
				error = ex.Message;
				_signals.Writer.TryWrite(ConnectionSignal.Lost(ex.Message));
			}

			while (!_cts.IsCancellationRequested)
			{
				var signal = await _signals.Reader.ReadAsync(_cts.Token).ConfigureAwait(false);
				if (signal.Connected)
				{
					everConnected = true;
					_status = ObsConnectionStatus.Connected;
					Interlocked.Exchange(ref _reconnectFailures, 0);
					LogConnected();
					var refresh = RefreshState();
					if (refresh != StateRefresh.Failed)
					{
						StartPolling();
					}

					if (refresh == StateRefresh.Published)
					{
						_onVariablesChanged?.Invoke();
					}

					continue;
				}

				error = signal.Reason ?? error;

				break;
			}

			if (_cts.IsCancellationRequested)
			{
				break;
			}

			_status = ObsConnectionStatus.Reconnecting;
			var delay = NextReconnectDelay();
			LogFailure(error ?? _lastError ?? "connection closed", everConnected, delay);
			await Task.Delay(delay, _cts.Token).ConfigureAwait(false);
		}
	}

	private void LogConnected()
	{
		if (_failures.RecordSuccess() is { } episode)
		{
			_logger.Information("OBS reconnected to {Url} after {Duration} and {Attempts} attempt(s)",
				_url,
				episode.Duration,
				episode.Failures);

			return;
		}

		_logger.Information("OBS connected to {Url}", _url);
	}

	private void LogFailure(string error, bool everConnected, TimeSpan delay)
	{
		var signal = _failures.RecordFailure(error);
		switch (signal.Kind)
		{
			case FailureEpisodeSignalKind.Onset when everConnected:
				_logger.Warning("OBS connection lost: {LastError}", error);

				break;
			case FailureEpisodeSignalKind.Onset:
				_logger.Information("OBS is not reachable at {Url}: {LastError}", _url, error);

				break;
			case FailureEpisodeSignalKind.SummaryDue:
				// The summary repeats for as long as the outage lasts, so it follows the same rule as the
				// onset it continues: a user who simply has not started OBS is not warned, every summary
				// interval, forever.
				_logger.Write(everConnected ? LogEventLevel.Warning : LogEventLevel.Information,
					"OBS has been unreachable for {Duration} ({Attempts} attempt(s)); last error: {LastError}",
					signal.Duration,
					signal.ConsecutiveFailures,
					error);

				break;
			default:
				_logger.Debug("OBS reconnect attempt {Attempts} failed: {LastError}; retrying in {Delay}s",
					signal.ConsecutiveFailures,
					error,
					delay.TotalSeconds);

				break;
		}
	}

	private TimeSpan NextReconnectDelay()
	{
		var failures = Math.Min(Interlocked.Increment(ref _reconnectFailures), 6);
		var seconds = _reconnectDelay.TotalSeconds * Math.Pow(2, failures - 1);

		return TimeSpan.FromSeconds(Math.Min(seconds, _maxReconnectDelay.TotalSeconds));
	}

	private void OnConnected(object? sender, EventArgs e)
	{
		if (_cts.IsCancellationRequested)
		{
			return;
		}

		Interlocked.Exchange(ref _disconnectedLogged, 0);
		_status = ObsConnectionStatus.Connected;
		var refresh = RefreshState();
		if (refresh == StateRefresh.Failed)
		{
			return;
		}

		StartPolling();
		_signals.Writer.TryWrite(ConnectionSignal.Established);
		if (refresh == StateRefresh.Published)
		{
			_onVariablesChanged?.Invoke();
		}
	}

	private void OnInputMuteChanged(object? sender, ObsInputMuteChange change)
	{
		ForgetTargetRead($"mute:{change.InputName}");
		lock (_publicationGate)
		{
			if (!_cts.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
			{
				_events?.PublishInputMuteChanged(change.InputName, change.Muted);
			}
		}
	}

	private void OnInputSettingsChanged(object? sender, ObsInputSettingsChange change)
		=> _changes.Add(new ObsTargetChange(ObsTargetKind.InputSettings, change.InputName, null, change.ChangedKeys));

	private void OnSourceFilterChanged(object? sender, ObsFilterChange change)
		=> _changes.Add(new ObsTargetChange(ObsTargetKind.Filter, change.SourceName, change.FilterName));

	private void OnInputActiveChanged(object? sender, ObsInputFlagChange change)
	{
		_changes.Add(new ObsTargetChange(ObsTargetKind.SourceActivity, change.InputName));
		PublishInputActivity(change.Value ? ObsEventIds.InputBecameActive : ObsEventIds.InputBecameInactive,
			change.InputName);
	}

	private void OnInputShowingChanged(object? sender, ObsInputFlagChange change)
	{
		_changes.Add(new ObsTargetChange(ObsTargetKind.SourceActivity, change.InputName));
		PublishInputActivity(change.Value ? ObsEventIds.InputStartedShowing : ObsEventIds.InputStoppedShowing,
			change.InputName);
	}

	private void PublishInputActivity(string eventId, string inputName)
	{
		lock (_publicationGate)
		{
			if (!_cts.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
			{
				_events?.PublishInputActivity(eventId, inputName);
			}
		}
	}

	private void OnCustomEventReceived(object? sender, string json)
	{
		try
		{
			if (!_customEventLimiter.TryAcquire(out var droppedBefore))
			{
				return;
			}

			if (droppedBefore > 0)
			{
				_logger.Warning("Dropped {Count} OBS custom events that arrived faster than the allowed rate",
					droppedBefore);
			}

			var payload = ObsCustomEvent.Parse(json);
			if (payload is null)
			{
				_logger.Debug("Ignored an OBS custom event that is oversized or not valid JSON");
				return;
			}

			lock (_publicationGate)
			{
				if (!_cts.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
				{
					_events?.PublishCustomEvent(payload);
				}
			}
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Failed to process an OBS custom event");
		}
	}

	private void FlushTargetChanges(IReadOnlyList<ObsTargetChange> changes)
	{
		try
		{
			if (_cts.IsCancellationRequested || Volatile.Read(ref _disposed) != 0)
			{
				return;
			}

			lock (_targetReadGate)
			{
				foreach (var change in changes)
				{
					_targetReads.Remove(change.Kind switch
					{
						ObsTargetKind.InputSettings => $"settings:{change.Name}",
						ObsTargetKind.SourceActivity => $"source-active:{change.Name}",
						_ => $"filter:{change.Name}\u0000{change.Child}"
					});
				}
			}

			_onTargetsChanged?.Invoke(changes);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Failed to apply OBS change notifications");
		}
	}

	private void OnReplayBufferSaved(object? sender, string path)
	{
		lock (_publicationGate)
		{
			if (!_cts.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
			{
				_events?.PublishReplayBufferSaved(path);
			}
		}
	}

	private void OnRecordFileChanged(object? sender, string path)
	{
		var published = false;
		lock (_publicationGate)
		{
			if (!_cts.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
			{
				_lastRecordingFilePath = path;
				_events?.PublishRecordFileChanged(path);
				published = true;
			}
		}

		if (published)
		{
			_onVariablesChanged?.Invoke();
		}
	}

	private void OnScreenshotSaved(object? sender, string path)
	{
		var published = false;
		lock (_publicationGate)
		{
			if (!_cts.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
			{
				_lastScreenshotPath = path;
				_events?.PublishScreenshotSaved(path);
				published = true;
			}
		}

		if (published)
		{
			_onVariablesChanged?.Invoke();
		}
	}

	private void OnDisconnected(object? sender, string? reason)
	{
		if (_cts.IsCancellationRequested)
		{
			return;
		}

		lock (_publicationGate)
		{
			if (_cts.IsCancellationRequested || Volatile.Read(ref _disposed) != 0)
			{
				return;
			}

			_state = ObsState.Disconnected;
			_status = ObsConnectionStatus.Reconnecting;
			_events?.Observe(_state);
			_events?.Reset();
		}

		_lastError = reason;
		if (Interlocked.Exchange(ref _disconnectedLogged, 1) == 0)
		{
			_logger.Debug("OBS disconnected: {Reason}", reason);
		}

		StopPolling();
		_onVariablesChanged?.Invoke();
		_signals.Writer.TryWrite(ConnectionSignal.Lost(reason));
	}

	private void OnStateChanged(object? sender, EventArgs e)
	{
		if (RefreshState() == StateRefresh.Published)
		{
			_onVariablesChanged?.Invoke();
		}
	}

	private StateRefresh RefreshState()
	{
		if (_cts.IsCancellationRequested || Volatile.Read(ref _disposed) != 0 || !_client.IsConnected)
		{
			return StateRefresh.Failed;
		}

		try
		{
			var state = ObsState.FromStatus(_client.QueryStatus(), _state, DateTime.UtcNow);
			lock (_publicationGate)
			{
				if (_cts.IsCancellationRequested || Volatile.Read(ref _disposed) != 0 || !_client.IsConnected)
				{
					return StateRefresh.Failed;
				}

				_state = state;
				_events?.Observe(state);
				return StateRefresh.Published;
			}
		}
		catch (ObsNotReadyException)
		{
			_logger.Debug("OBS is not ready yet; keeping the last published state");
			return StateRefresh.NotReady;
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Failed to read OBS state");
			return StateRefresh.Failed;
		}
	}

	private void StartPolling()
		=> _pollTimer ??= new Timer(_ => RefreshState(), null, _pollInterval, _pollInterval);

	private void StopPolling()
	{
		_pollTimer?.Dispose();
		_pollTimer = null;
	}

	private Task<bool> RunAsync(Action action) => Task.Run(() =>
	{
		if (!IsConnected)
		{
			return false;
		}

		try
		{
			action();
			return true;
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "OBS command failed");
			return false;
		}
	});

	private Task<ObsCommandResult> RunForResultAsync(Action action) => Task.Run(() =>
	{
		if (!IsConnected)
		{
			return ObsCommandResult.NotConnected;
		}

		try
		{
			action();
			return ObsCommandResult.Success;
		}
		catch (ObsRequestException ex) when (ex.Code == ObsRequestException.OutputNotRunning)
		{
			return ObsCommandResult.NotRecording;
		}
		catch (ObsRequestException ex)
		{
			_logger.Warning(ex, "OBS rejected a request with code {Code}", ex.Code);
			return ObsCommandResult.Rejected(ex.Message, ex.Code);
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "OBS command failed");
			return IsConnected ? ObsCommandResult.Rejected(ex.Message) : ObsCommandResult.NotConnected;
		}
	});

	private Task<ObsOutputOutcome> OutputCommandAsync(Action action) => Task.Run(() => RunOutputCommand(action));

	private ObsOutputOutcome RunOutputCommand(Action action)
	{
		if (!IsConnected)
		{
			return ObsOutputOutcome.NotConnected;
		}

		try
		{
			action();
			return ObsOutputOutcome.Done;
		}
		catch (ObsRequestUnsupportedException ex)
		{
			_logger.Warning(ex, "OBS output request unsupported");
			return ObsOutputOutcome.Unsupported;
		}
		catch (ObsOutputNotFoundException ex)
		{
			_logger.Warning(ex, "OBS output not found");
			return ObsOutputOutcome.NotFound;
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "OBS output request failed");
			return ObsOutputOutcome.Failed;
		}
	}

	private Task<IReadOnlyList<string>> QueryAsync(Func<IReadOnlyList<string>> query) => Task.Run(() =>
	{
		if (!IsConnected)
		{
			return (IReadOnlyList<string>)[];
		}

		try
		{
			return query();
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "OBS query failed");
			return [];
		}
	});

	private Task<T> QueryValueAsync<T>(Func<T> query, T fallback) => Task.Run(() =>
	{
		if (!IsConnected)
		{
			return fallback;
		}

		try
		{
			return query();
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "OBS query failed");
			return fallback;
		}
	});

	private static float ToMultiplier(double percent) => (float)(Math.Clamp(percent, 0d, 100d) / 100d);

	private enum StateRefresh
	{
		Published,
		NotReady,
		Failed
	}

	private readonly record struct ConnectionSignal(bool Connected, string? Reason)
	{
		public static ConnectionSignal Established { get; } = new(true, null);

		public static ConnectionSignal Lost(string? reason) => new(false, reason);
	}
}
