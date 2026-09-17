using MacroDeckHost.Integrations.Keyboard;
using MacroDeckHost.Integrations.Keyboard.Actions;
using MacroDeckHost.Integrations.Keyboard.Native;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Keyboard;

public class KeyDownActionDefinitionTests
{
	private static Dictionary<string, object> HoldParameters(string key, string targetProcess = "", string targetMode = "")
	{
		var parameters = new Dictionary<string, object>
		{
			["keys"] = new Dictionary<string, object?> { ["modifiers"] = new List<object?>(), ["key"] = key }
		};

		if (targetProcess.Length > 0)
		{
			parameters["targetProcess"] = targetProcess;
			parameters["targetMode"] = targetMode;
		}

		return parameters;
	}

	private static Task<ActionResult> RunAsync(FakeKeyboardInputService input, Dictionary<string, object> parameters)
		=> new KeyDownActionDefinition(input, new KeyboardLayoutService()).CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext { Parameters = parameters });

	[Test]
	public async Task Holds_the_key_in_the_configured_background_target()
	{
		var input = new FakeKeyboardInputService();

		var result = await RunAsync(input, HoldParameters("W", "code", "background"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(input.LastHoldTarget, Is.EqualTo(new KeyboardTarget("code", KeyboardTargetMode.Background)));
			Assert.That(input.Calls, Does.Contain("down:None+W"));
		});
	}

	[Test]
	public async Task A_hand_written_focus_then_send_mode_holds_only_when_focused()
	{
		var input = new FakeKeyboardInputService();

		await RunAsync(input, HoldParameters("W", "code", "focus-send"));

		Assert.That(input.LastHoldTarget, Is.EqualTo(new KeyboardTarget("code", KeyboardTargetMode.WhenFocused)));
	}

	[Test]
	public async Task An_only_when_focused_target_that_is_not_focused_is_a_legitimate_no_op()
	{
		var input = new FakeKeyboardInputService { HoldUnavailableReason = KeyboardSessionUnavailableReason.NotFocused };

		var result = await RunAsync(input, HoldParameters("W", "code", "focused"));

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
	}

	[Test]
	public async Task A_modifier_the_background_target_cannot_receive_fails_instead_of_reporting_success()
	{
		var input = new FakeKeyboardInputService
		{
			HoldUnavailableReason = KeyboardSessionUnavailableReason.BackgroundModifiersUnsupported
		};

		var result = await RunAsync(input, HoldParameters("Ctrl", "code", "background"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
		});
	}

	[Test]
	public async Task An_empty_key_succeeds_without_looking_for_the_target()
	{
		var input = new FakeKeyboardInputService { HoldUnavailableReason = KeyboardSessionUnavailableReason.TargetNotFound };

		var result = await RunAsync(input, HoldParameters("", "code", "background"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(input.LastHoldTarget, Is.EqualTo(default(KeyboardTarget)));
		});
	}
}
