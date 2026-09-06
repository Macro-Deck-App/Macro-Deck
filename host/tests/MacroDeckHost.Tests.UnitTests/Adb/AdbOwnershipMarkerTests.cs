using MacroDeckHost.Infrastructure.Adb;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Adb;

public class AdbOwnershipMarkerTests
{
	private TestPaths _paths = null!;

	[SetUp]
	public void SetUp() => _paths = new TestPaths();

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public void Write_then_Read_round_trips_the_state()
	{
		var marker = new AdbOwnershipMarker(_paths, new LoggerConfiguration().CreateLogger());
		var written = new AdbOwnershipState(4242,
			true,
			[new AdbOwnedTunnel("R58M12ABCDE", 8193, 8193), new AdbOwnedTunnel("OTHERSERIAL", 8194, 8193)],
			new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

		marker.Write(written);
		var read = marker.Read();

		Assert.That(read, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(read!.ProcessId, Is.EqualTo(written.ProcessId));
			Assert.That(read.StartedServer, Is.EqualTo(written.StartedServer));
			Assert.That(read.Tunnels, Is.EqualTo(written.Tunnels));
			Assert.That(read.WrittenAt, Is.EqualTo(written.WrittenAt));
		});
	}

	[Test]
	public void Read_returns_null_without_throwing_when_no_file_has_ever_been_written()
	{
		var marker = new AdbOwnershipMarker(_paths, new LoggerConfiguration().CreateLogger());

		Assert.That(marker.Read(), Is.Null);
	}

	[Test]
	public void Read_returns_null_without_throwing_when_the_file_is_corrupt()
	{
		var marker = new AdbOwnershipMarker(_paths, new LoggerConfiguration().CreateLogger());
		Directory.CreateDirectory(_paths.ConfigDirectory);
		File.WriteAllText(Path.Combine(_paths.ConfigDirectory, "adb-state.json"), "{ this is not valid json");

		Assert.That(marker.Read(), Is.Null);
	}

	[Test]
	public void Read_returns_null_without_throwing_when_the_file_is_empty()
	{
		var marker = new AdbOwnershipMarker(_paths, new LoggerConfiguration().CreateLogger());
		Directory.CreateDirectory(_paths.ConfigDirectory);
		File.WriteAllText(Path.Combine(_paths.ConfigDirectory, "adb-state.json"), string.Empty);

		Assert.That(marker.Read(), Is.Null);
	}

	[Test]
	public void Delete_removes_the_file_so_a_subsequent_read_returns_null()
	{
		var marker = new AdbOwnershipMarker(_paths, new LoggerConfiguration().CreateLogger());
		marker.Write(new AdbOwnershipState(1, false, [], DateTimeOffset.UtcNow));

		marker.Delete();

		Assert.That(marker.Read(), Is.Null);
	}

	[Test]
	public void Delete_of_a_file_that_never_existed_does_not_throw()
	{
		var marker = new AdbOwnershipMarker(_paths, new LoggerConfiguration().CreateLogger());

		Assert.DoesNotThrow(() => marker.Delete());
	}

	[Test]
	public async Task A_marker_present_before_construction_makes_the_manager_report_an_unclean_previous_shutdown()
	{
		var preexisting = new AdbOwnershipMarker(_paths, new LoggerConfiguration().CreateLogger());
		preexisting.Write(new AdbOwnershipState(999,
			false,
			[new AdbOwnedTunnel("R58M12ABCDE", 8193, 8193), new AdbOwnedTunnel("OTHERSERIAL", 8194, 8193)],
			DateTimeOffset.UtcNow));

		using var harness = new AdbManagerHarness(paths: _paths);
		harness.Runner.When(argv => argv.Contains("--list"),
			new AdbProcessResult(true, 0, string.Empty, string.Empty, false));

		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(harness.Manager.Status.PreviousShutdownWasUnclean, Is.True);
			Assert.That(harness.Manager.Status.StaleTunnelsCleaned, Is.EqualTo(2));
		});
	}

	[Test]
	public async Task No_marker_present_before_construction_means_a_clean_previous_shutdown()
	{
		using var harness = new AdbManagerHarness();

		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(harness.Manager.Status.PreviousShutdownWasUnclean, Is.False);
			Assert.That(harness.Manager.Status.StaleTunnelsCleaned, Is.Zero);
		});
	}
}
