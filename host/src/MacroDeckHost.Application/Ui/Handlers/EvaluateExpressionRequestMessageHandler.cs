using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Templates;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class EvaluateExpressionRequestMessageHandler
	: IUiTransportMessageHandler<EvaluateExpressionRequest, EvaluateExpressionResponse>
{
	private readonly IVariableTemplateRenderer _renderer;
	private readonly IActionConditionEvaluator _evaluator;
	private readonly EventPreviewSamples _eventSamples;

	public EvaluateExpressionRequestMessageHandler(
		IVariableTemplateRenderer renderer,
		IActionConditionEvaluator evaluator,
		EventPreviewSamples eventSamples)
	{
		_renderer = renderer;
		_evaluator = evaluator;
		_eventSamples = eventSamples;
	}

	public async ValueTask<EvaluateExpressionResponse> Handle(
		EvaluateExpressionRequest request,
		CancellationToken cancellationToken)
	{
		var scope = VariableDtoMapper.ScopeFromWire(request.Scope) ?? VariableScope.Global;
		var leaves = new Dictionary<string, ComparisonLeafEvaluation>();

		try
		{
			var context = _eventSamples.Overlay(await _renderer.CreateContextAsync(scope, request.ScopeRefId),
				request.EventId);
			var result = _evaluator.EvaluateExpression(request.Expression,
				context,
				leaf =>
					leaves[leaf.Id] = new ComparisonLeafEvaluation
					{
						Result = leaf.Result,
						LeftDisplay = leaf.LeftDisplay,
						RightDisplay = leaf.RightDisplay,
						Error = leaf.Error
					});

			return new EvaluateExpressionResponse
			{
				Success = true,
				Result = result,
				Leaves = leaves
			};
		}
		catch (Exception ex)
		{
			return new EvaluateExpressionResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = "EVALUATION_ERROR",
					Message = AppStrings.Errors.Scripting.EvaluationFailed(details: ex.Message)
				},
				Leaves = leaves
			};
		}
	}
}
