using MacroDeckHost.Application.Backups;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Email;

[TestFixture]
public class EmailInboxKeysTests
{
	private TestPaths _paths = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
	}

	[TearDown]
	public void TearDown()
	{
		_paths.Cleanup();
	}

	[Test]
	public void A_slug_is_derived_from_the_name_once_and_survives_a_rename()
	{
		var keys = EmailInboxKeys.Assign(EmailInboxKeySet.Empty, [Inbox("app.imap::a", "Arbeit (GMX) Ü")]);
		var renamed = EmailInboxKeys.Assign(keys, [Inbox("app.imap::a", "Something else")]);

		Assert.Multiple(() =>
		{
			Assert.That(keys.Keys.Single().Slug, Is.EqualTo("arbeit_gmx_u"));
			Assert.That(renamed.Keys.Single().Slug, Is.EqualTo("arbeit_gmx_u"));
		});
	}

	[Test]
	public void Slugs_are_unique_across_providers_short_and_never_empty()
	{
		var keys = EmailInboxKeys.Assign(EmailInboxKeySet.Empty,
		[
			Inbox("app.imap::a", "Work"),
			Inbox("com.example.mail::b", "work"),
			Inbox("com.example.mail::c", "WORK!"),
			Inbox("app.imap::d", "!!!"),
			Inbox("app.imap::e", new string('x', 60)),
			Inbox("app.imap::f", new string('x', 60))
		]);

		Assert.That(keys.Keys.Select(key => key.Slug),
			Is.EqualTo(new[]
			{
				"work", "work_2", "work_3", "inbox", new string('x', 32), new string('x', 30) + "_2"
			}));
	}

	[Test]
	public void An_unchanged_set_of_inboxes_is_not_rewritten()
	{
		var keys = EmailInboxKeys.Assign(EmailInboxKeySet.Empty, [Inbox("app.imap::a", "Work")]);

		Assert.That(EmailInboxKeys.Assign(keys, [Inbox("app.imap::a", "Work")]), Is.SameAs(keys));
	}

	[Test]
	public void Beyond_the_limit_the_least_recently_seen_inbox_is_forgotten()
	{
		var keys = EmailInboxKeySet.Empty;
		for (var i = 0; i < EmailInboxKeys.MaxKeys; i++)
		{
			keys = EmailInboxKeys.Assign(keys, [Inbox($"app.imap::i{i}", "Inbox " + i)]);
		}

		keys = EmailInboxKeys.Assign(keys, [Inbox("app.imap::i0", "Inbox 0")]);
		keys = EmailInboxKeys.Assign(keys, [Inbox("app.imap::new", "New")]);

		var refs = keys.Keys.Select(key => key.Ref).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(refs, Has.Count.EqualTo(EmailInboxKeys.MaxKeys));
			Assert.That(refs, Does.Contain("app.imap::i0"), "seen again, so kept");
			Assert.That(refs, Does.Not.Contain("app.imap::i1"), "the least recently seen goes");
			Assert.That(refs[0], Is.EqualTo("app.imap::i0"), "first-seen order is kept");
		});
	}

	[Test]
	public void Assigned_slugs_are_persisted_for_the_next_start()
	{
		var first = new JsonEmailInboxKeyStore(_paths, EmailTesting.Logger());
		first.Assign([Inbox("app.imap::a", "Work"), Inbox("app.imap::b", "Work")]);

		var reloaded = new JsonEmailInboxKeyStore(_paths, EmailTesting.Logger());
		var slugs = reloaded.Assign([Inbox("app.imap::b", "Renamed")]);

		Assert.Multiple(() =>
		{
			Assert.That(slugs["app.imap::b"], Is.EqualTo("work_2"));
			Assert.That(reloaded.Keys.Select(key => key.Ref), Is.EqualTo(new[] { "app.imap::a", "app.imap::b" }));
		});
	}

	[Test]
	public void An_unreadable_key_file_is_not_overwritten()
	{
		Directory.CreateDirectory(_paths.DataDirectory);
		var path = Path.Combine(_paths.DataDirectory, "email-inboxes.json");
		File.WriteAllText(path, "{ not json");

		var slugs = new JsonEmailInboxKeyStore(_paths, EmailTesting.Logger()).Assign([Inbox("app.imap::a", "Work")]);

		Assert.Multiple(() =>
		{
			Assert.That(slugs["app.imap::a"], Is.EqualTo("work"));
			Assert.That(File.Exists(path) ? File.ReadAllText(path) : File.ReadAllText(path + ".bak"),
				Does.Contain("not json"));
		});
	}

	[Test]
	public void The_key_file_travels_with_the_variables_in_a_backup()
	{
		Assert.That(BackupComponentGroups.Owner("data/email-inboxes.json"), Is.EqualTo(BackupComponentGroup.Variables));
	}

	private static EmailInboxDescriptor Inbox(string inboxRef, string displayName)
	{
		var separator = inboxRef.IndexOf("::", StringComparison.Ordinal);
		return new EmailInboxDescriptor(inboxRef,
			inboxRef[..separator],
			inboxRef[(separator + 2)..],
			"IMAP",
			displayName,
			null);
	}
}
