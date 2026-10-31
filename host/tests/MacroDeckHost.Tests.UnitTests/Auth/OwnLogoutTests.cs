using System.Net;
using System.Text.Json;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Auth;

[NonParallelizable]
public class OwnLogoutTests
{
	private const string ProtectedPath = "/api/profiles";

	private InProcessHost _host = null!;

	[SetUp]
	public async Task SetUp()
	{
		_host = await InProcessHost.StartAsync();
		await _host.SetUpAccountAsync();
	}

	[TearDown]
	public async Task TearDown()
	{
		await _host.DisposeAsync();
	}

	[Test]
	public async Task Signing_out_without_a_device_ends_only_this_sessions_refresh_token()
	{
		var mine = await Login("client");
		var other = await Login("client");

		var logout = await Logout(mine);
		var renewMine = await Refresh(mine);
		var renewOther = await Refresh(other);
		var mineStillReads = await Read(mine);

		Assert.Multiple(() =>
		{
			Assert.That(logout, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(renewMine, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(renewOther, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(mineStillReads, Is.EqualTo(HttpStatusCode.OK));
		});
	}

	[Test]
	public async Task Signing_out_on_a_device_ends_every_session_of_that_device()
	{
		var mine = await Login("client", NewDevice("phone"));
		var second = await Login("client", CredentialOf(mine));
		Assert.That(await Read(mine), Is.EqualTo(HttpStatusCode.OK));

		var logout = await Logout(mine);

		Assert.That(logout, Is.EqualTo(HttpStatusCode.NoContent));
		Assert.That(await Read(mine), Is.EqualTo(HttpStatusCode.Unauthorized));
		Assert.That(await Read(second), Is.EqualTo(HttpStatusCode.Unauthorized));
		Assert.That(await Refresh(mine), Is.EqualTo(HttpStatusCode.Unauthorized));
		Assert.That(await Refresh(second), Is.EqualTo(HttpStatusCode.Unauthorized));
	}

	[Test]
	public async Task Signing_out_on_a_device_leaves_other_devices_signed_in()
	{
		var mine = await Login("client", NewDevice("phone"));
		var other = await Login("client", NewDevice("tablet"));

		await Logout(mine);

		Assert.That(await Read(other), Is.EqualTo(HttpStatusCode.OK));
		Assert.That(await Refresh(other), Is.EqualTo(HttpStatusCode.OK));
	}

	[Test]
	public async Task A_device_can_sign_in_again_right_after_signing_out()
	{
		var before = await Login("client", NewDevice("phone"));
		await Logout(before);

		var after = await Login("client", CredentialOf(before));

		Assert.That(await Read(after), Is.EqualTo(HttpStatusCode.OK));
		Assert.That(await Read(before), Is.EqualTo(HttpStatusCode.Unauthorized));
	}

	[Test]
	public async Task An_admin_session_on_a_device_signs_out_and_back_in_the_same_way()
	{
		var before = await Login("admin", NewDevice("browser"));

		var logout = await Logout(before);

		Assert.That(logout, Is.EqualTo(HttpStatusCode.NoContent));
		Assert.That(await Read(before), Is.EqualTo(HttpStatusCode.Unauthorized));
		Assert.That(await Refresh(before), Is.EqualTo(HttpStatusCode.Unauthorized));

		var after = await Login("admin", CredentialOf(before));
		Assert.That(await Read(after), Is.EqualTo(HttpStatusCode.OK));
	}

	private static object NewDevice(string name)
	{
		return new
		{
			clientType = "web-client",
			proposedName = name,
			platform = "Windows",
			browser = "Chrome",
			formFactor = "desktop",
			appVersion = "test"
		};
	}

	private static object CredentialOf(Session session)
	{
		var device = session.Response.GetProperty("device");
		return new
		{
			deviceId = device.GetProperty("deviceId").GetString(),
			deviceSecret = device.GetProperty("deviceSecret").GetString(),
			clientType = "web-client",
			proposedName = "same device",
			platform = "Windows",
			browser = "Chrome",
			formFactor = "desktop",
			appVersion = "test"
		};
	}

	private async Task<Session> Login(string scope, object? device = null)
	{
		var response = await _host.SendAsync(HttpMethod.Post,
			"/api/auth/login",
			new { username = InProcessHost.Username, password = InProcessHost.Password, scope, device });
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var cookie = response.Headers.GetValues("Set-Cookie")
			.First(value => value.StartsWith(AuthDefaults.RefreshCookie + "_", StringComparison.Ordinal))
			.Split(';')[0];
		var body = await InProcessHost.ReadJsonAsync(response);

		return new Session(body.GetProperty("accessToken").GetString()!, cookie, body);
	}

	private async Task<HttpStatusCode> Logout(Session session)
	{
		return (await _host.SendAsync(HttpMethod.Post, "/api/auth/logout", cookie: session.RefreshCookie)).StatusCode;
	}

	private async Task<HttpStatusCode> Refresh(Session session)
	{
		return (await _host.SendAsync(HttpMethod.Post, "/api/auth/refresh", cookie: session.RefreshCookie)).StatusCode;
	}

	private async Task<HttpStatusCode> Read(Session session)
	{
		return (await _host.SendAsync(HttpMethod.Get, ProtectedPath, bearerToken: session.AccessToken)).StatusCode;
	}

	private sealed record Session(string AccessToken, string RefreshCookie, JsonElement Response);
}
