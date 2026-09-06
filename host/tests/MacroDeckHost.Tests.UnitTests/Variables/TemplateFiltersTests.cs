using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class TemplateFiltersTests
{
	[Test]
	public void Issue_290_styled_discord_name_folds_to_plain_letters()
	{
		var styled
			= "\U0001D49E\U0001D4A5\U0001D43B\U0001D49C\U0001D49E\U0001D4A6\U0001D438\U0001D445\U0001D4B4\U0001D4AF";

		Assert.That(TemplateFilters.PlainText(styled), Is.EqualTo("CJHACKERYT"));
	}

	[TestCase("\u212C\u2130\u210B\u211B\u2102\u210D", "BEHRCH")]
	[TestCase("\uFF21\uFF22\uFF23", "ABC")]
	[TestCase("\u24B6\u2460", "A1")]
	[TestCase("\uFB01\u00B2", "fi2")]
	public void Compatibility_characters_fold_to_their_plain_form(string input, string expected)
	{
		Assert.That(TemplateFilters.PlainText(input), Is.EqualTo(expected));
	}

	[TestCase("Server: Main")]
	[TestCase("caf\u00E9")] // diacritics (U+00E9) are preserved, not stripped
	[TestCase("\u041F\u0440\u0438\u0432\u0435\u0442 \u03B1\u03B2\u03B3 \u65E5\u672C\u8A9E")] // Cyrillic, Greek, CJK
	public void Ordinary_text_is_unchanged(string input)
	{
		Assert.That(TemplateFilters.PlainText(input), Is.EqualTo(input));
	}

	[Test]
	public void Emoji_and_box_drawing_have_no_compatibility_mapping_and_are_left_alone()
	{
		// Emoji (U+1F600) and a box-drawing character (U+250A) have no NFKC compatibility mapping.
		var input = "\U0001F600\u250A";

		Assert.That(TemplateFilters.PlainText(input), Is.EqualTo(input));
	}

	[Test]
	public void No_break_space_folds_to_a_regular_space()
	{
		// Intentional side effect of NFKC folding - not something this filter special-cases.
		// Input has a no-break space (U+00A0) between "a" and "b"; output has a regular space (U+0020).
		Assert.That(TemplateFilters.PlainText("a\u00A0b"), Is.EqualTo("a b"));
	}

	[TestCase(null)]
	[TestCase("")]
	public void Null_or_empty_returns_empty(string? input)
	{
		Assert.That(TemplateFilters.PlainText(input), Is.EqualTo(string.Empty));
	}

	[Test]
	public void Lone_surrogate_is_left_unchanged_and_does_not_throw()
	{
		var input = "a\uD83Db";

		string? result = null;
		Assert.DoesNotThrow(() => result = TemplateFilters.PlainText(input));
		Assert.That(result, Is.EqualTo(input));
	}

	[Test]
	public void Unavailable_placeholder_is_unaffected()
	{
		Assert.That(TemplateFilters.PlainText(VariableTemplateRenderer.UnavailablePlaceholder),
			Is.EqualTo(VariableTemplateRenderer.UnavailablePlaceholder));
	}
}
