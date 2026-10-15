using System.Collections.Concurrent;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Integrations.Imap.Configuration;
using MacroDeckHost.Integrations.Imap.Connections;
using MacroDeckHost.Integrations.Imap.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Imap;

internal sealed record FakeMail(
	uint Uid,
	string Subject,
	bool Seen,
	DateTimeOffset ReceivedAt,
	string Sender = "Sam Sender");

internal sealed class FakeImapServer : IImapSessionFactory
{
	private readonly Lock _gate = new();
	private readonly List<FakeMail> _mails = [];
	private FakeImapSession? _active;
	private int _sessions;

	public uint UidValidity { get; set; } = 7;

	public bool SupportsIdle { get; set; } = true;

	public volatile bool Unreachable;

	public volatile bool RejectSignIn;

	public volatile bool MissingFolder;

	public volatile bool UntrustedCertificate;

	public int SessionsCreated => Volatile.Read(ref _sessions);

	public ImapAccountSettings? LastOpened { get; private set; }

	public FakeImapSession? Active
	{
		get
		{
			lock (_gate)
			{
				return _active;
			}
		}
	}

	public IReadOnlyList<FakeMail> Mails
	{
		get
		{
			lock (_gate)
			{
				return [.. _mails];
			}
		}
	}

	public IImapSession Create()
	{
		Interlocked.Increment(ref _sessions);
		return new FakeImapSession(this);
	}

	public void Seed(int count, Func<uint, bool>? seen = null)
	{
		lock (_gate)
		{
			for (uint uid = 1; uid <= count; uid++)
			{
				_mails.Add(new FakeMail(uid,
					$"Subject {uid}",
					seen?.Invoke(uid) ?? true,
					new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(uid)));
			}
		}
	}

	public FakeMail Deliver(string subject)
	{
		FakeMail mail;
		lock (_gate)
		{
			var uid = _mails.Count == 0 ? 1 : _mails.Max(item => item.Uid) + 1;
			mail = new FakeMail(uid, subject, false, DateTimeOffset.UtcNow);
			_mails.Add(mail);
		}

		Active?.RaiseMailboxChanged();
		return mail;
	}

	public void MarkSeen(uint uid)
	{
		lock (_gate)
		{
			var index = _mails.FindIndex(item => item.Uid == uid);
			_mails[index] = _mails[index] with { Seen = true };
		}

		Active?.RaiseMailboxChanged();
	}

	public void DropConnection()
	{
		Unreachable = true;
		Active?.Drop();
	}

	internal void Opened(FakeImapSession session, ImapAccountSettings settings)
	{
		lock (_gate)
		{
			_active = session;
			LastOpened = settings;
		}
	}
}

internal sealed class FakeImapSession(FakeImapServer server) : IImapSession
{
	private readonly TaskCompletionSource _dropped = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public event EventHandler? MailboxChanged;

	public bool SupportsIdle => server.SupportsIdle;

	public int MessageCount => server.Mails.Count;

	public uint UidValidity => server.UidValidity;

	public bool Disposed { get; private set; }

	public Task OpenAsync(ImapAccountSettings settings, CancellationToken cancellationToken)
	{
		if (server.Unreachable)
		{
			throw new ImapSessionException(ImapFailure.Unreachable, "unreachable");
		}

		if (server.UntrustedCertificate)
		{
			throw new ImapSessionException(ImapFailure.Certificate, "untrusted certificate");
		}

		if (server.RejectSignIn)
		{
			throw new ImapSessionException(ImapFailure.Authentication, "rejected");
		}

		if (server.MissingFolder)
		{
			throw new ImapSessionException(ImapFailure.FolderNotFound, "no folder");
		}

		server.Opened(this, settings);
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<uint>> SearchUnreadAsync(CancellationToken cancellationToken)
	{
		ThrowIfDropped();
		return Task.FromResult<IReadOnlyList<uint>>(
			[.. server.Mails.Where(mail => !mail.Seen).Select(mail => mail.Uid)]);
	}

	public Task<IReadOnlyList<ImapMessageSummary>> FetchLatestAsync(int count, CancellationToken cancellationToken)
	{
		ThrowIfDropped();
		return Task.FromResult<IReadOnlyList<ImapMessageSummary>>([
			.. server.Mails.OrderByDescending(mail => mail.Uid).Take(count).Select(Summary)
		]);
	}

	public Task<IReadOnlyList<ImapMessageSummary>> FetchAsync(
		IReadOnlyList<uint> uids,
		CancellationToken cancellationToken)
	{
		ThrowIfDropped();
		return Task.FromResult<IReadOnlyList<ImapMessageSummary>>([
			.. server.Mails.Where(mail => uids.Contains(mail.Uid)).Select(Summary)
		]);
	}

	public async Task IdleAsync(CancellationToken doneToken, CancellationToken cancellationToken)
	{
		ThrowIfDropped();
		var done = Task.Delay(Timeout.Infinite, doneToken);
		var cancelled = Task.Delay(Timeout.Infinite, cancellationToken);
		var finished = await Task.WhenAny(done, cancelled, _dropped.Task).ConfigureAwait(false);
		if (finished == cancelled)
		{
			throw new OperationCanceledException(cancellationToken);
		}

		ThrowIfDropped();
	}

	public Task NoOpAsync(CancellationToken cancellationToken)
	{
		ThrowIfDropped();
		return Task.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		Disposed = true;
		return ValueTask.CompletedTask;
	}

	internal void RaiseMailboxChanged()
	{
		MailboxChanged?.Invoke(this, EventArgs.Empty);
	}

	internal void Drop()
	{
		_dropped.TrySetResult();
	}

	private void ThrowIfDropped()
	{
		if (_dropped.Task.IsCompleted)
		{
			throw new ImapSessionException(ImapFailure.Unreachable, "connection lost");
		}
	}

	private static ImapMessageSummary Summary(FakeMail mail)
	{
		return new ImapMessageSummary(mail.Uid,
			mail.Sender,
			"sam@example.com",
			mail.Subject,
			$"Preview of {mail.Subject}",
			mail.ReceivedAt,
			mail.Seen);
	}
}

internal sealed class ImapTestContext : IIntegrationContext
{
	public ImapTestConfig ConfigStore { get; } = new();

	public IIntegrationConfig Config => ConfigStore;

	public IVariableApi Variables => throw new NotSupportedException();
	public IUserVariableApi UserVariables => throw new NotSupportedException();
	public IDeckNavigator Deck => throw new NotSupportedException();
	public IScriptApi Scripts => throw new NotSupportedException();
	public IWidgetApi Widgets => throw new NotSupportedException();
	public IEventPublisher Events => throw new NotSupportedException();
	public MacroDeck.Sdk.Notifications.IUserNotifier Notifications => throw new NotSupportedException();

	public Guid AddAccount(
		string title,
		string host = "imap.example.com",
		string username = "alex@example.com",
		string password = "secret",
		string port = "993",
		string folder = "INBOX")
	{
		return ConfigStore.AddEntry(title,
			new Dictionary<string, string?>
			{
				[ImapConfigKeys.Host] = host,
				[ImapConfigKeys.Port] = port,
				[ImapConfigKeys.Security] = ImapConfigKeys.SecuritySslTls,
				[ImapConfigKeys.Username] = username,
				[ImapConfigKeys.Folder] = folder
			},
			new Dictionary<string, string> { [ImapConfigKeys.Password] = password });
	}
}

internal sealed class ImapTestConfig : IIntegrationConfig
{
	private readonly Lock _gate = new();
	private readonly List<ConfigEntrySnapshot> _entries = [];
	private readonly ConcurrentDictionary<(Guid EntryId, string Key), string?> _strings = new();
	private readonly ConcurrentDictionary<(Guid EntryId, string Key), string> _secrets = new();

	public Guid AddEntry(
		string title,
		IReadOnlyDictionary<string, string?> values,
		IReadOnlyDictionary<string, string> secrets)
	{
		var entryId = Guid.NewGuid();
		lock (_gate)
		{
			_entries.Add(new ConfigEntrySnapshot(entryId, title));
		}

		foreach (var (key, value) in values)
		{
			_strings[(entryId, key)] = value;
		}

		foreach (var (key, value) in secrets)
		{
			_secrets[(entryId, key)] = value;
		}

		return entryId;
	}

	public void RemoveEntry(Guid entryId)
	{
		lock (_gate)
		{
			_entries.RemoveAll(entry => entry.Id == entryId);
		}
	}

	public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
	{
		lock (_gate)
		{
			return Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>([.. _entries]);
		}
	}

	public Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(_strings.GetValueOrDefault((entryId, key)));
	}

	public Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
	{
		return Task.FromResult<string?>(_secrets.GetValueOrDefault((entryId, key)));
	}

	public Task SetStringAsync(Guid entryId, string key, string? value, CancellationToken cancellationToken = default)
	{
		_strings[(entryId, key)] = value;
		return Task.CompletedTask;
	}

	public Task SetSecretAsync(Guid entryId, string key, string value, CancellationToken cancellationToken = default)
	{
		_secrets[(entryId, key)] = value;
		return Task.CompletedTask;
	}
}

internal sealed class TestFlowContext(string? entryTitle = null) : IConfigFlowEntryContext
{
	public IOAuthSession OAuth => throw new NotSupportedException();

	public string? EntryTitle { get; } = entryTitle;
}

internal static class ImapTestTimings
{
	public static ImapConnectionTimings Fast { get; } = new(TimeSpan.FromMilliseconds(40),
		TimeSpan.FromMilliseconds(160),
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMilliseconds(40));

	public static async Task WaitUntilAsync(Func<bool> condition, string because)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail($"Timed out waiting until {because}.");
			}

			await Task.Delay(10);
		}
	}
}
