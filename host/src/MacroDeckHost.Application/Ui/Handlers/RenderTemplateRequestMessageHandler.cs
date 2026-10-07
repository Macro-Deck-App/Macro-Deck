using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Templates;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Variables.Templates;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class RenderTemplateRequestMessageHandler
	: IUiTransportMessageHandler<RenderTemplateRequest, RenderTemplateResponse>
{
	private readonly IVariableTemplateRenderer _renderer;
	private readonly TemplateVariableSynchronizer _templates;

	public RenderTemplateRequestMessageHandler(IVariableTemplateRenderer renderer, TemplateVariableSynchronizer templates)
	{
		_renderer = renderer;
		_templates = templates;
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
			var response = new RenderTemplateResponse
			{
				Success = true,
				Rendered = rendered ?? string.Empty
			};

			if (VariableDtoMapper.TypeFromWire(request.ResultType) is { } resultType)
			{
				var evaluation = _templates.Preview(request.Template ?? string.Empty,
					resultType,
					request.DecimalPlaces,
					scope,
					request.ScopeRefId,
					Guid.TryParse(request.VariableId, out var variableId) ? variableId : null,
					request.VariableName);
				response.Value = evaluation.Value;
				response.TemplateError = evaluation.Error is { } error
					? new VariableTemplateErrorDto { Code = error.Code, Detail = error.Detail }
					: null;
			}

			return response;
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
