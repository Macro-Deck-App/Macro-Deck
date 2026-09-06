using MacroDeckHost.Integrations.Http.JsonPath;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.Http;

[TestFixture]
internal sealed class JsonPathExtractorTests
{
	private const string Document = """
									{
										"data": { "items": [ { "name": "first" }, { "name": "second" } ] },
										"a": { "b": [ [ "x", "y", "deep" ] ] },
										"number": 7,
										"decimal": 1.5,
										"flagTrue": true,
										"flagFalse": false,
										"text": "hello",
										"nothing": null,
										"nested": { "x": 1 },
										"list": [1, 2, 3]
									}
									""";

	[Test]
	public void A_dotted_path_with_an_array_index_resolves()
	{
		Assert.That(JsonPathExtractor.TryExtract(Document, "data.items[0].name", out var type, out var value),
			Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(type, Is.EqualTo(VariableType.Text));
			Assert.That(value, Is.EqualTo("first"));
		});
	}

	[Test]
	public void A_leading_index_with_no_property_name_resolves()
	{
		Assert.That(JsonPathExtractor.TryExtract("""[ { "id": 1 }, { "id": 2 } ]""",
				"[0].id",
				out var type,
				out var value),
			Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(type, Is.EqualTo(VariableType.Numeric));
			Assert.That(value, Is.EqualTo(1d));
		});
	}

	[Test]
	public void Chained_indices_resolve()
	{
		Assert.That(JsonPathExtractor.TryExtract(Document, "a.b[0][2]", out var type, out var value), Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(type, Is.EqualTo(VariableType.Text));
			Assert.That(value, Is.EqualTo("deep"));
		});
	}

	[Test]
	public void A_leading_dollar_dot_is_stripped()
	{
		Assert.That(JsonPathExtractor.TryExtract(Document, "$.text", out _, out var value), Is.True);
		Assert.That(value, Is.EqualTo("hello"));
	}

	[Test]
	public void A_bare_dollar_addresses_the_whole_document()
	{
		Assert.That(JsonPathExtractor.TryExtract("""{"a":1}""", "$", out var type, out var value), Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(type, Is.EqualTo(VariableType.Text));
			Assert.That(value, Is.EqualTo("""{"a":1}"""));
		});
	}

	[TestCase("number", VariableType.Numeric, 7d)]
	[TestCase("decimal", VariableType.Numeric, 1.5d)]
	[TestCase("flagTrue", VariableType.Boolean, true)]
	[TestCase("flagFalse", VariableType.Boolean, false)]
	[TestCase("text", VariableType.Text, "hello")]
	public void Every_json_value_kind_maps_to_the_expected_variable_type(
		string path,
		VariableType expectedType,
		object expectedValue)
	{
		Assert.That(JsonPathExtractor.TryExtract(Document, path, out var type, out var value), Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(type, Is.EqualTo(expectedType));
			Assert.That(value, Is.EqualTo(expectedValue));
		});
	}

	[Test]
	public void A_null_value_maps_to_empty_text()
	{
		Assert.That(JsonPathExtractor.TryExtract(Document, "nothing", out var type, out var value), Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(type, Is.EqualTo(VariableType.Text));
			Assert.That(value, Is.EqualTo(string.Empty));
		});
	}

	[Test]
	public void An_object_value_maps_to_text_with_its_compact_raw_json()
	{
		Assert.That(JsonPathExtractor.TryExtract(Document, "nested", out var type, out var value), Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(type, Is.EqualTo(VariableType.Text));
			Assert.That(value, Is.EqualTo("""{ "x": 1 }"""));
		});
	}

	[Test]
	public void An_array_value_maps_to_text_with_its_compact_raw_json()
	{
		Assert.That(JsonPathExtractor.TryExtract(Document, "list", out var type, out var value), Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(type, Is.EqualTo(VariableType.Text));
			Assert.That(value, Is.EqualTo("[1, 2, 3]"));
		});
	}

	[Test]
	public void A_missing_property_is_a_miss()
	{
		Assert.That(JsonPathExtractor.TryExtract(Document, "data.missing", out _, out _), Is.False);
	}

	[Test]
	public void An_out_of_range_index_is_a_miss()
	{
		Assert.That(JsonPathExtractor.TryExtract(Document, "data.items[99]", out _, out _), Is.False);
	}

	[Test]
	public void Indexing_an_object_is_a_miss()
	{
		Assert.That(JsonPathExtractor.TryExtract(Document, "data[0]", out _, out _), Is.False);
	}

	[Test]
	public void A_property_lookup_on_an_array_is_a_miss()
	{
		Assert.That(JsonPathExtractor.TryExtract(Document, "list.name", out _, out _), Is.False);
	}

	[Test]
	public void The_lookup_is_case_sensitive()
	{
		Assert.That(JsonPathExtractor.TryExtract(Document, "Text", out _, out _), Is.False);
	}

	[Test]
	public void A_property_name_containing_a_dot_is_not_addressable()
	{
		Assert.That(JsonPathExtractor.TryExtract("""{"a.b":"dotted-name"}""", "a.b", out _, out _), Is.False);
	}

	[Test]
	public void An_invalid_json_document_is_a_miss()
	{
		Assert.That(JsonPathExtractor.TryExtract("not json", "$", out _, out _), Is.False);
	}
}
