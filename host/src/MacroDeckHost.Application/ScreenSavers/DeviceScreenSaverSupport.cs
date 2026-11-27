using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.ScreenSavers;

public static class DeviceScreenSaverSupport
{
	public const string NotSupportedMessage = "This device does not show screensavers.";

	public static bool IsSupported(DeviceEntity device)
	{
		if (!LayoutSnapshotSerializer.TryDeserialize(device.LayoutSnapshot, out var layout))
		{
			return true;
		}

		var declared = layout.PrimaryGrid?.Visuals?.ScreenSavers ?? layout.Capabilities?.Visuals?.ScreenSavers;

		return declared != false;
	}
}
