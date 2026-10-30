using System.Net;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Auth;

// ADR 0091: a credential change ends every signed-in session. A UI socket is a session too, whether or not it
// names a device; only the trusted desktop, which holds no credential, stays connected.
[NonParallelizable]
public class UiSocketSessionEndTests
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

	[TestCase("reset")]
	[TestCase("change")]
	public async Task A_credential_change_closes_every_remote_ui_socket_and_keeps_the_desktop(string change)
	{
		await using var deviceSocket = await _host.ConnectUiAsync(await _host.LoginTokenAsync("client", "Tablet"));
		await using var deviceless = await _host.ConnectUiAsync(await _host.LoginTokenAsync("admin"));
		await using var desktop = await _host.ConnectUiAsync(null);

		var response = change == "reset"
			? await _host.SendAsync(HttpMethod.Post,
				"/api/auth/reset-password",
				new { newPassword = "newpassword1" },
				loopback: true)
			: await _host.SendAsync(HttpMethod.Post,
				"/api/auth/change-password",
				new { currentPassword = InProcessHost.Password, newPassword = "newpassword1" },
				loopback: true);
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

		await deviceSocket.WaitForCloseAsync();
		await deviceless.WaitForCloseAsync();
		await desktop.SyncAsync();

		Assert.That(desktop.Closed.IsCompleted, Is.False);
	}
}
