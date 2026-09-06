using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Limits;

[TestFixture]
public class ProtocolLimitsTests
{
	[Test]
	public void Backpressure_watermarks_and_queue_depth_are_strictly_ordered()
	{
		Assert.Multiple(() =>
		{
			Assert.That(ProtocolLimits.QueueLowWatermark, Is.LessThan(ProtocolLimits.QueueHighWatermark));
			Assert.That(ProtocolLimits.QueueHighWatermark, Is.LessThan(ProtocolLimits.MaxInboundQueueDepth));
		});
	}

	[Test]
	public void Max_asset_chunk_bytes_is_smaller_than_max_message_bytes()
		=> Assert.That(ProtocolLimits.MaxAssetChunkBytes, Is.LessThan(ProtocolLimits.MaxMessageBytes));

	[Test]
	public void Max_json_depth_matches_the_serializers_configured_max_depth()
		=> Assert.That(PluginProtocolJson.Options.MaxDepth, Is.EqualTo(ProtocolLimits.MaxJsonDepth));

	[Test]
	public void All_timeouts_are_positive()
	{
		Assert.Multiple(() =>
		{
			Assert.That(ProtocolTimeouts.Handshake, Is.GreaterThan(TimeSpan.Zero));
			Assert.That(ProtocolTimeouts.DefaultRequest, Is.GreaterThan(TimeSpan.Zero));
			Assert.That(ProtocolTimeouts.CapabilityInvoke, Is.GreaterThan(TimeSpan.Zero));
			Assert.That(ProtocolTimeouts.AssetUpload, Is.GreaterThan(TimeSpan.Zero));
			Assert.That(ProtocolTimeouts.KeepAliveInterval, Is.GreaterThan(TimeSpan.Zero));
			Assert.That(ProtocolTimeouts.KeepAliveTimeout, Is.GreaterThan(TimeSpan.Zero));
			Assert.That(ProtocolTimeouts.SessionResumeWindow, Is.GreaterThan(TimeSpan.Zero));
			Assert.That(ProtocolTimeouts.GracefulClose, Is.GreaterThan(TimeSpan.Zero));
		});
	}

	[Test]
	public void Keep_alive_timeout_is_at_least_three_keep_alive_intervals()
		=> Assert.That(ProtocolTimeouts.KeepAliveTimeout,
			Is.GreaterThanOrEqualTo(ProtocolTimeouts.KeepAliveInterval * 3));

	[Test]
	public void Close_codes_are_unique()
		=> Assert.That(ProtocolCloseCodes.All, Is.Unique);

	[Test]
	public void Application_close_codes_fall_within_the_reserved_4000_to_4999_range()
	{
		var applicationCloseCodes = ProtocolCloseCodes.All.Where(code => code != ProtocolCloseCodes.QueueOverflow);

		Assert.Multiple(() =>
		{
			foreach (var code in applicationCloseCodes)
			{
				Assert.That(code, Is.InRange(4000, 4999));
			}
		});
	}

	[Test]
	public void Queue_overflow_is_the_one_close_code_reserved_from_the_rfc6455_range_not_the_application_range()
		=> Assert.That(ProtocolCloseCodes.QueueOverflow, Is.EqualTo(1013));

	[Test]
	public void Supervisor_shutdown_is_a_declared_close_code()
		=> Assert.That(ProtocolCloseCodes.All, Does.Contain(ProtocolCloseCodes.SupervisorShutdown));
}
