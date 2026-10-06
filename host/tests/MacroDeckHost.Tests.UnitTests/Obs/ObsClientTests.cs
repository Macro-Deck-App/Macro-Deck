using System.Net;
using System.Net.Sockets;
using MacroDeckHost.Integrations.Obs;
using OBSWebsocketDotNet.Types;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Obs;

[TestFixture]
internal sealed class ObsClientTests
{
	// Regression test for the OBS UnobservedTaskException flood (issue #65): when OBS is not
	// running, Connect must report failure through the Disconnected event without handing the URL
	// to obs-websocket-dotnet's ConnectAsync (whose fire-and-forget StartOrFail() faults an
	// unobserved task). The synthetic "unreachable" reason only appears when the reachability probe
	// short-circuits the connect, so this test is red without the probe.
	[Test]
	public async Task Connect_WhenObsUnreachable_RaisesUnreachableDisconnectedWithoutThrowing()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();

		var client = new ObsClient();
		var disconnected = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		client.Disconnected += (_, reason) => disconnected.TrySetResult(reason);

		client.Connect($"ws://127.0.0.1:{port}", null);

		var completed = await Task.WhenAny(disconnected.Task, Task.Delay(TimeSpan.FromSeconds(5)));

		Assert.That(completed, Is.SameAs(disconnected.Task), "Disconnected event was not raised");
		Assert.That(await disconnected.Task, Is.EqualTo("unreachable"));
		Assert.That(client.IsConnected, Is.False);
	}

	[Test]
	public async Task Connect_WhenObsIsUnreachable_DoesNotLogEachRetry()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();

		var sink = new CollectingSink();
		var logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Sink(sink)
			.CreateLogger();
		var client = new ObsClient(logger);
		var disconnected = 0;
		var allDisconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		client.Disconnected += (_, _) =>
		{
			if (Interlocked.Increment(ref disconnected) == 3)
			{
				allDisconnected.TrySetResult();
			}
		};

		client.Connect($"ws://127.0.0.1:{port}", null);
		client.Connect($"ws://127.0.0.1:{port}", null);
		client.Connect($"ws://127.0.0.1:{port}", null);

		var completed = await Task.WhenAny(allDisconnected.Task, Task.Delay(TimeSpan.FromSeconds(5)));

		Assert.Multiple(() =>
		{
			Assert.That(completed, Is.SameAs(allDisconnected.Task), "Disconnected events were not raised");
			Assert.That(sink.Events, Is.Empty);
		});
	}

	[Test]
	public async Task Subscriptions_are_the_minimum_set_and_never_include_the_per_frame_events()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		var client = new ObsClient();
		var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		client.Disconnected += (_, _) => disconnected.TrySetResult();

		client.Connect($"ws://127.0.0.1:{port}", null);
		await Task.WhenAny(disconnected.Task, Task.Delay(TimeSpan.FromSeconds(5)));

		var mask = client.EventSubscriptions;
		Assert.Multiple(() =>
		{
			Assert.That(mask.HasFlag(EventSubscription.InputActiveStateChanged), Is.True);
			Assert.That(mask.HasFlag(EventSubscription.InputShowStateChanged), Is.True);
			Assert.That(mask.HasFlag(EventSubscription.InputVolumeMeters), Is.False);
			Assert.That(mask.HasFlag(EventSubscription.SceneItemTransformChanged), Is.False);
			Assert.That(mask & (EventSubscription.Transitions | EventSubscription.SceneItems |
				EventSubscription.MediaInputs | EventSubscription.Vendors | EventSubscription.Canvases),
				Is.EqualTo(EventSubscription.None));
		});

		var required = EventSubscription.General | EventSubscription.Config | EventSubscription.Scenes |
			EventSubscription.Inputs | EventSubscription.Filters | EventSubscription.Outputs | EventSubscription.Ui;
		Assert.That(mask & required, Is.EqualTo(required),
			"CustomEvent (General), profile (Config), scenes, mute and settings (Inputs), filters, outputs and studio mode (Ui)");
	}

	[Test]
	public async Task A_status_read_while_obs_is_loading_is_abandoned_instead_of_reading_as_stopped()
	{
		await using var server = new FakeObsWebSocketServer(_ => ObsRequestException.NotReady);
		var client = await ConnectedClientAsync(server);
		try
		{
			Assert.Throws<ObsNotReadyException>(() => client.QueryStatus());
		}
		finally
		{
			client.Disconnect();
		}
	}

	[Test]
	public async Task A_status_read_that_obs_refuses_for_another_reason_still_answers()
	{
		await using var server = new FakeObsWebSocketServer(_ => ObsRequestException.ResourceNotFound);
		var client = await ConnectedClientAsync(server);
		ObsStatus status;
		try
		{
			status = client.QueryStatus();
		}
		finally
		{
			client.Disconnect();
		}

		Assert.That(status.IsStreaming, Is.False);
	}

	[Test]
	public async Task Switching_to_a_scene_collection_obs_does_not_have_reports_its_code()
	{
		await using var server = new FakeObsWebSocketServer(_ => ObsRequestException.ResourceNotFound);
		var client = await ConnectedClientAsync(server);
		ObsRequestException? error;
		try
		{
			error = Assert.Throws<ObsRequestException>(() => client.SetCurrentSceneCollection("Deleted"));
		}
		finally
		{
			client.Disconnect();
		}

		Assert.Multiple(() =>
		{
			Assert.That(error!.Code, Is.EqualTo(ObsRequestException.ResourceNotFound));
			Assert.That(server.Requests, Does.Contain("SetCurrentSceneCollection"));
		});
	}

	private static async Task<ObsClient> ConnectedClientAsync(FakeObsWebSocketServer server)
	{
		var client = new ObsClient();
		var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		client.Connected += (_, _) => connected.TrySetResult();
		client.Connect(server.Url, null);
		await connected.Task.WaitAsync(TimeSpan.FromSeconds(10));
		return client;
	}

	private sealed class CollectingSink : ILogEventSink
	{
		public List<LogEvent> Events { get; } = [];

		public void Emit(LogEvent logEvent) => Events.Add(logEvent);
	}
}
