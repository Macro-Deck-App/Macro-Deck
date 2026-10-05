namespace MacroDeckHost.Integrations.YouTube.Protocol;

internal interface IYouTubeApiClient
{
	Task<IReadOnlyList<YouTubeBroadcast>> ListBroadcastsAsync(string broadcastStatus, CancellationToken cancellationToken);

	Task<YouTubeVideo?> GetVideoAsync(string videoId, CancellationToken cancellationToken);

	Task<YouTubeChannel?> GetMyChannelAsync(CancellationToken cancellationToken);

	Task<YouTubeChatPage> ListChatMessagesAsync(
		string liveChatId,
		string? pageToken,
		CancellationToken cancellationToken);

	Task<string> InsertChatMessageAsync(string liveChatId, string text, CancellationToken cancellationToken);

	Task DeleteChatMessageAsync(string messageId, CancellationToken cancellationToken);

	Task<string> InsertBanAsync(
		string liveChatId,
		string channelId,
		long? durationSeconds,
		CancellationToken cancellationToken);

	Task DeleteBanAsync(string banId, CancellationToken cancellationToken);

	Task UpdateVideoSnippetAsync(string videoId, YouTubeVideoSnippet snippet, CancellationToken cancellationToken);

	Task<string> TransitionBroadcastAsync(
		string broadcastId,
		string broadcastStatus,
		CancellationToken cancellationToken);

	Task InsertCuepointAsync(string broadcastId, int durationSeconds, CancellationToken cancellationToken);
}
