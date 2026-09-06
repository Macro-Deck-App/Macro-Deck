using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal sealed class SourceVisibilityActionDefinition : IDynamicOptionsActionDefinition, IStateProviderActionDefinition
{
	internal const string SceneParameter = "scene";
	internal const string SourceParameter = "source";
	internal const string ModeParameter = "mode";

	private const string ModeShow = "show";
	private const string ModeHide = "hide";
	private const string ModeToggle = "toggle";

	private readonly ObsTargetResolver _resolver;

	public SourceVisibilityActionDefinition(Func<ObsConnection?> resolver)
		: this(ObsTargetResolver.Legacy(resolver))
	{
	}

	public SourceVisibilityActionDefinition(ObsTargetResolver resolver)
	{
		_resolver = resolver;
	}

	public string Id => "set-source-visibility";

	public LocalizedText Name => AppStrings.Integrations.Obs.Actions.SetSourceVisibility.Name();

	public LocalizedText Description => AppStrings.Integrations.Obs.Actions.SetSourceVisibility.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ObsTargetResolver.Parameter(),
		ActionParameter.DynamicChoice(SceneParameter,
			label: AppStrings.Integrations.Obs.Params.Scene(),
			required: true),
		ActionParameter.DynamicChoice(SourceParameter,
			label: AppStrings.Integrations.Obs.Params.Source(),
			required: true),
		ActionParameter.Choice(ModeParameter,
			options:
			[
				new ActionParameterOption
					{ Value = ModeToggle, Label = AppStrings.Integrations.Obs.Params.ModeToggle() },
				new ActionParameterOption
					{ Value = ModeShow, Label = AppStrings.Integrations.Obs.Actions.SetSourceVisibility.ModeShow() },
				new ActionParameterOption
					{ Value = ModeHide, Label = AppStrings.Integrations.Obs.Actions.SetSourceVisibility.ModeHide() }
			],
			label: AppStrings.Integrations.Obs.Params.Mode(),
			defaultValue: ModeToggle)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public async Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		if (parameters.GetValueOrDefault(SceneParameter)?.ToString() is not { Length: > 0 } scene ||
			parameters.GetValueOrDefault(SourceParameter)?.ToString() is not { Length: > 0 } source)
		{
			return null;
		}

		var connection = _resolver.ForOptions(parameters);
		var visible = connection is null ? null : await connection.GetSourceVisibleCachedAsync(scene, source);
		return ActionStates.Snapshot(ActionStates.Visibility, visible);
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
		IReadOnlyList<string> values = [];

		if (connection is not null)
		{
			if (context.ParameterName == SourceParameter &&
				context.CurrentParameters.GetValueOrDefault(SceneParameter) is string scene &&
				!string.IsNullOrWhiteSpace(scene))
			{
				values = await connection.GetSceneItemNamesAsync(scene);
			}
			else if (context.ParameterName == SceneParameter)
			{
				values = await connection.GetSceneNamesAsync();
			}
		}

		return new DynamicOptionsResult
		{
			Options = values.Select(v => new ActionParameterOption { Value = v, Label = v }).ToList(),
			CacheSeconds = 5
		};
	}

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<SourceVisibilityActionDefinition>(ObsIntegration.IntegrationId);

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

			if (context.Parameters.GetValueOrDefault(SceneParameter) is not string scene ||
				string.IsNullOrWhiteSpace(scene) ||
				context.Parameters.GetValueOrDefault(SourceParameter) is not string source ||
				string.IsNullOrWhiteSpace(source))
			{
				_logger.Warning("OBS source action skipped: scene or source not selected");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Obs.Errors.NoSceneOrSourceSelected());
			}

			var mode = context.Parameters.GetValueOrDefault(ModeParameter) as string ?? ModeToggle;
			var succeeded = mode switch
			{
				ModeShow => await connection.SetSourceVisibleAsync(scene, source, true),
				ModeHide => await connection.SetSourceVisibleAsync(scene, source, false),
				_ => await connection.ToggleSourceVisibleAsync(scene, source)
			};

			return succeeded
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Obs.Errors.NotConnected());
		}
	}
}
