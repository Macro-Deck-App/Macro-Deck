using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Keyboard.Native;

namespace MacroDeckHost.Tests.UnitTests.Linux.Keyboard;

[Platform("Linux")]
[SupportedOSPlatform("linux")]
public class LinuxKeycodeResolutionTests
{
	private const uint Alt = 1 << 3;
	private const uint Level3 = 1 << 7;
	private const nuint AltR = 0xFFEA;
	private const nuint IsoLevel3Shift = 0xFE03;
	private const nuint MultiKey = 0xFF20;

	private static uint GermanLayout(nuint keysym)
	{
		return keysym switch
		{
			0x7A => 29,
			0x2D => 61,
			0xFFE9 => 64,
			_ => 0
		};
	}

	[TestCase(KeyCode.Minus, 20u)]
	[TestCase(KeyCode.BracketLeft, 34u)]
	[TestCase(KeyCode.IntlBackslash, 94u)]
	public void A_positional_key_presses_its_us_position_whatever_the_layout_types_there(KeyCode key, uint expected)
	{
		Assert.That(LinuxKeyboardInputProvider.ResolveKeycode(key, GermanLayout), Is.EqualTo(expected));
	}

	[Test]
	public void A_letter_presses_the_key_the_layout_labels_with_it()
	{
		Assert.That(LinuxKeyboardInputProvider.ResolveKeycode(KeyCode.Z, GermanLayout), Is.EqualTo(29u));
	}

	[Test]
	public void A_letter_the_layout_does_not_type_falls_back_to_its_us_position()
	{
		Assert.That(LinuxKeyboardInputProvider.ResolveKeycode(KeyCode.Q, _ => 0), Is.EqualTo(24u));
	}

	[Test]
	public void Right_alt_presses_the_right_alt_key_where_the_layout_makes_it_altgr()
	{
		Assert.That(LinuxKeyboardInputProvider.ResolveKeycode(KeyCode.RightAlt, GermanLayout), Is.EqualTo(108u));
	}

	[Test]
	public void Right_alt_presses_the_key_the_layout_labels_alt_r()
	{
		Assert.That(LinuxKeyboardInputProvider.ResolveKeycode(KeyCode.RightAlt, keysym => keysym == AltR ? 113u : 0),
			Is.EqualTo(113u));
	}

	[TestCase(KeyCode.LeftControl, 37u)]
	[TestCase(KeyCode.LeftShift, 50u)]
	[TestCase(KeyCode.RightShift, 62u)]
	[TestCase(KeyCode.LeftAlt, 64u)]
	[TestCase(KeyCode.RightControl, 105u)]
	[TestCase(KeyCode.RightAlt, 108u)]
	[TestCase(KeyCode.LeftMeta, 133u)]
	[TestCase(KeyCode.RightMeta, 134u)]
	public void A_modifier_the_layout_does_not_label_presses_its_physical_key(KeyCode key, uint expected)
	{
		Assert.That(LinuxKeyboardInputProvider.ResolveKeycode(key, _ => 0), Is.EqualTo(expected));
	}

	[Test]
	public void A_modifier_the_layout_labels_presses_the_key_it_labels()
	{
		Assert.That(LinuxKeyboardInputProvider.ResolveKeycode(KeyCode.LeftAlt, GermanLayout), Is.EqualTo(64u));
	}

	[Test]
	public void A_background_right_alt_reports_altgr_where_the_layout_makes_it_altgr()
	{
		Assert.That(StateMaskOf(KeyCode.RightAlt, IsoLevel3Shift), Is.EqualTo(Level3));
	}

	[Test]
	public void A_background_right_alt_reports_alt_where_the_layout_keeps_it_alt()
	{
		Assert.That(StateMaskOf(KeyCode.RightAlt, AltR), Is.EqualTo(Alt));
	}

	[Test]
	public void A_background_right_alt_reports_no_modifier_where_its_key_sets_none()
	{
		Assert.That(StateMaskOf(KeyCode.RightAlt, MultiKey), Is.Zero);
	}

	[Test]
	public void A_background_modifier_the_layout_cannot_look_up_reports_its_usual_modifier()
	{
		var state = LinuxKeyboardInputProvider.ResolveStateMask(KeyCode.RightAlt, 108, _ => 0, _ => 0);

		Assert.That(state, Is.EqualTo(Alt));
	}

	[Test]
	public void A_background_target_accepts_every_right_side_modifier()
	{
		using var provider = new LinuxKeyboardInputProvider();
		const KeyModifier rightSide = KeyModifier.RightControl
			| KeyModifier.RightShift
			| KeyModifier.RightAlt
			| KeyModifier.RightMeta;

		Assert.That(provider.BackgroundModifiers & rightSide, Is.EqualTo(rightSide));
	}

	private static uint StateMaskOf(KeyCode key, nuint rightAltKeysym)
	{
		return LinuxKeyboardInputProvider.ResolveStateMask(key,
			108,
			keycode => keycode == 108 ? rightAltKeysym : 0,
			keysym => keysym switch
			{
				AltR => Alt,
				IsoLevel3Shift => Level3,
				_ => 0
			});
	}
}
