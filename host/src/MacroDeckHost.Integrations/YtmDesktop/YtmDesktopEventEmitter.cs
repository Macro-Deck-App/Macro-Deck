using MacroDeckHost.Integrations.YtmDesktop.Protocol;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Integrations.YtmDesktop;

internal sealed class YtmDesktopEventEmitter
{
	private readonly IEventPublisher _publisher;

	public YtmDesktopEventEmitter(IEventPublisher publisher)
	{
		_publisher = publisher;
	}

	public void PublishLikeChanged(
		YtmLikeStatus? previous,
		YtmLikeStatus current,
		string? trackName,
		string? videoId)
		=> _publisher.Publish(YtmDesktopEventIds.LikeChanged,
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["likeStatus"] = Describe(current),
				["previousLikeStatus"] = previous is { } value ? Describe(value) : null,
				["trackName"] = trackName,
				["videoId"] = videoId
			});

	internal static string Describe(YtmLikeStatus status)
		=> status switch
		{
			YtmLikeStatus.Like => "like",
			YtmLikeStatus.Dislike => "dislike",
			YtmLikeStatus.Indifferent => "indifferent",
			_ => "unknown"
		};
}
