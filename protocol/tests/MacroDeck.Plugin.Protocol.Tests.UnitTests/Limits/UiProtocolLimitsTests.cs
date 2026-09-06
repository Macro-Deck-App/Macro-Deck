using System.Reflection;
using MacroDeck.Plugin.Protocol.Limits;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Limits;

/// <summary>
/// The UI limits travel inside the envelopes the protocol limits already bound, so they only make sense
/// in relation to those. The concrete numbers are a tuning decision and are deliberately not asserted.
/// </summary>
[TestFixture]
public class UiProtocolLimitsTests
{
	[Test]
	public void The_ui_limits_stand_in_a_consistent_relation_to_the_protocol_limits_they_travel_inside()
	{
		var uiLimits = typeof(ProtocolLimits)
			.GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(field => field.IsLiteral && field.Name.StartsWith("MaxUi", StringComparison.Ordinal))
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(uiLimits, Is.Not.Empty);

			foreach (var field in uiLimits)
			{
				Assert.That((int)field.GetRawConstantValue()!,
					Is.GreaterThan(0),
					$"{field.Name} must be a positive bound.");
			}

			// A patch that approaches a whole tree means the provider should have re-snapshotted.
			Assert.That(ProtocolLimits.MaxUiPatchBytes, Is.LessThanOrEqualTo(ProtocolLimits.MaxUiTreeBytes));

			// A tree plus its envelope has to fit one frame, or an oversize tree closes the connection
			// instead of being answered with a session error.
			Assert.That(ProtocolLimits.MaxUiTreeBytes, Is.LessThanOrEqualTo(ProtocolLimits.MaxMessageBytes));

			Assert.That(ProtocolLimits.MaxUiResourceBytes, Is.LessThanOrEqualTo(ProtocolLimits.MaxAssetBytes));

			// A burst smaller than the steady rate could never be refilled to.
			Assert.That(ProtocolLimits.MaxUiUpdateBurst,
				Is.GreaterThanOrEqualTo(ProtocolLimits.MaxUiUpdatesPerSecond));

			Assert.That(ProtocolLimits.MaxUiAttachmentsPerSession, Is.GreaterThanOrEqualTo(1));
			Assert.That(ProtocolLimits.MaxUiSessionsPerProvider, Is.GreaterThanOrEqualTo(1));
		});
	}
}
