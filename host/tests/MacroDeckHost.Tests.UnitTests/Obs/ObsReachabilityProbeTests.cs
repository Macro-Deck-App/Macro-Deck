using System.Net;
using System.Net.Sockets;
using MacroDeckHost.Integrations.Obs;

namespace MacroDeckHost.Tests.UnitTests.Obs;

[TestFixture]
internal sealed class ObsReachabilityProbeTests
{
	[Test]
	public async Task IsReachableAsync_ReturnsTrue_WhenPortIsOpen()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		try
		{
			var port = ((IPEndPoint)listener.LocalEndpoint).Port;

			var reachable = await ObsReachabilityProbe.IsReachableAsync($"ws://127.0.0.1:{port}",
				TimeSpan.FromSeconds(2));

			Assert.That(reachable, Is.True);
		}
		finally
		{
			listener.Stop();
		}
	}

	[Test]
	public async Task IsReachableAsync_ReturnsFalse_WhenPortIsClosed()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();

		var reachable = await ObsReachabilityProbe.IsReachableAsync($"ws://127.0.0.1:{port}", TimeSpan.FromSeconds(2));

		Assert.That(reachable, Is.False);
	}

	[Test]
	public async Task IsReachableAsync_ReturnsFalse_WhenUrlIsMalformed()
	{
		var reachable = await ObsReachabilityProbe.IsReachableAsync("not-a-valid-url", TimeSpan.FromSeconds(2));

		Assert.That(reachable, Is.False);
	}

	[Test]
	public async Task IsReachableAsync_ReturnsFalse_WhenAlreadyCancelled()
	{
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		var reachable = await ObsReachabilityProbe.IsReachableAsync("ws://127.0.0.1:4455",
			TimeSpan.FromSeconds(2),
			cts.Token);

		Assert.That(reachable, Is.False);
	}
}
