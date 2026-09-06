using MacroDeckHost.Integrations.System.Power;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.System.Actions;

internal abstract class PowerActionDefinitionBase : IActionDefinition
{
	private readonly IPowerService _power;
	private readonly PowerOperation _operation;

	protected PowerActionDefinitionBase(IPowerService power, PowerOperation operation)
	{
		_power = power;
		_operation = operation;
	}

	public abstract string Id { get; }
	public abstract LocalizedText Name { get; }
	public abstract LocalizedText Description { get; }

	public virtual IReadOnlyList<ActionParameter> Parameters { get; } = [];

	public virtual MacroDeckPlatform Platforms => MacroDeckPlatform.All;

	public IActionExecutor CreateExecutor() => new Executor(_power, _operation);

	private sealed class Executor : IActionExecutor
	{
		private readonly IPowerService _power;
		private readonly PowerOperation _operation;

		public Executor(IPowerService power, PowerOperation operation)
		{
			_power = power;
			_operation = operation;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var force = SystemActionValues.ReadBool(context.Parameters, "force", false);
			var result = await _power.ExecuteAsync(_operation, force, context.CancellationToken);

			return result.Success
				? ActionResult.Success()
				: ActionResult.Failed(result.ErrorCode ?? ActionErrorCodes.ProviderError,
					result.FailureReason ?? AppStrings.Integrations.System.Errors.Power.OperationFailed());
		}
	}
}
