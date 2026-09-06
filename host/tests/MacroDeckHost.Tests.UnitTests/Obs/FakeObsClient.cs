using System.Globalization;
using MacroDeckHost.Integrations.Obs;

namespace MacroDeckHost.Tests.UnitTests.Obs;

internal sealed class FakeObsClient : IObsClient
{
	private static readonly ObsAudioTracks _noAudioTracks = new([false, false, false, false, false, false]);

	private static readonly ObsSourceActivity _inactive = new(false, false);

	public int ConnectCount { get; private set; }

	public string? LastUrl { get; private set; }

	public string? LastPassword { get; private set; }

	public bool IsConnected { get; set; }

	public bool ConnectRaisesConnected { get; set; }

	public bool ConnectRaisesDisconnected { get; set; }

	public string? DisconnectReason { get; set; }

	public ObsStatus Status { get; set; } = new();

	public Func<ObsStatus>? QueryStatusHandler { get; set; }

	public IReadOnlyList<string> SceneNames { get; set; } = [];

	public Dictionary<string, IReadOnlyList<string>> SceneItems { get; } = new();

	public IReadOnlyList<string> InputNames { get; set; } = [];

	public IReadOnlyList<string> SourceNames { get; set; } = [];

	public Dictionary<string, float> InputVolumes { get; } = new(StringComparer.Ordinal);

	public Dictionary<string, IReadOnlyList<string>> SourceFilters { get; } = new(StringComparer.Ordinal);

	public Dictionary<string, bool> FilterStates { get; } = new(StringComparer.Ordinal);

	public Dictionary<string, string> InputAudioMonitorTypes { get; } = new(StringComparer.Ordinal);

	public Dictionary<string, int> InputAudioSyncOffsetsMs { get; } = new(StringComparer.Ordinal);

	public Dictionary<string, ObsAudioTracks> InputAudioTracksByName { get; } = new(StringComparer.Ordinal);

	public Dictionary<string, ObsSourceActivity> SourceActivity { get; } = new(StringComparer.Ordinal);

	public Dictionary<string, string> InputSettingsJson { get; } = new(StringComparer.Ordinal);

	/// <summary>
	/// Names that used to exist but no longer do. A real OBS instance fails every request naming a gone
	/// input or source; this is what lets tests simulate that instead of the dictionaries below silently
	/// answering with their fallback value for a name that was never registered in the first place.
	/// </summary>
	public HashSet<string> DeletedNames { get; } = new(StringComparer.Ordinal);

	public List<string> Calls { get; } = [];

	public event EventHandler? Connected;

	public event EventHandler<string?>? Disconnected;

	public event EventHandler? StateChanged;

	public event EventHandler<ObsInputMuteChange>? InputMuteChanged;

	public event EventHandler<string>? ReplayBufferSaved;

	public void RaiseInputMuteChanged(string inputName, bool muted)
		=> InputMuteChanged?.Invoke(this, new ObsInputMuteChange(inputName, muted));

	public void RaiseReplayBufferSaved(string path) => ReplayBufferSaved?.Invoke(this, path);

	public void Connect(string url, string? password)
	{
		ConnectCount++;
		LastUrl = url;
		LastPassword = password;

		if (ConnectRaisesConnected)
		{
			IsConnected = true;
			RaiseConnected();
		}
		else if (ConnectRaisesDisconnected)
		{
			IsConnected = false;
			RaiseDisconnected(DisconnectReason);
		}
	}

	public void Disconnect()
	{
		IsConnected = false;
		Calls.Add("Disconnect");
	}

	public ObsStatus QueryStatus() => QueryStatusHandler?.Invoke() ?? Status;

	public IReadOnlyList<string> GetSceneNames() => SceneNames;

	public IReadOnlyList<string> GetSceneItemNames(string sceneName)
		=> SceneItems.GetValueOrDefault(sceneName, []);

	public IReadOnlyList<string> GetInputNames() => InputNames;

	public IReadOnlyList<string> GetSourceNames() => SourceNames;

	public IReadOnlyList<string> GetSourceFilterNames(string sourceName)
		=> SourceFilters.GetValueOrDefault(sourceName, []);

	public void SetCurrentScene(string sceneName) => Calls.Add($"SetCurrentScene:{sceneName}");

	public void SetPreviewScene(string sceneName) => Calls.Add($"SetPreviewScene:{sceneName}");

	public void StartRecord() => Calls.Add("StartRecord");

	public void StopRecord() => Calls.Add("StopRecord");

	public void ToggleRecord() => Calls.Add("ToggleRecord");

	public void ToggleRecordPause() => Calls.Add("ToggleRecordPause");

	public void StartStream() => Calls.Add("StartStream");

	public void StopStream() => Calls.Add("StopStream");

	public void ToggleStream() => Calls.Add("ToggleStream");

	public void StartVirtualCam() => Calls.Add("StartVirtualCam");

	public void StopVirtualCam() => Calls.Add("StopVirtualCam");

	public void ToggleVirtualCam() => Calls.Add("ToggleVirtualCam");

	public void StartReplayBuffer() => Calls.Add("StartReplayBuffer");

	public void StopReplayBuffer() => Calls.Add("StopReplayBuffer");

	public void ToggleReplayBuffer() => Calls.Add("ToggleReplayBuffer");

	public void SaveReplayBuffer() => Calls.Add("SaveReplayBuffer");

	public Dictionary<string, bool> MutedInputs { get; } = new(StringComparer.Ordinal);

	public Dictionary<string, bool> VisibleSources { get; } = new(StringComparer.Ordinal);

	public bool GetInputMuted(string inputName)
	{
		Calls.Add($"GetInputMuted:{inputName}");
		EnsureExists(inputName);
		return MutedInputs.GetValueOrDefault(inputName);
	}

	public bool GetSourceVisible(string sceneName, string sourceName)
	{
		Calls.Add($"GetSourceVisible:{sceneName}:{sourceName}");
		EnsureExists(sceneName, sourceName);
		return VisibleSources.GetValueOrDefault($"{sceneName}:{sourceName}");
	}

	public void SetSourceVisible(string sceneName, string sourceName, bool visible)
		=> Calls.Add($"SetSourceVisible:{sceneName}:{sourceName}:{visible}");

	public void ToggleSourceVisible(string sceneName, string sourceName)
		=> Calls.Add($"ToggleSourceVisible:{sceneName}:{sourceName}");

	public void SetInputMute(string inputName, bool muted) => Calls.Add($"SetInputMute:{inputName}:{muted}");

	public void ToggleInputMute(string inputName) => Calls.Add($"ToggleInputMute:{inputName}");

	public float GetInputVolume(string inputName)
	{
		EnsureExists(inputName);
		return InputVolumes.GetValueOrDefault(inputName, 0f);
	}

	public void SetInputVolume(string inputName, float volumeMultiplier)
	{
		InputVolumes[inputName] = volumeMultiplier;
		Calls.Add($"SetInputVolume:{inputName}:{volumeMultiplier.ToString(CultureInfo.InvariantCulture)}");
	}

	public bool GetSourceFilterEnabled(string sourceName, string filterName)
	{
		EnsureExists(sourceName);
		return FilterStates.GetValueOrDefault(FilterKey(sourceName, filterName), false);
	}

	public void SetSourceFilterEnabled(string sourceName, string filterName, bool enabled)
	{
		FilterStates[FilterKey(sourceName, filterName)] = enabled;
		Calls.Add($"SetSourceFilterEnabled:{sourceName}:{filterName}:{enabled}");
	}

	public void ToggleSourceFilterEnabled(string sourceName, string filterName)
	{
		var key = FilterKey(sourceName, filterName);
		FilterStates[key] = !FilterStates.GetValueOrDefault(key, false);
		Calls.Add($"ToggleSourceFilterEnabled:{sourceName}:{filterName}");
	}

	public void SetStudioMode(bool enabled) => Calls.Add($"SetStudioMode:{enabled}");

	public string GetInputAudioMonitorType(string inputName)
	{
		EnsureExists(inputName);
		return InputAudioMonitorTypes.GetValueOrDefault(inputName, "OBS_MONITORING_TYPE_NONE");
	}

	public int GetInputAudioSyncOffsetMilliseconds(string inputName)
	{
		EnsureExists(inputName);
		return InputAudioSyncOffsetsMs.GetValueOrDefault(inputName, 0);
	}

	public ObsAudioTracks GetInputAudioTracks(string inputName)
	{
		EnsureExists(inputName);
		return InputAudioTracksByName.GetValueOrDefault(inputName, _noAudioTracks);
	}

	public ObsSourceActivity GetSourceActive(string sourceName)
	{
		EnsureExists(sourceName);
		return SourceActivity.GetValueOrDefault(sourceName, _inactive);
	}

	public string GetInputSettings(string inputName)
	{
		EnsureExists(inputName);
		return InputSettingsJson.GetValueOrDefault(inputName, "{}");
	}

	public void RaiseConnected() => Connected?.Invoke(this, EventArgs.Empty);

	public void RaiseDisconnected(string? reason) => Disconnected?.Invoke(this, reason);

	public void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

	private static string FilterKey(string sourceName, string filterName) => $"{sourceName}::{filterName}";

	private void EnsureExists(params string[] names)
	{
		foreach (var name in names)
		{
			if (DeletedNames.Contains(name))
			{
				throw new InvalidOperationException($"OBS resource '{name}' does not exist.");
			}
		}
	}
}
