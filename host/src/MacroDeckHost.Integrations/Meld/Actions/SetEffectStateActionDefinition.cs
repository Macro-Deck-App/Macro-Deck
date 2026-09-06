using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Meld.Actions;

internal sealed class SetEffectStateActionDefinition : IDynamicOptionsActionDefinition, IStateProviderActionDefinition
{
	private readonly Func<MeldConnection?> _resolver;

	public SetEffectStateActionDefinition(Func<MeldConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "set-effect-state";

	public LocalizedText Name => AppStrings.Integrations.Meld.Actions.SetEffectState.Name();

	public LocalizedText Description => AppStrings.Integrations.Meld.Actions.SetEffectState.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice(MeldActionParameters.Effect,
			label: AppStrings.Integrations.Meld.Parameters.Effect(),
			required: true),
		ActionParameter.Choice(MeldActionParameters.Mode,
			options: MeldActionParameters.EffectStateModes,
			label: AppStrings.Integrations.Meld.Parameters.Mode(),
			defaultValue: MeldActionParameters.ModeToggle)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
		=> Task.FromResult(MeldActionStates.Snapshot(_resolver(),
			parameters.GetValueOrDefault(MeldActionParameters.Effect),
			ActionStates.Enablement,
			session => session.EffectsById,
			session => session.EffectsById.Values,
			effect => effect.Name,
			(_, effect) => effect.Enabled));

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		var session = _resolver()?.State.Session ?? MeldSession.Empty;
		return Task.FromResult(new DynamicOptionsResult { Options = MeldOptions.Effects(session), CacheSeconds = 5 });
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
			if (!MeldTargetResolver.TryResolve(AppStrings.Integrations.Meld.Parameters.Effect(),
				context.Parameters.GetValueOrDefault(MeldActionParameters.Effect) as string,
				session.EffectsById,
				session.EffectsById.Values,
				effect => effect.Name,
				out var target,
				out var resolveError))
			{
				return resolveError!;
			}

			var mode = context.Parameters.GetValueOrDefault(MeldActionParameters.Mode) as string ??
				MeldActionParameters.ModeToggle;
			bool? desired = mode switch
			{
				"enable" => true,
				"disable" => false,
				_ => null
			};

			return await connection.SetEffectEnabledAsync(target!.Id, desired, context.CancellationToken)
				.ConfigureAwait(false);
		}
	}
}
