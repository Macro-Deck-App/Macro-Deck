namespace MacroDeckHost.Tests.UnitTests.YouTube;

internal sealed class YouTubeManualClock : TimeProvider
{
	public YouTubeManualClock(DateTimeOffset now)
	{
		Now = now;
	}

	public DateTimeOffset Now { get; set; }

	public override DateTimeOffset GetUtcNow() => Now;
}
