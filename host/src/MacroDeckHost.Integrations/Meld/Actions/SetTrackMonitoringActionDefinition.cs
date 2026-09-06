using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Meld.Actions;

internal sealed class SetTrackMonitoringActionDefinition : IDynamicOptionsActionDefinition,
	IStateProviderActionDefinition
{
	private readonly Func<MeldConnection?> _resolver;

	public SetTrackMonitoringActionDefinition(Func<MeldConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "set-track-monitoring";

	public LocalizedText Name => AppStrings.Integrations.Meld.Actions.SetTrackMonitoring.Name();

	public LocalizedText Description => AppStrings.Integrations.Meld.Actions.SetTrackMonitoring.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice(MeldActionParameters.Track,
			label: AppStrings.Integrations.Meld.Parameters.Track(),
			required: true),
		ActionParameter.Choice(MeldActionParameters.Mode,
			options: MeldActionParameters.TrackMonitoringModes,
			label: AppStrings.Integrations.Meld.Parameters.Mode(),
			defaultValue: MeldActionParameters.ModeToggle)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
		=> Task.FromResult(MeldActionStates.Snapshot(_resolver(),
			parameters.GetValueOrDefault(MeldActionParameters.Track),
			ActionStates.Monitoring,
			session => session.TracksById,
			session => session.Tracks,
			track => track.Name,
			(_, track) => track.Monitoring));

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		var session = _resolver()?.State.Session ?? MeldSession.Empty;
		return Task.FromResult(new DynamicOptionsResult { Options = MeldOptions.Tracks(session), CacheSeconds = 5 });
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
			if (!MeldTargetResolver.TryResolve(AppStrings.Integrations.Meld.Parameters.Track(),
				context.Parameters.GetValueOrDefault(MeldActionParameters.Track) as string,
				session.TracksById,
				session.Tracks,
				track => track.Name,
				out var target,
				out var resolveError))
			{
				return resolveError!;
			}

			var mode = context.Parameters.GetValueOrDefault(MeldActionParameters.Mode) as string ??
				MeldActionParameters.ModeToggle;
			bool? desired = mode switch
			{
				"on" => true,
				"off" => false,
				_ => null
			};

			return await connection.SetTrackMonitoringAsync(target!.Id, desired, context.CancellationToken)
				.ConfigureAwait(false);
		}
	}
}
