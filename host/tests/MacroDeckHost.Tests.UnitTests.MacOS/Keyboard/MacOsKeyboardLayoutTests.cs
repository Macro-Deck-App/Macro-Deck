using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Keyboard.Native;
using MacroDeckHost.Integrations.Native;

namespace MacroDeckHost.Tests.UnitTests.MacOS.Keyboard;

[Platform("MacOsX")]
[SupportedOSPlatform("macos")]
public class MacOsKeyboardLayoutTests
{
	private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
	private const string CoreFoundation = MacOsAccessibility.CoreFoundation;

	[Test]
	public void On_a_german_layout_z_and_y_are_the_keys_labeled_z_and_y()
	{
		var letters = LettersOf("com.apple.keylayout.German");

		Assert.Multiple(() =>
		{
			Assert.That(letters[KeyCode.Z], Is.EqualTo(16));
			Assert.That(letters[KeyCode.Y], Is.EqualTo(6));
			Assert.That(letters[KeyCode.Q], Is.EqualTo(12));
		});
	}

	[Test]
	public void On_a_french_layout_a_q_z_w_and_m_are_the_keys_labeled_with_them()
	{
		var letters = LettersOf("com.apple.keylayout.French");

		Assert.Multiple(() =>
		{
			Assert.That(letters[KeyCode.A], Is.EqualTo(12));
			Assert.That(letters[KeyCode.Q], Is.EqualTo(0));
			Assert.That(letters[KeyCode.Z], Is.EqualTo(13));
			Assert.That(letters[KeyCode.W], Is.EqualTo(6));
			Assert.That(letters[KeyCode.M], Is.EqualTo(41));
		});
	}

	[Test]
	public async Task The_active_layout_resolves_every_letter_from_a_thread_pool_thread()
	{
		var layout = new MacOsKeyboardLayout();

		var unresolved = await Task.Run(() => Enumerable.Range(0, 26)
			.Select(offset => KeyCode.A + offset)
			.Where(key => !layout.TryGetLetterKeyCode(key, out _))
			.ToList());

		Assert.That(unresolved, Is.Empty);
	}

	private static Dictionary<KeyCode, ushort> LettersOf(string inputSourceId)
	{
		var sources = TISCreateInputSourceList(IntPtr.Zero, includeAllInstalled: true);
		Assert.That(sources, Is.Not.EqualTo(IntPtr.Zero));
		try
		{
			var idKey = MacOsAccessibility.ReadGlobalRef(Carbon, "kTISPropertyInputSourceID");
			for (nint index = 0; index < CFArrayGetCount(sources); index++)
			{
				var source = CFArrayGetValueAtIndex(sources, index);
				if (MacOsCoreFoundation.ReadCFString(TISGetInputSourceProperty(source, idKey)) == inputSourceId)
				{
					return MacOsKeyboardLayout.BuildLetterMap(source);
				}
			}
		}
		finally
		{
			MacOsCoreFoundation.CFRelease(sources);
		}

		Assert.Inconclusive($"{inputSourceId} is not installed");
		return [];
	}

	[DllImport(Carbon)]
	private static extern IntPtr TISCreateInputSourceList(IntPtr properties, [MarshalAs(UnmanagedType.I1)] bool includeAllInstalled);

	[DllImport(Carbon)]
	private static extern IntPtr TISGetInputSourceProperty(IntPtr inputSource, IntPtr propertyKey);

	[DllImport(CoreFoundation)]
	private static extern nint CFArrayGetCount(IntPtr array);

	[DllImport(CoreFoundation)]
	private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);
}
