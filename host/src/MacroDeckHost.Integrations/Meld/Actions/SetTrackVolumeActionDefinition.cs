using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Meld.Actions;

internal sealed class SetTrackVolumeActionDefinition : IDynamicOptionsActionDefinition
{
	private readonly Func<MeldConnection?> _resolver;

	public SetTrackVolumeActionDefinition(Func<MeldConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "set-track-volume";

	public LocalizedText Name => AppStrings.Integrations.Meld.Actions.SetTrackVolume.Name();

	public LocalizedText Description => AppStrings.Integrations.Meld.Actions.SetTrackVolume.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice(MeldActionParameters.Track,
			label: AppStrings.Integrations.Meld.Parameters.Track(),
			required: true),
		ActionParameter.Slider(MeldActionParameters.Volume,
			min: 0,
			max: 100,
			label: AppStrings.Integrations.Meld.Parameters.Volume(),
			step: 1,
			defaultValue: 100)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

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

			var volume = MeldActionParameters.ReadNumber(
					context.Parameters.GetValueOrDefault(MeldActionParameters.Volume)) ??
				100;
			var gain01 = Math.Clamp(volume, 0, 100) / 100.0;

			return await connection.SetGainAsync(target!.Id, gain01, context.CancellationToken).ConfigureAwait(false);
		}
	}
}
