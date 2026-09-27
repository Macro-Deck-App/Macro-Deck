using System.Reflection;
using System.Runtime.InteropServices;

namespace MacroDeckHost.Infrastructure.Usb.Native;

internal static class LibUsbNative
{
	public const int Success = 0;
	public const int ErrorTimeout = -7;

	private const string Library = "libusb-1.0";

	private static readonly Lock _resolverGate = new();
	private static bool _resolverInstalled;

	// libusb is not bundled: it comes from the system or a package manager (ADR 0095).
	internal static IReadOnlyList<string> Candidates()
	{
		if (OperatingSystem.IsWindows())
		{
			return ["libusb-1.0.dll"];
		}

		if (OperatingSystem.IsMacOS())
		{
			return ["libusb-1.0.0.dylib", "/opt/homebrew/lib/libusb-1.0.0.dylib", "/usr/local/lib/libusb-1.0.0.dylib"];
		}

		return ["libusb-1.0.so.0"];
	}

	public static bool TryInitialize(out IntPtr context)
	{
		context = IntPtr.Zero;
		InstallResolver();
		try
		{
			return Init(out context) == Success && context != IntPtr.Zero;
		}
		catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
		{
			return false;
		}
	}

	private static void InstallResolver()
	{
		lock (_resolverGate)
		{
			if (_resolverInstalled)
			{
				return;
			}

			NativeLibrary.SetDllImportResolver(typeof(LibUsbNative).Assembly, Resolve);
			_resolverInstalled = true;
		}
	}

	private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
	{
		if (libraryName != Library)
		{
			return IntPtr.Zero;
		}

		foreach (var candidate in Candidates())
		{
			var loaded = Path.IsPathRooted(candidate)
				? NativeLibrary.TryLoad(candidate, out var handle)
				: NativeLibrary.TryLoad(candidate, assembly, searchPath, out handle);
			if (loaded)
			{
				return handle;
			}
		}

		return IntPtr.Zero;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct DeviceDescriptor
	{
		public byte Length;
		public byte DescriptorType;
		public ushort UsbVersion;
		public byte DeviceClass;
		public byte DeviceSubClass;
		public byte DeviceProtocol;
		public byte MaxPacketSize0;
		public ushort VendorId;
		public ushort ProductId;
		public ushort DeviceVersion;
		public byte ManufacturerIndex;
		public byte ProductIndex;
		public byte SerialNumberIndex;
		public byte NumConfigurations;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct ConfigDescriptor
	{
		public byte Length;
		public byte DescriptorType;
		public ushort TotalLength;
		public byte NumInterfaces;
		public byte ConfigurationValue;
		public byte ConfigurationIndex;
		public byte Attributes;
		public byte MaxPower;
		public IntPtr Interfaces;
		public IntPtr Extra;
		public int ExtraLength;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct Interface
	{
		public IntPtr AltSettings;
		public int NumAltSettings;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct InterfaceDescriptor
	{
		public byte Length;
		public byte DescriptorType;
		public byte InterfaceNumber;
		public byte AlternateSetting;
		public byte NumEndpoints;
		public byte InterfaceClass;
		public byte InterfaceSubClass;
		public byte InterfaceProtocol;
		public byte InterfaceIndex;
		public IntPtr Endpoints;
		public IntPtr Extra;
		public int ExtraLength;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct EndpointDescriptor
	{
		public byte Length;
		public byte DescriptorType;
		public byte EndpointAddress;
		public byte Attributes;
		public ushort MaxPacketSize;
		public byte Interval;
		public byte Refresh;
		public byte SynchAddress;
		public IntPtr Extra;
		public int ExtraLength;
	}

	[DllImport(Library, EntryPoint = "libusb_init")]
	public static extern int Init(out IntPtr context);

	[DllImport(Library, EntryPoint = "libusb_exit")]
	public static extern void Exit(IntPtr context);

	[DllImport(Library, EntryPoint = "libusb_get_device_list")]
	public static extern nint GetDeviceList(IntPtr context, out IntPtr list);

	[DllImport(Library, EntryPoint = "libusb_free_device_list")]
	public static extern void FreeDeviceList(IntPtr list, int unrefDevices);

	[DllImport(Library, EntryPoint = "libusb_get_device_descriptor")]
	public static extern int GetDeviceDescriptor(IntPtr device, out DeviceDescriptor descriptor);

	[DllImport(Library, EntryPoint = "libusb_get_active_config_descriptor")]
	public static extern int GetActiveConfigDescriptor(IntPtr device, out IntPtr config);

	[DllImport(Library, EntryPoint = "libusb_free_config_descriptor")]
	public static extern void FreeConfigDescriptor(IntPtr config);

	[DllImport(Library, EntryPoint = "libusb_get_bus_number")]
	public static extern byte GetBusNumber(IntPtr device);

	[DllImport(Library, EntryPoint = "libusb_get_port_numbers")]
	public static extern int GetPortNumbers(IntPtr device, byte[] ports, int length);

	[DllImport(Library, EntryPoint = "libusb_open")]
	public static extern int Open(IntPtr device, out IntPtr handle);

	[DllImport(Library, EntryPoint = "libusb_close")]
	public static extern void Close(IntPtr handle);

	[DllImport(Library, EntryPoint = "libusb_get_string_descriptor_ascii")]
	public static extern int GetStringDescriptorAscii(IntPtr handle, byte index, byte[] data, int length);

	[DllImport(Library, EntryPoint = "libusb_control_transfer")]
	public static extern int ControlTransfer(IntPtr handle,
		byte requestType,
		byte request,
		ushort value,
		ushort index,
		byte[]? data,
		ushort length,
		uint timeout);

	[DllImport(Library, EntryPoint = "libusb_set_auto_detach_kernel_driver")]
	public static extern int SetAutoDetachKernelDriver(IntPtr handle, int enable);

	[DllImport(Library, EntryPoint = "libusb_claim_interface")]
	public static extern int ClaimInterface(IntPtr handle, int interfaceNumber);

	[DllImport(Library, EntryPoint = "libusb_release_interface")]
	public static extern int ReleaseInterface(IntPtr handle, int interfaceNumber);

	[DllImport(Library, EntryPoint = "libusb_bulk_transfer")]
	public static extern int BulkTransfer(IntPtr handle,
		byte endpoint,
		ref byte data,
		int length,
		out int transferred,
		uint timeout);
}
