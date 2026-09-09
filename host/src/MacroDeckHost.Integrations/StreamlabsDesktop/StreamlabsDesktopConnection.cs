using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Integrations.StreamlabsDesktop.Protocol;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.StreamlabsDesktop;

internal sealed class StreamlabsDesktopConnection : IDisposable
{
	private static readonly TimeSpan _intentWindow = TimeSpan.FromSeconds(10);

	private static readonly TimeSpan _maximumReconnectDelay = TimeSpan.FromSeconds(60);

	private readonly Func<IStreamlabsClient> _clientFactory;
	private readonly StreamlabsDesktopEndpoint _endpoint;
	private readonly string _token;
	private readonly StreamlabsDesktopEventEmitter? _events;
	private readonly Action? _onVariablesChanged;
	private readonly TimeSpan _reconnectDelay;
	private readonly ILogger _logger;
	private readonly CancellationTokenSource _cts = new();

	private readonly SemaphoreSlim _streamingGate = new(1, 1);
	private readonly SemaphoreSlim _recordingGate = new(1, 1);
	private readonly SemaphoreSlim _replayGate = new(1, 1);

	private readonly object _stateGate = new();
	private readonly object _intentGate = new();
	private readonly HashSet<string> _pushedFields = new(StringComparer.Ordinal);

	private readonly StreamlabsDesktopCatalog _catalog;

	private IStreamlabsClient? _client;
	private Task? _pump;
	private StreamlabsDesktopState _state = StreamlabsDesktopState.Disconnected;
	private Intent<StreamlabsStreamingState> _streamingIntent;
	private Intent<StreamlabsRecordingState> _recordingIntent;
	private int _failures;
	private bool _announcedConnected;
	private bool _disposed;

	internal StreamlabsDesktopConnection(
		Func<IStreamlabsClient> clientFactory,
		StreamlabsDesktopEndpoint endpoint,
		string token,
		StreamlabsDesktopEventEmitter? events = null,
		TimeSpan? reconnectDelay = null,
		ILogger? logger = null,
		Action? onVariablesChanged = null)
	{
		_clientFactory = clientFactory;
		_endpoint = endpoint;
		_token = token;
		_events = events;
		_onVariablesChanged = onVariablesChanged;
		_reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(5);
		_logger = logger ?? IntegrationLog.For<StreamlabsDesktopConnection>(StreamlabsDesktopIntegration.IntegrationId);
		_catalog = new StreamlabsDesktopCatalog(() => _client);
	}

	public StreamlabsDesktopState State
	{
		get
		{
			lock (_stateGate)
			{
				return _state;
			}
		}
	}

	public bool NeedsAuthorization { get; private set; }

	public bool IsConnected => _client?.IsConnected == true;

	public void Start()
	{
		_logger.Information("Streamlabs Desktop connecting to {Address}", _endpoint.DisplayAddress);
		_pump = Task.Run(() => PumpAsync(_cts.Token), CancellationToken.None);
	}

	public Task<IReadOnlyList<string>> GetSceneNamesAsync() => _catalog.GetSceneNamesAsync();

	public Task<IReadOnlyList<string>> GetSceneItemNamesAsync(string sceneName)
		=> _catalog.GetSceneItemNamesAsync(sceneName);

	public Task<IReadOnlyList<string>> GetAudioSourceNamesAsync() => _catalog.GetAudioSourceNamesAsync();

	public async Task<StreamlabsCommandResult> SetSceneAsync(string sceneName)
	{
		if (_client is null)
		{
			return StreamlabsCommandResult.NotConnected;
		}

		var scene = await _catalog.ResolveSceneAsync(sceneName).ConfigureAwait(false);
		if (scene is null)
		{
			return StreamlabsCommandResult.NotFound(string.Create(CultureInfo.InvariantCulture,
				$"Streamlabs Desktop has no scene called '{sceneName}'."));
		}

		return await InvokeAsync(StreamlabsServices.Scenes, StreamlabsServices.MakeSceneActive, [scene.Id])
			.ConfigureAwait(false);
	}

	public Task<StreamlabsCommandResult> StartStreamingAsync() => SetStreamingAsync(live: true);

	public Task<StreamlabsCommandResult> StopStreamingAsync() => SetStreamingAsync(live: false);

	public async Task<StreamlabsCommandResult> ToggleStreamingAsync()
	{
		await _streamingGate.WaitAsync(_cts.Token).ConfigureAwait(false);
		try
		{
			var result = await InvokeAsync(StreamlabsServices.Streaming, StreamlabsServices.ToggleStreaming)
				.ConfigureAwait(false);
			if (result.Succeeded)
			{
				SetStreamingIntent(State.IsStreaming
					? StreamlabsStreamingState.Offline
					: StreamlabsStreamingState.Live);
			}

			return result;
		}
		finally
		{
			_streamingGate.Release();
		}
	}

	public Task<StreamlabsCommandResult> StartRecordingAsync() => SetRecordingAsync(recording: true);

	public Task<StreamlabsCommandResult> StopRecordingAsync() => SetRecordingAsync(recording: false);

	public async Task<StreamlabsCommandResult> ToggleRecordingAsync()
	{
		await _recordingGate.WaitAsync(_cts.Token).ConfigureAwait(false);
		try
		{
			var result = await InvokeAsync(StreamlabsServices.Streaming, StreamlabsServices.ToggleRecording)
				.ConfigureAwait(false);
			if (result.Succeeded)
			{
				SetRecordingIntent(State.IsRecording
					? StreamlabsRecordingState.Offline
					: StreamlabsRecordingState.Recording);
			}

			return result;
		}
		finally
		{
			_recordingGate.Release();
		}
	}

	public Task<StreamlabsCommandResult> StartReplayBufferAsync()
		=> InvokeAsync(StreamlabsServices.Streaming, StreamlabsServices.StartReplayBuffer);

	public Task<StreamlabsCommandResult> StopReplayBufferAsync()
		=> InvokeAsync(StreamlabsServices.Streaming, StreamlabsServices.StopReplayBuffer);

	public async Task<StreamlabsCommandResult> ToggleReplayBufferAsync()
	{
		await _replayGate.WaitAsync(_cts.Token).ConfigureAwait(false);
		try
		{
			var model = await ReadStreamingModelAsync().ConfigureAwait(false);
			if (model is null)
			{
				return StreamlabsCommandResult.NotConnected;
			}

			var active = model.ReplayBuffer is StreamlabsReplayBufferState.Running
				or StreamlabsReplayBufferState.Saving;

			return await InvokeAsync(StreamlabsServices.Streaming,
					active ? StreamlabsServices.StopReplayBuffer : StreamlabsServices.StartReplayBuffer)
				.ConfigureAwait(false);
		}
		finally
		{
			_replayGate.Release();
		}
	}

	public Task<StreamlabsCommandResult> SaveReplayAsync()
		=> InvokeAsync(StreamlabsServices.Streaming, StreamlabsServices.SaveReplay);

	public Task<StreamlabsCommandResult> SetStudioModeAsync(bool enabled)
		=> InvokeAsync(StreamlabsServices.Transitions,
			enabled ? StreamlabsServices.EnableStudioMode : StreamlabsServices.DisableStudioMode);

	public Task<StreamlabsCommandResult> ToggleStudioModeAsync() => SetStudioModeAsync(!State.StudioModeActive);

	public Task<StreamlabsCommandResult> ExecuteStudioModeTransitionAsync()
		=> InvokeAsync(StreamlabsServices.Transitions, StreamlabsServices.ExecuteStudioModeTransition);

	public async Task<StreamlabsCommandResult> SetSceneItemVisibleAsync(
		string sceneName,
		string sourceName,
		bool? visible)
	{
		if (_client is null)
		{
			return StreamlabsCommandResult.NotConnected;
		}

		var items = await _catalog.ResolveSceneItemsAsync(sceneName, sourceName).ConfigureAwait(false);
		if (items.Count == 0)
		{
			return NoSuchSource(sceneName, sourceName);
		}

		var target = visible ?? !items[0].Visible;
		foreach (var item in items)
		{
			var result = await InvokeAsync(item.ResourceId, StreamlabsServices.SetVisibility, [target])
				.ConfigureAwait(false);
			if (!result.Succeeded)
			{
				return result;
			}
		}

		return StreamlabsCommandResult.Ok;
	}

	public async Task<bool?> GetSceneItemVisibleAsync(string sceneName, string sourceName)
	{
		if (_client is null)
		{
			return null;
		}

		var items = await _catalog.ResolveSceneItemsAsync(sceneName, sourceName).ConfigureAwait(false);
		return items.Count == 0 ? null : items[0].Visible;
	}

	public async Task<StreamlabsCommandResult> SetAudioMutedAsync(string sourceName, bool? muted)
	{
		var source = await ResolveAudioAsync(sourceName).ConfigureAwait(false);
		if (source is null)
		{
			return _client is null ? StreamlabsCommandResult.NotConnected : NoSuchAudioSource(sourceName);
		}

		return await InvokeAsync(source.ResourceId, StreamlabsServices.SetMuted, [muted ?? !source.Muted])
			.ConfigureAwait(false);
	}

	public async Task<bool?> GetAudioMutedAsync(string sourceName)
	{
		var source = await ResolveAudioAsync(sourceName).ConfigureAwait(false);
		if (source is null)
		{
			return null;
		}

		var model = await ReadAudioModelAsync(source).ConfigureAwait(false);
		return model?.Muted ?? source.Muted;
	}

	public async Task<double?> GetAudioVolumePercentAsync(string sourceName)
	{
		var source = await ResolveAudioAsync(sourceName).ConfigureAwait(false);
		if (source is null)
		{
			return null;
		}

		var model = await ReadAudioModelAsync(source).ConfigureAwait(false);
		return (model?.Deflection ?? source.Deflection) * 100d;
	}

	public async Task<StreamlabsCommandResult> SetAudioVolumePercentAsync(string sourceName, double percent)
	{
		var source = await ResolveAudioAsync(sourceName).ConfigureAwait(false);
		if (source is null)
		{
			return _client is null ? StreamlabsCommandResult.NotConnected : NoSuchAudioSource(sourceName);
		}

		return await InvokeAsync(source.ResourceId, StreamlabsServices.SetDeflection, [ToDeflection(percent)])
			.ConfigureAwait(false);
	}

	public async Task<StreamlabsCommandResult> AdjustAudioVolumePercentAsync(string sourceName, double deltaPercent)
	{
		var source = await ResolveAudioAsync(sourceName).ConfigureAwait(false);
		if (source is null)
		{
			return _client is null ? StreamlabsCommandResult.NotConnected : NoSuchAudioSource(sourceName);
		}

		var model = await ReadAudioModelAsync(source).ConfigureAwait(false);
		var current = (model?.Deflection ?? source.Deflection) * 100d;

		return await InvokeAsync(source.ResourceId,
				StreamlabsServices.SetDeflection,
				[ToDeflection(current + deltaPercent)])
			.ConfigureAwait(false);
	}

	public async Task<StreamlabsCommandResult> RunSceneItemCommandAsync(
		string sceneName,
		string sourceName,
		string method,
		double? degrees)
	{
		if (_client is null)
		{
			return StreamlabsCommandResult.NotConnected;
		}

		var items = await _catalog.ResolveSceneItemsAsync(sceneName, sourceName).ConfigureAwait(false);
		if (items.Count == 0)
		{
			return NoSuchSource(sceneName, sourceName);
		}

		IReadOnlyList<object?>? args = degrees is { } value ? [value] : null;
		foreach (var item in items)
		{
			var result = await InvokeAsync(item.ResourceId, method, args).ConfigureAwait(false);
			if (!result.Succeeded)
			{
				return result;
			}
		}

		return StreamlabsCommandResult.Ok;
	}

	public async Task StopAsync()
	{
		if (_disposed)
		{
			return;
		}

		await _cts.CancelAsync().ConfigureAwait(false);

		if (_pump is { } pump)
		{
			try
			{
				await pump.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "The Streamlabs Desktop pump ended with an error");
			}
		}

		Dispose();
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
		_catalog.Clear();
		SetState(StreamlabsDesktopState.Disconnected);

		_streamingGate.Dispose();
		_recordingGate.Dispose();
		_replayGate.Dispose();
		_cts.Dispose();
	}

	private static double ToDeflection(double percent) => Math.Clamp(percent, 0d, 100d) / 100d;

	private static StreamlabsCommandResult NoSuchSource(string sceneName, string sourceName)
		=> StreamlabsCommandResult.NotFound(string.Create(CultureInfo.InvariantCulture,
			$"Scene '{sceneName}' has no source called '{sourceName}'."));

	private static StreamlabsCommandResult NoSuchAudioSource(string sourceName)
		=> StreamlabsCommandResult.NotFound(string.Create(CultureInfo.InvariantCulture,
			$"Streamlabs Desktop has no audio source called '{sourceName}'."));

	private async Task<StreamlabsAudioSource?> ResolveAudioAsync(string sourceName)
		=> _client is null ? null : await _catalog.ResolveAudioSourceAsync(sourceName).ConfigureAwait(false);

	private async Task<StreamlabsAudioSource?> ReadAudioModelAsync(StreamlabsAudioSource source)
	{
		if (_client is not { } client)
		{
			return null;
		}

		try
		{
			var response = await client
				.InvokeAsync(source.ResourceId, StreamlabsServices.GetModel, null, _cts.Token)
				.ConfigureAwait(false);
			return StreamlabsModelReader.ReadAudioSource(response);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Could not read the Streamlabs audio source {Source}", source.Name);
			return null;
		}
	}

	private async Task<StreamlabsCommandResult> InvokeAsync(
		string resource,
		string method,
		IReadOnlyList<object?>? args = null)
	{
		if (_client is not { } client)
		{
			return StreamlabsCommandResult.NotConnected;
		}

		try
		{
			await client.InvokeAsync(resource, method, args, _cts.Token).ConfigureAwait(false);
			return StreamlabsCommandResult.Ok;
		}
		catch (StreamlabsRpcException ex)
		{
			_logger.Warning(ex, "Streamlabs Desktop refused {Resource}.{Method}", resource, method);
			return StreamlabsCommandResult.Rejected(ex.Message);
		}
		catch (OperationCanceledException)
		{
			return StreamlabsCommandResult.NotConnected;
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Streamlabs Desktop command {Resource}.{Method} failed", resource, method);
			return StreamlabsCommandResult.Rejected(ex.Message);
		}
	}

	private async Task<StreamlabsStreamingModel?> ReadStreamingModelAsync()
	{
		if (_client is not { } client)
		{
			return null;
		}

		try
		{
			var response = await client
				.InvokeAsync(StreamlabsServices.Streaming, StreamlabsServices.GetModel, null, _cts.Token)
				.ConfigureAwait(false);
			return StreamlabsModelReader.ReadStreamingModel(response);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Could not read the Streamlabs streaming state");
			return null;
		}
	}

	private async Task<StreamlabsCommandResult> SetStreamingAsync(bool live)
	{
		await _streamingGate.WaitAsync(_cts.Token).ConfigureAwait(false);
		try
		{
			var model = await ReadStreamingModelAsync().ConfigureAwait(false);
			if (model is null)
			{
				return StreamlabsCommandResult.NotConnected;
			}

			var effective = StreamingIntent ?? model.Streaming;
			var isLive = effective is StreamlabsStreamingState.Live or StreamlabsStreamingState.Reconnecting;

			if (isLive == live)
			{
				return StreamlabsCommandResult.Ok;
			}

			if (effective is StreamlabsStreamingState.Starting or StreamlabsStreamingState.Ending)
			{
				_logger.Information("Streamlabs Desktop stream is {State}; the request was ignored",
					StreamlabsModelReader.ToWireString(effective));
				return StreamlabsCommandResult.Ok;
			}

			var result = await InvokeAsync(StreamlabsServices.Streaming, StreamlabsServices.ToggleStreaming)
				.ConfigureAwait(false);
			if (result.Succeeded)
			{
				SetStreamingIntent(live ? StreamlabsStreamingState.Live : StreamlabsStreamingState.Offline);
			}

			return result;
		}
		finally
		{
			_streamingGate.Release();
		}
	}

	private async Task<StreamlabsCommandResult> SetRecordingAsync(bool recording)
	{
		await _recordingGate.WaitAsync(_cts.Token).ConfigureAwait(false);
		try
		{
			var model = await ReadStreamingModelAsync().ConfigureAwait(false);
			if (model is null)
			{
				return StreamlabsCommandResult.NotConnected;
			}

			var effective = RecordingIntent ?? model.Recording;
			var isRecording = effective is StreamlabsRecordingState.Recording;

			if (isRecording == recording)
			{
				return StreamlabsCommandResult.Ok;
			}

			if (effective is StreamlabsRecordingState.Starting or StreamlabsRecordingState.Stopping)
			{
				_logger.Information("Streamlabs Desktop recording is {State}; the request was ignored",
					StreamlabsModelReader.ToWireString(effective));
				return StreamlabsCommandResult.Ok;
			}

			var result = await InvokeAsync(StreamlabsServices.Streaming, StreamlabsServices.ToggleRecording)
				.ConfigureAwait(false);
			if (result.Succeeded)
			{
				SetRecordingIntent(recording
					? StreamlabsRecordingState.Recording
					: StreamlabsRecordingState.Offline);
			}

			return result;
		}
		finally
		{
			_recordingGate.Release();
		}
	}

	private async Task PumpAsync(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await RunSessionAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (StreamlabsAuthenticationException ex)
			{
				NeedsAuthorization = true;
				_logger.Warning(ex,
					"Streamlabs Desktop rejected the API token; run setup again to reconnect");
				return;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "The Streamlabs Desktop session ended");
			}

			try
			{
				await Task.Delay(NextReconnectDelay(), cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (ObjectDisposedException)
			{
				return;
			}
		}
	}

	private TimeSpan NextReconnectDelay()
	{
		var attempt = Math.Min(Interlocked.Increment(ref _failures), 8);
		var scaled = _reconnectDelay * Math.Pow(2, attempt - 1);
		return scaled > _maximumReconnectDelay ? _maximumReconnectDelay : scaled;
	}

	private async Task RunSessionAsync(CancellationToken cancellationToken)
	{
		var client = _clientFactory();
		var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

		void OnDisconnected(object? sender, string? reason) => closed.TrySetResult(true);
		void OnEvent(object? sender, StreamlabsEvent push) => HandlePush(push);

		client.Disconnected += OnDisconnected;
		client.EventReceived += OnEvent;

		try
		{
			await client.ConnectAsync(_endpoint.WebSocketUri(), _token, cancellationToken).ConfigureAwait(false);
			_client = client;

			await SubscribeAllAsync(client, cancellationToken).ConfigureAwait(false);
			await SeedStateAsync(client, cancellationToken).ConfigureAwait(false);

			Interlocked.Exchange(ref _failures, 0);
			_onVariablesChanged?.Invoke();
			_events?.MarkReady();
			_events?.PublishConnected();
			_announcedConnected = true;
			_logger.Information("Streamlabs Desktop connected to {Address}", _endpoint.DisplayAddress);

			await closed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			client.Disconnected -= OnDisconnected;
			client.EventReceived -= OnEvent;
			_client = null;

			_catalog.Clear();
			SetStateAndRequestRefresh(StreamlabsDesktopState.Disconnected);
			lock (_stateGate)
			{
				_pushedFields.Clear();
			}

			ClearIntents();
			_events?.Reset();

			if (_announcedConnected)
			{
				_announcedConnected = false;
				_events?.PublishDisconnected();
				_logger.Information("Streamlabs Desktop disconnected from {Address}", _endpoint.DisplayAddress);
			}

			try
			{
				await client.DisconnectAsync().ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Error while closing the Streamlabs Desktop session");
			}

			client.Dispose();
		}
	}

	private static async Task SubscribeAllAsync(IStreamlabsClient client, CancellationToken cancellationToken)
	{
		foreach (var (service, observable) in StreamlabsServices.Subscriptions)
		{
			await client.SubscribeAsync(service, observable, cancellationToken).ConfigureAwait(false);
		}
	}

	private async Task SeedStateAsync(IStreamlabsClient client, CancellationToken cancellationToken)
	{
		var streaming = StreamlabsModelReader.ReadStreamingModel(await client
			.InvokeAsync(StreamlabsServices.Streaming, StreamlabsServices.GetModel, null, cancellationToken)
			.ConfigureAwait(false));

		var studioMode = StreamlabsModelReader.ReadStudioMode(await client
			.InvokeAsync(StreamlabsServices.Transitions, StreamlabsServices.GetModel, null, cancellationToken)
			.ConfigureAwait(false));

		var activeScene = StreamlabsModelReader.ReadScene(await client
			.InvokeAsync(StreamlabsServices.Scenes, StreamlabsServices.ActiveScene, null, cancellationToken)
			.ConfigureAwait(false));

		var sceneCount = (await _catalog.GetSceneNamesAsync().ConfigureAwait(false)).Count;

		// A push that arrived while these six calls were in flight already describes a newer reality,
		// so the reply it raced must not overwrite it.
		lock (_stateGate)
		{
			var next = _state with { IsConnected = true, SceneCount = sceneCount };

			if (!_pushedFields.Contains(StateFields.Scene) && activeScene is not null)
			{
				next = next with { CurrentScene = activeScene.Name, CurrentSceneId = activeScene.Id };
			}

			if (!_pushedFields.Contains(StateFields.Streaming))
			{
				next = next with
				{
					Streaming = streaming.Streaming,
					StreamingSince = streaming.Streaming is StreamlabsStreamingState.Live
						or StreamlabsStreamingState.Reconnecting
						? streaming.StreamingSince ?? DateTimeOffset.UtcNow
						: null
				};
			}

			if (!_pushedFields.Contains(StateFields.Recording))
			{
				next = next with
				{
					Recording = streaming.Recording,
					RecordingSince = streaming.Recording is StreamlabsRecordingState.Recording
						? streaming.RecordingSince ?? DateTimeOffset.UtcNow
						: null
				};
			}

			if (!_pushedFields.Contains(StateFields.ReplayBuffer))
			{
				next = next with { ReplayBuffer = streaming.ReplayBuffer };
			}

			if (!_pushedFields.Contains(StateFields.StudioMode))
			{
				next = next with { StudioModeActive = studioMode };
			}

			_state = next;
		}
	}

	private void HandlePush(StreamlabsEvent push)
	{
		try
		{
			switch (push.ResourceId)
			{
				case StreamlabsServices.Scenes + "." + StreamlabsServices.SceneSwitched:
					OnSceneSwitched(push.Data);
					_onVariablesChanged?.Invoke();
					break;

				case StreamlabsServices.Scenes + "." + StreamlabsServices.SceneAdded:
				case StreamlabsServices.Scenes + "." + StreamlabsServices.SceneRemoved:
					_catalog.Invalidate();
					_ = RefreshSceneCountAsync();
					break;

				case StreamlabsServices.Scenes + "." + StreamlabsServices.ItemAdded:
				case StreamlabsServices.Scenes + "." + StreamlabsServices.ItemRemoved:
					InvalidateItemScene(push.Data);
					break;

				case StreamlabsServices.Scenes + "." + StreamlabsServices.ItemUpdated:
					OnItemUpdated(push.Data);
					break;

				case StreamlabsServices.Sources + "." + StreamlabsServices.SourceAdded:
				case StreamlabsServices.Sources + "." + StreamlabsServices.SourceRemoved:
					_catalog.Invalidate();
					break;

				case StreamlabsServices.Sources + "." + StreamlabsServices.SourceUpdated:
					OnSourceUpdated(push.Data);
					break;

				case StreamlabsServices.Streaming + "." + StreamlabsServices.StreamingStatusChange:
					OnStreamingStatus(StreamlabsModelReader.ReadStreamingState(push.Data));
					_onVariablesChanged?.Invoke();
					break;

				case StreamlabsServices.Streaming + "." + StreamlabsServices.RecordingStatusChange:
					OnRecordingStatus(StreamlabsModelReader.ReadRecordingState(push.Data));
					_onVariablesChanged?.Invoke();
					break;

				case StreamlabsServices.Streaming + "." + StreamlabsServices.ReplayBufferStatusChange:
					OnReplayBufferStatus(StreamlabsModelReader.ReadReplayBufferState(push.Data));
					_onVariablesChanged?.Invoke();
					break;

				case StreamlabsServices.Transitions + "." + StreamlabsServices.StudioModeChanged:
					OnStudioModeChanged(push.Data.ValueKind == JsonValueKind.True);
					_onVariablesChanged?.Invoke();
					break;

				default:
					break;
			}
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Could not handle the Streamlabs Desktop push {Resource}", push.ResourceId);
		}
	}

	private void OnSceneSwitched(JsonElement data)
	{
		if (StreamlabsModelReader.ReadScene(data) is not { } scene)
		{
			return;
		}

		string? previous;
		lock (_stateGate)
		{
			previous = _state.CurrentScene;
			_pushedFields.Add(StateFields.Scene);
			_state = _state with { CurrentScene = scene.Name, CurrentSceneId = scene.Id };
		}

		if (!string.Equals(previous, scene.Name, StringComparison.Ordinal))
		{
			_events?.PublishSceneChanged(scene.Name, previous);
		}
	}

	private void OnStreamingStatus(StreamlabsStreamingState status)
	{
		StreamlabsStreamingState previous;
		lock (_stateGate)
		{
			previous = _state.Streaming;
			_pushedFields.Add(StateFields.Streaming);

			var wasLive = _state.IsStreaming;
			var isLive = status is StreamlabsStreamingState.Live or StreamlabsStreamingState.Reconnecting;

			_state = _state with
			{
				Streaming = status,
				StreamingSince = isLive
					? wasLive ? _state.StreamingSince ?? DateTimeOffset.UtcNow : DateTimeOffset.UtcNow
					: null
			};
		}

		SettleStreamingIntent(status);
		_events?.PublishStreamingStatus(previous, status);
	}

	private void OnRecordingStatus(StreamlabsRecordingState status)
	{
		StreamlabsRecordingState previous;
		lock (_stateGate)
		{
			previous = _state.Recording;
			_pushedFields.Add(StateFields.Recording);

			var wasRecording = _state.IsRecording;
			var isRecording = status is StreamlabsRecordingState.Recording;

			_state = _state with
			{
				Recording = status,
				RecordingSince = isRecording
					? wasRecording ? _state.RecordingSince ?? DateTimeOffset.UtcNow : DateTimeOffset.UtcNow
					: null
			};
		}

		SettleRecordingIntent(status);
		_events?.PublishRecordingStatus(previous, status);
	}

	private void OnReplayBufferStatus(StreamlabsReplayBufferState status)
	{
		StreamlabsReplayBufferState previous;
		lock (_stateGate)
		{
			previous = _state.ReplayBuffer;
			_pushedFields.Add(StateFields.ReplayBuffer);
			_state = _state with { ReplayBuffer = status };
		}

		_events?.PublishReplayBufferStatus(previous, status);
	}

	private void OnStudioModeChanged(bool enabled)
	{
		lock (_stateGate)
		{
			_pushedFields.Add(StateFields.StudioMode);
			_state = _state with { StudioModeActive = enabled };
		}

		_events?.PublishStudioModeChanged(enabled);
	}

	private void OnItemUpdated(JsonElement data)
	{
		var sceneId = StreamlabsModelReader.ReadString(StreamlabsModelReader.Property(data, "sceneId"));
		var sceneItemId = StreamlabsModelReader.ReadString(StreamlabsModelReader.Property(data, "sceneItemId"));
		if (sceneId is null || sceneItemId is null)
		{
			return;
		}

		var known = _catalog.FindItem(sceneId, sceneItemId);
		if (known is null)
		{
			_catalog.InvalidateScene(sceneId);
			return;
		}

		var visible = StreamlabsModelReader.Property(data, "visible").ValueKind != JsonValueKind.False;
		_catalog.PatchItemVisibility(sceneId, sceneItemId, visible);

		var sceneName = _catalog.SceneName(sceneId) ?? sceneId;
		_events?.PublishSourceVisibilityChanged(sceneId, sceneItemId, sceneName, known.Name, visible);
	}

	private void OnSourceUpdated(JsonElement data)
	{
		var sourceId = StreamlabsModelReader.ReadString(StreamlabsModelReader.Property(data, "sourceId")) ??
			StreamlabsModelReader.ReadString(StreamlabsModelReader.Property(data, "id"));
		if (sourceId is null)
		{
			return;
		}

		var knownName = _catalog.SourceName(sourceId);
		var name = StreamlabsModelReader.ReadString(StreamlabsModelReader.Property(data, "name"));
		if (name is not null && knownName is not null && !string.Equals(name, knownName, StringComparison.Ordinal))
		{
			_catalog.Invalidate();
			return;
		}

		if (StreamlabsModelReader.TryReadMuted(data, out var muted))
		{
			_catalog.PatchAudioMuted(sourceId, muted);
			_events?.PublishSourceMuteChanged(sourceId, name ?? knownName ?? sourceId, muted);
			return;
		}

		_ = RefreshMuteAsync(sourceId, name ?? knownName ?? sourceId);
	}

	private async Task RefreshMuteAsync(string sourceId, string sourceName)
	{
		if (_client is not { } client)
		{
			return;
		}

		try
		{
			var response = await client.InvokeAsync(StreamlabsRpcFrames.AudioSourceResource(sourceId),
					StreamlabsServices.GetModel,
					null,
					_cts.Token)
				.ConfigureAwait(false);

			if (StreamlabsModelReader.ReadAudioSource(response) is not { } source)
			{
				return;
			}

			_catalog.PatchAudioMuted(sourceId, source.Muted);
			_events?.PublishSourceMuteChanged(sourceId, source.Name, source.Muted);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Could not read the mute state of Streamlabs source {Source}", sourceName);
		}
	}

	private async Task RefreshSceneCountAsync()
	{
		try
		{
			var count = (await _catalog.GetSceneNamesAsync().ConfigureAwait(false)).Count;
			lock (_stateGate)
			{
				_state = _state with { SceneCount = count };
			}

			_onVariablesChanged?.Invoke();
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Could not refresh the Streamlabs scene count");
		}
	}

	private void InvalidateItemScene(JsonElement data)
	{
		if (StreamlabsModelReader.ReadString(StreamlabsModelReader.Property(data, "sceneId")) is { } sceneId)
		{
			_catalog.InvalidateScene(sceneId);
		}
		else
		{
			_catalog.Invalidate();
		}
	}

	private void SetStateAndRequestRefresh(StreamlabsDesktopState state)
	{
		SetState(state);
		_onVariablesChanged?.Invoke();
	}

	private void SetState(StreamlabsDesktopState state)
	{
		lock (_stateGate)
		{
			_state = state;
		}
	}

	private StreamlabsStreamingState? StreamingIntent
	{
		get
		{
			lock (_intentGate)
			{
				return _streamingIntent.Target;
			}
		}
	}

	private StreamlabsRecordingState? RecordingIntent
	{
		get
		{
			lock (_intentGate)
			{
				return _recordingIntent.Target;
			}
		}
	}

	private void SetStreamingIntent(StreamlabsStreamingState target)
	{
		lock (_intentGate)
		{
			_streamingIntent = Intent<StreamlabsStreamingState>.For(target, _intentWindow);
		}
	}

	private void SetRecordingIntent(StreamlabsRecordingState target)
	{
		lock (_intentGate)
		{
			_recordingIntent = Intent<StreamlabsRecordingState>.For(target, _intentWindow);
		}
	}

	private void ClearIntents()
	{
		lock (_intentGate)
		{
			_streamingIntent = default;
			_recordingIntent = default;
		}
	}

	private void SettleStreamingIntent(StreamlabsStreamingState status)
	{
		lock (_intentGate)
		{
			var isLive = status is StreamlabsStreamingState.Live or StreamlabsStreamingState.Reconnecting;
			if (_streamingIntent.Target is { } target && (target is StreamlabsStreamingState.Live) == isLive)
			{
				_streamingIntent = default;
			}
		}
	}

	private void SettleRecordingIntent(StreamlabsRecordingState status)
	{
		lock (_intentGate)
		{
			var isRecording = status is StreamlabsRecordingState.Recording;
			if (_recordingIntent.Target is { } target &&
				(target is StreamlabsRecordingState.Recording) == isRecording)
			{
				_recordingIntent = default;
			}
		}
	}

	private static class StateFields
	{
		public const string Scene = "scene";

		public const string Streaming = "streaming";

		public const string Recording = "recording";

		public const string ReplayBuffer = "replay-buffer";

		public const string StudioMode = "studio-mode";
	}

	private readonly record struct Intent<T>(T? Value, DateTimeOffset Expires)
		where T : struct
	{
		public T? Target => Value is { } value && DateTimeOffset.UtcNow < Expires ? value : null;

		public static Intent<T> For(T value, TimeSpan window) => new(value, DateTimeOffset.UtcNow + window);
	}
}
