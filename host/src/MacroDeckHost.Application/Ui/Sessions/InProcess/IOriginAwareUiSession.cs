using MacroDeck.Ui.Model.Events;

namespace MacroDeckHost.Application.Ui.Sessions.InProcess;

/// <summary>
/// Host-internal extension an in-process <see cref="MacroDeck.Sdk.Ui.IUiSession" /> may implement to
/// learn which attached client raised an event on a <b>shared</b> session - the acting client id a
/// dispatched event already carries upstream, which <see cref="InProcessUiSessionProvider" /> otherwise
/// has nowhere to deliver.
///
/// <para>
/// Deliberately not on the public <see cref="MacroDeck.Sdk.Ui.IUiSession" /> contract: origin identity is
/// host plumbing a plugin session never needs and must never be asked to implement, and
/// <see cref="MacroDeck.Sdk.Ui.IUiSession.Dispatch" /> is public SDK surface this migration does not
/// touch. A session that does not implement this interface is dispatched through
/// <see cref="MacroDeck.Sdk.Ui.IUiSession.Dispatch(UiEvent)" /> exactly as before.
/// </para>
/// </summary>
public interface IOriginAwareUiSession
{
	/// <summary>Delivers a client event exactly like <see cref="MacroDeck.Sdk.Ui.IUiSession.Dispatch(UiEvent)" />,
	/// additionally carrying which attached client raised it. <paramref name="originClientId" /> is
	/// <c>null</c> when the caller does not know it - a session must treat that exactly as it would an
	/// unknown origin from any other source.</summary>
	void Dispatch(UiEvent uiEvent, string? originClientId);
}
