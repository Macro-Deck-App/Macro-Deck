using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal sealed class SceneCollectionActionDefinition : IDynamicOptionsActionDefinition, IStateProviderActionDefinition
{
	internal const string SceneCollectionParameter = "sceneCollection";

	private readonly ObsTargetResolver _resolver;

	public SceneCollectionActionDefinition(ObsTargetResolver resolver)
	{
		_resolver = resolver;
	}

	public string Id => "set-scene-collection";

	public LocalizedText Name => AppStrings.Integrations.Obs.Actions.SetSceneCollection.Name();

	public LocalizedText Description => AppStrings.Integrations.Obs.Actions.SetSceneCollection.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ObsTargetResolver.Parameter(),
		ActionParameter.DynamicChoice(SceneCollectionParameter,
			label: AppStrings.Integrations.Obs.Params.SceneCollection(),
			required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		var state = _resolver.ForOptions(parameters)?.State;
		if (parameters.GetValueOrDefault(SceneCollectionParameter)?.ToString() is not { Length: > 0 } sceneCollection)
		{
			return Task.FromResult<ActionStateSnapshot?>(null);
		}

		var active = state is { IsConnected: true }
			? string.Equals(state.CurrentSceneCollection, sceneCollection, StringComparison.Ordinal)
			: (bool?)null;

		return Task.FromResult<ActionStateSnapshot?>(ActionStates.Snapshot(ActionStates.ActiveInactive, active));
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
		var sceneCollections = connection is null ? [] : await connection.GetSceneCollectionNamesAsync();

		return new DynamicOptionsResult
		{
			Options = sceneCollections.Select(c => new ActionParameterOption { Value = c, Label = c }).ToList(),
			CacheSeconds = 5
		};
	}

	private sealed class Executor : IActionExecutor
	{
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

			if (context.Parameters.GetValueOrDefault(SceneCollectionParameter) is not string sceneCollection ||
				string.IsNullOrWhiteSpace(sceneCollection))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Obs.Errors.NoSceneCollectionSelected());
			}

			var result = await connection.SetSceneCollectionAsync(sceneCollection);
			return result.Outcome == ObsCommandOutcome.Rejected
				? Rejected(result, sceneCollection)
				: ObsCommandResults.ToActionResult(result);
		}

		private static ActionResult Rejected(ObsCommandResult result, string sceneCollection) => result.Code switch
		{
			ObsRequestException.ResourceNotFound => ActionResult.Failed(ActionErrorCodes.NotFound,
				AppStrings.Integrations.Obs.Errors.SceneCollectionNotFound(sceneCollection)),
			ObsRequestException.NotReady => ActionResult.Failed(ActionErrorCodes.Unavailable,
				AppStrings.Integrations.Obs.Errors.SceneCollectionLoading()),
			ObsRequestException.TimedOut => ActionResult.Failed(ActionErrorCodes.Timeout,
				AppStrings.Integrations.Obs.Errors.SceneCollectionSlowToLoad()),
			_ => ObsCommandResults.ToActionResult(result)
		};
	}
}
