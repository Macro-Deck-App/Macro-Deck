using MacroDeckHost.Infrastructure.Adb;

namespace MacroDeckHost.Tests.UnitTests.Adb;

public class AdbShellQuoteTests
{
	[TestCase("hello", @"'hello'")]
	[TestCase("; rm -rf /", @"'; rm -rf /'")]
	[TestCase("`whoami`", @"'`whoami`'")]
	[TestCase("$(id)", @"'$(id)'")]
	[TestCase("a && b", @"'a && b'")]
	[TestCase("a | b", @"'a | b'")]
	[TestCase("it's", @"'it'\''s'")]
	[TestCase("com.foo'; pm uninstall x", @"'com.foo'\''; pm uninstall x'")]
	public void Quote_wraps_the_value_and_keeps_shell_metacharacters_as_literal_text(string value, string expected)
	{
		Assert.That(AdbShellQuote.Quote(value), Is.EqualTo(expected));
	}

	[Test]
	public void Quote_preserves_an_embedded_newline_as_literal_text()
	{
		Assert.That(AdbShellQuote.Quote("line1\nline2"), Is.EqualTo("'line1\nline2'"));
	}

	[Test]
	public void Quote_replaces_a_single_embedded_quote_with_the_POSIX_escape_sequence()
	{
		Assert.That(AdbShellQuote.Quote("'"), Is.EqualTo(@"''\'''"));
	}

	[Test]
	public void Quote_handles_multiple_embedded_quotes()
	{
		Assert.That(AdbShellQuote.Quote("''"), Is.EqualTo(@"''\'''\'''"));
	}

	[Test]
	public void A_plain_value_round_trips_between_the_quotes_unchanged()
	{
		const string value = "plain-value_123";

		Assert.That(AdbShellQuote.Quote(value), Is.EqualTo($"'{value}'"));
	}
}
