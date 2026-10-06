using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Applications;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Localization;
using ILogger = Serilog.ILogger;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Calendar;

namespace MacroDeckHost.Widgets.Calendar;

internal sealed record CalendarAgendaDialogState(
	bool IsMissing,
	bool HasNoCalendars,
	IReadOnlyList<CalendarAgendaDay> Days,
	int HiddenEvents)
{
	public static CalendarAgendaDialogState Missing { get; } = new(true, false, [], 0);

	public bool HasEvents => Days.Any(day => day.Rows.Count > 0);
}

internal sealed record CalendarAgendaDialogActions(
	Action<CalendarEventSummary> Open,
	Action Back,
	Action Join,
	Func<UiAsyncState<CalendarDialogContent>> Selected);

internal sealed class CalendarAgendaDialogSession : IUiSession
{
	// With every row's texts bounded in encoded bytes, this keeps the busiest week within the UI tree limits.
	private const int MaxRows = 50;

	private static readonly TimeSpan _minimumDelay = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan _maximumDelay = TimeSpan.FromMinutes(5);

	private readonly ICalendarEventCache _cache;
	private readonly IFolderCache _folders;
	private readonly CalendarWidgetChanges _widgetChanges;
	private readonly IExternalUrlOpener _opener;
	private readonly TimeProvider _time;
	private readonly CalendarFormat _format;
	private readonly Guid? _widgetId;
	private readonly ILogger _logger;
	private readonly UiState<CalendarAgendaDialogState> _state;
	private readonly UiState<CalendarEventSummary?> _selected = new(null);
	private readonly UiState<LocalizedText> _feedback = new(default);
	private readonly UiView _view;
	private readonly ITimer _timer;
	private readonly Lock _sync = new();

	private UiAsyncState<CalendarDialogContent>? _details;
	private bool _disposed;

	public CalendarAgendaDialogSession(
		UiSurface surface,
		ICalendarEventCache cache,
		IFolderCache folders,
		CalendarWidgetChanges widgetChanges,
		IExternalUrlOpener opener,
		TimeProvider time,
		CalendarFormat format,
		string widgetId,
		ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(surface);
		ArgumentNullException.ThrowIfNull(cache);
		ArgumentNullException.ThrowIfNull(folders);
		ArgumentNullException.ThrowIfNull(widgetChanges);
		ArgumentNullException.ThrowIfNull(time);
		ArgumentNullException.ThrowIfNull(format);

		_cache = cache;
		_folders = folders;
		_widgetChanges = widgetChanges;
		_opener = opener;
		_time = time;
		_format = format;
		_widgetId = Guid.TryParse(widgetId, out var id) ? id : null;
		_logger = logger;
		_state = new UiState<CalendarAgendaDialogState>(Compute(time.GetUtcNow()).State);

		_view = new UiView(surface,
			CalendarAgendaDialogView.Build(_state,
				_selected,
				_feedback,
				format,
				new CalendarAgendaDialogActions(Open, Back, Join, () => _details!)));
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		_timer = time.CreateTimer(_ => Refresh(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
		_cache.Changed += Refresh;
		_widgetChanges.Changed += OnWidgetsChanged;

		// Also arms the timer, and a change landing between the first compute and the subscriptions is not lost.
		Refresh();
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree() => _view.Tree;

	public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

	public void Dispatch(UiEvent uiEvent) => _view.Dispatch(uiEvent);

	public ValueTask DisposeAsync()
	{
		_cache.Changed -= Refresh;
		_widgetChanges.Changed -= OnWidgetsChanged;

		lock (_sync)
		{
			_disposed = true;
		}

		_timer.Dispose();
		_view.Dispose();

		return ValueTask.CompletedTask;
	}

	private void OnWidgetsChanged(object? sender, EventArgs e) => Refresh();

	private void Open(CalendarEventSummary calendarEvent)
	{
		var details = CalendarEventDialogSession.ContentOf(_cache,
			calendarEvent.CalendarKey,
			calendarEvent.EventId,
			calendarEvent);
		_details = details;

		using (_view.Batch())
		{
			_feedback.Set(default);
			_selected.Set(calendarEvent);
		}

		details.Reload();
	}

	private void Back()
	{
		using (_view.Batch())
		{
			_selected.Set(null);
			_feedback.Set(default);
		}
	}

	private void Join()
	{
		var message = CalendarEventDialogSession.JoinMeeting(_opener, _details?.Peek().Summary?.MeetingUrl, _logger);

		using (_view.Batch())
		{
			_feedback.Set(message);
		}
	}

	private void Refresh()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			var now = _time.GetUtcNow();
			var computed = Compute(now);

			using (_view.Batch())
			{
				_state.Set(computed.State);
			}

			var delay = computed.NextRefresh - now;
			_timer.Change(delay < _minimumDelay ? _minimumDelay : delay > _maximumDelay ? _maximumDelay : delay,
				Timeout.InfiniteTimeSpan);
		}
	}

	private CalendarComputed<CalendarAgendaDialogState> Compute(DateTimeOffset now)
	{
		var widget = _widgetId is { } id
			? _folders.GetAllFolders()
				.SelectMany(folder => folder.Widgets)
				.FirstOrDefault(candidate => candidate.Id == id)
			: null;

		if (widget is null || !CalendarWidgetTypes.IsCalendarWidget(widget.Type))
		{
			return new CalendarComputed<CalendarAgendaDialogState>(CalendarAgendaDialogState.Missing,
				now + _maximumDelay);
		}

		var snapshot = _cache.Snapshot;
		var settings = CalendarAgendaSettings.Parse(CalendarWidgetData.Parse(widget.Data)) with
		{
			ShowTime = true,
			ShowLocation = true,
		};
		var (days, nextRefresh) = CalendarAgenda.DaysAhead(snapshot, now, settings, _format, int.MaxValue);
		var (shown, hidden) = Limit(days);

		return new CalendarComputed<CalendarAgendaDialogState>(
			new CalendarAgendaDialogState(false, snapshot.Accounts.Count == 0, shown, hidden),
			nextRefresh);
	}

	private static (IReadOnlyList<CalendarAgendaDay> Days, int Hidden) Limit(IReadOnlyList<CalendarAgendaDay> days)
	{
		var remaining = MaxRows;
		var shown = new List<CalendarAgendaDay>(days.Count);
		var shownEvents = new HashSet<string>(StringComparer.Ordinal);
		var hiddenEvents = new HashSet<string>(StringComparer.Ordinal);

		foreach (var day in days)
		{
			var rows = day.Rows.Take(remaining).Select(Fit).ToList();
			remaining -= rows.Count;
			shown.Add(day with { Rows = rows });
			shownEvents.UnionWith(rows.Select(row => row.Event.InstanceKey));
			hiddenEvents.UnionWith(day.Rows.Skip(rows.Count).Select(row => row.Event.InstanceKey));
		}

		hiddenEvents.ExceptWith(shownEvents);

		return (shown, hiddenEvents.Count);
	}

	private static CalendarAgendaRow Fit(CalendarAgendaRow row)
		=> row with
		{
			Title = CalendarFormat.Title(CalendarDialogText.Title(row.Event.Title)),
			Location = CalendarDialogText.Location(row.Location),
			Calendar = row.Calendar is null ? null : CalendarDialogText.Title(row.Calendar),
		};
}

internal static class CalendarAgendaDialogView
{
	public const string Root = "calendarAgendaDialog";

	private static readonly UiSize _text = UiSize.FromBasis(0.042);
	private static readonly UiSize _smallText = UiSize.FromBasis(0.034);
	private static readonly UiSize _icon = UiSize.FromBasis(0.042);
	private static readonly UiSize _gap = UiSize.FromBasis(0.022);
	private static readonly UiSize _rowPadding = UiSize.FromBasis(0.012);
	private static readonly UiSize _bar = UiSize.FromBasis(0.01);

	public static UiElement Build(
		UiState<CalendarAgendaDialogState> state,
		UiState<CalendarEventSummary?> selected,
		UiState<LocalizedText> feedback,
		CalendarFormat format,
		CalendarAgendaDialogActions actions)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(selected);
		ArgumentNullException.ThrowIfNull(feedback);
		ArgumentNullException.ThrowIfNull(format);
		ArgumentNullException.ThrowIfNull(actions);

		return new UiStack
		{
			Key = Root,
			Direction = UiComponentDirections.Vertical,
			Padding = 0.04,
			Gap = _gap,
			Children =
			[
				new UiWhen
				{
					Key = "widgetMissingGate",
					Condition = () => state.Value.IsMissing,
					Content = () => Notice("widgetMissing",
						AppStrings.Integrations.Calendar.Actions.ShowDetails.WidgetMissing()),
				},
				new UiWhen
				{
					Key = "noCalendarsGate",
					Condition = () => !state.Value.IsMissing && state.Value.HasNoCalendars,
					Content = () => Notice("noCalendars", Strings.NoCalendars()),
				},
				new UiWhen
				{
					Key = "listGate",
					Condition = () => !state.Value.IsMissing && !state.Value.HasNoCalendars && selected.Value is null,
					Content = () => List(state, actions),
				},
				new UiWhen
				{
					Key = "detailsGate",
					Condition = () => !state.Value.IsMissing && !state.Value.HasNoCalendars && selected.Value is not null,
					Content = () => Details(selected, feedback, format, actions),
				},
			],
		};
	}

	private static UiList List(UiState<CalendarAgendaDialogState> state, CalendarAgendaDialogActions actions)
		=> new()
		{
			Key = "agenda",
			Fill = true,
			Gap = _gap,
			Children =
			[
				new UiWhen
				{
					Key = "emptyGate",
					Condition = () => !state.Value.HasEvents,
					Content = () => Notice("empty", Strings.NoEvents()),
				},
				new UiRepeat<CalendarAgendaDay>
				{
					Key = "days",
					Items = UiValue.From<IReadOnlyList<CalendarAgendaDay>>(() =>
						state.Value.HasEvents ? [.. state.Value.Days.Where(day => day.Rows.Count > 0)] : []),
					KeySelector = day => day.Key,
					Template = (day, _) => Day(day, actions),
				},
				new UiWhen
				{
					Key = "moreGate",
					Condition = () => state.Value.HiddenEvents > 0,
					Content = () => new UiTextRun
					{
						Key = "more",
						Text = UiText.Optional(() => Strings.MoreEvents(state.Value.HiddenEvents)),
						Size = _smallText,
						Role = UiComponentTextRoles.Secondary,
						Align = UiComponentAlignments.Center,
						Wrap = true,
					},
				},
			],
		};

	private static UiStack Day(CalendarAgendaDay day, CalendarAgendaDialogActions actions)
	{
		var children = new List<UiElement>
		{
			new UiTextRun
			{
				Key = "heading",
				Text = day.Heading,
				Size = _smallText,
				Weight = UiComponentTextWeights.SemiBold,
				Role = UiComponentTextRoles.Muted,
				MaxLines = 1,
			},
		};

		children.AddRange(day.Rows.Select(row => Row(row, actions)));

		return new UiStack
		{
			Key = day.Key,
			Direction = UiComponentDirections.Vertical,
			Gap = _gap,
			Children = children,
		};
	}

	private static UiStack Row(CalendarAgendaRow row, CalendarAgendaDialogActions actions)
	{
		var details = new List<UiElement>();

		if (!row.Time.IsEmpty)
		{
			details.Add(new UiTextRun
			{
				Key = "time",
				Text = row.Time,
				Size = _smallText,
				Weight = UiComponentTextWeights.Medium,
				Role = UiComponentTextRoles.Secondary,
				MaxLines = 1,
			});
		}

		if (row.Location is { } location)
		{
			details.Add(Detail("location", location));
		}

		if (row.Calendar is { } calendar)
		{
			details.Add(Detail("calendar", calendar));
		}

		var text = new List<UiElement>
		{
			new UiTextRun
			{
				Key = "title",
				Text = row.Title,
				Size = _text,
				Wrap = true,
				MaxLines = 2,
			},
		};

		if (details.Count > 0)
		{
			text.Add(new UiStack
			{
				Key = "details",
				Direction = UiComponentDirections.Horizontal,
				Gap = _gap,
				Children = details,
			});
		}

		return new UiStack
		{
			Key = row.Key,
			Direction = UiComponentDirections.Horizontal,
			Align = UiComponentAlignments.Center,
			Gap = _gap,
			Padding = _rowPadding,
			Events = [UiEventHandler.On(UiComponentEvents.Press, () => actions.Open(row.Event))],
			Children =
			[
				new UiStack
				{
					Key = "bar",
					MainSize = _bar,
					Background = row.Color ?? CalendarWidgetParts.DefaultBarColor,
				},
				new UiStack
				{
					Key = "text",
					Direction = UiComponentDirections.Vertical,
					Fill = true,
					Children = text,
				},
				new UiIcon
				{
					Key = "open",
					Icon = UiIcons.ChevronRight,
					Size = _icon,
					MainSize = _icon,
					Role = UiComponentTextRoles.Secondary,
				},
			],
		};
	}

	private static UiStack Details(
		UiState<CalendarEventSummary?> selected,
		UiState<LocalizedText> feedback,
		CalendarFormat format,
		CalendarAgendaDialogActions actions)
		=> new()
		{
			Key = "details",
			Direction = UiComponentDirections.Vertical,
			Fill = true,
			Gap = _gap,
			Children =
			[
				new UiStack
				{
					Key = "header",
					Direction = UiComponentDirections.Horizontal,
					Align = UiComponentAlignments.Center,
					Gap = _gap,
					Children =
					[
						new UiButton
						{
							Key = "back",
							Direction = UiComponentDirections.Horizontal,
							Justify = UiComponentJustify.Center,
							Align = UiComponentAlignments.Center,
							Gap = _gap,
							MainSize = UiSize.FromBasis(0.24),
							Padding = _gap,
							Events = [UiEventHandler.On(UiComponentEvents.Press, actions.Back)],
							Children =
							[
								new UiIcon { Key = "icon", Icon = UiIcons.ArrowLeft, Size = _icon, MainSize = _icon },
								new UiTextRun
								{
									Key = "label",
									Text = MacroDeckStrings.Common.Back(),
									Size = _text,
									Weight = UiComponentTextWeights.Medium,
									MaxLines = 1,
								},
							],
						},
						new UiTextRun
						{
							Key = "eventTitle",
							Text = UiText.Optional(() => selected.Value is { } summary
								? CalendarFormat.Title(CalendarDialogText.Title(summary.Title))
								: default),
							Size = _text,
							Weight = UiComponentTextWeights.SemiBold,
							Wrap = true,
							MaxLines = 2,
							Fill = true,
						},
					],
				},
				.. CalendarEventDialogView.Body(actions.Selected(), feedback, format, actions.Join),
			],
		};

	private static UiTextRun Notice(string key, LocalizedText text)
		=> new()
		{
			Key = key,
			Text = text,
			Size = _text,
			Role = UiComponentTextRoles.Secondary,
			Align = UiComponentAlignments.Center,
			Wrap = true,
			Fill = true,
		};

	private static UiTextRun Detail(string key, string text)
		=> new()
		{
			Key = key,
			Text = text,
			Size = _smallText,
			Role = UiComponentTextRoles.Secondary,
			MaxLines = 1,
		};
}
