using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UnbindCatalogVariableRequestMessageHandler
	: IUiTransportMessageHandler<UnbindCatalogVariableRequest, UnbindCatalogVariableResponse>
{
	private readonly IVariableBindingService _bindings;

	public UnbindCatalogVariableRequestMessageHandler(IVariableBindingService bindings)
	{
		_bindings = bindings;
	}

	public async ValueTask<UnbindCatalogVariableResponse> Handle(
		UnbindCatalogVariableRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.VariableId, out var id))
		{
			return new UnbindCatalogVariableResponse
			{
				Success = false,
				Error = VariableBindingErrorMapper.ToTransportError(VariableBindingError.NotFound),
			};
		}

		var result = await _bindings.UnbindAsync(id, cancellationToken).ConfigureAwait(false);
		if (!result.Success)
		{
			return new UnbindCatalogVariableResponse
			{
				Success = false,
				Error = VariableBindingErrorMapper.ToTransportError(result.Error!.Value, result.ErrorMessage),
			};
		}

		return new UnbindCatalogVariableResponse { Success = true };
	}
}
