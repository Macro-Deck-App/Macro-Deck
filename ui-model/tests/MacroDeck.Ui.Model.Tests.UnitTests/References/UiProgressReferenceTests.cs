using System.Text.Json;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.References;

namespace MacroDeck.Ui.Model.Tests.UnitTests.References;

/// <summary>
/// A progress reference is what lets a timeline advance without the tree changing, so what needs pinning
/// is that the two members whose absence carries a meaning are omitted rather than written out, that the
/// anchor has exactly one spelling, and that the shape is told apart from the time reference it shares a
/// property key with.
/// </summary>
[TestFixture]
public class UiProgressReferenceTests
{
	private static readonly DateTimeOffset _anchor = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

	// Stated once so the assertion that uses it fits a line.
	private const string _haltedJson =
		"""{"$progress":{"positionMs":42000,"durationMs":215000,"rate":0,"anchor":"2026-08-25T12:00:00.000Z"}}""";

	[Test]
	public void A_position_advancing_at_normal_speed_omits_its_rate()
	{
		// Absent means 1, so the case whose display changes every second costs no key.
		Assert.That(UiCanonicalJson.Serialize(UiProgressReference.Advancing(42_000, _anchor, 215_000)),
			Is.EqualTo(
				"""{"$progress":{"positionMs":42000,"durationMs":215000,"anchor":"2026-08-25T12:00:00.000Z"}}"""));
	}

	[Test]
	public void A_halted_position_writes_its_rate_as_zero()
	{
		// Paused is a value, not an omission - a reader cannot tell "stopped" from "normal speed" otherwise.
		Assert.That(UiCanonicalJson.Serialize(UiProgressReference.Halted(42_000, _anchor, 215_000)),
			Is.EqualTo(_haltedJson));
	}

	[Test]
	public void An_unknown_length_is_omitted_rather_than_written_null()
	{
		Assert.That(UiCanonicalJson.Serialize(UiProgressReference.Advancing(42_000, _anchor)),
			Is.EqualTo("""{"$progress":{"positionMs":42000,"anchor":"2026-08-25T12:00:00.000Z"}}"""));
	}

	[Test]
	public void An_anchor_in_another_offset_is_written_as_the_same_instant_in_utc()
	{
		// One instant has one spelling: two producers that wrote the same moment differently would give one
		// tree two sets of canonical bytes.
		var reference = UiProgressReference.Advancing(0,
			new DateTimeOffset(2026,
				8,
				25,
				14,
				0,
				0,
				TimeSpan.FromHours(2)));

		Assert.That(UiCanonicalJson.Serialize(reference),
			Is.EqualTo("""{"$progress":{"positionMs":0,"anchor":"2026-08-25T12:00:00.000Z"}}"""));
	}

	[Test]
	public void An_explicit_null_rate_reads_back_as_normal_speed()
	{
		var parsed = JsonSerializer.Deserialize<UiProgressReference>(
			"""{"$progress":{"positionMs":1,"durationMs":null,"rate":null,"anchor":"2026-08-25T12:00:00Z"}}""",
			UiCanonicalJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(parsed!.Rate, Is.Null);
			Assert.That(parsed.DurationMs, Is.Null);
			// Tolerant read, strict write, exactly as the time reference's zone is.
			Assert.That(UiCanonicalJson.Serialize(parsed),
				Is.EqualTo("""{"$progress":{"positionMs":1,"anchor":"2026-08-25T12:00:00.000Z"}}"""));
		});
	}

	[Test]
	public void A_written_reference_reads_back_to_the_same_value()
	{
		var original = UiProgressReference.Halted(90_500, _anchor, 215_000);

		var parsed = JsonSerializer.Deserialize<UiProgressReference>(UiCanonicalJson.Serialize(original),
			UiCanonicalJson.Options);

		Assert.That(parsed, Is.EqualTo(original));
	}

	[TestCase("""{"positionMs":1,"anchor":"2026-08-25T12:00:00Z"}""")]
	[TestCase("""{"$progress":"2026-08-25T12:00:00Z"}""")]
	[TestCase("""{"$time":{"zone":"America/New_York"}}""")]
	[TestCase("""{"$progress":{"anchor":"2026-08-25T12:00:00Z"}}""")]
	[TestCase("""{"$progress":{"positionMs":1}}""")]
	[TestCase("""{"$progress":{"positionMs":1,"anchor":"2026-08-25T12:00:00Z","zone":"UTC"}}""")]
	public void A_shape_that_is_not_a_progress_reference_is_rejected(string json)
	{
		Assert.That(() => JsonSerializer.Deserialize<UiProgressReference>(json, UiCanonicalJson.Options),
			Throws.InstanceOf<JsonException>());
	}

	[Test]
	public void A_progress_reference_is_not_readable_as_a_time_reference()
	{
		// The two markers share the `value` property key and are told apart by the node type that carries
		// them. A reader that met the wrong one has to fail rather than silently resolve something.
		Assert.That(() => JsonSerializer.Deserialize<UiTimeReference>(
				"""{"$progress":{"positionMs":1,"anchor":"2026-08-25T12:00:00Z"}}""",
				UiCanonicalJson.Options),
			Throws.InstanceOf<JsonException>());
	}
}
