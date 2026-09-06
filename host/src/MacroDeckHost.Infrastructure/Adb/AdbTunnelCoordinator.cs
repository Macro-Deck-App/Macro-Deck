using System.Diagnostics;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Configuration;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Adb;

internal sealed class AdbTunnelCoordinator
{
	private static readonly TimeSpan _reverseCallTimeout = TimeSpan.FromSeconds(5);

	private readonly IAdbProcessRunner _processRunner;
	private readonly IHostListenerState _listenerState;
	private readonly AdbOwnershipMarker _ownershipMarker;
	private readonly ILogger _logger;

	private volatile string? _executablePath;

	private int _warnedListenerUnavailable;

	/// <summary>
	/// What has already been settled per device serial, so a reconcile pass touches a device only when
	/// there is something to do. Entries are dropped the moment a serial leaves the device list, which
	/// is the same event that ends the adb transport a reverse mapping lives in.
	/// </summary>
	private readonly Dictionary<string, DeviceTunnelState> _deviceState = new(StringComparer.Ordinal);

	/// <summary>Upper bound on the back-off after repeated failures, in reconcile cycles.</summary>
	private const int MaxCyclesToSkip = 32;

	public AdbTunnelCoordinator(
		IAdbProcessRunner processRunner,
		IHostListenerState listenerState,
		AdbOwnershipMarker ownershipMarker,
		ILogger logger)
	{
		_processRunner = processRunner;
		_listenerState = listenerState;
		_ownershipMarker = ownershipMarker;
		_logger = logger.ForContext<AdbTunnelCoordinator>();
	}

	public void SetExecutablePath(string? executablePath) => _executablePath = executablePath;

	public async Task<IReadOnlyDictionary<string, AdbTunnel>> ReconcileAsync(
		IReadOnlyList<AdbDevice> devices,
		CancellationToken cancellationToken)
	{
		var results = new Dictionary<string, AdbTunnel>(StringComparer.Ordinal);
		var executablePath = _executablePath;
		if (string.IsNullOrEmpty(executablePath))
		{
			return results;
		}

		if (!_listenerState.PublicListenerAvailable)
		{
			if (Interlocked.Exchange(ref _warnedListenerUnavailable, 1) == 0)
			{
				_logger.Warning("Public listener unavailable; no adb reverse tunnels will be created until it is");
			}

			return results;
		}

		// The host-side port of every reverse tunnel is a *public* listener and nothing else: never
		// LoopbackPort, never a constant. An `adb reverse` connection reaches the host from the local
		// adb server, so its remote address is loopback; on the loopback listener,
		// LoopbackConnection.IsTrusted (host/src/MacroDeckHost/Auth/LoopbackConnection.cs) grants that
		// caller a synthetic admin principal with no authentication. Pointing a reverse tunnel there
		// would hand an unauthenticated phone full admin.
		//
		// Which public listener comes from PublicEndpointSet.LocalClientEndpoint rather than being
		// decided here: plain HTTP while one exists (the companion app then needs no certificate
		// trust), the HTTPS listener in replace mode, where it is the only public listener there is.
		var publicPort = _listenerState.PublicEndpoints.LocalClientEndpoint!.Value.Port;

		ForgetDevicesThatLeft(devices);

		foreach (var device in devices)
		{
			if (device.State != AdbDeviceState.Device)
			{
				continue;
			}

			cancellationToken.ThrowIfCancellationRequested();

			var state = StateFor(device.Serial);

			// A reverse mapping lives in the device's own adb transport and dies with it, so while a
			// device stays connected there is nothing to re-check. Listing them on every pass instead
			// meant talking to every attached device several times a minute forever - which a minimal
			// adbd does not necessarily survive: a Car Thing answered `reverse --list` with a protocol
			// fault and dropped off the bus, rebooted, and was hammered again three seconds later.
			if (state.Established is { Established: true } established && state.PublicPort == publicPort)
			{
				results[device.Serial] = established;
				continue;
			}

			if (state.CyclesToSkip > 0)
			{
				state.CyclesToSkip--;
				if (state.LastFailure is { } lastFailure)
				{
					results[device.Serial] = lastFailure;
				}

				continue;
			}

			var tunnel = await ReconcileDeviceAsync(executablePath, device.Serial, publicPort, cancellationToken);
			results[device.Serial] = tunnel;
			RecordOutcome(state, tunnel, publicPort);
		}

		return results;
	}

	private void ForgetDevicesThatLeft(IReadOnlyList<AdbDevice> devices)
	{
		var present = devices.Select(device => device.Serial).ToHashSet(StringComparer.Ordinal);
		foreach (var serial in _deviceState.Keys.Where(serial => !present.Contains(serial)).ToList())
		{
			_deviceState.Remove(serial);
		}
	}

	private DeviceTunnelState StateFor(string serial)
	{
		if (!_deviceState.TryGetValue(serial, out var state))
		{
			state = new DeviceTunnelState();
			_deviceState[serial] = state;
		}

		return state;
	}

	private static void RecordOutcome(DeviceTunnelState state, AdbTunnel tunnel, int publicPort)
	{
		if (tunnel.Established)
		{
			state.Established = tunnel;
			state.PublicPort = publicPort;
			state.LastFailure = null;
			state.FailureStreak = 0;
			state.CyclesToSkip = 0;
			return;
		}

		// Retrying a failing device on every pass is what turns one bad interaction into a loop the
		// device never gets out of. Back off instead, and reset the moment it works.
		state.Established = null;
		state.LastFailure = tunnel;
		state.FailureStreak++;
		state.CyclesToSkip = Math.Min(1 << Math.Min(state.FailureStreak, 5), MaxCyclesToSkip);
	}

	private sealed class DeviceTunnelState
	{
		public AdbTunnel? Established { get; set; }

		public AdbTunnel? LastFailure { get; set; }

		public int PublicPort { get; set; }

		public int FailureStreak { get; set; }

		public int CyclesToSkip { get; set; }
	}

	public async Task RemoveOwnedTunnelsAsync(TimeSpan perDeviceTimeout,
		TimeSpan totalBudget,
		CancellationToken cancellationToken)
	{
		var executablePath = _executablePath;
		var state = _ownershipMarker.Read();
		if (string.IsNullOrEmpty(executablePath) || state is null || state.Tunnels.Count == 0)
		{
			return;
		}

		var stopwatch = Stopwatch.StartNew();
		foreach (var tunnel in state.Tunnels)
		{
			if (stopwatch.Elapsed >= totalBudget)
			{
				break;
			}

			try
			{
				// Raced against its own timeout rather than just passed one: RunAsync must already honor
				// it, but this method's "isolating failures per device, stopping when the budget is
				// exhausted" contract must not depend on that promise holding for every possible caller.
				var runTask = _processRunner.RunAsync(executablePath,
					["-s", tunnel.Serial, "reverse", "--remove", "tcp:" + tunnel.DevicePort],
					perDeviceTimeout,
					cancellationToken);
				await Task.WhenAny(runTask, Task.Delay(perDeviceTimeout, cancellationToken));
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.Debug(ex, "Failed to remove owned adb reverse tunnel for {Serial}", tunnel.Serial);
			}
		}
	}

	private async Task<AdbTunnel> ReconcileDeviceAsync(
		string executablePath,
		string serial,
		int publicPort,
		CancellationToken cancellationToken)
	{
		var existing = await ListMappingsAsync(executablePath, serial, cancellationToken);

		foreach (var candidate in AdbUsbTunnelPorts.DeviceSideCandidates)
		{
			var remoteSpec = "tcp:" + candidate;
			var match = existing.FirstOrDefault(mapping => mapping.Remote == remoteSpec);
			if (match is not null && AdbReverseListParser.ParseTcpPort(match.Local) == publicPort)
			{
				RecordTunnel(serial, candidate, publicPort);
				return new AdbTunnel(true, candidate, publicPort, null, null);
			}
		}

		await RemoveStaleRecordedTunnelAsync(executablePath, serial, publicPort, cancellationToken);

		foreach (var candidate in AdbUsbTunnelPorts.DeviceSideCandidates)
		{
			if (await CreateTunnelAsync(executablePath, serial, candidate, publicPort, cancellationToken))
			{
				RecordTunnel(serial, candidate, publicPort);
				return new AdbTunnel(true, candidate, publicPort, null, null);
			}
		}

		var firstCandidate = AdbUsbTunnelPorts.DeviceSideCandidates[0];
		return new AdbTunnel(false,
			firstCandidate,
			publicPort,
			AdbFailureCode.CommandFailed,
			$"Could not open a reverse tunnel for device '{serial}': every candidate device-side port is in use.");
	}

	private async Task<IReadOnlyList<AdbReverseMapping>> ListMappingsAsync(
		string executablePath,
		string serial,
		CancellationToken cancellationToken)
	{
		var result = await _processRunner.RunAsync(executablePath,
			["-s", serial, "reverse", "--list"],
			_reverseCallTimeout,
			cancellationToken);

		if (!result.Started || result.TimedOut || result.ExitCode != 0)
		{
			return [];
		}

		return AdbReverseListParser.Parse(result.StandardOutput);
	}

	private async Task RemoveStaleRecordedTunnelAsync(
		string executablePath,
		string serial,
		int publicPort,
		CancellationToken cancellationToken)
	{
		var state = _ownershipMarker.Read();
		var recorded = state?.Tunnels.FirstOrDefault(tunnel => tunnel.Serial == serial);
		if (recorded is null || recorded.HostPort == publicPort)
		{
			return;
		}

		try
		{
			await _processRunner.RunAsync(executablePath,
				["-s", serial, "reverse", "--remove", "tcp:" + recorded.DevicePort],
				_reverseCallTimeout,
				cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Debug(ex, "Failed to remove stale adb reverse tunnel for {Serial}", serial);
		}

		RemoveRecordedTunnel(serial);
	}

	private async Task<bool> CreateTunnelAsync(
		string executablePath,
		string serial,
		int devicePort,
		int publicPort,
		CancellationToken cancellationToken)
	{
		var result = await _processRunner.RunAsync(executablePath,
			["-s", serial, "reverse", "--no-rebind", "tcp:" + devicePort, "tcp:" + publicPort],
			_reverseCallTimeout,
			cancellationToken);

		return result is { Started: true, TimedOut: false, ExitCode: 0 };
	}

	private void RecordTunnel(string serial, int devicePort, int hostPort)
	{
		var state = _ownershipMarker.Read() ??
			new AdbOwnershipState(Environment.ProcessId, false, [], DateTimeOffset.UtcNow);
		var tunnels = state.Tunnels
			.Where(tunnel => tunnel.Serial != serial)
			.Append(new AdbOwnedTunnel(serial, devicePort, hostPort))
			.ToList();

		_ownershipMarker.Write(state with
		{
			ProcessId = Environment.ProcessId, Tunnels = tunnels, WrittenAt = DateTimeOffset.UtcNow
		});
	}

	private void RemoveRecordedTunnel(string serial)
	{
		var state = _ownershipMarker.Read();
		if (state is null)
		{
			return;
		}

		var tunnels = state.Tunnels.Where(tunnel => tunnel.Serial != serial).ToList();
		if (tunnels.Count == state.Tunnels.Count)
		{
			return;
		}

		_ownershipMarker.Write(state with { Tunnels = tunnels, WrittenAt = DateTimeOffset.UtcNow });
	}
}
