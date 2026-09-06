using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.StreamlabsDesktop.Actions;

internal sealed class GetAudioStateActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string StateParameter = "state";
	internal const string StateVolume = "volume";
	internal const string StateMuted = "muted";

	private readonly Func<StreamlabsDesktopConnection?> _resolver;
	private readonly VariableApiAccessor _variables;

	public GetAudioStateActionDefinition(
		Func<StreamlabsDesktopConnection?> resolver,
		VariableApiAccessor variables)
	{
		_resolver = resolver;
		_variables = variables;
	}

	public string Id => "get-audio-state";

	public LocalizedText Name => AppStrings.Integrations.StreamlabsDesktop.Actions.GetAudioStateName();

	public LocalizedText Description => AppStrings.Integrations.StreamlabsDesktop.Actions.GetAudioStateDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice(StreamlabsActionValues.SourceParameter,
			label: AppStrings.Integrations.StreamlabsDesktop.Params.AudioSourceLabel(),
			required: true),
		ActionParameter.Choice(StateParameter,
			options:
			[
				new ActionParameterOption
				{
					Value = StateVolume, Label = AppStrings.Integrations.StreamlabsDesktop.Params.VolumePercentLabel()
				},
				new ActionParameterOption
				{
					Value = StateMuted, Label = AppStrings.Integrations.StreamlabsDesktop.Params.MutedLabel()
				}
			],
			label: AppStrings.Integrations.StreamlabsDesktop.Actions.ReadLabel(),
			defaultValue: StateVolume),
		ActionParameter.Text(StreamlabsActionValues.VariableParameter,
			label: AppStrings.Integrations.StreamlabsDesktop.Params.SaveToVariableLabel(),
			description: AppStrings.Integrations.StreamlabsDesktop.Params.SaveToVariableDescription(),
			required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver, _variables);

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
		private readonly VariableApiAccessor _variables;

		public Executor(Func<StreamlabsDesktopConnection?> resolver, VariableApiAccessor variables)
		{
			_resolver = resolver;
			_variables = variables;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (_resolver() is not { } connection)
			{
				return StreamlabsActionResults.NotConnected;
			}

			if (StreamlabsActionValues.ReadText(context, StreamlabsActionValues.SourceParameter) is not { } source ||
				StreamlabsActionValues.ReadText(context, StreamlabsActionValues.VariableParameter) is not { } variable)
			{
				return StreamlabsActionResults.MissingParameter(AppStrings.Integrations.StreamlabsDesktop.Errors
					.AudioSourceAndVariableRequired());
			}

			var muted = context.Parameters.GetValueOrDefault(StateParameter) as string == StateMuted;

			object? value = muted
				? await connection.GetAudioMutedAsync(source).ConfigureAwait(false)
				: await connection.GetAudioVolumePercentAsync(source).ConfigureAwait(false) is { } percent
					? Math.Round(percent, 1)
					: null;

			if (value is null)
			{
				return ActionResult.Failed(ActionErrorCodes.NotFound,
					AppStrings.Integrations.StreamlabsDesktop.Errors.AudioSourceNotFound(source: source));
			}

			return await StreamlabsVariableWriter
				.WriteAsync(_variables,
					variable,
					muted ? VariableType.Boolean : VariableType.Numeric,
					value)
				.ConfigureAwait(false)
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.ProviderError,
					AppStrings.Integrations.StreamlabsDesktop.Errors.CouldNotWriteVariable(variable: variable));
		}
	}
}
