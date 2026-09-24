using MacroDeckHost.Infrastructure.ClientTargets.CarThing;

namespace MacroDeckHost.Infrastructure.Usb.Native;

internal sealed record AndroidDescriptorVerdict(IReadOnlyList<byte> MtpNameIndexes);

internal static class AndroidCandidateFilter
{
	private static readonly byte[] _deviceClasses = [0x00, 0xEF, 0xFF];

	public static AndroidDescriptorVerdict? ClassifyDescriptors(UsbDeviceInfo device,
		IReadOnlyList<UsbInterfaceInfo> interfaces)
	{
		if (device.VendorId == AccessoryProtocol.AppleVendorId || !_deviceClasses.Contains(device.DeviceClass))
		{
			return null;
		}

		var mtpNameIndexes = new List<byte>();
		foreach (var usbInterface in interfaces)
		{
			if (IsAdb(usbInterface))
			{
				continue;
			}

			if (IsVendorMtp(usbInterface))
			{
				mtpNameIndexes.Add(usbInterface.NameIndex);
			}
			else if (!IsStillImage(usbInterface))
			{
				return null;
			}
		}

		return new AndroidDescriptorVerdict(mtpNameIndexes);
	}

	public static bool AcceptStrings(AndroidDescriptorVerdict verdict, UsbDeviceStrings strings)
		=> !CarThingIdentity.ContainsMarker(strings.Product) &&
			verdict.MtpNameIndexes.All(index =>
				strings.InterfaceNames.TryGetValue(index, out var name) &&
				string.Equals(name, "MTP", StringComparison.Ordinal));

	private static bool IsAdb(UsbInterfaceInfo usbInterface)
		=> usbInterface is { Class: 0xFF, SubClass: 0x42, Protocol: 0x01 };

	private static bool IsStillImage(UsbInterfaceInfo usbInterface)
		=> usbInterface is { Class: 0x06, SubClass: 0x01, Protocol: 0x01 };

	private static bool IsVendorMtp(UsbInterfaceInfo usbInterface)
		=> usbInterface is { Class: 0xFF, SubClass: 0xFF, Protocol: 0x00 };
}
