using MacroDeckHost.Integrations.Adb.Actions;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.Adb;

[MacroDeckIntegration(EnabledByDefault = false)]
public sealed class AdbIntegration
	: IIntegration,
		IVariableProvider,
		IEventProvider,
		IIntegrationIconProvider,
		IIntegrationIssueProvider,
		IAdbGatewayConsumer,
		IDisposable
{
	public const string IntegrationId = "app.macro-deck.adb";

	internal const string DisabledIssueId = "adb-disabled";
	internal const string ExecutableNotFoundIssueId = "adb-executable-not-found";
	internal const string NoDevicesIssueId = "no-devices-connected";
	internal const string UnauthorizedIssuePrefix = "device-unauthorized:";

	private static readonly ILogger _logger = IntegrationLog.For<AdbIntegration>(IntegrationId);
	private static readonly byte[] _icon = LoadIcon();

	private readonly VariableApiAccessor _variableAccessor = new();
	private readonly AdbHealthTracker _health = new();

	private IAdbGateway? _gateway;
	private AdbEventEmitter? _eventEmitter;
	private IVariableApi? _variables;
	private bool _subscribed;

	public AdbIntegration()
	{
		Actions = AdbActions.Create(() => _gateway, _health, _variableAccessor);
	}

	public string Id => IntegrationId;

	public LocalizedText Name => AppStrings.Integrations.Adb.Name();

	public string Version => "1.0.0";

	public bool IsInitialized { get; private set; }

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public string IconMimeType => "image/svg+xml";

	public string ProviderName => "ADB";

	public IReadOnlyList<EventDefinition> EventDefinitions => AdbEventDefinitions.All;

	public IReadOnlyList<VariableDefinition> Variables => AdbVariables.All;

	public IReadOnlyList<VariableDefinition> DeclaredVariables { get; } =
		[.. AdbVariables.All, .. AdbVariables.DeviceTemplates];

	public bool VariablesDependOnConfiguration => true;

	public void UseGateway(IAdbGateway gateway) => _gateway = gateway;

	public byte[] GetIcon() => _icon;

	public Task InitializeAsync(IIntegrationContext context)
	{
		_variables = context.Variables;
		_variableAccessor.Current = context.Variables;
		_eventEmitter = new AdbEventEmitter(context.Events);

		if (_gateway is { } gateway)
		{
			gateway.DeviceChanged -= OnDeviceChanged;
			gateway.DeviceChanged += OnDeviceChanged;
			_subscribed = true;
		}

		IsInitialized = true;
		return Task.CompletedTask;
	}

	public Task ShutdownAsync()
	{
		if (_subscribed && _gateway is { } gateway)
		{
			gateway.DeviceChanged -= OnDeviceChanged;
		}

		_subscribed = false;
		_eventEmitter = null;
		_variables = null;
		_variableAccessor.Current = null;
		IsInitialized = false;
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		if (_subscribed && _gateway is { } gateway)
		{
			gateway.DeviceChanged -= OnDeviceChanged;
			_subscribed = false;
		}
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
		=> AdbVariables.ReadAsync(_gateway, localId, cancellationToken);

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		var gateway = _gateway;
		if (gateway is null)
		{
			return Task.FromResult<IReadOnlyList<IntegrationIssue>>([]);
		}

		var issues = new List<IntegrationIssue>();

		if (!gateway.IsEnabled)
		{
			issues.Add(new IntegrationIssue
			{
				Id = DisabledIssueId,
				Title = AppStrings.Integrations.Adb.Issues.DisabledTitle(),
				Description = AppStrings.Integrations.Adb.Issues.DisabledDescription(),
				Severity = IntegrationIssueSeverity.Warning
			});
		}

		if (_health.LastFailure == AdbGatewayFailureCode.ExecutableNotFound)
		{
			issues.Add(new IntegrationIssue
			{
				Id = ExecutableNotFoundIssueId,
				Title = AppStrings.Integrations.Adb.Issues.ExecutableNotFoundTitle(),
				Description = AppStrings.Integrations.Adb.Issues.ExecutableNotFoundDescription(),
				Severity = IntegrationIssueSeverity.Error
			});
		}

		if (gateway.IsEnabled && !gateway.Devices.Any(device => device.State != AdbGatewayDeviceState.Disconnected))
		{
			issues.Add(new IntegrationIssue
			{
				Id = NoDevicesIssueId,
				Title = AppStrings.Integrations.Adb.Issues.NoDevicesTitle(),
				Description = AppStrings.Integrations.Adb.Issues.NoDevicesDescription(),
				Severity = IntegrationIssueSeverity.Info
			});
		}

		foreach (var device in gateway.Devices.Where(device =>
			device.State is AdbGatewayDeviceState.Unauthorized or AdbGatewayDeviceState.NoPermissions))
		{
			issues.Add(new IntegrationIssue
			{
				Id = $"{UnauthorizedIssuePrefix}{device.Serial}",
				Title = AppStrings.Integrations.Adb.Issues.UnauthorizedTitle(device: device.Model ?? device.Serial),
				Description = device.State == AdbGatewayDeviceState.NoPermissions
					? AppStrings.Integrations.Adb.Issues.NoPermissionsDescription()
					: AppStrings.Integrations.Adb.Issues.UnauthorizedDescription(),
				Severity = IntegrationIssueSeverity.Warning
			});
		}

		return Task.FromResult<IReadOnlyList<IntegrationIssue>>(issues);
	}

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
		=> Task.FromResult(IssueResolution.Failed(AppStrings.Integrations.Issues.CannotResolveAutomatically()));

	private void OnDeviceChanged(object? sender, AdbGatewayDeviceChange change)
	{
		try
		{
			_eventEmitter?.Publish(change);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Failed to publish a device event for {Serial}", change.Device.Serial);
		}

		var gateway = _gateway;
		var variables = _variables;
		if (gateway is not null && variables is not null)
		{
			_ = AdbDeviceVariableWriter.HandleAsync(variables, gateway, change, CancellationToken.None);
		}
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(AdbIntegration).Assembly;
		var name = assembly.GetManifestResourceNames().First(n => n.EndsWith("adb-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
