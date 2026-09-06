using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Application.Ui.Handlers;

public class SanitizeVariableNameRequestMessageHandler
	: IUiTransportMessageHandler<SanitizeVariableNameRequest, SanitizeVariableNameResponse>
{
	public ValueTask<SanitizeVariableNameResponse> Handle(
		SanitizeVariableNameRequest request,
		CancellationToken cancellationToken)
	{
		var sanitized = VariableNameSanitizer.Sanitize(request.Input);
		return ValueTask.FromResult(new SanitizeVariableNameResponse
		{
			Sanitized = sanitized,
			IsValid = VariableNameSanitizer.IsValid(sanitized)
		});
	}
}
