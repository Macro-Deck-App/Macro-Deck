using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Ui.Transport;

namespace MacroDeckHost.Application.Ui.Sessions;

// What an IUiEventOrigin may do on its client, for in-process providers and the plugin ui/origin.* calls
// alike: always gated on the provider owning the session and the client still being attached to it.
public sealed class UiEventOrigins
{
	private readonly IUiEventOriginTracker _tracker;
	private readonly IUiTransport _transport;
	private readonly IUiInteractionsFactory _interactions;

	public UiEventOrigins(IUiEventOriginTracker tracker, IUiTransport transport, IUiInteractionsFactory interactions)
	{
		_tracker = tracker;
		_transport = transport;
		_interactions = interactions;
	}

	public IUiEventOrigin Create(string providerId, string sessionId, string? clientId, string eventName)
		=> new InProcessUiEventOrigin(this, providerId, sessionId, clientId, eventName);

	public bool IsClientAttached(string providerId, string sessionId, string? clientId)
		=> !string.IsNullOrEmpty(clientId) && _tracker.IsClientAttached(providerId, sessionId, clientId);

	public async Task<bool> ReportFailureAsync(string providerId,
		string sessionId,
		string? clientId,
		string? eventName,
		LocalizedText message,
		CancellationToken cancellationToken)
	{
		if (!IsClientAttached(providerId, sessionId, clientId))
		{
			return false;
		}

		var evt = UiEventFeedback.Failed(ActionExecutionErrorCodes.ActionFailed,
			message,
			_tracker.SurfaceOf(sessionId),
			eventName);

		await _transport.SendToGroup(UiClientGroups.For(clientId!), evt, cancellationToken).ConfigureAwait(false);
		return true;
	}

	private sealed class InProcessUiEventOrigin(
		UiEventOrigins owner,
		string providerId,
		string sessionId,
		string? clientId,
		string eventName) : IUiEventOrigin
	{
		public string? ClientId { get; } = string.IsNullOrEmpty(clientId) ? null : clientId;

		public Task<bool> ReportFailureAsync(LocalizedText message, CancellationToken cancellationToken = default)
			=> owner.ReportFailureAsync(providerId, sessionId, ClientId, eventName, message, cancellationToken);

		public Task<bool> ShowModalAsync(ModalDefinition modal, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(modal);

			return owner.IsClientAttached(providerId, sessionId, ClientId)
				? owner._interactions.ForIntegration(providerId).ShowModalAsync(ClientId, modal, cancellationToken)
				: Task.FromResult(false);
		}

		public Task<ModalResult<T>> ShowModalAsync<T>(ModalDefinition modal,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(modal);

			return owner.IsClientAttached(providerId, sessionId, ClientId)
				? owner._interactions.ForIntegration(providerId).ShowModalAsync<T>(ClientId, modal, cancellationToken)
				: Task.FromResult(ModalResult.FromCancellation<T>());
		}
	}
}
