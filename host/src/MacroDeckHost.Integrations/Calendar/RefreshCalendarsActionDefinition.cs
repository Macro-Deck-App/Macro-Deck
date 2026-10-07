using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Calendar;

internal sealed class RefreshCalendarsActionDefinition : IActionDefinition
{
	public const string ActionId = "refresh-calendars";

	public const string UnavailableCode = "CALENDAR_REFRESH_UNAVAILABLE";
	public const string AccountFailedCode = "CALENDAR_REFRESH_ACCOUNT_FAILED";

	private readonly Func<CalendarHostServices?> _services;
	private readonly Lock _gate = new();
	private Task? _running;

	public RefreshCalendarsActionDefinition(Func<CalendarHostServices?> services)
	{
		_services = services;
	}

	public string Id => ActionId;

	public LocalizedText Name => AppStrings.Integrations.Calendar.Actions.RefreshCalendars.Name();

	public LocalizedText Description => AppStrings.Integrations.Calendar.Actions.RefreshCalendars.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

	public IActionExecutor CreateExecutor() => new Executor(this);

	private sealed class Executor : IActionExecutor
	{
		private readonly RefreshCalendarsActionDefinition _definition;

		public Executor(RefreshCalendarsActionDefinition definition)
		{
			_definition = definition;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (_definition._services() is not { } services)
			{
				return ActionResult.Failed(UnavailableCode,
					AppStrings.Integrations.Calendar.Actions.RefreshCalendars.Unavailable());
			}

			await _definition.SyncOnceAsync(services.Events).WaitAsync(context.CancellationToken);

			return services.Events.Snapshot.Accounts.Any(account => account.Status == CalendarAccountStatus.Error)
				? ActionResult.Failed(AccountFailedCode, AppStrings.Widgets.Calendar.AccountError())
				: ActionResult.Success();
		}
	}

	// Presses during a refresh join it: each press would otherwise queue a full round of provider requests.
	private Task SyncOnceAsync(ICalendarEventCache events)
	{
		lock (_gate)
		{
			if (_running is not { IsCompleted: false })
			{
				_running = events.SyncAsync(CancellationToken.None);
			}

			return _running;
		}
	}
}
