using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.Meld.Actions;

internal sealed class GetLayerVisibilityActionDefinition : IDynamicOptionsActionDefinition
{
	private readonly Func<MeldConnection?> _resolver;
	private readonly VariableApiAccessor _variables;

	public GetLayerVisibilityActionDefinition(Func<MeldConnection?> resolver, VariableApiAccessor variables)
	{
		_resolver = resolver;
		_variables = variables;
	}

	public string Id => "get-layer-visibility";

	public LocalizedText Name => AppStrings.Integrations.Meld.Actions.GetLayerVisibility.Name();

	public LocalizedText Description => AppStrings.Integrations.Meld.Actions.GetLayerVisibility.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice(MeldActionParameters.Layer,
			label: AppStrings.Integrations.Meld.Parameters.Layer(),
			required: true),
		ActionParameter.Text(MeldActionParameters.Variable,
			label: AppStrings.Integrations.Meld.Parameters.SaveToVariable(),
			required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver, _variables);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		var session = _resolver()?.State.Session ?? MeldSession.Empty;
		return Task.FromResult(new DynamicOptionsResult { Options = MeldOptions.Layers(session), CacheSeconds = 5 });
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<MeldConnection?> _resolver;
		private readonly VariableApiAccessor _variables;

		public Executor(Func<MeldConnection?> resolver, VariableApiAccessor variables)
		{
			_resolver = resolver;
			_variables = variables;
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

			if (context.Parameters.GetValueOrDefault(MeldActionParameters.Variable) is not string variable ||
				string.IsNullOrWhiteSpace(variable))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Meld.Errors.NoTargetVariable());
			}

			var written = await MeldVariableWriter
				.WriteAsync(_variables, variable, VariableType.Boolean, target!.Visible)
				.ConfigureAwait(false);
			return written
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.Unavailable,
					AppStrings.Integrations.Meld.Errors.CouldNotWriteVariable(variable: variable));
		}
	}
}
