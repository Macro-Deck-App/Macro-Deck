using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Triggers.Providers;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using Microsoft.Extensions.DependencyInjection;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTesting;

namespace MacroDeckHost.Tests.UnitTests.Calendar;

[TestFixture]
public class CalendarTriggerSchedulerTests
{
	private FakeTimeProvider _time = null!;
	private FakeCalendarIntegration _provider = null!;
	private StubFolderCache _folders = null!;
	private StubAutomationCache _automations = null!;
	private VariableRegistry _variables = null!;
	private RecordingEventBus _bus = null!;
	private RecordingFlowExecutor _executor = null!;
	private EventSubscriptionIndex _index = null!;
	private EventTriggerRunner _runner = null!;
	private CalendarEventCache _cache = null!;
	private CalendarTriggerScheduler _scheduler = null!;
	private ServiceProvider _services = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new FakeTimeProvider { Now = Noon };
		_provider = new FakeCalendarIntegration("app.google", "Google Calendar")
			.WithAccount("work", "main")
			.WithAccount("home", "main");
		_folders = new StubFolderCache();
		_automations = new StubAutomationCache();
		_variables = new VariableRegistry();
		_bus = new RecordingEventBus();
		_executor = new RecordingFlowExecutor();
		_index = new EventSubscriptionIndex(_folders, _automations);

		var renderer = new VariableTemplateRenderer(_variables);
		var matcher = new EventSubscriptionMatcher(new ActionConditionEvaluator(renderer));
		var events = new EventRegistry(new ConfigurableIntegrationRegistry([]),
			[new CalendarEventProvider()],
			Logger());

		_runner = new EventTriggerRunner(_folders, _automations, events, matcher, renderer, _executor, Logger());
		_services = new ServiceCollection()
			.AddScoped<IEventTriggerContextResolver>(_ =>
				new EventTriggerContextResolver(_folders, _automations, events, matcher, renderer))
			.BuildServiceProvider();
		_cache = Cache(_time, integrations: _provider);
		_scheduler = new CalendarTriggerScheduler(_index,
			_cache,
			_bus,
			_services.GetRequiredService<IServiceScopeFactory>(),
			_time,
			Logger());
	}

	[TearDown]
	public void TearDown()
	{
		_scheduler.Dispose();
		_cache.Dispose();
		_services.Dispose();
	}

	[Test]
	public async Task Starts_soon_fires_at_each_subscriptions_own_lead_time_and_only_once()
	{
		_provider.WithEvent("work", Event("review", Noon.AddMinutes(30), TimeSpan.FromMinutes(30), title: "Review"));
		var tenMinutes = Automation(CalendarEventProvider.StartsSoonEventId, leadMinutes: 10);
		var fiveMinutes = Automation(CalendarEventProvider.StartsSoonEventId, leadMinutes: 5);

		await RunAtAsync(Noon.AddMinutes(19));
		var beforeAnyLead = Runs().Count;
		await RunAtAsync(Noon.AddMinutes(20));
		await RunAtAsync(Noon.AddMinutes(20).AddSeconds(5));
		var afterFirstLead = Runs();
		await RunAtAsync(Noon.AddMinutes(25));
		var afterSecondLead = Runs();

		Assert.Multiple(() =>
		{
			Assert.That(beforeAnyLead, Is.Zero);
			Assert.That(afterFirstLead, Is.EqualTo(new[] { tenMinutes }));
			Assert.That(afterSecondLead, Is.EqualTo(new[] { tenMinutes, fiveMinutes }));
		});
	}

	[Test]
	public async Task Started_fires_at_the_start_and_ended_at_the_end_with_the_event_as_payload()
	{
		_provider.WithEvent("work",
			Event("review", Noon.AddMinutes(30), TimeSpan.FromMinutes(30), title: "Review",
				meetingUrl: "https://meet.example.com/review"));
		var started = Automation(CalendarEventProvider.StartedEventId);
		var ended = Automation(CalendarEventProvider.EndedEventId);

		await RunAtAsync(Noon.AddMinutes(29));
		var beforeStart = Runs().Count;
		await RunAtAsync(Noon.AddMinutes(30));
		var atStart = Runs();
		await RunAtAsync(Noon.AddMinutes(60));
		var atEnd = Runs();

		var payload = _executor.Requests[0].EventParameters!;
		Assert.Multiple(() =>
		{
			Assert.That(beforeStart, Is.Zero);
			Assert.That(atStart, Is.EqualTo(new[] { started }));
			Assert.That(atEnd, Is.EqualTo(new[] { started, ended }));
			Assert.That(payload["title"], Is.EqualTo("Review"));
			Assert.That(payload["eventId"], Is.EqualTo("review"));
			Assert.That(payload["accountId"], Is.EqualTo("app.google::work"));
			Assert.That(payload["account"], Is.EqualTo("work@example.com"));
			Assert.That(payload["calendarId"], Is.EqualTo(CalendarKeys.Calendar("app.google::work", "main")));
			Assert.That(payload["calendar"], Is.EqualTo("Calendar main"));
			Assert.That(payload["provider"], Is.EqualTo("Google Calendar"));
			Assert.That(payload["meetingUrl"], Is.EqualTo("https://meet.example.com/review"));
			Assert.That(payload["allDay"], Is.EqualTo(false));
			Assert.That(DateTimeOffset.Parse((string)payload["start"]!, CultureInfo.InvariantCulture), Is.EqualTo(Noon.AddMinutes(30)));
			Assert.That(DateTimeOffset.Parse((string)payload["end"]!, CultureInfo.InvariantCulture), Is.EqualTo(Noon.AddMinutes(60)));
		});
	}

	[TestCase(CalendarEventProvider.StartsSoonEventId, 20)]
	[TestCase(CalendarEventProvider.StartedEventId, 30)]
	[TestCase(CalendarEventProvider.EndedEventId, 60)]
	public async Task The_account_filter_and_a_title_condition_select_the_events_a_trigger_runs_for(
		string eventId,
		int dueMinute)
	{
		_provider.WithEvent("work", Event("w-standup", Noon.AddMinutes(30), TimeSpan.FromMinutes(30), title: "Standup"))
			.WithEvent("work", Event("w-review", Noon.AddMinutes(30), TimeSpan.FromMinutes(30), title: "Review"))
			.WithEvent("home", Event("h-standup", Noon.AddMinutes(30), TimeSpan.FromMinutes(30), title: "Standup"));
		Automation(eventId,
			leadMinutes: 10,
			accountId: "app.google::work",
			filter: """{"kind":"compare","id":"c1","left":{"$event":"title"},"operator":"==","right":"Standup"}""");

		await RunAtAsync(Noon.AddMinutes(dueMinute));

		Assert.That(_executor.Requests.Select(r => r.EventParameters!["eventId"]), Is.EqualTo(new[] { "w-standup" }));
	}

	[Test]
	public async Task A_widget_trigger_checks_its_widget_variable_when_the_event_is_due()
	{
		_provider.WithEvent("work", Event("first", Noon.AddMinutes(30), TimeSpan.FromMinutes(30)))
			.WithEvent("work", Event("second", Noon.AddMinutes(60), TimeSpan.FromMinutes(30)));
		var widget = StubFolderCache.Widget(WidgetFlowsJson.ToSource(Flows(CalendarEventProvider.StartsSoonEventId,
			leadMinutes: 15,
			filter: """{"kind":"compare","id":"c1","left":{"$var":"armed"},"operator":"==","right":"yes"}""")));
		_folders.AddFolder(widget);
		_index.Rebuild();
		var armed = new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "armed",
			Scope = VariableScope.Widget,
			ScopeRefId = widget.Id.ToString(),
			Type = VariableType.Text,
			Classification = VariableClassification.User,
			Value = "yes"
		};

		await RunAtAsync(Noon);
		armed.Value = "no";
		_variables.Upsert(armed);
		await RunAtAsync(Noon.AddMinutes(15));
		armed.Value = "yes";
		_variables.Upsert(armed);
		await RunAtAsync(Noon.AddMinutes(45));

		Assert.Multiple(() =>
		{
			Assert.That(_executor.Requests.Select(r => r.EventParameters!["eventId"]), Is.EqualTo(new[] { "second" }));
			Assert.That(_executor.Requests.Single().OwnerWidgetId, Is.EqualTo(widget.Id));
		});
	}

	[Test]
	public async Task An_event_first_seen_inside_its_lead_window_fires_starts_soon_once()
	{
		var startsSoon = Automation(CalendarEventProvider.StartsSoonEventId, leadMinutes: 15);
		await RunAtAsync(Noon);

		_provider.WithEvent("work", Event("moved", Noon.AddMinutes(10), TimeSpan.FromMinutes(30)));
		await RunAtAsync(Noon.AddMinutes(5));
		await RunAtAsync(Noon.AddMinutes(6));

		Assert.That(Runs(), Is.EqualTo(new[] { startsSoon }));
	}

	[Test]
	public async Task A_host_that_starts_inside_the_lead_window_still_fires_starts_soon()
	{
		_provider.WithEvent("work", Event("soon", Noon.AddMinutes(8), TimeSpan.FromMinutes(30)));
		var startsSoon = Automation(CalendarEventProvider.StartsSoonEventId, leadMinutes: 10);

		await RunAtAsync(Noon);

		Assert.That(Runs(), Is.EqualTo(new[] { startsSoon }));
	}

	[Test]
	public async Task An_event_that_started_before_the_host_started_does_not_fire_started()
	{
		_provider.WithEvent("work", Event("running", Noon.AddMinutes(-5), TimeSpan.FromMinutes(30)));
		Automation(CalendarEventProvider.StartedEventId);

		await RunAtAsync(Noon);
		await RunAtAsync(Noon.AddMinutes(1));

		Assert.That(_executor.Requests, Is.Empty);
	}

	[Test]
	public async Task An_event_first_seen_after_its_start_while_the_host_runs_fires_started_once()
	{
		var started = Automation(CalendarEventProvider.StartedEventId);
		await RunAtAsync(Noon);

		_provider.WithEvent("work", Event("late", Noon.AddMinutes(3), TimeSpan.FromMinutes(30)));
		await RunAtAsync(Noon.AddMinutes(5));
		await RunAtAsync(Noon.AddMinutes(10));

		Assert.That(Runs(), Is.EqualTo(new[] { started }));
	}

	[Test]
	public async Task Replanning_never_fires_the_same_instant_twice()
	{
		_provider.WithEvent("work", Event("review", Noon.AddMinutes(10), TimeSpan.FromMinutes(20)));
		var startsSoon = Automation(CalendarEventProvider.StartsSoonEventId, leadMinutes: 15);
		var started = Automation(CalendarEventProvider.StartedEventId);
		var ended = Automation(CalendarEventProvider.EndedEventId);

		foreach (var minute in new[] { 0, 0, 1, 10, 10, 11, 30, 30, 45 })
		{
			await RunAtAsync(Noon.AddMinutes(minute));
		}

		Assert.That(Runs(), Is.EqualTo(new[] { startsSoon, started, ended }));
	}

	[Test]
	public async Task A_new_automation_does_not_replay_events_that_started_and_ended_earlier()
	{
		_provider.WithEvent("work", Event("morning", Noon.AddMinutes(10), TimeSpan.FromMinutes(20)));
		await RunAtAsync(Noon);
		await RunAtAsync(Noon.AddMinutes(60));

		Automation(CalendarEventProvider.StartedEventId);
		Automation(CalendarEventProvider.EndedEventId);
		await RunAtAsync(Noon.AddMinutes(120));

		Assert.That(_executor.Requests, Is.Empty);
	}

	[Test]
	public async Task An_event_that_already_ended_when_first_seen_fires_nothing()
	{
		Automation(CalendarEventProvider.StartedEventId);
		Automation(CalendarEventProvider.EndedEventId);
		await RunAtAsync(Noon);

		_provider.WithEvent("work", Event("past", Noon.AddMinutes(10), TimeSpan.FromMinutes(20)));
		await RunAtAsync(Noon.AddMinutes(60));

		Assert.That(_executor.Requests, Is.Empty);
	}

	[Test]
	public async Task Starts_soon_without_a_lead_time_fires_at_the_start()
	{
		_provider.WithEvent("work", Event("review", Noon.AddMinutes(10), TimeSpan.FromMinutes(30)));
		var startsSoon = Automation(CalendarEventProvider.StartsSoonEventId, leadMinutes: 0);

		await RunAtAsync(Noon);
		await RunAtAsync(Noon.AddMinutes(10));

		Assert.That(Runs(), Is.EqualTo(new[] { startsSoon }));
	}

	private async Task RunAtAsync(DateTimeOffset now)
	{
		_time.Now = now;
		await _cache.SyncAsync(CancellationToken.None);
		await _scheduler.RunAsync(CancellationToken.None);
		await DispatchAsync();
	}

	private async Task DispatchAsync()
	{
		var published = _bus.Published.ToList();
		_bus.Published.Clear();

		foreach (var occurrence in published)
		{
			IReadOnlyList<EventSubscription> subscriptions = occurrence.Target is { } target
				? _index.Find(target) is { } single ? [single] : []
				: _index.Find(occurrence.EventId);

			foreach (var subscription in subscriptions)
			{
				if (!EventSubscriptionMatcher.QuickReject(subscription, occurrence))
				{
					await _runner.Run(subscription, occurrence, CancellationToken.None);
				}
			}
		}
	}

	private List<string?> Runs() => [.. _executor.Requests.Select(r => r.FlowsSource)];

	private string Automation(
		string eventId,
		int? leadMinutes = null,
		string? accountId = null,
		string? filter = null)
	{
		var automation = _automations.Add(Flows(eventId, leadMinutes, accountId, filter));
		_index.Rebuild();
		return WidgetFlowsJson.ToSource(automation.Flows);
	}

	private static string Flows(string eventId, int? leadMinutes = null, string? accountId = null, string? filter = null)
	{
		var parameters = new List<object>();
		if (leadMinutes is { } lead)
		{
			parameters.Add(new { name = "leadTime", value = TimeSpan.FromMinutes(lead).TotalMilliseconds });
		}

		if (accountId is not null)
		{
			parameters.Add(new { name = "accountId", value = accountId });
		}

		var flow = new
		{
			triggerId = "t1",
			triggerType = "onEvent",
			@event = new
			{
				providerId = CalendarEventProvider.ProviderIdValue,
				eventId,
				parameters,
				filter = filter is null ? (JsonElement?)null : JsonDocument.Parse(filter).RootElement.Clone()
			},
			children = Array.Empty<object>()
		};

		return JsonSerializer.Serialize(new[] { flow });
	}

	private sealed class RecordingFlowExecutor : IFlowExecutor
	{
		public List<FlowExecutionRequest> Requests { get; } = [];

		public Task<FlowExecutionResult> ExecuteAsync(FlowExecutionRequest request, CancellationToken cancellationToken)
		{
			Requests.Add(request);
			return Task.FromResult(new FlowExecutionResult
			{
				ExecutionId = Guid.NewGuid(),
				Status = FlowExecutionStatus.Succeeded,
				MatchedFlows = 1
			});
		}
	}
}
