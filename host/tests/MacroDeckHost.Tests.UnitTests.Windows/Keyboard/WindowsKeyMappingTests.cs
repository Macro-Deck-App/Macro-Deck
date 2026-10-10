using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Keyboard.Native;

namespace MacroDeckHost.Tests.UnitTests.Windows.Keyboard;

[Platform("Win")]
[SupportedOSPlatform("windows")]
public class WindowsKeyMappingTests
{
	private const uint KlfNoTellShell = 0x00000080;

	private readonly List<IntPtr> _loadedByTest = [];

	[TearDown]
	public void UnloadLayoutsThisTestLoaded()
	{
		foreach (var layout in _loadedByTest)
		{
			_ = UnloadKeyboardLayout(layout);
		}

		_loadedByTest.Clear();
	}

	[Test]
	public void A_positional_key_presses_the_same_position_on_a_german_layout()
	{
		var german = Load("00000407");

		Assert.That(WindowsKeyboardInputProvider.TryMapKey(KeyCode.Minus, german, out var vk, out var scan), Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(scan, Is.EqualTo(0x0C));
			Assert.That(vk, Is.EqualTo(0xDB), "VK_OEM_4 is the key that types ß on German");
		});
	}

	[Test]
	public void A_letter_presses_the_key_labeled_with_it_on_a_german_layout()
	{
		var german = Load("00000407");

		Assert.That(WindowsKeyboardInputProvider.TryMapKey(KeyCode.Z, german, out var vk, out var scan), Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(vk, Is.EqualTo(0x5A));
			Assert.That(scan, Is.EqualTo(0x15), "German Z sits at the US Y position");
		});
	}

	[Test]
	public void The_iso_key_has_its_own_position()
	{
		var german = Load("00000407");

		Assert.That(WindowsKeyboardInputProvider.TryMapKey(KeyCode.IntlBackslash, german, out var vk, out var scan), Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(scan, Is.EqualTo(0x56));
			Assert.That(vk, Is.EqualTo(0xE2));
		});
	}

	private IntPtr Load(string layoutId)
	{
		var before = InstalledLayouts();
		var layout = LoadKeyboardLayout(layoutId, KlfNoTellShell);
		if (layout == IntPtr.Zero)
		{
			Assert.Inconclusive($"Keyboard layout {layoutId} cannot be loaded");
		}

		if (!before.Contains(layout))
		{
			_loadedByTest.Add(layout);
		}

		return layout;
	}

	private static HashSet<IntPtr> InstalledLayouts()
	{
		var layouts = new IntPtr[GetKeyboardLayoutList(0, null)];
		_ = GetKeyboardLayoutList(layouts.Length, layouts);
		return [.. layouts];
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr LoadKeyboardLayout(string pwszKlid, uint flags);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool UnloadKeyboardLayout(IntPtr hkl);

	[DllImport("user32.dll")]
	private static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[]? lpList);
}
