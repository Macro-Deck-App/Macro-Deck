using MacroDeck.Sdk.Input;

namespace MacroDeck.Sdk.Tests.UnitTests.Input;

[TestFixture]
public class NativeKeyPositionTests
{
	[TestCase(KeyCode.A, 0x1E, 0)]
	[TestCase(KeyCode.Z, 0x2C, 6)]
	[TestCase(KeyCode.D1, 0x02, 18)]
	[TestCase(KeyCode.D0, 0x0B, 29)]
	[TestCase(KeyCode.Semicolon, 0x27, 41)]
	[TestCase(KeyCode.Backquote, 0x29, 50)]
	[TestCase(KeyCode.IntlBackslash, 0x56, 10)]
	public void A_key_of_the_main_block_has_its_us_position_on_each_platform(KeyCode key, int scanCode, int macKeyCode)
	{
		Assert.Multiple(() =>
		{
			Assert.That(NativeKeys.TryGetWindowsScanCode(key, out var scan), Is.True);
			Assert.That(scan, Is.EqualTo(scanCode));
			Assert.That(NativeKeys.TryGetMacOsKeyCode(key, out var mac), Is.True);
			Assert.That(mac, Is.EqualTo(macKeyCode));
			Assert.That(NativeKeys.TryFromWindowsScanCode((ushort)scanCode, out var back), Is.True);
			Assert.That(back, Is.EqualTo(key));
		});
	}

	[TestCase(KeyCode.F1)]
	[TestCase(KeyCode.Enter)]
	[TestCase(KeyCode.MediaPlayPause)]
	[TestCase(KeyCode.None)]
	public void A_key_outside_the_main_block_has_no_position(KeyCode key)
	{
		Assert.Multiple(() =>
		{
			Assert.That(NativeKeys.TryGetWindowsScanCode(key, out _), Is.False);
			Assert.That(NativeKeys.TryGetMacOsKeyCode(key, out _), Is.False);
		});
	}

	[Test]
	public void A_scan_code_outside_the_main_block_names_no_key()
	{
		Assert.That(NativeKeys.TryFromWindowsScanCode(0x3B, out _), Is.False);
	}

	[Test]
	public void Only_punctuation_keys_are_positional()
	{
		var positional = Enum.GetValues<KeyCode>().Where(NativeKeys.IsPositional);

		Assert.That(positional,
			Is.EquivalentTo(new[]
			{
				KeyCode.Semicolon, KeyCode.Equal, KeyCode.Comma, KeyCode.Minus, KeyCode.Period, KeyCode.Slash,
				KeyCode.Backquote, KeyCode.BracketLeft, KeyCode.Backslash, KeyCode.BracketRight, KeyCode.Quote,
				KeyCode.IntlBackslash
			}));
	}
}
