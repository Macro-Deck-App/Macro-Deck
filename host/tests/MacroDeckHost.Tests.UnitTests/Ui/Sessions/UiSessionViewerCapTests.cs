using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Sessions;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

[TestFixture]
internal sealed class UiSessionViewerCapTests : UiSessionFixture
{
	[Test]
	public void A_viewer_can_open_its_widgets_while_another_viewer_holds_the_full_cap()
	{
		var provider = AddProvider();

		for (var index = 0; index < ProtocolLimits.MaxUiWidgetSessionsPerProvider; index++)
		{
			Assert.That(OpenWidget(provider, DeviceA).Accepted, Is.True, "A viewer was refused below the cap.");
		}

		var otherViewer = OpenWidget(provider, DeviceB);
		var sameViewerOverCap = OpenWidget(provider, DeviceA);

		Assert.Multiple(() =>
		{
			Assert.That(otherViewer.Accepted, Is.True, "Another viewer's tiles counted against this viewer's cap.");
			Assert.That(sameViewerOverCap.Accepted, Is.False, "The cap no longer bounds a single viewer.");
			Assert.That(sameViewerOverCap.Code, Is.EqualTo(UiSessionErrorCodes.TooManySessions));
		});
	}

	[TestCase(UiSurfaceKinds.Folder)]
	[TestCase(UiSurfaceKinds.ScreenSaver)]
	public void A_device_can_show_its_view_while_other_devices_hold_the_full_cap(string kind)
	{
		var provider = AddProvider();

		for (var index = 0; index < ProtocolLimits.MaxUiSessionsPerProvider; index++)
		{
			Assert.That(Open(provider, kind, $"device-{index}").Accepted, Is.True);
		}

		var anotherDevice = Enumerable.Range(0, ProtocolLimits.MaxUiSessionsPerProvider)
			.Select(_ => Open(provider, kind, DeviceB).Accepted)
			.ToList();
		var sameDeviceOverCap = Open(provider, kind, DeviceB);

		Assert.Multiple(() =>
		{
			Assert.That(anotherDevice, Is.All.True, "Other devices' views counted against this device.");
			Assert.That(sameDeviceOverCap.Code, Is.EqualTo(UiSessionErrorCodes.TooManySessions));
		});
	}

	[Test]
	public void A_configuration_surface_opens_while_every_device_shows_a_view_of_the_provider()
	{
		var provider = AddProvider();

		for (var index = 0; index < ProtocolLimits.MaxUiSessionsPerProvider; index++)
		{
			Open(provider, index % 2 == 0 ? UiSurfaceKinds.ScreenSaver : UiSurfaceKinds.Folder, $"device-{index}");
		}

		Assert.That(Open(provider, UiSurfaceKinds.Config, DeviceA).Accepted,
			Is.True,
			"Devices showing a view took the configuration surfaces' slots.");
	}

	[Test]
	public void A_device_view_opens_while_the_configuration_surfaces_are_full()
	{
		var provider = AddProvider();

		for (var index = 0; index < ProtocolLimits.MaxUiSessionsPerProvider; index++)
		{
			Assert.That(Open(provider, UiSurfaceKinds.Config, $"device-{index}").Accepted, Is.True);
		}

		Assert.That(Open(provider, UiSurfaceKinds.Folder, DeviceA).Accepted,
			Is.True,
			"Configuration surfaces took a device's view slot.");
	}

	[Test]
	public async Task The_configuration_surface_cap_is_still_shared_by_the_provider()
	{
		var provider = AddProvider();

		for (var index = 0; index < ProtocolLimits.MaxUiSessionsPerProvider; index++)
		{
			await OpenAsync(provider, principal: index % 2 == 0 ? DeviceA : DeviceB);
		}

		var refused = Broker.Open(provider.ProviderId, Surface(), DeviceB);

		Assert.That(refused.Code, Is.EqualTo(UiSessionErrorCodes.TooManySessions));
	}

	private UiSessionOpenTicket OpenWidget(StubUiSessionProvider provider, string principal)
	{
		return Open(provider, UiSurfaceKinds.Widget, principal);
	}

	private UiSessionOpenTicket Open(StubUiSessionProvider provider, string kind, string principal)
	{
		return Broker.Open(provider.ProviderId, Surface(UiSessionModes.Shared, kind), principal);
	}
}
