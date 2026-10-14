using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Email;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Serialization;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Widgets;
using MacroDeckHost.Widgets.EmailInbox;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTree;
using static MacroDeckHost.Tests.UnitTests.Email.EmailTesting;

namespace MacroDeckHost.Tests.UnitTests.Email;

[TestFixture]
public class EmailInboxWidgetLayoutTests
{
	private EmailInboxWidgetHarness _harness = null!;

	[SetUp]
	public async Task SetUp()
	{
		_harness = new EmailInboxWidgetHarness();
		_harness.Imap.Set("work", Snapshot(EmailInboxStatus.Connected, 1, [Message("hello", -5, sender: "Ann")]));
		await _harness.SyncAsync();
	}

	[TearDown]
	public void TearDown()
	{
		_harness.Dispose();
	}

	[TestCase("list", 1, 1, "glance")]
	[TestCase("list", 1, 2, "glance")]
	[TestCase("list", 2, 1, "strip")]
	[TestCase("list", 2, 2, "list")]
	[TestCase("list", 3, 2, "list")]
	[TestCase("list", 3, 3, "list")]
	[TestCase("count", 1, 1, "counter")]
	[TestCase("count", 1, 2, "counter")]
	[TestCase("count", 2, 1, "counterWide")]
	[TestCase("count", 3, 1, "counterWide")]
	[TestCase("count", 2, 2, "counter")]
	public async Task The_widget_picks_its_layout_for_the_tile_size(string mode, int columns, int rows, string layout)
	{
		await using var session = await _harness.OpenWidgetAsync(new { mode });

		Assert.That(Chosen(session.BuildTree().Root, columns, rows), Does.EndWith("." + layout));
	}

	[Test]
	public async Task A_one_cell_message_list_shows_the_newest_email_rather_than_the_count()
	{
		await using var list = await _harness.OpenWidgetAsync(new { mode = "list" });
		await using var count = await _harness.OpenWidgetAsync(new { mode = "count" });
		var glance = EmailInboxTree.Layout(list, Chosen(list.BuildTree().Root, 1, 1).Split('.')[^1]);
		var counter = EmailInboxTree.Layout(count, Chosen(count.BuildTree().Root, 1, 1).Split('.')[^1]);

		Assert.Multiple(() =>
		{
			Assert.That(glance.Id, Is.Not.EqualTo(counter.Id));
			Assert.That(EmailInboxTree.Rows(glance).Select(row => EmailInboxTree.Text(row, "title")),
				Is.EqualTo(new[] { "Ann" }));
			Assert.That(Find(glance, "count"), Is.Null, "the list never shows the big count");
			Assert.That(Text(Get(glance, "unread")), Is.EqualTo("1"), "the unread number sits in the header badge");
		});
	}

	[TestCase("list", true, 10)]
	[TestCase("list", false, 10)]
	[TestCase("glance", true, EmailInboxWidgetView.MaxGlanceRows)]
	[TestCase("strip", false, EmailInboxWidgetView.MaxStripRows)]
	public async Task Each_email_is_shown_exactly_when_it_fits_below_the_ones_before_it(
		string layout,
		bool preview,
		int rows)
	{
		_harness.Imap.Set("work",
			Snapshot(EmailInboxStatus.Connected,
				10,
				[.. Enumerable.Range(0, 10).Select(index => Message("w" + index, -index, sender: "Sender " + index))]));
		await _harness.SyncAsync();

		await using var session
			= await _harness.OpenWidgetAsync(new { mode = "list", maxMessages = 10, showPreview = preview });
		var fits = Flatten(EmailInboxTree.Layout(session, layout))
			.Where(node => node.Id.EndsWith(".fit", StringComparison.Ordinal))
			.ToList();
		var needed = fits.Select(fit => MinHeight(fit) * UiLength.Cell).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(fits, Has.Count.EqualTo(rows - 1), "every email after the first waits for room");
			Assert.That(needed, Is.Ordered.Ascending);
			foreach (var fit in fits)
			{
				var placed = Get(fit, "placed");
				var extent = placed.Children.Sum(child => FullSize(child));

				Assert.That(MinHeight(fit) * UiLength.Cell,
					Is.InRange(extent - 1, extent),
					"an email appears as soon as its whole row fits, and only then");
			}
		});
	}

	[TestCase("list")]
	[TestCase("glance")]
	[TestCase("strip")]
	public async Task The_unread_dot_column_is_reserved_only_while_an_unread_email_is_on_screen(string layout)
	{
		_harness.Imap.Set("work",
			Snapshot(EmailInboxStatus.Connected,
				0,
				[
					Message("ann", -5, true, "Ann"),
					Message("bob", -10, true, "Bob"),
					Message("cy", -15, true, "Cy")
				]));
		await _harness.SyncAsync();
		await using var session = await _harness.OpenWidgetAsync(new { mode = "list" });

		var allRead = EmailInboxTree.Layout(session, layout);

		_harness.Imap.Set("work",
			Snapshot(EmailInboxStatus.Connected,
				1,
				[
					Message("ann", -5, true, "Ann"),
					Message("bob", -10, sender: "Bob"),
					Message("cy", -15, true, "Cy")
				]));
		_harness.Imap.RaiseChanged("work");
		await _harness.World.SyncPendingAsync();

		var mixed = EmailInboxTree.Layout(session, layout);
		var onlyNewest = EmailInboxTree.Drawn(mixed, 0.01).ToList();
		var everything = EmailInboxTree.Rows(mixed);

		Assert.Multiple(() =>
		{
			Assert.That(EmailInboxTree.Rows(allRead), Has.Count.EqualTo(3));
			Assert.That(EmailInboxTree.Drawn(allRead, EmailInboxTree.AllRoom).Where(IsColumn),
				Is.Empty,
				"read emails line up with the header title, separators included");
			Assert.That(EmailInboxTree.Rows(mixed, 0.01).Select(row => EmailInboxTree.Text(row, "title")),
				Is.EqualTo(new[] { "Ann" }));
			Assert.That(onlyNewest.Where(IsColumn), Is.Empty, "the unread email below is not on screen");
			Assert.That(everything.Select(row => EmailInboxTree.Text(row, "title")),
				Is.EqualTo(new[] { "Ann", "Bob", "Cy" }));
			Assert.That(everything.Select(row => Find(row, "marker") is not null),
				Is.All.True,
				"every row keeps the column so the senders stay aligned");
			Assert.That(everything.Select(row => Find(row, "dot") is not null),
				Is.EqualTo(new[] { false, true, false }));
			Assert.That(EmailInboxTree.Drawn(mixed, EmailInboxTree.AllRoom)
					.Count(node => node.Id.EndsWith(".indent",
						StringComparison.Ordinal)),
				Is.EqualTo(2),
				"separators start at the text, past the column");
		});
	}

	[Test]
	public async Task A_full_list_of_long_emails_stays_within_the_ui_tree_limits()
	{
		var subject = new string('s', 2000);
		var preview = new string('p', 2000);
		var sender = new string('n', 2000);
		_harness.Imap.Set("work",
			Snapshot(EmailInboxStatus.Connected,
				20,
				[
					.. Enumerable.Range(0, 20)
						.Select(index => Message("w" + index,
								-index,
								index < 4,
								sender,
								subject) with
							{
								Preview = preview
							})
				]));
		_harness.Other.Set("home",
			Snapshot(EmailInboxStatus.Connected,
				20,
				[
					.. Enumerable.Range(0, 20)
						.Select(index => Message("h" + index,
								-index,
								index < 4,
								sender,
								subject) with
							{
								Preview = preview
							})
				]));
		await _harness.SyncAsync();

		await using var session
			= await _harness.OpenWidgetAsync(new { mode = "list", maxMessages = 10, showPreview = true });
		var tree = session.BuildTree();

		Assert.Multiple(() =>
		{
			Assert.That(EmailInboxTree.Rows(EmailInboxTree.Layout(session, "list")), Has.Count.EqualTo(10));
			Assert.That(UiCanonicalJson.SerializeToUtf8Bytes(tree).Length,
				Is.LessThanOrEqualTo(ProtocolLimits.MaxUiTreeBytes));
			Assert.That(Flatten(tree.Root).Count(), Is.LessThanOrEqualTo(ProtocolLimits.MaxUiNodesPerTree));
		});
	}

	private static bool IsColumn(UiNode node)
	{
		return node.Id.EndsWith(".marker", StringComparison.Ordinal)
			|| node.Id.EndsWith(".indent", StringComparison.Ordinal);
	}

	private static double MinHeight(UiNode fit)
	{
		return fit.Properties[UiComponentProperties.Variants][0].GetProperty("minHeight").GetDouble();
	}

	private static double FullSize(UiNode node)
	{
		return node.Properties[UiComponentProperties.MainSize].GetProperty("maxOfCell").GetDouble() * UiLength.Cell;
	}

	private static string Chosen(UiNode root, int columns, int rows)
	{
		var layout = Get(root, "layout");
		var index = UiResponsiveSelection.SelectChild(layout.Properties[UiComponentProperties.Variants],
			layout.Children.Count,
			InnerCells(columns, columns, rows),
			InnerCells(rows, columns, rows));

		return layout.Children[index].Id;
	}

	private static double InnerCells(int cells, int columns, int rows)
	{
		var padding = WidgetSafeArea.LengthFor(WidgetSafeArea.DefaultCornerRadius);
		var basis = Math.Min(Extent(columns), Extent(rows));
		var inset = Math.Min(padding.Basis * basis, padding.MaxOfCell!.Value * UiLength.Cell);

		return (Extent(cells) - 2 * inset) / UiLength.Cell;
	}

	private static double Extent(int cells)
	{
		return cells * UiLength.Cell + (cells - 1) * GridDefaults.WidgetSpacing;
	}
}
