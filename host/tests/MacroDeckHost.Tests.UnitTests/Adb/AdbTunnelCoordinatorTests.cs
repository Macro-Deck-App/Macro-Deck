using MacroDeckHost.Application.Adb;
using MacroDeckHost.Infrastructure.Adb;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Adb;

public class AdbTunnelCoordinatorTests
{
	private const string ExecutablePath = "fake-adb";
	private const string Serial = "R58M12ABCDE";

	private TestPaths _paths = null!;

	[SetUp]
	public void SetUp() => _paths = new TestPaths();

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	// --- Security: the host-side port is always the public port, never the loopback port. -------------

	[Test]
	public async Task Security_first_candidate_uses_the_public_port_and_never_the_loopback_port()
	{
		var (coordinator, runner, _) = Create(publicPort: 8193, loopbackPort: 51234);

		var results = await coordinator.ReconcileAsync([AuthorizedDevice()], CancellationToken.None);

		var tunnel = results[Serial];
		Assert.Multiple(() =>
		{
			Assert.That(tunnel.Established, Is.True);
			Assert.That(tunnel.DevicePort, Is.EqualTo(AdbUsbTunnelPorts.DeviceSideCandidates[0]));
			Assert.That(tunnel.HostPort, Is.EqualTo(8193));
			AssertLoopbackPortNeverAppears(runner, 51234);
		});
	}

	[Test]
	public async Task Security_falling_through_to_the_second_candidate_still_uses_the_public_port_never_loopback()
	{
		var (coordinator, runner, _) = Create(publicPort: 8193, loopbackPort: 51234);
		var firstCandidate = AdbUsbTunnelPorts.DeviceSideCandidates[0];
		var secondCandidate = AdbUsbTunnelPorts.DeviceSideCandidates[1];
		runner.When(argv => IsNoRebindCreate(argv, firstCandidate),
			new AdbProcessResult(true, 1, string.Empty, "error: already reversed", false));

		var results = await coordinator.ReconcileAsync([AuthorizedDevice()], CancellationToken.None);

		var tunnel = results[Serial];
		Assert.Multiple(() =>
		{
			Assert.That(tunnel.Established, Is.True);
			Assert.That(tunnel.DevicePort, Is.EqualTo(secondCandidate));
			Assert.That(tunnel.HostPort, Is.EqualTo(8193));
			AssertLoopbackPortNeverAppears(runner, 51234);
		});
	}

	[Test]
	public async Task Security_adopting_an_existing_mapping_still_uses_the_public_port_never_loopback()
	{
		var (coordinator, runner, _) = Create(publicPort: 8193, loopbackPort: 51234);
		var candidate = AdbUsbTunnelPorts.DeviceSideCandidates[1];
		runner.When(argv => argv.Contains("--list"),
			ListResult($"{Serial} tcp:{candidate} tcp:8193\n"));

		var results = await coordinator.ReconcileAsync([AuthorizedDevice()], CancellationToken.None);

		var tunnel = results[Serial];
		Assert.Multiple(() =>
		{
			Assert.That(tunnel.Established, Is.True);
			Assert.That(tunnel.DevicePort, Is.EqualTo(candidate));
			Assert.That(tunnel.HostPort, Is.EqualTo(8193));
			Assert.That(runner.Invocations.Any(argv => argv.Contains("--no-rebind")),
				Is.False,
				"adopting an exact match must not call reverse create");
			AssertLoopbackPortNeverAppears(runner, 51234);
		});
	}

	// A loopback reverse tunnel would hand an unauthenticated phone a synthetic admin principal (see
	// the comment in AdbTunnelCoordinator.ReconcileAsync), so when the public listener could not be
	// opened at all (issue #515) the only safe outcome is no tunnel and no adb call whatsoever - not a
	// tunnel pointed at loopback, and not even a probing --list call.
	[Test]
	public async Task Security_no_reverse_call_happens_at_all_while_the_public_listener_is_unavailable()
	{
		var runner = new FakeAdbProcessRunner();
		var marker = new AdbOwnershipMarker(_paths, new LoggerConfiguration().CreateLogger());
		var listenerState = new FakeHostListenerState
		{
			PublicPort = 8193, LoopbackPort = 51234, PublicListenerAvailable = false
		};
		var coordinator
			= new AdbTunnelCoordinator(runner, listenerState, marker, new LoggerConfiguration().CreateLogger());
		coordinator.SetExecutablePath(ExecutablePath);

		var results = await coordinator.ReconcileAsync([AuthorizedDevice()], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(results, Does.Not.ContainKey(Serial));
			Assert.That(runner.Invocations, Is.Empty, "not even a --list call should happen");
			AssertLoopbackPortNeverAppears(runner, 51234);
		});
	}

	private static void AssertLoopbackPortNeverAppears(FakeAdbProcessRunner runner, int loopbackPort)
	{
		var loopbackSpec = "tcp:" + loopbackPort;
		Assert.That(runner.Invocations.Any(argv => argv.Any(a => a.Contains(loopbackSpec, StringComparison.Ordinal))),
			Is.False,
			"the loopback port must never appear in any adb argv");
	}

	// --- Adoption, staleness, --no-rebind, ordering, and failure reporting. -----------------------------

	[Test]
	public async Task An_exact_existing_mapping_is_adopted_without_any_reverse_create_call()
	{
		var (coordinator, runner, _) = Create(publicPort: 8193);
		runner.When(argv => argv.Contains("--list"), ListResult($"{Serial} tcp:8194 tcp:8193\n"));

		var results = await coordinator.ReconcileAsync([AuthorizedDevice()], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(results[Serial].Established, Is.True);
			Assert.That(results[Serial].DevicePort, Is.EqualTo(8194));
			Assert.That(runner.Invocations, Has.Count.EqualTo(1), "only the --list call should have happened");
		});
	}

	[Test]
	public async Task A_recorded_stale_mapping_is_removed_before_a_new_one_is_created()
	{
		var (coordinator, runner, marker) = Create(publicPort: 8193);
		marker.Write(
			new AdbOwnershipState(1234, false, [new AdbOwnedTunnel(Serial, 8193, 8192)], DateTimeOffset.UtcNow));
		runner.When(argv => argv.Contains("--list"), ListResult(string.Empty));

		var results = await coordinator.ReconcileAsync([AuthorizedDevice()], CancellationToken.None);

		var removeIndex = runner.Invocations.ToList().FindIndex(argv => argv.Contains("--remove"));
		var createIndex = runner.Invocations.ToList().FindIndex(argv => argv.Contains("--no-rebind"));
		Assert.Multiple(() =>
		{
			Assert.That(results[Serial].Established, Is.True);
			Assert.That(removeIndex, Is.GreaterThanOrEqualTo(0), "the stale mapping must be removed");
			Assert.That(runner.Invocations[removeIndex],
				Is.EqualTo(new[] { "-s", Serial, "reverse", "--remove", "tcp:8193" }));
			Assert.That(createIndex, Is.GreaterThanOrEqualTo(0));
			Assert.That(removeIndex,
				Is.LessThan(createIndex),
				"removal of the stale mapping must happen before creation");
		});
	}

	[Test]
	public async Task No_stale_removal_call_happens_when_nothing_is_recorded_for_the_serial()
	{
		var (coordinator, runner, _) = Create(publicPort: 8193);
		runner.When(argv => argv.Contains("--list"), ListResult(string.Empty));

		await coordinator.ReconcileAsync([AuthorizedDevice()], CancellationToken.None);

		Assert.That(runner.Invocations.Any(argv => argv.Contains("--remove")), Is.False);
	}

	[Test]
	public async Task Every_create_call_includes_no_rebind()
	{
		var (coordinator, runner, _) = Create(publicPort: 8193);
		runner.When(argv => argv.Contains("--list"), ListResult(string.Empty));

		await coordinator.ReconcileAsync([AuthorizedDevice()], CancellationToken.None);

		var createCalls = runner.Invocations.Where(argv => argv.Contains("reverse") && !argv.Contains("--list"))
			.ToList();
		Assert.That(createCalls, Is.Not.Empty);
		Assert.That(createCalls, Has.All.Matches<IReadOnlyList<string>>(argv => argv.Contains("--no-rebind")));
	}

	[Test]
	public async Task Candidates_are_walked_in_AdbUsbTunnelPorts_order()
	{
		var (coordinator, runner, _) = Create(publicPort: 8193);
		runner.When(argv => argv.Contains("--list"), ListResult(string.Empty));
		var firstTwo = AdbUsbTunnelPorts.DeviceSideCandidates.Take(2).ToArray();
		foreach (var candidate in firstTwo)
		{
			runner.When(argv => IsNoRebindCreate(argv, candidate),
				new AdbProcessResult(true, 1, string.Empty, "busy", false));
		}

		var results = await coordinator.ReconcileAsync([AuthorizedDevice()], CancellationToken.None);

		var attemptedDevicePorts = runner.Invocations
			.Where(argv => argv.Contains("--no-rebind"))
			.Select(argv => argv[4])
			.ToList();
		Assert.Multiple(() =>
		{
			Assert.That(attemptedDevicePorts,
				Is.EqualTo(AdbUsbTunnelPorts.DeviceSideCandidates.Take(3).Select(port => "tcp:" + port).ToList()));
			Assert.That(results[Serial].DevicePort, Is.EqualTo(AdbUsbTunnelPorts.DeviceSideCandidates[2]));
		});
	}

	[Test]
	public async Task An_unauthorized_device_gets_no_tunnel_and_no_adb_calls()
	{
		var (coordinator, runner, _) = Create(publicPort: 8193);
		var device = AuthorizedDevice() with { State = AdbDeviceState.Unauthorized };

		var results = await coordinator.ReconcileAsync([device], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(results, Does.Not.ContainKey(Serial));
			Assert.That(runner.Invocations, Is.Empty);
		});
	}

	[Test]
	public async Task An_offline_device_gets_no_tunnel_and_no_adb_calls()
	{
		var (coordinator, runner, _) = Create(publicPort: 8193);
		var device = AuthorizedDevice() with { State = AdbDeviceState.Offline };

		var results = await coordinator.ReconcileAsync([device], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(results, Does.Not.ContainKey(Serial));
			Assert.That(runner.Invocations, Is.Empty);
		});
	}

	[Test]
	public async Task When_every_candidate_fails_the_tunnel_is_reported_unestablished_with_a_message()
	{
		var (coordinator, runner, _) = Create(publicPort: 8193);
		runner.When(argv => argv.Contains("--list"), ListResult(string.Empty));
		foreach (var candidate in AdbUsbTunnelPorts.DeviceSideCandidates)
		{
			runner.When(argv => IsNoRebindCreate(argv, candidate),
				new AdbProcessResult(true, 1, string.Empty, "busy", false));
		}

		var results = await coordinator.ReconcileAsync([AuthorizedDevice()], CancellationToken.None);

		var tunnel = results[Serial];
		Assert.Multiple(() =>
		{
			Assert.That(tunnel.Established, Is.False);
			Assert.That(tunnel.Failure, Is.Not.Null);
			Assert.That(tunnel.FailureMessage, Is.Not.Null.And.Not.Empty);
		});
	}

	// --- Devices are touched only when there is something to do (issue #727). ------------------------

	[Test]
	public async Task An_established_tunnel_is_not_re_checked_on_every_pass()
	{
		// A reverse mapping lives in the device's own adb transport and dies with it, so re-listing it
		// every few seconds only ever talks to the device for nothing. A Car Thing answered that with a
		// protocol fault and dropped off the bus, rebooted, and was polled again three seconds later.
		var (coordinator, runner, _) = Create(publicPort: 8193, loopbackPort: 51234);
		await coordinator.ReconcileAsync([AuthorizedDevice()], CancellationToken.None);
		var afterFirstPass = runner.Invocations.Count;

		for (var pass = 0; pass < 5; pass++)
		{
			await coordinator.ReconcileAsync([AuthorizedDevice()], CancellationToken.None);
		}

		Assert.That(runner.Invocations.Count,
			Is.EqualTo(afterFirstPass),
			"nothing changed, so the device should not have been contacted again at all");
	}

	[Test]
	public async Task A_settled_device_still_reports_its_tunnel_on_later_passes()
	{
		var (coordinator, _, _) = Create(publicPort: 8193, loopbackPort: 51234);
		await coordinator.ReconcileAsync([AuthorizedDevice()], CancellationToken.None);

		var results = await coordinator.ReconcileAsync([AuthorizedDevice()], CancellationToken.None);

		Assert.That(results[Serial].Established,
			Is.True,
			"skipping the device-side work must not make a working tunnel look absent");
	}

	[Test]
	public async Task A_device_that_reconnects_gets_its_tunnel_established_again()
	{
		// Unplugging ends the transport the mapping lived in, so the next session needs a real pass.
		var (coordinator, runner, _) = Create(publicPort: 8193, loopbackPort: 51234);
		await coordinator.ReconcileAsync([AuthorizedDevice()], CancellationToken.None);
		var afterFirstPass = runner.Invocations.Count;

		await coordinator.ReconcileAsync([], CancellationToken.None);
		var results = await coordinator.ReconcileAsync([AuthorizedDevice()], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(runner.Invocations.Count, Is.GreaterThan(afterFirstPass));
			Assert.That(results[Serial].Established, Is.True);
		});
	}

	[Test]
	public async Task A_changed_public_port_re_establishes_the_tunnel()
	{
		// The settled tunnel points at the old port, so it has to be redone rather than reused - the
		// device would otherwise keep dialling a listener that moved.
		var listenerState = new FakeHostListenerState { PublicPort = 8193, LoopbackPort = 51234 };
		var runner = new FakeAdbProcessRunner();
		var coordinator = new AdbTunnelCoordinator(runner,
			listenerState,
			new AdbOwnershipMarker(_paths, new LoggerConfiguration().CreateLogger()),
			new LoggerConfiguration().CreateLogger());
		coordinator.SetExecutablePath(ExecutablePath);

		await coordinator.ReconcileAsync([AuthorizedDevice()], CancellationToken.None);
		var afterFirstPass = runner.Invocations.Count;

		listenerState.PublicPort = 9000;
		var results = await coordinator.ReconcileAsync([AuthorizedDevice()], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(runner.Invocations.Count, Is.GreaterThan(afterFirstPass));
			Assert.That(results[Serial].HostPort, Is.EqualTo(9000));
		});
	}

	[Test]
	public async Task A_device_that_cannot_be_tunnelled_is_not_retried_on_every_pass()
	{
		// Retrying a failing device every few seconds is what turns one bad interaction into a loop it
		// never escapes.
		var (coordinator, runner, _) = Create(publicPort: 8193, loopbackPort: 51234);
		runner.DefaultResult = new AdbProcessResult(true, 1, string.Empty, "error: protocol fault", false);

		await coordinator.ReconcileAsync([AuthorizedDevice()], CancellationToken.None);
		var afterFirstAttempt = runner.Invocations.Count;
		await coordinator.ReconcileAsync([AuthorizedDevice()], CancellationToken.None);

		Assert.That(runner.Invocations.Count,
			Is.EqualTo(afterFirstAttempt),
			"the pass straight after a failure must back off rather than try again immediately");
	}

	[Test]
	public async Task A_device_that_starts_working_again_is_picked_up()
	{
		var (coordinator, runner, _) = Create(publicPort: 8193, loopbackPort: 51234);
		runner.DefaultResult = new AdbProcessResult(true, 1, string.Empty, "error: protocol fault", false);
		await coordinator.ReconcileAsync([AuthorizedDevice()], CancellationToken.None);

		runner.DefaultResult = new AdbProcessResult(true, 0, string.Empty, string.Empty, false);
		AdbTunnel? tunnel = null;
		for (var pass = 0; pass < 10 && tunnel?.Established != true; pass++)
		{
			var results = await coordinator.ReconcileAsync([AuthorizedDevice()], CancellationToken.None);
			results.TryGetValue(Serial, out tunnel);
		}

		Assert.That(tunnel?.Established, Is.True, "the back-off has to expire, not become permanent");
	}

	private (AdbTunnelCoordinator Coordinator, FakeAdbProcessRunner Runner, AdbOwnershipMarker Marker) Create(
		int publicPort,
		int? loopbackPort = null)
	{
		var runner = new FakeAdbProcessRunner();
		var marker = new AdbOwnershipMarker(_paths, new LoggerConfiguration().CreateLogger());
		var listenerState = new FakeHostListenerState { PublicPort = publicPort, LoopbackPort = loopbackPort };
		var coordinator
			= new AdbTunnelCoordinator(runner, listenerState, marker, new LoggerConfiguration().CreateLogger());
		coordinator.SetExecutablePath(ExecutablePath);
		return (coordinator, runner, marker);
	}

	private static AdbDevice AuthorizedDevice(string serial = Serial)
		=> new(serial, AdbDeviceState.Device, "Pixel", "Google", "product", "1", null, DateTimeOffset.UtcNow);

	private static AdbProcessResult ListResult(string output) => new(true, 0, output, string.Empty, false);

	private static bool IsNoRebindCreate(IReadOnlyList<string> argv, int devicePort)
		=> argv.Count == 6 && argv[2] == "reverse" && argv[3] == "--no-rebind" && argv[4] == "tcp:" + devicePort;
}
