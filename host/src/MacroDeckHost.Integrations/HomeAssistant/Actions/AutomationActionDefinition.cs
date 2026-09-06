using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.HomeAssistant.Actions;

internal sealed class AutomationActionDefinition : IDynamicOptionsActionDefinition, IStateProviderActionDefinition
{
	internal const string EntityParameterName = "entity";
	internal const string CommandParameterName = "command";
	internal const string SkipConditionParameterName = "skipCondition";

	private const string AutomationDomain = "automation";

	private readonly Func<HomeAssistantConnection?> _resolver;

	public AutomationActionDefinition(Func<HomeAssistantConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "automation-control";

	public LocalizedText Name => AppStrings.Integrations.HomeAssistant.Actions.Automation.Name();

	public LocalizedText Description => AppStrings.Integrations.HomeAssistant.Actions.Automation.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Autocomplete(EntityParameterName,
			label: AppStrings.Integrations.HomeAssistant.Actions.Automation.EntityLabel(),
			required: true),
		ActionParameter.Choice(CommandParameterName,
			[
				new ActionParameterOption
				{
					Value = "trigger", Label = AppStrings.Integrations.HomeAssistant.Actions.Automation.CommandTrigger()
				},
				new ActionParameterOption
				{
					Value = "turn_on", Label = AppStrings.Integrations.HomeAssistant.Actions.Automation.CommandEnable()
				},
				new ActionParameterOption
				{
					Value = "turn_off",
					Label = AppStrings.Integrations.HomeAssistant.Actions.Automation.CommandDisable()
				},
				new ActionParameterOption
				{
					Value = "toggle", Label = AppStrings.Integrations.HomeAssistant.Actions.Automation.CommandToggle()
				}
			],
			label: AppStrings.Integrations.HomeAssistant.Actions.Automation.CommandLabel(),
			defaultValue: "trigger",
			required: true),
		ActionParameter.Toggle(SkipConditionParameterName,
				label: AppStrings.Integrations.HomeAssistant.Actions.Automation.SkipConditionLabel(),
				description: AppStrings.Integrations.HomeAssistant.Actions.Automation.SkipConditionDescription(),
				defaultValue: true)
			.OnlyWhen(CommandParameterName, "trigger")
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	// An automation's state is whether it is enabled, which is what turn_on/turn_off change; the
	// "trigger" command runs it without changing that, so the button still follows the enabled flag.
	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		if (parameters.GetValueOrDefault(EntityParameterName)?.ToString() is not { Length: > 0 } entity)
		{
			return Task.FromResult<ActionStateSnapshot?>(null);
		}

		return Task.FromResult(HomeAssistantActionStates.Snapshot(_resolver(), [entity]));
	}

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> Task.FromResult(HomeAssistantOptions.Entities(_resolver(), context.Filter, AutomationDomain));

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<HomeAssistantConnection?> _resolver;

		public Executor(Func<HomeAssistantConnection?> resolver)
		{
			_resolver = resolver;
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

			var command = HomeAssistantActionValues.ReadText(context.Parameters, CommandParameterName) ?? "trigger";
			var data = command switch
			{
				"trigger" => new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["skip_condition"] = HomeAssistantActionValues.ReadBool(context.Parameters,
						SkipConditionParameterName,
						fallback: true)
				},
				_ => null
			};

			var service = command switch
			{
				"turn_on" or "turn_off" or "toggle" => command,
				_ => "trigger"
			};

			return await HomeAssistantServiceCall.ExecuteAsync(connection,
				AutomationDomain,
				service,
				HomeAssistantServiceCall.Target(entityId),
				data,
				context.CancellationToken);
		}
	}
}
