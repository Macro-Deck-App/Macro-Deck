using MacroDeckHost.Application.Backups;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Backups;

[TestFixture]
public class BackupAudioDeviceFileTests
{
	[Test]
	public void The_known_audio_device_file_travels_with_the_variables()
	{
		Assert.That(BackupComponentGroups.Owner("data/system-audio-devices.json"),
			Is.EqualTo(BackupComponentGroup.Variables));
	}

	[Test]
	public void Remembered_devices_in_integration_storage_travel_with_the_integrations()
	{
		Assert.That(BackupComponentGroups.Owner("data/integration-data/com.example.audio/system-audio-devices.json"),
			Is.EqualTo(BackupComponentGroup.Integrations));
	}

	[TestCase(BackupComponentGroup.Profiles)]
	[TestCase(BackupComponentGroup.Automations)]
	public void Restoring_data_that_names_variables_brings_the_names_the_integrations_pinned(
		BackupComponentGroup group)
	{
		var effective = BackupComponentSelection.Expand([group]).Effective;

		Assert.That(effective,
			Is.SupersetOf(new[] { BackupComponentGroup.Variables, BackupComponentGroup.Integrations }));
	}
}
