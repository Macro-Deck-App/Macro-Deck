using MacroDeck.Sdk.Ui;

namespace MacroDeck.Sdk.Actions;

/// <summary>
/// An <see cref="IActionDefinition" /> that can render its configuration as a Macro Deck UI tree
/// instead of the declared parameter list.
/// </summary>
/// <remarks>
/// <see cref="IActionDefinition.Parameters" /> stays mandatory and unchanged: it is what a client that
/// cannot render a tree falls back to, and what Macro Deck persists into. The tree is a richer way to
/// collect the same values.
/// </remarks>
public interface IUiConfigurableActionDefinition : IActionDefinition
{
	/// <summary>
	/// Creates a configuration session for one configured instance of this action, or returns
	/// <c>null</c> to decline - which is not an error: Macro Deck then renders
	/// <see cref="IActionDefinition.Parameters" />.
	/// </summary>
	/// <remarks>
	/// One session per open configuration surface, so several may be live at once for the same action.
	/// State kept here belongs to the session, never to the definition. The returned session is owned by
	/// the host and is disposed when the session closes.
	/// </remarks>
	Task<IUiSession?> CreateConfigurationSessionAsync(
		ActionConfigurationRequest request,
		CancellationToken cancellationToken);
}
