using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal sealed class GetSourceVisibilityActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string SceneParameter = "scene";
	internal const string SourceParameter = "source";
	internal const string VariableParameter = "variable";

	private readonly ObsTargetResolver _resolver;
	private readonly VariableApiAccessor _variables;

	public GetSourceVisibilityActionDefinition(Func<ObsConnection?> resolver, VariableApiAccessor variables)
		: this(ObsTargetResolver.Legacy(resolver), variables)
	{
	}

	public GetSourceVisibilityActionDefinition(ObsTargetResolver resolver, VariableApiAccessor variables)
	{
		_resolver = resolver;
		_variables = variables;
	}

	public string Id => "get-source-visibility";

	public LocalizedText Name => AppStrings.Integrations.Obs.Actions.GetSourceVisibility.Name();

	public LocalizedText Description => AppStrings.Integrations.Obs.Actions.GetSourceVisibility.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ObsTargetResolver.Parameter(),
		ActionParameter.DynamicChoice(SceneParameter,
			label: AppStrings.Integrations.Obs.Params.Scene(),
			required: true),
		ActionParameter.DynamicChoice(SourceParameter,
			label: AppStrings.Integrations.Obs.Params.Source(),
			required: true),
		ActionParameter.Text(VariableParameter,
			label: AppStrings.Integrations.Obs.Params.SaveToVariable(),
			description: AppStrings.Integrations.Obs.Actions.GetSourceVisibility.VariableDescription(),
			required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver, _variables);

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
			IntegrationLog.For<GetSourceVisibilityActionDefinition>(ObsIntegration.IntegrationId);

		private readonly ObsTargetResolver _resolver;
		private readonly VariableApiAccessor _variables;

		public Executor(ObsTargetResolver resolver, VariableApiAccessor variables)
		{
			_resolver = resolver;
			_variables = variables;
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
				_logger.Warning("OBS get-visibility action skipped: scene or source not selected");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Obs.Errors.NoSceneOrSourceSelected());
			}

			if (context.Parameters.GetValueOrDefault(VariableParameter) is not string variable ||
				string.IsNullOrWhiteSpace(variable))
			{
				_logger.Warning("OBS get-visibility action skipped: no target variable");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Obs.Errors.NoTargetVariable());
			}

			var visible = await connection.GetSourceVisibleAsync(scene, source);
			if (visible is null)
			{
				_logger.Warning("OBS get-visibility action skipped: visibility unavailable for '{Source}' in '{Scene}'",
					source,
					scene);
				return ActionResult.Failed(ActionErrorCodes.NotFound,
					AppStrings.Integrations.Obs.Errors.SourceNotFound(source: source, scene: scene));
			}

			var written =
				await ObsVariableWriter.WriteAsync(_variables, variable, VariableType.Boolean, visible.Value);

			return written
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.ProviderError,
					AppStrings.Integrations.Obs.Errors.VariableWriteFailed(variable: variable));
		}
	}
}
