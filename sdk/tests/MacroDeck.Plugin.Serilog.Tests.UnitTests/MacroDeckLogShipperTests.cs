using System.Text.Json;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Logging;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Serilog.Tests.UnitTests.Support;
using Serilog;
using Serilog.Events;

namespace MacroDeck.Plugin.Serilog.Tests.UnitTests;

/// <summary>
/// <see cref="MacroDeckLogShipper" />'s batching, protocol-cap and failure-handling contract, driven
/// against a fully controllable <see cref="FakeLogTransport" /> rather than a real socket.
/// </summary>
[TestFixture]
public class MacroDeckLogShipperTests
{
	[Test]
	public async Task Batches_never_exceed_the_protocol_limits()
	{
		var options = new MacroDeckLoggingOptions
		{
			BatchSize = 200,
			FlushInterval = TimeSpan.FromMilliseconds(25),
			QueueCapacity = 2000
		};

		var (sink, shipper, transport) = ShipperFactory.Create(options);

		await shipper.StartAsync(CancellationToken.None);
		try
		{
			// Large enough, and enough of them, that a single drained batch's envelope exceeds
			// ProtocolLimits.MaxMessageBytes and has to be split.
			var longMessage = new string('x', 3000);
			var baseTime = DateTimeOffset.UtcNow;

			for (var i = 0; i < 200; i++)
			{
				sink.Emit(LogEventFactory.Create(baseTime.AddMilliseconds(i), LogEventLevel.Information, longMessage));
			}

			await TestHelpers.WaitForAsync(() => TotalEventCount(transport) >= 200, TimeSpan.FromSeconds(10));

			var envelopes = transport.Sent;

			Assert.That(envelopes,
				Has.Count.GreaterThan(1),
				"A batch this large must have been split into more than one envelope.");

			Assert.Multiple(() =>
			{
				foreach (var envelope in envelopes)
				{
					var payload = envelope.Payload!.Value.Deserialize<LogPublishPayload>(PluginProtocolJson.Options)!;
					Assert.That(payload.Events.Count, Is.LessThanOrEqualTo(ProtocolLimits.MaxLogEventsPerBatch));
					Assert.That(ProtocolEnvelopeWriter.WriteToUtf8Bytes(envelope).Length,
						Is.LessThanOrEqualTo(ProtocolLimits.MaxMessageBytes));
				}
			});

			// A handful of events, far under BatchSize - must still flush on the interval rather than
			// waiting for a full batch that will never come.
			var before = transport.Sent.Count;
			sink.Emit(LogEventFactory.Create(DateTimeOffset.UtcNow, LogEventLevel.Information, "partial-1"));
			sink.Emit(LogEventFactory.Create(DateTimeOffset.UtcNow, LogEventLevel.Information, "partial-2"));
			sink.Emit(LogEventFactory.Create(DateTimeOffset.UtcNow, LogEventLevel.Information, "partial-3"));

			await TestHelpers.WaitForAsync(() => transport.Sent.Count > before, TimeSpan.FromSeconds(5));
		}
		finally
		{
			await shipper.StopAsync(CancellationToken.None);
			shipper.Dispose();
		}
	}

	[Test]
	public async Task A_transport_that_always_throws_does_not_recurse_and_does_not_surface_to_the_author()
	{
		var options = new MacroDeckLoggingOptions
		{
			FlushInterval = TimeSpan.FromMilliseconds(50),
			EnableFallbackFile = false
		};

		var (sink, shipper, transport) = ShipperFactory.Create(options);
		transport.OnSend = _ => throw new IOException("simulated transport failure");

		var collecting = new CollectingSink();
		var logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Sink(sink)
			.WriteTo.Sink(collecting)
			.CreateLogger();

		await shipper.StartAsync(CancellationToken.None);
		try
		{
			Assert.DoesNotThrow(() =>
			{
				for (var i = 0; i < 30; i++)
				{
					logger.Information("event {Index}", i);
				}
			});

			// Long enough for the flush interval and at least one backoff step to have kicked in.
			await Task.Delay(TimeSpan.FromSeconds(2));

			Assert.Multiple(() =>
			{
				// Bounded, not proportional to the 30 events logged and not inflated by the shipper's own
				// failure diagnostics re-entering the pipeline (which would show up here as extra attempts).
				Assert.That(transport.AttemptCount, Is.LessThan(10));

				// The second sink in the same pipeline is the ground truth for what the author actually
				// logged - exactly 30, no self-log-originated extras mixed in.
				Assert.That(collecting.Events, Has.Count.EqualTo(30));
			});
		}
		finally
		{
			await shipper.StopAsync(CancellationToken.None);
			shipper.Dispose();
		}
	}

	private static int TotalEventCount(FakeLogTransport transport)
		=> transport.Sent.Sum(envelope
			=> envelope.Payload!.Value.Deserialize<LogPublishPayload>(PluginProtocolJson.Options)!.Events.Count);
}
