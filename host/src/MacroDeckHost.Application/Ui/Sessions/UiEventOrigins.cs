using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Applications;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Links;

namespace MacroDeckHost.Application.Ui.Sessions;

// What an IUiEventOrigin may do on its client, for in-process providers and the plugin ui/origin.* calls
// alike: always gated on the provider owning the session and the client still being attached to it.
public sealed class UiEventOrigins
{
	private readonly IUiEventOriginTracker _tracker;
	private readonly IUiTransport _transport;
	private readonly IUiInteractionsFactory _interactions;
	private readonly UiClientFeatures _clientFeatures;

	public UiEventOrigins(
		IUiEventOriginTracker tracker,
		IUiTransport transport,
		IUiInteractionsFactory interactions,
		UiClientFeatures clientFeatures)
	{
		_tracker = tracker;
		_transport = transport;
		_interactions = interactions;
		_clientFeatures = clientFeatures;
	}

	public static TimeSpan OpenUrlWindow { get; } = TimeSpan.FromSeconds(10);

	public IUiEventOrigin Create(string providerId, string sessionId, string? clientId, string eventName)
	{
		return new InProcessUiEventOrigin(this, providerId, sessionId, clientId, eventName);
	}

	public bool IsClientAttached(string providerId, string sessionId, string? clientId)
	{
		return !string.IsNullOrEmpty(clientId) && _tracker.IsClientAttached(providerId, sessionId, clientId);
	}

	public async Task<bool> ReportFailureAsync(
		string providerId,
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

	// A link only goes to the connection that raised an event in this session moments ago and said it can
	// open links, and one event opens one link: a plugin must not open pages the user did not ask for.
	public async Task<bool> OpenUrlAsync(
		string providerId,
		string sessionId,
		string? clientId,
		string? url,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(clientId) || !ExternalUrls.TryNormalizeWebUrl(url, out var normalized))
		{
			return false;
		}

		var connectionId = _tracker.TakeRecentEventConnection(providerId, sessionId, clientId, OpenUrlWindow);
		if (connectionId is null || !_clientFeatures.Supports(connectionId, UiClientFeatures.OpenUrl))
		{
			return false;
		}

		await _transport.SendToConnection(connectionId, new UiOpenUrlEvent { Url = normalized }, cancellationToken)
			.ConfigureAwait(false);
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
		{
			return owner.ReportFailureAsync(providerId, sessionId, ClientId, eventName, message, cancellationToken);
		}

		public Task<bool> ShowModalAsync(ModalDefinition modal, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(modal);

			return owner.IsClientAttached(providerId, sessionId, ClientId)
				? owner._interactions.ForIntegration(providerId).ShowModalAsync(ClientId, modal, cancellationToken)
				: Task.FromResult(false);
		}

		public Task<ModalResult<T>> ShowModalAsync<T>(
			ModalDefinition modal,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(modal);

			return owner.IsClientAttached(providerId, sessionId, ClientId)
				? owner._interactions.ForIntegration(providerId).ShowModalAsync<T>(ClientId, modal, cancellationToken)
				: Task.FromResult(ModalResult.FromCancellation<T>());
		}

		public Task<bool> OpenUrlAsync(Uri url, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(url);

			if (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
			{
				throw new ArgumentException("Only absolute http and https URLs can be opened.", nameof(url));
			}

			return owner.OpenUrlAsync(providerId, sessionId, ClientId, url.AbsoluteUri, cancellationToken);
		}
	}
}
