using System.Runtime.Versioning;
using MacroDeckHost.Tests.InputProbe;
using MacroDeckHost.Tests.Support;

namespace MacroDeckHost.Tests.PlatformInput.Keyboard;

[Platform("Win")]
[Category("Windows")]
[SupportedOSPlatform("windows")]
public sealed class WindowsKeyboardInputTests : KeyboardInputCases
{
	private const string German = "00000407";
	private const string English = "00000409";
	private const int VkPacket = 0xE7;

	private static readonly Dictionary<KeyCode, (int VirtualKey, int? Scan, bool? Extended)> _keys = new()
	{
		[KeyCode.A] = (0x41, null, null),
		[KeyCode.Z] = (0x5A, null, null),
		[KeyCode.Enter] = (0x0D, null, false),
		[KeyCode.Space] = (0x20, null, null),
		[KeyCode.Tab] = (0x09, null, null),
		[KeyCode.Escape] = (0x1B, null, null),
		[KeyCode.F5] = (0x74, null, null),
		[KeyCode.ArrowLeft] = (0x25, null, true),
		[KeyCode.Home] = (0x24, null, true),
		[KeyCode.Delete] = (0x2E, null, true),
		[KeyCode.D7] = (0x37, null, null),
		[KeyCode.LeftShift] = (0x10, 0x2A, null),
		[KeyCode.RightControl] = (0x11, null, true)
	};

	protected override string LineBreaksRedToday => "Red today: #1713 typed line breaks do not arrive as the Enter key";

	protected override bool IsKey(ProbeEvent keyEvent, KeyCode key)
	{
		return _keys.TryGetValue(key, out var expected)
			&& keyEvent.Code == expected.VirtualKey
			&& (expected.Scan is null || keyEvent.Scan == expected.Scan)
			&& (expected.Extended is null || keyEvent.Extended == expected.Extended);
	}

	internal override void Focus(ProbeProcess probe)
	{
		ProbeFocus.Focus(probe);
	}

	[Test]
	public async Task A_german_layout_types_its_own_characters_and_keys()
	{
		SwitchLayout(German);
		try
		{
			await Keyboard.PressComboAsync(KeyModifier.None, KeyCode.Z);
			await Keyboard.TypeTextAsync("äöüß@€{}");
			First.Settle();

			Assert.That(First.Text, Is.EqualTo("zäöüß@€{}"), First.Diagnostics);
		}
		finally
		{
			SwitchLayout(English);
		}
	}

	[Test]
	public async Task A_layout_switch_between_key_down_and_key_up_releases_the_key_that_went_down()
	{
		await Keyboard.KeyDownAsync(KeyModifier.None, KeyCode.Z);
		SwitchLayout(German);
		try
		{
			await Keyboard.KeyUpAsync(KeyModifier.None, KeyCode.Z);
			First.Settle();

			var keys = First.Keys.Where(e => e.Code is not VkPacket).ToArray();
			var down = keys.First(e => e.Down == true);
			Assert.Multiple(() =>
			{
				Assert.That(keys.Count(e => e.Down == false && e.Scan == down.Scan), Is.EqualTo(1), First.Diagnostics);
				Assert.That(InputState.HeldKeys(), Is.Empty);
			});
		}
		finally
		{
			SwitchLayout(English);
		}
	}

	[Test]
	public void Input_blocked_by_an_elevated_window_is_logged_and_never_delivered()
	{
		using var sender = LowIntegrityProcess.StartProbe("mdprobe-low", "send", "--text", "uipi");

		var finished = sender.WaitFor(e => e.Type is "sent" or "error", TimeSpan.FromSeconds(30), "the sender");
		First.Settle();

		Assert.Multiple(() =>
		{
			Assert.That(finished.Type, Is.EqualTo("sent"), sender.Diagnostics);
			Assert.That(First.Text, Is.Empty, "a low-integrity sender must not reach the probe");
			Assert.That(sender.Events.Any(e => e.Type == "log"
					&& e.Level == "Warning"
					&& e.Message?.Contains("elevated", StringComparison.Ordinal) == true),
				Is.True,
				sender.Diagnostics);
		});
	}

	private void SwitchLayout(string layout)
	{
		First.Send($"layout {layout}");
		var switched = First.WaitFor(e => e.Type == "layout" && e.Message == layout,
			TimeSpan.FromSeconds(5),
			$"layout {layout}");
		Interactive.Require(switched.Layout is not null, $"Keyboard layout {layout} cannot be loaded.");
		First.Clear();
	}
}
