using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Serialization;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Localization;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTesting;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTree;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Calendar;

namespace MacroDeckHost.Tests.UnitTests.Calendar;

[TestFixture]
public class CalendarAgendaDialogTests
{
	private const string MeetingUrl = "https://meet.example.com/abc-defg-hij";

	private CalendarWidgetHarness _harness = null!;

	[SetUp]
	public async Task SetUp()
	{
		var planning = Event("planning", Noon.AddHours(2), TimeSpan.FromHours(1), "team", "Planning", MeetingUrl) with
		{
			Location = "Room 4",
		};
		_harness = new CalendarWidgetHarness()
			.With(Event("ended", Noon.AddHours(-3), TimeSpan.FromHours(1), title: "Ended"))
			.With(Event("running", Noon.AddMinutes(-10), TimeSpan.FromMinutes(30), title: "Running"))
			.With(planning)
			.With(Event("holiday", new DateTimeOffset(Noon.Date, TimeSpan.Zero), TimeSpan.FromDays(1), title: "Holiday") with
			{
				IsAllDay = true,
			})
			.With(Event("retro", Noon.AddDays(1), TimeSpan.FromHours(1), title: "Retro"))
			.With(Event("offsite", Noon.AddDays(3), TimeSpan.FromHours(1), title: "Offsite"));
		_harness.Google.Details["planning"] = planning with { Description = "Bring the roadmap" };
		await _harness.SyncAsync();
	}

	[Test]
	public async Task The_dialog_lists_the_upcoming_events_of_the_agenda_by_day_with_its_own_settings()
	{
		var widget = Agenda($$"""{"days":2,"showAllDay":false,"calendars":["{{CalendarWidgetHarness.CalendarKey()}}"]}""");

		await using var dialog = await _harness.OpenAgendaDialogAsync(widget.Id);
		var days = Days(dialog.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(days.Select(day => day.Heading),
				Is.EqualTo(new[] { Resolve(Strings.Today()), Resolve(Strings.Tomorrow()) }));
			Assert.That(days[0].Titles, Is.EqualTo(new[] { "Running" }), "ended, all-day and other calendars stay out");
			Assert.That(days[1].Titles, Is.EqualTo(new[] { "Retro" }));
		});
	}

	[Test]
	public async Task Days_without_events_are_left_out_of_the_list()
	{
		var widget = Agenda($$"""{"days":5,"showAllDay":false,"calendars":["{{CalendarWidgetHarness.CalendarKey()}}"]}""");

		await using var dialog = await _harness.OpenAgendaDialogAsync(widget.Id);
		var days = Days(dialog.BuildTree().Root);

		Assert.That(days.Select(day => day.Titles.Single()), Is.EqualTo(new[] { "Running", "Retro", "Offsite" }));
	}

	[Test]
	public async Task An_agenda_without_upcoming_events_says_so()
	{
		var widget = Agenda($$"""{"calendars":["{{CalendarWidgetHarness.CalendarKey("elsewhere")}}"]}""");

		await using var dialog = await _harness.OpenAgendaDialogAsync(widget.Id);

		Assert.That(Texts(dialog.BuildTree().Root), Is.EqualTo(new[] { Resolve(Strings.NoEvents()) }));
	}

	[Test]
	public async Task Pressing_an_event_shows_its_details_in_the_same_dialog_and_back_returns_to_the_list()
	{
		var widget = Agenda("{}");
		await using var dialog = await _harness.OpenAgendaDialogAsync(widget.Id);

		PressEvent(dialog, "Planning");
		await WaitForAsync(() => Texts(dialog.BuildTree().Root).Contains("Bring the roadmap"),
			"the event's details never arrived");
		var details = Texts(dialog.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(details, Does.Contain("Planning"));
			Assert.That(details, Does.Contain(Resolve(Strings.Details.TimedRange(date: "Thursday, January 1, 2026",
				start: "14:00", end: "15:00"))));
			Assert.That(details, Does.Contain("Room 4"));
			Assert.That(details, Does.Not.Contain("Running"), "the list gives way to the event");
			Assert.That(_harness.Interactions.Opened.Task.IsCompleted, Is.False, "no second dialog opens");
		});

		Press(dialog, Get(dialog.BuildTree().Root, "back"));
		var list = Days(dialog.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(list.Single().Titles, Is.EqualTo(new[] { "Holiday", "Running", "Planning" }));
			Assert.That(Texts(dialog.BuildTree().Root), Does.Not.Contain("Bring the roadmap"));
		});
	}

	[TestCase(false)]
	[TestCase(true)]
	public async Task Joining_from_an_event_of_the_dialog_opens_the_link_on_the_host_unless_it_is_locked(bool locked)
	{
		_harness.Opener.Locked = locked;
		var widget = Agenda("{}");
		await using var dialog = await _harness.OpenAgendaDialogAsync(widget.Id);

		PressEvent(dialog, "Planning");
		Press(dialog, Get(dialog.BuildTree().Root, "joinButton"));

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Opener.Opened, locked ? Is.Empty : Is.EqualTo(new[] { MeetingUrl }));
			Assert.That(Texts(dialog.BuildTree().Root),
				Does.Contain(Resolve(locked
					? AppStrings.Integrations.Calendar.Actions.JoinMeeting.HostLocked()
					: Strings.Details.MeetingOpened())));
		});
	}

	[Test]
	public async Task The_dialog_follows_new_events_and_says_so_once_its_widget_is_deleted()
	{
		var widget = Agenda("{}");
		await using var dialog = await _harness.OpenAgendaDialogAsync(widget.Id);

		_harness.With(Event("late", Noon.AddHours(4), TimeSpan.FromMinutes(30), title: "Late add"));
		await _harness.SyncAsync();

		Assert.That(Days(dialog.BuildTree().Root).Single().Titles, Does.Contain("Late add"));

		_harness.Folders.Remove(widget);
		_harness.WidgetChanges.Notify();

		Assert.That(Texts(dialog.BuildTree().Root),
			Is.EqualTo(new[] { Resolve(AppStrings.Integrations.Calendar.Actions.ShowDetails.WidgetMissing()) }));
	}

	[TestCase('x')]
	[TestCase('漢')]
	public async Task A_busy_week_lists_what_fits_the_dialog_and_counts_the_events_left_out(char letter)
	{
		const int busyDays = 7;
		const int eventsPerDay = 30;
		const int upcomingFromSetUp = 5;
		var calendarId = new string(letter, 300);

		for (var day = 0; day < busyDays; day++)
		{
			for (var index = 0; index < eventsPerDay; index++)
			{
				var start = Noon.AddDays(day).AddMinutes(10 + (index * 10));
				_harness.With(Event($"busy-{day}-{index}", start, TimeSpan.FromMinutes(10), calendarId,
					$"{day}-{index} " + new string(letter, 400)) with { Location = new string(letter, 800) });
			}
		}

		await _harness.SyncAsync();
		var widget = Agenda("""{"days":7,"showCalendar":true}""");

		await using var dialog = await _harness.OpenAgendaDialogAsync(widget.Id);
		var tree = dialog.BuildTree();
		var listed = Days(tree.Root).Sum(day => day.Titles.Count);
		var upcoming = (busyDays * eventsPerDay) + upcomingFromSetUp;

		Assert.Multiple(() =>
		{
			Assert.That(UiCanonicalJson.SerializeToUtf8Bytes(tree).Length, Is.LessThanOrEqualTo(ProtocolLimits.MaxUiTreeBytes));
			Assert.That(Flatten(tree.Root).Count(), Is.LessThanOrEqualTo(ProtocolLimits.MaxUiNodesPerTree));
			Assert.That(Days(tree.Root), Has.Count.GreaterThan(1), "the list keeps its day sections");
			Assert.That(listed, Is.LessThan(upcoming));
			Assert.That(Texts(tree.Root), Does.Contain(Resolve(Strings.MoreEvents(upcoming - listed))));
		});
	}

	private WidgetEntity Agenda(string data) => _harness.Folders.AddCalendar(CalendarWidgetTypes.LayoutAgenda, data);

	private static void PressEvent(IUiSession dialog, string title)
		=> Press(dialog, Flatten(dialog.BuildTree().Root).First(node => IsPressable(node) && Texts(node).Contains(title)));

	private static List<(string Heading, List<string> Titles)> Days(UiNode root)
		=> [.. Flatten(root)
			.Where(node => node.Children.Any(child => child.Id.EndsWith(".heading", StringComparison.Ordinal)))
			.Select(day => (Text(Get(day, "heading"))!, Titles(day)))];

	private static List<string> Titles(UiNode root)
		=> [.. Flatten(root).Where(node => node.Id.EndsWith(".title", StringComparison.Ordinal)).Select(node => Text(node)!)];
}
