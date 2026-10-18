using MacroDeck.Localization;

namespace MacroDeck.Sdk.Ui;

/// <summary>
/// The client a UI event came from, and what a session may do on that client in answer to it: show that
/// what the user asked for failed, or open one of the provider's own dialogs there. Handed to
/// <see cref="IUiSession.Dispatch(MacroDeck.Ui.Model.Events.UiEvent, IUiEventOrigin)" /> with every event.
/// </summary>
/// <remarks>
/// <para>
/// An origin stays usable after the event handler returned, so a session may keep it for work that
/// finishes later, such as a request to a device. It only acts while the client is still attached to the
/// session: once the client closed the view or disconnected, the calls do nothing and answer
/// <c>false</c> or a cancellation. They never throw for that reason.
/// </para>
/// <para>
/// A dialog opens against the provider's own <see cref="IUiProvider" /> as a <c>dialog</c> surface, as
/// <see cref="IUiInteractions" /> does for an action. A Macro Deck that predates origins hands every event
/// an origin without a client, and a provider talking to such a Macro Deck sees every call answer
/// <c>false</c> or a cancellation.
/// </para>
/// </remarks>
public interface IUiEventOrigin
{
	/// <summary>The id of the client that raised the event, or <c>null</c> when it is not known, for example
	/// for an event Macro Deck raised itself.</summary>
	string? ClientId { get; }

	/// <summary>
	/// Shows <paramref name="message" /> on the client that raised the event the way Macro Deck shows a
	/// failed action, for example as a toast. Use it when a press could not be carried out.
	/// </summary>
	/// <returns>Whether the failure was shown: <c>false</c> when there is no client or it is no longer
	/// attached to the session.</returns>
	Task<bool> ReportFailureAsync(LocalizedText message, CancellationToken cancellationToken = default);

	/// <summary>Opens one of the provider's dialogs on the client that raised the event and returns as
	/// soon as it is open.</summary>
	/// <returns>Whether a dialog was opened.</returns>
	Task<bool> ShowModalAsync(ModalDefinition modal, CancellationToken cancellationToken = default);

	/// <summary>Opens one of the provider's dialogs on the client that raised the event and waits for the
	/// user to complete or cancel it.</summary>
	/// <returns>The user's answer, or a cancellation when the dialog could not be opened, was dismissed, or
	/// <paramref name="cancellationToken" /> fired.</returns>
	Task<ModalResult<T>> ShowModalAsync<T>(ModalDefinition modal, CancellationToken cancellationToken = default);

	/// <summary>
	/// Opens <paramref name="url" /> in the browser of the device that raised the event, for example the phone
	/// the user tapped a widget on, never on the machine Macro Deck runs on.
	/// </summary>
	/// <remarks>
	/// Macro Deck only delivers it to a client that said it can open links, only to the connection the event
	/// came from, only for a short time after that event and only once per event, so keep it for answering a
	/// press rather than for later work. A Macro Deck or client that predates this answers <c>false</c>; fall
	/// back to showing the link, for example in a dialog.
	/// </remarks>
	/// <returns>Whether the link was handed to the client. <c>true</c> does not prove a browser opened it, for
	/// example when the browser blocked the window.</returns>
	/// <exception cref="ArgumentException"><paramref name="url" /> is not an absolute <c>http</c> or
	/// <c>https</c> URL.</exception>
	Task<bool> OpenUrlAsync(Uri url, CancellationToken cancellationToken = default)
	{
		UiEventOriginUrls.EnsureWebUrl(url);
		return Task.FromResult(false);
	}
}
