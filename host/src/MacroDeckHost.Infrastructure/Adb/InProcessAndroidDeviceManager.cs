using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Sdk.Android;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Domain.Common;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Adb;

// ADR 0107: built-in integrations reach adb through the same SDK manager plugins use. The consent gate is
// the plugin path's own; the locked-host rule and the shell budget are shared through AdbDeviceCallPolicy.
public sealed class InProcessAndroidDeviceManager : IAndroidDeviceManager, IDisposable
{
	private readonly IAdbManager _adbManager;
	private readonly IAdbDeviceOperations _operations;
	private readonly AdbDeviceCallPolicy _callPolicy;
	private readonly ILogger _logger;
	private readonly Lock _gate = new();
	private readonly Dictionary<string, InProcessAndroidDevice> _known = new(StringComparer.Ordinal);
	private List<InProcessAndroidDevice> _attached = [];
	private AndroidDeviceAccess _access = AndroidDeviceAccess.AdbNotEnabled;
	private bool _adbUnavailable;

	public InProcessAndroidDeviceManager(IAdbManager adbManager,
		IAdbDeviceOperations operations,
		IHostLockState lockState,
		ILogger logger)
	{
		_adbManager = adbManager;
		_operations = operations;
		_callPolicy = new AdbDeviceCallPolicy(lockState);
		_logger = logger.ForContext<InProcessAndroidDeviceManager>();
		adbManager.SnapshotChanged += OnSnapshotChanged;
		Update();
	}

	public event EventHandler<AndroidDeviceEventArgs>? DeviceConnected;

	public event EventHandler<AndroidDeviceEventArgs>? DeviceDisconnected;

	public event EventHandler<AndroidDeviceEventArgs>? DeviceStateChanged;

	public event EventHandler? AccessChanged;

	public AndroidDeviceAccess Access
	{
		get
		{
			lock (_gate)
			{
				return _access;
			}
		}
	}

	public IReadOnlyCollection<IAndroidDevice> Devices
	{
		get
		{
			lock (_gate)
			{
				return _attached;
			}
		}
	}

	public IAndroidDevice? FindDevice(string serial)
	{
		lock (_gate)
		{
			return _attached.FirstOrDefault(device => string.Equals(device.Serial, serial, StringComparison.Ordinal));
		}
	}

	public bool IsAdbUnavailable
	{
		get
		{
			lock (_gate)
			{
				return _adbUnavailable;
			}
		}
	}

	public IAndroidDevice? DefaultDevice
	{
		get
		{
			var configured = _adbManager.Status.DefaultDeviceSerial;
			lock (_gate)
			{
				if (_access != AndroidDeviceAccess.Available)
				{
					return null;
				}

				if (!string.IsNullOrWhiteSpace(configured))
				{
					return _known.GetValueOrDefault(configured);
				}

				return _attached.Count == 1 ? _attached[0] : null;
			}
		}
	}

	public async Task<IAndroidDevice> ConnectAsync(string address, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(address);
		ThrowIfLocked(HostOperations.Adb.Connect);
		var serial = Unwrap(await _operations.ConnectAsync(address, cancellationToken));
		lock (_gate)
		{
			return Known(serial);
		}
	}

	public void Dispose() => _adbManager.SnapshotChanged -= OnSnapshotChanged;

	internal void ThrowIfLocked(string operation)
	{
		if (_callPolicy.IsRefusedWhileLocked(operation))
		{
			throw new AndroidDeviceException(AndroidDeviceErrorCode.HostLocked, "Macro Deck is locked.");
		}
	}

	internal static T Unwrap<T>(Result<T, AdbFailureCode> result)
		=> result.Success ? result.Data! : throw ToException(result.Error, result.ErrorMessage);

	internal static void Unwrap(Result<AdbFailureCode> result)
	{
		if (!result.Success)
		{
			throw ToException(result.Error, result.ErrorMessage);
		}
	}

	private static AndroidDeviceException ToException(AdbFailureCode? code, string? message)
	{
		var errorCode = code switch
		{
			AdbFailureCode.Disabled => AndroidDeviceErrorCode.AdbNotEnabled,
			AdbFailureCode.ExecutableNotFound or AdbFailureCode.ServerUnreachable => AndroidDeviceErrorCode.AdbUnavailable,
			AdbFailureCode.DeviceNotFound => AndroidDeviceErrorCode.DeviceNotFound,
			AdbFailureCode.DeviceUnauthorized => AndroidDeviceErrorCode.DeviceUnauthorized,
			AdbFailureCode.DeviceOffline => AndroidDeviceErrorCode.DeviceOffline,
			AdbFailureCode.Timeout => AndroidDeviceErrorCode.Timeout,
			AdbFailureCode.InvalidParameter => AndroidDeviceErrorCode.InvalidArgument,
			AdbFailureCode.Unsupported => AndroidDeviceErrorCode.Unsupported,
			_ => AndroidDeviceErrorCode.CommandFailed
		};

		return new AndroidDeviceException(errorCode, string.IsNullOrWhiteSpace(message) ? $"adb failed with {errorCode}." : message);
	}

	private void OnSnapshotChanged(object? sender, EventArgs e)
	{
		foreach (var change in Update())
		{
			Raise(change);
		}
	}

	private List<Change> Update()
	{
		var status = _adbManager.Status;
		var access = status is { Enabled: true, Supported: true }
			? AndroidDeviceAccess.Available
			: AndroidDeviceAccess.AdbNotEnabled;
		var current = access == AndroidDeviceAccess.Available
			? _adbManager.Devices.Where(device => device.State != AdbDeviceState.Disconnected).DistinctBy(device => device.Serial)
			: [];

		var changes = new List<Change>();
		lock (_gate)
		{
			var adbUnavailable = access == AndroidDeviceAccess.Available && status.ResolvedExecutablePath is null;
			if (access != _access || adbUnavailable != _adbUnavailable)
			{
				_access = access;
				_adbUnavailable = adbUnavailable;
				changes.Add(new Change(ChangeKind.Access, null, null));
			}

			var before = _attached.ToDictionary(device => device.Serial, StringComparer.Ordinal);
			var attached = new List<InProcessAndroidDevice>();
			foreach (var adbDevice in current)
			{
				var device = Known(adbDevice.Serial);
				var previousState = device.State;
				var previousLacksUsbPermission = device.LacksUsbPermission;
				var previousMode = device.Mode;
				device.Update(new AndroidDeviceInfo(adbDevice.Model, adbDevice.Manufacturer, adbDevice.Product)
					{
						StableId = adbDevice.StableId
					},
					ToState(adbDevice.State),
					adbDevice.State == AdbDeviceState.NoPermissions,
					ToMode(adbDevice.State));
				attached.Add(device);

				if (!before.Remove(adbDevice.Serial))
				{
					changes.Add(new Change(ChangeKind.Connected, device, null));
				}
				else if (previousState != device.State ||
					previousLacksUsbPermission != device.LacksUsbPermission ||
					previousMode != device.Mode)
				{
					changes.Add(new Change(ChangeKind.StateChanged, device, previousState));
				}
			}

			changes.AddRange(before.Values.Select(device => new Change(ChangeKind.Disconnected, device, null)));
			_attached = attached;
		}

		return changes;
	}

	private InProcessAndroidDevice Known(string serial)
	{
		if (!_known.TryGetValue(serial, out var device))
		{
			device = new InProcessAndroidDevice(serial, this, _operations, _adbManager);
			_known[serial] = device;
		}

		return device;
	}

	// Raised under the adb reconcile gate, like every SnapshotChanged handler: a handler must return quickly.
	private void Raise(Change change)
	{
		try
		{
			switch (change.Kind)
			{
				case ChangeKind.Access:
					AccessChanged?.Invoke(this, EventArgs.Empty);
					break;
				case ChangeKind.Connected:
					DeviceConnected?.Invoke(this, new AndroidDeviceEventArgs(change.Device!, null));
					break;
				case ChangeKind.Disconnected:
					DeviceDisconnected?.Invoke(this, new AndroidDeviceEventArgs(change.Device!, null));
					break;
				case ChangeKind.StateChanged:
					DeviceStateChanged?.Invoke(this, new AndroidDeviceEventArgs(change.Device!, change.PreviousState));
					break;
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Error(exception, "An Android device {Change} handler failed", change.Kind);
		}
	}

	private static AndroidDeviceState ToState(AdbDeviceState state)
		=> state switch
		{
			AdbDeviceState.Device => AndroidDeviceState.Online,
			AdbDeviceState.Authorizing => AndroidDeviceState.Connecting,
			AdbDeviceState.Unauthorized or AdbDeviceState.NoPermissions => AndroidDeviceState.Unauthorized,
			_ => AndroidDeviceState.Offline
		};

	internal static AndroidDeviceMode ToMode(AdbDeviceState state)
		=> state switch
		{
			AdbDeviceState.Bootloader => AndroidDeviceMode.Bootloader,
			AdbDeviceState.Recovery => AndroidDeviceMode.Recovery,
			AdbDeviceState.Sideload => AndroidDeviceMode.Sideload,
			AdbDeviceState.Unknown => AndroidDeviceMode.Unknown,
			_ => AndroidDeviceMode.Normal
		};

	private enum ChangeKind
	{
		Access,
		Connected,
		Disconnected,
		StateChanged
	}

	private sealed record Change(ChangeKind Kind, InProcessAndroidDevice? Device, AndroidDeviceState? PreviousState);
}
