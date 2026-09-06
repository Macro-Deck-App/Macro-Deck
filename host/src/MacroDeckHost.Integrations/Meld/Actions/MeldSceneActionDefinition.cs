using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Meld.Actions;

internal sealed class MeldSceneActionDefinition : IDynamicOptionsActionDefinition, IStateProviderActionDefinition
{
	private readonly Func<MeldConnection?> _resolver;
	private readonly Func<MeldConnection, string, CancellationToken, Task<ActionResult>> _command;
	private readonly Func<MeldSession, string?> _readTargetSceneId;

	public MeldSceneActionDefinition(
		string id,
		LocalizedText name,
		LocalizedText description,
		Func<MeldConnection?> resolver,
		Func<MeldConnection, string, CancellationToken, Task<ActionResult>> command,
		Func<MeldSession, string?> readTargetSceneId)
	{
		Id = id;
		Name = name;
		Description = description;
		_resolver = resolver;
		_command = command;
		_readTargetSceneId = readTargetSceneId;
	}

	public string Id { get; }

	public LocalizedText Name { get; }

	public LocalizedText Description { get; }

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice(MeldActionParameters.Scene,
			label: AppStrings.Integrations.Meld.Parameters.Scene(),
			required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver, _command);

	// Whether *this* scene is the one showing (or staged), not which scene that is: one button per scene
	// is the normal layout, and each has to answer for the scene it names.
	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
		=> Task.FromResult(MeldActionStates.Snapshot(_resolver(),
			parameters.GetValueOrDefault(MeldActionParameters.Scene),
			ActionStates.ActiveInactive,
			session => session.ScenesById,
			session => session.Scenes,
			scene => scene.Name,
			(connection, scene) =>
				string.Equals(_readTargetSceneId(connection.State.Session), scene.Id, StringComparison.Ordinal)));

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		var session = _resolver()?.State.Session ?? MeldSession.Empty;
		return Task.FromResult(new DynamicOptionsResult { Options = MeldOptions.Scenes(session), CacheSeconds = 5 });
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<MeldConnection?> _resolver;
		private readonly Func<MeldConnection, string, CancellationToken, Task<ActionResult>> _command;

		public Executor(Func<MeldConnection?> resolver,
			Func<MeldConnection, string, CancellationToken, Task<ActionResult>> command)
		{
			_resolver = resolver;
			_command = command;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			if (!MeldTargetResolver.TryRequireConnection(connection, out var connectionError))
			{
				return connectionError!;
			}

			var session = connection!.State.Session;
			if (!MeldTargetResolver.TryResolve(AppStrings.Integrations.Meld.Parameters.Scene(),
				context.Parameters.GetValueOrDefault(MeldActionParameters.Scene) as string,
				session.ScenesById,
				session.Scenes,
				scene => scene.Name,
				out var target,
				out var resolveError))
			{
				return resolveError!;
			}

			return await _command(connection, target!.Id, context.CancellationToken).ConfigureAwait(false);
		}
	}
}
