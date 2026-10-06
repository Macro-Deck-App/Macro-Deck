namespace MacroDeckHost.Application.StreamChat;

public enum ChatModerationKind
{
	DeleteMessage,

	Timeout,

	Ban,

	Unban
}

public sealed record ChatModerationRequest(
	ChatModerationKind Kind,
	string? MessageId = null,
	string? AuthorId = null,
	int DurationSeconds = 0)
{
	public static ChatModerationRequest Delete(string messageId) => new(ChatModerationKind.DeleteMessage,
		MessageId: messageId);

	public static ChatModerationRequest Timeout(string authorId, int seconds)
		=> new(ChatModerationKind.Timeout, AuthorId: authorId, DurationSeconds: seconds);

	public static ChatModerationRequest Ban(string authorId)
		=> new(ChatModerationKind.Ban, AuthorId: authorId);

	public static ChatModerationRequest Unban(string authorId)
		=> new(ChatModerationKind.Unban, AuthorId: authorId);
}

public enum ChatModerationResult
{
	Succeeded,

	AccountUnavailable,

	MissingScope,

	NotPermitted,

	Refused,

	Failed,

	Unsupported
}

public interface IStreamChatModerator
{
	Task<ChatModerationResult> ModerateAsync(
		string accountId,
		ChatModerationRequest request,
		CancellationToken cancellationToken);
}
