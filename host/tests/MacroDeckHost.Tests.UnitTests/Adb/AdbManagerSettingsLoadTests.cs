using MacroDeckHost.Application.Services;

namespace MacroDeckHost.Tests.UnitTests.Adb;

[TestFixture]
public class AdbManagerSettingsLoadTests
{
	[Test]
	public void Status_reports_disabled_before_anything_has_read_the_stored_preferences()
	{
		using var harness = new AdbManagerHarness();
		harness.PreferenceService.AdbSettings = new AdbSettings(true, "fake-adb", true, null);

		Assert.That(harness.Manager.Status.Enabled,
			Is.False,
			"a freshly constructed manager has not read preferences yet");
	}

	[Test]
	public async Task ApplySettingsAsync_loads_the_stored_preferences_and_enables_the_manager()
	{
		using var harness = new AdbManagerHarness();
		harness.PreferenceService.AdbSettings = new AdbSettings(true, "fake-adb", true, null);

		await harness.Manager.ApplySettingsAsync(CancellationToken.None);

		Assert.That(harness.Manager.Status.Enabled, Is.True);
	}

	[Test]
	public async Task WaitForEnabledAsync_parks_on_a_fresh_manager_even_when_the_stored_setting_is_on()
	{
		using var harness = new AdbManagerHarness();
		harness.PreferenceService.AdbSettings = new AdbSettings(true, "fake-adb", true, null);

		var parked = harness.Manager.WaitForEnabledAsync(CancellationToken.None);
		var finished = await Task.WhenAny(parked, Task.Delay(TimeSpan.FromMilliseconds(250)));

		Assert.That(finished, Is.Not.SameAs(parked), "the gate must be reached before settings are loaded");
	}

	// Every installation starts with adb off. If the disabled snapshot reported the platform as
	// unsupported, the settings pane would disable the very switch that turns adb on and the feature
	// would be unreachable out of the box.
	[Test]
	public async Task A_disabled_manager_still_reports_the_platform_as_supported()
	{
		using var harness = new AdbManagerHarness();
		harness.PreferenceService.AdbSettings = new AdbSettings(false, null, false, null);

		await harness.Manager.ApplySettingsAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(harness.Manager.Status.Enabled, Is.False);
			Assert.That(harness.Manager.Status.Supported, Is.True, "the user must still be able to turn adb on");
		});
	}

	[Test]
	public async Task ApplySettingsAsync_keeps_the_manager_disabled_when_the_stored_setting_is_off()
	{
		using var harness = new AdbManagerHarness();
		harness.PreferenceService.AdbSettings = new AdbSettings(false, null, false, null);

		await harness.Manager.ApplySettingsAsync(CancellationToken.None);

		Assert.That(harness.Manager.Status.Enabled, Is.False);
	}
}
