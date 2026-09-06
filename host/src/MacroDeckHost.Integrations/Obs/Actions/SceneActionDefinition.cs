using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal sealed class SceneActionDefinition : IDynamicOptionsActionDefinition, IStateProviderActionDefinition
{
	internal const string SceneParameter = "scene";

	private readonly ObsTargetResolver _resolver;
	private readonly bool _preview;

	public SceneActionDefinition(string id,
		LocalizedText name,
		LocalizedText description,
		bool preview,
		Func<ObsConnection?> resolver)
		: this(id, name, description, preview, ObsTargetResolver.Legacy(resolver))
	{
	}

	public SceneActionDefinition(string id,
		LocalizedText name,
		LocalizedText description,
		bool preview,
		ObsTargetResolver resolver)
	{
		Id = id;
		Name = name;
		Description = description;
		_preview = preview;
		_resolver = resolver;
	}

	public string Id { get; }

	public LocalizedText Name { get; }

	public LocalizedText Description { get; }

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ObsTargetResolver.Parameter(),
		ActionParameter.DynamicChoice(SceneParameter, label: AppStrings.Integrations.Obs.Params.Scene(), required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver, _preview);

	// Answers for the scene this instance is configured with, not for the scene that happens to be live:
	// several buttons each naming a different scene is the normal layout, and every one of them has to be
	// able to show whether it is the current one.
	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		var state = _resolver.ForOptions(parameters)?.State;
		if (parameters.GetValueOrDefault(SceneParameter)?.ToString() is not { Length: > 0 } scene)
		{
			return Task.FromResult<ActionStateSnapshot?>(null);
		}

		var current = _preview ? state?.PreviewScene : state?.CurrentScene;
		var active = state is { IsConnected: true }
			? string.Equals(current, scene, StringComparison.Ordinal)
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
		var scenes = connection is null ? [] : await connection.GetSceneNamesAsync();

		return new DynamicOptionsResult
		{
			Options = scenes.Select(s => new ActionParameterOption { Value = s, Label = s }).ToList(),
			CacheSeconds = 5
		};
	}

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<SceneActionDefinition>(ObsIntegration.IntegrationId);

		private readonly ObsTargetResolver _resolver;
		private readonly bool _preview;

		public Executor(ObsTargetResolver resolver, bool preview)
		{
			_resolver = resolver;
			_preview = preview;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (!_resolver.TryResolve(context.Parameters, out var connection, out var error))
			{
				return error;
			}

			if (context.Parameters.GetValueOrDefault(SceneParameter) is not string scene ||
				string.IsNullOrWhiteSpace(scene))
			{
				_logger.Warning("OBS scene action skipped: no scene selected");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Obs.Errors.NoSceneSelected());
			}

			if (_preview)
			{
				return await connection.SetPreviewSceneAsync(scene)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.NotConnected,
						AppStrings.Integrations.Obs.Errors.NotConnected());
			}
			else
			{
				return await connection.SetSceneAsync(scene)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.NotConnected,
						AppStrings.Integrations.Obs.Errors.NotConnected());
			}
		}
	}
}
