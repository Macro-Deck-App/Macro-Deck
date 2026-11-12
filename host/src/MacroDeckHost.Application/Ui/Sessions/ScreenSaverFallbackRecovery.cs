using System.Collections.Concurrent;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.ScreenSavers;
using Mediator;

namespace MacroDeckHost.Application.Ui.Sessions;

public sealed class ScreenSaverFallbackRecovery : IDisposable
{
	private const string RecoveredMessage = "The selected screensaver is available again.";

	private readonly UiSessionRegistry _sessions;
	private readonly IScreenSaverRegistry _screenSavers;
	private readonly ConcurrentDictionary<string, string> _selectionBySession = new(StringComparer.Ordinal);

	public ScreenSaverFallbackRecovery(UiSessionRegistry sessions, IScreenSaverRegistry screenSavers)
	{
		_sessions = sessions;
		_screenSavers = screenSavers;

		_sessions.Ended += OnSessionEnded;
	}

	// False when the selection came back between the opener's check and here, with its event already handled:
	// the stand-in is ended before any client attached, so the caller must open the real session instead.
	public bool Track(string sessionId, string selectedScreenSaverId)
	{
		if (_sessions.WasEnded(sessionId))
		{
			return true;
		}

		_selectionBySession[sessionId] = selectedScreenSaverId;
		return !Release(sessionId, selectedScreenSaverId) && !_sessions.WasEnded(sessionId);
	}

	public void Forget(string sessionId)
	{
		_selectionBySession.TryRemove(sessionId, out _);
	}

	public void Reevaluate()
	{
		foreach (var (sessionId, selectedScreenSaverId) in _selectionBySession)
		{
			Release(sessionId, selectedScreenSaverId);
		}
	}

	private bool Release(string sessionId, string selectedScreenSaverId)
	{
		if (!_screenSavers.TryResolve(selectedScreenSaverId, out _)
			|| !_selectionBySession.TryRemove(sessionId, out _))
		{
			return false;
		}

		_sessions.TryInvalidate(sessionId, UiSessionErrorCodes.ProviderUnavailable, RecoveredMessage, true);
		return true;
	}

	public void Dispose()
	{
		_sessions.Ended -= OnSessionEnded;
	}

	private void OnSessionEnded(object? sender, UiSessionEndedEventArgs e)
	{
		_selectionBySession.TryRemove(e.Session.SessionId, out _);
	}
}

public sealed class ScreenSaverFallbackRecoveryHandler : INotificationHandler<ScreenSaverCatalogChangedNotification>
{
	private readonly ScreenSaverFallbackRecovery _recovery;

	public ScreenSaverFallbackRecoveryHandler(ScreenSaverFallbackRecovery recovery)
	{
		_recovery = recovery;
	}

	public ValueTask Handle(ScreenSaverCatalogChangedNotification notification, CancellationToken cancellationToken)
	{
		_recovery.Reevaluate();
		return ValueTask.CompletedTask;
	}
}
