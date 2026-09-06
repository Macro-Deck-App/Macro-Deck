using MacroDeckHost.Integrations.Obs;

namespace MacroDeckHost.Tests.UnitTests.Obs;

[TestFixture]
internal sealed class ObsStateTests
{
	private static readonly DateTime _start = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

	[Test]
	public void StreamingBitrate_IsTheByteDeltaOverElapsedTime()
	{
		var first = ObsState.FromStatus(Streaming(0), null, _start);
		var second = ObsState.FromStatus(Streaming(125_000), first, _start.AddSeconds(1));

		Assert.That(ObsVariables.Read(second, "streaming_bitrate"), Is.EqualTo(1000));
	}

	[Test]
	public void RecordingBitrate_UsesElapsedTimeRatherThanASingleSecondAssumption()
	{
		var first = ObsState.FromStatus(Recording(0), null, _start);
		var second = ObsState.FromStatus(Recording(250_000), first, _start.AddSeconds(2));

		Assert.That(ObsVariables.Read(second, "recording_bitrate"), Is.EqualTo(1000));
	}

	[Test]
	public void StreamingAndRecordingBitrate_AreMeasuredIndependently()
	{
		var first = ObsState.FromStatus(Streaming(0) with { IsRecording = true, RecordBytes = 0 }, null, _start);
		var second = ObsState.FromStatus(Streaming(125_000) with { IsRecording = true, RecordBytes = 0 },
			first,
			_start.AddSeconds(1));

		Assert.Multiple(() =>
		{
			Assert.That(ObsVariables.Read(second, "streaming_bitrate"), Is.EqualTo(1000));
			Assert.That(ObsVariables.Read(second, "recording_bitrate"), Is.EqualTo(0));
		});
	}

	[Test]
	public void Bitrate_WhenTheOutputIsNotActive_IsZero()
	{
		var first = ObsState.FromStatus(Streaming(0), null, _start);
		var second = ObsState.FromStatus(new ObsStatus { StreamBytes = 125_000 }, first, _start.AddSeconds(1));

		Assert.That(ObsVariables.Read(second, "streaming_bitrate"), Is.EqualTo(0));
	}

	[Test]
	public void Bitrate_WhenTheByteCounterRestarts_IsZeroRatherThanNegative()
	{
		var first = ObsState.FromStatus(Streaming(5_000_000), null, _start);
		var second = ObsState.FromStatus(Streaming(0), first, _start.AddSeconds(1));

		Assert.That(ObsVariables.Read(second, "streaming_bitrate"), Is.EqualTo(0));
	}

	[Test]
	public void Bitrate_ForSamplesTakenMillisecondsApart_KeepsThePreviousRate()
	{
		var first = ObsState.FromStatus(Streaming(0), null, _start);
		var measured = ObsState.FromStatus(Streaming(125_000), first, _start.AddSeconds(1));
		var immediate = ObsState.FromStatus(Streaming(125_400), measured, _start.AddSeconds(1).AddMilliseconds(100));

		Assert.That(ObsVariables.Read(immediate, "streaming_bitrate"), Is.EqualTo(1000));
	}

	[Test]
	public void DroppedFrames_ReportTheStreamsSkippedFramesAndTheirShare()
	{
		var state = ObsState.FromStatus(Streaming(0) with { StreamSkippedFrames = 5, StreamTotalFrames = 1000 },
			null,
			_start);

		Assert.Multiple(() =>
		{
			Assert.That(ObsVariables.Read(state, "dropped_frames"), Is.EqualTo(5));
			Assert.That(ObsVariables.Read(state, "dropped_frames_percent"), Is.EqualTo(0.5));
		});
	}

	[Test]
	public void DroppedFramesPercent_WithoutAnyFramesYet_IsZero()
	{
		var state = ObsState.FromStatus(Streaming(0), null, _start);

		Assert.That(ObsVariables.Read(state, "dropped_frames_percent"), Is.EqualTo(0));
	}

	[Test]
	public void EncoderSkippedFrames_AreReportedSeparatelyFromTheStreamsDroppedFrames()
	{
		var status = Streaming(0) with
		{
			StreamSkippedFrames = 5,
			StreamTotalFrames = 1000,
			EncoderSkippedFrames = 20,
			EncoderTotalFrames = 500
		};

		var state = ObsState.FromStatus(status, null, _start);

		Assert.Multiple(() =>
		{
			Assert.That(ObsVariables.Read(state, "skipped_frames"), Is.EqualTo(20));
			Assert.That(ObsVariables.Read(state, "skipped_frames_percent"), Is.EqualTo(4));
			Assert.That(ObsVariables.Read(state, "dropped_frames"), Is.EqualTo(5));
		});
	}

	[TestCase(30000, 1001, 29.97)]
	[TestCase(60, 1, 60d)]
	public void MaxFps_ReportsTheConfiguredFrameRate(int numerator, int denominator, double expected)
	{
		var state = ObsState.FromStatus(new ObsStatus { MaxFps = numerator / (double)denominator }, null, _start);

		Assert.That(ObsVariables.Read(state, "max_fps"), Is.EqualTo(expected));
	}

	[Test]
	public void WhileDisconnected_TheNewVariablesAreUnavailable()
	{
		string[] slots =
		[
			"max_fps",
			"streaming_bitrate",
			"recording_bitrate",
			"dropped_frames",
			"dropped_frames_percent",
			"skipped_frames",
			"skipped_frames_percent"
		];

		Assert.Multiple(() =>
		{
			foreach (var slot in slots)
			{
				Assert.That(ObsVariables.Read(ObsState.Disconnected, slot), Is.Null, slot);
			}
		});
	}

	private static ObsStatus Streaming(long bytesSent) => new() { IsStreaming = true, StreamBytes = bytesSent };

	private static ObsStatus Recording(long bytes) => new() { IsRecording = true, RecordBytes = bytes };
}
