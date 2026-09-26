using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace MacroDeckHost.Tests.UnitTests.Host;

public class SingleInstanceGuardTests
{
	[Test]
	public void ParsePort_accepts_valid_port()
	{
		Assert.That(SingleInstanceGuard.ParsePort("52345\n"), Is.EqualTo(52345));
	}

	[Test]
	public void ParsePort_rejects_invalid_content()
	{
		Assert.Multiple(() =>
		{
			Assert.That(SingleInstanceGuard.ParsePort(null), Is.Null);
			Assert.That(SingleInstanceGuard.ParsePort(""), Is.Null);
			Assert.That(SingleInstanceGuard.ParsePort("not-a-port"), Is.Null);
			Assert.That(SingleInstanceGuard.ParsePort("0"), Is.Null);
			Assert.That(SingleInstanceGuard.ParsePort("-1"), Is.Null);
			Assert.That(SingleInstanceGuard.ParsePort("70000"), Is.Null);
		});
	}

	[Test]
	public async Task A_running_host_is_detected_without_holding_its_loopback_secret()
	{
		if (File.Exists(HostEndpoints.LoopbackPortFilePath))
		{
			Assert.Ignore("A host on this machine owns the shared port file; not overwriting it.");
		}

		using var listener = new HttpListener();
		var port = FreePort();
		listener.Prefixes.Add($"http://127.0.0.1:{port}/");
		listener.Start();
		var answering = Task.Run(async () =>
		{
			var context = await listener.GetContextAsync();
			var trusted = context.Request.Headers["X-MacroDeck-Loopback-Secret"] is not null;
			context.Response.StatusCode = context.Request.Url!.AbsolutePath == "/api/auth/status" && !trusted ? 200 : 401;
			context.Response.Close();
		});

		try
		{
			await File.WriteAllTextAsync(HostEndpoints.LoopbackPortFilePath, port.ToString(CultureInfo.InvariantCulture));

			Assert.That(await SingleInstanceGuard.IsAnotherInstanceRunning(), Is.True);
			await answering;
		}
		finally
		{
			File.Delete(HostEndpoints.LoopbackPortFilePath);
		}
	}

	private static int FreePort()
	{
		var probe = new TcpListener(IPAddress.Loopback, 0);
		probe.Start();
		var port = ((IPEndPoint)probe.LocalEndpoint).Port;
		probe.Stop();
		return port;
	}
}
