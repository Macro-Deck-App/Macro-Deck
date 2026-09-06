using System.Net;
using System.Net.Sockets;
using MacroDeckHost.Integrations.Obs;
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

	private sealed class CollectingSink : ILogEventSink
	{
		public List<LogEvent> Events { get; } = [];

		public void Emit(LogEvent logEvent) => Events.Add(logEvent);
	}
}
