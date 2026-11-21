using System.Diagnostics;
using MacroDeckHost.Tests.InputProbe;
using MacroDeckHost.Tests.Support;

namespace MacroDeckHost.Tests.PlatformInput.Keyboard;

[Platform("Linux")]
[Category("X11")]
public sealed class X11KeyboardInputTests : KeyboardInputCases
{
	private const int RightAltKeycode = 108;

	private static readonly Dictionary<KeyCode, long[]> _keysyms = new()
	{
		[KeyCode.A] = [0x61, 0x41],
		[KeyCode.Enter] = [0xFF0D],
		[KeyCode.Space] = [0x20],
		[KeyCode.Tab] = [0xFF09, 0xFE20],
		[KeyCode.Escape] = [0xFF1B],
		[KeyCode.F5] = [0xFFC2],
		[KeyCode.ArrowLeft] = [0xFF51],
		[KeyCode.Home] = [0xFF50],
		[KeyCode.Delete] = [0xFFFF],
		[KeyCode.D7] = [0x37, 0x26],
		[KeyCode.LeftShift] = [0xFFE1],
		[KeyCode.RightControl] = [0xFFE4],
		[KeyCode.CapsLock] = [0xFFE5]
	};

	protected override bool IsKey(ProbeEvent keyEvent, KeyCode key)
	{
		return _keysyms.TryGetValue(key, out var keysyms) && keysyms.Contains(keyEvent.Keysym ?? -1);
	}

	internal override void Focus(ProbeProcess probe)
	{
		ProbeFocus.Focus(probe);
	}

	[Test]
	[Ignore("Red today: #1709 Type text inverts letter case while Caps Lock is on")]
	public async Task Caps_lock_does_not_change_the_case_of_typed_text()
	{
		await Keyboard.PressComboAsync(KeyModifier.None, KeyCode.CapsLock);
		try
		{
			First.Settle();
			First.Clear();
			await Keyboard.TypeTextAsync("aB");
			First.Settle();

			Assert.That(First.Text, Is.EqualTo("aB"), First.Diagnostics);
		}
		finally
		{
			await Keyboard.PressComboAsync(KeyModifier.None, KeyCode.CapsLock);
		}
	}

	[TestCase("us")]
	[TestCase("de")]
	public async Task Right_alt_arrives_as_the_physical_right_alt_key(string layout)
	{
		using var _ = Layout(First, layout);

		await Keyboard.PressComboAsync(KeyModifier.None, KeyCode.RightAlt);
		First.Settle();

		Assert.That(First.Keys.Where(e => e.Code == RightAltKeycode).Select(e => e.Down).ToArray(),
			Is.EqualTo(new bool?[] { true, false }),
			First.Diagnostics);
	}

	[Test]
	public async Task A_german_layout_types_its_own_and_its_altgr_characters()
	{
		using var _ = Layout(First, "de");

		await Keyboard.TypeTextAsync("zyäöüß@€{}");
		First.Settle();

		Assert.That(First.Text, Is.EqualTo("zyäöüß@€{}"), First.Diagnostics);
	}

	[Test]
	[Ignore("Red today: #1711 a key pressed right after a layout switch stays pressed")]
	public async Task A_letter_pressed_right_after_a_layout_switch_is_the_key_labelled_with_it()
	{
		using var _ = Layout(First, "de");

		await Keyboard.PressComboAsync(KeyModifier.None, KeyCode.Z);
		First.Settle();

		Assert.Multiple(() =>
		{
			Assert.That(First.Text, Is.EqualTo("z"), First.Diagnostics);
			Assert.That(InputState.HeldKeys(), Is.Empty);
		});
	}

	private static Restore Layout(ProbeProcess probe, string layout)
	{
		SetLayout(probe, layout);
		return new Restore(() => SetLayout(probe, "us"));
	}

	private static void SetLayout(ProbeProcess probe, string layout)
	{
		probe.Clear();
		using var process = Process.Start(new ProcessStartInfo("setxkbmap", layout) { UseShellExecute = false })
			?? throw new InvalidOperationException("setxkbmap did not start");
		process.WaitForExit();
		Assert.That(process.ExitCode, Is.Zero, $"setxkbmap {layout}");
		probe.WaitFor(e => e.Type == "mapping", TimeSpan.FromSeconds(5), "the new keyboard mapping");
		probe.Settle(500);
		probe.Clear();
	}

	private sealed class Restore(Action restore) : IDisposable
	{
		public void Dispose()
		{
			restore();
		}
	}
}
