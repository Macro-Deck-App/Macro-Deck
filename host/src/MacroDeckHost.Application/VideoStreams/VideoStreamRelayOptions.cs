namespace MacroDeckHost.Application.VideoStreams;

public sealed class VideoStreamRelayOptions
{
	public static readonly TimeSpan DefaultResponseHeaderTimeout = TimeSpan.FromSeconds(15);

	public static readonly TimeSpan DefaultIdleReadTimeout = TimeSpan.FromSeconds(30);

	public TimeSpan ResponseHeaderTimeout { get; init; } = DefaultResponseHeaderTimeout;

	public TimeSpan IdleReadTimeout { get; init; } = DefaultIdleReadTimeout;
}
