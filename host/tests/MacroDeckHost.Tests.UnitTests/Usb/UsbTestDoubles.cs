using MacroDeckHost.Infrastructure.Usb.Native;

namespace MacroDeckHost.Tests.UnitTests.Usb;

internal sealed class FakeLinkSession : INativeLinkSession
{
	private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public bool IsLinked { get; set; }

	public bool EverLinked { get; set; }

	public bool ByeReceived { get; set; }

	public bool Ended => _ended.Task.IsCompleted;

	public Task Completion => _ended.Task;

	public void Link()
	{
		IsLinked = true;
		EverLinked = true;
	}

	public void End() => _ended.TrySetResult();

	public bool StopRequested { get; private set; }

	public bool EndsWhenStopped { get; set; } = true;

	public void Stop()
	{
		StopRequested = true;
		if (EndsWhenStopped)
		{
			End();
		}
	}
}

internal sealed class FakePipe : IUsbBulkPipe
{
	public UsbTransferResult Read(byte[] buffer, int timeoutMilliseconds) => new(UsbTransferStatus.TimedOut, 0);

	public UsbTransferResult Write(byte[] buffer, int offset, int count, int timeoutMilliseconds)
		=> new(UsbTransferStatus.Completed, count);

	public void Dispose()
	{
	}
}

internal sealed class FakeUsb : IUsbHost, IUsbDeviceList
{
	public static readonly UsbPlugKey DefaultPlug = new(1, "4.2");

	public List<(UsbDeviceInfo Device, IReadOnlyList<UsbInterfaceInfo> Interfaces, UsbDeviceStrings? Strings)> Attached
	{
		get;
	} = [];

	public List<UsbPlugKey> Switched { get; } = [];

	public int StringReads { get; private set; }

	public int Enumerations { get; private set; }

	public int AvailabilityChecks { get; private set; }

	public AccessorySwitchOutcome SwitchOutcome { get; set; } = AccessorySwitchOutcome.Switched;

	public bool Available
	{
		get
		{
			AvailabilityChecks++;
			return true;
		}
	}

	public IReadOnlyList<UsbDeviceInfo> Devices => Attached.Select(entry => entry.Device).ToList();

	public void Plug(ushort productId,
		IReadOnlyList<UsbInterfaceInfo> interfaces,
		string? product = "Example Phone",
		string? serial = "EXAMPLE0001",
		ushort vendorId = 0x1234,
		byte deviceClass = 0,
		UsbPlugKey? plug = null,
		IReadOnlyDictionary<byte, string>? interfaceNames = null)
	{
		var key = plug ?? DefaultPlug;
		Attached.RemoveAll(entry => entry.Device.Plug == key);
		Attached.Add((new UsbDeviceInfo(key, vendorId, productId, deviceClass, Attached.Count),
			interfaces,
			new UsbDeviceStrings("Example", product, serial, interfaceNames ?? new Dictionary<byte, string>())));
	}

	public void SwitchToAccessoryMode(UsbPlugKey? plug = null, string? serial = "EXAMPLE0001")
		=> Plug(0x2D01, [new UsbInterfaceInfo(0, 0xFF, 0xFF, 0x00, 0)], serial: serial,
			vendorId: AccessoryProtocol.GoogleVendorId, plug: plug);

	public void Unplug() => Attached.Clear();

	public IUsbDeviceList Enumerate()
	{
		Enumerations++;
		return this;
	}

	public IReadOnlyList<UsbInterfaceInfo>? ReadInterfaces(UsbDeviceInfo device)
		=> Attached.Single(entry => entry.Device == device).Interfaces;

	public UsbDeviceStrings? ReadStrings(UsbDeviceInfo device, IReadOnlyCollection<byte> interfaceNameIndexes)
	{
		StringReads++;
		return Attached.Single(entry => entry.Device == device).Strings;
	}

	public List<IReadOnlyList<string>> SentIdentifications { get; } = [];

	public AccessorySwitchOutcome SwitchToAccessory(UsbDeviceInfo device, IReadOnlyList<string> identification)
	{
		Switched.Add(device.Plug);
		SentIdentifications.Add(identification);
		return SwitchOutcome;
	}

	public IUsbBulkPipe? OpenAccessory(UsbDeviceInfo device) => new FakePipe();

	public void Dispose()
	{
	}
}
