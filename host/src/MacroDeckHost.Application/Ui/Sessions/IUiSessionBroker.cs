using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;

namespace MacroDeckHost.Application.Ui.Sessions;

public sealed record UiSessionOpenTicket
{
	public required bool Accepted { get; init; }

	public required string SessionId { get; init; }

	public string? Code { get; init; }

	public string? Message { get; init; }

	// Completes once the provider has accepted the session and produced its first tree, or the
	// attempt has failed. The session id is usable before this completes - a client may attach to a
	// session whose provider has not answered yet, and is told so by the revision it is handed.
	public required Task<UiSessionOpenTicket> Ready { get; init; }

	public static UiSessionOpenTicket Rejected(string code, string message)
		=> Settled(accepted: false, string.Empty, code, message);

	public static UiSessionOpenTicket Opened(string sessionId)
		=> Settled(accepted: true, sessionId, code: null, message: null);

	// Ready must hand back this very ticket, so it is completed after the record exists rather than
	// built from a copy that is thrown away by the `with` that would follow it.
	private static UiSessionOpenTicket Settled(bool accepted, string sessionId, string? code, string? message)
	{
		var completion = new TaskCompletionSource<UiSessionOpenTicket>();
		var ticket = new UiSessionOpenTicket
		{
			Accepted = accepted,
			SessionId = sessionId,
			Code = code,
			Message = message,
			Ready = completion.Task
		};

		completion.SetResult(ticket);
		return ticket;
	}
}

// No member here awaits a provider, which is what lets every realtime operation satisfy ADR 0062. The only
// exceptions are UiSessionOpenTicket.Ready and FirstTreeAsync, which a caller opts into explicitly and which
// no realtime operation awaits.
public interface IUiSessionBroker : IUiSessionSink
{
	UiSessionOpenTicket Open(string providerId, UiSurface surface, string ownerPrincipal);

	// For a host-side reader no client attaches to. Null when the session ended before its first tree.
	Task<UiRawJson?> FirstTreeAsync(string sessionId, CancellationToken cancellationToken);

	// Raised by the host itself, so unlike SendEvent it needs no attached connection.
	bool DispatchHostEvent(string sessionId, UiSessionEventCommand command);

	Task<UiSessionOpenTicket> OpenAsync(string providerId,
		UiSurface surface,
		string ownerPrincipal,
		CancellationToken cancellationToken);

	Task CloseAsync(string sessionId, string reason, CancellationToken cancellationToken);

	/// <summary>Invalidates every open widget-surface session drawing <paramref name="widgetId" />, so its
	/// clients reopen and are served a tree built from the configuration as it now stands. For a session
	/// that built the configuration into its tree and so cannot re-read it - which is every widget whose
	/// session does not subscribe to the stored-data signal, plugin widgets included.</summary>
	void InvalidateWidgetSessions(Guid widgetId);

	/// <summary>Terminally closes every open session naming <paramref name="widgetId" /> as the widget it
	/// renders or configures - its widget surface (and ghost) and its config surface - because the widget
	/// itself is gone. Unlike <see cref="InvalidateWidgetSessions" />, which asks a client to reopen against
	/// the configuration as it now stands, there is nothing left to reopen against: a deleted widget has no
	/// configuration to rebuild a tree from.</summary>
	void CloseWidgetSessions(Guid widgetId, string reason);

	// Closing on the client's own say-so, so unlike CloseAsync it is bound to the owning principal:
	// a session id alone must not let one device end another device's session. Returns false when
	// there is no such session or it belongs to someone else - the caller cannot tell the two apart,
	// which is what keeps it from being an enumeration oracle.
	bool CloseOwned(string? sessionId, string principal, string reason);

	UiAttachSessionResponse Attach(string? sessionId, string connectionId, string principal);

	void Detach(string? sessionId, string connectionId);

	void DetachConnection(string connectionId);

	/// <summary>
	/// Relays a client event to the session's provider. <paramref name="actingClientId" /> is the client
	/// id the connection registered, which is what reaches the provider as
	/// <see cref="UiSessionEventCommand.ClientId" /> and what an origin-aware session attributes the event
	/// to; it is <c>null</c> when the connection never registered one. It is deliberately not
	/// <paramref name="connectionId" />: consumers address the actor by client id - a connection id names a
	/// socket no client group is keyed by, so passing one silently drops every reply aimed at the origin.
	/// </summary>
	UiSendEventResponse SendEvent(UiSendEventRequest request, string connectionId, string? actingClientId = null);

	void SweepDraining();
}
