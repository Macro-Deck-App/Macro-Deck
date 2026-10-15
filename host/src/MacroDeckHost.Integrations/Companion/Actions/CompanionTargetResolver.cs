using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Companion;
using MacroDeckHost.Integrations.Companion.Configuration;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Companion.Actions;

internal sealed class CompanionTargetResolver
{
	public const string ConfigurationParameter = "configuration";

	private readonly Func<IReadOnlyList<CompanionRuntime>> _runtimes;
	private readonly Func<ICompanionDeviceApi?> _devices;

	public CompanionTargetResolver(Func<IReadOnlyList<CompanionRuntime>> runtimes, Func<ICompanionDeviceApi?> devices)
	{
		_runtimes = runtimes;
		_devices = devices;
	}

	public static ActionParameter Parameter()
	{
		return ActionParameter.DynamicChoice(ConfigurationParameter,
			AppStrings.Integrations.Companion.Params.Configuration(),
			required: true);
	}

	public DynamicOptionsResult ConfigurationOptions()
	{
		return new DynamicOptionsResult
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
	}

	public bool TryResolveTarget(
		IReadOnlyDictionary<string, object> parameters,
		out Guid deviceId,
		out ActionResult error)
	{
		if (!parameters.TryGetValue(ConfigurationParameter, out var raw)
			|| raw is not string text
			|| !Guid.TryParseExact(text, "D", out deviceId))
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

	public async Task<(ICompanionDeviceApi? Devices, ActionResult? Error)> ConnectAsync(
		Guid deviceId,
		CancellationToken cancellationToken)
	{
		if (_devices() is not { } devices || !await devices.WaitForDeviceAsync(deviceId, cancellationToken))
		{
			return (null, NotConnected());
		}

		return HasConfiguration(deviceId) ? (devices, null) : (null, ConfigurationNotFound());
	}

	private bool HasConfiguration(Guid deviceId)
	{
		return _runtimes().Any(runtime => runtime.Id == deviceId);
	}

	private static ActionResult ConfigurationNotFound()
	{
		return ActionResult.Failed(ActionErrorCodes.NotFound,
			AppStrings.Integrations.Companion.Errors.ConfigurationNotFound());
	}

	public static ActionResult InvalidParameter()
	{
		return ActionResult.Failed(ActionErrorCodes.InvalidParameter, AppStrings.Errors.Actions.InvalidParameter());
	}

	public static ActionResult NotConnected()
	{
		return ActionResult.Failed(ActionErrorCodes.NotConnected,
			AppStrings.Integrations.Companion.Errors.NotConnected());
	}
}
