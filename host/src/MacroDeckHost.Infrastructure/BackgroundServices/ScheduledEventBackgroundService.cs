using System.Text.Json;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Infrastructure.Triggers;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class ScheduledEventBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _maximumSleep = TimeSpan.FromHours(1);

	private readonly IEventBus _bus;
	private readonly IEventSubscriptionIndex _index;
	private readonly StartupReadiness _readiness;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;

	private readonly SemaphoreSlim _replan = new(0, 1);

	public ScheduledEventBackgroundService(
		IHostApplicationLifetime lifetime,
		IEventBus bus,
		IEventSubscriptionIndex index,
		StartupReadiness readiness,
		TimeProvider time,
		ILogger logger)
		: base(lifetime)
	{
		_bus = bus;
		_index = index;
		_readiness = readiness;
		_time = time;
		_logger = logger.ForContext<ScheduledEventBackgroundService>();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		await _readiness.WhenReady.WaitAsync(stoppingToken);

		_index.Changed += OnIndexChanged;
		try
		{
			var due = new Dictionary<EventTarget, Planned>();

			while (!stoppingToken.IsCancellationRequested)
			{
				var now = _time.GetUtcNow();
				var next = Plan(due, now);

				await FireDue(due, now, stoppingToken);

				var sleep = next is null
					? _maximumSleep
					: Clamp(next.Value - _time.GetUtcNow());

				await _replan.WaitAsync(sleep, stoppingToken);
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}
		finally
		{
			_index.Changed -= OnIndexChanged;
		}
	}

	private DateTimeOffset? Plan(Dictionary<EventTarget, Planned> due, DateTimeOffset now)
	{
		var timeZone = TimeZoneInfo.Local;
		var subscriptions = _index.FindByProvider(TimeEventProvider.ProviderIdValue);
		var live = new HashSet<EventTarget>();

		foreach (var subscription in subscriptions)
		{
			var target = subscription.Target;
			live.Add(target);

			var fingerprint = Fingerprint(subscription);
			if (due.TryGetValue(target, out var planned) && planned.Schedule == fingerprint)
			{
				continue;
			}

			var next = TriggerSchedule.TryCreate(subscription)?.NextOccurrence(now, timeZone);
			if (next is null)
			{
				due.Remove(target);
			}
			else
			{
				due[target] = new Planned(next.Value, fingerprint);
			}
		}

		foreach (var stale in due.Keys.Where(target => !live.Contains(target)).ToList())
		{
			due.Remove(stale);
		}

		return due.Count == 0 ? null : due.Values.Min(planned => planned.At);
	}

	private Task FireDue(
		Dictionary<EventTarget, Planned> due,
		DateTimeOffset now,
		CancellationToken cancellationToken)
	{
		var timeZone = TimeZoneInfo.Local;

		foreach (var (target, planned) in due.Where(entry => entry.Value.At <= now).ToList())
		{
			var subscription = _index.Find(target);
			var schedule = subscription is null ? null : TriggerSchedule.TryCreate(subscription);
			if (subscription is null || schedule is null)
			{
				due.Remove(target);
				continue;
			}

			var fingerprint = Fingerprint(subscription);
			if (planned.Schedule != fingerprint)
			{
				var replanned = schedule.NextOccurrence(now, timeZone);
				if (replanned is null)
				{
					due.Remove(target);
				}
				else
				{
					due[target] = new Planned(replanned.Value, fingerprint);
				}

				continue;
			}

			var scheduledFor = planned.At;
			_bus.Publish(new EventOccurrence(subscription.EventId,
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["firedAt"] = now.ToLocalTime().ToString("O"),
					["scheduledFor"] = scheduledFor.ToLocalTime().ToString("O")
				},
				target));

			var next = schedule.NextOccurrence(now, timeZone);
			if (next is null)
			{
				due.Remove(target);
			}
			else
			{
				due[target] = new Planned(next.Value, fingerprint);
			}
		}

		return Task.CompletedTask;
	}

	private static string Fingerprint(EventSubscription subscription)
	{
		var configuration = subscription.Configuration
			.OrderBy(entry => entry.Key, StringComparer.Ordinal)
			.Select(entry => $"{entry.Key}\u001f{entry.Value.Operator}\u001f{RawText(entry.Value.Value)}");
		return string.Join('\u001e', configuration.Prepend(subscription.EventId));
	}

	private static string RawText(JsonElement value)
	{
		return value.ValueKind == JsonValueKind.Undefined ? string.Empty : value.GetRawText();
	}

	private static TimeSpan Clamp(TimeSpan sleep)
	{
		return sleep < TimeSpan.Zero ? TimeSpan.Zero : sleep > _maximumSleep ? _maximumSleep : sleep;
	}

	private void OnIndexChanged()
	{
		try
		{
			_replan.Release();
		}
		catch (SemaphoreFullException)
		{
		}
	}

	private readonly record struct Planned(DateTimeOffset At, string Schedule);
}
