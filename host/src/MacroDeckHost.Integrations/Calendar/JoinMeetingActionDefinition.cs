using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Applications;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Calendar;

internal sealed class JoinMeetingActionDefinition : IActionDefinition
{
	public const string ActionId = "join-meeting";
	public const string WindowParameter = "window";
	public const string CalendarParameter = "calendar";

	public const string NoMeetingCode = "CALENDAR_NO_MEETING";
	public const string HostLockedCode = "CALENDAR_HOST_LOCKED";
	public const string OpenFailedCode = "CALENDAR_OPEN_FAILED";

	private readonly Func<CalendarHostServices?> _services;

	public JoinMeetingActionDefinition(Func<CalendarHostServices?> services)
	{
		_services = services;
	}

	public string Id => ActionId;

	public LocalizedText Name => AppStrings.Integrations.Calendar.Actions.JoinMeeting.Name();

	public LocalizedText Description => AppStrings.Integrations.Calendar.Actions.JoinMeeting.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Duration(WindowParameter,
			label: AppStrings.Integrations.Calendar.Actions.JoinMeeting.WindowLabel(),
			description: AppStrings.Integrations.Calendar.Actions.JoinMeeting.WindowDescription(),
			min: 0,
			max: TimeSpan.FromDays(1).TotalMilliseconds,
			defaultMilliseconds: CalendarMeetings.DefaultJoinWindow.TotalMilliseconds),
		ActionParameter.DynamicChoice(CalendarParameter,
			label: AppStrings.Events.Calendar.CalendarLabel(),
			description: AppStrings.Integrations.Calendar.Actions.JoinMeeting.CalendarDescription(),
			optionsSourceId: CalendarOptionsSourceIds.Calendars,
			placeholder: AppStrings.Events.Calendar.AnyCalendarPlaceholder())
	];

	public IActionExecutor CreateExecutor() => new Executor(_services);

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<CalendarHostServices?> _services;

		public Executor(Func<CalendarHostServices?> services)
		{
			_services = services;
		}

		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (_services() is not { } services)
			{
				return Task.FromResult(ActionResult.Failed(OpenFailedCode,
					AppStrings.Integrations.Calendar.Actions.JoinMeeting.OpenFailed()));
			}

			var window = ReadWindow(context.Parameters);
			var calendarKey = context.Parameters.GetValueOrDefault(CalendarParameter) as string;
			var meeting = CalendarMeetings.FindJoinable(services.Events.Snapshot,
				services.Time.GetUtcNow(),
				window,
				calendarKey);

			if (meeting?.MeetingUrl is not { } url)
			{
				return Task.FromResult(ActionResult.Failed(NoMeetingCode,
					AppStrings.Integrations.Calendar.Actions.JoinMeeting.NoMeeting()));
			}

			var opened = services.UrlOpener.Open(url);
			if (opened.Success)
			{
				return ActionResult.SucceededTask;
			}

			return Task.FromResult(opened.Error == ExternalUrlOpenError.HostLocked
				? ActionResult.Failed(HostLockedCode, AppStrings.Integrations.Calendar.Actions.JoinMeeting.HostLocked())
				: ActionResult.Failed(OpenFailedCode, AppStrings.Integrations.Calendar.Actions.JoinMeeting.OpenFailed()));
		}

		private static TimeSpan ReadWindow(IReadOnlyDictionary<string, object> parameters)
		{
			double? milliseconds = parameters.GetValueOrDefault(WindowParameter) switch
			{
				long value => value,
				int value => value,
				double value => value,
				string text when double.TryParse(text,
					NumberStyles.Float,
					CultureInfo.InvariantCulture,
					out var parsed) => parsed,
				_ => null
			};

			return milliseconds is null or < 0 || double.IsNaN(milliseconds.Value)
				? CalendarMeetings.DefaultJoinWindow
				: TimeSpan.FromMilliseconds(Math.Min(milliseconds.Value, TimeSpan.FromDays(1).TotalMilliseconds));
		}
	}
}
