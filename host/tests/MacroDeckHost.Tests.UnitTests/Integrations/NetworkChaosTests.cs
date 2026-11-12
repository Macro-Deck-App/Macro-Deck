using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using MacroDeckHost.Integrations.HomeAssistant.Connections;
using MacroDeckHost.Integrations.HomeAssistant.Protocol;
using MacroDeckHost.Integrations.Meld.Connections;
using MacroDeckHost.Integrations.Meld.Protocol;
using MacroDeckHost.Integrations.Obs.Connections;
using MacroDeckHost.Integrations.Obs.Protocol;
using MacroDeckHost.Integrations.Streamerbot.Connections;
using MacroDeckHost.Integrations.Streamerbot.Protocol;
using MacroDeckHost.Tests.UnitTests.Obs;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
internal sealed class NetworkChaosTests
{
	private const string HomeAssistantAuthRequired = """{ "type": "auth_required", "ha_version": "2026.8.0" }""";
	private const string HomeAssistantAuthOk = """{ "type": "auth_ok", "ha_version": "2026.8.0" }""";

	private const string StreamerbotHello =
		"""
		{ "request": "Hello", "session": "s1",
		  "info": { "instanceId": "instance-1", "name": "Streamer.bot", "version": "0.2.5" },
		  "authentication": { "salt": "c2FsdA==", "challenge": "Y2hhbGxlbmdl" } }
		""";

	private static readonly TimeSpan _shortTimeout = TimeSpan.FromMilliseconds(300);
	private static readonly TimeSpan _reconnectDelay = TimeSpan.FromMilliseconds(20);
	private static readonly TimeSpan _deadline = TimeSpan.FromSeconds(30);
	private static readonly Uri _endpoint = new("ws://127.0.0.1:9/");

	[TestCase(NetworkFault.HandshakeStall)]
	[TestCase(NetworkFault.HalfOpen)]
	[TestCase(NetworkFault.AbortMidMessage)]
	public async Task Home_assistant_retries_a_network_fault_without_blaming_the_token(NetworkFault fault)
	{
		using var server = new ScriptedServer(HomeAssistantScript(fault));
		var attempts = new ReconnectProbe();
		using var connection = new HomeAssistantConnection(() => attempts.Attempt(CreateClient),
			_endpoint,
			"token",
			null,
			null,
			null,
			_reconnectDelay);
		attempts.Observe = () => new AfterFault(connection.IsConnected, connection.AuthInvalid);

		connection.Start();
		var after = await attempts.SecondAttempt.WaitAsync(_deadline);

		Assert.Multiple(() =>
		{
			Assert.That(after.Connected, Is.False);
			Assert.That(after.Flagged, Is.False, "a network fault is not a rejected access token");
		});

		IHomeAssistantClient CreateClient()
		{
			return new HomeAssistantClient(server.ConnectAsync, _shortTimeout);
		}
	}

	[TestCase(NetworkFault.HandshakeStall)]
	[TestCase(NetworkFault.HalfOpen)]
	[TestCase(NetworkFault.AbortMidMessage)]
	public async Task Streamerbot_retries_a_network_fault_without_asking_for_the_password(NetworkFault fault)
	{
		using var server = new ScriptedServer(StreamerbotScript(fault));
		var attempts = new ReconnectProbe();
		using var connection = new StreamerbotConnection(() => attempts.Attempt(CreateClient),
			_endpoint,
			"secret",
			null,
			_reconnectDelay);
		attempts.Observe = () => new AfterFault(connection.IsConnected, connection.NeedsAuthentication);

		connection.Start();
		var after = await attempts.SecondAttempt.WaitAsync(_deadline);

		Assert.Multiple(() =>
		{
			Assert.That(after.Connected, Is.False);
			Assert.That(after.Flagged, Is.False, "a network fault is not a rejected password");
		});

		IStreamerbotClient CreateClient()
		{
			return new StreamerbotClient(server.ConnectAsync, _shortTimeout);
		}
	}

	[TestCase(NetworkFault.HandshakeStall)]
	[TestCase(NetworkFault.HalfOpen)]
	[TestCase(NetworkFault.AbortMidMessage)]
	public async Task Meld_retries_a_network_fault_without_asking_for_setup(NetworkFault fault)
	{
		using var server = new ScriptedServer(MeldScript(fault));
		var attempts = new ReconnectProbe();
		using var connection = new MeldConnection(() => attempts.Attempt(CreateClient), _endpoint, _reconnectDelay);
		attempts.Observe = () => new AfterFault(connection.IsConnected, connection.NeedsSetup);

		connection.Start();
		var after = await attempts.SecondAttempt.WaitAsync(_deadline);

		Assert.Multiple(() =>
		{
			Assert.That(after.Connected, Is.False);
			Assert.That(after.Flagged, Is.False, "one network fault is not a reason to send the user to setup");
		});

		IQWebChannelClient CreateClient()
		{
			return new QWebChannelClient(server.ConnectAsync, _shortTimeout, _shortTimeout);
		}
	}

	[TestCase(ObsServerFault.NoHello)]
	[TestCase(ObsServerFault.SilentAfterIdentified,
		IgnoreReason = "#1705: an OBS session that goes silent after Identified is never dropped or retried")]
	[TestCase(ObsServerFault.AbortMidMessage)]
	public async Task Obs_retries_a_network_fault(ObsServerFault fault)
	{
		ObsConnection? observed = null;
		var second = new TaskCompletionSource<(ObsConnectionStatus Status, bool Connected)>(TaskCreationOptions
			.RunContinuationsAsynchronously);
		await using var server = new FakeObsWebSocketServer(_ => 100,
			fault,
			session =>
			{
				if (session == 2)
				{
					second.TrySetResult((observed!.Status, observed.IsConnected));
				}
			});
		await using var connection = new ObsConnection(
			new ObsClient(null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)),
			server.Url,
			null,
			_reconnectDelay);
		observed = connection;

		connection.Start();
		var after = await second.Task.WaitAsync(_deadline);

		Assert.Multiple(() =>
		{
			Assert.That(after.Connected, Is.False);
			Assert.That(after.Status, Is.EqualTo(ObsConnectionStatus.Reconnecting));
		});
	}

	private static Func<WebSocketPair, Task> HomeAssistantScript(NetworkFault fault)
	{
		return fault switch
		{
			NetworkFault.HandshakeStall => Stall,
			NetworkFault.HalfOpen => async pair =>
			{
				await pair.SendAsync(HomeAssistantAuthRequired);
				await pair.ReceiveAsync();
				pair.HalfOpen();
			},
			_ => async pair =>
			{
				await pair.SendAsync(HomeAssistantAuthRequired);
				await pair.ReceiveAsync();
				await pair.SendAsync(HomeAssistantAuthOk);
				var subscribe = await ReadIdAsync(pair);
				await pair.SendAsync($$"""{ "type": "result", "id": {{subscribe}}, "success": true }""");
				await pair.ReceiveAsync();
				await pair.AbortMidMessageAsync(4096, 512);
			}
		};
	}

	private static Func<WebSocketPair, Task> StreamerbotScript(NetworkFault fault)
	{
		return fault switch
		{
			NetworkFault.HandshakeStall => Stall,
			NetworkFault.HalfOpen => async pair =>
			{
				await pair.SendAsync(StreamerbotHello);
				await pair.ReceiveAsync();
				pair.HalfOpen();
			},
			_ => async pair =>
			{
				await pair.SendAsync(StreamerbotHello);
				await pair.ReceiveAsync();
				await pair.AbortMidMessageAsync();
			}
		};
	}

	private static Func<WebSocketPair, Task> MeldScript(NetworkFault fault)
	{
		return fault switch
		{
			NetworkFault.HandshakeStall => Stall,
			NetworkFault.HalfOpen => async pair =>
			{
				await pair.ReceiveAsync();
				pair.HalfOpen();
			},
			_ => async pair =>
			{
				await pair.ReceiveAsync();
				await pair.AbortMidMessageAsync(4096, 512);
			}
		};
	}

	private static Task Stall(WebSocketPair pair)
	{
		pair.StallHandshake();
		return Task.CompletedTask;
	}

	private static async Task<int> ReadIdAsync(WebSocketPair pair)
	{
		using var request = JsonDocument.Parse(await pair.ReceiveAsync());
		return request.RootElement.GetProperty("id").GetInt32();
	}

	internal enum NetworkFault
	{
		HandshakeStall,
		HalfOpen,
		AbortMidMessage
	}

	private readonly record struct AfterFault(bool Connected, bool Flagged);

	private sealed class ReconnectProbe
	{
		private readonly TaskCompletionSource<AfterFault> _second =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		private int _attempts;

		public Func<AfterFault>? Observe { get; set; }

		public Task<AfterFault> SecondAttempt => _second.Task;

		public T Attempt<T>(Func<T> create)
		{
			if (Interlocked.Increment(ref _attempts) == 2)
			{
				_second.TrySetResult(Observe!());
			}

			return create();
		}
	}

	private sealed class ScriptedServer(Func<WebSocketPair, Task> script) : IDisposable
	{
		private readonly ConcurrentBag<WebSocketPair> _pairs = [];

		public async Task<WebSocket> ConnectAsync(Uri uri, CancellationToken cancellationToken)
		{
			var pair = await WebSocketPair.CreateAsync();
			_pairs.Add(pair);
			_ = RunAsync(pair);
			return pair.Client;
		}

		public void Dispose()
		{
			foreach (var pair in _pairs)
			{
				pair.Dispose();
			}
		}

		private async Task RunAsync(WebSocketPair pair)
		{
			try
			{
				await script(pair);
			}
			catch (Exception ex) when (ex is WebSocketException
				or IOException
				or ObjectDisposedException
				or JsonException)
			{
			}
		}
	}
}
