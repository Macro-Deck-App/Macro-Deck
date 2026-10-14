using MacroDeck.Sdk.Variables;

namespace MacroDeck.Sdk.Tests.UnitTests.Variables;

[TestFixture]
public class VariableNamesTests
{
	[TestCase("obs_streaming", true)]
	[TestCase("a", true)]
	[TestCase("a1_b2", true)]
	[TestCase("", false)]
	[TestCase(null, false)]
	[TestCase("1abc", false)]
	[TestCase("_abc", false)]
	[TestCase("Abc", false)]
	[TestCase("a-b", false)]
	[TestCase("vars.abc", false)]
	[TestCase("abc\n", false)]
	public void A_canonical_name_is_lowercase_ascii_starting_with_a_letter(string? name, bool valid)
	{
		Assert.That(VariableNames.IsValid(name), Is.EqualTo(valid));
	}

	[TestCase("Gaming PC", "gaming_pc")]
	[TestCase("vars.Stream Title", "stream_title")]
	[TestCase("Café Déjà-vu", "cafe_deja_vu")]
	[TestCase("  __a!!b__  ", "a_b")]
	[TestCase("2nd Monitor", "v_2nd_monitor")]
	[TestCase("!!!", "")]
	[TestCase("   ", "")]
	[TestCase(null, "")]
	public void Sanitizing_turns_text_into_a_canonical_name(string? input, string expected)
	{
		Assert.That(VariableNames.Sanitize(input), Is.EqualTo(expected));
	}

	[TestCase("Gaming PC")]
	[TestCase("Café")]
	[TestCase("2nd Monitor")]
	public void A_sanitized_name_is_valid(string input)
	{
		Assert.That(VariableNames.IsValid(VariableNames.Sanitize(input)), Is.True);
	}
}
