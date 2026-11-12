using MacroDeckHost.Integrations.Keyboard.Native;

namespace MacroDeckHost.Tests.UnitTests.Keyboard;

public class TypedLineBreaksTests
{
	private const string Enter = "<enter>";

	[TestCase("one\ntwo", new[] { "one", Enter, "two" })]
	[TestCase("one\r\ntwo", new[] { "one", Enter, "two" })]
	[TestCase("one\rtwo", new[] { "one", Enter, "two" })]
	[TestCase("a\n\nb", new[] { "a", Enter, Enter, "b" })]
	[TestCase("\r\n\r\n", new[] { Enter, Enter })]
	[TestCase("\n\r", new[] { Enter, Enter })]
	[TestCase("\nhi\n", new[] { Enter, "hi", Enter })]
	[TestCase("tab\tand 😀", new[] { "tab\tand 😀" })]
	public void Each_line_break_becomes_one_enter_between_the_text_around_it(string text, string[] expected)
	{
		var typed = new List<string>();

		TypedLineBreaks.Walk(text, typed.Add, () => typed.Add(Enter));

		Assert.That(typed, Is.EqualTo(expected));
	}

	[Test]
	public void Empty_text_types_nothing()
	{
		var typed = new List<string>();

		TypedLineBreaks.Walk(string.Empty, typed.Add, () => typed.Add(Enter));

		Assert.That(typed, Is.Empty);
	}
}
