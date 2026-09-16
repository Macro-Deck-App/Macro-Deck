using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal sealed class ProfileActionDefinition : IDynamicOptionsActionDefinition, IStateProviderActionDefinition
{
	internal const string ProfileParameter = "profile";

	private readonly ObsTargetResolver _resolver;

	public ProfileActionDefinition(ObsTargetResolver resolver)
	{
		_resolver = resolver;
	}

	public string Id => "set-profile";

	public LocalizedText Name => AppStrings.Integrations.Obs.Actions.SetProfile.Name();

	public LocalizedText Description => AppStrings.Integrations.Obs.Actions.SetProfile.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ObsTargetResolver.Parameter(),
		ActionParameter.DynamicChoice(ProfileParameter,
			label: AppStrings.Integrations.Obs.Params.Profile(),
			required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		var state = _resolver.ForOptions(parameters)?.State;
		if (parameters.GetValueOrDefault(ProfileParameter)?.ToString() is not { Length: > 0 } profile)
		{
			return Task.FromResult<ActionStateSnapshot?>(null);
		}

		var active = state is { IsConnected: true }
			? string.Equals(state.CurrentProfile, profile, StringComparison.Ordinal)
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
		var profiles = connection is null ? [] : await connection.GetProfileNamesAsync();

		return new DynamicOptionsResult
		{
			Options = profiles.Select(p => new ActionParameterOption { Value = p, Label = p }).ToList(),
			CacheSeconds = 5
		};
	}

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<ProfileActionDefinition>(ObsIntegration.IntegrationId);

		private readonly ObsTargetResolver _resolver;

		public Executor(ObsTargetResolver resolver)
		{
			_resolver = resolver;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (!_resolver.TryResolve(context.Parameters, out var connection, out var error))
			{
				return error;
			}

			if (context.Parameters.GetValueOrDefault(ProfileParameter) is not string profile ||
				string.IsNullOrWhiteSpace(profile))
			{
				_logger.Warning("OBS profile action skipped: no profile selected");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Obs.Errors.NoProfileSelected());
			}

			return await connection.SetProfileAsync(profile)
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Obs.Errors.NotConnected());
		}
	}
}
