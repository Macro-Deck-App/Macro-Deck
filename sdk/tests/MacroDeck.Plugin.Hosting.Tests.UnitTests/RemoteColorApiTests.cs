using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Colors;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class RemoteColorApiTests
{
	private const string Reference = "{{ vars.primary | color }}";

	private ScriptedColorHost _host = null!;
	private PluginConnectionState _state = null!;
	private HostStateCache _cache = null!;
	private FakePluginSocket _socket = null!;
	private RemoteColorApi _api = null!;
	private long _revision;

	[SetUp]
	public void SetUp()
	{
		_host = new ScriptedColorHost();
		_state = new PluginConnectionState();
		_cache = new HostStateCache(_state);
		_socket = new FakePluginSocket();
		_api = new RemoteColorApi(_host, _state, _cache, Serilog.Core.Logger.None);
	}

	[TearDown]
	public async Task TearDown()
	{
		_api.Dispose();
		await _socket.DisposeAsync();
	}

	[Test]
	public async Task A_watch_hands_the_host_the_table_and_gets_its_first_value_exactly_once()
	{
		Connect();
		var received = new ConcurrentQueue<string?>();

		await _api.WatchAsync(Reference, (color, _) => Record(received, color));
		var watch = _host.Tables[^1].Watches.Single();
		Push((watch.WatchId, "#3366ff"));
		Push((watch.WatchId, "#3366ff"));

		await WaitForAsync(() => !received.IsEmpty);
		await Task.Delay(100);
		Assert.Multiple(() =>
		{
			Assert.That(watch.Value, Is.EqualTo(Reference));
			Assert.That(received, Is.EqualTo(new[] { "#3366ff" }));
		});
	}

	[Test]
	public async Task Only_the_watch_whose_value_changed_is_called()
	{
		Connect();
		var first = new ConcurrentQueue<string?>();
		var second = new ConcurrentQueue<string?>();
		await _api.WatchAsync(Reference, (color, _) => Record(first, color));
		await _api.WatchAsync("{{ vars.accent | color }}", (color, _) => Record(second, color));
		var ids = _host.Tables[^1].Watches.Select(watch => watch.WatchId).ToArray();

		Push((ids[0], "#3366ff"), (ids[1], "#ff0000"));
		await WaitForAsync(() => first.Count == 1 && second.Count == 1);
		Push((ids[0], "#00ff00"), (ids[1], "#ff0000"));
		await WaitForAsync(() => first.Count == 2);
		await Task.Delay(100);

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.EqualTo(new[] { "#3366ff", "#00ff00" }));
			Assert.That(second, Is.EqualTo(new[] { "#ff0000" }));
		});
	}

	[Test]
	public async Task A_reconnect_sends_the_table_again_and_the_watch_keeps_firing()
	{
		Connect();
		var received = new ConcurrentQueue<string?>();
		await _api.WatchAsync(Reference, (color, _) => Record(received, color));
		var watchId = _host.Tables[^1].Watches.Single().WatchId;
		Push((watchId, "#3366ff"));
		await WaitForAsync(() => received.Count == 1);

		_revision = 0;
		_state.RaiseConnected(resumed: false);
		await WaitForAsync(() => _host.Tables.Count == 2);
		Push((watchId, "#3366ff"));
		Push((watchId, "#ff0000"));
		await WaitForAsync(() => received.Count == 2);

		Assert.Multiple(() =>
		{
			Assert.That(_host.Tables[^1].Watches.Single().WatchId, Is.EqualTo(watchId));
			Assert.That(received, Is.EqualTo(new[] { "#3366ff", "#ff0000" }));
		});
	}

	[Test]
	public async Task Initializing_again_drops_the_integrations_own_watches_but_not_injected_ones()
	{
		Connect();
		await _api.Lifecycle.WatchAsync(Reference, (_, _) => Task.CompletedTask);
		await _api.WatchAsync("#ffffff", (_, _) => Task.CompletedTask);

		_api.ReleaseLifecycleWatches();
		await _api.SyncAfterReleaseAsync();

		Assert.That(_host.Tables[^1].Watches.Select(watch => watch.Value), Is.EqualTo(new[] { "#ffffff" }));
	}

	[Test]
	public async Task A_disposed_watch_is_no_longer_called_and_leaves_the_table()
	{
		Connect();
		var received = new ConcurrentQueue<string?>();
		var watch = await _api.WatchAsync(Reference, (color, _) => Record(received, color));
		var watchId = _host.Tables[^1].Watches.Single().WatchId;

		await watch.DisposeAsync();
		Push((watchId, "#3366ff"));
		await Task.Delay(100);

		Assert.Multiple(() =>
		{
			Assert.That(received, Is.Empty);
			Assert.That(_host.Tables[^1].Watches, Is.Empty);
		});
	}

	[Test]
	public async Task A_watch_cancelled_while_waiting_to_sync_leaves_nothing_behind()
	{
		Connect();
		_host.Gate = new TaskCompletionSource();
		var first = _api.WatchAsync("#ffffff", (_, _) => Task.CompletedTask);
		using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

		Assert.That(async () => await _api.WatchAsync(Reference, (_, _) => Task.CompletedTask, null, cancellation.Token),
			Throws.InstanceOf<OperationCanceledException>());
		_host.Gate.SetResult();
		await first;
		await _api.WatchAsync("#000000", (_, _) => Task.CompletedTask);

		Assert.That(_host.Tables[^1].Watches.Select(watch => watch.Value), Is.EqualTo(new[] { "#ffffff", "#000000" }));
	}

	[Test]
	public async Task A_transient_failure_to_sync_the_table_is_retried()
	{
		Connect();
		_host.WatchFailures.Enqueue(ProtocolErrorCodes.RateLimited);

		await _api.WatchAsync(Reference, (_, _) => Task.CompletedTask);

		Assert.That(_host.Tables.Single().Watches.Single().Value, Is.EqualTo(Reference));
	}

	[Test]
	public async Task A_watch_the_host_would_not_take_still_gets_its_first_value()
	{
		Connect();
		_host.Resolved = "#3366ff";
		_host.WatchFailures.Enqueue(ProtocolErrorCodes.InvalidPayload);
		var received = new ConcurrentQueue<string?>();

		await _api.WatchAsync(Reference, (color, _) => Record(received, color));
		await WaitForAsync(() => !received.IsEmpty);

		Assert.That(received, Is.EqualTo(new[] { "#3366ff" }));
	}

	[Test]
	public async Task A_plugin_cannot_watch_more_than_the_limit()
	{
		Connect();
		for (var i = 0; i < ProtocolLimits.MaxColorWatches; i++)
		{
			await _api.WatchAsync("#000000", (_, _) => Task.CompletedTask);
		}

		Assert.ThrowsAsync<InvalidOperationException>(() => _api.WatchAsync("#000000", (_, _) => Task.CompletedTask));
	}

	[Test]
	public async Task A_host_without_the_colors_api_resolves_fixed_colours_locally_and_references_to_nothing()
	{
		Connect();
		_host.Unsupported = true;
		var received = new ConcurrentQueue<string?>();

		var literal = await _api.ResolveAsync("#ABC");
		var reference = await _api.ResolveAsync(Reference);
		await _api.WatchAsync("rgb(51, 102, 255)", (color, _) => Record(received, color));
		await WaitForAsync(() => !received.IsEmpty);

		Assert.Multiple(() =>
		{
			Assert.That(literal, Is.EqualTo("#aabbcc"));
			Assert.That(reference, Is.Null);
			Assert.That(received, Is.EqualTo(new[] { "#3366ff" }));
		});
	}

	private static Task Record(ConcurrentQueue<string?> received, string? color)
	{
		received.Enqueue(color);
		return Task.CompletedTask;
	}

	private void Push(params (string WatchId, string? Color)[] values)
		=> _cache.Apply(new ProtocolEnvelope
		{
			Type = MessageTypes.HostState,
			Id = Guid.NewGuid().ToString(),
			Payload = JsonSerializer.SerializeToElement(new HostStatePayload
				{
					Api = HostApis.Colors,
					Data = JsonSerializer.SerializeToElement(new ColorWatchStateDto
						{
							Revision = ++_revision,
							Values = [.. values.Select(value => new ColorWatchValueDto { WatchId = value.WatchId, Color = value.Color })]
						},
						PluginProtocolJson.Options)
				},
				PluginProtocolJson.Options)
		});

	private void Connect()
		=> _state.ActiveConnection = new PluginSessionConnection(_socket,
			TestSession.Create(),
			TestSession.Dispatcher(),
			_state,
			TimeProvider.System,
			Serilog.Core.Logger.None,
			hostInvoker: null,
			hostStateCache: null);

	private static async Task WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail("The expected state was never reached.");
			}

			await Task.Delay(10);
		}
	}

	private sealed class ScriptedColorHost : IHostInvoker
	{
		private readonly ConcurrentQueue<ColorsWatchesArguments> _tables = new();

		public IReadOnlyList<ColorsWatchesArguments> Tables => [.. _tables];

		public bool Unsupported { get; set; }

		public TaskCompletionSource? Gate { get; set; }

		public Queue<string> WatchFailures { get; } = new();

		public string? Resolved { get; set; }

		public async Task<JsonElement?> InvokeAsync(string api, string operation, object? arguments, CancellationToken cancellationToken)
		{
			Assert.That(api, Is.EqualTo(HostApis.Colors));

			if (Gate is { } gate && operation == HostOperations.Colors.Watches)
			{
				await gate.Task;
			}

			if (Unsupported)
			{
				throw HostInvocationException.From(new ProtocolError
				{
					Code = ProtocolErrorCodes.CapabilityUnsupported,
					Message = "The host has no api 'colors'.",
					Retryable = false
				});
			}

			if (operation == HostOperations.Colors.Watches)
			{
				if (WatchFailures.TryDequeue(out var code))
				{
					throw HostInvocationException.From(new ProtocolError { Code = code, Message = code, Retryable = false });
				}

				_tables.Enqueue((ColorsWatchesArguments)arguments!);
				return null;
			}

			return JsonSerializer.SerializeToElement(new ColorsResolveResult { Color = Resolved }, PluginProtocolJson.Options);
		}

		public bool TryComplete(ProtocolEnvelope result) => false;
	}
}
