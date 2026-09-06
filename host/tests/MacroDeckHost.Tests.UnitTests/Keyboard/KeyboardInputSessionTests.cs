using MacroDeckHost.Integrations.Keyboard;
using MacroDeckHost.Integrations.Keyboard.Native;

namespace MacroDeckHost.Tests.UnitTests.Keyboard;

public class KeyboardInputSessionTests
{
	private static readonly (KeyCode Key, bool Down)[] _ctrlC =
	[
		(KeyCode.LeftControl, true),
		(KeyCode.C, true),
		(KeyCode.C, false),
		(KeyCode.LeftControl, false)
	];

	private static readonly string[] _hiText = ["hi"];

	private FakeKeyboardInputProvider _provider = null!;
	private KeyboardInputService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_provider = new FakeKeyboardInputProvider();
		_service = new KeyboardInputService(_provider, new KeyboardLayoutService());
	}

	[Test]
	public async Task Unscoped_session_emits_to_the_focused_window()
	{
		using var session = (await _service.OpenSessionAsync(KeyboardTarget.None)).Session;

		Assert.That(session, Is.Not.Null);
		await session!.PressComboAsync(KeyModifier.Control, KeyCode.C);
		Assert.That(_provider.Events, Is.EqualTo(_ctrlC));
	}

	[Test]
	public async Task WhenFocused_without_platform_support_yields_no_session()
	{
		_provider.SupportsWindowTargeting = false;

		var result = await _service.OpenSessionAsync(new KeyboardTarget("code", KeyboardTargetMode.WhenFocused));

		Assert.Multiple(() =>
		{
			Assert.That(result.Session, Is.Null);
			Assert.That(result.UnavailableReason, Is.EqualTo(KeyboardSessionUnavailableReason.Unsupported));
		});
	}

	[Test]
	public async Task WhenFocused_emits_globally_only_when_the_target_is_focused()
	{
		_provider.SupportsWindowTargeting = true;
		_provider.ForegroundProcessName = "Code";

		using var session =
			(await _service.OpenSessionAsync(new KeyboardTarget("code", KeyboardTargetMode.WhenFocused))).Session;

		Assert.That(session, Is.Not.Null);
		await session!.PressComboAsync(KeyModifier.Control, KeyCode.C);
		Assert.That(_provider.Events, Is.EqualTo(_ctrlC));
	}

	[Test]
	public async Task WhenFocused_yields_no_session_when_another_app_is_focused()
	{
		_provider.SupportsWindowTargeting = true;
		_provider.ForegroundProcessName = "explorer";

		var result = await _service.OpenSessionAsync(new KeyboardTarget("code", KeyboardTargetMode.WhenFocused));

		Assert.Multiple(() =>
		{
			Assert.That(result.Session, Is.Null);
			Assert.That(result.UnavailableReason, Is.EqualTo(KeyboardSessionUnavailableReason.NotFocused));
		});
	}

	[Test]
	public async Task FocusThenSend_activates_emits_globally_and_restores_on_dispose()
	{
		_provider.SupportsWindowTargeting = true;
		var target = new FakeTargetWindow();
		_provider.Target = target;

		var session = (await _service.OpenSessionAsync(new KeyboardTarget("code", KeyboardTargetMode.FocusThenSend)))
			.Session;
		Assert.That(session, Is.Not.Null);
		await session!.PressComboAsync(KeyModifier.Control, KeyCode.C);

		Assert.Multiple(() =>
		{
			Assert.That(target.FocusCalls, Is.EqualTo(1));
			Assert.That(_provider.Events, Is.EqualTo(_ctrlC));
			Assert.That(target.Events, Is.Empty);
			Assert.That(target.RestoreCalls, Is.EqualTo(0));
		});

		session.Dispose();
		Assert.Multiple(() =>
		{
			Assert.That(target.RestoreCalls, Is.EqualTo(1));
			Assert.That(target.Disposed, Is.True);
		});
	}

	[Test]
	public async Task FocusThenSend_yields_no_session_when_the_target_cannot_be_resolved()
	{
		_provider.SupportsWindowTargeting = true;
		_provider.Target = null;

		var result = await _service.OpenSessionAsync(new KeyboardTarget("code", KeyboardTargetMode.FocusThenSend));

		Assert.Multiple(() =>
		{
			Assert.That(result.Session, Is.Null);
			Assert.That(result.UnavailableReason, Is.EqualTo(KeyboardSessionUnavailableReason.TargetNotFound));
		});
	}

	[Test]
	public async Task FocusThenSend_reports_a_focus_failure_rather_than_a_missing_target()
	{
		_provider.SupportsWindowTargeting = true;
		var target = new FakeTargetWindow { CanFocus = false };
		_provider.Target = target;

		var result = await _service.OpenSessionAsync(new KeyboardTarget("code", KeyboardTargetMode.FocusThenSend));

		Assert.Multiple(() =>
		{
			Assert.That(result.Session, Is.Null);
			Assert.That(result.UnavailableReason, Is.EqualTo(KeyboardSessionUnavailableReason.FocusFailed));
			Assert.That(target.FocusCalls, Is.EqualTo(1));
			Assert.That(target.Disposed, Is.True);
		});
	}

	[Test]
	public async Task Background_without_platform_support_yields_no_session()
	{
		_provider.SupportsBackgroundSend = false;
		_provider.Target = new FakeTargetWindow();

		var result = await _service.OpenSessionAsync(new KeyboardTarget("code", KeyboardTargetMode.Background));

		Assert.Multiple(() =>
		{
			Assert.That(result.Session, Is.Null);
			Assert.That(result.UnavailableReason, Is.EqualTo(KeyboardSessionUnavailableReason.Unsupported));
		});
	}

	[Test]
	public async Task Background_emits_into_the_target_without_focusing_it()
	{
		_provider.SupportsBackgroundSend = true;
		var target = new FakeTargetWindow();
		_provider.Target = target;

		using var session =
			(await _service.OpenSessionAsync(new KeyboardTarget("code", KeyboardTargetMode.Background))).Session;
		Assert.That(session, Is.Not.Null);
		await session!.PressComboAsync(KeyModifier.Control, KeyCode.C);

		Assert.Multiple(() =>
		{
			Assert.That(target.Events, Is.EqualTo(_ctrlC));
			Assert.That(_provider.Events, Is.Empty);
			Assert.That(target.FocusCalls, Is.EqualTo(0));
		});
	}

	[Test]
	public async Task Background_type_text_routes_to_the_target()
	{
		_provider.SupportsBackgroundSend = true;
		var target = new FakeTargetWindow();
		_provider.Target = target;

		using var session =
			(await _service.OpenSessionAsync(new KeyboardTarget("code", KeyboardTargetMode.Background))).Session;
		await session!.TypeTextAsync("hi");

		Assert.Multiple(() =>
		{
			Assert.That(target.TypedText, Is.EqualTo(_hiText));
			Assert.That(_provider.TypedText, Is.Empty);
		});
	}
}
