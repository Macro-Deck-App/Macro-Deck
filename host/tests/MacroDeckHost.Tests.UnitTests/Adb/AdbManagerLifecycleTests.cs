using System.Diagnostics;
using MacroDeckHost.Infrastructure.Adb;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Adb;

public class AdbManagerLifecycleTests
{
	private const string Serial = "R58M12ABCDE";

	[Test]
	public async Task ShutdownAsync_completes_within_its_cap_even_when_every_adb_call_hangs()
	{
		using var harness = new AdbManagerHarness();
		harness.Runner.When(argv => argv.Contains("--list"),
			new AdbProcessResult(true, 0, string.Empty, string.Empty, false));
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		var marker = new AdbOwnershipMarker(harness.Paths, new LoggerConfiguration().CreateLogger());
		marker.Write(new AdbOwnershipState(1, false, [new AdbOwnedTunnel(Serial, 8193, 8193)], DateTimeOffset.UtcNow));

		harness.Runner.HangForever = new TaskCompletionSource<AdbProcessResult>();
		var stopwatch = Stopwatch.StartNew();

		await harness.Manager.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
		stopwatch.Stop();

		Assert.That(stopwatch.Elapsed,
			Is.LessThan(TimeSpan.FromSeconds(5)),
			"ShutdownAsync must enforce its own cap rather than waiting out a hung adb call");
	}

	[Test]
	public async Task ShutdownAsync_removes_every_owned_tunnel_even_when_one_device_fails()
	{
		using var harness = new AdbManagerHarness();
		const string other = "OTHERSERIAL";
		harness.Runner.When(argv => argv.Contains("--list"),
			new AdbProcessResult(true, 0, string.Empty, string.Empty, false));
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		var marker = new AdbOwnershipMarker(harness.Paths, new LoggerConfiguration().CreateLogger());
		marker.Write(new AdbOwnershipState(1,
			false,
			[new AdbOwnedTunnel(Serial, 8193, 8193), new AdbOwnedTunnel(other, 8194, 8193)],
			DateTimeOffset.UtcNow));
		harness.Runner.When(argv => argv.Contains("--remove") && argv.Contains(Serial),
			_ => throw new IOException("device disconnected mid-call"));

		await harness.Manager.ShutdownAsync();

		var removeCalls = harness.Runner.Invocations.Where(argv => argv.Contains("--remove")).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(removeCalls.Any(argv => argv.Contains(Serial)),
				Is.True,
				"the failing device's removal must still be attempted");
			Assert.That(removeCalls.Any(argv => argv.Contains(other)),
				Is.True,
				"one device's failure must not skip the others");
		});
	}

	[Test]
	public async Task ShutdownAsync_deletes_the_ownership_marker_after_a_clean_pass()
	{
		using var harness = new AdbManagerHarness();
		harness.Runner.When(argv => argv.Contains("--list"),
			new AdbProcessResult(true, 0, string.Empty, string.Empty, false));
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		var marker = new AdbOwnershipMarker(harness.Paths, new LoggerConfiguration().CreateLogger());
		marker.Write(new AdbOwnershipState(1, false, [new AdbOwnedTunnel(Serial, 8193, 8193)], DateTimeOffset.UtcNow));
		Assert.That(marker.Read(),
			Is.Not.Null,
			"the marker must exist before shutdown for this test to prove anything");

		await harness.Manager.ShutdownAsync();

		Assert.That(marker.Read(), Is.Null);
	}

	[Test]
	public async Task ShutdownAsync_never_invokes_kill_server()
	{
		using var harness = new AdbManagerHarness();
		harness.Runner.When(argv => argv.Contains("--list"),
			new AdbProcessResult(true, 0, string.Empty, string.Empty, false));
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		await harness.Manager.ShutdownAsync();

		Assert.That(harness.Runner.Invocations.Any(argv => argv.Contains("kill-server")), Is.False);
	}

	[Test]
	public async Task Disabling_adb_never_invokes_kill_server()
	{
		using var harness = new AdbManagerHarness();
		harness.Runner.When(argv => argv.Contains("--list"),
			new AdbProcessResult(true, 0, string.Empty, string.Empty, false));
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		harness.PreferenceService.AdbSettings = harness.PreferenceService.AdbSettings with { Enabled = false };
		await harness.Manager.ApplySettingsAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(harness.Runner.Invocations.Any(argv => argv.Contains("kill-server")), Is.False);
			Assert.That(harness.Manager.Status.Enabled, Is.False);
		});
	}

	[Test]
	public async Task RestartServerAsync_is_the_only_path_that_invokes_kill_server()
	{
		using var harness = new AdbManagerHarness();
		harness.Runner.When(argv => argv.Contains("--list"),
			new AdbProcessResult(true, 0, string.Empty, string.Empty, false));
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		var result = await harness.Manager.RestartServerAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(harness.Runner.Invocations.Any(argv => argv.Contains("kill-server")), Is.True);
		});
	}
}
