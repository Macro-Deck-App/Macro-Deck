namespace MacroDeckHost.Integrations.Obs;

internal sealed record ObsState
{
	public static ObsState Disconnected { get; } = new();

	public bool IsConnected { get; init; }

	public string? CurrentScene { get; init; }

	public string? PreviewScene { get; init; }

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

	public double StreamingBitrateKbps { get; init; }

	public double RecordingBitrateKbps { get; init; }

	public long DroppedFrames { get; init; }

	public double DroppedFramesPercent { get; init; }

	public long EncoderSkippedFrames { get; init; }

	public double EncoderSkippedFramesPercent { get; init; }

	public long StreamBytes { get; init; }

	public long RecordBytes { get; init; }

	public DateTime? SampledAtUtc { get; init; }

	public static ObsState FromStatus(ObsStatus status, ObsState? previous, DateTime nowUtc) => new()
	{
		IsConnected = true,
		CurrentScene = status.CurrentScene,
		PreviewScene = status.PreviewScene,
		IsRecording = status.IsRecording,
		RecordingPaused = status.RecordingPaused,
		RecordingTimecode = status.RecordingTimecode,
		IsStreaming = status.IsStreaming,
		StreamingTimecode = status.StreamingTimecode,
		VirtualCamActive = status.VirtualCamActive,
		ReplayBufferActive = status.ReplayBufferActive,
		StudioModeActive = status.StudioModeActive,
		Fps = status.Fps,
		MaxFps = status.MaxFps,
		CpuUsage = status.CpuUsage,
		StreamingBitrateKbps = BitrateKbps(status.IsStreaming,
			status.StreamBytes,
			previous?.StreamBytes,
			previous?.StreamingBitrateKbps ?? 0,
			Elapsed(previous, nowUtc)),
		RecordingBitrateKbps = BitrateKbps(status.IsRecording,
			status.RecordBytes,
			previous?.RecordBytes,
			previous?.RecordingBitrateKbps ?? 0,
			Elapsed(previous, nowUtc)),
		DroppedFrames = status.StreamSkippedFrames,
		DroppedFramesPercent = Percent(status.StreamSkippedFrames, status.StreamTotalFrames),
		EncoderSkippedFrames = status.EncoderSkippedFrames,
		EncoderSkippedFramesPercent = Percent(status.EncoderSkippedFrames, status.EncoderTotalFrames),
		StreamBytes = status.StreamBytes,
		RecordBytes = status.RecordBytes,
		SampledAtUtc = nowUtc
	};

	private static TimeSpan? Elapsed(ObsState? previous, DateTime nowUtc)
		=> previous is { IsConnected: true, SampledAtUtc: { } sampledAt } ? nowUtc - sampledAt : null;

	// OBS reports no bitrate, only a cumulative byte counter per output, so the rate is the delta
	// between two samples. State-change events refresh the status outside the poll interval, which can
	// put two samples milliseconds apart; measuring over that window turns rounding noise into a wild
	// spike, so a too-short window carries the previous rate instead of recomputing.
	private static double BitrateKbps(bool outputActive,
		long bytes,
		long? previousBytes,
		double previousKbps,
		TimeSpan? elapsed)
	{
		if (!outputActive)
		{
			return 0;
		}

		if (elapsed is not { TotalSeconds: > 0 } window || previousBytes is not { } before)
		{
			return 0;
		}

		if (window < TimeSpan.FromSeconds(0.5))
		{
			return previousKbps;
		}

		// A restarted stream or recording resets the counter; a negative delta is that, not a rate.
		return bytes < before ? 0 : (bytes - before) * 8 / window.TotalSeconds / 1000;
	}

	private static double Percent(long part, long total) => total > 0 ? part * 100d / total : 0;
}
