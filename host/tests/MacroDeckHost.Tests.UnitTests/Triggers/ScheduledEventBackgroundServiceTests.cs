using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Infrastructure.Triggers;
using MacroDeckHost.Tests.UnitTests.Delegation;
using Microsoft.Extensions.Hosting;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Triggers;

[TestFixture]
internal sealed class ScheduledEventBackgroundServiceTests
{
	private static readonly DateTime _wednesdayMorning = new(2026, 6, 10, 10, 0, 0, DateTimeKind.Unspecified);

	private static DateTimeOffset LocalTime(DateTime local)
	{
		return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
	}

	private static IEnumerable<TestCaseData> EditedSchedules()
	{
		var fiveMinutesLater = LocalTime(_wednesdayMorning.AddMinutes(5));

		yield return new TestCaseData(TimeEventProvider.DailyEventId,
				new[] { ("time", "\"20:00\"") },
				new[] { ("time", "\"10:05\"") },
				TimeSpan.FromMinutes(6),
				fiveMinutesLater)
			.SetName("A daily trigger fires at its edited time");
		yield return new TestCaseData(TimeEventProvider.WeeklyEventId,
				new[] { ("dayOfWeek", "\"MON\""), ("time", "\"10:00\"") },
				new[] { ("dayOfWeek", "\"WED\""), ("time", "\"10:05\"") },
				TimeSpan.FromMinutes(6),
				fiveMinutesLater)
			.SetName("A weekly trigger fires at its edited time");
		yield return new TestCaseData(TimeEventProvider.MonthlyEventId,
				new[] { ("dayOfMonth", "1"), ("time", "\"10:00\"") },
				new[] { ("dayOfMonth", "10"), ("time", "\"10:05\"") },
				TimeSpan.FromMinutes(6),
				fiveMinutesLater)
			.SetName("A monthly trigger fires at its edited time");
		yield return new TestCaseData(TimeEventProvider.CronEventId,
				new[] { ("expression", "\"0 20 * * *\"") },
				new[] { ("expression", "\"5 10 * * *\"") },
				TimeSpan.FromMinutes(6),
				fiveMinutesLater)
			.SetName("A cron trigger fires at its edited time");
		yield return new TestCaseData(TimeEventProvider.IntervalEventId,
				new[] { ("every", "1"), ("unit", "\"hours\"") },
				new[] { ("every", "10"), ("unit", "\"seconds\"") },
				TimeSpan.FromSeconds(11),
				LocalTime(_wednesdayMorning.AddSeconds(10)))
			.SetName("An interval trigger fires at its edited interval");
	}

	[TestCaseSource(nameof(EditedSchedules))]
	public async Task An_edited_schedule_fires_at_its_new_time_instead_of_the_old_one(
		string eventId,
		(string Name, string Json)[] before,
		(string Name, string Json)[] after,
		TimeSpan elapsed,
		DateTimeOffset expected)
	{
		var owner = EventTriggerOwner.ForAutomation(Guid.NewGuid());
		var harness = await Harness.Start(Subscription(owner, eventId, before));

		await harness.Edit(Subscription(owner, eventId, after));
		await harness.Elapse(elapsed);

		var fired = await harness.WaitForOccurrence();
		await harness.Stop();
		Assert.That(ScheduledFor(fired), Is.EqualTo(expected));
	}

	[Test]
	public async Task An_unrelated_index_change_does_not_restart_a_running_interval()
	{
		var owner = EventTriggerOwner.ForAutomation(Guid.NewGuid());
		(string, string)[] everyTenSeconds = [("every", "10"), ("unit", "\"seconds\"")];
		var harness = await Harness.Start(Subscription(owner, TimeEventProvider.IntervalEventId, everyTenSeconds));

		await harness.Elapse(TimeSpan.FromSeconds(6));
		await harness.Edit(Subscription(owner, TimeEventProvider.IntervalEventId, everyTenSeconds));
		await harness.Elapse(TimeSpan.FromSeconds(5));

		var fired = await harness.WaitForOccurrence();
		await harness.Stop();
		Assert.That(ScheduledFor(fired), Is.EqualTo(LocalTime(_wednesdayMorning.AddSeconds(10))));
	}

	private static DateTimeOffset ScheduledFor(EventOccurrence occurrence)
	{
		return DateTimeOffset.Parse((string)occurrence.Parameters["scheduledFor"]!, CultureInfo.InvariantCulture);
	}

	private static EventSubscription Subscription(
		EventTriggerOwner owner,
		string eventId,
		(string Name, string Json)[] configuration)
	{
		var values = configuration.ToDictionary(c => c.Name,
			c => new EventConfigurationValue(JsonDocument.Parse(c.Json).RootElement.Clone()),
			StringComparer.Ordinal);

		return new EventSubscription(owner, "t1", $"{TimeEventProvider.ProviderIdValue}::{eventId}", values, null);
	}

	private sealed class Harness
	{
		private readonly FakeTimeProvider _time;
		private readonly FakeSubscriptionIndex _index;
		private readonly RecordingEventBus _bus;
		private readonly ScheduledEventBackgroundService _service;

		private Harness(
			FakeTimeProvider time,
			FakeSubscriptionIndex index,
			RecordingEventBus bus,
			ScheduledEventBackgroundService service)
		{
			_time = time;
			_index = index;
			_bus = bus;
			_service = service;
		}

		public static async Task<Harness> Start(EventSubscription subscription)
		{
			var time = new FakeTimeProvider { Now = LocalTime(_wednesdayMorning) };
			var index = new FakeSubscriptionIndex();
			index.Set(subscription);
			var bus = new RecordingEventBus();
			var readiness = new StartupReadiness();
			readiness.MarkCachesReady();
			readiness.MarkVariablesReady();

			var service = new ScheduledEventBackgroundService(new StartedHostLifetime(),
				bus,
				index,
				readiness,
				time,
				Logger.None);
			await service.StartAsync(CancellationToken.None);
			await WaitUntil(() => index.PlanCount > 0);

			return new Harness(time, index, bus, service);
		}

		public async Task Edit(EventSubscription subscription)
		{
			_index.Set(subscription);
			await Wake();
		}

		public async Task Elapse(TimeSpan delta)
		{
			_time.Advance(delta);
			await Wake();
		}

		public async Task<EventOccurrence> WaitForOccurrence()
		{
			await WaitUntil(() => !_bus.Published.IsEmpty);
			return _bus.Published.First();
		}

		public async Task Stop()
		{
			await _service.StopAsync(CancellationToken.None);
			_service.Dispose();
		}

		private async Task Wake()
		{
			var planned = _index.PlanCount;
			_index.RaiseChanged();
			await WaitUntil(() => _index.PlanCount > planned);
		}
	}

	private static async Task WaitUntil(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail("The condition was not met in time.");
			}

			await Task.Delay(10);
		}
	}

	private sealed class FakeSubscriptionIndex : IEventSubscriptionIndex
	{
		private readonly Lock _lock = new();
		private EventSubscription? _subscription;
		private int _planCount;

		public int PlanCount => Volatile.Read(ref _planCount);

		public event Action? Changed;

		public void Set(EventSubscription subscription)
		{
			lock (_lock)
			{
				_subscription = subscription;
			}
		}

		public void RaiseChanged()
		{
			Changed?.Invoke();
		}

		public IReadOnlyList<EventSubscription> FindByProvider(string providerId)
		{
			lock (_lock)
			{
				Interlocked.Increment(ref _planCount);
				return _subscription is null ? [] : [_subscription];
			}
		}

		public EventSubscription? Find(EventTarget target)
		{
			lock (_lock)
			{
				return _subscription?.Target == target ? _subscription : null;
			}
		}

		public bool HasSubscribers(string qualifiedEventId)
		{
			throw new NotSupportedException();
		}

		public IReadOnlyList<EventSubscription> Find(string qualifiedEventId)
		{
			throw new NotSupportedException();
		}

		public IReadOnlyCollection<string> ProviderIds()
		{
			throw new NotSupportedException();
		}

		public void Rebuild()
		{
			throw new NotSupportedException();
		}

		public void ReindexWidget(Guid widgetId, string? widgetData)
		{
			throw new NotSupportedException();
		}

		public void ReindexAutomation(Guid automationId, string? flows, bool enabled)
		{
			throw new NotSupportedException();
		}

		public void Remove(EventTriggerOwner owner)
		{
			throw new NotSupportedException();
		}
	}

	private sealed class RecordingEventBus : IEventBus
	{
		public ConcurrentQueue<EventOccurrence> Published { get; } = new();

		public ChannelReader<EventOccurrence> Reader => throw new NotSupportedException();

		public void Publish(EventOccurrence occurrence)
		{
			Published.Enqueue(occurrence);
		}
	}

	private sealed class StartedHostLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted { get; } = new(true);
		public CancellationToken ApplicationStopping { get; } = CancellationToken.None;
		public CancellationToken ApplicationStopped { get; } = CancellationToken.None;

		public void StopApplication()
		{
		}
	}
}
