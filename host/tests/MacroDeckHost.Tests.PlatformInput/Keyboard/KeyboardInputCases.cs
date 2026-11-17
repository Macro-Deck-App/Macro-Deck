using MacroDeckHost.Integrations.Keyboard.Models;
using MacroDeckHost.Integrations.Keyboard.Native;
using MacroDeckHost.Tests.InputProbe;
using MacroDeckHost.Tests.Support;

namespace MacroDeckHost.Tests.PlatformInput.Keyboard;

public abstract class KeyboardInputCases
{
	private IKeyboardInputProvider _provider = null!;

	protected KeyboardInputService Keyboard { get; private set; } = null!;

	internal ProbeProcess First { get; private set; } = null!;

	internal ProbeProcess Second { get; private set; } = null!;

	protected virtual string? LineBreaksRedToday => null;

	protected abstract bool IsKey(ProbeEvent keyEvent, KeyCode key);

	internal abstract void Focus(ProbeProcess probe);

	[OneTimeSetUp]
	public void StartProbes()
	{
		_provider = KeyboardInputProviderFactory.Create();
		Interactive.Require(_provider.IsSupported, $"{_provider.PlatformName} cannot inject keyboard input here.");
		Keyboard = new KeyboardInputService(_provider, new KeyboardLayoutService());
		First = ProbeProcess.StartWindow("mdprobe-a", 80, 80, 600, 400);
		Second = ProbeProcess.StartWindow("mdprobe-b", 720, 80, 600, 400);
	}

	[OneTimeTearDown]
	public void StopProbes()
	{
		First?.Dispose();
		Second?.Dispose();
		(_provider as IDisposable)?.Dispose();
	}

	[SetUp]
	public void FocusFirstProbe()
	{
		Focus(First);
		First.Settle();
		Second.Settle();
		First.Clear();
		Second.Clear();
	}

	[TearDown]
	public async Task NothingStaysPressed()
	{
		await Keyboard.ReleaseAllAsync();
		First.Settle();
		Assert.That(InputState.HeldKeys(), Is.Empty, "keys still held after the case");
	}

	[TestCase(KeyCode.A)]
	[TestCase(KeyCode.Enter)]
	[TestCase(KeyCode.Space)]
	[TestCase(KeyCode.Tab)]
	[TestCase(KeyCode.Escape)]
	[TestCase(KeyCode.F5)]
	[TestCase(KeyCode.ArrowLeft)]
	[TestCase(KeyCode.Home)]
	[TestCase(KeyCode.Delete)]
	[TestCase(KeyCode.D7)]
	public async Task A_single_key_arrives_as_one_press_and_one_release(KeyCode key)
	{
		await Keyboard.PressComboAsync(KeyModifier.None, key);
		First.Settle();

		var events = First.Keys.Where(e => IsKey(e, key)).Select(e => e.Down).ToArray();
		Assert.That(events, Is.EqualTo(new bool?[] { true, false }), First.Diagnostics);
	}

	[Test]
	public async Task A_combo_holds_its_modifiers_around_the_key_and_releases_them()
	{
		await Keyboard.PressComboAsync(KeyModifier.Control | KeyModifier.Shift, KeyCode.A);
		First.Settle();

		var keys = First.Keys;
		var press = keys.Single(e => IsKey(e, KeyCode.A) && e.Down == true);
		var release = keys.Single(e => IsKey(e, KeyCode.A) && e.Down == false);
		Assert.Multiple(() =>
		{
			Assert.That(press.HasModifier("ctrl") && press.HasModifier("shift"), Is.True, First.Diagnostics);
			Assert.That(keys[^1].Down, Is.False, "the combo must end with a release");
			Assert.That(IndexOf(keys, release), Is.LessThan(keys.Count - 1), "modifiers are released after the key");
		});
	}

	[Test]
	public async Task A_right_hand_modifier_arrives_as_the_right_hand_key()
	{
		await Keyboard.PressComboAsync(KeyModifier.RightControl, KeyCode.A);
		First.Settle();

		Assert.That(First.Keys.Count(e => IsKey(e, KeyCode.RightControl) && e.Down == true),
			Is.EqualTo(1),
			First.Diagnostics);
	}

	[Test]
	public async Task A_key_held_down_stays_down_until_it_is_released()
	{
		await Keyboard.KeyDownAsync(KeyModifier.None, KeyCode.LeftShift);
		First.Settle();
		var heldWhileDown = InputState.HeldKeys();
		await Keyboard.PressComboAsync(KeyModifier.None, KeyCode.A);
		First.Settle();
		var typedWhileHeld = First.Text;

		await Keyboard.KeyUpAsync(KeyModifier.None, KeyCode.LeftShift);
		First.Settle();

		Assert.Multiple(() =>
		{
			Assert.That(heldWhileDown, Is.Not.Empty, "the held key is down for the OS");
			Assert.That(typedWhileHeld, Is.EqualTo("A"), First.Diagnostics);
			Assert.That(First.Keys.Where(e => IsKey(e, KeyCode.LeftShift)).Select(e => e.Down).ToArray(),
				Is.EqualTo(new bool?[] { true, false }));
		});
	}

	[TestCase("Hello, World! 123")]
	[TestCase("äöüß €")]
	[TestCase("Ελληνικά")]
	[TestCase("😀🎉")]
	[TestCase("漢字かな한국어")]
	public async Task Typed_text_arrives_exactly(string text)
	{
		await Keyboard.TypeTextAsync(text);
		First.Settle();

		Assert.That(First.Text, Is.EqualTo(text), First.Diagnostics);
	}

	[TestCase("one\ntwo", 1)]
	[TestCase("one\r\ntwo", 1)]
	[TestCase("one\rtwo", 1)]
	[TestCase("a\n\nb", 2)]
	public async Task Each_line_break_in_typed_text_arrives_as_one_enter_press(string text, int lineBreaks)
	{
		if (LineBreaksRedToday is { } reason)
		{
			Assert.Ignore(reason);
		}

		await Keyboard.TypeTextAsync(text);
		First.Settle();

		Assert.Multiple(() =>
		{
			Assert.That(First.Keys.Count(e => IsKey(e, KeyCode.Enter) && e.Down == true),
				Is.EqualTo(lineBreaks),
				First.Diagnostics);
			Assert.That(First.Keys.Count(e => IsKey(e, KeyCode.Enter) && e.Down == false), Is.EqualTo(lineBreaks));
			Assert.That(First.Text, Is.EqualTo(text.Replace("\r", string.Empty).Replace("\n", string.Empty)));
		});
	}

	[Test]
	public async Task A_thousand_characters_typed_fast_all_arrive_in_order()
	{
		var random = new Random(1577);
		const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
		var text = new string(Enumerable.Range(0, 1000).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());

		await Keyboard.TypeTextAsync(text);
		First.Settle(1000, 60000);

		Assert.That(First.Text, Is.EqualTo(text));
	}

	[Test]
	public async Task Cancelling_a_repeated_press_leaves_every_key_released()
	{
		using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

		try
		{
			await Keyboard.PressComboAsync(KeyModifier.Shift, KeyCode.A, 200, 10, cancellation.Token);
		}
		catch (OperationCanceledException)
		{
		}

		First.Settle();
		var keys = First.Keys;
		Assert.Multiple(() =>
		{
			Assert.That(keys.Count(e => e.Down == true),
				Is.EqualTo(keys.Count(e => e.Down == false)),
				First.Diagnostics);
			Assert.That(InputState.HeldKeys(), Is.Empty);
		});
	}

	[Test]
	public async Task When_focused_sends_to_the_target_only_while_it_has_the_focus()
	{
		var focused = await Keyboard.OpenSessionAsync(new KeyboardTarget(First.Name, KeyboardTargetMode.WhenFocused));
		using (focused.Session)
		{
			await focused.Session!.TypeTextAsync("ab");
		}

		var unfocused = await Keyboard.OpenSessionAsync(new KeyboardTarget(Second.Name,
			KeyboardTargetMode.WhenFocused));
		First.Settle();
		Second.Settle();

		Assert.Multiple(() =>
		{
			Assert.That(First.Text, Is.EqualTo("ab"), First.Diagnostics);
			Assert.That(unfocused.UnavailableReason, Is.EqualTo(KeyboardSessionUnavailableReason.NotFocused));
			Assert.That(Second.Keys, Is.Empty, Second.Diagnostics);
		});
	}

	[Test]
	public async Task Focus_then_send_types_into_the_target_and_gives_the_focus_back()
	{
		var opened = await Keyboard.OpenSessionAsync(new KeyboardTarget(Second.Name,
			KeyboardTargetMode.FocusThenSend));
		Assert.That(opened.Session, Is.Not.Null, $"session unavailable: {opened.UnavailableReason}");
		using (opened.Session)
		{
			await opened.Session!.TypeTextAsync("xy");
			Second.Settle();
		}

		First.WaitFor(e => e.Type == "focus" && e.Down == true, TimeSpan.FromSeconds(5), "the focus to return");
		First.Settle();

		Assert.Multiple(() =>
		{
			Assert.That(Second.Text, Is.EqualTo("xy"), Second.Diagnostics);
			Assert.That(First.Text, Is.Empty, First.Diagnostics);
			Assert.That(_provider.GetForegroundProcessName(), Is.EqualTo(First.Name));
		});
	}

	[Test]
	public async Task Background_sends_to_the_target_without_taking_the_focus()
	{
		var opened = await Keyboard.OpenSessionAsync(new KeyboardTarget(Second.Name, KeyboardTargetMode.Background));
		Assert.That(opened.Session, Is.Not.Null, $"session unavailable: {opened.UnavailableReason}");
		using (opened.Session)
		{
			await opened.Session!.TypeTextAsync("bg");
		}

		First.Settle();
		Second.Settle();

		Assert.Multiple(() =>
		{
			Assert.That(Second.Text, Is.EqualTo("bg"), Second.Diagnostics);
			Assert.That(First.Keys, Is.Empty, First.Diagnostics);
			Assert.That(_provider.GetForegroundProcessName(), Is.EqualTo(First.Name));
		});
	}

	private static int IndexOf(IReadOnlyList<ProbeEvent> events, ProbeEvent probeEvent)
	{
		for (var i = 0; i < events.Count; i++)
		{
			if (ReferenceEquals(events[i], probeEvent))
			{
				return i;
			}
		}

		return -1;
	}
}
