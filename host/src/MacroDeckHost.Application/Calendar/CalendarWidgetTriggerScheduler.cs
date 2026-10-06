using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Application.Calendar;

public sealed class CalendarWidgetTriggerScheduler : IDisposable
{
	private static readonly TimeSpan _maximumSleep = TimeSpan.FromHours(1);

	private static readonly (CalendarFiringKind Kind, string Trigger)[] _triggers =
	[
		(CalendarFiringKind.Started, WidgetTriggerTypes.CalendarEventStarted),
		(CalendarFiringKind.Ended, WidgetTriggerTypes.CalendarEventEnded),
		(CalendarFiringKind.StartsSoon, WidgetTriggerTypes.CalendarEventStartsSoon),
	];

	private readonly IFolderCache _folders;
	private readonly ICalendarEventCache _cache;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly CalendarFiringPlanner _planner = new();
	private readonly SemaphoreSlim _runLock = new(1, 1);

	public CalendarWidgetTriggerScheduler(
		IFolderCache folders,
		ICalendarEventCache cache,
		IServiceScopeFactory scopeFactory,
		TimeProvider time,
		ILogger logger)
	{
		_folders = folders;
		_cache = cache;
		_scopeFactory = scopeFactory;
		_time = time;
		_logger = logger.ForContext<CalendarWidgetTriggerScheduler>();
	}

	public event Action? ReplanRequested;

	public void RequestReplan() => ReplanRequested?.Invoke();

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

	public void Dispose() => _runLock.Dispose();

	private List<CalendarFiringTarget> Targets()
	{
		var targets = new List<CalendarFiringTarget>();

		foreach (var widget in _folders.GetAllFolders().SelectMany(folder => folder.Widgets))
		{
			if (!CalendarWidgetTypes.IsCalendarWidget(widget.Type) ||
				CalendarWidgetSelection.EventFilter(widget.Type, widget.Data) is not { } filter)
			{
				continue;
			}

			foreach (var (kind, trigger) in _triggers)
			{
				if (WidgetFlowsJson.HasRunnableFlow(widget.Data, trigger))
				{
					targets.Add(new CalendarFiringTarget(kind,
						new WidgetTarget(widget.Id, trigger),
						filter.LeadTime,
						filter.Accepts));
				}
			}
		}

		return targets;
	}

	private async Task FireAsync(CalendarFiring firing)
	{
		var target = (WidgetTarget)firing.Target.Key;

		try
		{
			if (FindWidget(target.WidgetId) is not { } widget)
			{
				return;
			}

			await using var scope = _scopeFactory.CreateAsyncScope();
			var parameters = await CalendarPayloads.BuildAsync(scope.ServiceProvider, firing.Event).ConfigureAwait(false);

			// Host-initiated like a countdown finishing: nobody pressed anything, so no host-lock gate applies.
			var result = await scope.ServiceProvider.GetRequiredService<IFlowExecutor>()
				.ExecuteAsync(new FlowExecutionRequest
					{
						FlowsSource = widget.Data,
						Trigger = TriggerSelector.ByType(target.Trigger),
						Scope = VariableScope.Widget,
						ScopeRefId = widget.Id.ToString(),
						OwnerWidgetId = widget.Id,
						Origin = ExecutionOrigin.Host,
						EventParameters = parameters,
					},
					CancellationToken.None)
				.ConfigureAwait(false);

			if (result.Status != FlowExecutionStatus.Succeeded)
			{
				_logger.Warning("{Trigger} flow of calendar widget {WidgetId} finished as {Status}: {ErrorCode}",
					target.Trigger,
					target.WidgetId,
					result.Status,
					result.ErrorCode);
			}
		}
		catch (Exception exception)
		{
			_logger.Error(exception,
				"{Trigger} flow of calendar widget {WidgetId} for event {InstanceKey} failed",
				target.Trigger,
				target.WidgetId,
				firing.Event.InstanceKey);
		}
	}

	private WidgetEntity? FindWidget(Guid widgetId)
		=> _folders.GetAllFolders().SelectMany(folder => folder.Widgets).FirstOrDefault(widget => widget.Id == widgetId);

	private readonly record struct WidgetTarget(Guid WidgetId, string Trigger);
}
