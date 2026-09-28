using System.Net;
using System.Security.Claims;
using MacroDeck.Sdk.VideoStreams;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Usb;
using MacroDeckHost.Ui;
using Microsoft.AspNetCore.Http;

namespace MacroDeckHost.Tests.UnitTests.Ui;

[TestFixture]
public class VideoStreamConsumerClassifierTests
{
	private const int PublicPort = 8191;

	[Test]
	public void The_desktop_on_the_loopback_listener_is_local()
	{
		var context = Context(TestListenerPorts.Loopback, IPAddress.Loopback, "127.0.0.1");

		var consumer = VideoStreamConsumerClassifier.Classify(context, Principal());

		Assert.Multiple(() =>
		{
			Assert.That(consumer.ConnectionKind, Is.EqualTo(VideoStreamConnectionKind.Local));
			Assert.That(consumer.HostAddress, Is.EqualTo(new Uri($"http://127.0.0.1:{TestListenerPorts.Loopback}")));
		});
	}

	[Test]
	public void A_bridged_connection_is_a_usb_tunnel_even_on_the_loopback_listener()
	{
		var context = Context(TestListenerPorts.Loopback, IPAddress.Loopback, "127.0.0.1");
		context.Features.Set<IBridgedConnectionFeature>(new BridgedConnectionFeature("device-key"));

		Assert.That(VideoStreamConsumerClassifier.Classify(context, Principal()).ConnectionKind,
			Is.EqualTo(VideoStreamConnectionKind.UsbTunnel));
	}

	[Test]
	public void Loopback_traffic_on_the_public_port_is_a_usb_tunnel()
	{
		var context = Context(PublicPort, IPAddress.Loopback, "localhost");

		Assert.That(VideoStreamConsumerClassifier.Classify(context, Principal()).ConnectionKind,
			Is.EqualTo(VideoStreamConnectionKind.UsbTunnel));
	}

	[Test]
	public void A_device_on_the_lan_is_a_network_consumer_with_its_device_id_and_the_address_it_used()
	{
		var context = Context(PublicPort, IPAddress.Parse("192.168.1.40"), "192.168.1.2");
		var deviceId = Guid.NewGuid().ToString();

		var consumer = VideoStreamConsumerClassifier.Classify(context,
			Principal(new Claim(AuthDefaults.DeviceClaim, deviceId)));

		Assert.Multiple(() =>
		{
			Assert.That(consumer.ConnectionKind, Is.EqualTo(VideoStreamConnectionKind.Network));
			Assert.That(consumer.HostAddress, Is.EqualTo(new Uri($"http://192.168.1.2:{PublicPort}")));
			Assert.That(consumer.DeviceId, Is.EqualTo(deviceId));
		});
	}

	private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

	private static DefaultHttpContext Context(int localPort, IPAddress remote, string host)
	{
		var context = new DefaultHttpContext { Connection = { LocalPort = localPort, RemoteIpAddress = remote } };
		context.Request.Scheme = "http";
		context.Request.Host = new HostString(host, localPort);
		return context;
	}
}
