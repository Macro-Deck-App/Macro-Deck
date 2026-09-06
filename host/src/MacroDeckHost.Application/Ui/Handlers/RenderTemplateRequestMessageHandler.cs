using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Templates;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class RenderTemplateRequestMessageHandler
	: IUiTransportMessageHandler<RenderTemplateRequest, RenderTemplateResponse>
{
	private readonly IVariableTemplateRenderer _renderer;

	public RenderTemplateRequestMessageHandler(IVariableTemplateRenderer renderer)
	{
		_renderer = renderer;
	}

	public async ValueTask<RenderTemplateResponse> Handle(
		RenderTemplateRequest request,
		CancellationToken cancellationToken)
	{
		var scope = VariableDtoMapper.ScopeFromWire(request.Scope) ?? VariableScope.Global;

		try
		{
			var context = await _renderer.CreateContextAsync(scope, request.ScopeRefId);
			var rendered = _renderer.Render(request.Template ?? string.Empty, context);
			return new RenderTemplateResponse
			{
				Success = true,
				Rendered = rendered ?? string.Empty
			};
		}
		catch (Exception ex)
		{
			return new RenderTemplateResponse
			{
				Success = false,
				Rendered = string.Empty,
				Error = new TransportError
				{
					Code = "TEMPLATE_ERROR",
					Message = AppStrings.Errors.Scripting.TemplateRenderFailed(details: ex.Message)
				}
			};
		}
	}
}
