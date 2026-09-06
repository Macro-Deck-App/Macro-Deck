namespace MacroDeck.Sdk.Ui;

/// <summary>
/// Opens a Macro Deck UI modal on the client that triggered a running action, so an action can ask a
/// question instead of being limited to its stored configuration. Reached through
/// <c>ActionExecutionContext.Ui</c> and populated by the host per execution.
/// </summary>
/// <remarks>
/// <para>
/// <b>What bounds the wait.</b> A flow that has not finished within a few seconds detaches and keeps
/// running, so awaiting a person is viable - but not indefinitely. The wait ends when the user answers, when
/// the passed <see cref="CancellationToken" /> fires, or when the flow reaches the host's maximum run
/// duration, whichever comes first; every one of those but an answer is a cancellation. Do not hold a modal
/// open as a substitute for a widget: a running flow occupies one of the host's concurrent run slots for as
/// long as its modal is open.
/// </para>
/// <para>
/// <b>The tree is the provider's.</b> These calls say which dialog to open, not what it looks like. Macro
/// Deck opens a <c>dialog</c> surface against the integration's own <see cref="IUiProvider" />, which builds
/// and updates the tree exactly as it would for a widget.
/// </para>
/// </remarks>
public interface IUiInteractions
{
	/// <summary>
	/// Opens a modal and returns as soon as it has been opened, without waiting for the user - the right
	/// call for a dialog that shows something rather than asking something.
	/// </summary>
	/// <param name="originClientId">The client that triggered the action; the modal is shown there. A null
	/// or empty id means there is no client to show it on and the call does nothing.</param>
	/// <returns>Whether a modal was opened.</returns>
	Task<bool> ShowModalAsync(
		string? originClientId,
		ModalDefinition modal,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Opens a modal and waits for the user to complete or cancel it.
	/// </summary>
	/// <param name="originClientId">The client that triggered the action; the modal is shown there. A null
	/// or empty id means there is no client to ask, and the result is a cancellation.</param>
	/// <returns>The user's answer, or a cancellation - see <see cref="ModalResult{T}" /> for everything
	/// that counts as one.</returns>
	Task<ModalResult<T>> ShowModalAsync<T>(
		string? originClientId,
		ModalDefinition modal,
		CancellationToken cancellationToken = default);
}
