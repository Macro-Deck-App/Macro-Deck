using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Triggers.Providers;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Application.Calendar;

public sealed class CalendarTriggerScheduler : IDisposable
{
	private static readonly TimeSpan _maximumSleep = TimeSpan.FromHours(1);

	private static readonly string _startsSoonId = CalendarEventProvider.Qualify(CalendarEventProvider.StartsSoonEventId);
	private static readonly string _startedId = CalendarEventProvider.Qualify(CalendarEventProvider.StartedEventId);
	private static readonly string _endedId = CalendarEventProvider.Qualify(CalendarEventProvider.EndedEventId);

	private static readonly CalendarFiringTarget _started = new(CalendarFiringKind.Started, _startedId);
	private static readonly CalendarFiringTarget _ended = new(CalendarFiringKind.Ended, _endedId);

	private readonly IEventSubscriptionIndex _index;
	private readonly ICalendarEventCache _cache;
	private readonly IEventBus _bus;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;

	private readonly CalendarFiringPlanner _planner = new();
	private readonly SemaphoreSlim _runLock = new(1, 1);

	public CalendarTriggerScheduler(
		IEventSubscriptionIndex index,
		ICalendarEventCache cache,
		IEventBus bus,
		IServiceScopeFactory scopeFactory,
		TimeProvider time,
		ILogger logger)
	{
		_index = index;
		_cache = cache;
		_bus = bus;
		_scopeFactory = scopeFactory;
		_time = time;
		_logger = logger.ForContext<CalendarTriggerScheduler>();
	}

	public async Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
	{
		await _runLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			var (due, next) = _planner.Plan(_cache.Snapshot, _time.GetUtcNow(), Targets());

			foreach (var firing in due)
			{
				await FireAsync(firing).ConfigureAwait(false);
			}

			if (next is null)
			{
				return _maximumSleep;
			}

			var sleep = next.Value - _time.GetUtcNow();
			return sleep < TimeSpan.Zero ? TimeSpan.Zero : sleep > _maximumSleep ? _maximumSleep : sleep;
		}
		finally
		{
			_runLock.Release();
		}
	}

	private List<CalendarFiringTarget> Targets()
	{
		var targets = new List<CalendarFiringTarget>();

		if (_index.HasSubscribers(_startedId))
		{
			targets.Add(_started);
		}

		if (_index.HasSubscribers(_endedId))
		{
			targets.Add(_ended);
		}

		targets.AddRange(_index.Find(_startsSoonId)
			.Select(subscription => new CalendarFiringTarget(CalendarFiringKind.StartsSoon,
				subscription.Target,
				CalendarEventProvider.LeadTime(subscription),
				Context: subscription)));

		return targets;
	}

	private async Task FireAsync(CalendarFiring firing)
	{
		try
		{
			await using var scope = _scopeFactory.CreateAsyncScope();
			var parameters = await CalendarPayloads.BuildAsync(scope.ServiceProvider, firing.Event).ConfigureAwait(false);

			var eventId = firing.Target.Kind switch
			{
				CalendarFiringKind.Started => _startedId,
				CalendarFiringKind.Ended => _endedId,
				_ => _startsSoonId,
			};

			if (firing.Target.Context is not EventSubscription subscription)
			{
				_bus.Publish(new EventOccurrence(eventId, parameters));
				return;
			}

			var contexts = scope.ServiceProvider.GetRequiredService<IEventTriggerContextResolver>();
			if (contexts.ResolveSource(subscription.Owner) is not { } source ||
				!await contexts.MatchesAsync(subscription, source, eventId, parameters).ConfigureAwait(false))
			{
				return;
			}

			_bus.Publish(new EventOccurrence(eventId, parameters, subscription.Target));
		}
		catch (Exception exception)
		{
			_logger.Error(exception,
				"Calendar trigger {Kind} for event {InstanceKey} failed",
				firing.Target.Kind,
				firing.Event.InstanceKey);
		}
	}

	public void Dispose() => _runLock.Dispose();
}
