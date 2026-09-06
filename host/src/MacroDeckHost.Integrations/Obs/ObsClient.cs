using MacroDeck.Sdk.Logging;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Communication;
using Serilog;

namespace MacroDeckHost.Integrations.Obs;

internal sealed class ObsClient : IObsClient
{
	private readonly ILogger _logger;
	private readonly TimeSpan _reachabilityTimeout;

	private readonly OBSWebsocket _obs = new();

	internal ObsClient(ILogger? logger = null, TimeSpan? reachabilityTimeout = null)
	{
		_logger = logger ?? IntegrationLog.For<ObsClient>(ObsIntegration.IntegrationId);
		_reachabilityTimeout = reachabilityTimeout ?? TimeSpan.FromSeconds(2);
		_obs.Connected += OnConnected;
		_obs.Disconnected += OnDisconnected;

		_obs.CurrentProgramSceneChanged += (_, _) => RaiseStateChanged();
		_obs.CurrentPreviewSceneChanged += (_, _) => RaiseStateChanged();
		_obs.RecordStateChanged += (_, _) => RaiseStateChanged();
		_obs.StreamStateChanged += (_, _) => RaiseStateChanged();
		_obs.ReplayBufferStateChanged += (_, _) => RaiseStateChanged();
		_obs.VirtualcamStateChanged += (_, _) => RaiseStateChanged();
		_obs.StudioModeStateChanged += (_, _) => RaiseStateChanged();

		_obs.InputMuteStateChanged += (_, args) =>
			InputMuteChanged?.Invoke(this, new ObsInputMuteChange(args.InputName, args.InputMuted));
		_obs.ReplayBufferSaved += (_, args) => ReplayBufferSaved?.Invoke(this, args.SavedReplayPath);

		// Deliberately not subscribed: InputVolumeMeters fires at the audio meter refresh rate
		// (tens of times a second). Nothing downstream should ever be driven by it.
	}

	public event EventHandler? Connected;

	public event EventHandler<string?>? Disconnected;

	public event EventHandler? StateChanged;

	public event EventHandler<ObsInputMuteChange>? InputMuteChanged;

	public event EventHandler<string>? ReplayBufferSaved;

	public bool IsConnected => _obs.IsConnected;

	public void Connect(string url, string? password) => _ = ConnectCoreAsync(url, password);

	public void Disconnect()
	{
		if (_obs.IsConnected)
		{
			_obs.Disconnect();
		}
	}

	public ObsStatus QueryStatus()
	{
		var status = new ObsStatus();

		status = status with
		{
			CurrentScene = Try(() => _obs.GetCurrentProgramScene(), status.CurrentScene)
		};

		var record = Try(() => _obs.GetRecordStatus(), null);
		if (record is not null)
		{
			status = status with
			{
				IsRecording = record.IsRecording,
				RecordingPaused = record.IsRecordingPaused,
				RecordingTimecode = record.RecordTimecode,
				RecordBytes = record.RecordingBytes
			};
		}

		var stream = Try(() => _obs.GetStreamStatus(), null);
		if (stream is not null)
		{
			status = status with
			{
				IsStreaming = stream.IsActive,
				StreamingTimecode = stream.TimeCode,
				StreamBytes = stream.BytesSent,
				StreamSkippedFrames = stream.SkippedFrames,
				StreamTotalFrames = stream.TotalFrames
			};
		}

		var virtualCam = Try(() => _obs.GetVirtualCamStatus(), null);
		if (virtualCam is not null)
		{
			status = status with { VirtualCamActive = virtualCam.IsActive };
		}

		status = status with
		{
			ReplayBufferActive = Try(() => _obs.GetReplayBufferStatus(), status.ReplayBufferActive),
			StudioModeActive = Try(() => _obs.GetStudioModeEnabled(), status.StudioModeActive)
		};

		if (status.StudioModeActive)
		{
			status = status with { PreviewScene = Try(() => _obs.GetCurrentPreviewScene(), status.PreviewScene) };
		}

		var stats = Try(() => _obs.GetStats(), null);
		if (stats is not null)
		{
			status = status with
			{
				Fps = stats.FPS,
				CpuUsage = stats.CpuUsage,
				EncoderSkippedFrames = stats.OutputSkippedFrames,
				EncoderTotalFrames = stats.OutputTotalFrames
			};
		}

		var video = Try(() => _obs.GetVideoSettings(), null);
		if (video is not null && video.FpsDenominator > 0)
		{
			status = status with { MaxFps = video.FpsNumerator / (double)video.FpsDenominator };
		}

		return status;
	}

	public IReadOnlyList<string> GetSceneNames()
	{
		var scenes = Try(() => _obs.GetSceneList(), null);
		return scenes?.Scenes.Select(s => s.Name).ToList() ?? [];
	}

	public IReadOnlyList<string> GetSceneItemNames(string sceneName)
	{
		var items = Try(() => _obs.GetSceneItemList(sceneName), null);
		return items?.Select(i => i.SourceName).ToList() ?? [];
	}

	public IReadOnlyList<string> GetInputNames()
	{
		var inputs = Try(() => _obs.GetInputList(), null);
		return inputs?.Select(i => i.InputName).ToList() ?? [];
	}

	public IReadOnlyList<string> GetSourceNames()
		=> GetSceneNames().Concat(GetInputNames()).Distinct(StringComparer.Ordinal).ToList();

	public IReadOnlyList<string> GetSourceFilterNames(string sourceName)
	{
		var filters = Try(() => _obs.GetSourceFilterList(sourceName), null);
		return filters?.Select(f => f.Name).ToList() ?? [];
	}

	public void SetCurrentScene(string sceneName) => _obs.SetCurrentProgramScene(sceneName);

	public void SetPreviewScene(string sceneName) => _obs.SetCurrentPreviewScene(sceneName);

	public void StartRecord() => _obs.StartRecord();

	public void StopRecord() => _obs.StopRecord();

	public void ToggleRecord() => _obs.ToggleRecord();

	public void ToggleRecordPause() => _obs.ToggleRecordPause();

	public void StartStream() => _obs.StartStream();

	public void StopStream() => _obs.StopStream();

	public void ToggleStream() => _obs.ToggleStream();

	public void StartVirtualCam() => _obs.StartVirtualCam();

	public void StopVirtualCam() => _obs.StopVirtualCam();

	public void ToggleVirtualCam() => _obs.ToggleVirtualCam();

	public void StartReplayBuffer() => _obs.StartReplayBuffer();

	public void StopReplayBuffer() => _obs.StopReplayBuffer();

	public void ToggleReplayBuffer() => _obs.ToggleReplayBuffer();

	public void SaveReplayBuffer() => _obs.SaveReplayBuffer();

	public bool GetSourceVisible(string sceneName, string sourceName)
		=> _obs.GetSceneItemEnabled(sceneName, _obs.GetSceneItemId(sceneName, sourceName, 0));

	public void SetSourceVisible(string sceneName, string sourceName, bool visible)
	{
		var itemId = _obs.GetSceneItemId(sceneName, sourceName, 0);
		_obs.SetSceneItemEnabled(sceneName, itemId, visible);
	}

	public void ToggleSourceVisible(string sceneName, string sourceName)
	{
		var itemId = _obs.GetSceneItemId(sceneName, sourceName, 0);
		var enabled = _obs.GetSceneItemEnabled(sceneName, itemId);
		_obs.SetSceneItemEnabled(sceneName, itemId, !enabled);
	}

	public bool GetInputMuted(string inputName) => _obs.GetInputMute(inputName);

	public void SetInputMute(string inputName, bool muted) => _obs.SetInputMute(inputName, muted);

	public void ToggleInputMute(string inputName) => _obs.ToggleInputMute(inputName);

	public float GetInputVolume(string inputName) => _obs.GetInputVolume(inputName).VolumeMul;

	public void SetInputVolume(string inputName, float volumeMultiplier)
		=> _obs.SetInputVolume(inputName, volumeMultiplier, false);

	public bool GetSourceFilterEnabled(string sourceName, string filterName)
		=> _obs.GetSourceFilter(sourceName, filterName).IsEnabled;

	public void SetSourceFilterEnabled(string sourceName, string filterName, bool enabled)
		=> _obs.SetSourceFilterEnabled(sourceName, filterName, enabled);

	public void ToggleSourceFilterEnabled(string sourceName, string filterName)
	{
		var enabled = _obs.GetSourceFilter(sourceName, filterName).IsEnabled;
		_obs.SetSourceFilterEnabled(sourceName, filterName, !enabled);
	}

	public void SetStudioMode(bool enabled) => _obs.SetStudioModeEnabled(enabled);

	public string GetInputAudioMonitorType(string inputName) => _obs.GetInputAudioMonitorType(inputName);

	// obs-websocket reports the offset in nanoseconds; every value OBS itself produces is a whole
	// millisecond in nanoseconds, so plain integer division is exact and keeps a negative offset negative.
	public int GetInputAudioSyncOffsetMilliseconds(string inputName)
		=> _obs.GetInputAudioSyncOffset(inputName) / 1_000_000;

	public ObsAudioTracks GetInputAudioTracks(string inputName)
	{
		var tracks = _obs.GetInputAudioTracks(inputName);
		return new ObsAudioTracks([
			tracks.IsTrack1Active,
			tracks.IsTrack2Active,
			tracks.IsTrack3Active,
			tracks.IsTrack4Active,
			tracks.IsTrack5Active,
			tracks.IsTrack6Active
		]);
	}

	public ObsSourceActivity GetSourceActive(string sourceName)
	{
		var active = _obs.GetSourceActive(sourceName);
		return new ObsSourceActivity(active.VideoActive, active.VideoShowing);
	}

	// InputSettings.Settings is a Newtonsoft JObject - obs-websocket-dotnet's own dependency, not one this
	// project takes on. Returning its raw text instead of the JObject keeps Newtonsoft entirely out of
	// this project's surface; callers parse it with System.Text.Json like every other JSON this project
	// handles.
	public string GetInputSettings(string inputName) => _obs.GetInputSettings(inputName).Settings.ToString();

	// obs-websocket-dotnet's ConnectAsync is void and starts the underlying Websocket.Client via a
	// fire-and-forget StartOrFail(); a refused connection (OBS closed) faults that unobserved task,
	// which the finalizer rethrows as an UnobservedTaskException. Probing reachability first lets us
	// skip the connect in the common "OBS not running" case, so the faulting task is never started.
	// Reported failures surface through the Disconnected event, matching the IObsClient contract, so
	// ObsConnection schedules its normal reconnect. This method never throws, so the fire-and-forget
	// call in Connect cannot itself produce an unobserved exception.
	private async Task ConnectCoreAsync(string url, string? password)
	{
		try
		{
			if (!await ObsReachabilityProbe.IsReachableAsync(url, _reachabilityTimeout).ConfigureAwait(false))
			{
				Disconnected?.Invoke(this, "unreachable");
				return;
			}

			_obs.ConnectAsync(url, password ?? string.Empty);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "OBS connect attempt failed");
			Disconnected?.Invoke(this, ex.Message);
		}
	}

	private void OnConnected(object? sender, EventArgs e) => Connected?.Invoke(this, EventArgs.Empty);

	private void OnDisconnected(object? sender, ObsDisconnectionInfo e)
		=> Disconnected?.Invoke(this, e.DisconnectReason);

	private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

	private T Try<T>(Func<T> read, T fallback)
	{
		try
		{
			return read();
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "OBS query failed; using fallback value");
			return fallback;
		}
	}
}
