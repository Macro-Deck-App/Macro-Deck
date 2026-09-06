using System.Net;
using System.Security.Claims;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Ui;
using Microsoft.AspNetCore.Http;

namespace MacroDeckHost.Tests.UnitTests.Ui;

[NonParallelizable]
public sealed class UiWebSocketTicketTests
{
	private const int LoopbackPort = TestListenerPorts.Loopback;
	private const int PublicPort = 9100;
	private ManualTimeProvider _time = null!;
	private UiWebSocketTickets _tickets = null!;

	[SetUp]
	public void SetUp()
	{
		ResolvedPublicEndpoints.ResetForTests();
		ResolvedPublicEndpoints.Set(PublicEndpointSet.HttpOnly(PublicPort));
		ResolvedLoopbackPort.ResetForTests();
		ResolvedLoopbackPort.Set(LoopbackPort);
		_time = new ManualTimeProvider();
		_tickets = new UiWebSocketTickets(_time);
	}

	[TearDown]
	public void TearDown()
	{
		ResolvedPublicEndpoints.ResetForTests();
		ResolvedLoopbackPort.ResetForTests();
		ResolvedLoopbackPort.Set(TestListenerPorts.Loopback);
	}

	[Test]
	public void Ticket_is_random_url_safe_single_use_and_preserves_the_authenticated_principal()
	{
		var principal = Principal("device-1");
		Assert.That(_tickets.TryCreate(PublicContext("https://admin.example"), principal, out var ticket), Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(ticket.Value, Has.Length.EqualTo(43));
			Assert.That(ticket.Value, Does.Match("^[A-Za-z0-9_-]+$"));
		});
		Assert.That(_tickets.TryRedeem(PublicContext("null"), ticket.Value, out var redeemed), Is.True);
		Assert.That(redeemed.FindFirst(AuthDefaults.DeviceClaim)?.Value, Is.EqualTo("device-1"));
		Assert.That(_tickets.TryRedeem(PublicContext("https://admin.example"), ticket.Value, out _), Is.False);
	}

	[Test]
	public void Origin_host_and_remote_address_do_not_bind_a_public_listener_ticket()
	{
		Assert.That(_tickets.TryCreate(PublicContext("https://one.example",
					"proxy-one.example",
					IPAddress.Parse("192.168.1.20")),
				Principal("device-1"),
				out var ticket),
			Is.True);

		Assert.That(_tickets.TryRedeem(PublicContext("https://two.example",
					"proxy-two.example",
					IPAddress.Parse("10.0.0.8")),
				ticket.Value,
				out _),
			Is.True);
	}

	[Test]
	public void Ticket_is_consumed_when_redeemed_on_the_wrong_listener_class()
	{
		Assert.That(_tickets.TryCreate(PublicContext(null), Principal("device-1"), out var ticket), Is.True);

		Assert.That(_tickets.TryRedeem(LoopbackContext(), ticket.Value, out _), Is.False);
		Assert.That(_tickets.TryRedeem(PublicContext(null), ticket.Value, out _), Is.False);
	}

	[Test]
	public void Expired_ticket_cannot_be_redeemed()
	{
		Assert.That(_tickets.TryCreate(PublicContext(null), Principal("device-1"), out var ticket), Is.True);
		_time.Advance(TimeSpan.FromSeconds(15));

		Assert.That(_tickets.TryRedeem(PublicContext(null), ticket.Value, out _), Is.False);
	}

	[Test]
	public void A_principal_can_have_at_most_four_outstanding_tickets()
	{
		var principal = Principal("device-1");
		for (var index = 0; index < 4; index++)
		{
			Assert.That(_tickets.TryCreate(PublicContext(null), principal, out _), Is.True);
		}

		Assert.That(_tickets.TryCreate(PublicContext(null), principal, out _), Is.False);
		Assert.That(_tickets.TryCreate(PublicContext(null), Principal("device-2"), out _), Is.True);
	}

	[TestCase("")]
	[TestCase("not-a-ticket")]
	[TestCase("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
	public void Malformed_ticket_is_rejected_without_throwing(string value)
	{
		Assert.That(_tickets.TryRedeem(PublicContext(null), value, out _), Is.False);
	}

	private static ClaimsPrincipal Principal(string deviceId)
		=> new(new ClaimsIdentity([
				new Claim(AuthDefaults.DeviceClaim, deviceId),
				new Claim(AuthDefaults.ScopeClaim, AuthDefaults.ClientScope)
			],
			"test"));

	private static DefaultHttpContext PublicContext(string? origin,
		string host = "macrodeck.local",
		IPAddress? remoteAddress = null)
	{
		var context = Context(PublicPort, remoteAddress ?? IPAddress.Parse("192.168.1.10"), host);
		if (origin is not null)
		{
			context.Request.Headers.Origin = origin;
		}

		return context;
	}

	private static DefaultHttpContext LoopbackContext()
		=> Context(LoopbackPort, IPAddress.Loopback, "localhost");

	private static DefaultHttpContext Context(int localPort, IPAddress remoteAddress, string host)
	{
		var context = new DefaultHttpContext
		{
			Connection = { LocalPort = localPort, RemoteIpAddress = remoteAddress }
		};
		context.Request.Host = new HostString(host, localPort);
		return context;
	}
}
