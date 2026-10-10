using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Keyboard.Native;

namespace MacroDeckHost.Tests.UnitTests.MacOS.Keyboard;

[Platform("MacOsX")]
[SupportedOSPlatform("macos")]
public class MacOsNativeKeysRoundTripTests
{
	[Test]
	public void Every_key_the_host_presses_is_translated_back_to_itself_on_the_active_layout()
	{
		var layout = new MacOsKeyboardLayout();
		var letterCodes = Enumerable.Range(0, 26)
			.Select(offset => KeyCode.A + offset)
			.Select(letter => layout.TryGetLetterKeyCode(letter, out var code) ? (int)code : -1)
			.ToHashSet();

		Assert.Multiple(() =>
		{
			foreach (var key in Enum.GetValues<KeyCode>().Where(key => key != KeyCode.None))
			{
				if (!MacOsKeyboardInputProvider.TryGetKeyCode(layout, key, out var code))
				{
					continue;
				}

				var isLetter = key is >= KeyCode.A and <= KeyCode.Z;
				if (isLetter || !letterCodes.Contains(code))
				{
					Assert.That(NativeKeys.FromMacOS(code), Is.EqualTo(key), key.ToString());
				}
			}
		});
	}

	[Test]
	public void The_keys_the_host_cannot_press_on_a_mac_are_none()
		=> Assert.That(Enum.GetValues<KeyCode>().Where(key => key is >= KeyCode.F21 and <= KeyCode.F24)
			.Select(key => MacOsKeyboardInputProvider.TryGetKeyCode(new MacOsKeyboardLayout(), key, out _)), Has.None.True);
}
