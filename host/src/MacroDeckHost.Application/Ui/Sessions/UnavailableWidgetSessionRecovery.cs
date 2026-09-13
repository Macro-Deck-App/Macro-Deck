using System.Collections.Concurrent;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Widgets;
using Mediator;

namespace MacroDeckHost.Application.Ui.Sessions;

public sealed class UnavailableWidgetSessionRecovery : IDisposable
{
	private const string RecoveredMessage = "The provider of this widget is available again.";

	private readonly UiSessionRegistry _sessions;
	private readonly IWidgetProviderAvailability _availability;
	private readonly IIntegrationRegistry _integrations;
	private readonly ConcurrentDictionary<string, string> _widgetTypeBySession = new(StringComparer.Ordinal);

	public UnavailableWidgetSessionRecovery(
		UiSessionRegistry sessions,
		IWidgetProviderAvailability availability,
		IIntegrationRegistry integrations)
	{
		_sessions = sessions;
		_availability = availability;
		_integrations = integrations;

		_sessions.Ended += OnSessionEnded;
		_integrations.AvailabilityChanged += OnAvailabilityChanged;
	}

	// False when the provider came back between the opener's check and here, with its event already handled:
	// the placeholder is ended before any client attached, so the caller must open the real session instead.
	public bool Track(UiSessionOpenTicket ticket, string widgetType)
	{
		if (!ticket.Accepted || _sessions.WasEnded(ticket.SessionId))
		{
			return true;
		}

		_widgetTypeBySession[ticket.SessionId] = widgetType;
		return !Release(ticket.SessionId, widgetType);
	}

	// A plugin registering several types raises one notification per type, so each placeholder is released
	// exactly once, when its own provider is back: an invalidation per notification would race the reopen.
	public void Reevaluate()
	{
		foreach (var (sessionId, widgetType) in _widgetTypeBySession)
		{
			Release(sessionId, widgetType);
		}
	}

	private bool Release(string sessionId, string widgetType)
	{
		if (_availability.Check(widgetType) is not null || !_widgetTypeBySession.TryRemove(sessionId, out _))
		{
			return false;
		}

		_sessions.TryInvalidate(sessionId, UiSessionErrorCodes.ProviderUnavailable, RecoveredMessage, retryable: true);
		return true;
	}

	public void Dispose()
	{
		_sessions.Ended -= OnSessionEnded;
		_integrations.AvailabilityChanged -= OnAvailabilityChanged;
	}

	private void OnSessionEnded(object? sender, UiSessionEndedEventArgs e)
		=> _widgetTypeBySession.TryRemove(e.Session.SessionId, out _);

	private void OnAvailabilityChanged(object? sender, IntegrationAvailabilityChangedEventArgs e) => Reevaluate();
}

public sealed class UnavailableWidgetSessionRecoveryHandler
	: INotificationHandler<WidgetTypeCatalogChangedNotification>,
		INotificationHandler<PluginSessionsChangedNotification>
{
	private readonly UnavailableWidgetSessionRecovery _recovery;

	public UnavailableWidgetSessionRecoveryHandler(UnavailableWidgetSessionRecovery recovery) => _recovery = recovery;

	public ValueTask Handle(WidgetTypeCatalogChangedNotification notification, CancellationToken cancellationToken)
	{
		_recovery.Reevaluate();
		return ValueTask.CompletedTask;
	}

	public ValueTask Handle(PluginSessionsChangedNotification notification, CancellationToken cancellationToken)
	{
		_recovery.Reevaluate();
		return ValueTask.CompletedTask;
	}
}
