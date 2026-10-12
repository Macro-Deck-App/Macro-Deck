using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Batteries;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

[TestFixture]
public class FakeBatteryApiTests
{
	[Test]
	public void Setting_devices_raises_changed_with_the_removed_ids_and_serves_find()
	{
		var api = new FakeBatteryApi();
		BatteryDevicesChangedEventArgs? raised = null;
		api.Changed += (_, args) => raised = args;

		api.Set([new BatteryDeviceSnapshot { Id = "p:a", LocalId = "a", ProviderId = "p", Name = "A" }], ["p:b"]);

		Assert.Multiple(() =>
		{
			Assert.That(raised?.RemovedIds.Single(), Is.EqualTo("p:b"));
			Assert.That(api.Find("p:a")?.Name, Is.EqualTo("A"));
			Assert.That(api.Find("p:b"), Is.Null);
		});
	}
}
