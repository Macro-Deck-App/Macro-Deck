using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.HomeAssistant.Actions;

internal sealed class GetEntityStateActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string EntityParameterName = "entity";
	internal const string AttributeParameterName = "attribute";
	internal const string VariableParameterName = "variable";

	private readonly Func<HomeAssistantConnection?> _resolver;
	private readonly HomeAssistantVariableAccessor _variables;

	public GetEntityStateActionDefinition(
		Func<HomeAssistantConnection?> resolver,
		HomeAssistantVariableAccessor variables)
	{
		_resolver = resolver;
		_variables = variables;
	}

	public string Id => "get-entity-state";

	public LocalizedText Name => AppStrings.Integrations.HomeAssistant.Actions.GetEntityState.Name();

	public LocalizedText Description => AppStrings.Integrations.HomeAssistant.Actions.GetEntityState.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Autocomplete(EntityParameterName,
			label: AppStrings.Integrations.HomeAssistant.Actions.GetEntityState.EntityLabel(),
			required: true),
		ActionParameter.Autocomplete(AttributeParameterName,
			label: AppStrings.Integrations.HomeAssistant.Actions.GetEntityState.AttributeLabel(),
			description: AppStrings.Integrations.HomeAssistant.Actions.GetEntityState.AttributeDescription(),
			placeholder: AppStrings.Integrations.HomeAssistant.Actions.GetEntityState.AttributePlaceholder()),
		ActionParameter.Text(VariableParameterName,
			label: AppStrings.Integrations.HomeAssistant.Actions.GetEntityState.VariableLabel(),
			description: AppStrings.Integrations.HomeAssistant.Actions.GetEntityState.VariableDescription(),
			required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver, _variables);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		var connection = _resolver();

		var result = context.ParameterName == AttributeParameterName
			? HomeAssistantOptions.Attributes(connection,
				context.Filter,
				context.CurrentParameters.GetValueOrDefault(EntityParameterName) as string)
			: HomeAssistantOptions.Entities(connection, context.Filter);

		return Task.FromResult(result);
	}

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<GetEntityStateActionDefinition>(HomeAssistantIntegration.IntegrationId);

		private readonly Func<HomeAssistantConnection?> _resolver;
		private readonly HomeAssistantVariableAccessor _variables;

		public Executor(Func<HomeAssistantConnection?> resolver, HomeAssistantVariableAccessor variables)
		{
			_resolver = resolver;
			_variables = variables;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (!HomeAssistantServiceCall.TryConnect(_resolver, out var connection, out var rejection))
			{
				return rejection;
			}

			if (!HomeAssistantServiceCall.TryEntity(connection,
				HomeAssistantActionValues.ReadText(context.Parameters, EntityParameterName),
				out var entityId,
				out var rejected))
			{
				return rejected;
			}

			if (HomeAssistantActionValues.ReadText(context.Parameters, VariableParameterName) is not { } target)
			{
				_logger.Warning("Home Assistant get-entity-state skipped: no target variable");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.HomeAssistant.Errors.NoTargetVariable());
			}

			var state = connection.Entity(entityId);
			if (state is null)
			{
				return ActionResult.Failed(ActionErrorCodes.NotFound,
					AppStrings.Integrations.HomeAssistant.Errors.EntityUnknown(entityId: entityId));
			}

			var attribute = HomeAssistantActionValues.ReadText(context.Parameters, AttributeParameterName);
			string value;
			if (attribute is null)
			{
				value = state.State;
			}
			else if (state.ReadAttribute(attribute) is { } attributeValue)
			{
				value = attributeValue;
			}
			else
			{
				return ActionResult.Failed(ActionErrorCodes.NotFound,
					AppStrings.Integrations.HomeAssistant.Errors.AttributeUnknown(entityId: entityId,
						attribute: attribute));
			}

			await HomeAssistantVariableWriter.WriteAsync(_variables, target, VariableType.Text, value);

			return ActionResult.Success();
		}
	}
}
