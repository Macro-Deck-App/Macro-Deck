using System.Runtime.InteropServices;
using System.Text;

namespace MacroDeckHost.Infrastructure.Usb.Native;

internal sealed class LibUsbHost : IUsbHost, IDisposable
{
	private const uint ControlTimeoutMilliseconds = 1000;
	private const int MaxPortDepth = 7;
	private const byte BulkTransferType = 0x02;
	private const byte EndpointDirectionIn = 0x80;

	public static readonly TimeSpan InitializeRetryInterval = TimeSpan.FromSeconds(30);

	private readonly TimeProvider _time;
	private readonly Lock _gate = new();
	private IntPtr _context;
	private int _openPipes;
	private DateTimeOffset? _lastAttempt;

	public LibUsbHost(TimeProvider time)
	{
		_time = time;
	}

	public bool Available => Context != IntPtr.Zero;

	private IntPtr Context
	{
		get
		{
			lock (_gate)
			{
				var now = _time.GetUtcNow();
				if (_context == IntPtr.Zero && (_lastAttempt is not { } last || now - last >= InitializeRetryInterval))
				{
					_lastAttempt = now;
					_context = LibUsbNative.TryInitialize(out var context) ? context : IntPtr.Zero;
				}

				return _context;
			}
		}
	}

	public IUsbDeviceList Enumerate()
	{
		var context = Context;
		if (context == IntPtr.Zero)
		{
			return EmptyDeviceList.Instance;
		}

		var count = LibUsbNative.GetDeviceList(context, out var list);
		if (count < 0 || list == IntPtr.Zero)
		{
			return EmptyDeviceList.Instance;
		}

		var pointers = new IntPtr[count];
		var devices = new List<UsbDeviceInfo>((int)count);
		for (var index = 0; index < count; index++)
		{
			var device = Marshal.ReadIntPtr(list, index * IntPtr.Size);
			pointers[index] = device;
			if (LibUsbNative.GetDeviceDescriptor(device, out var descriptor) != LibUsbNative.Success)
			{
				continue;
			}

			devices.Add(new UsbDeviceInfo(PlugOf(device),
				descriptor.VendorId,
				descriptor.ProductId,
				descriptor.DeviceClass,
				index));
		}

		return new LibUsbDeviceList(this, list, pointers, devices);
	}

	public void Dispose()
	{
		lock (_gate)
		{
			// libusb_exit with a handle still open is undefined; a pipe still closing keeps the context alive.
			if (_context != IntPtr.Zero && Volatile.Read(ref _openPipes) == 0)
			{
				LibUsbNative.Exit(_context);
				_context = IntPtr.Zero;
			}
		}
	}

	private static UsbPlugKey PlugOf(IntPtr device)
	{
		var ports = new byte[MaxPortDepth];
		var depth = LibUsbNative.GetPortNumbers(device, ports, ports.Length);
		var path = depth > 0 ? string.Join('.', ports.Take(depth)) : "0";
		return new UsbPlugKey(LibUsbNative.GetBusNumber(device), path);
	}

	private sealed class EmptyDeviceList : IUsbDeviceList
	{
		public static readonly EmptyDeviceList Instance = new();

		public IReadOnlyList<UsbDeviceInfo> Devices => [];

		public IReadOnlyList<UsbInterfaceInfo>? ReadInterfaces(UsbDeviceInfo device) => null;

		public UsbDeviceStrings? ReadStrings(UsbDeviceInfo device, IReadOnlyCollection<byte> interfaceNameIndexes)
			=> null;

		public AccessorySwitchOutcome SwitchToAccessory(UsbDeviceInfo device, IReadOnlyList<string> identification)
			=> AccessorySwitchOutcome.Failed;

		public IUsbBulkPipe? OpenAccessory(UsbDeviceInfo device) => null;

		public void Dispose()
		{
		}
	}

	private sealed class LibUsbDeviceList : IUsbDeviceList
	{
		private readonly LibUsbHost _host;
		private readonly IntPtr _list;
		private readonly IntPtr[] _pointers;
		private bool _disposed;

		public LibUsbDeviceList(LibUsbHost host, IntPtr list, IntPtr[] pointers, IReadOnlyList<UsbDeviceInfo> devices)
		{
			_host = host;
			_list = list;
			_pointers = pointers;
			Devices = devices;
		}

		public IReadOnlyList<UsbDeviceInfo> Devices { get; }

		public IReadOnlyList<UsbInterfaceInfo>? ReadInterfaces(UsbDeviceInfo device)
			=> ReadConfiguration(_pointers[device.Index])?.Interfaces;

		public UsbDeviceStrings? ReadStrings(UsbDeviceInfo device, IReadOnlyCollection<byte> interfaceNameIndexes)
		{
			var pointer = _pointers[device.Index];
			if (LibUsbNative.GetDeviceDescriptor(pointer, out var descriptor) != LibUsbNative.Success ||
				LibUsbNative.Open(pointer, out var handle) != LibUsbNative.Success)
			{
				return null;
			}

			try
			{
				var names = new Dictionary<byte, string>();
				foreach (var index in interfaceNameIndexes)
				{
					if (ReadString(handle, index) is { } name)
					{
						names[index] = name;
					}
				}

				return new UsbDeviceStrings(ReadString(handle, descriptor.ManufacturerIndex),
					ReadString(handle, descriptor.ProductIndex),
					ReadString(handle, descriptor.SerialNumberIndex),
					names);
			}
			finally
			{
				LibUsbNative.Close(handle);
			}
		}

		public AccessorySwitchOutcome SwitchToAccessory(UsbDeviceInfo device, IReadOnlyList<string> identification)
		{
			if (LibUsbNative.Open(_pointers[device.Index], out var handle) != LibUsbNative.Success)
			{
				return AccessorySwitchOutcome.Failed;
			}

			try
			{
				var version = new byte[2];
				if (LibUsbNative.ControlTransfer(handle, AccessoryProtocol.VendorIn, AccessoryProtocol.GetProtocolRequest,
						0, 0, version, (ushort)version.Length, ControlTimeoutMilliseconds) < version.Length)
				{
					return AccessorySwitchOutcome.Failed;
				}

				if ((version[0] | (version[1] << 8)) < 1)
				{
					return AccessorySwitchOutcome.NotSupported;
				}

				for (var index = 0; index < identification.Count; index++)
				{
					var value = Encoding.UTF8.GetBytes(identification[index] + "\0");
					if (LibUsbNative.ControlTransfer(handle, AccessoryProtocol.VendorOut, AccessoryProtocol.SendStringRequest,
							0, (ushort)index, value, (ushort)value.Length, ControlTimeoutMilliseconds) < 0)
					{
						return AccessorySwitchOutcome.Failed;
					}
				}

				return LibUsbNative.ControlTransfer(handle, AccessoryProtocol.VendorOut, AccessoryProtocol.StartRequest,
					0, 0, null, 0, ControlTimeoutMilliseconds) < 0
					? AccessorySwitchOutcome.Failed
					: AccessorySwitchOutcome.Switched;
			}
			finally
			{
				LibUsbNative.Close(handle);
			}
		}

		public IUsbBulkPipe? OpenAccessory(UsbDeviceInfo device)
		{
			var pointer = _pointers[device.Index];
			if (ReadConfiguration(pointer)?.AccessoryEndpoints is not { } endpoints ||
				LibUsbNative.Open(pointer, out var handle) != LibUsbNative.Success)
			{
				return null;
			}

			if (OperatingSystem.IsLinux())
			{
				_ = LibUsbNative.SetAutoDetachKernelDriver(handle, 1);
			}

			if (LibUsbNative.ClaimInterface(handle, 0) != LibUsbNative.Success)
			{
				LibUsbNative.Close(handle);
				return null;
			}

			Interlocked.Increment(ref _host._openPipes);
			return new LibUsbBulkPipe(handle, endpoints.In, endpoints.Out, () => Interlocked.Decrement(ref _host._openPipes));
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			LibUsbNative.FreeDeviceList(_list, 1);
		}

		private static string? ReadString(IntPtr handle, byte index)
		{
			if (index == 0)
			{
				return null;
			}

			var buffer = new byte[256];
			var length = LibUsbNative.GetStringDescriptorAscii(handle, index, buffer, buffer.Length);
			return length > 0 ? Encoding.ASCII.GetString(buffer, 0, length).Trim() : null;
		}

		private static ActiveConfiguration? ReadConfiguration(IntPtr device)
		{
			if (LibUsbNative.GetActiveConfigDescriptor(device, out var configPointer) != LibUsbNative.Success)
			{
				return null;
			}

			try
			{
				var config = Marshal.PtrToStructure<LibUsbNative.ConfigDescriptor>(configPointer);
				var interfaces = new List<UsbInterfaceInfo>(config.NumInterfaces);
				(byte In, byte Out)? accessoryEndpoints = null;
				var interfaceSize = Marshal.SizeOf<LibUsbNative.Interface>();
				for (var index = 0; index < config.NumInterfaces; index++)
				{
					var entry = Marshal.PtrToStructure<LibUsbNative.Interface>(config.Interfaces + (index * interfaceSize));
					if (entry.NumAltSettings < 1 || entry.AltSettings == IntPtr.Zero)
					{
						continue;
					}

					var descriptor = Marshal.PtrToStructure<LibUsbNative.InterfaceDescriptor>(entry.AltSettings);
					interfaces.Add(new UsbInterfaceInfo(descriptor.InterfaceNumber,
						descriptor.InterfaceClass,
						descriptor.InterfaceSubClass,
						descriptor.InterfaceProtocol,
						descriptor.InterfaceIndex));
					if (descriptor.InterfaceNumber == 0)
					{
						accessoryEndpoints = BulkEndpoints(descriptor);
					}
				}

				return new ActiveConfiguration(interfaces, accessoryEndpoints);
			}
			finally
			{
				LibUsbNative.FreeConfigDescriptor(configPointer);
			}
		}

		private static (byte In, byte Out)? BulkEndpoints(LibUsbNative.InterfaceDescriptor descriptor)
		{
			byte? inEndpoint = null;
			byte? outEndpoint = null;
			var endpointSize = Marshal.SizeOf<LibUsbNative.EndpointDescriptor>();
			for (var index = 0; index < descriptor.NumEndpoints; index++)
			{
				var endpoint = Marshal.PtrToStructure<LibUsbNative.EndpointDescriptor>(
					descriptor.Endpoints + (index * endpointSize));
				if ((endpoint.Attributes & 0x03) != BulkTransferType)
				{
					continue;
				}

				if ((endpoint.EndpointAddress & EndpointDirectionIn) != 0)
				{
					inEndpoint ??= endpoint.EndpointAddress;
				}
				else
				{
					outEndpoint ??= endpoint.EndpointAddress;
				}
			}

			return inEndpoint is { } input && outEndpoint is { } output ? (input, output) : null;
		}

		private sealed record ActiveConfiguration(
			IReadOnlyList<UsbInterfaceInfo> Interfaces,
			(byte In, byte Out)? AccessoryEndpoints);
	}

	private sealed class LibUsbBulkPipe : IUsbBulkPipe
	{
		private readonly IntPtr _handle;
		private readonly byte _inEndpoint;
		private readonly byte _outEndpoint;
		private readonly Action _closed;
		private int _disposed;

		public LibUsbBulkPipe(IntPtr handle, byte inEndpoint, byte outEndpoint, Action closed)
		{
			_handle = handle;
			_closed = closed;
			_inEndpoint = inEndpoint;
			_outEndpoint = outEndpoint;
		}

		public UsbTransferResult Read(byte[] buffer, int timeoutMilliseconds)
			=> Transfer(_inEndpoint, buffer, 0, buffer.Length, timeoutMilliseconds);

		public UsbTransferResult Write(byte[] buffer, int offset, int count, int timeoutMilliseconds)
			=> Transfer(_outEndpoint, buffer, offset, count, timeoutMilliseconds);

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) != 0)
			{
				return;
			}

			_ = LibUsbNative.ReleaseInterface(_handle, 0);
			LibUsbNative.Close(_handle);
			_closed();
		}

		private UsbTransferResult Transfer(byte endpoint, byte[] buffer, int offset, int count, int timeoutMilliseconds)
		{
			if (Volatile.Read(ref _disposed) != 0 || count == 0)
			{
				return new UsbTransferResult(UsbTransferStatus.Failed, 0);
			}

			var status = LibUsbNative.BulkTransfer(_handle,
				endpoint,
				ref buffer[offset],
				count,
				out var transferred,
				(uint)timeoutMilliseconds);
			return status switch
			{
				LibUsbNative.Success => new UsbTransferResult(UsbTransferStatus.Completed, transferred),
				LibUsbNative.ErrorTimeout => new UsbTransferResult(UsbTransferStatus.TimedOut, transferred),
				_ => new UsbTransferResult(UsbTransferStatus.Failed, transferred)
			};
		}
	}
}
