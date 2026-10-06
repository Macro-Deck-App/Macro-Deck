using System.Collections.Concurrent;
using System.Globalization;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Application.Calendar;

// The names are frozen: flows and templates reference them as widget variables.
public sealed class CalendarWidgetVariableWriter
{
	public const string Title = "calendar_next_title";

	public const string Start = "calendar_next_start";

	public const string End = "calendar_next_end";

	public const string Countdown = "calendar_next_countdown";

	public const string Minutes = "calendar_next_minutes";

	public const string Running = "calendar_next_running";

	public const string Location = "calendar_next_location";

	public const string MeetingUrl = "calendar_next_meeting_url";

	public const string Calendar = "calendar_next_calendar";

	private readonly IFolderCache _folders;
	private readonly ICalendarEventCache _cache;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();
	private readonly ConcurrentDictionary<Guid, byte> _written = new();

	public CalendarWidgetVariableWriter(
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
		_logger = logger.ForContext<CalendarWidgetVariableWriter>();
	}

	public static IReadOnlyList<string> Names { get; } =
		[Title, Start, End, Countdown, Minutes, Running, Location, MeetingUrl, Calendar];

	public async Task<DateTimeOffset> RefreshAllAsync()
	{
		var now = _time.GetUtcNow();
		var next = CalendarTime.NextMidnight(now, _cache.TimeZone);
		var present = new HashSet<Guid>();

		foreach (var widget in CalendarWidgets())
		{
			present.Add(widget.Id);
			var change = await WriteAsync(widget, now).ConfigureAwait(false);
			next = change < next ? change : next;
		}

		foreach (var widgetId in _written.Keys.Where(id => !present.Contains(id)).ToList())
		{
			await ForgetAsync(widgetId).ConfigureAwait(false);
		}

		return next;
	}

	public async Task RefreshAsync(WidgetEntity widget)
	{
		ArgumentNullException.ThrowIfNull(widget);

		if (CalendarWidgetTypes.IsCalendarWidget(widget.Type))
		{
			await WriteAsync(widget, _time.GetUtcNow()).ConfigureAwait(false);
		}
		else if (_written.ContainsKey(widget.Id))
		{
			await ForgetAsync(widget.Id).ConfigureAwait(false);
		}
	}

	public async Task ForgetAsync(Guid widgetId)
	{
		var gate = _gates.GetOrAdd(widgetId, _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync().ConfigureAwait(false);

		try
		{
			if (_written.TryRemove(widgetId, out _))
			{
				await RemoveAsync(widgetId).ConfigureAwait(false);
			}
		}
		catch (Exception exception)
		{
			_logger.Warning(exception, "Failed to remove the calendar variables of widget {WidgetId}", widgetId);
		}
		finally
		{
			gate.Release();
		}
	}

	private async Task<DateTimeOffset> WriteAsync(WidgetEntity widget, DateTimeOffset now)
	{
		var shown = CalendarWidgetSelection.ShownEvent(widget.Type, widget.Data, _cache.Snapshot, now, _cache.TimeZone);
		var gate = _gates.GetOrAdd(widget.Id, _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync().ConfigureAwait(false);

		try
		{
			// A write queued before the widget was deleted must not bring its variables back.
			if (!Exists(widget.Id))
			{
				return DateTimeOffset.MaxValue;
			}

			if (shown is null)
			{
				if (_written.TryRemove(widget.Id, out _))
				{
					await RemoveAsync(widget.Id).ConfigureAwait(false);
				}

				return DateTimeOffset.MaxValue;
			}

			_written[widget.Id] = 0;
			await UpsertAsync(widget.Id, shown, now).ConfigureAwait(false);

			// The widget can be deleted while the upserts above were running.
			if (!Exists(widget.Id) && _written.TryRemove(widget.Id, out _))
			{
				await RemoveAsync(widget.Id).ConfigureAwait(false);
			}

			var change = shown.Start <= now ? shown.End : CalendarCountdown.NextChange(shown.Start, now);

			// A zero-length event at this very instant changes nothing later, so it must not ask for an instant rerun.
			return change > now ? change : now + TimeSpan.FromSeconds(1);
		}
		catch (Exception exception)
		{
			_logger.Warning(exception, "Failed to write the calendar variables of widget {WidgetId}", widget.Id);
			return DateTimeOffset.MaxValue;
		}
		finally
		{
			gate.Release();
		}
	}

	private async Task UpsertAsync(Guid widgetId, CalendarEventSummary shown, DateTimeOffset now)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var variables = scope.ServiceProvider.GetRequiredService<IVariableService>();
		var countdown = await ActiveLocalization.Resolve(scope.ServiceProvider, CalendarCountdown.When(shown, now))
			.ConfigureAwait(false);
		var scopeRefId = widgetId.ToString();

		(string Name, VariableType Type, object? Value)[] values =
		[
			(Title, VariableType.Text, shown.Title),
			(Start, VariableType.Text, shown.Start.ToString("O", CultureInfo.InvariantCulture)),
			(End, VariableType.Text, shown.End.ToString("O", CultureInfo.InvariantCulture)),
			(Countdown, VariableType.Text, countdown),
			(Minutes, VariableType.Numeric, (decimal)CalendarCountdown.WholeMinutes(shown.Start - now)),
			(Running, VariableType.Boolean, shown.IsRunningAt(now)),
			(Location, VariableType.Text, shown.Location ?? string.Empty),
			(MeetingUrl, VariableType.Text, shown.MeetingUrl ?? string.Empty),
			(Calendar, VariableType.Text, shown.CalendarName),
		];

		foreach (var (name, type, value) in values)
		{
			await variables.UpsertWidgetVariable(VariableScope.Widget, scopeRefId, name, type, value).ConfigureAwait(false);
		}
	}

	private async Task RemoveAsync(Guid widgetId)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var variables = scope.ServiceProvider.GetRequiredService<IVariableService>();

		foreach (var name in Names)
		{
			await variables.RemoveWidgetVariable(VariableScope.Widget, widgetId.ToString(), name).ConfigureAwait(false);
		}
	}

	private List<WidgetEntity> CalendarWidgets()
		=> _folders.GetAllFolders()
			.SelectMany(folder => folder.Widgets)
			.Where(widget => CalendarWidgetTypes.IsCalendarWidget(widget.Type))
			.ToList();

	private bool Exists(Guid widgetId)
		=> _folders.GetAllFolders().Any(folder => folder.Widgets.Any(widget => widget.Id == widgetId));
}
