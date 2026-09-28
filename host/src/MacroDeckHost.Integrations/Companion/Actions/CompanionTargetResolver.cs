using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Companion.Actions;

internal sealed class CompanionTargetResolver
{
	public const string ConfigurationParameter = "configuration";

	private readonly Func<IReadOnlyList<CompanionRuntime>> _runtimes;
	private readonly Func<ICompanionGateway?> _gateway;

	public CompanionTargetResolver(Func<IReadOnlyList<CompanionRuntime>> runtimes, Func<ICompanionGateway?> gateway)
	{
		_runtimes = runtimes;
		_gateway = gateway;
	}

	public static ActionParameter Parameter()
		=> ActionParameter.DynamicChoice(ConfigurationParameter,
			label: AppStrings.Integrations.Companion.Params.Configuration(),
			required: true);

	public DynamicOptionsResult ConfigurationOptions()
		=> new()
		{
			Options = _runtimes()
				.Select(runtime => new ActionParameterOption
				{
					Value = runtime.Id.ToString("D"),
					Label = runtime.Title
				})
				.ToList(),
			CacheSeconds = 0
		};

	public bool TryResolveTarget(IReadOnlyDictionary<string, object> parameters,
		out Guid deviceId,
		out ActionResult error)
	{
		if (!parameters.TryGetValue(ConfigurationParameter, out var raw) ||
			raw is not string text ||
			!Guid.TryParseExact(text, "D", out deviceId))
		{
			deviceId = Guid.Empty;
			error = InvalidParameter();
			return false;
		}

		if (!HasConfiguration(deviceId))
		{
			error = ConfigurationNotFound();
			return false;
		}

		error = null!;
		return true;
	}

	public async Task<(ICompanionGateway? Gateway, ActionResult? Error)> ConnectAsync(Guid deviceId,
		CancellationToken cancellationToken)
	{
		if (_gateway() is not { } gateway || !await gateway.WaitForStateAsync(deviceId, cancellationToken))
		{
			return (null, NotConnected());
		}

		return HasConfiguration(deviceId) ? (gateway, null) : (null, ConfigurationNotFound());
	}

	private bool HasConfiguration(Guid deviceId) => _runtimes().Any(runtime => runtime.Id == deviceId);

	private static ActionResult ConfigurationNotFound()
		=> ActionResult.Failed(ActionErrorCodes.NotFound,
			AppStrings.Integrations.Companion.Errors.ConfigurationNotFound());

	public static ActionResult InvalidParameter()
		=> ActionResult.Failed(ActionErrorCodes.InvalidParameter, AppStrings.Errors.Actions.InvalidParameter());

	public static ActionResult NotConnected()
		=> ActionResult.Failed(ActionErrorCodes.NotConnected,
			AppStrings.Integrations.Companion.Errors.NotConnected());
}
