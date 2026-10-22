using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Keyboard.Native;

namespace MacroDeckHost.Tests.UnitTests.Windows.Keyboard;

[Platform("Win")]
[SupportedOSPlatform("windows")]
public class WindowsKeyInputResolutionTests
{
	private const uint KlfNoTellShell = 0x00000080;
	private const uint KeyEventFExtendedKey = 0x0001;
	private const uint KeyEventFScanCode = 0x0008;
	private const uint MapVkVscToVkEx = 3;
	private const uint ExtendedScanPrefix = 0xE000;

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

	[TestCase("00000409")]
	[TestCase("00000407")]
	public void Every_key_the_host_presses_reaches_windows_as_that_key(string layoutId)
	{
		var layout = Load(layoutId);

		Assert.Multiple(() =>
		{
			foreach (var key in Enum.GetValues<KeyCode>().Where(key => key != KeyCode.None))
			{
				if (!WindowsKeyboardInputProvider.TryBuildKeyInput(key, layout, false, out var input))
				{
					continue;
				}

				var extended = (input.dwFlags & KeyEventFExtendedKey) != 0;
				var virtualKey = (input.dwFlags & KeyEventFScanCode) == 0
					? input.wVk
					: MapVirtualKeyEx(extended ? ExtendedScanPrefix | input.wScan : input.wScan,
						MapVkVscToVkEx,
						layout);

				Assert.That(NativeKeys.FromWindows((int)virtualKey, input.wScan, extended),
					Is.EqualTo(key),
					key.ToString());
			}
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

	[DllImport("user32.dll")]
	private static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr LoadKeyboardLayout(string pwszKlid, uint flags);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool UnloadKeyboardLayout(IntPtr hkl);

	[DllImport("user32.dll")]
	private static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[]? lpList);
}
