namespace MacroDeckHost.Application.Twitch.Chat;

public enum TwitchChatModerationKind
{
	DeleteMessage,

	Timeout,

	Ban,

	Unban
}

public sealed record TwitchChatModerationRequest(
	TwitchChatModerationKind Kind,
	string? MessageId = null,
	string? ChatterId = null,
	int DurationSeconds = 0)
{
	public static TwitchChatModerationRequest Delete(string messageId) => new(TwitchChatModerationKind.DeleteMessage,
		MessageId: messageId);

	public static TwitchChatModerationRequest Timeout(string chatterId, int seconds)
		=> new(TwitchChatModerationKind.Timeout, ChatterId: chatterId, DurationSeconds: seconds);

	public static TwitchChatModerationRequest Ban(string chatterId)
		=> new(TwitchChatModerationKind.Ban, ChatterId: chatterId);

	public static TwitchChatModerationRequest Unban(string chatterId)
		=> new(TwitchChatModerationKind.Unban, ChatterId: chatterId);
}

public enum TwitchChatModerationResult
{
	Succeeded,

	AccountUnavailable,

	MissingScope,

	NotPermitted,

	Refused,

	Failed
}

public interface ITwitchChatModerator
{
	Task<TwitchChatModerationResult> ModerateAsync(
		string accountId,
		TwitchChatModerationRequest request,
		CancellationToken cancellationToken);
}
