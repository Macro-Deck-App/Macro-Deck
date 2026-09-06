using System.Globalization;
using MacroDeckHost.Integrations.Http.Actions;

namespace MacroDeckHost.Tests.UnitTests.Http;

[TestFixture]
internal sealed class HttpActionValuesTests
{
	[Test]
	public void ReadNumber_accepts_long_double_and_string()
	{
		var parameters = Parameters(("l", 5L), ("d", 5.5d), ("s", "7"));

		Assert.Multiple(() =>
		{
			Assert.That(HttpActionValues.ReadNumber(parameters, "l", 0), Is.EqualTo(5d));
			Assert.That(HttpActionValues.ReadNumber(parameters, "d", 0), Is.EqualTo(5.5d));
			Assert.That(HttpActionValues.ReadNumber(parameters, "s", 0), Is.EqualTo(7d));
			Assert.That(HttpActionValues.ReadNumber(parameters, "missing", 42), Is.EqualTo(42d));
		});
	}

	[Test]
	public void ReadDurationMilliseconds_accepts_long_double_and_suffixed_wire_text()
	{
		var parameters = Parameters(("l", 5_000L),
			("d", 2_500.5d),
			("s", "30s"),
			("ms", "500ms"),
			("m", "2m"),
			("h", "1h"),
			("bad", "not-a-duration"));

		Assert.Multiple(() =>
		{
			Assert.That(HttpActionValues.ReadDurationMilliseconds(parameters, "l", 0), Is.EqualTo(5_000d));
			Assert.That(HttpActionValues.ReadDurationMilliseconds(parameters, "d", 0), Is.EqualTo(2_500.5d));
			Assert.That(HttpActionValues.ReadDurationMilliseconds(parameters, "s", 0), Is.EqualTo(30_000d));
			Assert.That(HttpActionValues.ReadDurationMilliseconds(parameters, "ms", 0), Is.EqualTo(500d));
			Assert.That(HttpActionValues.ReadDurationMilliseconds(parameters, "m", 0), Is.EqualTo(120_000d));
			Assert.That(HttpActionValues.ReadDurationMilliseconds(parameters, "h", 0), Is.EqualTo(3_600_000d));
			Assert.That(HttpActionValues.ReadDurationMilliseconds(parameters, "bad", 999), Is.EqualTo(999d));
			Assert.That(HttpActionValues.ReadDurationMilliseconds(parameters, "missing", 999), Is.EqualTo(999d));
		});
	}

	[Test]
	public void ReadBool_reads_both_the_toggle_and_its_rendered_text()
	{
		var parameters = Parameters(("toggle", true), ("text", "true"), ("blank", ""));

		Assert.Multiple(() =>
		{
			Assert.That(HttpActionValues.ReadBool(parameters, "toggle", false), Is.True);
			Assert.That(HttpActionValues.ReadBool(parameters, "text", false), Is.True);
			Assert.That(HttpActionValues.ReadBool(parameters, "blank", true), Is.True);
			Assert.That(HttpActionValues.ReadBool(parameters, "missing", true), Is.True);
		});
	}

	[Test]
	public void ReadKeyValue_reads_a_string_dictionary()
	{
		var parameters = Parameters(("kv", new Dictionary<string, string> { ["key"] = "value", [""] = "dropped" }));

		var map = HttpActionValues.ReadKeyValue(parameters, "kv");

		Assert.Multiple(() =>
		{
			Assert.That(map, Has.Count.EqualTo(1));
			Assert.That(map["key"], Is.EqualTo("value"));
		});
	}

	[Test]
	public void ReadKeyValue_reads_raw_json_text()
	{
		var parameters = Parameters(("kv", """{"a":"1","b":2}"""));

		var map = HttpActionValues.ReadKeyValue(parameters, "kv");

		Assert.Multiple(() =>
		{
			Assert.That(map["a"], Is.EqualTo("1"));
			Assert.That(map["b"], Is.EqualTo("2"));
		});
	}

	[Test]
	public void ReadKeyValue_answers_empty_for_a_bare_non_json_string()
	{
		var parameters = Parameters(("kv", "just some text"));

		Assert.That(HttpActionValues.ReadKeyValue(parameters, "kv"), Is.Empty);
	}

	[Test]
	public void ReadKeyValue_drops_blank_keys()
	{
		var parameters = Parameters(("kv", """{"":"dropped","kept":"1"}"""));

		var map = HttpActionValues.ReadKeyValue(parameters, "kv");

		Assert.Multiple(() =>
		{
			Assert.That(map, Has.Count.EqualTo(1));
			Assert.That(map["kept"], Is.EqualTo("1"));
		});
	}

	[Test]
	public void A_missing_key_and_a_null_value_return_the_default_without_throwing()
	{
		var parameters = new Dictionary<string, object> { ["present"] = null! };

		Assert.Multiple(() =>
		{
			Assert.That(HttpActionValues.ReadText(parameters, "present"), Is.Null);
			Assert.That(HttpActionValues.ReadText(parameters, "missing"), Is.Null);
			Assert.That(HttpActionValues.ReadNumber(parameters, "present", 3), Is.EqualTo(3d));
			Assert.That(HttpActionValues.ReadNumber(parameters, "missing", 3), Is.EqualTo(3d));
			Assert.That(HttpActionValues.ReadBool(parameters, "present", true), Is.True);
			Assert.That(HttpActionValues.ReadKeyValue(parameters, "missing"), Is.Empty);
		});
	}

	[Test]
	public void Numeric_parsing_is_invariant_and_survives_a_comma_decimal_culture()
	{
		var original = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = new CultureInfo("de-DE");

			var parameters = Parameters(("n", "41.6"));
			Assert.That(HttpActionValues.ReadNumber(parameters, "n", 0), Is.EqualTo(41.6d));
		}
		finally
		{
			CultureInfo.CurrentCulture = original;
		}
	}

	private static Dictionary<string, object> Parameters(params (string Name, object Value)[] values)
		=> values.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
}
