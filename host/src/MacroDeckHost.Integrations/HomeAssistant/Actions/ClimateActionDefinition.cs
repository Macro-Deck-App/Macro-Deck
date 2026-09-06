using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.HomeAssistant.Actions;

internal sealed class ClimateActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string EntityParameterName = "entity";
	internal const string TemperatureParameterName = "temperature";
	internal const string HvacModeParameterName = "hvacMode";
	internal const string FanModeParameterName = "fanMode";
	internal const string PresetModeParameterName = "presetMode";

	private const string ClimateDomain = "climate";

	private readonly Func<HomeAssistantConnection?> _resolver;

	public ClimateActionDefinition(Func<HomeAssistantConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "climate-set";

	public LocalizedText Name => AppStrings.Integrations.HomeAssistant.Actions.Climate.Name();

	public LocalizedText Description => AppStrings.Integrations.HomeAssistant.Actions.Climate.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Autocomplete(EntityParameterName,
			label: AppStrings.Integrations.HomeAssistant.Actions.Climate.EntityLabel(),
			required: true),
		ActionParameter.Number(TemperatureParameterName,
			label: AppStrings.Integrations.HomeAssistant.Actions.Climate.TemperatureLabel(),
			description: AppStrings.Integrations.HomeAssistant.Actions.Climate.TemperatureDescription(),
			min: -50,
			max: 100,
			step: 0.5),
		ActionParameter.DynamicChoice(HvacModeParameterName,
			label: AppStrings.Integrations.HomeAssistant.Actions.Climate.ModeLabel(),
			description: AppStrings.Integrations.HomeAssistant.Actions.Climate.ModeDescription(),
			placeholder: AppStrings.Integrations.HomeAssistant.Actions.Climate.LeaveUnchanged()),
		ActionParameter.DynamicChoice(FanModeParameterName,
			label: AppStrings.Integrations.HomeAssistant.Actions.Climate.FanModeLabel(),
			placeholder: AppStrings.Integrations.HomeAssistant.Actions.Climate.LeaveUnchanged()),
		ActionParameter.DynamicChoice(PresetModeParameterName,
			label: AppStrings.Integrations.HomeAssistant.Actions.Climate.PresetLabel(),
			placeholder: AppStrings.Integrations.HomeAssistant.Actions.Climate.LeaveUnchanged())
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		var connection = _resolver();
		var entityId = context.CurrentParameters.GetValueOrDefault(EntityParameterName) as string;

		var result = context.ParameterName switch
		{
			HvacModeParameterName => HomeAssistantOptions.AttributeValues(connection,
				context.Filter,
				entityId,
				"hvac_modes"),
			FanModeParameterName => HomeAssistantOptions.AttributeValues(connection,
				context.Filter,
				entityId,
				"fan_modes"),
			PresetModeParameterName => HomeAssistantOptions.AttributeValues(connection,
				context.Filter,
				entityId,
				"preset_modes"),
			_ => HomeAssistantOptions.Entities(connection, context.Filter, ClimateDomain)
		};

		return Task.FromResult(result);
	}

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

			var target = HomeAssistantServiceCall.Target(entityId);
			var calls = new List<(string Service, Dictionary<string, object?> Data)>();

			if (HomeAssistantActionValues.ReadNumber(context.Parameters, TemperatureParameterName) is { } temperature)
			{
				calls.Add(("set_temperature",
					new Dictionary<string, object?>(StringComparer.Ordinal) { ["temperature"] = temperature }));
			}

			AddMode(context, calls, HvacModeParameterName, "set_hvac_mode", "hvac_mode");
			AddMode(context, calls, FanModeParameterName, "set_fan_mode", "fan_mode");
			AddMode(context, calls, PresetModeParameterName, "set_preset_mode", "preset_mode");

			if (calls.Count == 0)
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.HomeAssistant.Errors.NoTemperatureOrMode());
			}

			foreach (var (service, data) in calls)
			{
				var result = await HomeAssistantServiceCall.ExecuteAsync(connection,
					ClimateDomain,
					service,
					target,
					data,
					context.CancellationToken);

				if (result.Status == ActionResultStatus.Failed)
				{
					return result;
				}
			}

			return ActionResult.Success();
		}

		private static void AddMode(
			ActionExecutionContext context,
			List<(string Service, Dictionary<string, object?> Data)> calls,
			string parameterName,
			string service,
			string field)
		{
			if (HomeAssistantActionValues.ReadText(context.Parameters, parameterName) is { } mode)
			{
				calls.Add((service, new Dictionary<string, object?>(StringComparer.Ordinal) { [field] = mode }));
			}
		}
	}
}
