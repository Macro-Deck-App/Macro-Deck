using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MacroDeckHost.Integrations.Ping.Protocol;
using MacroDeckHost.Integrations.Ping.State;

namespace MacroDeckHost.Tests.UnitTests.Ping;

[TestFixture]
internal sealed class PingProberTests
{
	private static readonly IPAddress _v6 = IPAddress.Parse("fd00::10");
	private static readonly IPAddress _v4 = IPAddress.Parse("192.168.1.10");

	private readonly List<IPAddress> _pinged = [];
	private readonly Dictionary<IPAddress, PingProbeResult> _replies = [];
	private int _resolves;

	[SetUp]
	public void SetUp()
	{
		_pinged.Clear();
		_replies.Clear();
		_resolves = 0;
	}

	[Test]
	public async Task A_name_answering_only_on_ipv4_is_reachable_although_ipv6_comes_first()
	{
		_replies[_v6] = new PingProbeResult(PingProblem.TimedOut);
		_replies[_v4] = PingProbeResult.Reply(3);

		var result = await Prober([_v6, _v4]).ProbeAsync("nas.lan", TimeSpan.FromSeconds(1), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.EqualTo(PingProbeResult.Reply(3)));
			Assert.That(_pinged, Is.EqualTo(new[] { _v6, _v4 }));
		});
	}

	[Test]
	public async Task An_address_is_pinged_without_asking_the_name_resolver()
	{
		_replies[IPAddress.Parse("fe80::1%1")] = PingProbeResult.Reply(1);

		var result = await Prober([]).ProbeAsync("fe80::1%1", TimeSpan.FromSeconds(1), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Succeeded, Is.True);
			Assert.That(_resolves, Is.Zero);
		});
	}

	[Test]
	public async Task A_name_that_does_not_resolve_is_reported_as_such()
	{
		var prober = new PingProber((_, _) => throw new SocketException((int)SocketError.HostNotFound), Echo);

		var result = await prober.ProbeAsync("missing.invalid", TimeSpan.FromSeconds(1), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Problem, Is.EqualTo(PingProblem.NameNotResolved));
			Assert.That(_pinged, Is.Empty);
		});
	}

	[Test]
	public async Task A_timeout_on_one_address_outweighs_a_missing_permission_on_another()
	{
		_replies[_v6] = new PingProbeResult(PingProblem.NotPermitted);
		_replies[_v4] = new PingProbeResult(PingProblem.TimedOut);

		var result = await Prober([_v6, _v4]).ProbeAsync("nas.lan", TimeSpan.FromSeconds(1), CancellationToken.None);

		Assert.That(result.Problem, Is.EqualTo(PingProblem.TimedOut));
	}

	[Test]
	public void Platform_refusals_count_as_missing_permission_and_network_errors_as_unreachable()
	{
		Assert.Multiple(() =>
		{
			Assert.That(PingProber.Classify(new PlatformNotSupportedException()), Is.EqualTo(PingProblem.NotPermitted));
			Assert.That(PingProber.Classify(new PingException("x", new SocketException((int)SocketError.AccessDenied))),
				Is.EqualTo(PingProblem.NotPermitted));
			Assert.That(PingProber.Classify(new PingException("x",
					new SocketException((int)SocketError.NetworkUnreachable))),
				Is.EqualTo(PingProblem.Unreachable));
			Assert.That(PingProber.Classify(IPStatus.TimedOut), Is.EqualTo(PingProblem.TimedOut));
			Assert.That(PingProber.Classify(IPStatus.DestinationHostUnreachable), Is.EqualTo(PingProblem.Unreachable));
		});
	}

	private PingProber Prober(IPAddress[] addresses)
	{
		return new PingProber((_, _) =>
			{
				_resolves++;
				return Task.FromResult(addresses);
			},
			Echo);
	}

	private Task<PingProbeResult> Echo(IPAddress address, TimeSpan timeout, CancellationToken cancellationToken)
	{
		_pinged.Add(address);
		return Task.FromResult(_replies.GetValueOrDefault(address) ?? new PingProbeResult(PingProblem.TimedOut));
	}
}
