using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Application.Ui.Handlers;

internal static class VariableBindingErrorMapper
{
	// No resource carries a localized message per VariableBindingError member yet, so - the same
	// way VariableDtoMapper.ToError already does for VariableError - the wire message is the service's
	// plain diagnostic text, and Code is what a client actually localizes for display.
	public static TransportError ToTransportError(VariableBindingError error, string? message = null)
		=> new() { Code = error.ToString(), Message = message ?? string.Empty };
}
