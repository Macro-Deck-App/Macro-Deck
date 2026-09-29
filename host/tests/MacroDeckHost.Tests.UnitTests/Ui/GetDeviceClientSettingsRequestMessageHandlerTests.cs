using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Devices;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Ui;

public class GetDeviceClientSettingsRequestMessageHandlerTests
{
	private InMemoryDeviceRepository _devices = null!;
	private GetDeviceClientSettingsRequestMessageHandler _handler = null!;

	[SetUp]
	public void SetUp()
	{
		_devices = new InMemoryDeviceRepository();
		_handler = new GetDeviceClientSettingsRequestMessageHandler(_devices);
	}

	private async Task<DeviceEntity> SeedDevice(bool settingsButtonHidden)
	{
		var device = new DeviceEntity
		{
			Id = Guid.NewGuid(),
			SecretHash = "hash",
			Name = "Kitchen tablet",
			SettingsButtonHidden = settingsButtonHidden
		};
		await _devices.Create(device);
		return device;
	}

	[Test]
	public async Task The_device_reads_its_own_stored_setting()
	{
		var hidden = await SeedDevice(settingsButtonHidden: true);
		var shown = await SeedDevice(settingsButtonHidden: false);

		var hiddenResponse = await _handler.Handle(new GetDeviceClientSettingsRequest { DeviceId = hidden.Id },
			CancellationToken.None);
		var shownResponse = await _handler.Handle(new GetDeviceClientSettingsRequest { DeviceId = shown.Id },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(hiddenResponse.SettingsButtonHidden, Is.True);
			Assert.That(shownResponse.SettingsButtonHidden, Is.False);
		});
	}

	[Test]
	public async Task A_connection_without_a_known_device_keeps_the_settings_button()
	{
		var unknown = await _handler.Handle(new GetDeviceClientSettingsRequest { DeviceId = Guid.NewGuid() },
			CancellationToken.None);
		var noClaim = await _handler.Handle(new GetDeviceClientSettingsRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(unknown.SettingsButtonHidden, Is.False);
			Assert.That(noClaim.SettingsButtonHidden, Is.False);
		});
	}
}
