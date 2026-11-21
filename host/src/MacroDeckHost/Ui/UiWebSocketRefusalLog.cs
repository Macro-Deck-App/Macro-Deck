using System.Collections.Concurrent;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Ui;

// The UI socket is anonymous and on the public port, so a refusal must never cost more than one log
// line per kind and interval: otherwise any peer could flood out the logs users are asked to attach.
public sealed class UiWebSocketRefusalLog(ILogger logger, TimeProvider timeProvider)
{
	private static readonly TimeSpan _interval = TimeSpan.FromMinutes(1);
	private readonly ConcurrentDictionary<UiWebSocketTicketRefusal, DateTimeOffset> _lastLogged = new();

	public void TicketRefused(int port, UiWebSocketTicketRefusal refusal)
	{
		if (refusal is UiWebSocketTicketRefusal.Malformed or UiWebSocketTicketRefusal.None)
		{
			logger.Debug("Refused a UI WebSocket upgrade on port {Port}: the ticket was refused ({Refusal})",
				port,
				refusal);
			return;
		}

		var now = timeProvider.GetUtcNow();
		var logged = false;
		_lastLogged.AddOrUpdate(refusal,
			_ =>
			{
				logged = true;
				return now;
			},
			(_, last) =>
			{
				if (now - last < _interval)
				{
					logged = false;
					return last;
				}

				logged = true;
				return now;
			});
		if (logged)
		{
			logger.Warning("Refused a UI WebSocket upgrade on port {Port}: the ticket was refused ({Refusal})",
				port,
				refusal);
		}
	}
}
