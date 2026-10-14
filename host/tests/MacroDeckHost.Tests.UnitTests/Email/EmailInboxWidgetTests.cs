using MacroDeck.Sdk.Email;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Nodes;
using MacroDeckHost.Tests.UnitTests.Calendar;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTree;
using static MacroDeckHost.Tests.UnitTests.Email.EmailTesting;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.EmailInbox;

namespace MacroDeckHost.Tests.UnitTests.Email;

[TestFixture]
public class EmailInboxWidgetTests
{
	private EmailInboxWidgetHarness _harness = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new EmailInboxWidgetHarness();
		_harness.Imap.Set("work",
			Snapshot(EmailInboxStatus.Connected,
				2,
				[
					Message("standup", -10, sender: "Ann", subject: "Standup notes"),
					Message("invoice", -30, true, "Billing", "Invoice"),
					Message("release", -90, sender: "Bob", subject: "Release plan")
				],
				40));
		_harness.Other.Set("home",
			Snapshot(EmailInboxStatus.Connected,
				1,
				[
					Message("dinner", -20, sender: "Sam", subject: "Dinner?")
				],
				12));
	}

	[TearDown]
	public void TearDown()
	{
		_harness.Dispose();
	}

	[Test]
	public async Task The_counter_adds_up_every_inbox_unless_the_widget_picks_some()
	{
		await _harness.SyncAsync();

		await using var all = await _harness.OpenWidgetAsync(new { });
		await using var work
			= await _harness.OpenWidgetAsync(new { inboxes = new[] { EmailInboxWidgetHarness.WorkRef } });
		var allCounter = EmailInboxTree.Layout(all, "counter");
		var workCounter = EmailInboxTree.Layout(work, "counter");

		Assert.Multiple(() =>
		{
			Assert.That(Text(Get(allCounter, "count")), Is.EqualTo("3"));
			Assert.That(Text(Get(allCounter, "caption")),
				Is.EqualTo(Resolve(Strings.AllInboxes())),
				"several inboxes carry no single name");
			Assert.That(Text(Get(workCounter, "count")), Is.EqualTo("2"));
			Assert.That(Text(Get(workCounter, "caption")), Is.EqualTo("Work"));
		});
	}

	[Test]
	public async Task The_list_shows_the_newest_emails_of_every_inbox_first_and_marks_the_unread_ones()
	{
		await _harness.SyncAsync();

		await using var session = await _harness.OpenWidgetAsync(new { mode = "list", maxMessages = 3 });
		var rows = EmailInboxTree.Rows(EmailInboxTree.Layout(session, "list"));

		Assert.Multiple(() =>
		{
			Assert.That(rows.Select(row => EmailInboxTree.Text(row, "title")),
				Is.EqualTo(new[] { "Ann", "Sam", "Billing" }),
				"newest first across inboxes, cut to the configured count");
			Assert.That(rows.Select(IsMarkedUnread), Is.EqualTo(new[] { true, true, false }));
			Assert.That(rows.Select(row => Weight(Get(row, "title"))),
				Is.EqualTo(new[]
					{ UiComponentTextWeights.Bold, UiComponentTextWeights.Bold, UiComponentTextWeights.Regular }),
				"an unread sender is bold, a read one regular");
		});
	}

	[Test]
	public async Task The_list_header_names_the_inboxes_and_badges_the_unread_count_only_while_some_are_unread()
	{
		_harness.Other.Set("home",
			Snapshot(EmailInboxStatus.Connected,
				0,
				[
					Message("dinner", -20, true, "Sam", "Dinner?")
				]));
		await _harness.SyncAsync();

		await using var all = await _harness.OpenWidgetAsync(new { mode = "list" });
		await using var home = await _harness.OpenWidgetAsync(new
		{
			mode = "list", inboxes = new[] { EmailInboxWidgetHarness.HomeRef }
		});
		var allHeader = Get(EmailInboxTree.Layout(all, "list"), "header");
		var homeHeader = Get(EmailInboxTree.Layout(home, "list"), "header");

		Assert.Multiple(() =>
		{
			Assert.That(Text(Get(allHeader, "title")), Is.EqualTo(Resolve(Strings.AllInboxes())));
			Assert.That(Text(Get(allHeader, "unread")), Is.EqualTo("2"));
			Assert.That(Text(Get(homeHeader, "title")), Is.EqualTo("Home"));
			Assert.That(Find(homeHeader, "unread"), Is.Null, "nothing unread shows no badge");
		});
	}

	[Test]
	public async Task A_received_time_reserves_its_own_width_so_a_long_sender_cannot_cut_it()
	{
		_harness.Imap.Set("work",
			Snapshot(EmailInboxStatus.Connected,
				2,
				[
					Message("today", -10, sender: new string('n', 300)),
					Message("yesterday", -(18 * 60), sender: new string('n', 300))
				]));
		await _harness.SyncAsync();

		await using var session = await _harness.OpenWidgetAsync(new
		{
			mode = "list", inboxes = new[] { EmailInboxWidgetHarness.WorkRef }
		});
		var rows = EmailInboxTree.Rows(EmailInboxTree.Layout(session, "list"));

		Assert.Multiple(() =>
		{
			foreach (var row in rows)
			{
				var time = Get(row, "time");
				var text = Text(time)!;

				Assert.That(time.Properties.ContainsKey(UiComponentProperties.Fill), Is.False);
				Assert.That(time.Properties[UiComponentProperties.Digits].GetDouble(),
					Is.GreaterThanOrEqualTo(text.Count(char.IsDigit)
						+ 0.5 * text.Count(character => !char.IsDigit(character))),
					text);
			}

			Assert.That(Text(Get(rows[1], "time")), Is.EqualTo(Resolve(Strings.Yesterday())));
			Assert.That(Get(rows[1], "time").Properties[UiComponentProperties.Digits].GetDouble(),
				Is.GreaterThanOrEqualTo(Resolve(Strings.Yesterday()).Length),
				"room for the localized word");
		});
	}

	[Test]
	public async Task Only_unread_emails_are_listed_when_the_widget_asks_for_them()
	{
		await _harness.SyncAsync();

		await using var session = await _harness.OpenWidgetAsync(new
		{
			mode = "list", unreadOnly = true, inboxes = new[] { EmailInboxWidgetHarness.WorkRef }
		});
		var rows = EmailInboxTree.Rows(EmailInboxTree.Layout(session, "list"));

		Assert.That(rows.Select(row => EmailInboxTree.Text(row, "title")), Is.EqualTo(new[] { "Ann", "Bob" }));
	}

	[Test]
	public async Task The_toggles_decide_what_a_row_shows()
	{
		await _harness.SyncAsync();
		var work = new[] { EmailInboxWidgetHarness.WorkRef };

		await using var everything = await _harness.OpenWidgetAsync(new
		{
			mode = "list", inboxes = work, showPreview = true
		});
		await using var subjectOnly = await _harness.OpenWidgetAsync(new
		{
			mode = "list", inboxes = work, showSender = false, showTimestamp = false
		});
		var full = EmailInboxTree.Rows(EmailInboxTree.Layout(everything, "list"))[0];
		var reduced = EmailInboxTree.Rows(EmailInboxTree.Layout(subjectOnly, "list"))[0];

		Assert.Multiple(() =>
		{
			Assert.That(EmailInboxTree.Text(full, "title"), Is.EqualTo("Ann"));
			Assert.That(EmailInboxTree.Text(full, "subject"), Is.EqualTo("Standup notes"));
			Assert.That(EmailInboxTree.Text(full, "preview"), Is.EqualTo("Preview standup"));
			Assert.That(EmailInboxTree.Text(full, "time"), Is.EqualTo("11:50"));
			Assert.That(EmailInboxTree.Text(reduced, "title"),
				Is.EqualTo("Standup notes"),
				"the subject leads without a sender");
			Assert.That(Find(reduced, "subject"), Is.Null);
			Assert.That(Find(reduced, "preview"), Is.Null, "the preview is off by default");
			Assert.That(Find(reduced, "time"), Is.Null);
		});
	}

	[Test]
	public async Task A_received_time_turns_relative_once_the_day_is_over()
	{
		await _harness.SyncAsync();

		await using var session = await _harness.OpenWidgetAsync(new { mode = "list", maxMessages = 1 });
		var before = EmailInboxTree.Text(EmailInboxTree.Rows(EmailInboxTree.Layout(session, "list"))[0], "time");

		_harness.World.Time.Advance(TimeSpan.FromHours(12));
		await WaitForAsync(() => EmailInboxTree.Text(EmailInboxTree.Rows(EmailInboxTree.Layout(session, "list"))[0],
					"time")
				== Resolve(Strings.Yesterday()),
			"the row never re-rendered after midnight");

		Assert.That(before, Is.EqualTo("11:50"));
	}

	[Test]
	public async Task A_placed_widget_follows_the_inbox_live()
	{
		await _harness.SyncAsync();
		await using var session = await _harness.OpenWidgetAsync(new { mode = "list" });

		_harness.Imap.Set("work",
			Snapshot(EmailInboxStatus.Connected,
				3,
				[
					Message("urgent", 0, sender: "Carol", subject: "Urgent"),
					Message("standup", -10, sender: "Ann", subject: "Standup notes")
				]));
		_harness.Imap.RaiseChanged("work");
		await _harness.World.SyncPendingAsync();

		var list = EmailInboxTree.Layout(session, "list");
		Assert.Multiple(() =>
		{
			Assert.That(EmailInboxTree.Text(EmailInboxTree.Rows(list)[0], "title"), Is.EqualTo("Carol"));
			Assert.That(Text(Get(Get(list, "header"), "unread")), Is.EqualTo("4"));
		});
	}

	[Test]
	public async Task Without_any_inbox_the_widget_says_so()
	{
		var integrations = new ConfigurableIntegrationRegistry([]);
		using var world = new EmailWorld(integrations);
		var provider = EmailInboxWidgetHarness.CreateProvider(world);
		await _harness.SyncAsync();

		await using var empty = await EmailInboxWidgetHarness.OpenWidgetAsync(provider, new { });
		await using var gone = await _harness.OpenWidgetAsync(new { inboxes = new[] { "app.removed::inbox" } });

		Assert.Multiple(() =>
		{
			Assert.That(Texts(empty.BuildTree().Root), Is.EqualTo(new[] { Resolve(Strings.NoInboxes()) }));
			Assert.That(Texts(gone.BuildTree().Root), Is.EqualTo(new[] { Resolve(Strings.NoInboxes()) }));
		});
	}

	[Test]
	public async Task An_unreachable_inbox_keeps_its_last_emails_and_says_it_is_disconnected()
	{
		await _harness.SyncAsync();
		await using var session = await _harness.OpenWidgetAsync(new
		{
			mode = "list", inboxes = new[] { EmailInboxWidgetHarness.WorkRef }
		});

		await using var count
			= await _harness.OpenWidgetAsync(new { inboxes = new[] { EmailInboxWidgetHarness.WorkRef } });

		_harness.Imap.Failing.Add("work");
		await _harness.SyncAsync();
		await _harness.SyncAsync();

		var list = EmailInboxTree.Layout(session, "list");
		Assert.Multiple(() =>
		{
			Assert.That(_harness.World.Cache.Snapshot.Inboxes
					.Single(state => state.Ref == EmailInboxWidgetHarness.WorkRef)
					.Status,
				Is.EqualTo(EmailInboxStatus.Disconnected),
				"the second failure in a row disconnects the inbox");
			Assert.That(EmailInboxTree.Rows(list).Select(row => EmailInboxTree.Text(row, "title")),
				Does.Contain("Ann"));
			Assert.That(Text(Get(Get(list, "problem"), "text")), Is.EqualTo(Resolve(Strings.Disconnected())));
			Assert.That(Find(EmailInboxTree.Layout(session, "glance"), "problemIcon"),
				Is.Not.Null,
				"a small list flags the problem in its header");
			Assert.That(Text(Get(Get(EmailInboxTree.Layout(count, "counter"), "problem"), "text")),
				Is.EqualTo(Resolve(Strings.Offline())));
		});
	}

	[Test]
	public async Task A_single_offline_inbox_shows_why_its_provider_says_it_is_offline()
	{
		const string reason = "Cannot reach imap.example.com. Macro Deck keeps trying.";
		_harness.Imap.Set("work",
			Snapshot(EmailInboxStatus.Disconnected, 2, [Message("standup", -10, sender: "Ann")])
				with
				{
					StatusMessage = reason
				});
		await _harness.SyncAsync();

		await using var work = await _harness.OpenWidgetAsync(new
		{
			mode = "list", inboxes = new[] { EmailInboxWidgetHarness.WorkRef }
		});
		await using var all = await _harness.OpenWidgetAsync(new { mode = "list" });

		Assert.Multiple(() =>
		{
			Assert.That(Text(Get(Get(EmailInboxTree.Layout(work, "list"), "problem"), "text")), Is.EqualTo(reason));
			Assert.That(Text(Get(Get(EmailInboxTree.Layout(all, "list"), "problem"), "text")),
				Is.EqualTo(Resolve(Strings.SomeDisconnected())),
				"several inboxes keep the summary");
		});
	}

	[Test]
	public async Task An_inbox_without_emails_says_so_in_the_list()
	{
		_harness.Imap.Set("work", Snapshot(EmailInboxStatus.Connected, 0, []));
		await _harness.SyncAsync();

		await using var session = await _harness.OpenWidgetAsync(new
		{
			mode = "list", unreadOnly = true, inboxes = new[] { EmailInboxWidgetHarness.WorkRef }
		});

		Assert.That(Text(Get(EmailInboxTree.Layout(session, "list"), "empty")),
			Is.EqualTo(Resolve(Strings.NoUnreadEmails())));
	}

	[TestCase("count", "counter")]
	[TestCase("list", "list")]
	public async Task The_picker_sample_shows_made_up_emails_without_any_inbox(string mode, string layout)
	{
		var integrations = new ConfigurableIntegrationRegistry([]);
		using var world = new EmailWorld(integrations);
		var provider = EmailInboxWidgetHarness.CreateProvider(world);

		await using var session = await EmailInboxWidgetHarness.OpenWidgetAsync(provider, new { mode }, true);
		var texts = Texts(EmailInboxTree.Layout(session, layout));

		Assert.Multiple(() =>
		{
			Assert.That(texts, Does.Not.Contain(Resolve(Strings.NoInboxes())));
			Assert.That(texts, Does.Contain(mode == "count" ? "2" : Resolve(Strings.Sample.TeamSender())));
		});
	}

	[Test]
	public async Task The_provider_declines_every_other_widget_type()
	{
		var widget = await _harness.Provider.CreateSessionAsync(new UiSessionRequest
			{
				Surface = CalendarWidgetHarness.WidgetSurface("app.macro-deck.calendar::calendar", new { }),
				UiModelVersion = 1
			},
			CancellationToken.None);
		var config = await _harness.Provider.CreateSessionAsync(new UiSessionRequest
			{
				Surface = CalendarWidgetHarness.ConfigSurface("app.macro-deck.calendar::calendar", new { }),
				UiModelVersion = 1
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(widget, Is.Null);
			Assert.That(config, Is.Null);
		});
	}

	private static bool IsMarkedUnread(UiNode row)
	{
		return Find(row, "dot") is not null;
	}

	private static string Weight(UiNode text)
	{
		return text.Properties.TryGetValue(UiComponentProperties.Weight, out var weight)
			? weight.GetString()!
			: UiComponentTextWeights.Regular;
	}
}
