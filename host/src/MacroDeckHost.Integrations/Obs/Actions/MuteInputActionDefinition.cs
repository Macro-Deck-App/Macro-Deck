using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal sealed class MuteInputActionDefinition : IDynamicOptionsActionDefinition, IStateProviderActionDefinition
{
	internal const string InputParameter = "input";
	internal const string ModeParameter = "mode";

	private const string ModeMute = "mute";
	private const string ModeUnmute = "unmute";
	private const string ModeToggle = "toggle";

	private readonly ObsTargetResolver _resolver;

	public MuteInputActionDefinition(Func<ObsConnection?> resolver)
		: this(ObsTargetResolver.Legacy(resolver))
	{
	}

	public MuteInputActionDefinition(ObsTargetResolver resolver)
	{
		_resolver = resolver;
	}

	public string Id => "set-input-mute";

	public LocalizedText Name => AppStrings.Integrations.Obs.Actions.MuteInput.Name();

	public LocalizedText Description => AppStrings.Integrations.Obs.Actions.MuteInput.Description();

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
					{ Value = ModeToggle, Label = AppStrings.Integrations.Obs.Params.ModeToggle() },
				new ActionParameterOption
					{ Value = ModeMute, Label = AppStrings.Integrations.Obs.Actions.MuteInput.ModeMute() },
				new ActionParameterOption
					{ Value = ModeUnmute, Label = AppStrings.Integrations.Obs.Actions.MuteInput.ModeUnmute() }
			],
			label: AppStrings.Integrations.Obs.Params.Mode(),
			defaultValue: ModeToggle)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public async Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		if (parameters.GetValueOrDefault(InputParameter)?.ToString() is not { Length: > 0 } input)
		{
			return null;
		}

		var connection = _resolver.ForOptions(parameters);
		var muted = connection is null ? null : await connection.GetInputMutedCachedAsync(input);
		return ActionStates.Snapshot(ActionStates.Mute, muted);
	}

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
			IntegrationLog.For<MuteInputActionDefinition>(ObsIntegration.IntegrationId);

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
				_logger.Warning("OBS mute action skipped: no input selected");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Obs.Errors.NoInputSelected());
			}

			var mode = context.Parameters.GetValueOrDefault(ModeParameter) as string ?? ModeToggle;
			var succeeded = mode switch
			{
				ModeMute => await connection.SetInputMuteAsync(input, true),
				ModeUnmute => await connection.SetInputMuteAsync(input, false),
				_ => await connection.ToggleInputMuteAsync(input)
			};

			return succeeded
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Obs.Errors.NotConnected());
		}
	}
}
