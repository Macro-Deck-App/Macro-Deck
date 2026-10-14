using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.Json;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Tests.UnitTests.Plugins;
using MacroDeckHost.Ui;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Ui;

[NonParallelizable]
public class UiWebSocketCloseHandshakeTests
{
	private static readonly TimeSpan _testTimeout = TimeSpan.FromSeconds(15);

	private IHost _host = null!;
	private string _dataDir = null!;
	private string? _previousDataDir;

	[OneTimeSetUp]
	public async Task OneTimeSetUp()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);

		DatabaseMigrationHelper.MigrateDatabase(new MacroDeckPaths());

		_host = await new HostBuilder()
			.ConfigureWebHost(builder =>
			{
				builder.UseTestServer();
				builder.UseStartup<Startup>();
				builder.ConfigureTestServices(services =>
				{
					services.RemoveAll<IHostedService>();
					services.AddSingleton(Log.Logger);
					services.AddSingleton<ILogLevelState>(new LogLevelState(LogEntryLevel.Information));
					services.RemoveAll<StartupReadiness>();
					services.AddSingleton(CompletedStartupReadiness());
				});
			})
			.StartAsync();
	}

	[OneTimeTearDown]
	public async Task OneTimeTearDown()
	{
		await _host.StopAsync();
		_host.Dispose();
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);
		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, true);
		}
	}

	[Test]
	public async Task A_client_close_after_welcome_is_echoed_with_its_status_and_reason()
	{
		using var pair = await PluginWebSocketConnectionPair.CreateAsync();
		var running = Run(pair.Host);
		await SendAsync(pair.Plugin,
			new UiWebSocketEnvelope(UiWebSocketProtocol.Version, "hello", null, null, "hello-1", null, null));
		Assert.That((await ReceiveEnvelopeAsync(pair.Plugin))?.Kind, Is.EqualTo("welcome"));

		var closing = pair.Plugin.CloseAsync((WebSocketCloseStatus)4001, "Client leaving.", Timeout());
		await FinishAsync(running, pair.Host);
		await closing.WaitAsync(_testTimeout);

		Assert.Multiple(() =>
		{
			Assert.That(pair.Plugin.State, Is.EqualTo(WebSocketState.Closed));
			Assert.That((int?)pair.Plugin.CloseStatus, Is.EqualTo(4001));
			Assert.That(pair.Plugin.CloseStatusDescription, Is.EqualTo("Client leaving."));
		});
	}

	[Test]
	public async Task A_client_close_before_hello_is_answered_with_its_own_status()
	{
		using var pair = await PluginWebSocketConnectionPair.CreateAsync();
		var running = Run(pair.Host);

		var closing = pair.Plugin.CloseAsync((WebSocketCloseStatus)4000, "Not now.", Timeout());
		await FinishAsync(running, pair.Host);
		await closing.WaitAsync(_testTimeout);

		Assert.Multiple(() =>
		{
			Assert.That(pair.Plugin.State, Is.EqualTo(WebSocketState.Closed));
			Assert.That((int?)pair.Plugin.CloseStatus, Is.EqualTo(4000));
			Assert.That(pair.Plugin.CloseStatusDescription, Is.EqualTo("Not now."));
		});
	}

	// A browser's close() without arguments sends a close frame with no payload, which ManagedWebSocket
	// cannot produce, so the client side writes its frames itself.
	[Test]
	public async Task A_close_without_a_status_after_welcome_is_answered_with_a_normal_close()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		using var client = new TcpClient();
		var connect = client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
		using var accepted = await listener.AcceptTcpClientAsync();
		await connect;
		listener.Stop();
		using var host = WebSocket.CreateFromStream(accepted.GetStream(),
			true,
			null,
			TimeSpan.FromMinutes(1));
		var stream = client.GetStream();
		var running = Run(host);

		await WriteMaskedFrameAsync(stream,
			0x81,
			JsonSerializer.SerializeToUtf8Bytes(
				new UiWebSocketEnvelope(UiWebSocketProtocol.Version, "hello", null, null, "hello-1", null, null),
				UiWebSocketProtocol.Json));
		var (welcomeOpcode, welcome) = await ReadFrameAsync(stream);
		Assert.That(welcomeOpcode, Is.EqualTo(0x81));
		Assert.That(JsonSerializer.Deserialize<UiWebSocketEnvelope>(welcome, UiWebSocketProtocol.Json)?.Kind,
			Is.EqualTo("welcome"));

		await WriteMaskedFrameAsync(stream, 0x88, []);
		var (opcode, payload) = await ReadFrameAsync(stream);
		await FinishAsync(running, host);

		Assert.Multiple(() =>
		{
			Assert.That(opcode, Is.EqualTo(0x88), "expected a final close frame");
			Assert.That(payload, Has.Length.EqualTo(2), "expected a status code and no reason");
			Assert.That(BinaryPrimitives.ReadUInt16BigEndian(payload),
				Is.EqualTo((ushort)WebSocketCloseStatus.NormalClosure));
		});
	}

	[Test]
	public async Task A_hello_of_the_wrong_kind_is_still_closed_as_an_invalid_hello()
	{
		using var pair = await PluginWebSocketConnectionPair.CreateAsync();
		var running = Run(pair.Host);
		await SendAsync(pair.Plugin,
			new UiWebSocketEnvelope(UiWebSocketProtocol.Version, "ping", null, null, "ping-1", null, null));

		Assert.That(await ReceiveEnvelopeAsync(pair.Plugin), Is.Null, "expected the host's close frame");
		await FinishAsync(running, pair.Host);

		Assert.Multiple(() =>
		{
			Assert.That(pair.Plugin.CloseStatus, Is.EqualTo(WebSocketCloseStatus.PolicyViolation));
			Assert.That(pair.Plugin.CloseStatusDescription, Is.EqualTo("Invalid hello"));
		});
	}

	private Task Run(WebSocket socket)
	{
		return _host.Services.GetRequiredService<UiWebSocketEndpoint>()
			.RunConnectionAsync(socket, new ClaimsPrincipal(new ClaimsIdentity()), CancellationToken.None);
	}

	private static async Task FinishAsync(Task running, WebSocket host)
	{
		await running.WaitAsync(_testTimeout);
		host.Dispose();
	}

	private static async Task SendAsync(WebSocket socket, UiWebSocketEnvelope envelope)
	{
		await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(envelope, UiWebSocketProtocol.Json),
			WebSocketMessageType.Text,
			true,
			Timeout());
	}

	private static async Task<UiWebSocketEnvelope?> ReceiveEnvelopeAsync(WebSocket socket)
	{
		var buffer = new byte[UiWebSocketProtocol.MaxMessageBytes];
		var result = await socket.ReceiveAsync(buffer, Timeout());
		return result.MessageType == WebSocketMessageType.Close
			? null
			: JsonSerializer.Deserialize<UiWebSocketEnvelope>(buffer.AsSpan(0, result.Count), UiWebSocketProtocol.Json);
	}

	private static async Task WriteMaskedFrameAsync(NetworkStream stream, byte header, byte[] payload)
	{
		byte[] mask = [0x12, 0x34, 0x56, 0x78];
		var frame = new byte[6 + payload.Length];
		frame[0] = header;
		frame[1] = (byte)(0x80 | payload.Length);
		mask.CopyTo(frame, 2);
		for (var index = 0; index < payload.Length; index++)
		{
			frame[6 + index] = (byte)(payload[index] ^ mask[index % 4]);
		}

		await stream.WriteAsync(frame, Timeout());
	}

	private static async Task<(int Opcode, byte[] Payload)> ReadFrameAsync(NetworkStream stream)
	{
		var header = new byte[2];
		await stream.ReadExactlyAsync(header, Timeout());
		var length = header[1] & 0x7F;
		if (length == 126)
		{
			var extended = new byte[2];
			await stream.ReadExactlyAsync(extended, Timeout());
			length = BinaryPrimitives.ReadUInt16BigEndian(extended);
		}

		var payload = new byte[length];
		await stream.ReadExactlyAsync(payload, Timeout());
		return (header[0], payload);
	}

	private static CancellationToken Timeout()
	{
		return new CancellationTokenSource(_testTimeout).Token;
	}

	private static StartupReadiness CompletedStartupReadiness()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		return readiness;
	}
}
