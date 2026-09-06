using MacroDeckHost.Infrastructure.Adb;

namespace MacroDeckHost.Tests.UnitTests.Adb;

/// <summary>
/// The property probe against a device that cannot answer it (issue #727). `dumpsys` is an Android
/// command; a Linux appliance reached over adb has none, and asking it every few seconds forever is
/// not merely wasted work - a jailbroken Car Thing dropped off the bus under the repeated shell
/// sessions, which is what the Devices page triggered.
/// </summary>
[TestFixture]
public class AdbManagerPropertyProbeTests
{
	private const string Serial = "123456";

	private static int DumpsysCallCount(FakeAdbProcessRunner runner)
		=> runner.Invocations.Count(argv => argv.Any(arg => arg.StartsWith("dumpsys", StringComparison.Ordinal)));

	private static async Task GivenOnlineDeviceAsync(AdbManagerHarness harness)
	{
		harness.Runner.When(argv => argv.Contains("devices"),
			new AdbProcessResult(true, 0, $"List of devices attached\n{Serial}\tdevice\n", string.Empty, false));
		await harness.Manager.RefreshNowAsync(CancellationToken.None);
	}

	[Test]
	public async Task A_device_that_answers_nothing_is_probed_once_and_then_left_alone()
	{
		using var harness = new AdbManagerHarness();
		// Everything the probe asks for fails the way a device without dumpsys answers it.
		harness.Runner.DefaultResult = new AdbProcessResult(true, 127, string.Empty, "not found", false);
		await GivenOnlineDeviceAsync(harness);
		await harness.Manager.GetPropertiesAsync(Serial, CancellationToken.None);

		await harness.Manager.ProbePropertiesNowAsync(CancellationToken.None);
		var afterFirstProbe = DumpsysCallCount(harness.Runner);
		await harness.Manager.ProbePropertiesNowAsync(CancellationToken.None);
		await harness.Manager.ProbePropertiesNowAsync(CancellationToken.None);

		Assert.That(DumpsysCallCount(harness.Runner),
			Is.EqualTo(afterFirstProbe),
			"a device with no dumpsys is not going to grow one while it stays plugged in");
	}

	[Test]
	public async Task A_device_that_answers_keeps_being_probed()
	{
		using var harness = new AdbManagerHarness();
		harness.Runner.DefaultResult = new AdbProcessResult(true, 0, "level: 80", string.Empty, false);
		await GivenOnlineDeviceAsync(harness);
		await harness.Manager.GetPropertiesAsync(Serial, CancellationToken.None);

		await harness.Manager.ProbePropertiesNowAsync(CancellationToken.None);
		var afterFirstProbe = DumpsysCallCount(harness.Runner);
		await harness.Manager.ProbePropertiesNowAsync(CancellationToken.None);

		Assert.That(DumpsysCallCount(harness.Runner),
			Is.GreaterThan(afterFirstProbe),
			"battery level and screen state do change, so a device that reports them must stay polled");
	}
}
