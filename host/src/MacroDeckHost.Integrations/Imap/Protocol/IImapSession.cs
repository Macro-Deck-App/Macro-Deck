using MacroDeckHost.Integrations.Imap.Configuration;

namespace MacroDeckHost.Integrations.Imap.Protocol;

internal interface IImapSessionFactory
{
	IImapSession Create();
}

internal interface IImapSession : IAsyncDisposable
{
	event EventHandler? MailboxChanged;

	bool SupportsIdle { get; }

	int MessageCount { get; }

	uint UidValidity { get; }

	Task OpenAsync(ImapAccountSettings settings, CancellationToken cancellationToken);

	Task<IReadOnlyList<uint>> SearchUnreadAsync(CancellationToken cancellationToken);

	Task<IReadOnlyList<ImapMessageSummary>> FetchLatestAsync(int count, CancellationToken cancellationToken);

	Task<IReadOnlyList<ImapMessageSummary>> FetchAsync(IReadOnlyList<uint> uids, CancellationToken cancellationToken);

	Task IdleAsync(CancellationToken doneToken, CancellationToken cancellationToken);

	Task NoOpAsync(CancellationToken cancellationToken);
}

internal sealed record ImapMessageSummary(
	uint Uid,
	string? SenderName,
	string? SenderAddress,
	string Subject,
	string? Preview,
	DateTimeOffset ReceivedAt,
	bool IsSeen);

internal enum ImapFailure
{
	Unreachable,
	Certificate,
	Authentication,
	FolderNotFound
}

internal sealed class ImapSessionException(ImapFailure failure, string message, Exception? innerException = null)
	: Exception(message, innerException)
{
	public ImapFailure Failure { get; } = failure;
}
