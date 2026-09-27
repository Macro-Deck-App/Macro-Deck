using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.RateLimiting;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Usb;
using MacroDeckHost.Auth;
using MacroDeckHost.Infrastructure.Usb.Native;
using MacroDeckHost.Licensing;
using MacroDeckHost.Tests.UnitTests.Delegation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Usb;

[NonParallelizable]
public class BridgedConnectionTests
{
	private const string DeviceKey = "usb:EXAMPLE0001";

	[Test]
	public void The_dialler_targets_the_plain_http_public_listener_on_loopback_and_never_the_loopback_listener()
	{
		Assert.Multiple(() =>
		{
			Assert.That(LoopbackBridgeDialer.Target(PublicEndpointSet.HttpOnly(8193)),
				Is.EqualTo(new IPEndPoint(IPAddress.Loopback, 8193)));
			Assert.That(LoopbackBridgeDialer.Target(PublicEndpointSet.HttpAndHttps(8193, 8194)),
				Is.EqualTo(new IPEndPoint(IPAddress.Loopback, 8193)));
			Assert.That(LoopbackBridgeDialer.Target(PublicEndpointSet.HttpsReplacingHttp(8194)), Is.Null);
			Assert.That(LoopbackBridgeDialer.Target(PublicEndpointSet.HttpOnly(8193).WithoutHttp()), Is.Null);
			Assert.That(LoopbackBridgeDialer.Target(PublicEndpointSet.HttpOnly(8193))!.Port,
				Is.Not.EqualTo(TestListenerPorts.Loopback));
		});
	}

	[Test]
	public async Task The_dialler_registers_its_bound_local_endpoint_before_connecting_and_releases_it_on_failure()
	{
		var registry = new BridgedConnections(new FakeTimeProvider());
		var closedPort = FreePort();
		var dialer = new LoopbackBridgeDialer(
			new FakeHostListenerState { PublicEndpoints = PublicEndpointSet.HttpOnly(closedPort) },
			registry);

		Assert.That(async () => await dialer.DialAsync(DeviceKey, CancellationToken.None), Throws.InstanceOf<SocketException>());
		Assert.That(registry.PendingCount, Is.Zero);

		var refusing = new LoopbackBridgeDialer(
			new FakeHostListenerState { PublicEndpoints = PublicEndpointSet.HttpsReplacingHttp(closedPort) },
			registry);
		Assert.That(await refusing.DialAsync(DeviceKey, CancellationToken.None), Is.Null);
	}

	[Test]
	public void The_stamp_matches_the_registered_endpoint_including_its_ipv4_mapped_form_and_is_taken_once()
	{
		var registry = new BridgedConnections(new FakeTimeProvider());
		registry.Register(new IPEndPoint(IPAddress.Loopback, 50001), DeviceKey);
		registry.Register(new IPEndPoint(IPAddress.Loopback, 50002), "usb:OTHER");

		var mapped = Stamped(registry, new IPEndPoint(IPAddress.Loopback.MapToIPv6(), 50001));
		var again = Stamped(registry, new IPEndPoint(IPAddress.Loopback, 50001));
		var ipv6SamePort = Stamped(registry, new IPEndPoint(IPAddress.IPv6Loopback, 50002));

		Assert.Multiple(() =>
		{
			Assert.That(mapped, Is.EqualTo(DeviceKey));
			Assert.That(again, Is.Null);
			Assert.That(ipv6SamePort, Is.Null);
		});
	}

	[Test]
	public void A_closed_dial_stays_stampable_for_the_accept_grace_and_never_removes_a_newer_registration()
	{
		var time = new FakeTimeProvider();
		var registry = new BridgedConnections(time);
		var endpoint = new IPEndPoint(IPAddress.Loopback, 50003);
		var closed = registry.Register(endpoint, DeviceKey);

		registry.ReleaseAfterClose(closed);
		var withinGrace = Stamped(registry, endpoint);

		var expired = registry.Register(endpoint, DeviceKey);
		registry.ReleaseAfterClose(expired);
		time.Advance(BridgedConnections.AcceptGrace);
		registry.Register(new IPEndPoint(IPAddress.Loopback, 50004), DeviceKey);
		var afterGrace = Stamped(registry, endpoint);

		var old = registry.Register(endpoint, DeviceKey);
		registry.Register(endpoint, "usb:NEWER");
		registry.Remove(old);
		var newer = Stamped(registry, endpoint);

		Assert.Multiple(() =>
		{
			Assert.That(withinGrace, Is.EqualTo(DeviceKey));
			Assert.That(afterGrace, Is.Null);
			Assert.That(newer, Is.EqualTo("usb:NEWER"));
		});
	}

	[Test]
	public void A_stamped_connection_is_not_a_local_request_and_an_unstamped_loopback_one_still_is()
	{
		var bridged = Context(IPAddress.Loopback, DeviceKey);
		var local = Context(IPAddress.Loopback, null);
		var ipv6Local = Context(IPAddress.IPv6Loopback, null);

		Assert.Multiple(() =>
		{
			Assert.That(LoopbackConnection.IsLocalRequest(bridged), Is.False);
			Assert.That(LoopbackConnection.IsLocalRequest(local), Is.True);
			Assert.That(LoopbackConnection.IsLocalRequest(ipv6Local), Is.True);
		});
	}

	[Test]
	public void Throttle_and_rate_limit_keys_name_the_device_and_keep_the_username_suffix()
	{
		var bridged = Context(IPAddress.IPv6Loopback, DeviceKey);
		var local = Context(IPAddress.Loopback, null);
		var remoteV6 = Context(IPAddress.Parse("2001:db8::5"), null);

		Assert.Multiple(() =>
		{
			Assert.That($"{BridgedConnectionStamp.ClientKey(bridged)}|alice", Is.EqualTo("usb:EXAMPLE0001|alice"));
			Assert.That($"{BridgedConnectionStamp.ClientKey(local)}|alice", Is.EqualTo("127.0.0.1|alice"));
			Assert.That(HostIdentityRateLimit.AddressKey(bridged), Is.EqualTo(DeviceKey));
			Assert.That(HostIdentityRateLimit.NetworkKey(bridged), Is.Null);
			Assert.That(HostIdentityRateLimit.AddressKey(local), Is.EqualTo("127.0.0.1"));
			Assert.That(HostIdentityRateLimit.NetworkKey(remoteV6), Is.Not.Null);
		});
	}

	[Test]
	public void One_bridged_device_failing_logins_does_not_lock_out_another_device_or_local_clients()
	{
		var throttle = new LoginThrottle(new FakeTimeProvider());
		var first = $"{BridgedConnectionStamp.ClientKey(Context(IPAddress.Loopback, DeviceKey))}|alice";
		var second = $"{BridgedConnectionStamp.ClientKey(Context(IPAddress.Loopback, "usb:OTHER"))}|alice";
		var local = $"{BridgedConnectionStamp.ClientKey(Context(IPAddress.Loopback, null))}|alice";

		for (var attempt = 0; attempt < 10; attempt++)
		{
			throttle.RegisterFailure(first);
		}

		var firstThrottled = throttle.IsThrottled(first, out _);
		var secondThrottled = throttle.IsThrottled(second, out _);
		var localThrottled = throttle.IsThrottled(local, out _);
		throttle.ClearUsername("alice");

		Assert.Multiple(() =>
		{
			Assert.That(firstThrottled, Is.True);
			Assert.That(secondThrottled, Is.False);
			Assert.That(localThrottled, Is.False);
			Assert.That(throttle.IsThrottled(first, out _), Is.False);
		});
	}

	[Test]
	public void Identity_and_license_transfer_rate_limits_count_each_bridged_device_separately()
	{
		using var identity = HostIdentityRateLimit.Create();
		using var transfer = LegacyLicenseTransferRateLimit.Create();

		Assert.Multiple(() =>
		{
			Assert.That(Exhausts(identity, "/api/auth/identity", HostIdentityRateLimit.PerAddressLimit), Is.True);
			Assert.That(identity.AttemptAcquire(Context(IPAddress.Loopback, "usb:OTHER", "/api/auth/identity")).IsAcquired,
				Is.True);
			Assert.That(identity.AttemptAcquire(Context(IPAddress.Loopback, null, "/api/auth/identity")).IsAcquired,
				Is.True);

			Assert.That(Exhausts(transfer, "/api/legacy/md2-app/license-transfer",
				LegacyLicenseTransferRateLimit.PerAddressLimit), Is.True);
			Assert.That(transfer.AttemptAcquire(
				Context(IPAddress.Loopback, "usb:OTHER", "/api/legacy/md2-app/license-transfer")).IsAcquired, Is.True);
		});
	}

	[Test]
	public async Task Requests_over_a_link_reach_kestrel_bridged_for_the_whole_connection_while_direct_loopback_stays_local()
	{
		await using var harness = await KestrelHarness.StartAsync();
		await harness.HandshakeAsync();

		harness.Carrier.Send(LinkFrame.Open(1), LinkFrame.Data(1, Request("/probe?n=1")));
		await Eventually.True(() => harness.Seen.Count == 1, "the first bridged request arrived");
		await Eventually.True(() => Encoding.ASCII.GetString(harness.Carrier.DataOn(1)).Contains("200 OK"),
			"the first response came back");
		harness.Carrier.Send(LinkFrame.Data(1, Request("/probe?n=2")));
		await Eventually.True(() => harness.Seen.Count == 2, "the second request on the same connection arrived");

		using var direct = new HttpClient();
		await direct.GetStringAsync($"http://127.0.0.1:{harness.Port}/probe?n=direct");

		var seen = harness.Seen.ToArray();
		Assert.Multiple(() =>
		{
			Assert.That(seen[0], Is.EqualTo(("1", false, DeviceKey)));
			Assert.That(seen[1], Is.EqualTo(("2", false, DeviceKey)));
			Assert.That(seen[2], Is.EqualTo(("direct", true, (string?)null)));
		});
	}

	[Test]
	public async Task A_request_sent_right_before_close_is_still_bridged()
	{
		await using var harness = await KestrelHarness.StartAsync();
		await harness.HandshakeAsync();

		harness.Carrier.Send(LinkFrame.Open(1), LinkFrame.Data(1, Request("/probe?n=closing")), LinkFrame.Close(1));

		await Eventually.True(() => harness.Seen.Count == 1, "the request arrived");
		Assert.That(harness.Seen.Single(), Is.EqualTo(("closing", false, DeviceKey)));
	}

	private static bool Exhausts(PartitionedRateLimiter<HttpContext> limiter,
		string path,
		int permits)
	{
		for (var attempt = 0; attempt < permits; attempt++)
		{
			if (!limiter.AttemptAcquire(Context(IPAddress.Loopback, DeviceKey, path)).IsAcquired)
			{
				return false;
			}
		}

		return !limiter.AttemptAcquire(Context(IPAddress.Loopback, DeviceKey, path)).IsAcquired;
	}

	private static byte[] Request(string target)
		=> Encoding.ASCII.GetBytes($"GET {target} HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");

	private static string? Stamped(BridgedConnections registry, IPEndPoint remote)
	{
		var connection = new DefaultConnectionContext { RemoteEndPoint = remote };
		BridgedConnectionStamp.Stamp(connection, registry);
		return connection.Features.Get<IBridgedConnectionFeature>()?.DeviceKey;
	}

	private static DefaultHttpContext Context(IPAddress remote, string? deviceKey, string path = "/")
	{
		var context = new DefaultHttpContext { Connection = { RemoteIpAddress = remote, LocalPort = 8193 } };
		context.Request.Host = new HostString("127.0.0.1", 8193);
		context.Request.Path = path;
		if (deviceKey is not null)
		{
			context.Features.Set<IBridgedConnectionFeature>(new BridgedConnectionFeature(deviceKey));
		}

		return context;
	}

	private static int FreePort()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}

	private sealed class KestrelHarness : IAsyncDisposable
	{
		private readonly IHost _host;
		private readonly CancellationTokenSource _stop = new();
		private readonly Task _run;

		private KestrelHarness(IHost host, int port, FakeLinkCarrier carrier, NativeLink link,
			ConcurrentQueue<(string, bool, string?)> seen)
		{
			_host = host;
			Port = port;
			Carrier = carrier;
			Link = link;
			Seen = seen;
			_run = link.RunAsync(_stop.Token);
		}

		public int Port { get; }

		public FakeLinkCarrier Carrier { get; }

		public NativeLink Link { get; }

		public ConcurrentQueue<(string Label, bool Local, string? DeviceKey)> Seen { get; }

		public static async Task<KestrelHarness> StartAsync()
		{
			var port = FreePort();
			var registry = new BridgedConnections(TimeProvider.System);
			var plan = HostListenerPlan.Create(PublicEndpointSet.HttpOnly(port), 0, bridgedConnections: registry);
			var seen = new ConcurrentQueue<(string, bool, string?)>();
			var host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
				.ConfigureWebHost(web =>
				{
					web.UseKestrel(plan.Apply);
					web.Configure(app => app.Run(async context =>
					{
						var label = context.Request.Query["n"].ToString();
						seen.Enqueue((label, LoopbackConnection.IsLocalRequest(context),
							BridgedConnectionStamp.DeviceKey(context)));
						await context.Response.WriteAsync(label);
					}));
				})
				.Build();
			await host.StartAsync();

			var dialer = new LoopbackBridgeDialer(
				new FakeHostListenerState { PublicEndpoints = PublicEndpointSet.HttpOnly(port) },
				registry);
			var carrier = new FakeLinkCarrier();
			var link = new NativeLink(carrier, DeviceKey, dialer, TimeProvider.System, Logger.None);
			return new KestrelHarness(host, port, carrier, link, seen);
		}

		public async Task HandshakeAsync()
		{
			var hello = LinkHello.Parse(await Carrier.NextAsync(frame => frame.Type == LinkFrameType.Hello));
			Carrier.Send(LinkFrame.Hello(true, 0xABCD, hello.Epoch), LinkFrame.Hello(false, 0xABCD, 0));
			await Eventually.True(() => Link.IsLinked, "the link came up");
		}

		public async ValueTask DisposeAsync()
		{
			await _stop.CancelAsync();
			await _run.WaitAsync(TimeSpan.FromSeconds(10));
			await _host.StopAsync();
			_host.Dispose();
			_stop.Dispose();
			await Carrier.DisposeAsync();
		}
	}
}
