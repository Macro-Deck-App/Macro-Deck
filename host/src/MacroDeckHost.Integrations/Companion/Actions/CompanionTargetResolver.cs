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

	public bool TryResolve(IReadOnlyDictionary<string, object> parameters,
		out Guid deviceId,
		out ICompanionGateway gateway,
		out ActionResult error)
	{
		gateway = null!;
		if (!parameters.TryGetValue(ConfigurationParameter, out var raw) ||
			raw is not string text ||
			!Guid.TryParseExact(text, "D", out deviceId))
		{
			deviceId = Guid.Empty;
			error = InvalidParameter();
			return false;
		}

		var id = deviceId;
		if (!_runtimes().Any(runtime => runtime.Id == id))
		{
			error = ActionResult.Failed(ActionErrorCodes.NotFound,
				AppStrings.Integrations.Companion.Errors.ConfigurationNotFound());
			return false;
		}

		if (_gateway() is not { } resolved || !resolved.TryGetState(id, out _))
		{
			error = NotConnected();
			return false;
		}

		gateway = resolved;
		error = null!;
		return true;
	}

	public static ActionResult InvalidParameter()
		=> ActionResult.Failed(ActionErrorCodes.InvalidParameter, AppStrings.Errors.Actions.InvalidParameter());

	public static ActionResult NotConnected()
		=> ActionResult.Failed(ActionErrorCodes.NotConnected,
			AppStrings.Integrations.Companion.Errors.NotConnected());
}
