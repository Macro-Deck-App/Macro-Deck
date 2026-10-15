using System.Net.Sockets;
using MacroDeckHost.Integrations.Imap.Configuration;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

namespace MacroDeckHost.Integrations.Imap.Protocol;

internal sealed class MailKitImapSessionFactory : IImapSessionFactory
{
	public IImapSession Create()
	{
		return new MailKitImapSession();
	}
}

internal sealed class MailKitImapSession : IImapSession
{
	private static readonly TimeSpan _commandTimeout = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan _logoutTimeout = TimeSpan.FromSeconds(3);

	private static readonly IFetchRequest _summaryRequest = new FetchRequest(MessageSummaryItems.UniqueId
		| MessageSummaryItems.Envelope
		| MessageSummaryItems.Flags
		| MessageSummaryItems.InternalDate
		| MessageSummaryItems.PreviewText);

	// Never given a protocol logger: the transcript would contain the LOGIN password. Chain and host name
	// are always validated; revocation is not, as .NET's own default, since an unreachable CRL rejects valid servers.
	private readonly ImapClient _client = new()
	{
		Timeout = (int)_commandTimeout.TotalMilliseconds,
		CheckCertificateRevocation = false
	};

	private IMailFolder? _folder;
	private volatile bool _fetching;

	public event EventHandler? MailboxChanged;

	public bool SupportsIdle => _client.Capabilities.HasFlag(ImapCapabilities.Idle);

	public int MessageCount => _folder?.Count ?? 0;

	public uint UidValidity => _folder?.UidValidity ?? 0;

	public async Task OpenAsync(ImapAccountSettings settings, CancellationToken cancellationToken)
	{
		var options = settings.Security == ImapSecurity.StartTls
			? SecureSocketOptions.StartTls
			: SecureSocketOptions.SslOnConnect;

		await RunAsync(() => _client.ConnectAsync(settings.Host, settings.Port, options, cancellationToken))
			.ConfigureAwait(false);
		await RunAsync(() => _client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken))
			.ConfigureAwait(false);

		IMailFolder folder;
		try
		{
			folder = string.Equals(settings.Folder,
				ImapAccountSettings.DefaultFolder,
				StringComparison.OrdinalIgnoreCase)
				? _client.Inbox
				: await _client.GetFolderAsync(settings.Folder, cancellationToken).ConfigureAwait(false);
			await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is FolderNotFoundException or ImapCommandException)
		{
			throw new ImapSessionException(ImapFailure.FolderNotFound, ex.Message, ex);
		}
		catch (Exception ex) when (Translate(ex) is { } translated)
		{
			throw translated;
		}

		folder.CountChanged += OnMailboxChanged;
		folder.MessageExpunged += OnMailboxChanged;
		folder.MessagesVanished += OnMailboxChanged;
		folder.UidValidityChanged += OnMailboxChanged;
		folder.MessageFlagsChanged += OnFlagsChanged;
		_folder = folder;
	}

	public async Task<IReadOnlyList<uint>> SearchUnreadAsync(CancellationToken cancellationToken)
	{
		var folder = RequireFolder();
		var uids = await RunAsync(() => folder.SearchAsync(SearchQuery.NotSeen, cancellationToken))
			.ConfigureAwait(false);
		return [.. uids.Select(uid => uid.Id)];
	}

	public async Task<IReadOnlyList<ImapMessageSummary>> FetchLatestAsync(
		int count,
		CancellationToken cancellationToken)
	{
		var folder = RequireFolder();
		if (folder.Count == 0)
		{
			return [];
		}

		return await FetchSummariesAsync(() =>
				folder.FetchAsync(Math.Max(0, folder.Count - count), -1, _summaryRequest, cancellationToken))
			.ConfigureAwait(false);
	}

	public async Task<IReadOnlyList<ImapMessageSummary>> FetchAsync(
		IReadOnlyList<uint> uids,
		CancellationToken cancellationToken)
	{
		var folder = RequireFolder();
		if (uids.Count == 0)
		{
			return [];
		}

		var ids = uids.Select(uid => new UniqueId(folder.UidValidity, uid)).ToList();
		return await FetchSummariesAsync(() => folder.FetchAsync(ids, _summaryRequest, cancellationToken))
			.ConfigureAwait(false);
	}

	public Task IdleAsync(CancellationToken doneToken, CancellationToken cancellationToken)
	{
		return RunAsync(() => _client.IdleAsync(doneToken, cancellationToken));
	}

	public Task NoOpAsync(CancellationToken cancellationToken)
	{
		return RunAsync(() => _client.NoOpAsync(cancellationToken));
	}

	public async ValueTask DisposeAsync()
	{
		if (_folder is { } folder)
		{
			folder.CountChanged -= OnMailboxChanged;
			folder.MessageExpunged -= OnMailboxChanged;
			folder.MessagesVanished -= OnMailboxChanged;
			folder.UidValidityChanged -= OnMailboxChanged;
			folder.MessageFlagsChanged -= OnFlagsChanged;
		}

		try
		{
			if (_client.IsConnected)
			{
				using var timeout = new CancellationTokenSource(_logoutTimeout);
				await _client.DisconnectAsync(true, timeout.Token).ConfigureAwait(false);
			}
		}
		catch (Exception)
		{
			// Logging out is a courtesy; the socket is closed by Dispose either way.
		}
		finally
		{
			_client.Dispose();
		}
	}

	private async Task<IReadOnlyList<ImapMessageSummary>> FetchSummariesAsync(
		Func<Task<IList<IMessageSummary>>> fetch)
	{
		_fetching = true;
		try
		{
			var summaries = await RunAsync(fetch).ConfigureAwait(false);
			return [.. summaries.Select(Map)];
		}
		finally
		{
			_fetching = false;
		}
	}

	private static ImapMessageSummary Map(IMessageSummary summary)
	{
		var sender = summary.Envelope?.From.Mailboxes.FirstOrDefault();
		return new ImapMessageSummary(summary.UniqueId.Id,
			Blank(sender?.Name),
			Blank(sender?.Address),
			summary.Envelope?.Subject?.Trim() ?? string.Empty,
			Blank(summary.PreviewText?.Trim()),
			summary.InternalDate ?? summary.Envelope?.Date ?? DateTimeOffset.UnixEpoch,
			summary.Flags?.HasFlag(MessageFlags.Seen) == true);
	}

	private static string? Blank(string? value)
	{
		return string.IsNullOrWhiteSpace(value) ? null : value;
	}

	private IMailFolder RequireFolder()
	{
		return _folder ?? throw new ImapSessionException(ImapFailure.Unreachable, "The IMAP folder is not open.");
	}

	private void OnMailboxChanged(object? sender, EventArgs e)
	{
		MailboxChanged?.Invoke(this, EventArgs.Empty);
	}

	// The flags of our own FETCH are not a change, and treating them as one would re-read forever.
	private void OnFlagsChanged(object? sender, EventArgs e)
	{
		if (!_fetching)
		{
			MailboxChanged?.Invoke(this, EventArgs.Empty);
		}
	}

	private static async Task RunAsync(Func<Task> operation)
	{
		try
		{
			await operation().ConfigureAwait(false);
		}
		catch (Exception ex) when (Translate(ex) is { } translated)
		{
			throw translated;
		}
	}

	private static async Task<T> RunAsync<T>(Func<Task<T>> operation)
	{
		try
		{
			return await operation().ConfigureAwait(false);
		}
		catch (Exception ex) when (Translate(ex) is { } translated)
		{
			throw translated;
		}
	}

	private static ImapSessionException? Translate(Exception exception)
	{
		return exception switch
		{
			OperationCanceledException => null,
			ImapSessionException => null,
			AuthenticationException => new ImapSessionException(ImapFailure.Authentication,
				exception.Message,
				exception),
			SslHandshakeException => new ImapSessionException(ImapFailure.Certificate, exception.Message, exception),
			SocketException
				or IOException
				or TimeoutException
				or ProtocolException
				or ServiceNotConnectedException
				or ServiceNotAuthenticatedException
				or ImapCommandException
				or InvalidOperationException
				=> new ImapSessionException(ImapFailure.Unreachable, exception.Message, exception),
			_ => null
		};
	}
}
