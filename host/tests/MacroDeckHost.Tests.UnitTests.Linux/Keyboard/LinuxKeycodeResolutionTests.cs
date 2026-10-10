using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Keyboard.Native;

namespace MacroDeckHost.Tests.UnitTests.Linux.Keyboard;

[Platform("Linux")]
[SupportedOSPlatform("linux")]
public class LinuxKeycodeResolutionTests
{
	private static uint GermanLayout(nuint keysym) => keysym switch
	{
		0x7A => 29,
		0x2D => 61,
		_ => 0
	};

	[TestCase(KeyCode.Minus, 20u)]
	[TestCase(KeyCode.BracketLeft, 34u)]
	[TestCase(KeyCode.IntlBackslash, 94u)]
	public void A_positional_key_presses_its_us_position_whatever_the_layout_types_there(KeyCode key, uint expected)
		=> Assert.That(LinuxKeyboardInputProvider.ResolveKeycode(key, GermanLayout), Is.EqualTo(expected));

	[Test]
	public void A_letter_presses_the_key_the_layout_labels_with_it()
		=> Assert.That(LinuxKeyboardInputProvider.ResolveKeycode(KeyCode.Z, GermanLayout), Is.EqualTo(29u));

	[Test]
	public void A_letter_the_layout_does_not_type_falls_back_to_its_us_position()
		=> Assert.That(LinuxKeyboardInputProvider.ResolveKeycode(KeyCode.Q, _ => 0), Is.EqualTo(24u));
}
