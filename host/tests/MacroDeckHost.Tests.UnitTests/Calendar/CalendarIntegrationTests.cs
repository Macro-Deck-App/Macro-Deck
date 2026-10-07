using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Integrations.Calendar;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Timers;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTesting;

namespace MacroDeckHost.Tests.UnitTests.Calendar;

[TestFixture]
public class CalendarIntegrationTests
{
	private FakeTimeProvider _time = null!;
	private FakeCalendarIntegration _provider = null!;
	private RecordingUrlOpener _opener = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new FakeTimeProvider { Now = Noon };
		_provider = new FakeCalendarIntegration("app.google").WithAccount("alice", "main", "team");
		_opener = new RecordingUrlOpener();
	}

	[Test]
	public async Task One_calendar_widget_type_is_registered_under_its_frozen_id_and_starts_as_an_agenda()
	{
		var registry = TestWidgetTypeProviders.Registry();
		var host = new WidgetTypeProviderHost(registry, TimeProvider.System, Serilog.Core.Logger.None);

		await host.StartAsync(new CalendarIntegration());

		Assert.That(registry.TryResolve("app.macro-deck.calendar::calendar", out var calendar), Is.True);
		using var defaults = JsonDocument.Parse(calendar.Descriptor.DefaultData!);
		Assert.Multiple(() =>
		{
			Assert.That(registry.IsRegistered("app.macro-deck.calendar::agenda"), Is.False);
			Assert.That(registry.IsRegistered("app.macro-deck.calendar::next-event"), Is.False);
			Assert.That(defaults.RootElement.GetProperty("layout").GetString(), Is.EqualTo("agenda"));
			Assert.That(defaults.RootElement.GetProperty("showDate").GetBoolean(), Is.True);
		});
	}

	[Test]
	public async Task Join_meeting_opens_the_link_of_the_running_event()
	{
		_provider.WithEvent("alice",
			Event("standup", Noon.AddMinutes(-10), TimeSpan.FromMinutes(30), meetingUrl: "https://meet.example.com/standup"));

		var result = await JoinAsync();

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_opener.Opened, Is.EqualTo(new[] { "https://meet.example.com/standup" }));
		});
	}

	[Test]
	public async Task Join_meeting_opens_an_event_starting_within_the_window()
	{
		_provider.WithEvent("alice",
			Event("review", Noon.AddMinutes(10), TimeSpan.FromMinutes(30), meetingUrl: "https://meet.example.com/review"));

		var result = await JoinAsync();

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_opener.Opened, Is.EqualTo(new[] { "https://meet.example.com/review" }));
		});
	}

	[Test]
	public async Task Join_meeting_honours_the_calendar_filter()
	{
		_provider.WithEvent("alice",
				Event("main-call", Noon, TimeSpan.FromMinutes(30), meetingUrl: "https://meet.example.com/main"))
			.WithEvent("alice",
				Event("team-call", Noon.AddMinutes(5), TimeSpan.FromMinutes(30), "team", meetingUrl: "https://meet.example.com/team"));

		var result = await JoinAsync(calendarKey: CalendarKeys.Calendar("app.google::alice", "team"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_opener.Opened, Is.EqualTo(new[] { "https://meet.example.com/team" }));
		});
	}

	[Test]
	public async Task Join_meeting_fails_with_a_message_when_nothing_starts_within_the_window()
	{
		_provider.WithEvent("alice",
				Event("later", Noon.AddMinutes(30), TimeSpan.FromMinutes(30), meetingUrl: "https://meet.example.com/later"))
			.WithEvent("alice", Event("no-link", Noon, TimeSpan.FromMinutes(30)));

		var result = await JoinAsync(windowMilliseconds: TimeSpan.FromMinutes(15).TotalMilliseconds);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Is.Not.Empty);
			Assert.That(_opener.Opened, Is.Empty);
		});
	}

	[Test]
	public async Task Join_meeting_fails_with_a_message_while_the_host_is_locked()
	{
		_provider.WithEvent("alice",
			Event("standup", Noon, TimeSpan.FromMinutes(30), meetingUrl: "https://meet.example.com/standup"));
		_opener.Locked = true;

		var result = await JoinAsync();

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Is.Not.Empty);
			Assert.That(_opener.Opened, Is.Empty);
		});
	}

	[Test]
	public async Task Refresh_calendars_reads_new_events_right_away()
	{
		var cache = Cache(_time, integrations: _provider);
		await cache.SyncAsync(CancellationToken.None);
		_provider.WithEvent("alice", Event("added", Noon.AddHours(2), TimeSpan.FromMinutes(30)));

		var result = await RefreshAsync(cache);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(cache.Snapshot.Events.Select(e => e.EventId), Does.Contain("added"));
		});
	}

	[Test]
	public async Task Refresh_calendars_fails_when_an_account_cannot_be_read()
	{
		var cache = Cache(_time, integrations: _provider);
		_provider.FailingAccounts.Add("alice");

		var result = await RefreshAsync(cache);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Is.EqualTo("Some calendars couldn't be updated"));
		});
	}

	[Test]
	public async Task Refresh_calendars_pressed_again_while_refreshing_joins_the_running_refresh()
	{
		var cache = Cache(_time, integrations: _provider);
		var integration = new CalendarIntegration();
		integration.UseCalendarServices(new CalendarHostServices(cache, _opener, _time, new MutableFolderCache()));
		var action = integration.Actions.Single(a => a.Id == "refresh-calendars");
		var held = new TaskCompletionSource();
		_provider.ReadsHeldBy = held.Task;

		var first = action.CreateExecutor().ExecuteAsync(new ActionExecutionContext { Parameters = new Dictionary<string, object>() });
		var second = action.CreateExecutor().ExecuteAsync(new ActionExecutionContext { Parameters = new Dictionary<string, object>() });
		held.SetResult();
		var results = await Task.WhenAll(first, second);

		Assert.Multiple(() =>
		{
			Assert.That(results.Select(r => r.Status), Is.All.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_provider.Queries, Has.Count.EqualTo(1));
		});
	}

	private Task<ActionResult> RefreshAsync(ICalendarEventCache cache)
	{
		var integration = new CalendarIntegration();
		integration.UseCalendarServices(new CalendarHostServices(cache, _opener, _time, new MutableFolderCache()));
		return integration.Actions.Single(a => a.Id == "refresh-calendars")
			.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext { Parameters = new Dictionary<string, object>() });
	}

	private async Task<ActionResult> JoinAsync(double? windowMilliseconds = null, string? calendarKey = null)
	{
		var cache = Cache(_time, integrations: _provider);
		await cache.SyncAsync(CancellationToken.None);

		var integration = new CalendarIntegration();
		integration.UseCalendarServices(new CalendarHostServices(cache, _opener, _time, new MutableFolderCache()));
		var action = integration.Actions.Single(a => a.Id == "join-meeting");

		var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
		if (windowMilliseconds is { } window)
		{
			parameters["window"] = window;
		}

		if (calendarKey is not null)
		{
			parameters["calendar"] = calendarKey;
		}

		return await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext { Parameters = parameters });
	}
}
