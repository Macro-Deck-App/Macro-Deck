using MacroDeckHost.Integrations.Steam.Native;

namespace MacroDeckHost.Tests.UnitTests.Steam;

[TestFixture]
internal sealed class ValveKeyValuesTests
{
	[Test]
	public void Nested_blocks_are_readable_by_path()
	{
		var root = ValveKeyValues.TryParse("""
										   "Registry"
										   {
										   	"HKCU"
										   	{
										   		"Software" { "Valve" { "Steam" { "RunningAppID" "730" } } }
										   	}
										   }
										   """);

		Assert.That(root?.Find("Registry", "HKCU", "Software", "Valve", "Steam", "RunningAppID")?.Value,
			Is.EqualTo("730"));
	}

	[Test]
	public void Escaped_backslashes_in_windows_paths_are_unescaped()
	{
		var root = ValveKeyValues.TryParse("\"path\"\t\t\"D:\\\\Games\\\\Steam Library\"");

		Assert.That(root?["path"]?.Value, Is.EqualTo(@"D:\Games\Steam Library"));
	}

	[Test]
	public void An_escaped_quote_does_not_end_the_value()
	{
		var root = ValveKeyValues.TryParse("\"name\" \"The \\\"Game\\\"\"");

		Assert.That(root?["name"]?.Value, Is.EqualTo("The \"Game\""));
	}

	[Test]
	public void A_byte_order_mark_and_comments_are_ignored()
	{
		var root = ValveKeyValues.TryParse("\uFEFF// a comment\n\"a\" \"b\" // trailing\n");

		Assert.That(root?["a"]?.Value, Is.EqualTo("b"));
	}

	[Test]
	public void Unicode_text_is_preserved()
	{
		var root = ValveKeyValues.TryParse("\"name\" \"Pokémon 宝可梦 Ёж\"");

		Assert.That(root?["name"]?.Value, Is.EqualTo("Pokémon 宝可梦 Ёж"));
	}

	[Test]
	public void Keys_are_case_insensitive()
	{
		var root = ValveKeyValues.TryParse("\"AppState\" { \"AppID\" \"1\" }");

		Assert.That(root?["appstate"]?["appid"]?.Value, Is.EqualTo("1"));
	}

	[TestCase("")]
	[TestCase("\"a\" {")]
	[TestCase("\"a\" \"unterminated")]
	[TestCase("}")]
	[TestCase("\"a\" { \"b\" }")]
	public void Malformed_input_yields_null_instead_of_throwing(string text)
	{
		Assert.That(ValveKeyValues.TryParse(text), Is.Null);
	}

	[Test]
	public void Absurdly_deep_nesting_is_rejected()
	{
		var text = string.Concat(Enumerable.Repeat("\"a\" {", 500)) + string.Concat(Enumerable.Repeat("}", 500));

		Assert.That(ValveKeyValues.TryParse(text), Is.Null);
	}
}
