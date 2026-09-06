using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Meld.Actions;

internal sealed class SetLayerVisibilityActionDefinition : IDynamicOptionsActionDefinition,
	IStateProviderActionDefinition
{
	private readonly Func<MeldConnection?> _resolver;

	public SetLayerVisibilityActionDefinition(Func<MeldConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "set-layer-visibility";

	public LocalizedText Name => AppStrings.Integrations.Meld.Actions.SetLayerVisibility.Name();

	public LocalizedText Description => AppStrings.Integrations.Meld.Actions.SetLayerVisibility.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice(MeldActionParameters.Layer,
			label: AppStrings.Integrations.Meld.Parameters.Layer(),
			required: true),
		ActionParameter.Choice(MeldActionParameters.Mode,
			options: MeldActionParameters.LayerVisibilityModes,
			label: AppStrings.Integrations.Meld.Parameters.Mode(),
			defaultValue: MeldActionParameters.ModeToggle)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
		=> Task.FromResult(MeldActionStates.Snapshot(_resolver(),
			parameters.GetValueOrDefault(MeldActionParameters.Layer),
			ActionStates.Visibility,
			session => session.LayersById,
			session => session.LayersById.Values,
			layer => layer.Name,
			(_, layer) => layer.Visible));

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		var session = _resolver()?.State.Session ?? MeldSession.Empty;
		return Task.FromResult(new DynamicOptionsResult { Options = MeldOptions.Layers(session), CacheSeconds = 5 });
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<MeldConnection?> _resolver;

		public Executor(Func<MeldConnection?> resolver)
		{
			_resolver = resolver;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			if (!MeldTargetResolver.TryRequireConnection(connection, out var connectionError))
			{
				return connectionError!;
			}

			var session = connection!.State.Session;
			if (!MeldTargetResolver.TryResolve(AppStrings.Integrations.Meld.Parameters.Layer(),
				context.Parameters.GetValueOrDefault(MeldActionParameters.Layer) as string,
				session.LayersById,
				session.LayersById.Values,
				layer => layer.Name,
				out var target,
				out var resolveError))
			{
				return resolveError!;
			}

			var mode = context.Parameters.GetValueOrDefault(MeldActionParameters.Mode) as string ??
				MeldActionParameters.ModeToggle;
			bool? desired = mode switch
			{
				"show" => true,
				"hide" => false,
				_ => null
			};

			return await connection.SetLayerVisibleAsync(target!.Id, desired, context.CancellationToken)
				.ConfigureAwait(false);
		}
	}
}
