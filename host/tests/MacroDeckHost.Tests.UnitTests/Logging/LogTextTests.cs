using MacroDeckHost.Application.Logging;

namespace MacroDeckHost.Tests.UnitTests.Logging;

public class LogTextTests
{
	private static readonly char LineSeparator = (char)0x2028;

	private static readonly char ParagraphSeparator = (char)0x2029;

	private static readonly char Escape = (char)0x1B;

	private static readonly char C1Csi = (char)0x9B;

	[TestCase("a\nb")]
	[TestCase("a\r\nb")]
	[TestCase("a\rb")]
	[TestCase("a\n\n[ERR] [Host] forged")]
	public void Line_breaks_never_survive(string input)
	{
		var result = LogText.Neutralize(input)!;

		Assert.That(result.IndexOfAny(['\r', '\n']), Is.EqualTo(-1));
	}

	[Test]
	public void Terminal_escapes_and_other_controls_never_survive()
	{
		var input = $"a{Escape}[31mb\0c{(char)0x7F}d{C1Csi}e{LineSeparator}f{ParagraphSeparator}g";

		var result = LogText.Neutralize(input)!;

		Assert.Multiple(() =>
		{
			Assert.That(result.Any(c => char.IsControl(c) || c == LineSeparator || c == ParagraphSeparator),
				Is.False);
			Assert.That(result, Does.Contain("[31m").And.Contain("a").And.Contain("g"));
		});
	}

	[Test]
	public void Readable_text_is_returned_unchanged_and_as_the_same_instance()
	{
		var input = $"Grüße 🎛 tab\t and a bidi mark {(char)0x202E} kept";

		Assert.That(LogText.Neutralize(input), Is.SameAs(input));
	}

	[Test]
	public void Null_stays_null()
	{
		Assert.Multiple(() =>
		{
			Assert.That(LogText.Neutralize(null), Is.Null);
			Assert.That(LogText.NeutralizeControls(null), Is.Null);
		});
	}

	[Test]
	public void Exception_text_keeps_its_line_breaks_but_loses_escapes_and_stray_carriage_returns()
	{
		var input = $"first\r\nsecond\n\tthird{Escape}[31m stray\rcursor";

		var result = LogText.NeutralizeControls(input)!;

		Assert.Multiple(() =>
		{
			Assert.That(result, Does.Contain("first\r\nsecond\n\tthird"));
			Assert.That(result.Contains(Escape), Is.False);
			Assert.That(result.Replace("\r\n", string.Empty, StringComparison.Ordinal).Contains('\r'), Is.False);
		});
	}
}
