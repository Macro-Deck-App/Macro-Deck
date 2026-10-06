using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeckHost.Application.Calendar;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Calendar.Actions.ShowDetails;

namespace MacroDeckHost.Integrations.Calendar;

internal sealed class ShowDetailsActionDefinition : IActionDefinition
{
	public const string NotCalendarWidgetCode = "CALENDAR_NOT_A_CALENDAR_WIDGET";
	public const string WidgetMissingCode = "CALENDAR_WIDGET_NOT_FOUND";
	public const string NoEventCode = "CALENDAR_NO_EVENT";

	private readonly Func<CalendarHostServices?> _services;

	public ShowDetailsActionDefinition(Func<CalendarHostServices?> services)
	{
		_services = services;
	}

	public string Id => CalendarWidgetTypes.ShowDetailsActionId;

	public LocalizedText Name => Strings.Name();

	public LocalizedText Description => Strings.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

	public IActionExecutor CreateExecutor() => new Executor(_services);

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<CalendarHostServices?> _services;

		public Executor(Func<CalendarHostServices?> services)
		{
			_services = services;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			ArgumentNullException.ThrowIfNull(context);

			// A run nobody started from a screen has nobody to show the dialog to, which is not a failure.
			if (context.Ui is null)
			{
				return ActionResult.Success();
			}

			if (_services() is not { } services ||
				!Guid.TryParse(context.OwnerWidgetId, out var widgetId))
			{
				return ActionResult.Failed(NotCalendarWidgetCode, Strings.NotCalendarWidget());
			}

			var widget = services.Folders.GetAllFolders()
				.SelectMany(folder => folder.Widgets)
				.FirstOrDefault(candidate => candidate.Id == widgetId);

			if (widget is null)
			{
				return ActionResult.Failed(WidgetMissingCode, Strings.WidgetMissing());
			}

			if (!CalendarWidgetTypes.IsCalendarWidget(widget.Type))
			{
				return ActionResult.Failed(NotCalendarWidgetCode, Strings.NotCalendarWidget());
			}

			ModalDefinition modal;

			if (CalendarWidgetData.IsNextEvent(CalendarWidgetData.Parse(widget.Data)))
			{
				var shown = CalendarWidgetSelection.ShownEvent(widget.Type,
					widget.Data,
					services.Events.Snapshot,
					services.Time.GetUtcNow(),
					services.Events.TimeZone);

				if (shown is null)
				{
					return ActionResult.Failed(NoEventCode, Strings.NoEvent());
				}

				modal = CalendarEventDialogs.For(shown);
			}
			else
			{
				modal = CalendarEventDialogs.ForAgenda(widget.Id);
			}

			await context.Ui.ShowModalAsync(context.OriginClientId, modal, context.CancellationToken)
				.ConfigureAwait(false);

			return ActionResult.Success();
		}
	}
}
