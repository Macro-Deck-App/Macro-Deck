using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal sealed class SetInputVolumeActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string InputParameter = "input";
	internal const string ModeParameter = "mode";
	internal const string VolumeParameter = "volume";
	internal const string DecibelsParameter = "decibels";
	internal const string DecibelStepParameter = "decibelStep";

	private const string ModeSet = "set";
	private const string ModeIncrease = "increase";
	private const string ModeDecrease = "decrease";
	private const string ModeSetDecibels = "set-db";
	private const string ModeIncreaseDecibels = "increase-db";
	private const string ModeDecreaseDecibels = "decrease-db";

	private const double DefaultDecibels = 0;
	private const double DefaultDecibelStep = 3;

	private readonly ObsTargetResolver _resolver;

	public SetInputVolumeActionDefinition(Func<ObsConnection?> resolver)
		: this(ObsTargetResolver.Legacy(resolver))
	{
	}

	public SetInputVolumeActionDefinition(ObsTargetResolver resolver)
	{
		_resolver = resolver;
	}

	public string Id => "set-input-volume";

	public LocalizedText Name => AppStrings.Integrations.Obs.Actions.SetInputVolume.Name();

	public LocalizedText Description => AppStrings.Integrations.Obs.Actions.SetInputVolume.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ObsTargetResolver.Parameter(),
		ActionParameter.DynamicChoice(InputParameter,
			label: AppStrings.Integrations.Obs.Params.Input(),
			required: true),
		ActionParameter.Choice(ModeParameter,
			options:
			[
				new ActionParameterOption
					{ Value = ModeSet, Label = AppStrings.Integrations.Obs.Actions.SetInputVolume.ModeSet() },
				new ActionParameterOption
					{ Value = ModeIncrease, Label = AppStrings.Integrations.Obs.Actions.SetInputVolume.ModeIncrease() },
				new ActionParameterOption
					{ Value = ModeDecrease, Label = AppStrings.Integrations.Obs.Actions.SetInputVolume.ModeDecrease() },
				new ActionParameterOption
				{
					Value = ModeSetDecibels,
					Label = AppStrings.Integrations.Obs.Actions.SetInputVolume.ModeSetDecibels()
				},
				new ActionParameterOption
				{
					Value = ModeIncreaseDecibels,
					Label = AppStrings.Integrations.Obs.Actions.SetInputVolume.ModeIncreaseDecibels()
				},
				new ActionParameterOption
				{
					Value = ModeDecreaseDecibels,
					Label = AppStrings.Integrations.Obs.Actions.SetInputVolume.ModeDecreaseDecibels()
				}
			],
			label: AppStrings.Integrations.Obs.Params.Mode(),
			defaultValue: ModeSet),
		ActionParameter.Slider(VolumeParameter,
			min: 0,
			max: 100,
			label: AppStrings.Integrations.Obs.Actions.SetInputVolume.VolumeLabel(),
			description: AppStrings.Integrations.Obs.Actions.SetInputVolume.VolumeDescription(),
			step: 1,
			defaultValue: 100).OnlyWhen(ModeParameter, ModeSet, ModeIncrease, ModeDecrease),
		ActionParameter.Slider(DecibelsParameter,
			min: ObsVolumeScale.MinimumDecibels,
			max: ObsVolumeScale.MaximumDecibels,
			label: AppStrings.Integrations.Obs.Actions.SetInputVolume.DecibelsLabel(),
			description: AppStrings.Integrations.Obs.Actions.SetInputVolume.DecibelsDescription(),
			step: 1,
			defaultValue: DefaultDecibels).OnlyWhen(ModeParameter, ModeSetDecibels),
		ActionParameter.Slider(DecibelStepParameter,
			min: 0,
			max: 30,
			label: AppStrings.Integrations.Obs.Actions.SetInputVolume.DecibelStepLabel(),
			description: AppStrings.Integrations.Obs.Actions.SetInputVolume.DecibelStepDescription(),
			step: 0.5,
			defaultValue: DefaultDecibelStep).OnlyWhen(ModeParameter, ModeIncreaseDecibels, ModeDecreaseDecibels)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public async Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		if (context.ParameterName == ObsTargetResolver.ConfigurationParameter)
		{
			return _resolver.ConfigurationOptions();
		}

		var connection = _resolver.ForOptions(context.CurrentParameters);
		var inputs = connection is null ? [] : await connection.GetInputNamesAsync();

		return new DynamicOptionsResult
		{
			Options = inputs.Select(i => new ActionParameterOption { Value = i, Label = i }).ToList(),
			CacheSeconds = 5
		};
	}

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<SetInputVolumeActionDefinition>(ObsIntegration.IntegrationId);

		private readonly ObsTargetResolver _resolver;

		public Executor(ObsTargetResolver resolver)
		{
			_resolver = resolver;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (!_resolver.TryResolve(context.Parameters, out var connection, out var error))
			{
				return error;
			}

			if (context.Parameters.GetValueOrDefault(InputParameter) is not string input ||
				string.IsNullOrWhiteSpace(input))
			{
				_logger.Warning("OBS volume action skipped: no input selected");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Obs.Errors.NoInputSelected());
			}

			var volume = ReadVolume(context.Parameters.GetValueOrDefault(VolumeParameter));
			var decibels = ReadNumber(context.Parameters.GetValueOrDefault(DecibelsParameter)) ?? DefaultDecibels;
			var decibelStep = ReadNumber(context.Parameters.GetValueOrDefault(DecibelStepParameter)) ??
				DefaultDecibelStep;
			var mode = context.Parameters.GetValueOrDefault(ModeParameter) as string ?? ModeSet;
			var succeeded = mode switch
			{
				ModeIncrease => await connection.AdjustInputVolumePercentAsync(input, volume),
				ModeDecrease => await connection.AdjustInputVolumePercentAsync(input, -volume),
				ModeSetDecibels => await connection.SetInputVolumeDecibelsAsync(input, decibels),
				ModeIncreaseDecibels => await connection.AdjustInputVolumeDecibelsAsync(input, Math.Abs(decibelStep)),
				ModeDecreaseDecibels => await connection.AdjustInputVolumeDecibelsAsync(input, -Math.Abs(decibelStep)),
				_ => await connection.SetInputVolumePercentAsync(input, volume)
			};

			return succeeded
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Obs.Errors.NotConnected());
		}

		private static double ReadVolume(object? raw) => ReadNumber(raw) ?? 0d;

		private static double? ReadNumber(object? raw) => raw switch
		{
			double d when double.IsFinite(d) => d,
			float f when float.IsFinite(f) => f,
			int i => i,
			long l => l,
			string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
				double.IsFinite(parsed) => parsed,
			_ => null
		};
	}
}
