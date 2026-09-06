using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Adb.Actions;

internal sealed class AdbActionDefinition : IActionDefinition
{
	internal const string DeviceParameterName = "device";
	internal static readonly LocalizedText DisabledMessage = AppStrings.Integrations.Adb.Errors.AdbDisabled();

	private readonly Func<IAdbGateway?> _resolveGateway;
	private readonly AdbHealthTracker _health;

	private readonly Func<IAdbGateway, string?, IReadOnlyDictionary<string, object>, CancellationToken,
		Task<AdbGatewayResult>> _command;

	public AdbActionDefinition(
		Func<IAdbGateway?> resolveGateway,
		AdbHealthTracker health,
		string id,
		LocalizedText name,
		LocalizedText description,
		LocalizedText deviceDescription,
		IReadOnlyList<ActionParameter> extraParameters,
		Func<IAdbGateway, string?, IReadOnlyDictionary<string, object>, CancellationToken, Task<AdbGatewayResult>>
			command)
	{
		_resolveGateway = resolveGateway;
		_health = health;
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

	public IActionExecutor CreateExecutor() => new Executor(this);

	internal static ActionParameter DeviceParameter(LocalizedText description)
		=> ActionParameter.DynamicChoice(DeviceParameterName,
			label: AppStrings.Integrations.Adb.Params.DeviceLabel(),
			description: description,
			optionsSourceId: AdbOptionsSourceIds.Devices,
			placeholder: AppStrings.Integrations.Adb.Params.DeviceDefaultPlaceholder(),
			required: false);

	internal static string? ReadDevice(IReadOnlyDictionary<string, object> parameters)
		=> parameters.TryGetValue(DeviceParameterName, out var value) ? value as string : null;

	internal static ActionResult MapResult(AdbGatewayResult result)
	{
		if (result.Success)
		{
			return ActionResult.Success();
		}

		var errorCode = result.Failure switch
		{
			AdbGatewayFailureCode.Disabled => ActionErrorCodes.Unavailable,
			AdbGatewayFailureCode.ExecutableNotFound => ActionErrorCodes.Unavailable,
			AdbGatewayFailureCode.ServerUnreachable => ActionErrorCodes.NotConnected,
			AdbGatewayFailureCode.DeviceNotFound => ActionErrorCodes.NotFound,
			AdbGatewayFailureCode.DeviceUnauthorized => ActionErrorCodes.PermissionDenied,
			AdbGatewayFailureCode.DeviceOffline => ActionErrorCodes.NotConnected,
			AdbGatewayFailureCode.Timeout => ActionErrorCodes.Timeout,
			AdbGatewayFailureCode.InvalidParameter => ActionErrorCodes.InvalidParameter,
			AdbGatewayFailureCode.CommandFailed => ActionErrorCodes.ProviderError,
			AdbGatewayFailureCode.Unsupported => ActionErrorCodes.Unavailable,
			_ => ActionErrorCodes.ProviderError
		};

		return ActionResult.Failed(errorCode,
			result.Message is { } message ? message : AppStrings.Integrations.Adb.Errors.CommandFailed());
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
			var gateway = _owner._resolveGateway();
			if (gateway is null || !gateway.IsEnabled)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, DisabledMessage);
			}

			var device = ReadDevice(context.Parameters);
			var result = await _owner._command(gateway, device, context.Parameters, context.CancellationToken);
			_owner._health.Record(result);
			return MapResult(result);
		}
	}
}
