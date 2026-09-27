using System.Globalization;
using MacroDeckHost.Application.Usb;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Usb.Native;

internal interface IUsbmuxConnector
{
	Task ListenAsync(Action onListening, Action<UsbmuxDeviceEvent> onEvent, CancellationToken cancellationToken);

	Task<ILinkCarrier?> ConnectAsync(int deviceId, ushort port, CancellationToken cancellationToken);
}

internal sealed class UsbmuxConnector : IUsbmuxConnector
{
	private readonly UsbmuxClient _client;

	public UsbmuxConnector(UsbmuxClient client)
	{
		_client = client;
	}

	public Task ListenAsync(Action onListening, Action<UsbmuxDeviceEvent> onEvent, CancellationToken cancellationToken)
		=> _client.ListenAsync(onListening, onEvent, cancellationToken);

	public async Task<ILinkCarrier?> ConnectAsync(int deviceId, ushort port, CancellationToken cancellationToken)
		=> await _client.ConnectAsync(deviceId, port, cancellationToken) is { } socket
			? new SocketLinkCarrier(socket)
			: null;
}

internal sealed class UsbmuxCoordinator : IDisposable
{
	public static readonly TimeSpan ConnectInterval = TimeSpan.FromSeconds(3);

	private readonly IUsbmuxConnector _connector;
	private readonly Func<ILinkCarrier, string, INativeLinkSession> _startSession;
	private readonly ILogger _logger;
	private readonly Lock _gate = new();
	private readonly Dictionary<int, IosDevice> _devices = [];
	private readonly List<INativeLinkSession> _stopping = [];

	private CancellationTokenSource? _listening;
	private Task _listen = Task.CompletedTask;
	private bool _available;

	public UsbmuxCoordinator(IUsbmuxConnector connector,
		Func<ILinkCarrier, string, INativeLinkSession> startSession,
		ILogger logger)
	{
		_connector = connector;
		_startSession = startSession;
		_logger = logger.ForContext<UsbmuxCoordinator>();
	}

	public bool Available
	{
		get
		{
			lock (_gate)
			{
				return _available;
			}
		}
	}

	public IReadOnlyList<NativeUsbDevice> Devices
	{
		get
		{
			lock (_gate)
			{
				return _devices.Values.OrderBy(device => device.Id)
					.Select(device => new NativeUsbDevice(Id(device),
						NativeUsbPlatform.Ios,
						device.Serial,
						null,
						null,
						device.Session is { IsLinked: true } ? NativeUsbDeviceState.Linked : NativeUsbDeviceState.Connecting,
						Picked: false,
						Remembered: false))
					.ToList();
			}
		}
	}

	public void Poll(bool bridgeAvailable, DateTimeOffset now)
	{
		lock (_gate)
		{
			if (_listen.IsCompleted)
			{
				StartListening();
			}

			foreach (var device in _devices.Values)
			{
				if (device.Session is { Ended: true })
				{
					device.Session = null;
				}

				if (device.Session is null && device.Attempt.IsCompleted && bridgeAvailable && now >= device.NextAttempt)
				{
					device.NextAttempt = now + ConnectInterval;
					device.Attempt = TaskObservation.Settle(AttemptAsync(device, _listening!.Token), _logger);
				}
			}
		}
	}

	public void Dispose() => StopAll();

	public Task WhenStopped()
	{
		lock (_gate)
		{
			_stopping.RemoveAll(session => session.Ended);
			return Task.WhenAll(_devices.Values.Select(device => device.Session?.Completion ?? Task.CompletedTask)
				.Concat(_stopping.Select(session => session.Completion)));
		}
	}

	public void StopAll()
	{
		lock (_gate)
		{
			_listening?.Cancel();
			_listening?.Dispose();
			_listening = null;
			_listen = Task.CompletedTask;
			_available = false;
			StopDevices();
		}
	}

	private void StartListening()
	{
		_listening?.Cancel();
		_listening?.Dispose();
		_listening = new CancellationTokenSource();
		var token = _listening.Token;
		_listen = TaskObservation.Settle(Task.Run(async () =>
		{
			try
			{
				await _connector.ListenAsync(OnListening, OnDeviceEvent, token);
			}
			catch (Exception ex)
			{
				if (!token.IsCancellationRequested)
				{
					_logger.Debug(ex, "usbmuxd is not reachable");
				}
			}
			finally
			{
				lock (_gate)
				{
					if (!token.IsCancellationRequested)
					{
						_available = false;
						StopDevices();
					}
				}
			}
		}, token), _logger);
	}

	private void OnListening()
	{
		lock (_gate)
		{
			_available = true;
		}
	}

	private void OnDeviceEvent(UsbmuxDeviceEvent deviceEvent)
	{
		lock (_gate)
		{
			if (deviceEvent.Attached && deviceEvent.Usb)
			{
				_devices.TryAdd(deviceEvent.DeviceId, new IosDevice(deviceEvent.DeviceId, deviceEvent.SerialNumber));
			}
			else if (!deviceEvent.Attached && _devices.Remove(deviceEvent.DeviceId, out var detached))
			{
				Retire(detached.Session);
			}
		}
	}

	private async Task AttemptAsync(IosDevice device, CancellationToken cancellationToken)
	{
		ILinkCarrier? carrier = null;
		try
		{
			carrier = await _connector.ConnectAsync(device.Id, UsbmuxProtocol.CompanionLinkPort, cancellationToken);
		}
		catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or
			OperationCanceledException or ObjectDisposedException or FormatException or
			System.Xml.XmlException or ArgumentException or OverflowException or LinkLostException)
		{
			_logger.Debug(ex, "Could not reach the app on iOS device {DeviceId}", device.Id);
		}

		if (carrier is null)
		{
			return;
		}

		lock (_gate)
		{
			if (!cancellationToken.IsCancellationRequested &&
				_devices.TryGetValue(device.Id, out var current) &&
				ReferenceEquals(current, device) &&
				device.Session is null)
			{
				device.Session = _startSession(carrier, NativeUsbDeviceKeys.For(device.Serial, Id(device)));
				_logger.Information("Opened the usbmuxd link to iOS device {DeviceId}", device.Id);
				return;
			}
		}

		await carrier.DisposeAsync();
	}

	private void Retire(INativeLinkSession? session)
	{
		if (session is null)
		{
			return;
		}

		session.Stop();
		_stopping.RemoveAll(stopped => stopped.Ended);
		_stopping.Add(session);
	}

	private void StopDevices()
	{
		foreach (var device in _devices.Values)
		{
			Retire(device.Session);
		}

		_devices.Clear();
	}

	private static string Id(IosDevice device)
		=> "ios-" + (device.Serial ?? device.Id.ToString(CultureInfo.InvariantCulture));

	private sealed class IosDevice
	{
		public IosDevice(int id, string? serial)
		{
			Id = id;
			Serial = serial;
		}

		public int Id { get; }

		public string? Serial { get; }

		public INativeLinkSession? Session { get; set; }

		public Task Attempt { get; set; } = Task.CompletedTask;

		public DateTimeOffset NextAttempt { get; set; }
	}
}
