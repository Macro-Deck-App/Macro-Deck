using System.Globalization;
using System.Text;

namespace MacroDeckHost.Infrastructure.Usb.Native;

internal readonly record struct UsbPlugKey(byte Bus, string PortPath)
{
	public override string ToString() => $"{Bus}-{PortPath}";
}

internal sealed record UsbDeviceInfo(UsbPlugKey Plug, ushort VendorId, ushort ProductId, byte DeviceClass, int Index);

internal sealed record UsbInterfaceInfo(byte Number, byte Class, byte SubClass, byte Protocol, byte NameIndex);

internal sealed record UsbDeviceStrings(
	string? Manufacturer,
	string? Product,
	string? Serial,
	IReadOnlyDictionary<byte, string> InterfaceNames);

internal enum UsbTransferStatus
{
	Completed,

	TimedOut,

	Failed
}

internal readonly record struct UsbTransferResult(UsbTransferStatus Status, int Transferred);

internal interface IUsbBulkPipe : IDisposable
{
	UsbTransferResult Read(byte[] buffer, int timeoutMilliseconds);

	UsbTransferResult Write(byte[] buffer, int offset, int count, int timeoutMilliseconds);
}

internal enum AccessorySwitchOutcome
{
	Switched,

	NotSupported,

	Failed
}

internal interface IUsbHost
{
	bool Available { get; }

	IUsbDeviceList Enumerate();
}

internal interface IUsbDeviceList : IDisposable
{
	IReadOnlyList<UsbDeviceInfo> Devices { get; }

	IReadOnlyList<UsbInterfaceInfo>? ReadInterfaces(UsbDeviceInfo device);

	UsbDeviceStrings? ReadStrings(UsbDeviceInfo device, IReadOnlyCollection<byte> interfaceNameIndexes);

	AccessorySwitchOutcome SwitchToAccessory(UsbDeviceInfo device, IReadOnlyList<string> identification);

	IUsbBulkPipe? OpenAccessory(UsbDeviceInfo device);
}

// Android Open Accessory. Manufacturer and model are a contract with the Companion app's accessory
// filter; the description is what Android names in its prompt, so it carries this computer's name.
internal static class AccessoryProtocol
{
	public const ushort GoogleVendorId = 0x18D1;
	public const ushort AppleVendorId = 0x05AC;

	public const byte GetProtocolRequest = 51;
	public const byte SendStringRequest = 52;
	public const byte StartRequest = 53;

	public const byte VendorIn = 0xC0;
	public const byte VendorOut = 0x40;

	public static readonly IReadOnlySet<ushort> AccessoryProductIds = new HashSet<ushort> { 0x2D00, 0x2D01, 0x2D04, 0x2D05 };

	public const int MaxDescriptionLength = 64;

	// Android keeps each accessory string in a 256 byte buffer including its terminator.
	public const int MaxDescriptionBytes = 200;

	private const string FallbackDescription = "Macro Deck";

	public static IReadOnlyList<string> IdentificationStrings(string? hostName) =>
	[
		"Macro Deck",
		"Macro Deck Companion",
		Description(hostName),
		"1",
		"https://macro-deck.app",
		""
	];

	private static string Description(string? hostName)
	{
		var trimmed = hostName?.Trim() ?? string.Empty;
		if (trimmed.Length == 0)
		{
			return FallbackDescription;
		}

		var elements = StringInfo.GetTextElementEnumerator(trimmed);
		var builder = new StringBuilder();
		var bytes = 0;
		for (var count = 0; count < MaxDescriptionLength && elements.MoveNext(); count++)
		{
			var element = elements.GetTextElement();
			bytes += Encoding.UTF8.GetByteCount(element);
			if (bytes > MaxDescriptionBytes)
			{
				break;
			}

			builder.Append(element);
		}

		var description = builder.ToString().TrimEnd();
		return description.Length == 0 ? FallbackDescription : description;
	}

	public static bool IsAccessory(ushort vendorId, ushort productId)
		=> vendorId == GoogleVendorId && AccessoryProductIds.Contains(productId);
}
