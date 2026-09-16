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
}
