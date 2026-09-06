using System.Collections.Concurrent;
using System.Diagnostics;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Adb;

public sealed class AdbManager : IAdbManager, IDisposable
{
	private static readonly TimeSpan _defaultCommandTimeout = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan _rebootCommandTimeout = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan _screenshotTimeout = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan _versionTimeout = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan _devicesListTimeout = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan _serverStartTimeout = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan _killServerTimeout = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan _propertyProbeTimeout = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan _propertyProbeRecencyWindow = TimeSpan.FromSeconds(60);

	private static readonly TimeSpan _disconnectedRetention = TimeSpan.FromMinutes(5);
	private const int MaxDisconnectedDevices = 32;

	private static readonly TimeSpan _shutdownCap = TimeSpan.FromMilliseconds(2500);
	private static readonly TimeSpan _shutdownTunnelPerDeviceTimeout = TimeSpan.FromMilliseconds(700);
	private static readonly TimeSpan _shutdownTunnelTotalBudget = TimeSpan.FromMilliseconds(1200);
	private static readonly TimeSpan _shutdownDrainBudget = TimeSpan.FromSeconds(1);

	private static readonly TimeSpan _staleSweepPerDeviceTimeout = TimeSpan.FromMilliseconds(700);
	private static readonly TimeSpan _staleSweepTotalBudget = TimeSpan.FromSeconds(2);

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IAdbProcessRunner _processRunner;
	private readonly AdbOwnershipMarker _ownershipMarker;
	private readonly AdbTunnelCoordinator _tunnelCoordinator;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly SemaphoreSlim _wake = new(0, 1);
	private readonly ConcurrentDictionary<string, string?> _manufacturerCache = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, AdbDeviceProperties> _propertiesCache = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, DateTimeOffset> _propertiesRequestedAt = new(StringComparer.Ordinal);

	/// <summary>
	/// Devices that answered the property probe with nothing usable. A device that has no `dumpsys` is
	/// not going to grow one while it stays plugged in, so probing it again every few seconds is work
	/// that cannot succeed - and on a minimal adbd it is not harmless: a jailbroken Car Thing dropped
	/// off the bus under the repeated shell sessions (issue #727).
	/// </summary>
	private readonly ConcurrentDictionary<string, byte> _propertiesUnavailable = new(StringComparer.Ordinal);

	private Snapshot _snapshot = new(AdbStatus.Disabled, []);
	private Dictionary<string, AdbDevice> _knownDevices = new(StringComparer.Ordinal);
	private AdbSettings? _currentSettings;
	private bool _serverStartedByMacroDeck;
	private bool _pendingStaleSweep;
	private readonly int _staleTunnelsCleaned;
	private readonly bool _previousShutdownWasUnclean;
	private bool _disposed;

	internal AdbManager(
		IServiceScopeFactory scopeFactory,
		IAdbProcessRunner processRunner,
		IMacroDeckPaths paths,
		IHostListenerState listenerState,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_scopeFactory = scopeFactory;
		_processRunner = processRunner;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<AdbManager>();

		_ownershipMarker = new AdbOwnershipMarker(paths, logger);
		_tunnelCoordinator = new AdbTunnelCoordinator(processRunner, listenerState, _ownershipMarker, logger);

		var priorState = _ownershipMarker.Read();
		if (priorState is not null)
		{
			_previousShutdownWasUnclean = true;
			_staleTunnelsCleaned = priorState.Tunnels.Count;
			_pendingStaleSweep = true;
		}
	}

	public AdbStatus Status => Volatile.Read(ref _snapshot).Status;

	public IReadOnlyList<AdbDevice> Devices => Volatile.Read(ref _snapshot).Devices;

	public event EventHandler<AdbDeviceChange>? DeviceChanged;

	public AdbDevice? ResolveDevice(string? serialOrDefault)
	{
		var devices = Devices;
		if (!string.IsNullOrWhiteSpace(serialOrDefault))
		{
			return devices.FirstOrDefault(device =>
				string.Equals(device.Serial, serialOrDefault, StringComparison.Ordinal));
		}

		var defaultSerial = Status.DefaultDeviceSerial;
		if (!string.IsNullOrWhiteSpace(defaultSerial))
		{
			return devices.FirstOrDefault(device =>
				string.Equals(device.Serial, defaultSerial, StringComparison.Ordinal));
		}

		var connected = devices.Where(device => device.State != AdbDeviceState.Disconnected).ToList();
		return connected.Count == 1 ? connected[0] : null;
	}

	public async Task<Result<AdbFailureCode>> ExecuteAsync(AdbCommand command, CancellationToken cancellationToken)
	{
		var status = Status;
		if (!status.Enabled)
		{
			return Result.Fail<AdbFailureCode>(AdbFailureCode.Disabled, "The adb integration is disabled.");
		}

		if (status.ResolvedExecutablePath is null)
		{
			return Result.Fail<AdbFailureCode>(AdbFailureCode.ExecutableNotFound,
				"The adb executable could not be located.");
		}

		var device = ResolveDevice(command.Serial);
		var deviceFailure = ValidateDeviceState(device, command.Serial);
		if (deviceFailure is not null)
		{
			return Result.Fail<AdbFailureCode>(deviceFailure.Value.Code, deviceFailure.Value.Message);
		}

		var built = AdbCommandBuilder.Build(command with { Serial = device!.Serial });
		if (!built.Success)
		{
			return Result.Fail<AdbFailureCode>(built.Error!.Value, built.ErrorMessage);
		}

		var timeout = command is AdbRebootCommand ? _rebootCommandTimeout : _defaultCommandTimeout;
		var result = await _processRunner.RunAsync(status.ResolvedExecutablePath,
			built.Data!,
			timeout,
			cancellationToken);

		if (!result.Started)
		{
			return Result.Fail<AdbFailureCode>(AdbFailureCode.CommandFailed, "adb could not be started.");
		}

		if (result.TimedOut)
		{
			return Result.Fail<AdbFailureCode>(AdbFailureCode.Timeout, "The adb command timed out.");
		}

		if (result.ExitCode != 0)
		{
			return Result.Fail<AdbFailureCode>(AdbFailureCode.CommandFailed, result.StandardError.Trim());
		}

		return Result.Ok<AdbFailureCode>();
	}

	public async Task<Result<string, AdbFailureCode>> QueryAsync(
		AdbQueryCommand command,
		CancellationToken cancellationToken)
	{
		var status = Status;
		if (!status.Enabled)
		{
			return Result.Fail<string, AdbFailureCode>(AdbFailureCode.Disabled, "The adb integration is disabled.");
		}

		if (status.ResolvedExecutablePath is null)
		{
			return Result.Fail<string, AdbFailureCode>(AdbFailureCode.ExecutableNotFound,
				"The adb executable could not be located.");
		}

		var device = ResolveDevice(command.Serial);
		var deviceFailure = ValidateDeviceState(device, command.Serial);
		if (deviceFailure is not null)
		{
			return Result.Fail<string, AdbFailureCode>(deviceFailure.Value.Code, deviceFailure.Value.Message);
		}

		var built = AdbCommandBuilder.Build(command with { Serial = device!.Serial });
		if (!built.Success)
		{
			return Result.Fail<string, AdbFailureCode>(built.Error!.Value, built.ErrorMessage);
		}

		var result = await _processRunner.RunAsync(status.ResolvedExecutablePath,
			built.Data!,
			_defaultCommandTimeout,
			cancellationToken);

		if (!result.Started)
		{
			return Result.Fail<string, AdbFailureCode>(AdbFailureCode.CommandFailed, "adb could not be started.");
		}

		if (result.TimedOut)
		{
			return Result.Fail<string, AdbFailureCode>(AdbFailureCode.Timeout, "The adb command timed out.");
		}

		if (result.ExitCode != 0)
		{
			return Result.Fail<string, AdbFailureCode>(AdbFailureCode.CommandFailed, result.StandardError.Trim());
		}

		return Result.Ok<string, AdbFailureCode>(result.StandardOutput);
	}

	public async Task<Result<byte[], AdbFailureCode>> CaptureScreenshotAsync(
		string? serialOrDefault,
		CancellationToken cancellationToken)
	{
		var status = Status;
		if (!status.Enabled)
		{
			return Result.Fail<byte[], AdbFailureCode>(AdbFailureCode.Disabled, "The adb integration is disabled.");
		}

		if (status.ResolvedExecutablePath is null)
		{
			return Result.Fail<byte[], AdbFailureCode>(AdbFailureCode.ExecutableNotFound,
				"The adb executable could not be located.");
		}

		var device = ResolveDevice(serialOrDefault);
		var deviceFailure = ValidateDeviceState(device, serialOrDefault);
		if (deviceFailure is not null)
		{
			return Result.Fail<byte[], AdbFailureCode>(deviceFailure.Value.Code, deviceFailure.Value.Message);
		}

		var built = AdbCommandBuilder.Build(new AdbScreenshotCommand(device!.Serial));
		if (!built.Success)
		{
			return Result.Fail<byte[], AdbFailureCode>(built.Error!.Value, built.ErrorMessage);
		}

		var result = await _processRunner.RunBinaryAsync(status.ResolvedExecutablePath,
			built.Data!,
			_screenshotTimeout,
			cancellationToken);

		if (!result.Started)
		{
			return Result.Fail<byte[], AdbFailureCode>(AdbFailureCode.CommandFailed, "adb could not be started.");
		}

		if (result.TimedOut)
		{
			return Result.Fail<byte[], AdbFailureCode>(AdbFailureCode.Timeout, "Capturing the screenshot timed out.");
		}

		if (result.ExitCode != 0)
		{
			return Result.Fail<byte[], AdbFailureCode>(AdbFailureCode.CommandFailed, result.StandardError.Trim());
		}

		return Result.Ok<byte[], AdbFailureCode>(result.StandardOutput);
	}

	public Task<AdbDeviceProperties?> GetPropertiesAsync(string? serialOrDefault, CancellationToken cancellationToken)
	{
		var device = ResolveDevice(serialOrDefault);
		if (device is null)
		{
			return Task.FromResult<AdbDeviceProperties?>(null);
		}

		_propertiesRequestedAt[device.Serial] = _timeProvider.GetUtcNow();

		return Task.FromResult(_propertiesCache.GetValueOrDefault(device.Serial));
	}

	public async Task ApplySettingsAsync(CancellationToken cancellationToken)
	{
		_currentSettings = await LoadSettingsAsync();
		await ReconcileAsync(cancellationToken);
		SignalWake();
	}

	public async Task RefreshNowAsync(CancellationToken cancellationToken)
	{
		await ReconcileAsync(cancellationToken);
		SignalWake();
	}

	public async Task<Result<AdbFailureCode>> RestartServerAsync(CancellationToken cancellationToken)
	{
		var status = Status;
		if (!status.Enabled)
		{
			return Result.Fail<AdbFailureCode>(AdbFailureCode.Disabled, "The adb integration is disabled.");
		}

		if (status.ResolvedExecutablePath is null)
		{
			return Result.Fail<AdbFailureCode>(AdbFailureCode.ExecutableNotFound,
				"The adb executable could not be located.");
		}

		AdbProcessResult startResult;
		await _gate.WaitAsync(cancellationToken);
		try
		{
			await _processRunner.RunAsync(status.ResolvedExecutablePath,
				["kill-server"],
				_killServerTimeout,
				cancellationToken);
			startResult = await _processRunner.RunAsync(status.ResolvedExecutablePath,
				["start-server"],
				_serverStartTimeout,
				cancellationToken);
			_serverStartedByMacroDeck = true;

			await ReconcileCoreAsync(cancellationToken);
		}
		finally
		{
			_gate.Release();
		}

		if (startResult is not { Started: true, TimedOut: false, ExitCode: 0 })
		{
			return Result.Fail<AdbFailureCode>(AdbFailureCode.ServerUnreachable, "Failed to restart the adb server.");
		}

		return Result.Ok<AdbFailureCode>();
	}

	public async Task ShutdownAsync()
	{
		if (_disposed)
		{
			return;
		}

		var stopwatch = Stopwatch.StartNew();

		// Deliberately not gate-guarded: waiting for an in-flight reconcile pass could itself take up to
		// its own command timeout, blowing the cap below. DrainAsync kills whatever that pass is still
		// running anyway, so racing it is fine.
		try
		{
			await _tunnelCoordinator.RemoveOwnedTunnelsAsync(_shutdownTunnelPerDeviceTimeout,
				_shutdownTunnelTotalBudget,
				CancellationToken.None);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Failed to remove owned adb reverse tunnels during shutdown");
		}

		var remaining = _shutdownCap - stopwatch.Elapsed;
		var drainBudget = Clamp(remaining < _shutdownDrainBudget ? remaining : _shutdownDrainBudget,
			TimeSpan.Zero,
			_shutdownDrainBudget);
		await _processRunner.DrainAsync(drainBudget);

		_ownershipMarker.Delete();
	}

	internal async Task ProbePropertiesNowAsync(CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			await ProbePropertiesCoreAsync(cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}

	internal async Task WaitForEnabledAsync(CancellationToken cancellationToken)
	{
		if (Status.Enabled)
		{
			return;
		}

		await _wake.WaitAsync(cancellationToken);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_gate.Dispose();
		_wake.Dispose();
	}

	private async Task ReconcileAsync(CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			await ReconcileCoreAsync(cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task ReconcileCoreAsync(CancellationToken cancellationToken)
	{
		_currentSettings ??= await LoadSettingsAsync();
		var settings = _currentSettings;

		if (!settings.Enabled)
		{
			_tunnelCoordinator.SetExecutablePath(null);
			SetSnapshot(AdbStatus.Disabled, []);
			return;
		}

		var (resolvedPath, source) = AdbExecutableLocator.Resolve(settings.ExecutablePath,
			File.Exists,
			Environment.GetEnvironmentVariable,
			OperatingSystem.IsWindows(),
			OperatingSystem.IsMacOS(),
			OperatingSystem.IsLinux());

		if (resolvedPath is null)
		{
			_tunnelCoordinator.SetExecutablePath(null);
			var message = !string.IsNullOrWhiteSpace(settings.ExecutablePath)
				? $"The configured adb executable was not found at '{settings.ExecutablePath}'."
				: "The adb executable could not be located.";

			SetSnapshot(new AdbStatus(true,
					settings.UsbConnectionsEnabled,
					true,
					null,
					AdbExecutableSource.None,
					null,
					false,
					_serverStartedByMacroDeck,
					settings.DefaultDeviceSerial,
					AdbFailureCode.ExecutableNotFound,
					message,
					_timeProvider.GetUtcNow(),
					_previousShutdownWasUnclean,
					_staleTunnelsCleaned),
				[]);
			return;
		}

		_tunnelCoordinator.SetExecutablePath(resolvedPath);

		if (_pendingStaleSweep)
		{
			await _tunnelCoordinator.RemoveOwnedTunnelsAsync(_staleSweepPerDeviceTimeout,
				_staleSweepTotalBudget,
				cancellationToken);
			_ownershipMarker.Delete();
			_pendingStaleSweep = false;
		}

		var versionResult
			= await _processRunner.RunAsync(resolvedPath, ["version"], _versionTimeout, cancellationToken);
		var version = versionResult is { Started: true, TimedOut: false, ExitCode: 0 }
			? AdbPropertyParsers.ParseSingleLineProperty(versionResult.StandardOutput)
			: null;

		var devicesResult
			= await _processRunner.RunAsync(resolvedPath, ["devices", "-l"], _devicesListTimeout, cancellationToken);
		var serverReachable = devicesResult is { Started: true, TimedOut: false, ExitCode: 0 };

		if (!serverReachable)
		{
			var startResult = await _processRunner.RunAsync(resolvedPath,
				["start-server"],
				_serverStartTimeout,
				cancellationToken);
			if (startResult is { Started: true, TimedOut: false, ExitCode: 0 })
			{
				_serverStartedByMacroDeck = true;
				devicesResult = await _processRunner.RunAsync(resolvedPath,
					["devices", "-l"],
					_devicesListTimeout,
					cancellationToken);
				serverReachable = devicesResult is { Started: true, TimedOut: false, ExitCode: 0 };
			}
		}

		var now = _timeProvider.GetUtcNow();
		var parsed = serverReachable ? AdbDeviceListParser.Parse(devicesResult.StandardOutput, now) : [];

		var enriched = new List<AdbDevice>(parsed.Count);
		foreach (var device in parsed)
		{
			if (device.State == AdbDeviceState.Device)
			{
				var manufacturer = await GetOrFetchManufacturerAsync(resolvedPath, device.Serial, cancellationToken);
				enriched.Add(device with { Manufacturer = manufacturer });
			}
			else
			{
				enriched.Add(device);
			}
		}

		var merged = MergeWithRetained(enriched, now);

		if (settings.UsbConnectionsEnabled)
		{
			var authorizedOnline = merged.Where(device => device.State == AdbDeviceState.Device).ToList();
			var tunnels = await _tunnelCoordinator.ReconcileAsync(authorizedOnline, cancellationToken);
			merged = merged
				.Select(device => tunnels.TryGetValue(device.Serial, out var tunnel)
					? device with { Tunnel = tunnel }
					: device)
				.ToList();
		}

		DiffAndRaise(_knownDevices, merged);
		_knownDevices = merged.ToDictionary(device => device.Serial, StringComparer.Ordinal);

		SetSnapshot(new AdbStatus(true,
				settings.UsbConnectionsEnabled,
				true,
				resolvedPath,
				source,
				version,
				serverReachable,
				_serverStartedByMacroDeck,
				settings.DefaultDeviceSerial,
				serverReachable ? null : AdbFailureCode.ServerUnreachable,
				serverReachable ? null : "The adb server could not be reached.",
				serverReachable ? null : now,
				_previousShutdownWasUnclean,
				_staleTunnelsCleaned),
			merged);
	}

	private async Task ProbePropertiesCoreAsync(CancellationToken cancellationToken)
	{
		var status = Status;
		if (!status.Enabled || status.ResolvedExecutablePath is null)
		{
			return;
		}

		var now = _timeProvider.GetUtcNow();
		var wantedSerials = _propertiesRequestedAt
			.Where(pair => now - pair.Value <= _propertyProbeRecencyWindow)
			.Select(pair => pair.Key)
			.ToList();

		var onlineSerials = Devices
			.Where(device => device.State == AdbDeviceState.Device)
			.Select(device => device.Serial)
			.ToHashSet(StringComparer.Ordinal);

		// A device that has left takes its "no properties here" verdict with it; the next session gets
		// one fresh probe rather than inheriting the last one's.
		foreach (var serial in _propertiesUnavailable.Keys.Where(serial => !onlineSerials.Contains(serial)))
		{
			_propertiesUnavailable.TryRemove(serial, out _);
		}

		foreach (var serial in wantedSerials)
		{
			if (!onlineSerials.Contains(serial) || _propertiesUnavailable.ContainsKey(serial))
			{
				continue;
			}

			cancellationToken.ThrowIfCancellationRequested();
			await ProbeDevicePropertiesAsync(status.ResolvedExecutablePath, serial, cancellationToken);
		}
	}

	private async Task ProbeDevicePropertiesAsync(string executablePath,
		string serial,
		CancellationToken cancellationToken)
	{
		var battery = await _processRunner.RunAsync(executablePath,
			["-s", serial, "shell", "dumpsys battery"],
			_propertyProbeTimeout,
			cancellationToken);
		var power = await _processRunner.RunAsync(executablePath,
			["-s", serial, "shell", "dumpsys power"],
			_propertyProbeTimeout,
			cancellationToken);
		var window = await _processRunner.RunAsync(executablePath,
			["-s", serial, "shell", "dumpsys window"],
			_propertyProbeTimeout,
			cancellationToken);

		var batteryLevel = battery is { Started: true, ExitCode: 0 }
			? AdbPropertyParsers.ParseBatteryLevel(battery.StandardOutput)
			: null;
		var screenOn = power is { Started: true, ExitCode: 0 }
			? AdbPropertyParsers.ParseScreenOn(power.StandardOutput)
			: null;
		var locked = window is { Started: true, ExitCode: 0 }
			? AdbPropertyParsers.ParseLocked(window.StandardOutput)
			: null;
		var foregroundPackage = window is { Started: true, ExitCode: 0 }
			? AdbPropertyParsers.ParseForegroundPackage(window.StandardOutput)
			: null;

		// Every probe command failing means this is not a device that answers them at all - a Linux
		// appliance rather than Android. Record that once and stop asking for the rest of the session.
		if (battery is not { Started: true, ExitCode: 0 } &&
			power is not { Started: true, ExitCode: 0 } &&
			window is not { Started: true, ExitCode: 0 })
		{
			_propertiesUnavailable[serial] = 0;
		}

		_propertiesCache[serial] =
			new AdbDeviceProperties(batteryLevel, screenOn, locked, foregroundPackage, _timeProvider.GetUtcNow());
	}

	private async Task<string?> GetOrFetchManufacturerAsync(string executablePath,
		string serial,
		CancellationToken cancellationToken)
	{
		if (_manufacturerCache.TryGetValue(serial, out var cached))
		{
			return cached;
		}

		var result = await _processRunner.RunAsync(executablePath,
			["-s", serial, "shell", "getprop ro.product.manufacturer"],
			_propertyProbeTimeout,
			cancellationToken);

		var manufacturer = result is { Started: true, TimedOut: false, ExitCode: 0 }
			? AdbPropertyParsers.ParseSingleLineProperty(result.StandardOutput)
			: null;

		_manufacturerCache[serial] = manufacturer;
		return manufacturer;
	}

	private List<AdbDevice> MergeWithRetained(IReadOnlyList<AdbDevice> current, DateTimeOffset now)
	{
		var currentSerials = current.Select(device => device.Serial).ToHashSet(StringComparer.Ordinal);
		var merged = new List<AdbDevice>(current);

		foreach (var (serial, previous) in _knownDevices)
		{
			if (currentSerials.Contains(serial))
			{
				continue;
			}

			if (previous.State == AdbDeviceState.Disconnected)
			{
				if (now - previous.LastSeenAt <= _disconnectedRetention)
				{
					merged.Add(previous);
				}

				continue;
			}

			merged.Add(previous with { State = AdbDeviceState.Disconnected, Tunnel = null });
		}

		// Bounded count applies only to disconnected devices; a currently reachable device is never
		// hidden just because many others have recently disconnected.
		var disconnectedBySerial = merged
			.Where(device => device.State == AdbDeviceState.Disconnected)
			.OrderByDescending(device => device.LastSeenAt)
			.Skip(MaxDisconnectedDevices)
			.Select(device => device.Serial)
			.ToHashSet(StringComparer.Ordinal);
		if (disconnectedBySerial.Count > 0)
		{
			merged.RemoveAll(device => disconnectedBySerial.Contains(device.Serial));
		}

		return merged;
	}

	private void DiffAndRaise(Dictionary<string, AdbDevice> previous, IReadOnlyList<AdbDevice> current)
	{
		foreach (var device in current)
		{
			if (!previous.TryGetValue(device.Serial, out var previousDevice))
			{
				Raise(AdbDeviceChangeKind.Connected, device, null);
				continue;
			}

			if (previousDevice.State == device.State)
			{
				continue;
			}

			if (previousDevice.State == AdbDeviceState.Disconnected)
			{
				Raise(AdbDeviceChangeKind.Online, device, previousDevice.State);
				continue;
			}

			if (device.State == AdbDeviceState.Disconnected)
			{
				Raise(AdbDeviceChangeKind.Disconnected, device, previousDevice.State);
				continue;
			}

			switch (device.State)
			{
				case AdbDeviceState.Device:
					Raise(AdbDeviceChangeKind.Authorized, device, previousDevice.State);
					break;
				case AdbDeviceState.Unauthorized:
					Raise(AdbDeviceChangeKind.Unauthorized, device, previousDevice.State);
					break;
				case AdbDeviceState.Offline:
					Raise(AdbDeviceChangeKind.Offline, device, previousDevice.State);
					break;
			}
		}
	}

	private void Raise(AdbDeviceChangeKind kind, AdbDevice device, AdbDeviceState? previousState)
	{
		var handler = DeviceChanged;
		if (handler is null)
		{
			return;
		}

		var change = new AdbDeviceChange(kind, device, previousState);
		foreach (var subscriber in handler.GetInvocationList())
		{
			try
			{
				((EventHandler<AdbDeviceChange>)subscriber).Invoke(this, change);
			}
			catch (Exception ex)
			{
				_logger.Warning(ex, "A DeviceChanged subscriber threw for {Serial}", device.Serial);
			}
		}
	}

	private async Task<AdbSettings> LoadSettingsAsync()
	{
		using var scope = _scopeFactory.CreateScope();
		var preferenceService = scope.ServiceProvider.GetRequiredService<IAppPreferenceService>();
		return await preferenceService.GetAdb();
	}

	private void SetSnapshot(AdbStatus status, IReadOnlyList<AdbDevice> devices)
		=> Volatile.Write(ref _snapshot, new Snapshot(status, devices));

	private void SignalWake()
	{
		try
		{
			_wake.Release();
		}
		catch (SemaphoreFullException)
		{
		}
	}

	private static (AdbFailureCode Code, string Message)? ValidateDeviceState(AdbDevice? device,
		string? requestedSerial)
	{
		if (device is null)
		{
			return (AdbFailureCode.DeviceNotFound,
				string.IsNullOrWhiteSpace(requestedSerial)
					? "No default device is configured and no single device is connected."
					: $"No device with serial '{requestedSerial}' is known.");
		}

		return device.State switch
		{
			AdbDeviceState.Device => null,
			AdbDeviceState.Unauthorized => (AdbFailureCode.DeviceUnauthorized,
				$"Device '{device.Serial}' has not authorized this computer."),
			_ => (AdbFailureCode.DeviceOffline, $"Device '{device.Serial}' is not currently reachable.")
		};
	}

	private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
	{
		if (value < min)
		{
			return min;
		}

		return value > max ? max : value;
	}

	private sealed record Snapshot(AdbStatus Status, IReadOnlyList<AdbDevice> Devices);
}
