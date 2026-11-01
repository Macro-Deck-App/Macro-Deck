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
	public async Task A_host_of_another_user_is_not_this_users_instance()
	{
		var directory = Directory.CreateTempSubdirectory("md-guard-").FullName;
		var legacy = Path.Combine(directory, "legacy.port");
		await File.WriteAllTextAsync(legacy, "5000");

		var running = await SingleInstanceGuard.IsAnotherInstanceRunning([legacy],
			_ => true,
			_ => Task.FromResult(true));

		Directory.Delete(directory, true);
		Assert.That(running, Is.False);
	}

	[Test]
	public async Task A_foreign_legacy_file_does_not_hide_the_own_host_and_vice_versa()
	{
		var directory = Directory.CreateTempSubdirectory("md-guard-").FullName;
		var perUser = Path.Combine(directory, "per-user.port");
		var legacy = Path.Combine(directory, "legacy.port");
		await File.WriteAllTextAsync(perUser, "6000");
		await File.WriteAllTextAsync(legacy, "5000");

		var running = await SingleInstanceGuard.IsAnotherInstanceRunning([perUser, legacy],
			port => port == 5000,
			port => Task.FromResult(port == 6000));

		Directory.Delete(directory, true);
		Assert.That(running, Is.True);
	}

	[Test]
	public async Task An_older_host_of_the_same_user_is_found_through_the_legacy_file()
	{
		var directory = Directory.CreateTempSubdirectory("md-guard-").FullName;
		var legacy = Path.Combine(directory, "legacy.port");
		await File.WriteAllTextAsync(legacy, "5000");

		var running = await SingleInstanceGuard.IsAnotherInstanceRunning(
			[Path.Combine(directory, "per-user.port"), legacy],
			_ => false,
			_ => Task.FromResult(true));

		Directory.Delete(directory, true);
		Assert.That(running, Is.True);
	}

	[Test]
	public async Task A_port_file_that_cannot_be_read_is_no_instance()
	{
		var directory = Directory.CreateTempSubdirectory("md-guard-").FullName;

		var running = await SingleInstanceGuard.IsAnotherInstanceRunning([directory],
			_ => false,
			_ => Task.FromResult(true));

		Directory.Delete(directory, true);
		Assert.That(running, Is.False);
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
			context.Response.StatusCode
				= context.Request.Url!.AbsolutePath == "/api/auth/status" && !trusted ? 200 : 401;
			context.Response.Close();
		});

		try
		{
			await File.WriteAllTextAsync(HostEndpoints.LoopbackPortFilePath,
				port.ToString(CultureInfo.InvariantCulture));

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
