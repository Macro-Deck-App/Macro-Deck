using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.StreamlabsDesktop.Actions;

internal sealed class SetAudioVolumeActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string VolumeParameter = "volume";
	internal const string ModeSet = "set";
	internal const string ModeIncrease = "increase";
	internal const string ModeDecrease = "decrease";

	private readonly Func<StreamlabsDesktopConnection?> _resolver;

	public SetAudioVolumeActionDefinition(Func<StreamlabsDesktopConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "set-audio-volume";

	public LocalizedText Name => AppStrings.Integrations.StreamlabsDesktop.Actions.SetAudioVolumeName();

	public LocalizedText Description =>
		AppStrings.Integrations.StreamlabsDesktop.Actions.SetAudioVolumeDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice(StreamlabsActionValues.SourceParameter,
			label: AppStrings.Integrations.StreamlabsDesktop.Params.AudioSourceLabel(),
			required: true),
		ActionParameter.Choice(StreamlabsActionValues.ModeParameter,
			options:
			[
				new ActionParameterOption
				{
					Value = ModeSet, Label = AppStrings.Integrations.StreamlabsDesktop.Params.SetToLabel()
				},
				new ActionParameterOption
				{
					Value = ModeIncrease, Label = AppStrings.Integrations.StreamlabsDesktop.Params.IncreaseByLabel()
				},
				new ActionParameterOption
				{
					Value = ModeDecrease, Label = AppStrings.Integrations.StreamlabsDesktop.Params.DecreaseByLabel()
				}
			],
			label: AppStrings.Integrations.StreamlabsDesktop.Params.ModeLabel(),
			defaultValue: ModeSet),
		ActionParameter.Slider(VolumeParameter,
			min: 0,
			max: 100,
			label: AppStrings.Integrations.StreamlabsDesktop.Params.VolumePercentLabel(),
			description: AppStrings.Integrations.StreamlabsDesktop.Params.VolumeFaderDescription(),
			step: 1,
			defaultValue: 100)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public async Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		if (_resolver() is not { } connection || context.ParameterName != StreamlabsActionValues.SourceParameter)
		{
			return StreamlabsActionValues.Options([]);
		}

		return StreamlabsActionValues.Options(await connection.GetAudioSourceNamesAsync().ConfigureAwait(false));
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<StreamlabsDesktopConnection?> _resolver;

		public Executor(Func<StreamlabsDesktopConnection?> resolver)
		{
			_resolver = resolver;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (_resolver() is not { } connection)
			{
				return StreamlabsActionResults.NotConnected;
			}

			if (StreamlabsActionValues.ReadText(context, StreamlabsActionValues.SourceParameter) is not { } source)
			{
				return StreamlabsActionResults.MissingParameter(AppStrings.Integrations.StreamlabsDesktop.Errors
					.NoAudioSourceSelected());
			}

			var volume = StreamlabsActionValues.ReadNumber(context.Parameters.GetValueOrDefault(VolumeParameter));
			if (volume is not { } percent)
			{
				return StreamlabsActionResults.MissingParameter(AppStrings.Integrations.StreamlabsDesktop.Errors
					.VolumeNotANumber());
			}

			var result = StreamlabsActionValues.ReadMode(context, ModeSet) switch
			{
				ModeIncrease => await connection.AdjustAudioVolumePercentAsync(source, percent)
					.ConfigureAwait(false),
				ModeDecrease => await connection.AdjustAudioVolumePercentAsync(source, -percent)
					.ConfigureAwait(false),
				_ => await connection.SetAudioVolumePercentAsync(source, percent).ConfigureAwait(false)
			};

			return result.ToActionResult();
		}
	}
}
