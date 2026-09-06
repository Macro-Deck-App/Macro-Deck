using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Templates;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class EvaluateConditionRequestMessageHandler
	: IUiTransportMessageHandler<EvaluateConditionRequest, EvaluateConditionResponse>
{
	private readonly IVariableTemplateRenderer _renderer;
	private readonly IActionConditionEvaluator _evaluator;

	public EvaluateConditionRequestMessageHandler(
		IVariableTemplateRenderer renderer,
		IActionConditionEvaluator evaluator)
	{
		_renderer = renderer;
		_evaluator = evaluator;
	}

	public async ValueTask<EvaluateConditionResponse> Handle(
		EvaluateConditionRequest request,
		CancellationToken cancellationToken)
	{
		var scope = VariableDtoMapper.ScopeFromWire(request.Scope) ?? VariableScope.Global;

		try
		{
			var context = await _renderer.CreateContextAsync(scope, request.ScopeRefId);

			var op = request.Operator ?? "==";
			var leftSide = _evaluator.ResolveSide(request.Left.Value, context);

			// Mirrors ActionConditionEvaluator.EvaluateComparison: a state operator has no right operand,
			// so the preview must not resolve one either or the two preview paths would disagree about
			// what the badge shows.
			var rightSide = ActionConditionEvaluator.IsStateOperator(op)
				? new EvaluatedSide(null, string.Empty)
				: _evaluator.ResolveSide(request.Right.Value, context);

			var result = ActionConditionEvaluator.EvaluateOperator(leftSide.Value, op, rightSide.Value);

			return new EvaluateConditionResponse
			{
				Success = true,
				Result = result,
				LeftDisplay = leftSide.Display,
				RightDisplay = rightSide.Display
			};
		}
		catch (Exception ex)
		{
			return new EvaluateConditionResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = "EVALUATION_ERROR",
					Message = AppStrings.Errors.Scripting.EvaluationFailed(details: ex.Message)
				}
			};
		}
	}
}
