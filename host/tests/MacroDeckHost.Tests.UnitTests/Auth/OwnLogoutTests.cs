using System.Net;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Auth;

// Signing out ends this session's ability to renew itself and nobody else's. Whether the access token the
// session still holds must stop working at once is not decided yet, so it is not asserted here.
[NonParallelizable]
public class OwnLogoutTests
{
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
	public async Task Signing_out_revokes_only_this_sessions_refresh_token()
	{
		var mine = await LoginRefreshCookie();
		var other = await LoginRefreshCookie();

		var logout = await _host.SendAsync(HttpMethod.Post, "/api/auth/logout", cookie: mine);
		var renewMine = await _host.SendAsync(HttpMethod.Post, "/api/auth/refresh", cookie: mine);
		var renewOther = await _host.SendAsync(HttpMethod.Post, "/api/auth/refresh", cookie: other);

		Assert.Multiple(() =>
		{
			Assert.That(logout.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
			Assert.That(renewMine.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
			Assert.That(renewOther.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		});
	}

	private async Task<string> LoginRefreshCookie()
	{
		var response = await _host.SendAsync(HttpMethod.Post,
			"/api/auth/login",
			new { username = InProcessHost.Username, password = InProcessHost.Password, scope = "client" });
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		return response.Headers.GetValues("Set-Cookie")
			.First(value => value.StartsWith(AuthDefaults.RefreshCookie + "_", StringComparison.Ordinal))
			.Split(';')[0];
	}
}
