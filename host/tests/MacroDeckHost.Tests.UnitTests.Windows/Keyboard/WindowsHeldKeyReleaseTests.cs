using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Keyboard.Native;
using Input = MacroDeckHost.Platform.Native.Win32Input.Input;

namespace MacroDeckHost.Tests.UnitTests.Windows.Keyboard;

[Platform("Win")]
[SupportedOSPlatform("windows")]
public class WindowsHeldKeyReleaseTests
{
	private const uint KlfNoTellShell = 0x00000080;
	private const uint KeyEventFKeyUp = 0x0002;
	private const uint WmKeyDown = 0x0100;
	private const uint WmKeyUp = 0x0101;

	private readonly List<IntPtr> _loadedByTest = [];

	private IntPtr _us;
	private IntPtr _german;
	private ushort _usScan;
	private ushort _germanScan;

	[SetUp]
	public void LoadLayoutsWhoseZDiffers()
	{
		_us = Load("00000409");
		_german = Load("00000407");
		Assert.That(WindowsKeyboardInputProvider.TryMapKey(KeyCode.Z, _us, out _, out _usScan), Is.True);
		Assert.That(WindowsKeyboardInputProvider.TryMapKey(KeyCode.Z, _german, out _, out _germanScan), Is.True);
		Assume.That(_germanScan, Is.Not.EqualTo(_usScan), "Z must sit on another key in the German layout");
	}

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
	public void A_key_released_after_a_layout_switch_releases_the_key_that_went_down()
	{
		var layout = _us;
		var sent = new List<Input>();
		var provider = new WindowsKeyboardInputProvider(() => layout, inputs => sent.AddRange(inputs));

		provider.KeyDown(KeyCode.Z);
		layout = _german;
		provider.KeyUp(KeyCode.Z);

		Assert.That(sent.Select(Describe),
			Is.EqualTo(new[] { (_usScan, false), (_usScan, true) }));
	}

	[Test]
	public void A_key_pressed_again_after_a_layout_switch_repeats_the_key_already_down()
	{
		var layout = _us;
		var sent = new List<Input>();
		var provider = new WindowsKeyboardInputProvider(() => layout, inputs => sent.AddRange(inputs));

		provider.KeyDown(KeyCode.Z);
		layout = _german;
		provider.KeyDown(KeyCode.Z);
		provider.KeyUp(KeyCode.Z);

		Assert.That(sent.Select(Describe),
			Is.EqualTo(new[] { (_usScan, false), (_usScan, false), (_usScan, true) }));
	}

	[Test]
	public void A_key_released_without_a_layout_switch_releases_the_same_key()
	{
		var sent = new List<Input>();
		var provider = new WindowsKeyboardInputProvider(() => _german, inputs => sent.AddRange(inputs));

		provider.KeyDown(KeyCode.Z);
		provider.KeyUp(KeyCode.Z);
		provider.KeyDown(KeyCode.Z);
		provider.KeyUp(KeyCode.Z);

		Assert.That(sent.Select(Describe),
			Is.EqualTo(new[]
			{
				(_germanScan, false),
				(_germanScan, true),
				(_germanScan, false),
				(_germanScan, true)
			}));
	}

	[Test]
	public void A_key_released_in_a_background_window_after_a_layout_switch_releases_the_key_that_went_down()
	{
		var switching = new IntPtr(0x1001);
		var steady = new IntPtr(0x1002);
		var layouts = new Dictionary<IntPtr, IntPtr> { [switching] = _us, [steady] = _us };
		var posted = new List<(IntPtr Window, uint Message, nuint Vk, ushort Scan)>();
		var target = new WindowsKeyboardInputProvider.WindowsTargetWindow([switching, steady],
			window => layouts[window],
			(window, message, wParam, lParam) => posted.Add((window, message, wParam, ScanOf(lParam))));

		target.KeyDown(KeyCode.Z);
		layouts[switching] = _german;
		target.KeyUp(KeyCode.Z);

		Assert.Multiple(() =>
		{
			foreach (var window in new[] { switching, steady })
			{
				var messages = posted.Where(entry => entry.Window == window).ToList();
				Assert.That(messages.Select(entry => entry.Message), Is.EqualTo(new[] { WmKeyDown, WmKeyUp }));
				Assert.That(messages.Select(entry => (entry.Vk, entry.Scan)).Distinct().Single(),
					Is.EqualTo(((nuint)0x5A, _usScan)),
					$"window {window}");
			}
		});
	}

	private static (ushort Scan, bool IsUp) Describe(Input input)
	{
		var keyboard = input.union.keyboard;
		return (keyboard.wScan, (keyboard.dwFlags & KeyEventFKeyUp) != 0);
	}

	private static ushort ScanOf(nint lParam)
	{
		return (ushort)((ulong)lParam >> 16 & 0xFF);
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
