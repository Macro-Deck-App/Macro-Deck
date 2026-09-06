using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.StreamlabsDesktop.Actions;

internal sealed class SetSceneActionDefinition : IDynamicOptionsActionDefinition, IStateProviderActionDefinition
{
	private readonly Func<StreamlabsDesktopConnection?> _resolver;

	public SetSceneActionDefinition(Func<StreamlabsDesktopConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "set-scene";

	public LocalizedText Name => AppStrings.Integrations.StreamlabsDesktop.Actions.SetSceneName();

	public LocalizedText Description => AppStrings.Integrations.StreamlabsDesktop.Actions.SetSceneDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice(StreamlabsActionValues.SceneParameter,
			label: AppStrings.Integrations.StreamlabsDesktop.Params.SceneLabel(),
			required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		if (parameters.GetValueOrDefault(StreamlabsActionValues.SceneParameter)?.ToString()
			is not { Length: > 0 } scene)
		{
			return Task.FromResult<ActionStateSnapshot?>(null);
		}

		var state = _resolver()?.State;
		var active = state is { IsConnected: true }
			? string.Equals(state.CurrentScene, scene, StringComparison.Ordinal) ||
			string.Equals(state.CurrentSceneId, scene, StringComparison.Ordinal)
			: (bool?)null;

		return Task.FromResult<ActionStateSnapshot?>(ActionStates.Snapshot(ActionStates.ActiveInactive, active));
	}

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> StreamlabsActionValues.SceneOrSourceOptionsAsync(_resolver(), context);

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

			if (StreamlabsActionValues.ReadText(context, StreamlabsActionValues.SceneParameter) is not { } scene)
			{
				return StreamlabsActionResults.MissingParameter(AppStrings.Integrations.StreamlabsDesktop.Errors
					.NoSceneSelected());
			}

			var result = await connection.SetSceneAsync(scene).ConfigureAwait(false);
			return result.ToActionResult();
		}
	}
}
