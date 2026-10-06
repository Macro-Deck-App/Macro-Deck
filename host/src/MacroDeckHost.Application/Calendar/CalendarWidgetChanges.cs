namespace MacroDeckHost.Application.Calendar;

public sealed class CalendarWidgetChanges
{
	public event EventHandler? Changed;

	public void Notify() => Changed?.Invoke(this, EventArgs.Empty);
}
