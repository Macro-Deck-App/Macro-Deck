using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Calendar;

public sealed class CalendarWidgetLifecycle
{
	private readonly CalendarWidgetVariableWriter _variables;
	private readonly CalendarWidgetTriggerScheduler _triggers;
	private readonly CalendarWidgetChanges _changes;

	public CalendarWidgetLifecycle(
		CalendarWidgetVariableWriter variables,
		CalendarWidgetTriggerScheduler triggers,
		CalendarWidgetChanges changes)
	{
		_variables = variables;
		_triggers = triggers;
		_changes = changes;
	}

	public async ValueTask ChangedAsync(IEnumerable<WidgetEntity> widgets)
	{
		foreach (var widget in widgets)
		{
			await _variables.RefreshAsync(widget).ConfigureAwait(false);
		}

		_triggers.RequestReplan();
		_changes.Notify();
	}

	public async ValueTask DeletedAsync(IEnumerable<Guid> widgetIds)
	{
		foreach (var widgetId in widgetIds)
		{
			await _variables.ForgetAsync(widgetId).ConfigureAwait(false);
		}

		_triggers.RequestReplan();
		_changes.Notify();
	}

	public void ProfileDeleted()
	{
		_triggers.RequestReplan();
		_changes.Notify();
	}
}
