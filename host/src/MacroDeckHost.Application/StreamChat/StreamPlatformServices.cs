using MacroDeckHost.Application.StreamStats;

namespace MacroDeckHost.Application.StreamChat;

public sealed record StreamPlatformServiceSet(
	StreamPlatform Platform,
	IStreamChatSink ChatSink,
	IStreamChatFeed ChatFeed,
	IStreamStatsSink StatsSink,
	IStreamStatsAccounts StatsAccounts,
	IStreamThumbnails Thumbnails,
	ITwitchChatImages? ChatImages = null)
{
	public static StreamPlatformServiceSet Create(
		StreamPlatform platform,
		StreamChatHub chat,
		StreamStatsAccountsHub stats,
		IStreamThumbnails thumbnails,
		ITwitchChatImages? chatImages = null)
		=> new(platform, chat, chat, stats, stats, thumbnails, chatImages);
}

public interface IStreamPlatformServices
{
	IReadOnlyList<StreamPlatformServiceSet> Platforms { get; }

	StreamPlatformServiceSet? Find(string ownerId);
}

public sealed class StreamPlatformServices : IStreamPlatformServices, IDisposable
{
	public StreamPlatformServices(IEnumerable<StreamPlatformServiceSet> platforms)
	{
		ArgumentNullException.ThrowIfNull(platforms);

		Platforms = [.. platforms];

		var duplicate = Platforms.GroupBy(set => set.Platform.OwnerId, StringComparer.Ordinal)
			.FirstOrDefault(group => group.Count() > 1);

		if (duplicate is not null)
		{
			throw new ArgumentException($"Stream platform {duplicate.Key} is registered twice", nameof(platforms));
		}
	}

	public IReadOnlyList<StreamPlatformServiceSet> Platforms { get; }

	public IEnumerable<StreamChatHub> ChatHubs => Platforms.Select(set => set.ChatFeed).OfType<StreamChatHub>();

	public StreamPlatformServiceSet? Find(string ownerId)
		=> Platforms.FirstOrDefault(set => string.Equals(set.Platform.OwnerId, ownerId, StringComparison.Ordinal));

	public void Dispose()
	{
		foreach (var set in Platforms)
		{
			(set.ChatFeed as IDisposable)?.Dispose();
			(set.Thumbnails as IDisposable)?.Dispose();
		}
	}
}
