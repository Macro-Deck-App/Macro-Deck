using MacroDeckHost.Integrations.YtmDesktop.Protocol;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeckHost.Integrations.YtmDesktop;

internal sealed record YtmDesktopSnapshot
{
	public static YtmDesktopSnapshot Disconnected { get; } = new();

	public MusicPlayerState Player { get; init; } = MusicPlayerState.Disconnected;

	public string? VideoId { get; init; }

	public bool? Muted { get; init; }

	public YtmLikeStatus? LikeStatus { get; init; }

	public bool? IsLive { get; init; }

	public YtmVideoType? VideoType { get; init; }

	public bool? AdPlaying { get; init; }

	public bool? ShuffleEnabled { get; init; }

	public YtmRepeatMode? RepeatMode { get; init; }

	public IReadOnlyList<YtmQueueItem> Queue { get; init; } = [];
}
