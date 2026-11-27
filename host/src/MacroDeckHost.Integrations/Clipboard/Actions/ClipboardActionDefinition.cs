using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Clipboard.Native;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Clipboard.Errors;

namespace MacroDeckHost.Integrations.Clipboard.Actions;

internal abstract class ClipboardActionDefinition : IActionDefinition
{
	public const string OutputVariableParameter = "outputVariable";

	protected ClipboardActionDefinition(ClipboardActionServices services)
	{
		Services = services;
	}

	public abstract string Id { get; }

	public abstract LocalizedText Name { get; }

	public abstract LocalizedText Description { get; }

	public abstract IReadOnlyList<ActionParameter> Parameters { get; }

	public abstract ActionRiskCategory RiskCategory { get; }

	protected ClipboardActionServices Services { get; }

	public IActionExecutor CreateExecutor()
	{
		return new Executor(this);
	}

	protected abstract Task<ActionResult> RunAsync(ActionExecutionContext context);

	protected async Task<ActionResult> WriteVariableAsync(
		ActionExecutionContext context,
		string name,
		VariableType type,
		object value)
	{
		var integration = Services.Context();
		var written = integration is not null
			&& await ActionVariableTarget.WriteAsync(integration.UserVariables,
					integration.Variables,
					name,
					UserVariableContext.From(context),
					type,
					value,
					Services.Logger,
					context.CancellationToken)
				.ConfigureAwait(false);
		return written
			? ActionResult.Success()
			: ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.VariableNotWritten(name));
	}

	private async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
	{
		var availability = Services.Backend.Availability;
		if (!availability.IsAvailable)
		{
			return ActionResult.Failed(ActionErrorCodes.Unavailable, ClipboardErrors.Describe(availability));
		}

		try
		{
			return await RunAsync(context).ConfigureAwait(false);
		}
		catch (ClipboardException exception)
		{
			Services.Logger.Warning("Clipboard action {ActionId} failed: {Failure}", Id, exception.Failure);
			return ClipboardErrors.From(exception, Services.Backend.Availability);
		}
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly ClipboardActionDefinition _definition;

		public Executor(ClipboardActionDefinition definition)
		{
			_definition = definition;
		}

		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			return _definition.ExecuteAsync(context);
		}
	}
}
