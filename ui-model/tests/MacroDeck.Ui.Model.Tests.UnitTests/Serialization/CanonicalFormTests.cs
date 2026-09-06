using System.Text;
using System.Text.Json;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Serialization;

namespace MacroDeck.Ui.Model.Tests.UnitTests.Serialization;

/// <summary>
/// Canonical determinism: object member order follows declaration, map keys sort ordinal-ascending,
/// arrays never reorder, numbers are producer-verbatim, and the form is deterministic without being a
/// semantic digest.
/// </summary>
[TestFixture]
public class CanonicalFormTests
{
	private static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement.Clone();

	[Test]
	public void Property_map_keys_are_ordered_ordinal_ascending_regardless_of_insertion_order()
	{
		var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			["b"] = Element("1"), ["A"] = Element("2"), ["a"] = Element("3"), ["B"] = Element("4"),
			["_x"] = Element("5"), ["10"] = Element("6"), ["2"] = Element("7"),
		};
		var node = new UiNode { Id = "root", Type = "t", Properties = properties };

		Assert.That(UiCanonicalJson.Serialize(node),
			Is.EqualTo(
				"""{"id":"root","type":"t","properties":{"10":6,"2":7,"A":2,"B":4,"_x":5,"a":3,"b":1},"children":[]}"""));
	}

	[Test]
	public void Serialization_is_deterministic_across_differently_built_equal_graphs()
	{
		var first = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			["b"] = Element("1"), ["a"] = Element("2"),
		};
		var second = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			["a"] = Element("2"), ["b"] = Element("1"),
		};

		var nodeA = new UiNode { Id = "root", Type = "t", Properties = first };
		var nodeB = new UiNode { Id = "root", Type = "t", Properties = second };

		Assert.That(UiCanonicalJson.Serialize(nodeA), Is.EqualTo(UiCanonicalJson.Serialize(nodeB)));
	}

	[Test]
	public void Arrays_are_never_reordered()
	{
		var node = new UiNode
		{
			Id = "root",
			Type = "t",
			Children =
			[
				new UiNode { Id = "z", Type = "t" }, new UiNode { Id = "a", Type = "t" },
				new UiNode { Id = "m", Type = "t" },
			],
		};

		var deserialized
			= JsonSerializer.Deserialize<UiNode>(UiCanonicalJson.Serialize(node), UiCanonicalJson.Options)!;
		var expectedOrder = new[] { "z", "a", "m" };

		Assert.That(deserialized.Children.Select(child => child.Id), Is.EqualTo(expectedOrder));
	}

	[Test]
	public void Numbers_are_written_exactly_as_the_producer_wrote_them()
	{
		var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			["a"] = Element("1.50"), ["b"] = Element("1e3"), ["c"] = Element("-0.0"),
			["d"] = Element("1.0"), ["e"] = Element("10000000000000000000000"),
		};
		var node = new UiNode { Id = "root", Type = "t", Properties = properties };

		Assert.That(UiCanonicalJson.Serialize(node),
			Is.EqualTo(
				"""{"id":"root","type":"t","properties":{"a":1.50,"b":1e3,"c":-0.0,"d":1.0,"e":10000000000000000000000},"children":[]}"""));
	}

	[Test]
	public void The_canonical_form_is_not_a_content_address()
	{
		var spelledOneFifty = new UiNode
		{
			Id = "root", Type = "t",
			Properties = new Dictionary<string, JsonElement> { ["n"] = Element("1.50") },
		};
		var spelledOneFive = new UiNode
		{
			Id = "root", Type = "t",
			Properties = new Dictionary<string, JsonElement> { ["n"] = Element("1.5") },
		};

		var xThenY = new UiNode
		{
			Id = "root", Type = "t",
			Properties = new Dictionary<string, JsonElement> { ["v"] = Element("""{"x":1,"y":2}""") },
		};
		var yThenX = new UiNode
		{
			Id = "root", Type = "t",
			Properties = new Dictionary<string, JsonElement> { ["v"] = Element("""{"y":2,"x":1}""") },
		};

		Assert.Multiple(() =>
		{
			Assert.That(UiCanonicalJson.Serialize(spelledOneFifty),
				Is.Not.EqualTo(UiCanonicalJson.Serialize(spelledOneFive)));
			Assert.That(UiCanonicalJson.Serialize(xThenY), Is.Not.EqualTo(UiCanonicalJson.Serialize(yThenX)));
		});
	}

	[Test]
	public void Output_is_ascii_only_and_uses_the_default_escaping()
	{
		var node = new UiNode
		{
			Id = "root", Type = "t",
			Properties = new Dictionary<string, JsonElement>
			{
				["text"] = Element("\"<>&'+`é😀\""),
			},
		};

		var json = UiCanonicalJson.Serialize(node);

		Assert.Multiple(() =>
		{
			Assert.That(json.All(character => character < 0x80), Is.True);
			Assert.That(json, Does.Contain("\\u003C"));
			Assert.That(json, Does.Contain("\\u003E"));
			Assert.That(json, Does.Contain("\\u0026"));
			Assert.That(json, Does.Contain("\\u0027"));
			Assert.That(json, Does.Contain("\\u002B"));
			Assert.That(json, Does.Contain("\\u0060"));
			Assert.That(json, Does.Contain("\\u00E9"));
			Assert.That(json, Does.Contain("\\uD83D\\uDE00"));
		});
	}

	[Test]
	public void The_exposed_options_produce_the_same_bytes_as_the_helper()
	{
		foreach (var value in ThreeFixtureValues())
		{
			Assert.That(JsonSerializer.Serialize(value, UiCanonicalJson.Options),
				Is.EqualTo(UiCanonicalJson.Serialize(value)));
		}
	}

	[Test]
	public void Canonical_options_are_read_only() => Assert.That(UiCanonicalJson.Options.IsReadOnly, Is.True);

	[Test]
	public void Serialize_and_SerializeToUtf8Bytes_agree()
	{
		foreach (var value in ThreeFixtureValues())
		{
			var text = UiCanonicalJson.Serialize(value);
			var bytes = UiCanonicalJson.SerializeToUtf8Bytes(value);

			Assert.Multiple(() =>
			{
				Assert.That(Encoding.UTF8.GetString(bytes), Is.EqualTo(text));
				Assert.That(bytes[0], Is.EqualTo((byte)'{'), "No byte order mark.");
			});
		}
	}

	[Test]
	public void ToElement_composes_without_changing_bytes()
	{
		var inner = new UiNode { Id = "inner", Type = "t" };
		var innerBytes = UiCanonicalJson.Serialize(inner);

		var outer = new UiNode
		{
			Id = "outer", Type = "t",
			Properties = new Dictionary<string, JsonElement> { ["child"] = UiCanonicalJson.ToElement(inner) },
		};

		var outerJson = UiCanonicalJson.Serialize(outer);

		Assert.That(outerJson, Does.Contain($"\"child\":{innerBytes}"));
	}

	[Test]
	public void Canonical_serialization_is_idempotent()
	{
		foreach (var value in ThreeFixtureValues())
		{
			var once = UiCanonicalJson.Serialize(value);
			var deserialized = JsonSerializer.Deserialize<UiNode>(once, UiCanonicalJson.Options)!;
			var twice = UiCanonicalJson.Serialize(deserialized);

			Assert.That(twice, Is.EqualTo(once));
		}
	}

	[Test]
	public void Non_canonical_input_is_normalized_on_the_first_pass()
	{
		var noisy = ReadFixtureRaw("unknown-members-input");
		var expected = GoldenSnapshotTests.ReadFixture("node-minimal");

		var deserialized = JsonSerializer.Deserialize<UiNode>(noisy, UiCanonicalJson.Options)!;

		Assert.That(UiCanonicalJson.Serialize(deserialized), Is.EqualTo(expected));
	}

	[Test]
	public void No_whitespace_outside_string_literals()
	{
		var node = new UiNode
		{
			Id = "root", Type = "t",
			Properties = new Dictionary<string, JsonElement> { ["text"] = Element("\"has spaces  and\\ttabs\"") },
		};

		var json = UiCanonicalJson.Serialize(node);
		var withoutStrings = System.Text.RegularExpressions.Regex.Replace(json, "\"(?:[^\"\\\\]|\\\\.)*\"", "\"\"");

		Assert.That(withoutStrings, Does.Not.Contain(' ').And.Not.Contain('\t').And.Not.Contain('\n'));
	}

	private static IEnumerable<UiNode> ThreeFixtureValues()
	{
		yield return new UiNode { Id = "root", Type = "stack" };
		yield return new UiNode
		{
			Id = "root", Type = "t",
			Properties = new Dictionary<string, JsonElement> { ["a"] = Element("1") },
		};
		yield return new UiNode
		{
			Id = "root", Type = "t",
			Children = [new UiNode { Id = "child", Type = "t" }],
		};
	}

	private static string ReadFixtureRaw(string name)
	{
		var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", $"{name}.json");
		return File.ReadAllText(path).TrimEnd('\n');
	}
}
