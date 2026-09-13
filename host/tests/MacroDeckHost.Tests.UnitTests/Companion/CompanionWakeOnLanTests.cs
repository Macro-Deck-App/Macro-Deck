using System.Net;
using System.Net.NetworkInformation;
using MacroDeckHost.Application.Network.Discovery;
using MacroDeckHost.Application.Ui.Transport.Messages.Devices;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Companion;

[TestFixture]
internal sealed class CompanionWakeOnLanTests
{
	[Test]
	public async Task Each_connection_learns_the_host_cards_once_on_its_first_report()
	{
		var harness = new CompanionHarness();
		harness.Interfaces.Interfaces.Add(new NetworkInterfaceSnapshot(4,
			"en0",
			"Ethernet",
			NetworkInterfaceType.Ethernet,
			true,
			[IPAddress.Parse("192.168.20.13")],
			"001122334455"));
		var device = harness.AddDevice("Pixel");

		await harness.ReportAsync("connection-1", device);
		await harness.ReportAsync("connection-1", device);
		await harness.ReportAsync("connection-2", device);

		var sent = harness.Transport.ConnectionMessages
			.Where(entry => entry.Message is CompanionWakeOnLanEvent)
			.ToList();
		Assert.Multiple(() =>
		{
			Assert.That(sent.Select(entry => entry.ConnectionId), Is.EqualTo(new[] { "connection-1", "connection-2" }));
			var wake = (CompanionWakeOnLanEvent)sent[0].Message;
			Assert.That(wake.InstanceName, Is.EqualTo("studio-pc"));
			Assert.That(wake.MacAddresses, Is.EqualTo(new[] { "00:11:22:33:44:55" }));
		});
	}

	[Test]
	public async Task A_failed_send_is_logged_and_the_report_still_counts()
	{
		var harness = new CompanionHarness();
		harness.Transport.FailConnectionSends = true;
		var device = harness.AddDevice("Pixel");

		await harness.ReportAsync("connection-1", device);

		Assert.Multiple(() =>
		{
			Assert.That(harness.DeviceRegistry.TryGetState(device, out _), Is.True);
			Assert.That(harness.Entries.Select(entry => entry.Id), Does.Contain(device));
			Assert.That(harness.Sink.Events.Any(e => e.Level == LogEventLevel.Warning &&
					e.Exception is InvalidOperationException),
				Is.True);
		});
	}
}
