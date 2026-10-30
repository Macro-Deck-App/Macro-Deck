using MacroDeckHost.Integrations.Adb.Connections;
using MacroDeckHost.Integrations.Adb.Models;
using MacroDeckHost.Integrations.Adb.State;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Android;

namespace MacroDeckHost.Integrations.Adb.Actions;

internal sealed class AdbActionDefinition : IActionDefinition
{
	internal const string DeviceParameterName = "device";
	internal static readonly LocalizedText DisabledMessage = AppStrings.Integrations.Adb.Errors.AdbDisabled();

	private readonly AdbActionServices _services;
	private readonly Func<AdbTarget, IReadOnlyDictionary<string, object>, CancellationToken, Task<AdbOutcome>> _command;

	public AdbActionDefinition(
		AdbActionServices services,
		string id,
		LocalizedText name,
		LocalizedText description,
		LocalizedText deviceDescription,
		IReadOnlyList<ActionParameter> extraParameters,
		Func<AdbTarget, IReadOnlyDictionary<string, object>, CancellationToken, Task<AdbOutcome>> command,
		ActionRiskCategory riskCategory = ActionRiskCategory.None)
	{
		_services = services;
		RiskCategory = riskCategory;
		Id = id;
		Name = name;
		Description = description;
		_command = command;
		Parameters = [DeviceParameter(deviceDescription), .. extraParameters];
	}

	public string Id { get; }

	public LocalizedText Name { get; }

	public LocalizedText Description { get; }

	public IReadOnlyList<ActionParameter> Parameters { get; }

	public ActionRiskCategory RiskCategory { get; }

	public IActionExecutor CreateExecutor()
	{
		return new Executor(this);
	}

	internal static ActionParameter DeviceParameter(LocalizedText description)
	{
		return ActionParameter.DynamicChoice(DeviceParameterName,
			AppStrings.Integrations.Adb.Params.DeviceLabel(),
			description,
			HostOptionsSources.AndroidDevices,
			AppStrings.Integrations.Adb.Params.DeviceDefaultPlaceholder(),
			false);
	}

	internal static string? ReadDevice(IReadOnlyDictionary<string, object> parameters)
	{
		return parameters.TryGetValue(DeviceParameterName, out var value) ? value as string : null;
	}

	internal static ActionResult MapResult(AdbOutcome outcome)
	{
		if (outcome.Failure is not { } failure)
		{
			return ActionResult.Success();
		}

		var errorCode = failure switch
		{
			AndroidDeviceErrorCode.AdbNotEnabled => ActionErrorCodes.Unavailable,
			AndroidDeviceErrorCode.AdbNotAllowed => ActionErrorCodes.Unavailable,
			AndroidDeviceErrorCode.AdbUnavailable => ActionErrorCodes.Unavailable,
			AndroidDeviceErrorCode.DeviceNotFound => ActionErrorCodes.NotFound,
			AndroidDeviceErrorCode.DeviceUnauthorized => ActionErrorCodes.PermissionDenied,
			AndroidDeviceErrorCode.DeviceOffline => ActionErrorCodes.NotConnected,
			AndroidDeviceErrorCode.HostUnavailable => ActionErrorCodes.NotConnected,
			AndroidDeviceErrorCode.Timeout => ActionErrorCodes.Timeout,
			AndroidDeviceErrorCode.InvalidArgument => ActionErrorCodes.InvalidParameter,
			AndroidDeviceErrorCode.CommandFailed => ActionErrorCodes.ProviderError,
			AndroidDeviceErrorCode.Unsupported => ActionErrorCodes.Unavailable,
			AndroidDeviceErrorCode.HostLocked => ActionErrorCodes.Unavailable,
			_ => ActionErrorCodes.ProviderError
		};

		return ActionResult.Failed(errorCode,
			outcome.Message is { } message ? message : AppStrings.Integrations.Adb.Errors.CommandFailed());
	}

	internal static bool IsAvailable(IAndroidDeviceManager? manager, out IAndroidDeviceManager available)
	{
		available = manager!;
		return manager is { Access: AndroidDeviceAccess.Available };
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly AdbActionDefinition _owner;

		public Executor(AdbActionDefinition owner)
		{
			_owner = owner;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (!IsAvailable(_owner._services.Devices(), out var manager))
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, DisabledMessage);
			}

			var target = new AdbTarget(manager, _owner._services.Tracker(), ReadDevice(context.Parameters));
			var outcome = await _owner._command(target, context.Parameters, context.CancellationToken);
			_owner._services.Health.Record(outcome);
			return MapResult(outcome);
		}
	}
}

internal sealed record AdbActionServices(
	Func<IAndroidDeviceManager?> Devices,
	Func<AdbDeviceTracker?> Tracker,
	AdbHealthTracker Health,
	VariableApiAccessor Variables);
