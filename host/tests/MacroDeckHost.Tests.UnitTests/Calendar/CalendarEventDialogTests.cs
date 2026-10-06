using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Calendar;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Serialization;
using MacroDeckHost.Localization;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTesting;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTree;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Calendar;

namespace MacroDeckHost.Tests.UnitTests.Calendar;

[TestFixture]
public class CalendarEventDialogTests
{
	private const string MeetingUrl = "https://meet.example.com/abc-defg-hij";

	private CalendarWidgetHarness _harness = null!;

	[SetUp]
	public async Task SetUp()
	{
		_harness = new CalendarWidgetHarness();
		var planning = Event("planning", Noon.AddHours(2), TimeSpan.FromHours(1), title: "Planning",
			meetingUrl: MeetingUrl) with { Location = "Room 4" };
		_harness.With(planning);
		_harness.Google.Details["planning"] = planning with
		{
			Description = "<p>Bring the <b>roadmap</b></p>",
			Participants =
			[
				new CalendarParticipant { Name = "Alice", IsOrganizer = true, Response = CalendarResponseStatus.Accepted },
				new CalendarParticipant { Email = "bob@example.com", Response = CalendarResponseStatus.Declined },
			],
		};
		await _harness.SyncAsync();
	}

	[Test]
	public async Task The_dialog_shows_the_event_and_then_its_loaded_details()
	{
		await using var dialog = await Open("planning");

		await WaitForAsync(() => Texts(dialog.BuildTree().Root).Contains("Bring the roadmap"),
			"the description never arrived");
		var texts = Texts(dialog.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(texts, Does.Not.Contain("Planning"), "the dialog's own title already names the event");
			Assert.That(texts, Does.Contain(Resolve(Strings.Details.TimedRange(date: "Thursday, January 1, 2026",
				start: "14:00", end: "15:00"))));
			Assert.That(texts, Does.Contain(Resolve(Strings.Details.Source(calendar: "Calendar main",
				account: "alice@example.com", provider: "Google Calendar"))));
			Assert.That(texts, Does.Contain("Room 4"));
			Assert.That(texts, Does.Contain("Alice"));
			Assert.That(texts, Does.Contain(Resolve(Strings.Details.Organizer())));
			Assert.That(texts, Does.Contain(Resolve(Strings.Details.ResponseAccepted())));
			Assert.That(texts, Does.Contain("bob@example.com"));
			Assert.That(texts, Does.Contain(Resolve(Strings.Details.ResponseDeclined())));
			Assert.That(texts, Does.Contain("meet.example.com"));
		});
	}

	[Test]
	public async Task Joining_opens_the_meeting_link_on_the_host_and_says_so()
	{
		await using var dialog = await Open("planning");

		PressJoin(dialog);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Opener.Opened, Is.EqualTo(new[] { MeetingUrl }));
			Assert.That(Texts(dialog.BuildTree().Root), Does.Contain(Resolve(Strings.Details.MeetingOpened())));
		});
	}

	[Test]
	public async Task Joining_while_the_host_is_locked_opens_nothing_and_explains_why()
	{
		_harness.Opener.Locked = true;
		await using var dialog = await Open("planning");

		PressJoin(dialog);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Opener.Opened, Is.Empty);
			Assert.That(Texts(dialog.BuildTree().Root),
				Does.Contain(Resolve(AppStrings.Integrations.Calendar.Actions.JoinMeeting.HostLocked())));
		});
	}

	[Test]
	public async Task An_event_the_provider_no_longer_has_is_reported_as_unavailable()
	{
		_harness.Google.Details.Remove("planning");

		await using var dialog = await Open("planning");

		await WaitForAsync(() => Texts(dialog.BuildTree().Root).Contains(Resolve(Strings.Details.Unavailable())),
			"the dialog never reported the event as gone");
		Assert.That(Texts(dialog.BuildTree().Root), Does.Not.Contain("Planning"));
	}

	[Test]
	public async Task An_all_day_event_shows_its_date_without_times()
	{
		var holiday = Event("holiday", new DateTimeOffset(Noon.Date, TimeSpan.Zero), TimeSpan.FromDays(1),
			title: "Holiday") with { IsAllDay = true };
		_harness.With(holiday);
		_harness.Google.Details["holiday"] = holiday;
		await _harness.SyncAsync();

		await using var dialog = await Open("holiday");

		Assert.That(Texts(dialog.BuildTree().Root),
			Does.Contain(Resolve(Strings.Details.AllDayDate(date: "Thursday, January 1, 2026"))));
	}

	[TestCase('x')]
	[TestCase('漢')]
	public async Task A_huge_event_shows_capped_details_that_still_fit_the_dialog(char letter)
	{
		var allHands = Event("all-hands", Noon.AddHours(3), TimeSpan.FromHours(1), title: "All hands");
		_harness.With(allHands);
		_harness.Google.Details["all-hands"] = allHands with
		{
			Description = new string(letter, 50_000),
			Participants =
			[
				.. Enumerable.Range(0, 300).Select(index => new CalendarParticipant
				{
					Name = $"{index:000} " + new string(letter, 300),
					Email = $"person{index}@example.com",
					Response = CalendarResponseStatus.Tentative,
				}),
			],
		};
		await _harness.SyncAsync();

		await using var dialog = await Open("all-hands");
		await WaitForAsync(() => Find(dialog.BuildTree().Root, "description.text") is not null,
			"the details never arrived");
		var tree = dialog.BuildTree();
		var description = Text(Get(tree.Root, "description.text"))!;
		var people = Flatten(tree.Root).Where(node => node.Id.EndsWith(".lines.name", StringComparison.Ordinal)).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(description.Length,
				letter == 'x'
					? Is.EqualTo(ProtocolLimits.MaxCalendarDescriptionLength)
					: Is.InRange(1, ProtocolLimits.MaxCalendarDescriptionLength),
				"a text the wire escapes is shortened further to fit");
			Assert.That(people, Has.Count.EqualTo(ProtocolLimits.MaxCalendarParticipants));
			Assert.That(UiCanonicalJson.SerializeToUtf8Bytes(tree).Length, Is.LessThanOrEqualTo(ProtocolLimits.MaxUiTreeBytes));
			Assert.That(Flatten(tree.Root).Count(), Is.LessThanOrEqualTo(ProtocolLimits.MaxUiNodesPerTree));
		});
	}

	private Task<IUiSession> Open(string eventId)
		=> _harness.OpenDialogAsync(CalendarWidgetHarness.CalendarKey(), eventId);

	private static void PressJoin(IUiSession dialog)
		=> dialog.Dispatch(new UiEvent { NodeId = Get(dialog.BuildTree().Root, "joinButton").Id, Name = UiComponentEvents.Press });
}
