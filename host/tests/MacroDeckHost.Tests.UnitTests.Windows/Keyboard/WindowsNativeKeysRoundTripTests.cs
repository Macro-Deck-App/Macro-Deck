using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Keyboard.Native;

namespace MacroDeckHost.Tests.UnitTests.Windows.Keyboard;

[Platform("Win")]
[SupportedOSPlatform("windows")]
public class WindowsNativeKeysRoundTripTests
{
	private const uint KlfNoTellShell = 0x00000080;

	[Test]
	public void Every_key_the_host_presses_is_translated_back_to_itself_on_a_german_layout()
	{
		var german = LoadKeyboardLayout("00000407", KlfNoTellShell);
		try
		{
			Assert.Multiple(() =>
			{
				foreach (var key in Enum.GetValues<KeyCode>().Where(key => key != KeyCode.None))
				{
					if (!WindowsKeyboardInputProvider.TryMapKey(key, german, out var vk, out var scan) || vk == 0)
					{
						continue;
					}

					var extended = WindowsKeyboardInputProvider.IsExtendedKey(key);
					Assert.That(NativeKeys.FromWindows(vk, scan, extended), Is.EqualTo(key), key.ToString());
				}
			});
		}
		finally
		{
			_ = UnloadKeyboardLayout(german);
		}
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr LoadKeyboardLayout(string layoutId, uint flags);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool UnloadKeyboardLayout(IntPtr layout);
}
