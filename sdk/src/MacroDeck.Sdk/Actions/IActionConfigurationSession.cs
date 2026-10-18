using MacroDeck.Sdk.Ui;

namespace MacroDeck.Sdk.Actions;

/// <summary>
/// An action configuration session that can report values for the action's declared parameters, so a
/// tree that edits nested or host-computed state still persists through
/// <see cref="IActionDefinition.Parameters" />.
/// </summary>
/// <remarks>
/// Return one from <see cref="IUiConfigurableActionDefinition.CreateConfigurationSessionAsync" />, usually
/// by wrapping the session in an <see cref="ActionConfigurationSession" />. A reported value is treated
/// exactly like a user edit of that declared parameter: it is unsaved until the user saves the action.
/// Only names of declared parameters that are not <see cref="ActionParameterType.Secret" /> or
/// <see cref="ActionParameterType.Password" /> are applied, and a value whose shape does not match the
/// parameter type is dropped. A save waits only for reports raised synchronously while
/// <see cref="IUiSession.Dispatch(MacroDeck.Ui.Model.Events.UiEvent, IUiEventOrigin)" /> runs; a report
/// raised after awaited work may arrive after the user saved. Hosts and clients that predate this
/// interface ignore reports, and the session keeps working as a plain <see cref="IUiSession" />.
/// </remarks>
public interface IActionConfigurationSession : IUiSession
{
	/// <summary>
	/// Raised when the session reports parameter values. Raised synchronously by the session; the host
	/// relays reports in the order they were raised.
	/// </summary>
	event EventHandler<ActionParameterValuesReportedEventArgs>? ParameterValuesReported;
}
