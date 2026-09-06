using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Templates;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/templates")]
public class TemplatesController : ControllerBase
{
	private readonly IUiTransportMessageHandler<RenderTemplateRequest, RenderTemplateResponse> _renderTemplate;
	private readonly IUiTransportMessageHandler<EvaluateConditionRequest, EvaluateConditionResponse> _evaluateCondition;

	private readonly IUiTransportMessageHandler<EvaluateExpressionRequest, EvaluateExpressionResponse>
		_evaluateExpression;

	public TemplatesController(
		IUiTransportMessageHandler<RenderTemplateRequest, RenderTemplateResponse> renderTemplate,
		IUiTransportMessageHandler<EvaluateConditionRequest, EvaluateConditionResponse> evaluateCondition,
		IUiTransportMessageHandler<EvaluateExpressionRequest, EvaluateExpressionResponse> evaluateExpression)
	{
		_renderTemplate = renderTemplate;
		_evaluateCondition = evaluateCondition;
		_evaluateExpression = evaluateExpression;
	}

	[HttpPost("render")]
	public Task<RenderTemplateResponse> Render(RenderTemplateRequest body, CancellationToken ct)
		=> _renderTemplate.Handle(body, ct).AsTask();

	[HttpPost("evaluate-condition")]
	public Task<EvaluateConditionResponse> EvaluateCondition(EvaluateConditionRequest body, CancellationToken ct)
		=> _evaluateCondition.Handle(body, ct).AsTask();

	[HttpPost("evaluate-expression")]
	public Task<EvaluateExpressionResponse> EvaluateExpression(EvaluateExpressionRequest body, CancellationToken ct)
		=> _evaluateExpression.Handle(body, ct).AsTask();
}
