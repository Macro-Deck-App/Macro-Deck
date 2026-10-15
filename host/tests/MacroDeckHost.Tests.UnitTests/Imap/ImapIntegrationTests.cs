using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Email;
using MacroDeck.Sdk.Issues;
using MacroDeckHost.Integrations.Imap;
using MacroDeckHost.Integrations.Imap.Configuration;
using MacroDeckHost.Integrations.Imap.Protocol;
using static MacroDeckHost.Tests.UnitTests.Imap.ImapTestTimings;

namespace MacroDeckHost.Tests.UnitTests.Imap;

[TestFixture]
internal sealed class ImapIntegrationTests
{
	private FakeImapServer _server = null!;
	private ImapTestContext _context = null!;
	private ImapIntegration _integration = null!;
	private ConcurrentQueue<string?> _changes = null!;

	[SetUp]
	public void SetUp()
	{
		_server = new FakeImapServer();
		_context = new ImapTestContext();
		_integration = new ImapIntegration(_server, Fast);
		_changes = new ConcurrentQueue<string?>();
		_integration.Changed += (_, args) => _changes.Enqueue(args.InboxId);
	}

	[TearDown]
	public async Task TearDown()
	{
		await _integration.ShutdownAsync();
		_integration.Dispose();
	}

	[Test]
	public async Task A_connected_inbox_reports_whole_inbox_counts_and_its_newest_messages_first()
	{
		_server.Seed(60, uid => uid is not (1 or 59));
		var inboxId = await StartWithOneAccountAsync();

		var latest = await _integration.GetSnapshotAsync(inboxId,
			new EmailMessageQuery { MaxMessages = 3 },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(latest.Status, Is.EqualTo(EmailInboxStatus.Connected));
			Assert.That(latest.TotalCount, Is.EqualTo(60));
			Assert.That(latest.UnreadCount, Is.EqualTo(2));
			Assert.That(latest.Messages.Select(message => message.Subject),
				Is.EqualTo(new[] { "Subject 60", "Subject 59", "Subject 58" }));
			Assert.That(latest.Messages[0].Id, Is.EqualTo("7-60"));
			Assert.That(latest.Messages[1].IsRead, Is.False);
			Assert.That(latest.Messages[0].IsRead, Is.True);
		});
	}

	[Test]
	public async Task Unread_only_finds_unread_mail_older_than_the_newest_fifty()
	{
		_server.Seed(60, uid => uid != 3);
		var inboxId = await StartWithOneAccountAsync();

		var newest = await _integration.GetSnapshotAsync(inboxId,
			new EmailMessageQuery { MaxMessages = 50 },
			CancellationToken.None);
		var unread = await _integration.GetSnapshotAsync(inboxId,
			new EmailMessageQuery { MaxMessages = 10, UnreadOnly = true },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(newest.Messages.Any(message => !message.IsRead), Is.False);
			Assert.That(unread.Messages.Select(message => message.Id), Is.EqualTo(new[] { "7-3" }));
			Assert.That(unread.UnreadCount, Is.EqualTo(1));
			Assert.That(unread.TotalCount, Is.EqualTo(60));
		});
	}

	[Test]
	public async Task New_mail_raises_changed_for_its_inbox_and_the_next_read_shows_it()
	{
		_server.Seed(2);
		var inboxId = await StartWithOneAccountAsync();
		var query = new EmailMessageQuery { MaxMessages = 5 };
		await _integration.GetSnapshotAsync(inboxId, query, CancellationToken.None);
		_changes.Clear();

		_server.Deliver("Invoice");

		await WaitUntilAsync(() => _changes.Contains(inboxId), "the inbox reported a change");
		var after = await _integration.GetSnapshotAsync(inboxId, query, CancellationToken.None);
		Assert.Multiple(() =>
		{
			Assert.That(after.Messages[0].Subject, Is.EqualTo("Invoice"));
			Assert.That(after.UnreadCount, Is.EqualTo(1));
			Assert.That(after.TotalCount, Is.EqualTo(3));
		});
	}

	[Test]
	public async Task A_message_marked_read_elsewhere_raises_changed_and_reads_as_read()
	{
		_server.Seed(3, uid => uid != 3);
		var inboxId = await StartWithOneAccountAsync();
		var query = new EmailMessageQuery { MaxMessages = 5 };
		Assert.That((await _integration.GetSnapshotAsync(inboxId, query, CancellationToken.None)).UnreadCount,
			Is.EqualTo(1));
		_changes.Clear();

		_server.MarkSeen(3);

		await WaitUntilAsync(() => _changes.Contains(inboxId), "the inbox reported a change");
		var after = await _integration.GetSnapshotAsync(inboxId, query, CancellationToken.None);
		Assert.Multiple(() =>
		{
			Assert.That(after.UnreadCount, Is.Zero);
			Assert.That(after.Messages[0].IsRead, Is.True);
		});
	}

	[Test]
	public async Task A_lost_connection_serves_the_last_snapshot_as_disconnected_and_reconnects()
	{
		_server.Seed(4, uid => uid != 4);
		var inboxId = await StartWithOneAccountAsync();
		var query = new EmailMessageQuery { MaxMessages = 5 };
		var before = await _integration.GetSnapshotAsync(inboxId, query, CancellationToken.None);

		_server.DropConnection();
		await WaitUntilAsync(()
				=> _integration.GetSnapshotAsync(inboxId, query, CancellationToken.None).Result.Status
				== EmailInboxStatus.Disconnected,
			"the inbox reads as disconnected");
		var during = await _integration.GetSnapshotAsync(inboxId, query, CancellationToken.None);
		await WaitUntilAsync(() => _server.SessionsCreated >= 3, "it kept trying to reconnect");

		_server.Unreachable = false;
		await WaitUntilAsync(()
				=> _integration.GetSnapshotAsync(inboxId, query, CancellationToken.None).Result.Status
				== EmailInboxStatus.Connected,
			"the inbox reconnected");

		Assert.Multiple(() =>
		{
			Assert.That(during.UnreadCount, Is.EqualTo(before.UnreadCount));
			Assert.That(during.Messages, Is.EqualTo(before.Messages));
			Assert.That(during.StatusMessage, Is.Not.Null.And.Not.Empty);
			Assert.That(_integration.GetInboxes().Single().Id, Is.EqualTo(inboxId));
		});
	}

	[Test]
	public async Task A_refused_sign_in_reports_an_error_and_an_issue_and_stops_retrying()
	{
		_server.Seed(2);
		var inboxId = await StartWithOneAccountAsync();
		var query = new EmailMessageQuery { MaxMessages = 5 };
		await _integration.GetSnapshotAsync(inboxId, query, CancellationToken.None);

		_server.RejectSignIn = true;
		_server.DropConnection();
		_server.Unreachable = false;
		await WaitUntilAsync(()
				=> _integration.GetSnapshotAsync(inboxId, query, CancellationToken.None).Result.Status
				== EmailInboxStatus.Error,
			"the inbox reads as failed");
		var attempts = _server.SessionsCreated;
		await Task.Delay(Fast.BackoffCap * 3);

		var snapshot = await _integration.GetSnapshotAsync(inboxId, query, CancellationToken.None);
		var issues = await _integration.GetIssuesAsync();
		Assert.Multiple(() =>
		{
			Assert.That(_server.SessionsCreated, Is.EqualTo(attempts));
			Assert.That(snapshot.TotalCount, Is.EqualTo(2));
			Assert.That(snapshot.StatusMessage, Is.Not.Null.And.Not.Empty);
			Assert.That(issues.Single().Severity, Is.EqualTo(IntegrationIssueSeverity.Error));
			Assert.That(issues.Single().ActionLabel.IsEmpty, Is.True);
		});
	}

	[Test]
	public async Task New_credentials_saved_after_a_refused_sign_in_connect_again()
	{
		_server.RejectSignIn = true;
		var entryId = _context.AddAccount("Work");
		await _integration.InitializeAsync(_context);
		await WaitUntilAsync(() => _integration.GetIssuesAsync().Result.Count == 1, "the sign-in issue appeared");

		_server.RejectSignIn = false;
		await _context.ConfigStore.SetSecretAsync(entryId, ImapConfigKeys.Password, "new-app-password");
		await _integration.ReloadEntriesAsync();

		await WaitUntilAsync(() => _server.LastOpened?.Password == "new-app-password", "the new password was used");
		await WaitUntilAsync(() => _integration.GetIssuesAsync().Result.Count == 0, "the issue cleared");
	}

	[TestCase(ImapFailure.Certificate, IntegrationIssueSeverity.Error)]
	[TestCase(ImapFailure.FolderNotFound, IntegrationIssueSeverity.Warning)]
	public async Task A_certificate_or_folder_failure_is_an_issue_and_asks_for_reconfiguration_until_it_is_fixed(
		ImapFailure failure,
		IntegrationIssueSeverity severity)
	{
		SetFailure(failure, true);
		var entryId = _context.AddAccount("Work");
		await _integration.InitializeAsync(_context);
		await WaitUntilAsync(() => _integration.GetIssuesAsync().Result.Count == 1, "the issue appeared");

		var issue = (await _integration.GetIssuesAsync()).Single();
		var status = await _integration.GetEntryStatusAsync(Entry(entryId));
		Assert.Multiple(() =>
		{
			Assert.That(issue.Severity, Is.EqualTo(severity));
			Assert.That(issue.Title.IsEmpty, Is.False);
			Assert.That(issue.Description.IsEmpty, Is.False);
			Assert.That(issue.ActionLabel.IsEmpty, Is.True);
			Assert.That(status, Is.EqualTo(ConfigEntryStatus.NeedsReconfiguration));
		});

		SetFailure(failure, false);
		await WaitUntilAsync(() => _integration.GetIssuesAsync().Result.Count == 0, "the issue cleared");
		Assert.That(await _integration.GetEntryStatusAsync(Entry(entryId)), Is.EqualTo(ConfigEntryStatus.Connected));
	}

	[Test]
	public async Task An_unreachable_server_is_transient_and_raises_no_issue()
	{
		_server.Unreachable = true;
		var entryId = _context.AddAccount("Work");
		await _integration.InitializeAsync(_context);
		await WaitUntilAsync(() => _server.SessionsCreated >= 2, "it tried to connect");

		var issues = await _integration.GetIssuesAsync();
		var status = await _integration.GetEntryStatusAsync(Entry(entryId));
		Assert.Multiple(() =>
		{
			Assert.That(issues, Is.Empty);
			Assert.That(status, Is.EqualTo(ConfigEntryStatus.Reconnecting));
		});
	}

	[Test]
	public async Task Adding_and_removing_an_account_raises_an_inbox_list_change()
	{
		var first = await StartWithOneAccountAsync();
		_changes.Clear();

		var second = _context.AddAccount("Private", username: "alex@private.example");
		await _integration.ReloadEntriesAsync();
		var afterAdd = _integration.GetInboxes().Select(inbox => inbox.Id).ToList();
		_context.ConfigStore.RemoveEntry(second);
		await _integration.ReloadEntriesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(afterAdd, Has.Count.EqualTo(2));
			Assert.That(_integration.GetInboxes().Select(inbox => inbox.Id), Is.EqualTo(new[] { first }));
			Assert.That(_changes.Count(id => id is null), Is.EqualTo(2));
		});
	}

	[Test]
	public async Task An_unknown_inbox_id_is_a_failure()
	{
		await StartWithOneAccountAsync();

		Assert.ThrowsAsync<InvalidOperationException>(() =>
			_integration.GetSnapshotAsync("i0000000000000000", new EmailMessageQuery(), CancellationToken.None));
	}

	private void SetFailure(ImapFailure failure, bool on)
	{
		if (failure == ImapFailure.Certificate)
		{
			_server.UntrustedCertificate = on;
		}
		else
		{
			_server.MissingFolder = on;
		}
	}

	private static StoredConfigEntry Entry(Guid entryId)
	{
		return new StoredConfigEntry
		{
			Id = entryId,
			Title = "Work",
			Values = new Dictionary<string, JsonElement>
			{
				[ImapConfigKeys.Host] = JsonSerializer.SerializeToElement("imap.example.com"),
				[ImapConfigKeys.Port] = JsonSerializer.SerializeToElement("993"),
				[ImapConfigKeys.Username] = JsonSerializer.SerializeToElement("alex@example.com"),
				[ImapConfigKeys.Folder] = JsonSerializer.SerializeToElement("INBOX")
			}
		};
	}

	private async Task<string> StartWithOneAccountAsync()
	{
		_context.AddAccount("Work");
		await _integration.InitializeAsync(_context);
		var inboxId = _integration.GetInboxes().Single().Id;
		await WaitUntilAsync(() => _server.Active is not null && _changes.Contains(inboxId), "the account connected");
		return inboxId;
	}
}
