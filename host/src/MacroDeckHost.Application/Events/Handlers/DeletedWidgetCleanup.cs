using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Sessions.InProcess;

namespace MacroDeckHost.Application.Events.Handlers;

internal static class DeletedWidgetCleanup
{
	public static void Run(IUiSessionBroker sessions, WidgetUiProviderRegistry widgetUiProviders, Guid widgetId)
	{
		// Closed outright, not invalidated for a reopen: a deleted widget has no configuration left
		// to rebuild from.
		sessions.CloseWidgetSessions(widgetId, "The widget was deleted.");
		widgetUiProviders.EvictWidget(widgetId);
	}
}
