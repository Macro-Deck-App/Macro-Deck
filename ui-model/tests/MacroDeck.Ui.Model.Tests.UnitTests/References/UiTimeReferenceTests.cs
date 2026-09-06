using System.Text.Json;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.References;

namespace MacroDeck.Ui.Model.Tests.UnitTests.References;

/// <summary>
/// A time reference is the widget profile's answer to "the display advances but the tree does not", so
/// what needs pinning is that it carries no instant and that its zone-less spelling is one exact shape.
/// </summary>
[TestFixture]
public class UiTimeReferenceTests
{
	[Test]
	public void A_reference_carries_a_zone_and_nothing_else()
	{
		var members = typeof(UiTimeReference)
			.GetProperties()
			.Select(property => property.Name)
			.ToArray();

		Assert.That(members,
			Is.EquivalentTo(new[] { nameof(UiTimeReference.Zone) }),
			"A captured instant on this type would make the tree change whenever it was built.");
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	public void An_absent_zone_is_omitted_rather_than_written_null(string? zone)
	{
		Assert.That(UiCanonicalJson.Serialize(UiTimeReference.InZone(zone)),
			Is.EqualTo("""{"$time":{}}"""));
	}

	[Test]
	public void A_configured_zone_travels_inside_the_marker()
	{
		Assert.That(UiCanonicalJson.Serialize(UiTimeReference.InZone("America/New_York")),
			Is.EqualTo("""{"$time":{"zone":"America/New_York"}}"""));
	}

	[Test]
	public void An_explicit_null_zone_reads_back_as_the_readers_own_zone()
	{
		var parsed = JsonSerializer.Deserialize<UiTimeReference>("""{"$time":{"zone":null}}""",
			UiCanonicalJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(parsed!.Zone, Is.Null);
			// Tolerant read, strict write: a null spelling can be met but can never leave this process.
			Assert.That(UiCanonicalJson.Serialize(parsed), Is.EqualTo("""{"$time":{}}"""));
		});
	}

	[TestCase("""{"zone":"America/New_York"}""")]
	[TestCase("""{"$time":"America/New_York"}""")]
	[TestCase("""{"$time":{"instant":"2026-08-24T10:09:30Z"}}""")]
	public void A_shape_that_is_not_a_time_reference_is_rejected(string json)
	{
		Assert.That(() => JsonSerializer.Deserialize<UiTimeReference>(json, UiCanonicalJson.Options),
			Throws.InstanceOf<JsonException>());
	}
}
