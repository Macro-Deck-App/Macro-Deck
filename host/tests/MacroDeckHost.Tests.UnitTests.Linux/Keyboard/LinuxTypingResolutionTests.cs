using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Keyboard.Native;

namespace MacroDeckHost.Tests.UnitTests.Linux.Keyboard;

[Platform("Linux")]
[SupportedOSPlatform("linux")]
public class LinuxTypingResolutionTests
{
	private const uint Shift = 1 << 0;
	private const uint Level3 = 1 << 7;
	private const uint SecondGroup = 1 << 13;

	private static readonly Dictionary<uint, nuint[]> _germanKeys = new()
	{
		[16] = [0x37, 0x2F, 0x7B],
		[17] = [0x38, 0x28, 0x5B],
		[38] = [0x61, 0x41]
	};

	private static nuint GermanLayout(uint keycode, uint state)
	{
		if ((state & SecondGroup) != 0)
		{
			return keycode == 38 ? (nuint)0x6C6 : 0;
		}

		var level = ((state & Level3) != 0 ? 2 : 0) + ((state & Shift) != 0 ? 1 : 0);
		return _germanKeys.TryGetValue(keycode, out var levels) && level < levels.Length ? levels[level] : 0;
	}

	private static bool Resolve(nuint keysym, uint level3Mask, uint group, out LinuxKeyboardInputProvider.TypingKey key)
	{
		return LinuxKeyboardInputProvider.TryResolveTyping(keysym,
			0,
			8,
			255,
			Shift,
			level3Mask,
			group,
			GermanLayout,
			out key);
	}

	[TestCase(0x5Bu, 17u, Level3)]
	[TestCase(0x7Bu, 16u, Level3)]
	[TestCase(0x41u, 38u, Shift)]
	[TestCase(0x28u, 17u, Shift)]
	[TestCase(0x61u, 38u, 0u)]
	public void A_character_presses_its_key_with_the_modifiers_its_level_needs(
		uint keysym,
		uint expectedKeycode,
		uint expectedModifiers)
	{
		Assert.That(Resolve(keysym, Level3, 0, out var key), Is.True);
		Assert.That(key, Is.EqualTo(new LinuxKeyboardInputProvider.TypingKey(expectedKeycode, expectedModifiers)));
	}

	[Test]
	public void An_altgr_character_is_not_typed_from_its_base_key_when_the_layout_has_no_altgr()
	{
		Assert.That(Resolve(0x5B, 0, 0, out _), Is.False);
	}

	[Test]
	public void A_character_the_layout_cannot_type_is_left_to_the_fallback()
	{
		Assert.That(Resolve(0x010020AC, Level3, 0, out _), Is.False);
	}

	[Test]
	public void A_character_is_looked_up_in_the_active_layout_group()
	{
		Assert.That(Resolve(0x6C6, Level3, 1, out var key), Is.True);
		Assert.That(key, Is.EqualTo(new LinuxKeyboardInputProvider.TypingKey(38, 0)));
	}

	[Test]
	public void Line_breaks_are_typed_as_return_and_tabs_as_tab()
	{
		const nuint returnKey = 0xFF0D;
		const nuint tab = 0xFF09;

		Assert.That(LinuxKeyboardInputProvider.ToKeysyms("a\r\nb\nc\rd\te"),
			Is.EqualTo(new nuint[] { 0x61, returnKey, 0x62, returnKey, 0x63, returnKey, 0x64, tab, 0x65 }));
	}

	[Test]
	public void Characters_outside_latin1_use_their_unicode_keysym()
	{
		Assert.That(LinuxKeyboardInputProvider.ToKeysyms("ä€"), Is.EqualTo(new nuint[] { 0xE4, 0x010020AC }));
	}

	[Test]
	public void The_layout_never_resolves_through_the_spare_key()
	{
		const nuint emoji = 0x0101F600;

		var resolved = LinuxKeyboardInputProvider.TryResolveOutsideSpare(emoji,
			255,
			8,
			255,
			Shift,
			0,
			0,
			(keycode, _) => keycode == 255 ? emoji : 0,
			out _);

		Assert.That(resolved, Is.False);
	}

	[TestCase("😀😀", "map:1F600 key key final map:0")]
	[TestCase("😀🎉😀", "map:1F600 key settle map:1F389 key settle map:1F600 key final map:0")]
	[TestCase("a😀b", "layout map:1F600 key layout final map:0")]
	[TestCase("😀a😀", "map:1F600 key layout key final map:0")]
	[TestCase("abc", "layout layout layout")]
	public void Out_of_layout_characters_keep_the_spare_mapping_until_they_are_read(string text, string expected)
	{
		Assert.That(Type(text, 255), Is.EqualTo(expected));
	}

	[Test]
	public void The_mapping_is_cleared_when_sending_fails()
	{
		var log = new List<string>();
		var spare = Spare(255, log);

		Assert.Throws<InvalidOperationException>(() => LinuxKeyboardInputProvider.TypeKeysyms(
			LinuxKeyboardInputProvider.ToKeysyms("😀"),
			spare,
			_ => null,
			_ =>
			{
			},
			_ => throw new InvalidOperationException()));

		Assert.That(string.Join(' ', log), Is.EqualTo("map:1F600 final map:0"));
	}

	[Test]
	public void No_spare_keycode_skips_the_character_without_touching_the_keymap()
	{
		Assert.That(Type("a😀", 0), Is.EqualTo("layout"));
	}

	private static string Type(string text, int spareKeycode)
	{
		var log = new List<string>();
		LinuxKeyboardInputProvider.TypeKeysyms(LinuxKeyboardInputProvider.ToKeysyms(text),
			Spare(spareKeycode, log),
			keysym => keysym < 0x100 ? new LinuxKeyboardInputProvider.TypingKey(38, 0) : null,
			_ => log.Add("layout"),
			_ => log.Add("key"));

		return string.Join(' ', log);
	}

	private static LinuxKeyboardInputProvider.SpareKeycode Spare(int keycode, List<string> log)
	{
		return new LinuxKeyboardInputProvider.SpareKeycode(keycode,
			keysym => log.Add(keysym == 0 ? "map:0" : $"map:{keysym - 0x01000000:X}"),
			clearing => log.Add(clearing ? "final" : "settle"));
	}
}
