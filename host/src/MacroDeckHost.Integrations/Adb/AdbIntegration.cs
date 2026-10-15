using MacroDeckHost.Integrations.Adb.Actions;
using MacroDeckHost.Integrations.Adb.Connections;
using MacroDeckHost.Integrations.Adb.Events;
using MacroDeckHost.Integrations.Adb.Providers;
using MacroDeckHost.Integrations.Adb.State;
using MacroDeckHost.Integrations.Adb.Variables;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Android;
using MacroDeck.Sdk.Batteries;
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
		IBatteryProvider,
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
	private readonly AdbBatteryProvider _batteries;
	private readonly TimeProvider _timeProvider;
	private readonly Lock _gate = new();

	private AdbEventEmitter? _eventEmitter;
	private IVariableApi? _variables;
	private IAndroidDeviceManager? _androidDevices;
	private AdbDeviceTracker? _tracker;

	public AdbIntegration()
		: this(TimeProvider.System)
	{
	}

	internal AdbIntegration(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
		Actions = AdbActions.Create(new AdbActionServices(() => _androidDevices,
			() => _tracker,
			_health,
			_variableAccessor));
		_batteries = new AdbBatteryProvider(() => _androidDevices, timeProvider);
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

	// Empty so battery readers show the integration's localized name instead of the variable provider's ADB label.
	string IBatteryProvider.ProviderName => string.Empty;

	Task IBatteryProvider.InitializeAsync(IBatteryProviderContext context, CancellationToken cancellationToken)
	{
		return _batteries.InitializeAsync(context, cancellationToken);
	}

	Task IBatteryProvider.ShutdownAsync(CancellationToken cancellationToken)
	{
		return _batteries.ShutdownAsync(cancellationToken);
	}

	public byte[] GetIcon()
	{
		return _icon;
	}

	public Task InitializeAsync(IIntegrationContext context)
	{
		_variables = context.Variables;
		_variableAccessor.Current = context.Variables;
		_variableAccessor.UserVariables = context.UserVariables;
		_eventEmitter = new AdbEventEmitter(context.Events);
		var tracker = new AdbDeviceTracker(context.AndroidDevices, _timeProvider);
		tracker.Changed += OnDeviceChanged;
		lock (_gate)
		{
			_tracker?.Dispose();
			_tracker = tracker;
		}

		_androidDevices = context.AndroidDevices;
		IsInitialized = true;
		return Task.CompletedTask;
	}

	public Task ShutdownAsync()
	{
		ReleaseTracker();
		_eventEmitter = null;
		_variables = null;
		_androidDevices = null;
		_variableAccessor.Current = null;
		_variableAccessor.UserVariables = null;
		IsInitialized = false;
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		ReleaseTracker();
		_batteries.Dispose();
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		return AdbVariables.ReadAsync(_androidDevices, _tracker, localId, cancellationToken);
	}

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		var manager = _androidDevices;
		if (manager is null)
		{
			return Task.FromResult<IReadOnlyList<IntegrationIssue>>([]);
		}

		var issues = new List<IntegrationIssue>();
		var enabled = manager.Access == AndroidDeviceAccess.Available;

		if (!enabled)
		{
			issues.Add(new IntegrationIssue
			{
				Id = DisabledIssueId,
				Title = AppStrings.Integrations.Adb.Issues.DisabledTitle(),
				Description = AppStrings.Integrations.Adb.Issues.DisabledDescription(),
				Severity = IntegrationIssueSeverity.Warning
			});
		}

		if (_health.LastFailure == AndroidDeviceErrorCode.AdbUnavailable)
		{
			issues.Add(new IntegrationIssue
			{
				Id = ExecutableNotFoundIssueId,
				Title = AppStrings.Integrations.Adb.Issues.ExecutableNotFoundTitle(),
				Description = AppStrings.Integrations.Adb.Issues.ExecutableNotFoundDescription(),
				Severity = IntegrationIssueSeverity.Error
			});
		}

		var devices = manager.Devices;
		if (enabled && devices.Count == 0)
		{
			issues.Add(new IntegrationIssue
			{
				Id = NoDevicesIssueId,
				Title = AppStrings.Integrations.Adb.Issues.NoDevicesTitle(),
				Description = AppStrings.Integrations.Adb.Issues.NoDevicesDescription(),
				Severity = IntegrationIssueSeverity.Info
			});
		}

		foreach (var device in devices.Where(device => device.State == AndroidDeviceState.Unauthorized))
		{
			issues.Add(new IntegrationIssue
			{
				Id = $"{UnauthorizedIssuePrefix}{device.Serial}",
				Title = AppStrings.Integrations.Adb.Issues.UnauthorizedTitle(device.Info.Model ?? device.Serial),
				Description = device.LacksUsbPermission
					? AppStrings.Integrations.Adb.Issues.NoPermissionsDescription()
					: AppStrings.Integrations.Adb.Issues.UnauthorizedDescription(),
				Severity = IntegrationIssueSeverity.Warning
			});
		}

		return Task.FromResult<IReadOnlyList<IntegrationIssue>>(issues);
	}

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(IssueResolution.Failed(AppStrings.Integrations.Issues.CannotResolveAutomatically()));
	}

	private void OnDeviceChanged(object? sender, AdbDeviceChange change)
	{
		try
		{
			_eventEmitter?.Publish(change);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Failed to publish a device event for {Serial}", change.Device.Serial);
		}

		var tracker = _tracker;
		var variables = _variables;
		if (tracker is not null && variables is not null)
		{
			_ = AdbDeviceVariableWriter.HandleAsync(variables, tracker.Devices, change, CancellationToken.None);
		}
	}

	private void ReleaseTracker()
	{
		AdbDeviceTracker? tracker;
		lock (_gate)
		{
			tracker = _tracker;
			_tracker = null;
		}

		if (tracker is not null)
		{
			tracker.Changed -= OnDeviceChanged;
			tracker.Dispose();
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
