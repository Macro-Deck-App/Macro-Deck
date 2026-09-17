using System.Net.Sockets;
using MacroDeckHost.Integrations.Discord.Rpc;

namespace MacroDeckHost.Tests.UnitTests.Discord;

[TestFixture]
[Platform(Exclude = "Win")]
internal sealed class DiscordIpcTransportTests
{
	[Test]
	public async Task A_skipped_endpoint_is_not_connected_to_again()
	{
		var directory = Directory.CreateTempSubdirectory("mdipc-").FullName;
		using var scope = new EnvironmentScope
		{
			["XDG_RUNTIME_DIR"] = directory,
			["TMPDIR"] = null,
			["TMP"] = null,
			["TEMP"] = null
		};
		var first = Path.Combine(directory, "discord-ipc-0");
		var second = Path.Combine(directory, "discord-ipc-1");

		try
		{
			using var firstListener = Listen(first);
			using var secondListener = Listen(second);

			using var initial = new DiscordIpcTransport();
			await initial.ConnectAsync(new HashSet<string>(StringComparer.Ordinal), CancellationToken.None);

			using var retry = new DiscordIpcTransport();
			await retry.ConnectAsync(new HashSet<string>(StringComparer.Ordinal) { first }, CancellationToken.None);

			Assert.Multiple(() =>
			{
				Assert.That(initial.Endpoint, Is.EqualTo(first));
				Assert.That(retry.Endpoint, Is.EqualTo(second));
			});
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public void Skipping_every_endpoint_reports_discord_as_unavailable()
	{
		var directory = Directory.CreateTempSubdirectory("mdipc-").FullName;
		using var scope = new EnvironmentScope
		{
			["XDG_RUNTIME_DIR"] = directory,
			["TMPDIR"] = null,
			["TMP"] = null,
			["TEMP"] = null
		};
		var only = Path.Combine(directory, "discord-ipc-0");

		try
		{
			using var listener = Listen(only);
			using var transport = new DiscordIpcTransport();

			Assert.ThrowsAsync<DiscordIpcUnavailableException>(async () =>
				await transport.ConnectAsync(new HashSet<string>(StringComparer.Ordinal) { only },
					CancellationToken.None));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	private static Socket Listen(string path)
	{
		var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
		socket.Bind(new UnixDomainSocketEndPoint(path));
		socket.Listen(4);
		return socket;
	}
}
