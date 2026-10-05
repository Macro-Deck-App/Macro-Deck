using System.Text;
using MacroDeckHost.Integrations.Obs;

namespace MacroDeckHost.Tests.UnitTests.Obs;

[TestFixture]
internal sealed class ObsCustomEventTests
{
	[Test]
	public void A_well_formed_object_exposes_the_name_the_raw_json_and_flat_scalar_fields()
	{
		const string json = """{"eventName":"goal","team":"red","score":3,"ratio":0.5,"final":true,"nested":{"a":1}}""";

		var payload = ObsCustomEvent.Parse(json)!;

		Assert.Multiple(() =>
		{
			Assert.That(payload["eventName"], Is.EqualTo("goal"));
			Assert.That(payload["data"], Is.EqualTo(json));
			Assert.That(payload["field_team"], Is.EqualTo("red"));
			Assert.That(payload["field_score"], Is.EqualTo(3L));
			Assert.That(payload["field_ratio"], Is.EqualTo(0.5));
			Assert.That(payload["field_final"], Is.EqualTo(true));
			Assert.That(payload.ContainsKey("field_nested"), Is.False, "nested values are only reachable through data");
			Assert.That(payload.ContainsKey("field_eventName"), Is.False);
		});
	}

	[Test]
	public void An_object_without_a_name_has_an_empty_event_name()
	{
		var payload = ObsCustomEvent.Parse("""{"a":1}""")!;

		Assert.That(payload["eventName"], Is.EqualTo(string.Empty));
	}

	[TestCase("[1,2,3]")]
	[TestCase("\"text\"")]
	[TestCase("42")]
	[TestCase("null")]
	public void A_root_that_is_not_an_object_still_arrives_as_data_without_fields(string json)
	{
		var payload = ObsCustomEvent.Parse(json)!;

		Assert.Multiple(() =>
		{
			Assert.That(payload["data"], Is.EqualTo(json));
			Assert.That(payload["eventName"], Is.EqualTo(string.Empty));
			Assert.That(payload.Keys.Count(key => key.StartsWith("field_", StringComparison.Ordinal)), Is.Zero);
		});
	}

	[TestCase("")]
	[TestCase("not json")]
	[TestCase("{\"a\":")]
	public void Text_that_is_not_json_is_dropped(string json)
		=> Assert.That(ObsCustomEvent.Parse(json), Is.Null);

	[Test]
	public void A_payload_of_exactly_the_limit_is_accepted_and_one_character_more_is_dropped()
	{
		var prefix = "{\"a\":\"";
		var atLimit = prefix + new string('x', ObsCustomEvent.MaxPayloadChars - prefix.Length - 2) + "\"}";

		Assert.Multiple(() =>
		{
			Assert.That(atLimit, Has.Length.EqualTo(ObsCustomEvent.MaxPayloadChars));
			Assert.That(ObsCustomEvent.Parse(atLimit), Is.Not.Null);
			Assert.That(ObsCustomEvent.Parse(atLimit.Insert(10, "x")), Is.Null);
		});
	}

	[Test]
	public void Nesting_beyond_the_depth_limit_is_dropped()
	{
		var deep = new StringBuilder().Insert(0, "[", 40).Append(new string(']', 40)).ToString();

		Assert.That(ObsCustomEvent.Parse(deep), Is.Null);
	}

	[Test]
	public void Hostile_field_names_are_skipped_and_hostile_values_arrive_verbatim()
	{
		const string json = """
			{"__proto__":"x","../etc":"y","a b":"z","{{ vars.x }}":"w","configuration":"spoof","data":"spoof",
			 "path":"../../etc/passwd","tpl":"{{ vars.secret }}"}
			""";

		var payload = ObsCustomEvent.Parse(json)!;

		Assert.Multiple(() =>
		{
			Assert.That(payload["field___proto__"], Is.EqualTo("x"), "a valid identifier keeps its prefix and never shadows");
			Assert.That(payload.Keys, Does.Not.Contain("field_../etc"));
			Assert.That(payload.Keys, Does.Not.Contain("field_a b"));
			Assert.That(payload.Keys.Any(key => key.Contains("{{", StringComparison.Ordinal)), Is.False);
			Assert.That(payload.ContainsKey("configuration"), Is.False);
			Assert.That(payload["data"], Is.EqualTo(json));
			Assert.That(payload["field_data"], Is.EqualTo("spoof"));
			Assert.That(payload["field_path"], Is.EqualTo("../../etc/passwd"));
			Assert.That(payload["field_tpl"], Is.EqualTo("{{ vars.secret }}"));
		});
	}

	[Test]
	public void Only_the_first_sixteen_distinct_fields_are_exposed()
	{
		var json = "{" + string.Join(",", Enumerable.Range(0, 40).Select(i => $"\"k{i}\":{i}")) + "}";

		var payload = ObsCustomEvent.Parse(json)!;

		Assert.That(payload.Keys.Count(key => key.StartsWith("field_", StringComparison.Ordinal)),
			Is.EqualTo(ObsCustomEvent.MaxFields));
	}

	[Test]
	public void Long_values_and_names_are_cut()
	{
		var json = $$"""{"eventName":"{{new string('n', 500)}}","v":"{{new string('v', 5000)}}"}""";

		var payload = ObsCustomEvent.Parse(json)!;

		Assert.Multiple(() =>
		{
			Assert.That((string)payload["eventName"]!, Has.Length.EqualTo(ObsCustomEvent.MaxEventNameChars));
			Assert.That((string)payload["field_v"]!, Has.Length.EqualTo(ObsCustomEvent.MaxFieldValueChars));
		});
	}

	[Test]
	public void The_limiter_allows_a_burst_then_drops_and_recovers_with_time()
	{
		var time = new Delegation.FakeTimeProvider();
		var limiter = new ObsCustomEventLimiter(time, perSecond: 10, burst: 5);

		var accepted = Enumerable.Range(0, 20).Count(_ => limiter.TryAcquire(out _));
		time.Advance(TimeSpan.FromSeconds(1));
		var recovered = limiter.TryAcquire(out var droppedBefore);

		Assert.Multiple(() =>
		{
			Assert.That(accepted, Is.EqualTo(5));
			Assert.That(recovered, Is.True);
			Assert.That(droppedBefore, Is.EqualTo(15));
		});
	}
}
