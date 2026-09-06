using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.HomeAssistant.Actions;

internal sealed class LightActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string EntityParameterName = "entity";
	internal const string StateParameterName = "state";
	internal const string BrightnessParameterName = "brightnessPercent";
	internal const string ColorParameterName = "color";
	internal const string ColorTemperatureParameterName = "colorTempKelvin";
	internal const string TransitionParameterName = "transition";

	private const string LightDomain = "light";

	private readonly Func<HomeAssistantConnection?> _resolver;

	public LightActionDefinition(Func<HomeAssistantConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "light-set";

	public LocalizedText Name => AppStrings.Integrations.HomeAssistant.Actions.Light.Name();

	public LocalizedText Description => AppStrings.Integrations.HomeAssistant.Actions.Light.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Autocomplete(EntityParameterName,
			label: AppStrings.Integrations.HomeAssistant.Actions.Light.EntityLabel(),
			required: true),
		ActionParameter.Choice(StateParameterName,
			[
				new ActionParameterOption
				{
					Value = "on", Label = AppStrings.Integrations.HomeAssistant.Actions.Light.StateOn()
				},
				new ActionParameterOption
				{
					Value = "off", Label = AppStrings.Integrations.HomeAssistant.Actions.Light.StateOff()
				},
				new ActionParameterOption
				{
					Value = "toggle", Label = AppStrings.Integrations.HomeAssistant.Actions.Light.StateToggle()
				}
			],
			label: AppStrings.Integrations.HomeAssistant.Actions.Light.StateLabel(),
			defaultValue: "on",
			required: true),
		ActionParameter.Number(BrightnessParameterName,
				label: AppStrings.Integrations.HomeAssistant.Actions.Light.BrightnessLabel(),
				description: AppStrings.Integrations.HomeAssistant.Actions.Light.BrightnessDescription(),
				min: 0,
				max: 100)
			.OnlyWhen(StateParameterName, "on", "toggle"),
		ActionParameter.Color(ColorParameterName,
				label: AppStrings.Integrations.HomeAssistant.Actions.Light.ColorLabel(),
				description: AppStrings.Integrations.HomeAssistant.Actions.Light.ColorDescription())
			.OnlyWhen(StateParameterName, "on", "toggle"),
		ActionParameter.Number(ColorTemperatureParameterName,
				label: AppStrings.Integrations.HomeAssistant.Actions.Light.ColorTemperatureLabel(),
				description: AppStrings.Integrations.HomeAssistant.Actions.Light.ColorTemperatureDescription(),
				min: 2000,
				max: 6500)
			.OnlyWhen(StateParameterName, "on", "toggle"),
		ActionParameter.Number(TransitionParameterName,
			label: AppStrings.Integrations.HomeAssistant.Actions.Light.TransitionLabel(),
			description: AppStrings.Integrations.HomeAssistant.Actions.Light.TransitionDescription(),
			min: 0,
			max: 300)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> Task.FromResult(HomeAssistantOptions.Entities(_resolver(), context.Filter, LightDomain));

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

			var state = HomeAssistantActionValues.ReadText(context.Parameters, StateParameterName) ?? "on";
			var transition = HomeAssistantActionValues.ReadNumber(context.Parameters, TransitionParameterName, 0, 300);

			var data = new Dictionary<string, object?>(StringComparer.Ordinal);
			if (transition is { } seconds)
			{
				data["transition"] = seconds;
			}

			var service = state switch
			{
				"off" => "turn_off",
				"toggle" => "toggle",
				_ => "turn_on"
			};

			if (!string.Equals(state, "off", StringComparison.Ordinal))
			{
				AddValues(context, data);
			}

			return await HomeAssistantServiceCall.ExecuteAsync(connection,
				LightDomain,
				service,
				HomeAssistantServiceCall.Target(entityId),
				data.Count > 0 ? data : null,
				context.CancellationToken);
		}

		private static void AddValues(ActionExecutionContext context, Dictionary<string, object?> data)
		{
			if (HomeAssistantActionValues.ReadNumber(context.Parameters, BrightnessParameterName, 0, 100) is
				{ } brightness)
			{
				data["brightness_pct"] = brightness;
			}

			if (HomeAssistantActionValues.ReadRgb(
					HomeAssistantActionValues.ReadText(context.Parameters, ColorParameterName)) is { } rgb)
			{
				data["rgb_color"] = rgb;
			}

			if (HomeAssistantActionValues.ReadNumber(context.Parameters, ColorTemperatureParameterName, 1000, 10000) is
				{ } kelvin)
			{
				data["color_temp_kelvin"] = (int)kelvin;
			}
		}
	}
}
