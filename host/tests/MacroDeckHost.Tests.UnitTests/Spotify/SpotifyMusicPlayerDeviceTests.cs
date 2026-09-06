using MacroDeckHost.Integrations.Spotify;
using SpotifyAPI.Web;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifyMusicPlayerDeviceTests
{
	[Test]
	public void MapDevices_SkipsNullRestrictedAndIdLessEntries()
	{
		var valid = new Device
		{
			Id = "device-1",
			Name = "Living Room",
			Type = "Speaker",
			IsActive = true,
			VolumePercent = 42
		};
		var withoutId = new Device { Name = "No Id", Type = "Computer" };
		var restricted = new Device { Id = "device-2", Name = "Restricted", IsRestricted = true };

		var devices = SpotifyMusicPlayer.MapDevices([null, valid, withoutId, restricted]);

		Assert.That(devices, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(devices[0].Id, Is.EqualTo("device-1"));
			Assert.That(devices[0].Name, Is.EqualTo("Living Room"));
			Assert.That(devices[0].Type, Is.EqualTo("Speaker"));
			Assert.That(devices[0].IsActive, Is.True);
			Assert.That(devices[0].VolumePercent, Is.EqualTo(42));
		});
	}
}
