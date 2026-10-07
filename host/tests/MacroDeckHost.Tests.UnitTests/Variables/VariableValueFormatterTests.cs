using MacroDeckHost.Application.Variables;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.Variables;

public class VariableValueFormatterTests
{
	private static (string Value, string Unit) Format(double value, string? kind, string? unit)
	{
		var formatted = VariableValueFormatter.Format(value, kind, unit, 0);
		return (formatted.Value.ToString(), formatted.Unit.ToString());
	}

	[Test]
	public void A_byte_count_climbs_the_1024_ladder()
		=> Assert.That(Format(1536, VariableSemanticKinds.Bytes, "B"), Is.EqualTo(("1.5", "KB")));

	[Test]
	public void A_byte_rate_climbs_the_same_ladder_per_second()
		=> Assert.Multiple(() =>
		{
			Assert.That(Format(512, VariableSemanticKinds.BytesPerSecond, "B/s"), Is.EqualTo(("512", "B/s")));
			Assert.That(Format(3.5 * 1024 * 1024, VariableSemanticKinds.BytesPerSecond, "B/s"),
				Is.EqualTo(("3.5", "MB/s")));
		});

	[Test]
	public void A_kind_the_host_does_not_know_renders_the_number_with_its_declared_unit()
		=> Assert.That(Format(1048576, "kindFromANewerSdk", "B/s"), Is.EqualTo(("1048576", "B/s")));
}
