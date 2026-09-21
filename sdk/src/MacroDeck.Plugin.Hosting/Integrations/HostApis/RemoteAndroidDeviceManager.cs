using System.Text.Json;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Android;
using ILogger = Serilog.ILogger;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

internal sealed class RemoteAndroidDeviceManager : IAndroidDeviceManager
{
	// The host's own connect timeout plus room for the round trip.
	private static readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(30);

	private readonly IHostInvoker _invoker;
	private readonly HostStateCache _stateCache;
	private readonly ILogger _logger;
	private readonly Lock _gate = new();
	private readonly Dictionary<string, RemoteAndroidDevice> _known = new(StringComparer.Ordinal);
	private IReadOnlyList<RemoteAndroidDevice> _attached = [];
	private AndroidDeviceAccess _access = AndroidDeviceAccess.Unsupported;

	public RemoteAndroidDeviceManager(IHostInvoker invoker, HostStateCache stateCache, ILogger logger)
	{
		_invoker = invoker;
		_stateCache = stateCache;
		_logger = logger.ForContext<RemoteAndroidDeviceManager>();
		stateCache.AdbChanged += OnAdbChanged;
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

	public async Task<IAndroidDevice> ConnectAsync(string address, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(address);
		JsonElement? data;
		try
		{
			data = await _invoker.InvokeAsync(Protocol.Callbacks.HostApis.Adb,
					HostOperations.Adb.Connect,
					new AdbConnectArguments { Address = address },
					_connectTimeout,
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch (HostInvocationException exception)
		{
			throw RemoteAndroidDevice.ToAndroidDeviceException(exception);
		}

		var serial = data?.Deserialize<AdbConnectResultDto>(PluginProtocolJson.Options)?.Serial;
		if (string.IsNullOrEmpty(serial))
		{
			throw new AndroidDeviceException(AndroidDeviceErrorCode.Unknown, "The host sent no serial.");
		}

		lock (_gate)
		{
			if (!_known.TryGetValue(serial, out var device))
			{
				device = new RemoteAndroidDevice(serial, _invoker);
				_known[serial] = device;
			}

			return device;
		}
	}

	private void OnAdbChanged()
	{
		var changes = Update();

		// Runs on the receive loop: a throwing plugin handler must not end the connection.
		foreach (var change in changes)
		{
			Raise(change);
		}
	}

	private List<Change> Update()
	{
		var state = _stateCache.Get<AdbStateDto>(Protocol.Callbacks.HostApis.Adb);
		var access = state is null ? AndroidDeviceAccess.Unsupported : ToAccess(state.Access);
		var pushed = access == AndroidDeviceAccess.Available ? state!.Devices : [];

		var changes = new List<Change>();
		lock (_gate)
		{
			if (access != _access)
			{
				_access = access;
				changes.Add(new Change(ChangeKind.Access, null, null));
			}

			var before = _attached.ToDictionary(device => device.Serial, StringComparer.Ordinal);
			var current = new List<RemoteAndroidDevice>(pushed.Count);
			foreach (var dto in pushed.Where(dto => !string.IsNullOrEmpty(dto.Serial)).DistinctBy(dto => dto.Serial))
			{
				if (!_known.TryGetValue(dto.Serial, out var device))
				{
					device = new RemoteAndroidDevice(dto.Serial, _invoker);
					_known[dto.Serial] = device;
				}

				var previousState = device.State;
				device.Update(new AndroidDeviceInfo(dto.Model, dto.Manufacturer, dto.Product), ToState(dto.State));
				current.Add(device);

				if (!before.Remove(dto.Serial))
				{
					changes.Add(new Change(ChangeKind.Connected, device, null));
				}
				else if (previousState != device.State)
				{
					changes.Add(new Change(ChangeKind.StateChanged, device, previousState));
				}
			}

			changes.AddRange(before.Values.Select(device => new Change(ChangeKind.Disconnected, device, null)));
			_attached = current;
		}

		return changes;
	}

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

	private static AndroidDeviceAccess ToAccess(string? access)
		=> access switch
		{
			AdbAccessStates.Available => AndroidDeviceAccess.Available,
			AdbAccessStates.NotEnabled => AndroidDeviceAccess.AdbNotEnabled,
			AdbAccessStates.NotAllowed => AndroidDeviceAccess.AdbNotAllowed,
			_ => AndroidDeviceAccess.Unsupported
		};

	private static AndroidDeviceState ToState(string? state)
		=> state switch
		{
			AdbDeviceStates.Online => AndroidDeviceState.Online,
			AdbDeviceStates.Connecting => AndroidDeviceState.Connecting,
			AdbDeviceStates.Unauthorized => AndroidDeviceState.Unauthorized,
			_ => AndroidDeviceState.Offline
		};

	private enum ChangeKind
	{
		Access,
		Connected,
		Disconnected,
		StateChanged
	}

	private sealed record Change(ChangeKind Kind, RemoteAndroidDevice? Device, AndroidDeviceState? PreviousState);
}
