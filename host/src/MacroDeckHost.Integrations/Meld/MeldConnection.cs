using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Integrations.Meld.Protocol;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Meld;

internal sealed class MeldConnection : IDisposable
{
	private const string ObserverContext = MeldObjects.IntegrationId;

	private static readonly ILogger _logger = IntegrationLog.For<MeldConnection>(MeldObjects.IntegrationId);

	private static readonly TimeSpan _defaultMaxReconnectDelay = TimeSpan.FromMinutes(1);
	private static readonly TimeSpan _needsSetupReconnectDelayDefault = TimeSpan.FromMinutes(5);
	private static readonly TimeSpan _defaultConfirmationTimeout = TimeSpan.FromMilliseconds(1500);

	private static readonly TimeSpan _observerSyncDelay = TimeSpan.FromMilliseconds(500);

	private readonly Func<IQWebChannelClient> _clientFactory;
	private readonly Uri _endpoint;
	private readonly TimeSpan _reconnectDelay;
	private readonly TimeSpan _maxReconnectDelay;
	private readonly TimeSpan _confirmationTimeout;
	private readonly TimeSpan _needsSetupReconnectDelay;
	private readonly Action<MeldState>? _onState;
	private readonly Action? _onVariablesChanged;
	private readonly Action<string, string, bool>? _onTrackMute;
	private readonly Action? _onReset;

	private readonly ConcurrentDictionary<string, MeldGain> _gains = new(StringComparer.Ordinal);
	private readonly HashSet<string> _observedTracks = new(StringComparer.Ordinal);
	private readonly Lock _trackLock = new();
	private readonly SemaphoreSlim _observerSync = new(1, 1);
	private readonly CancellationTokenSource _cts = new();

	private volatile MeldState _state = MeldState.Disconnected;
	private volatile bool _needsSetup;
	private IQWebChannelClient? _client;
	private TaskCompletionSource _pulse = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private int _failures;
	private int _handshakeFailures;
	private int _observerSyncQueued;
	private int _hasLoggedDisconnection;
	private bool _disposed;

	internal MeldConnection(
		Func<IQWebChannelClient> clientFactory,
		Uri endpoint,
		TimeSpan? reconnectDelay = null,
		TimeSpan? maxReconnectDelay = null,
		TimeSpan? confirmationTimeout = null,
		Action<MeldState>? onState = null,
		Action<string, string, bool>? onTrackMute = null,
		Action? onReset = null,
		TimeSpan? needsSetupReconnectDelay = null,
		Action? onVariablesChanged = null)
	{
		_clientFactory = clientFactory;
		_endpoint = endpoint;
		_reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(5);
		_maxReconnectDelay = maxReconnectDelay ?? _defaultMaxReconnectDelay;
		_confirmationTimeout = confirmationTimeout ?? _defaultConfirmationTimeout;
		_onState = onState;
		_onVariablesChanged = onVariablesChanged;
		_onTrackMute = onTrackMute;
		_onReset = onReset;
		_needsSetupReconnectDelay = needsSetupReconnectDelay ?? _needsSetupReconnectDelayDefault;
	}

	public MeldState State => _state;

	public bool IsConnected => _state.IsConnected;

	public bool NeedsSetup => _needsSetup;

	public void Start()
	{
		_ = Task.Run(() => PumpAsync(_cts.Token), CancellationToken.None);
	}

	public bool TryGetGain(string trackId, out MeldGain gain) => _gains.TryGetValue(trackId, out gain);

	public async Task<ActionResult> ShowSceneAsync(string sceneId, CancellationToken cancellationToken = default)
	{
		var (state, client) = Snapshot();
		if (!state.IsConnected || client is null)
		{
			return NotConnected();
		}

		if (!state.Session.ScenesById.ContainsKey(sceneId))
		{
			return NotFound(AppStrings.Integrations.Meld.Parameters.Scene(), sceneId);
		}

		await client.InvokeAsync(MeldObjects.Object, MeldObjects.ShowScene, [sceneId], cancellationToken)
			.ConfigureAwait(false);
		return await ConfirmAsync(s => s.Session.CurrentSceneId == sceneId, cancellationToken).ConfigureAwait(false);
	}

	public async Task<ActionResult> StageSceneAsync(string sceneId, CancellationToken cancellationToken = default)
	{
		var (state, client) = Snapshot();
		if (!state.IsConnected || client is null)
		{
			return NotConnected();
		}

		if (!state.Session.ScenesById.ContainsKey(sceneId))
		{
			return NotFound(AppStrings.Integrations.Meld.Parameters.Scene(), sceneId);
		}

		await client.InvokeAsync(MeldObjects.Object, MeldObjects.SetStagedScene, [sceneId], cancellationToken)
			.ConfigureAwait(false);
		return await ConfirmAsync(s => s.Session.StagedSceneId == sceneId, cancellationToken).ConfigureAwait(false);
	}

	public async Task<ActionResult> ShowStagedSceneAsync(CancellationToken cancellationToken = default)
	{
		var (state, client) = Snapshot();
		if (!state.IsConnected || client is null)
		{
			return NotConnected();
		}

		var stagedSceneId = state.Session.StagedSceneId;
		if (stagedSceneId is null)
		{
			return ActionResult.Failed(ActionErrorCodes.Unavailable,
				AppStrings.Integrations.Meld.Errors.NoSceneStaged());
		}

		await client.InvokeAsync(MeldObjects.Object, MeldObjects.ShowStagedScene, [], cancellationToken)
			.ConfigureAwait(false);

		return await ConfirmAsync(s => s.Session.CurrentSceneId == stagedSceneId, cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task<ActionResult> SetLayerVisibleAsync(
		string layerId,
		bool? target,
		CancellationToken cancellationToken = default)
	{
		var (state, client) = Snapshot();
		if (!state.IsConnected || client is null)
		{
			return NotConnected();
		}

		if (!state.Session.LayersById.TryGetValue(layerId, out var layer))
		{
			return NotFound(AppStrings.Integrations.Meld.Parameters.Layer(), layerId);
		}

		var desired = target ?? !layer.Visible;
		if (desired == layer.Visible)
		{
			return ActionResult.Success();
		}

		await client
			.InvokeAsync(MeldObjects.Object, MeldObjects.ToggleLayer, [layer.SceneId, layerId], cancellationToken)
			.ConfigureAwait(false);
		return await ConfirmAsync(
				s => s.Session.LayersById.TryGetValue(layerId, out var updated) && updated.Visible == desired,
				cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task<ActionResult> SetEffectEnabledAsync(
		string effectId,
		bool? target,
		CancellationToken cancellationToken = default)
	{
		var (state, client) = Snapshot();
		if (!state.IsConnected || client is null)
		{
			return NotConnected();
		}

		if (!state.Session.EffectsById.TryGetValue(effectId, out var effect))
		{
			return NotFound(AppStrings.Integrations.Meld.Parameters.Effect(), effectId);
		}

		var desired = target ?? !effect.Enabled;
		if (desired == effect.Enabled)
		{
			return ActionResult.Success();
		}

		await client
			.InvokeAsync(MeldObjects.Object,
				MeldObjects.ToggleEffect,
				[effect.SceneId, effect.LayerId, effectId],
				cancellationToken)
			.ConfigureAwait(false);
		return await ConfirmAsync(
				s => s.Session.EffectsById.TryGetValue(effectId, out var updated) && updated.Enabled == desired,
				cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task<ActionResult> SetTrackMutedAsync(
		string trackId,
		bool? target,
		CancellationToken cancellationToken = default)
	{
		var (state, client) = Snapshot();
		if (!state.IsConnected || client is null)
		{
			return NotConnected();
		}

		if (!state.Session.TracksById.ContainsKey(trackId))
		{
			return NotFound(AppStrings.Integrations.Meld.Parameters.Track(), trackId);
		}

		var current = CurrentMuted(trackId, state);
		var desired = target ?? !current;
		if (desired == current)
		{
			return ActionResult.Success();
		}

		if (state.SupportsSetMuted)
		{
			await client
				.InvokeAsync(MeldObjects.Object, MeldObjects.SetMuted, [trackId, desired], cancellationToken)
				.ConfigureAwait(false);
		}
		else if (state.SupportsSetProperty)
		{
			await client
				.InvokeAsync(MeldObjects.Object,
					MeldObjects.SetProperty,
					[trackId, MeldObjects.PropertyMuted, desired],
					cancellationToken)
				.ConfigureAwait(false);
		}
		else
		{
			await client.InvokeAsync(MeldObjects.Object, MeldObjects.ToggleMute, [trackId], cancellationToken)
				.ConfigureAwait(false);
		}

		return await ConfirmAsync(s => CurrentMuted(trackId, s) == desired, cancellationToken).ConfigureAwait(false);
	}

	public async Task<ActionResult> SetTrackMonitoringAsync(
		string trackId,
		bool? target,
		CancellationToken cancellationToken = default)
	{
		var (state, client) = Snapshot();
		if (!state.IsConnected || client is null)
		{
			return NotConnected();
		}

		if (!state.Session.TracksById.TryGetValue(trackId, out var track))
		{
			return NotFound(AppStrings.Integrations.Meld.Parameters.Track(), trackId);
		}

		var desired = target ?? !track.Monitoring;
		if (desired == track.Monitoring)
		{
			return ActionResult.Success();
		}

		if (state.SupportsSetProperty)
		{
			await client
				.InvokeAsync(MeldObjects.Object,
					MeldObjects.SetProperty,
					[trackId, MeldObjects.PropertyMonitoring, desired],
					cancellationToken)
				.ConfigureAwait(false);
		}
		else
		{
			await client.InvokeAsync(MeldObjects.Object, MeldObjects.ToggleMonitor, [trackId], cancellationToken)
				.ConfigureAwait(false);
		}

		return await ConfirmAsync(s =>
					s.Session.TracksById.TryGetValue(trackId, out var updated) && updated.Monitoring == desired,
				cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task<ActionResult> SetGainAsync(
		string trackId,
		double gain01,
		CancellationToken cancellationToken = default)
	{
		var (state, client) = Snapshot();
		if (!state.IsConnected || client is null)
		{
			return NotConnected();
		}

		if (!state.Session.TracksById.ContainsKey(trackId))
		{
			return NotFound(AppStrings.Integrations.Meld.Parameters.Track(), trackId);
		}

		await client.InvokeAsync(MeldObjects.Object, MeldObjects.SetGain, [trackId, gain01], cancellationToken)
			.ConfigureAwait(false);
		return await ConfirmAsync(
				_ => _gains.TryGetValue(trackId, out var gain) && Math.Abs(gain.Gain - gain01) < 0.001,
				cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task<ActionResult> SendCommandAsync(string command, CancellationToken cancellationToken = default)
	{
		var (state, client) = Snapshot();
		if (!state.IsConnected || client is null)
		{
			return NotConnected();
		}

		await client.InvokeAsync(MeldObjects.Object, MeldObjects.SendCommand, [command], cancellationToken)
			.ConfigureAwait(false);

		switch (command)
		{
			case MeldObjects.CommandStartStreaming:
				return await ConfirmAsync(s => s.IsStreaming, cancellationToken).ConfigureAwait(false);
			case MeldObjects.CommandStopStreaming:
				return await ConfirmAsync(s => !s.IsStreaming, cancellationToken).ConfigureAwait(false);
			case MeldObjects.CommandToggleStreaming:
				return await ConfirmAsync(s => s.IsStreaming != state.IsStreaming, cancellationToken)
					.ConfigureAwait(false);
			case MeldObjects.CommandStartRecording:
				return await ConfirmAsync(s => s.IsRecording, cancellationToken).ConfigureAwait(false);
			case MeldObjects.CommandStopRecording:
				return await ConfirmAsync(s => !s.IsRecording, cancellationToken).ConfigureAwait(false);
			case MeldObjects.CommandToggleRecording:
				return await ConfirmAsync(s => s.IsRecording != state.IsRecording, cancellationToken)
					.ConfigureAwait(false);
			default:
				return ActionResult.Accepted();
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_cts.Cancel();
		_client?.Dispose();
		_client = null;
		_state = MeldState.Disconnected;
		_observerSync.Dispose();
		_cts.Dispose();
	}

	private (MeldState State, IQWebChannelClient? Client) Snapshot() => (_state, _client);

	private static ActionResult NotConnected()
		=> ActionResult.Failed(ActionErrorCodes.NotConnected, AppStrings.Integrations.Meld.Errors.NotConnected());

	private static ActionResult NotFound(LocalizedText kindNoun, string id)
		=> ActionResult.Failed(ActionErrorCodes.NotFound,
			AppStrings.Integrations.Meld.Errors.TargetNoLongerInSession(kind: kindNoun, id: id));

	private bool CurrentMuted(string trackId, MeldState state)
	{
		if (_gains.TryGetValue(trackId, out var gain))
		{
			return gain.Muted;
		}

		return state.Session.TracksById.TryGetValue(trackId, out var track) && track.Muted;
	}

	private async Task PumpAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await RunSessionAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (QWebChannelHandshakeException ex)
			{
				var failures = Interlocked.Increment(ref _handshakeFailures);
				_logger.Debug(ex, "Meld Studio handshake failed ({Count} consecutive)", failures);
				if (failures >= 3)
				{
					_needsSetup = true;
				}
			}
			catch (Exception ex)
			{
				Interlocked.Exchange(ref _handshakeFailures, 0);
				_logger.Debug(ex, "Meld Studio session ended");
			}

			if (cancellationToken.IsCancellationRequested)
			{
				return;
			}

			try
			{
				await Task.Delay(NextReconnectDelay(), cancellationToken).ConfigureAwait(false);
			}
			catch (Exception)
			{
				return;
			}
		}
	}

	private async Task RunSessionAsync(CancellationToken cancellationToken)
	{
		var client = _clientFactory();
		var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		void OnDisconnected(object? sender, string? reason) => closed.TrySetResult();

		client.PropertyUpdated += OnPropertyUpdated;
		client.SignalReceived += OnSignalReceived;
		client.Disconnected += OnDisconnected;
		_client = client;

		var announced = false;
		try
		{
			var objects = await client.ConnectAsync(_endpoint, cancellationToken).ConfigureAwait(false);
			if (!objects.TryGetValue(MeldObjects.Object, out var info))
			{
				throw new QWebChannelHandshakeException(
					$"{_endpoint} does not expose a '{MeldObjects.Object}' QWebChannel object.");
			}

			var apiVersion = ReadVersion(info.InitialProperties);
			var supportsSetMuted = info.Methods.ContainsKey(MeldObjects.SetMuted);
			var supportsSetProperty = apiVersion >= 2 && info.Methods.ContainsKey(MeldObjects.SetProperty);

			await client.ConnectToSignalAsync(MeldObjects.Object, MeldObjects.GainUpdatedSignal, cancellationToken)
				.ConfigureAwait(false);

			var session = info.InitialProperties.TryGetValue(MeldObjects.SessionProperty, out var sessionElement)
				? MeldSessionParser.Parse(sessionElement)
				: MeldSession.Empty;

			_state = new MeldState
			{
				IsConnected = true,
				ApiVersion = apiVersion,
				SupportsSetMuted = supportsSetMuted,
				SupportsSetProperty = supportsSetProperty,
				IsStreaming = ReadBool(info.InitialProperties, MeldObjects.IsStreamingProperty),
				IsRecording = ReadBool(info.InitialProperties, MeldObjects.IsRecordingProperty),
				Session = session
			};

			Interlocked.Exchange(ref _failures, 0);
			Interlocked.Exchange(ref _handshakeFailures, 0);
			Interlocked.Exchange(ref _hasLoggedDisconnection, 0);
			_needsSetup = false;

			announced = true;
			_onState?.Invoke(_state);
			_onVariablesChanged?.Invoke();
			Pulse();
			_logger.Information("Connected to Meld Studio (API v{Version}) at {Endpoint}", apiVersion, _endpoint);

			ScheduleObserverSync();

			await closed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			client.PropertyUpdated -= OnPropertyUpdated;
			client.SignalReceived -= OnSignalReceived;
			client.Disconnected -= OnDisconnected;
			_client = null;

			// Drop the caches before publishing the disconnected state, not after: IsConnected going
			// false is the signal everything else keys off, and a reader that acts on it must not still
			// find a stale gain or a track observer that died with the socket.
			_gains.Clear();
			lock (_trackLock)
			{
				_observedTracks.Clear();
			}

			_state = MeldState.Disconnected;
			_onState?.Invoke(_state);
			_onVariablesChanged?.Invoke();
			Pulse();
			_onReset?.Invoke();

			if (announced && Interlocked.Exchange(ref _hasLoggedDisconnection, 1) == 0)
			{
				_logger.Information("Meld Studio disconnected; retrying");
			}

			try
			{
				await client.DisconnectAsync().ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Error while closing the Meld Studio session");
			}

			client.Dispose();
		}
	}

	private void OnPropertyUpdated(object? sender, QWebChannelPropertyUpdate update)
	{
		if (!string.Equals(update.Object, MeldObjects.Object, StringComparison.Ordinal))
		{
			return;
		}

		if (!ReferenceEquals(sender, _client))
		{
			return;
		}

		var current = _state;
		if (!current.IsConnected)
		{
			return;
		}

		var session = current.Session;
		var sessionChanged = false;
		if (update.Properties.TryGetValue(MeldObjects.SessionProperty, out var sessionElement))
		{
			session = MeldSessionParser.Parse(sessionElement);
			sessionChanged = true;
		}

		var isStreaming = update.Properties.TryGetValue(MeldObjects.IsStreamingProperty, out var streamingElement)
			? ReadBoolValue(streamingElement, current.IsStreaming)
			: current.IsStreaming;
		var isRecording = update.Properties.TryGetValue(MeldObjects.IsRecordingProperty, out var recordingElement)
			? ReadBoolValue(recordingElement, current.IsRecording)
			: current.IsRecording;

		_state = current with { Session = session, IsStreaming = isStreaming, IsRecording = isRecording };
		_onState?.Invoke(_state);
		_onVariablesChanged?.Invoke();
		Pulse();

		if (sessionChanged)
		{
			ScheduleObserverSync();
		}
	}

	private void OnSignalReceived(object? sender, QWebChannelSignalMessage message)
	{
		if (!string.Equals(message.Object, MeldObjects.Object, StringComparison.Ordinal) ||
			!string.Equals(message.Signal, MeldObjects.GainUpdatedSignal, StringComparison.Ordinal) ||
			message.Args.Count < 3)
		{
			return;
		}

		if (message.Args[0].ValueKind != JsonValueKind.String || message.Args[0].GetString() is not { } trackId)
		{
			return;
		}

		var gain = message.Args[1].ValueKind == JsonValueKind.Number ? message.Args[1].GetDouble() : 0d;
		var muted = message.Args[2].ValueKind == JsonValueKind.True;

		_gains[trackId] = new MeldGain(gain, muted);
		Pulse();

		var name = _state.Session.TracksById.TryGetValue(trackId, out var track) ? track.Name : trackId;
		_onTrackMute?.Invoke(trackId, name, muted);
	}

	private void ScheduleObserverSync()
	{
		if (Interlocked.Exchange(ref _observerSyncQueued, 1) == 1)
		{
			return;
		}

		_ = Task.Run(async () =>
		{
			try
			{
				try
				{
					await Task.Delay(_observerSyncDelay, _cts.Token).ConfigureAwait(false);
				}
				finally
				{
					Interlocked.Exchange(ref _observerSyncQueued, 0);
				}

				await ReconcileObserversAsync(_cts.Token).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Meld Studio track observer sync failed");
			}
		});
	}

	private async Task ReconcileObserversAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		if (client is null || !client.IsConnected)
		{
			return;
		}

		await _observerSync.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var desired = _state.Session.TracksById.Keys.ToHashSet(StringComparer.Ordinal);
			HashSet<string> toRegister;
			HashSet<string> toUnregister;
			lock (_trackLock)
			{
				toRegister = desired.Except(_observedTracks).ToHashSet(StringComparer.Ordinal);
				toUnregister = _observedTracks.Except(desired).ToHashSet(StringComparer.Ordinal);
			}

			foreach (var trackId in toRegister)
			{
				try
				{
					await client
						.InvokeAsync(MeldObjects.Object,
							MeldObjects.RegisterTrackObserver,
							[ObserverContext, trackId],
							cancellationToken)
						.ConfigureAwait(false);
					lock (_trackLock)
					{
						_observedTracks.Add(trackId);
					}
				}
				catch (Exception ex)
				{
					_logger.Debug(ex, "Failed to register a Meld Studio track observer for {TrackId}", trackId);
				}
			}

			foreach (var trackId in toUnregister)
			{
				try
				{
					await client
						.InvokeAsync(MeldObjects.Object,
							MeldObjects.UnregisterTrackObserver,
							[ObserverContext, trackId],
							cancellationToken)
						.ConfigureAwait(false);
					lock (_trackLock)
					{
						_observedTracks.Remove(trackId);
					}

					_gains.TryRemove(trackId, out _);
				}
				catch (Exception ex)
				{
					_logger.Debug(ex, "Failed to unregister a Meld Studio track observer for {TrackId}", trackId);
				}
			}
		}
		finally
		{
			_observerSync.Release();
		}
	}

	private async Task<ActionResult> ConfirmAsync(Func<MeldState, bool> reached, CancellationToken cancellationToken)
	{
		var deadline = DateTime.UtcNow + _confirmationTimeout;
		while (true)
		{
			var pulse = Volatile.Read(ref _pulse).Task;

			if (reached(_state))
			{
				return ActionResult.Success();
			}

			var remaining = deadline - DateTime.UtcNow;
			if (remaining <= TimeSpan.Zero)
			{
				return ActionResult.Accepted();
			}

			try
			{
				await pulse.WaitAsync(remaining, cancellationToken).ConfigureAwait(false);
			}
			catch (TimeoutException)
			{
				return reached(_state) ? ActionResult.Success() : ActionResult.Accepted();
			}
		}
	}

	private void Pulse()
	{
		var previous = Interlocked.Exchange(ref _pulse,
			new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
		previous.TrySetResult();
	}

	private TimeSpan NextReconnectDelay()
	{
		var failures = Math.Min(Interlocked.Increment(ref _failures), 6);
		if (_needsSetup)
		{
			return _needsSetupReconnectDelay;
		}

		var seconds = _reconnectDelay.TotalSeconds * Math.Pow(2, failures - 1);
		return TimeSpan.FromSeconds(Math.Min(seconds, _maxReconnectDelay.TotalSeconds));
	}

	private static int ReadVersion(IReadOnlyDictionary<string, JsonElement> initialProperties)
	{
		if (!initialProperties.TryGetValue(MeldObjects.VersionProperty, out var element))
		{
			return 1;
		}

		if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var version))
		{
			return version;
		}

		if (element.ValueKind == JsonValueKind.String &&
			int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
		{
			return parsed;
		}

		return 1;
	}

	private static bool ReadBool(IReadOnlyDictionary<string, JsonElement> properties, string name)
		=> properties.TryGetValue(name, out var element) && element.ValueKind == JsonValueKind.True;

	private static bool ReadBoolValue(JsonElement element, bool fallback)
		=> element.ValueKind switch
		{
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			_ => fallback
		};
}
