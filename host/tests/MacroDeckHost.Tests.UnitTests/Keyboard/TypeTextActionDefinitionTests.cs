using MacroDeckHost.Integrations.Keyboard;
using MacroDeckHost.Integrations.Keyboard.Actions;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Keyboard;

public class TypeTextActionDefinitionTests
{
	private static Dictionary<string, object> TextParameters(string targetProcess = "", string targetMode = "")
	{
		var parameters = new Dictionary<string, object> { ["text"] = "hi" };
		if (targetProcess.Length > 0)
		{
			parameters["targetProcess"] = targetProcess;
			parameters["targetMode"] = targetMode;
		}

		return parameters;
	}

	[Test]
	public async Task Types_the_text_through_the_opened_session()
	{
		var input = new FakeKeyboardInputService();
		var action = new TypeTextActionDefinition(input);

		var result = await action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext { Parameters = TextParameters() });

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(input.Calls, Does.Contain("text:hi"));
		});
	}

	[Test]
	public async Task An_only_when_focused_target_that_is_not_focused_is_a_legitimate_no_op()
	{
		var input = new FakeKeyboardInputService
		{
			OpenSessionReturnsNull = true,
			UnavailableReason = KeyboardSessionUnavailableReason.NotFocused
		};
		var action = new TypeTextActionDefinition(input);

		var result = await action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext { Parameters = TextParameters("code", "focused") });

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(input.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task An_unresolvable_target_fails_with_not_found()
	{
		var input = new FakeKeyboardInputService
		{
			OpenSessionReturnsNull = true,
			UnavailableReason = KeyboardSessionUnavailableReason.TargetNotFound
		};
		var action = new TypeTextActionDefinition(input);

		var result = await action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext { Parameters = TextParameters("code", "focus-send") });

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
		});
	}

	[Test]
	public async Task A_target_that_cannot_be_focused_fails_with_permission_denied()
	{
		var input = new FakeKeyboardInputService
		{
			OpenSessionReturnsNull = true,
			UnavailableReason = KeyboardSessionUnavailableReason.FocusFailed
		};
		var action = new TypeTextActionDefinition(input);

		var result = await action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext { Parameters = TextParameters("code", "focus-send") });

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.PermissionDenied));
		});
	}

	[Test]
	public async Task An_unsupported_targeting_mode_fails_with_unavailable()
	{
		var input = new FakeKeyboardInputService
		{
			OpenSessionReturnsNull = true,
			UnavailableReason = KeyboardSessionUnavailableReason.Unsupported
		};
		var action = new TypeTextActionDefinition(input);

		var result = await action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext { Parameters = TextParameters("code", "background") });

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
		});
	}
}
