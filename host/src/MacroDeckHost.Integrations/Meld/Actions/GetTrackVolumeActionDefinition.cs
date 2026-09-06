using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.Meld.Actions;

internal sealed class GetTrackVolumeActionDefinition : IDynamicOptionsActionDefinition
{
	private readonly Func<MeldConnection?> _resolver;
	private readonly VariableApiAccessor _variables;

	public GetTrackVolumeActionDefinition(Func<MeldConnection?> resolver, VariableApiAccessor variables)
	{
		_resolver = resolver;
		_variables = variables;
	}

	public string Id => "get-track-volume";

	public LocalizedText Name => AppStrings.Integrations.Meld.Actions.GetTrackVolume.Name();

	public LocalizedText Description => AppStrings.Integrations.Meld.Actions.GetTrackVolume.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice(MeldActionParameters.Track,
			label: AppStrings.Integrations.Meld.Parameters.Track(),
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
		return Task.FromResult(new DynamicOptionsResult { Options = MeldOptions.Tracks(session), CacheSeconds = 5 });
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

			if (context.Parameters.GetValueOrDefault(MeldActionParameters.Variable) is not string variable ||
				string.IsNullOrWhiteSpace(variable))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Meld.Errors.NoTargetVariable());
			}

			if (!connection.TryGetGain(target!.Id, out var gain))
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable,
					AppStrings.Integrations.Meld.Errors.VolumeNotReported());
			}

			var written = await MeldVariableWriter
				.WriteAsync(_variables, variable, VariableType.Numeric, Math.Round(gain.Gain * 100))
				.ConfigureAwait(false);
			return written
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.Unavailable,
					AppStrings.Integrations.Meld.Errors.CouldNotWriteVariable(variable: variable));
		}
	}
}
