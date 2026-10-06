namespace MacroDeckHost.Integrations.Obs;

internal interface IObsClient
{
	event EventHandler? Connected;

	event EventHandler<string?>? Disconnected;

	event EventHandler? StateChanged;

	event EventHandler<ObsInputMuteChange>? InputMuteChanged;

	event EventHandler<string>? ReplayBufferSaved;

	event EventHandler<string>? RecordFileChanged;

	event EventHandler<string>? ScreenshotSaved;

	event EventHandler<ObsInputSettingsChange>? InputSettingsChanged;

	event EventHandler<ObsFilterChange>? SourceFilterChanged;

	event EventHandler<ObsInputFlagChange>? InputActiveChanged;

	event EventHandler<ObsInputFlagChange>? InputShowingChanged;

	event EventHandler<string>? CustomEventReceived;

	bool IsConnected { get; }

	void Connect(string url, string? password);

	void Disconnect();

	ObsStatus QueryStatus();

	IReadOnlyList<string> GetSceneNames();

	IReadOnlyList<string> GetSceneItemNames(string sceneName);

	IReadOnlyList<string> GetGroupNames();

	IReadOnlyList<string> GetGroupItemNames(string groupName);

	IReadOnlyList<string> GetInputNames();

	IReadOnlyList<string> GetSourceNames();

	IReadOnlyList<string> GetSourceFilterNames(string sourceName);

	IReadOnlyList<string> GetProfileNames();

	IReadOnlyList<string> GetSceneCollectionNames();

	IReadOnlyList<string> GetOutputNames();

	void SetCurrentScene(string sceneName);

	void SetPreviewScene(string sceneName);

	void SetCurrentProfile(string profileName);

	void SetCurrentSceneCollection(string sceneCollectionName);

	void StartRecord();

	void StopRecord();

	void ToggleRecord();

	void ToggleRecordPause();

	void StartStream();

	void StopStream();

	void ToggleStream();

	void StartVirtualCam();

	void StopVirtualCam();

	void ToggleVirtualCam();

	void StartReplayBuffer();

	void StopReplayBuffer();

	void ToggleReplayBuffer();

	void SaveReplayBuffer();

	void SplitRecordFile();

	void CreateRecordChapter(string? chapterName);

	void SetRecordDirectory(string directory);

	void StartOutput(string outputName);

	void StopOutput(string outputName);

	void ToggleOutput(string outputName);

	bool GetOutputActive(string outputName);

	bool GetSourceVisible(string sceneName, string sourceName);

	void SetSourceVisible(string sceneName, string sourceName, bool visible);

	void ToggleSourceVisible(string sceneName, string sourceName);

	bool GetInputMuted(string inputName);

	void SetInputMute(string inputName, bool muted);

	void ToggleInputMute(string inputName);

	float GetInputVolume(string inputName);

	void SetInputVolume(string inputName, float volumeMultiplier);

	bool GetSourceFilterEnabled(string sourceName, string filterName);

	void SetSourceFilterEnabled(string sourceName, string filterName, bool enabled);

	void ToggleSourceFilterEnabled(string sourceName, string filterName);

	void SetStudioMode(bool enabled);

	string GetInputAudioMonitorType(string inputName);

	int GetInputAudioSyncOffsetMilliseconds(string inputName);

	ObsAudioTracks GetInputAudioTracks(string inputName);

	ObsSourceActivity GetSourceActive(string sourceName);

	/// <summary>The input's settings as OBS reports them, verbatim JSON text.</summary>
	string GetInputSettings(string inputName);
}

internal sealed class ObsRequestException : Exception
{
	internal const int OutputNotRunning = 501;

	internal const int NotReady = 207;

	internal const int ResourceNotFound = 600;

	// obs-websocket-dotnet reports a request that got no answer within its timeout as code 1, which is
	// not an obs-websocket status code.
	internal const int TimedOut = 1;

	public ObsRequestException(int code, string message)
		: base(message)
	{
		Code = code;
	}

	public int Code { get; }
}

internal sealed class ObsNotReadyException(Exception inner)
	: Exception("OBS is not ready to answer requests", inner);

internal readonly record struct ObsInputMuteChange(string InputName, bool Muted);

internal sealed record ObsInputSettingsChange(string InputName, IReadOnlyList<string> ChangedKeys);

internal readonly record struct ObsFilterChange(string SourceName, string FilterName);

internal readonly record struct ObsInputFlagChange(string InputName, bool Value);

/// <summary>Which of OBS's six audio tracks an input is assigned to, in track order 1..6.</summary>
internal sealed record ObsAudioTracks(IReadOnlyList<bool> Tracks);

/// <summary>
/// A source's activity: <see cref="Active"/> is whether it is currently rendered into the program
/// output, <see cref="Showing"/> is whether it is rendered anywhere - program or preview. A source can be
/// showing without being active, e.g. visible only in the studio-mode preview.
/// </summary>
internal sealed record ObsSourceActivity(bool Active, bool Showing);

internal sealed record ObsStatus
{
	public string? CurrentScene { get; init; }

	public string? PreviewScene { get; init; }

	public string? CurrentProfile { get; init; }

	public string? CurrentSceneCollection { get; init; }

	public bool IsRecording { get; init; }

	public bool RecordingPaused { get; init; }

	public string? RecordingTimecode { get; init; }

	public bool IsStreaming { get; init; }

	public string? StreamingTimecode { get; init; }

	public bool VirtualCamActive { get; init; }

	public bool ReplayBufferActive { get; init; }

	public bool StudioModeActive { get; init; }

	public double Fps { get; init; }

	public double MaxFps { get; init; }

	public double CpuUsage { get; init; }

	public long StreamBytes { get; init; }

	public long StreamSkippedFrames { get; init; }

	public long StreamTotalFrames { get; init; }

	public long RecordBytes { get; init; }

	public long EncoderSkippedFrames { get; init; }

	public long EncoderTotalFrames { get; init; }
}
