using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Meld.Actions;

internal sealed class AdjustTrackVolumeActionDefinition : IDynamicOptionsActionDefinition
{
	private readonly Func<MeldConnection?> _resolver;

	public AdjustTrackVolumeActionDefinition(Func<MeldConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "adjust-track-volume";

	public LocalizedText Name => AppStrings.Integrations.Meld.Actions.AdjustTrackVolume.Name();

	public LocalizedText Description => AppStrings.Integrations.Meld.Actions.AdjustTrackVolume.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice(MeldActionParameters.Track,
			label: AppStrings.Integrations.Meld.Parameters.Track(),
			required: true),
		ActionParameter.Number(MeldActionParameters.Amount,
			label: AppStrings.Integrations.Meld.Parameters.ChangeBy(),
			min: -100,
			max: 100,
			step: 1,
			defaultValue: 5)
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

			if (!connection.TryGetGain(target!.Id, out var gain))
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable,
					AppStrings.Integrations.Meld.Errors.VolumeNotReported());
			}

			var amount = MeldActionParameters.ReadNumber(
					context.Parameters.GetValueOrDefault(MeldActionParameters.Amount)) ??
				0;
			var gain01 = Math.Clamp((gain.Gain * 100) + amount, 0, 100) / 100.0;

			return await connection.SetGainAsync(target.Id, gain01, context.CancellationToken).ConfigureAwait(false);
		}
	}
}
