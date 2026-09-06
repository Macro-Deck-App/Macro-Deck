using MacroDeck.Sdk.Ui;

namespace MacroDeck.Sdk.ConfigFlow;

/// <summary>An <see cref="IConfigFlow" /> that can also render its current step as a Macro Deck UI tree.</summary>
/// <remarks>
/// <para>
/// The tree renders a configuration transaction it does not own. It never completes the flow and never
/// persists anything: <see cref="IConfigFlow.SubmitAsync" /> stays the only way values are accepted,
/// which is what keeps secret encryption, OAuth and configuration replacement working. A value the
/// tree collects must therefore be visible to <see cref="IConfigFlow.SubmitAsync" />, and a step
/// transition the flow makes must appear as a patch - which is why this is implemented on the flow
/// object itself rather than beside it.
/// </para>
/// <para>
/// Because Macro Deck relays tree payloads without inspecting them, it cannot tell which of a tree's
/// values are sensitive. A flow serving a tree is responsible for classifying its own secrets when it
/// completes.
/// </para>
/// </remarks>
public interface IUiConfigFlow : IConfigFlow
{
	/// <summary>
	/// Creates the UI session for this flow instance, or returns <c>null</c> to decline - which is not
	/// an error: Macro Deck then serves this flow's declared field list instead.
	/// </summary>
	/// <remarks>
	/// Called at most once per flow instance. The returned session is owned by the host for the
	/// session's lifetime and is disposed when the session closes, including after a fault.
	/// </remarks>
	Task<IUiSession?> CreateUiSessionAsync(UiSessionRequest request, CancellationToken cancellationToken);
}
