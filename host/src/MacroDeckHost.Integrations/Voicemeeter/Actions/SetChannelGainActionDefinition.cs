using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Voicemeeter.Actions;

internal sealed class SetChannelGainActionDefinition : IDynamicOptionsActionDefinition
{
	internal const double MinimumGain = VoicemeeterVariables.MinimumGain;

	internal const double MaximumGain = VoicemeeterVariables.MaximumGain;

	private const double GainStep = VoicemeeterVariables.GainStep;

	private const string ModeSet = "set";
	private const string ModeIncrease = "increase";
	private const string ModeDecrease = "decrease";

	private readonly VoicemeeterChannelTarget _target;
	private readonly Func<VoicemeeterConnection?> _resolver;

	public SetChannelGainActionDefinition(VoicemeeterChannelTarget target, Func<VoicemeeterConnection?> resolver)
	{
		_target = target;
		_resolver = resolver;

		Parameters =
		[
			target.Picker(),
			ActionParameter.Choice(VoicemeeterActionValues.ModeParameter,
				options:
				[
					new ActionParameterOption
					{
						Value = ModeSet, Label = AppStrings.Integrations.Voicemeeter.Actions.SetGain.ModeSet()
					},
					new ActionParameterOption
					{
						Value = ModeIncrease,
						Label = AppStrings.Integrations.Voicemeeter.Actions.SetGain.ModeIncrease()
					},
					new ActionParameterOption
					{
						Value = ModeDecrease,
						Label = AppStrings.Integrations.Voicemeeter.Actions.SetGain.ModeDecrease()
					}
				],
				label: AppStrings.Integrations.Voicemeeter.Common.ModeLabel(),
				defaultValue: ModeSet),
			ActionParameter.Slider(VoicemeeterActionValues.GainParameter,
				min: MinimumGain,
				max: MaximumGain,
				label: AppStrings.Integrations.Voicemeeter.Common.GainLabel(),
				description: AppStrings.Integrations.Voicemeeter.Actions.SetGain.GainDescription(),
				step: GainStep,
				defaultValue: 0d),
			ActionParameter.Duration(VoicemeeterActionValues.FadeParameter,
				label: AppStrings.Integrations.Voicemeeter.Actions.SetGain.FadeLabel(),
				description: AppStrings.Integrations.Voicemeeter.Actions.SetGain.FadeDescription(),
				min: 0,
				max: 60_000,
				defaultMilliseconds: 0)
		];
	}

	public string Id => $"set-{_target.IdPrefix}-gain";

	public LocalizedText Name => _target.Kind == VoicemeeterChannelKind.Strip
		? AppStrings.Integrations.Voicemeeter.Actions.SetGain.StripName()
		: AppStrings.Integrations.Voicemeeter.Actions.SetGain.BusName();

	public LocalizedText Description => _target.Kind == VoicemeeterChannelKind.Strip
		? AppStrings.Integrations.Voicemeeter.Actions.SetGain.StripDescription()
		: AppStrings.Integrations.Voicemeeter.Actions.SetGain.BusDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; }

	public IActionExecutor CreateExecutor() => new Executor(_target, _resolver);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		var catalog = _resolver()?.Catalog ?? VoicemeeterChannelCatalog.Unknown;
		return Task.FromResult(VoicemeeterOptions.Channels(catalog, _target.Kind));
	}

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<SetChannelGainActionDefinition>(VoicemeeterIntegration.IntegrationId);

		private readonly VoicemeeterChannelTarget _target;
		private readonly Func<VoicemeeterConnection?> _resolver;

		public Executor(VoicemeeterChannelTarget target, Func<VoicemeeterConnection?> resolver)
		{
			_target = target;
			_resolver = resolver;
		}

		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			var index = VoicemeeterActionValues.ReadChannelIndex(context.Parameters, _target.ParameterName);
			if (connection is null)
			{
				_logger.Warning("Voicemeeter gain action skipped: Voicemeeter is not running");
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Voicemeeter.Errors.NotRunning()));
			}

			if (index is null)
			{
				_logger.Warning("Voicemeeter gain action skipped: no {Target} selected", _target.Title);
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Voicemeeter.Errors.NoTargetSelected(target: _target.NounText)));
			}

			var amount
				= VoicemeeterActionValues.ReadNumber(context.Parameters, VoicemeeterActionValues.GainParameter) ?? 0d;
			var mode = context.Parameters.GetValueOrDefault(VoicemeeterActionValues.ModeParameter) as string ?? ModeSet;
			var gainParameter = _target.Parameter(index.Value, VoicemeeterParameters.Gain);

			double target;
			switch (mode)
			{
				case ModeIncrease:
				case ModeDecrease:
					if (connection.GetParameter(gainParameter) is not { } current)
					{
						_logger.Warning("Voicemeeter gain action skipped: {Parameter} is unavailable", gainParameter);
						return Task.FromResult(ActionResult.Failed(ActionErrorCodes.ProviderError,
							AppStrings.Integrations.Voicemeeter.Errors.CurrentLevelNotReturned()));
					}

					target = current + (mode == ModeDecrease ? -amount : amount);
					break;
				default:
					target = amount;
					break;
			}

			target = Math.Clamp(target, MinimumGain, MaximumGain);

			var fade = (int)(VoicemeeterActionValues.ReadNumber(context.Parameters,
					VoicemeeterActionValues.FadeParameter) ??
				0d);
			if (fade > 0)
			{
				connection.SetParameter(_target.Parameter(index.Value, VoicemeeterParameters.FadeTo),
					VoicemeeterParameters.FadeArgument(target, fade));
			}
			else
			{
				connection.SetParameter(gainParameter, (float)target);
			}

			return ActionResult.SucceededTask;
		}
	}
}
